using System.Collections.ObjectModel;
using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isLinkOverlayVisible;
    private readonly Dictionary<int, PdfSourceLinkService.PageCoordinates> _sourceLinkPageCoordinates = [];
    private int _linkInputModeIndex;
    private IReadOnlyList<PdfSelectableTextCharacter>? _linkSelectionCharacters;
    internal void BeginLinkTextSelection() => _linkSelectionCharacters = GetLinkSelectableCharacters();
    internal void EndLinkTextSelection() => _linkSelectionCharacters = null;
    public ObservableCollection<DocumentLinkOverlayViewModel> LinkListItems { get; } = [];
    public bool IsLinkEditMode => EditorMode == EditorInteractionMode.LinkEditing;
    public bool IsLinkOverlayDisplayed => CanEditDocument && (IsLinkEditMode || IsLinkOverlayVisible);
    public bool IsPointerSurfaceVisible => IsOcrOverlayVisible || IsLinkEditMode;
    public bool CanHitLinkAreas => CanEditDocument && IsLinkEditMode && LinkInputModeIndex == 0;
    public bool IsLinkOverlayVisible
    {
        get => _isLinkOverlayVisible;
        set
        {
            if (!Set(ref _isLinkOverlayVisible, value)) return;
            OnPropertyChanged(nameof(IsLinkOverlayDisplayed));
            if (_project is not null && _project.LinkOverlayVisible != value)
            {
                _project = _project with { LinkOverlayVisible = value };
                MarkNonUndoableChange();
            }
        }
    }
    public int LinkInputModeIndex
    {
        get => _linkInputModeIndex;
        set
        {
            if (!Set(ref _linkInputModeIndex, Math.Clamp(value, 0, 2))) return;
            OnPropertyChanged(nameof(CanHitLinkAreas));
            OnPropertyChanged(nameof(LinkEditingHint));
        }
    }
    public string LinkEditingHint => LinkInputModeIndex switch
    {
        1 => "文字をドラッグして選択します。複数行は一つのリンクとして設定されます。",
        2 => "ページ上をドラッグしてリンク範囲を指定します。",
        _ => "リンク枠または一覧を選択して編集します。Ctrl＋クリックでリンク先を確認できます。",
    };
    internal event Action<PdfInternalLink>? LinkNavigationRequested;
    public long LinkOverlayVersion { get; private set; }
    public int CurrentPageLinkCount => LinkListItems.Count;
    public string SelectedLinkSummary => SelectedLinkArea is { } area
        ? $"{area.DestinationSummary}\n{area.Description}\n{(area.Link.SourceAnnotationReference is null ? "追加したリンク" : "元PDFのリンク")}"
        : "リンク枠または一覧から選択してください。";
    public bool CanTransformSelectedLink => CanHitLinkAreas && SelectedLinkArea is { IsEditable: true } area &&
        area.Link.SourceAreas.Count <= 1 && area.Link.SourceRegionId is null;

    public void ActivateLinkEditMode()
    {
        if (!CanUsePreview) return;
        IsAddOcrRegionMode = false;
        SelectedOverlay = null;
        EditorMode = EditorInteractionMode.LinkEditing;
        LinkInputModeIndex = 0;
    }

    internal static PdfCorrectoriumProject UpgradeImportedLinks(PdfCorrectoriumProject project, PdfSourceLinkService.ScanResult scan)
    {
        var sourceLinks = scan.Links.SelectMany(link => link.SourceAnnotations.Select(annotation =>
                (Key: (link.SourcePageId, annotation.Reference), Link: link)))
            .GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.First().Link);
        var pages = project.Pages.ToDictionary(page => page.Id);
        var sequence = project.PageSequence.ToDictionary(page => page.PageId);
        var retained = project.InternalLinks.Select(existing =>
        {
            if (existing.SourceAnnotationReference is { } reference &&
                sourceLinks.TryGetValue((existing.SourcePageId, reference), out var imported))
                return !existing.IsModified
                    ? imported with { Id = existing.Id, IsEnabled = existing.IsEnabled }
                    : existing with { SourceAnnotations = imported.SourceAnnotations };
            // Prior rectangle links used displayed-page coordinates. Migrate once to raw PDF coordinates.
            if (existing.SourceAnnotationReference is null && existing.SourceRegionId is null &&
                existing.SourceBounds is { } bounds && pages.TryGetValue(existing.SourcePageId, out var page) &&
                sequence.TryGetValue(existing.SourcePageId, out var source))
            {
                var coordinates = scan.PageCoordinates.GetValueOrDefault(source.SourcePageNumber) ?? new(0, new PdfPoint(0, 0));
                var rotation = ProjectPageSequence.NormalizeRotation(source.RotationDegrees + coordinates.Rotation);
                bounds = RotateLinkBounds(bounds, page.WidthPoints, page.HeightPoints, (360 - rotation) % 360);
                bounds = bounds with { Origin = new PdfPoint(bounds.Left + coordinates.Origin.X, bounds.Bottom + coordinates.Origin.Y) };
                return existing with { SourceBounds = bounds };
            }
            return existing;
        }).ToArray();
        var known = retained.SelectMany(link => link.SourceAnnotations.Select(annotation => (link.SourcePageId, annotation.Reference))
            .Concat(link.SourceAnnotationReference is { } reference ? [(link.SourcePageId, reference)] : [])).ToHashSet();
        var additions = scan.Links.Where(link => !link.SourceAnnotations.Any(annotation => known.Contains((link.SourcePageId, annotation.Reference))));
        return project with { InternalLinks = retained.Concat(additions).ToArray(), SourceLinksInitialized = true, SourceLinkImportVersion = 2 };
    }

    private PdfSourceLinkService.PageCoordinates LinkSourceCoordinates(int pageNumber)
    {
        var sourceNumber = _project?.PageSequence.ElementAtOrDefault(pageNumber - 1)?.SourcePageNumber ?? pageNumber;
        return _sourceLinkPageCoordinates.GetValueOrDefault(sourceNumber) ?? new(0, new PdfPoint(0, 0));
    }
    private int LinkRotation(int pageNumber) => ProjectPageSequence.NormalizeRotation(
        (_project?.PageSequence.ElementAtOrDefault(pageNumber - 1)?.RotationDegrees ?? 0) + LinkSourceCoordinates(pageNumber).Rotation);

    private PdfRectangle LinkFromPreview(Rect bounds, PageMetrics metrics, int pageNumber)
    {
        var displayed = ToPdfRectangle(bounds, metrics);
        var sourceBounds = RotateLinkBounds(displayed, metrics.WidthPoints, metrics.HeightPoints,
            (360 - LinkRotation(pageNumber)) % 360);
        var origin = LinkSourceCoordinates(pageNumber).Origin;
        return sourceBounds with { Origin = new PdfPoint(sourceBounds.Left + origin.X, sourceBounds.Bottom + origin.Y) };
    }

    private Rect LinkToPreview(PdfRectangle bounds, PageMetrics metrics, int pageNumber)
    {
        var origin = LinkSourceCoordinates(pageNumber).Origin;
        bounds = bounds with { Origin = new PdfPoint(bounds.Left - origin.X, bounds.Bottom - origin.Y) };
        var rotation = LinkRotation(pageNumber);
        var swapped = rotation is 90 or 270;
        var displayed = RotateLinkBounds(bounds, swapped ? metrics.HeightPoints : metrics.WidthPoints,
            swapped ? metrics.WidthPoints : metrics.HeightPoints, rotation);
        return ToPreviewRectangle(displayed, metrics);
    }

    internal static PdfRectangle RotateLinkBounds(PdfRectangle bounds, double width, double height, int rotation) =>
        rotation switch
        {
            90 => new(new PdfPoint(bounds.Bottom, width - bounds.Right), new PdfSize(bounds.Size.Height, bounds.Size.Width)),
            180 => new(new PdfPoint(width - bounds.Right, height - bounds.Top), bounds.Size),
            270 => new(new PdfPoint(height - bounds.Top, bounds.Left), new PdfSize(bounds.Size.Height, bounds.Size.Width)),
            _ => bounds,
        };

    internal IReadOnlyList<PdfSelectableTextCharacter> LinkCharactersForDiagnostics => GetLinkSelectableCharacters();
    private IReadOnlyList<PdfSelectableTextCharacter> GetLinkSelectableCharacters()
    {
        // Edited OCR cells replace the original invisible layer. Native visible text remains selectable.
        var result = _currentSelectableCharacters.Where(character => !character.IsInvisible).ToList();
        foreach (var region in OverlayItems.Where(region => !region.IsDeleted))
        {
            var radians = region.RotationDegrees * Math.PI / 180;
            var cos = Math.Cos(radians); var sin = Math.Sin(radians);
            foreach (var cell in region.CharacterCells)
            {
                var corners = new[]
                {
                    new Point(cell.Left, cell.Top), new Point(cell.Left + cell.Width, cell.Top),
                    new Point(cell.Left, cell.Top + cell.Height), new Point(cell.Left + cell.Width, cell.Top + cell.Height),
                }.Select(point =>
                {
                    var x = point.X - region.Width / 2; var y = point.Y - region.Height / 2;
                    return new Point(region.Left + region.Width / 2 + x * cos - y * sin,
                        region.Top + region.Height / 2 + x * sin + y * cos);
                }).ToArray();
                result.Add(new(cell.Text, corners.Min(p => p.X), corners.Min(p => p.Y),
                    corners.Max(p => p.X) - corners.Min(p => p.X), corners.Max(p => p.Y) - corners.Min(p => p.Y), true));
            }
        }
        return result;
    }

    internal IReadOnlyList<Rect> GetLinkTextSelectionPreview(Rect selection)
    {
        var characters = _linkSelectionCharacters ?? GetLinkSelectableCharacters();
        var hit = PdfTextSelectionService.Normalize(selection, characters, PreviewPixelWidth, PreviewPixelHeight);
        var selected = PdfTextSelectionService.Select(characters, hit, selection);
        return PdfTextSelectionService.BuildAreas(selected, selection.Height > selection.Width);
    }

    public async Task<PdfInternalLink?> CreateTextLinkAsync(Rect selection, int destinationPage, double? zoom,
        string description, string? uri)
    {
        var areas = GetLinkTextSelectionPreview(selection);
        if (areas.Count == 0)
        {
            StatusMessage = "選択できる文字がありません。画像内の文字は範囲指定でリンクを作成してください。";
            return null;
        }
        var union = areas.Aggregate(Rect.Empty, (bounds, area) => { bounds.Union(area); return bounds; });
        var link = await CreateLinkAreaAsync(union, destinationPage, zoom, description, uri, areas);
        return link;
    }

    internal void UpdateSelectedLinkGeometry(Rect previewBounds, bool commit)
    {
        if (_project is null || SelectedPage is null || !CanTransformSelectedLink ||
            SelectedLinkArea is not { } selected || !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics)) return;
        var before = CaptureProjectAnnotationSnapshot();
        var bounds = LinkFromPreview(NormalizePreviewBounds(previewBounds), metrics, SelectedPage.PageNumber);
        if (!bounds.IsValid) return;
        _project = _project with
        {
            InternalLinks = _project.InternalLinks.Select(link => link.Id == selected.Id
                ? link with { SourceBounds = bounds, SourceAreas = [], IsModified = link.SourceAnnotationReference is not null } : link).ToArray(),
        };
        if (commit) RecordProjectAnnotationChange(before, "リンク範囲を移動・サイズ変更");
        RefreshLinkAreaItems(selected.Id);
    }

    public IReadOnlyList<DocumentLinkOverlayViewModel> GetLinkOverlaysForPage(int pageNumber, double width, double height)
    {
        if (_project is null) return [];
        var page = _project.Pages.FirstOrDefault(page => page.PageNumber == pageNumber);
        var pageId = page?.Id ?? _project.PageSequence.ElementAtOrDefault(pageNumber - 1)?.PageId;
        if (pageId is null) return [];
        if (!_pageMetrics.TryGetValue(pageNumber, out var original))
            original = new PageMetrics((int)width, (int)height, page?.WidthPoints ?? 0, page?.HeightPoints ?? 0);
        if (original.WidthPoints <= 0 || original.HeightPoints <= 0) return [];
        var metrics = new PageMetrics((int)Math.Round(width), (int)Math.Round(height), original.WidthPoints, original.HeightPoints);
        var items = new List<DocumentLinkOverlayViewModel>();
        foreach (var link in _project.InternalLinks.Where(link => link.IsEnabled && link.SourcePageId == pageId))
        {
            var sourceBounds = link.SourceBounds ??
                page?.TextRegions.FirstOrDefault(region => region.Id == link.SourceRegionId)?.EditedGeometry.LocalBounds;
            var areas = link.SourceAreas.Count > 0 ? link.SourceAreas : sourceBounds is { IsValid: true } bounds ? [bounds] : [];
            var destination = link.UnsupportedAction is { } reason ? $"編集対象外：{reason}" :
                !string.IsNullOrWhiteSpace(link.ExternalUri) ? $"Web：{link.ExternalUri}" :
                _project.PageSequence.Select((page, index) => (page, index)).Where(pair => pair.page.PageId == link.DestinationPageId)
                    .Select(pair => $"文書内：{pair.index + 1}ページ").FirstOrDefault() ?? "移動先なし";
            if (link.SourceRegionId is { } regionId && _pageOverlays.TryGetValue(pageNumber, out var regions) &&
                regions.FirstOrDefault(region => region.Id == regionId) is { } region)
            {
                if (region.IsDeleted) continue;
                var regionBounds = ToPreviewRectangle(ToPdfRectangle(new Rect(region.Left, region.Top, region.Width, region.Height), original), metrics);
                items.Add(new DocumentLinkOverlayViewModel(link, regionBounds, destination, pageNumber));
            }
            else foreach (var area in areas)
                items.Add(new DocumentLinkOverlayViewModel(link, LinkToPreview(area, metrics, pageNumber), destination, pageNumber));
        }
        return items;
    }

    internal void SelectLinkById(Guid id) => SelectedLinkArea = LinkAreaItems.FirstOrDefault(item => item.Id == id);

    internal async Task SelectLinkPageAsync(int pageNumber, Guid linkId)
    {
        if (_project?.PageSequence.ElementAtOrDefault(pageNumber - 1) is not { } page) return;
        await NavigateToPageIdAsync(page.PageId, null);
        SelectLinkById(linkId);
    }

    internal Rect? GetLinkDestinationPreviewBounds(PdfInternalLink link)
    {
        var pageNumber = GetInternalLinkDestinationPage(link);
        if (!_pageMetrics.TryGetValue(pageNumber, out var metrics)) return null;
        if (link.DestinationViewType == "/FitR" && link.DestinationViewParameters.Count == 4 &&
            link.DestinationViewParameters.All(value => value.HasValue))
        {
            var values = link.DestinationViewParameters;
            var bounds = new PdfRectangle(new PdfPoint(values[0]!.Value, values[1]!.Value),
                new PdfSize(values[2]!.Value - values[0]!.Value, values[3]!.Value - values[1]!.Value));
            return bounds.IsValid ? LinkToPreview(bounds, metrics, pageNumber) : null;
        }
        var origin = LinkSourceCoordinates(pageNumber).Origin;
        var rotation = LinkRotation(pageNumber);
        var height = rotation is 90 or 270 ? metrics.WidthPoints : metrics.HeightPoints;
        var valuesToUse = link.DestinationViewParameters;
        double? x = link.DestinationPosition?.X;
        double? y = link.DestinationPosition?.Y;
        if (link.DestinationViewType == "/XYZ")
        {
            x = valuesToUse.ElementAtOrDefault(0); y = valuesToUse.ElementAtOrDefault(1);
        }
        else if (link.DestinationViewType is "/FitH" or "/FitBH") y = valuesToUse.ElementAtOrDefault(0);
        else if (link.DestinationViewType is "/FitV" or "/FitBV") x = valuesToUse.ElementAtOrDefault(0);
        if (x is null && y is null) return null;
        return LinkToPreview(new PdfRectangle(new PdfPoint(x ?? origin.X, y ?? origin.Y + height - .01),
            new PdfSize(.01, .01)), metrics, pageNumber);
    }

    private void SynchronizeRegionLinkBounds()
    {
        if (_project is null) return;
        var pages = _project.Pages.ToDictionary(page => page.Id);
        _project = _project with { InternalLinks = _project.InternalLinks.Select(link =>
        {
            if (link.SourceRegionId is not { } regionId || !pages.TryGetValue(link.SourcePageId, out var page) ||
                !_pageMetrics.TryGetValue(page.PageNumber, out var metrics)) return link;
            var region = page.TextRegions.FirstOrDefault(region => region.Id == regionId);
            if (region is null || region.IsDeleted) return link;
            return link with { SourceBounds = LinkFromPreview(ToPreviewRectangle(region.EditedGeometry.LocalBounds, metrics), metrics, page.PageNumber) };
        }).ToArray() };
    }

    private void NotifyLinkSelection()
    {
        OnPropertyChanged(nameof(SelectedLinkSummary));
        OnPropertyChanged(nameof(CanTransformSelectedLink));
    }
}
