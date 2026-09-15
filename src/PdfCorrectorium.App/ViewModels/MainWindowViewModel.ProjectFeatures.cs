using System.Collections.ObjectModel;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Stack<Guid> _internalLinkBackStack = [];
    private readonly Stack<Guid> _internalLinkForwardStack = [];

    public ObservableCollection<PdfInputIssue> InputPdfIssues { get; } = [];
    public int InputPdfIssueCount => InputPdfIssues.Count;
    public bool HasInputPdfIssues => InputPdfIssues.Count > 0;
    public string InputPdfIssueSummary => LocalizationService.IsEnglish
        ? InputPdfIssues.Count == 0 ? "No input PDF notices." : $"Input PDF notices: {InputPdfIssues.Count}"
        : InputPdfIssues.Count == 0 ? "入力PDFに注意事項はありません。" : $"入力PDFの注意事項: {InputPdfIssues.Count}件";
    public bool HasComments => _project?.Comments.Count > 0;
    public int CommentCount => _project?.Comments.Count ?? 0;
    public bool HasInternalLinkForSelection => FindSelectedInternalLink() is not null;
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
        link is null ? Math.Min((SelectedPage?.PageNumber ?? 1) + 1, Math.Max(1, PageItems.Count)) :
        _project?.Pages.FirstOrDefault(page => page.Id == link.DestinationPageId)?.PageNumber ?? 1;

    public async Task<PdfInternalLink?> CreateInternalLinkAsync(int destinationPageNumber, double? zoomPercent, string description)
    {
        if (_project is null || SelectedPage is null || SelectedOverlay is not { IsDeleted: false } region ||
            destinationPageNumber < 1 || destinationPageNumber > PageItems.Count) return null;
        await EnsurePageOverlaysLoadedForSearchAsync(destinationPageNumber);
        SynchronizeProjectPages();
        var sourcePage = _project.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage.PageNumber);
        var destinationPage = _project.Pages.FirstOrDefault(page => page.PageNumber == destinationPageNumber);
        if (sourcePage is null || destinationPage is null) return null;
        var before = CaptureProjectAnnotationSnapshot();
        var existing = FindSelectedInternalLink();
        var link = new PdfInternalLink
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            SourcePageId = sourcePage.Id,
            SourceRegionId = region.Id,
            DestinationPageId = destinationPage.Id,
            DestinationZoomPercent = zoomPercent,
            Description = description.Trim(),
            IsEnabled = true,
        };
        _project = _project with
        {
            InternalLinks = _project.InternalLinks.Where(item => item.Id != link.Id).Append(link).ToArray(),
        };
        RecordProjectAnnotationChange(before, existing is null ? "ページリンクを追加" : "ページリンクを変更");
        return link;
    }

    public void DeleteSelectedInternalLink()
    {
        if (_project is null || FindSelectedInternalLink() is not { } link) return;
        var before = CaptureProjectAnnotationSnapshot();
        _project = _project with { InternalLinks = _project.InternalLinks.Where(item => item.Id != link.Id).ToArray() };
        RecordProjectAnnotationChange(before, "ページリンクを削除");
    }

    public async Task<bool> NavigateSelectedInternalLinkAsync() =>
        await NavigateInternalLinkAsync(SelectedOverlay?.Id, true);

    public async Task<bool> NavigateInternalLinkAsync(Guid? sourceRegionId, bool recordHistory = true)
    {
        if (_project is null || SelectedPage is null || sourceRegionId is null) return false;
        SynchronizeProjectPages();
        var sourcePage = _project.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage.PageNumber);
        var link = _project.InternalLinks.FirstOrDefault(item => item.IsEnabled && item.SourcePageId == sourcePage?.Id && item.SourceRegionId == sourceRegionId);
        if (link is null) return false;
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
        if (_project is null || SelectedPage is null || SelectedOverlay is null) return null;
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        return _project.InternalLinks.FirstOrDefault(item => item.SourcePageId == page?.Id && item.SourceRegionId == SelectedOverlay.Id);
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
