using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Stack<Guid> _internalLinkBackStack = [];
    private readonly Stack<Guid> _internalLinkForwardStack = [];
    private DocumentLinkOverlayViewModel? _selectedLinkArea;
    private int _unsupportedSourceLinkActions;

    public ObservableCollection<PdfInputIssue> InputPdfIssues { get; } = [];
    public int InputPdfIssueCount => InputPdfIssues.Count;
    public bool HasInputPdfIssues => InputPdfIssues.Count > 0;
    public string InputPdfIssueSummary => LocalizationService.IsEnglish
        ? InputPdfIssues.Count == 0 ? "No input PDF notices." : $"Input PDF notices: {InputPdfIssues.Count}"
        : InputPdfIssues.Count == 0 ? "入力PDFに注意事項はありません。" : $"入力PDFの注意事項: {InputPdfIssues.Count}件";
    public bool HasComments => _project?.Comments.Count > 0;
    public int CommentCount => _project?.Comments.Count ?? 0;
    public bool HasInternalLinkForSelection => FindSelectedInternalLink() is not null;
    public ObservableCollection<DocumentLinkOverlayViewModel> LinkAreaItems { get; } = [];
    public DocumentLinkOverlayViewModel? SelectedLinkArea
    {
        get => _selectedLinkArea;
        set
        {
            if (!Set(ref _selectedLinkArea, value)) return;
            OnPropertyChanged(nameof(HasSelectedLinkArea));
            OnPropertyChanged(nameof(CanEditLinkSelection));
            OnPropertyChanged(nameof(HasInternalLinkForSelection));
        }
    }
    public bool HasSelectedLinkArea => SelectedLinkArea is not null;
    public bool CanEditLinkSelection => HasSelectedOverlay || HasSelectedLinkArea;
    public bool CanNavigateInternalLinkBack => _internalLinkBackStack.Count > 0;
    public bool CanNavigateInternalLinkForward => _internalLinkForwardStack.Count > 0;

    private void InitializeProjectFeatures()
    {
        InputPdfIssues.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(InputPdfIssueCount));
            OnPropertyChanged(nameof(HasInputPdfIssues));
            OnPropertyChanged(nameof(InputPdfIssueSummary));
        };
    }

    private async Task InspectInputPdfAsync(string pdfPath)
    {
        InputPdfIssues.Clear();
        try
        {
            foreach (var issue in await InputPdfInspectionService.InspectAsync(pdfPath, CancellationToken.None))
                InputPdfIssues.Add(issue);
            if (_unsupportedSourceLinkActions > 0)
                InputPdfIssues.Add(new PdfInputIssue(
                    "links.unsupported_actions", PdfInputIssueSeverity.Critical,
                    "編集対象外のPDFリンクがあります",
                    $"{_unsupportedSourceLinkActions}件のリンクは内部ページ移動またはHTTP(S)ではありません。アプリ内では実行せず、出力後に安全性と動作を確認してください。",
                    "アクション"));
        }
        catch (Exception ex)
        {
            InputPdfIssues.Add(new PdfInputIssue(
                "inspection.failed",
                PdfInputIssueSeverity.Warning,
                "PDF特性をすべて確認できませんでした",
                ex.Message,
                "ファイル"));
            await _log.WriteAsync(LogLevel.Warning, "pdf.input.inspection.failed", ex.Message, ex);
        }
    }

    private void RefreshProjectFeatureState()
    {
        NotifyProjectStorageMode();
        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(CommentCount));
        OnPropertyChanged(nameof(HasInternalLinkForSelection));
        OnPropertyChanged(nameof(CanNavigateInternalLinkBack));
        OnPropertyChanged(nameof(CanNavigateInternalLinkForward));
        RefreshLinkAreaItems();
    }

    public ProjectTargetReference GetCurrentCommentTarget()
    {
        SynchronizeProjectPages();
        var page = _project?.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage?.PageNumber);
        if (SelectedOverlay is { IsDeleted: false } region && page is not null)
        {
            return new ProjectTargetReference
            {
                Kind = ProjectTargetKind.OcrRegion,
                PageId = page.Id,
                ObjectId = region.Id,
                CharacterStart = GetContiguousCharacterSelection(region)?.Start,
                CharacterLength = GetContiguousCharacterSelection(region)?.Length,
            };
        }
        return page is null
            ? new ProjectTargetReference { Kind = ProjectTargetKind.Document }
            : new ProjectTargetReference { Kind = ProjectTargetKind.Page, PageId = page.Id };
    }

    public IReadOnlyList<ProjectComment> GetComments() => _project?.Comments ?? [];
    public IReadOnlyList<ProjectTag> GetTags() => _project?.Tags ?? [];

    public void ApplyCommentsAndTags(IReadOnlyList<ProjectComment> comments, IReadOnlyList<ProjectTag> tags)
    {
        if (_project is null) return;
        var before = CaptureProjectAnnotationSnapshot();
        _project = _project with { Comments = comments.ToArray(), Tags = tags.ToArray() };
        RecordProjectAnnotationChange(before, "コメントとタグを変更");
    }

    public PdfInternalLink? GetSelectedInternalLink() => FindSelectedInternalLink();
    public bool HasInternalLinkFor(Guid sourceRegionId)
    {
        if (_project is null || SelectedPage is null) return false;
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        return _project.InternalLinks.Any(item => item.IsEnabled && item.SourcePageId == page?.Id && item.SourceRegionId == sourceRegionId);
    }

    public int GetInternalLinkDestinationPage(PdfInternalLink? link) =>
        link is null || !string.IsNullOrWhiteSpace(link.ExternalUri)
            ? Math.Min((SelectedPage?.PageNumber ?? 1) + 1, Math.Max(1, PageItems.Count)) :
        _project?.Pages.FirstOrDefault(page => page.Id == link.DestinationPageId)?.PageNumber ?? 1;

    public async Task<PdfInternalLink?> CreateInternalLinkAsync(
        int destinationPageNumber,
        double? zoomPercent,
        string description,
        string? externalUri = null)
    {
        if (_project is null || SelectedPage is null || SelectedOverlay is not { IsDeleted: false } region ||
            (string.IsNullOrWhiteSpace(externalUri) && (destinationPageNumber < 1 || destinationPageNumber > PageItems.Count))) return null;
        if (string.IsNullOrWhiteSpace(externalUri))
            await EnsurePageOverlaysLoadedForSearchAsync(destinationPageNumber);
        SynchronizeProjectPages();
        var sourcePage = _project.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage.PageNumber);
        var destinationPage = string.IsNullOrWhiteSpace(externalUri)
            ? _project.Pages.FirstOrDefault(page => page.PageNumber == destinationPageNumber)
            : null;
        if (sourcePage is null || (string.IsNullOrWhiteSpace(externalUri) && destinationPage is null)) return null;
        var before = CaptureProjectAnnotationSnapshot();
        var existing = FindSelectedInternalLink();
        var link = new PdfInternalLink
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            SourcePageId = sourcePage.Id,
            SourceRegionId = region.Id,
            DestinationPageId = destinationPage?.Id ?? Guid.Empty,
            ExternalUri = NormalizeExternalUri(externalUri),
            DestinationZoomPercent = zoomPercent,
            Description = description.Trim(),
            IsEnabled = true,
            SourceAnnotationReference = existing?.SourceAnnotationReference,
            OriginalSourceBounds = existing?.OriginalSourceBounds,
            IsModified = existing?.SourceAnnotationReference is not null,
        };
        _project = _project with
        {
            InternalLinks = _project.InternalLinks.Where(item => item.Id != link.Id).Append(link).ToArray(),
        };
        RecordProjectAnnotationChange(before, existing is null ? "ページリンクを追加" : "ページリンクを変更");
        return link;
    }

    /// <summary>プレビュー座標の任意矩形をリンク元として文書内またはWebリンクを作成します。</summary>
    public async Task<PdfInternalLink?> CreateLinkAreaAsync(
        Rect previewBounds,
        int destinationPageNumber,
        double? zoomPercent,
        string description,
        string? externalUri)
    {
        if (_project is null || SelectedPage is null ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics) ||
            previewBounds.Width < 4 || previewBounds.Height < 4 ||
            (string.IsNullOrWhiteSpace(externalUri) && (destinationPageNumber < 1 || destinationPageNumber > PageItems.Count))) return null;
        if (string.IsNullOrWhiteSpace(externalUri))
            await EnsurePageOverlaysLoadedForSearchAsync(destinationPageNumber);
        SynchronizeProjectPages();
        var sourcePage = _project.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage.PageNumber);
        var destinationPage = string.IsNullOrWhiteSpace(externalUri)
            ? _project.Pages.FirstOrDefault(page => page.PageNumber == destinationPageNumber)
            : null;
        if (sourcePage is null || (string.IsNullOrWhiteSpace(externalUri) && destinationPage is null)) return null;
        var before = CaptureProjectAnnotationSnapshot();
        var existing = SelectedLinkArea?.Link;
        var link = new PdfInternalLink
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            SourcePageId = sourcePage.Id,
            SourceBounds = ToPdfRectangle(NormalizePreviewBounds(previewBounds), metrics),
            DestinationPageId = destinationPage?.Id ?? Guid.Empty,
            ExternalUri = NormalizeExternalUri(externalUri),
            DestinationZoomPercent = zoomPercent,
            Description = description.Trim(),
            IsEnabled = true,
            SourceAnnotationReference = existing?.SourceAnnotationReference,
            OriginalSourceBounds = existing?.OriginalSourceBounds,
            IsModified = existing?.SourceAnnotationReference is not null,
        };
        _project = _project with
        {
            InternalLinks = _project.InternalLinks.Where(item => item.Id != link.Id).Append(link).ToArray(),
        };
        RecordProjectAnnotationChange(before, existing is null ? "リンク範囲を追加" : "リンク範囲を変更");
        RefreshLinkAreaItems(link.Id);
        return link;
    }

    public void DeleteSelectedInternalLink()
    {
        if (_project is null || FindSelectedInternalLink() is not { } link) return;
        var before = CaptureProjectAnnotationSnapshot();
        _project = _project with
        {
            InternalLinks = link.SourceAnnotationReference is null
                ? _project.InternalLinks.Where(item => item.Id != link.Id).ToArray()
                : _project.InternalLinks.Select(item => item.Id == link.Id
                    ? item with { IsEnabled = false, IsModified = true }
                    : item).ToArray(),
        };
        RecordProjectAnnotationChange(before, "ページリンクを削除");
        SelectedLinkArea = null;
        RefreshLinkAreaItems();
    }

    public async Task<bool> NavigateSelectedInternalLinkAsync()
    {
        var link = FindSelectedInternalLink();
        return link is not null && await FollowLinkAsync(link, true);
    }

    public async Task<bool> NavigateInternalLinkAsync(Guid? sourceRegionId, bool recordHistory = true)
    {
        if (_project is null || SelectedPage is null || sourceRegionId is null) return false;
        SynchronizeProjectPages();
        var sourcePage = _project.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage.PageNumber);
        var link = _project.InternalLinks.FirstOrDefault(item => item.IsEnabled && item.SourcePageId == sourcePage?.Id && item.SourceRegionId == sourceRegionId);
        return link is not null && await FollowLinkAsync(link, recordHistory);
    }

    private async Task<bool> FollowLinkAsync(PdfInternalLink link, bool recordHistory)
    {
        if (_project is null) return false;
        if (!string.IsNullOrWhiteSpace(link.ExternalUri))
        {
            if (!Uri.TryCreate(link.ExternalUri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            StatusMessage = "Webリンクを既定のブラウザーで開きました。";
            return true;
        }
        var sourcePage = _project.Pages.FirstOrDefault(page => page.Id == link.SourcePageId);
        if (recordHistory && sourcePage is not null)
        {
            _internalLinkBackStack.Push(sourcePage.Id);
            _internalLinkForwardStack.Clear();
        }
        await NavigateToPageIdAsync(link.DestinationPageId, link.DestinationZoomPercent);
        RefreshProjectFeatureState();
        return true;
    }

    public async Task NavigateInternalLinkBackAsync()
    {
        if (_project is null || _internalLinkBackStack.Count == 0) return;
        var current = _project.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage?.PageNumber);
        if (current is not null) _internalLinkForwardStack.Push(current.Id);
        await NavigateToPageIdAsync(_internalLinkBackStack.Pop(), null);
        RefreshProjectFeatureState();
    }

    public async Task NavigateInternalLinkForwardAsync()
    {
        if (_project is null || _internalLinkForwardStack.Count == 0) return;
        var current = _project.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage?.PageNumber);
        if (current is not null) _internalLinkBackStack.Push(current.Id);
        await NavigateToPageIdAsync(_internalLinkForwardStack.Pop(), null);
        RefreshProjectFeatureState();
    }

    private async Task NavigateToPageIdAsync(Guid pageId, double? zoomPercent)
    {
        if (_project?.Pages.FirstOrDefault(page => page.Id == pageId) is not { } destination) return;
        if (zoomPercent is > 0) ZoomPercent = zoomPercent.Value;
        if (destination.PageNumber >= 1 && destination.PageNumber <= PageItems.Count)
        {
            SelectedPage = PageItems[destination.PageNumber - 1];
            await RenderPageAsync(destination.PageNumber, populatePageList: false);
            StatusMessage = $"ページリンクで{destination.PageNumber}ページへ移動しました。";
        }
    }

    private PdfInternalLink? FindSelectedInternalLink()
    {
        if (_project is null || SelectedPage is null) return null;
        if (SelectedLinkArea is { } area) return _project.InternalLinks.FirstOrDefault(item => item.Id == area.Id && item.IsEnabled);
        if (SelectedOverlay is null) return null;
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        return _project.InternalLinks.FirstOrDefault(item => item.IsEnabled && item.SourcePageId == page?.Id && item.SourceRegionId == SelectedOverlay.Id);
    }

    private void RefreshLinkAreaItems(Guid? selectId = null)
    {
        var previousId = selectId ?? SelectedLinkArea?.Id;
        LinkAreaItems.Clear();
        if (_project is null || SelectedPage is null || !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics))
        {
            SelectedLinkArea = null;
            return;
        }
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        if (page is null) return;
        foreach (var link in _project.InternalLinks.Where(item => item.IsEnabled && item.SourcePageId == page.Id && item.SourceBounds is { IsValid: true }))
            LinkAreaItems.Add(new DocumentLinkOverlayViewModel(link, ToPreviewRectangle(link.SourceBounds!.Value, metrics)));
        SelectedLinkArea = LinkAreaItems.FirstOrDefault(item => item.Id == previousId);
    }

    private static string? NormalizeExternalUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("WebリンクはHTTPまたはHTTPSの完全なアドレスで指定してください。", nameof(value));
        return uri.AbsoluteUri;
    }

    private static (int Start, int Length)? GetContiguousCharacterSelection(OverlayRegionViewModel region)
    {
        var selected = region.SelectedCharacterIndices.Order().ToArray();
        if (selected.Length == 0 || selected[^1] - selected[0] + 1 != selected.Length) return null;
        return (selected[0], selected.Length);
    }

    private ProjectAnnotationSnapshot CaptureProjectAnnotationSnapshot() => new(
        _project?.Comments.ToArray() ?? [], _project?.Tags.ToArray() ?? [], _project?.InternalLinks.ToArray() ?? [],
        _project?.Redactions.ToArray() ?? []);

    private void ApplyProjectAnnotationSnapshot(ProjectAnnotationSnapshot snapshot)
    {
        if (_project is null) return;
        _project = _project with
        {
            Comments = snapshot.Comments.ToArray(),
            Tags = snapshot.Tags.ToArray(),
            InternalLinks = snapshot.InternalLinks.ToArray(),
            Redactions = snapshot.Redactions.ToArray(),
        };
        RefreshRedactionItems();
        RefreshProjectFeatureState();
    }

    private void RecordProjectAnnotationChange(ProjectAnnotationSnapshot before, string description)
    {
        var after = CaptureProjectAnnotationSnapshot();
        if (before == after ||
            (before.Comments.SequenceEqual(after.Comments) && before.Tags.SequenceEqual(after.Tags) &&
             before.InternalLinks.SequenceEqual(after.InternalLinks) && before.Redactions.SequenceEqual(after.Redactions))) return;
        RecordHistory(new ProjectAnnotationEdit(before, after, description));
        RefreshProjectFeatureState();
    }
}
