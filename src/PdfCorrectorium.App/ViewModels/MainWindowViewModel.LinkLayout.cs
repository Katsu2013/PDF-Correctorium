using System.Windows;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // Selection order is significant: the first selected rectangle is the alignment reference.
    private readonly List<Guid> _selectedLinkIds = [];
    private bool _updatingLinkSelection;
    public int SelectedLinkCount => _selectedLinkIds.Count;
    public bool CanDeleteLinkSelection => CanEditLinkSelection && GetSelectedInternalLink() is { IsGeometryLocked: false } &&
        SelectedLinks().All(link => !link.IsGeometryLocked);
    public bool CanAlignLinks => CanHitLinkAreas && _selectedLinkIds.Count > 1 &&
        SelectedLinks().Count() == _selectedLinkIds.Count && SelectedLinks().All(IsLayoutLink);
    public bool CanLockLinks => CanHitLinkAreas && SelectedLinks().Any(link => link.UnsupportedAction is null);
    public bool AreSelectedLinksLocked => SelectedLinks().Any() && SelectedLinks().All(link => link.IsGeometryLocked);
    public string LinkSelectionSummary => $"選択: {SelectedLinkCount}件（最初の枠が整列基準）";

    private IEnumerable<PdfInternalLink> SelectedLinks() => _selectedLinkIds
        .Select(id => _project?.InternalLinks.FirstOrDefault(link => link.Id == id && link.IsEnabled))
        .OfType<PdfInternalLink>();
    private static bool IsLayoutLink(PdfInternalLink link) => !link.IsGeometryLocked &&
        link.UnsupportedAction is null && link.SourceRegionId is null && link.SourceAreas.Count <= 1 && link.SourceBounds is { IsValid: true };

    internal void SelectLink(DocumentLinkOverlayViewModel item, bool toggle)
    {
        if (!toggle) _selectedLinkIds.Clear();
        if (toggle && _selectedLinkIds.Contains(item.Id)) _selectedLinkIds.Remove(item.Id);
        else if (!_selectedLinkIds.Contains(item.Id)) _selectedLinkIds.Add(item.Id);
        _updatingLinkSelection = true;
        try { SelectedLinkArea = _selectedLinkIds.Contains(item.Id) ? item :
            LinkListItems.FirstOrDefault(link => link.Id == _selectedLinkIds.LastOrDefault()); }
        finally { _updatingLinkSelection = false; }
        RefreshLinkSelectionFlags();
    }

    private void RefreshLinkSelectionFlags()
    {
        foreach (var area in LinkAreaItems.Concat(LinkListItems)) area.IsSelected = _selectedLinkIds.Contains(area.Id);
        NotifyLinkSelection();
    }

    public void SetSelectedLinksLocked(bool locked)
    {
        if (_project is null || !CanLockLinks) return;
        var ids = SelectedLinks().Where(link => link.UnsupportedAction is null).Select(link => link.Id).ToHashSet();
        if (locked) SynchronizeRegionLinkBounds(ids);
        var before = CaptureProjectAnnotationSnapshot();
        _project = _project with { InternalLinks = _project.InternalLinks.Select(link => ids.Contains(link.Id)
            ? link with { IsGeometryLocked = locked } : link).ToArray() };
        RecordProjectAnnotationChange(before, locked ? "リンク枠をロック" : "リンク枠のロック解除");
        RefreshLinkAreaItems();
    }

    public void AlignSelectedLinks(string operation)
    {
        if (_project is null || SelectedPage is null || !CanAlignLinks ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics)) return;
        var links = SelectedLinks().ToArray();
        var areas = links.Select(link => LinkToPreview(link.SourceBounds!.Value, metrics, SelectedPage.PageNumber)).ToArray();
        var reference = areas[0];
        var updates = new Dictionary<Guid, PdfCorrectorium.Core.Geometry.PdfRectangle>();
        for (var i = 1; i < links.Length; i++)
        {
            var area = areas[i];
            var result = operation switch
            {
                "Left" => new Rect(reference.Left, area.Top, area.Width, area.Height),
                "Right" => new Rect(reference.Right - area.Width, area.Top, area.Width, area.Height),
                "Top" => new Rect(area.Left, reference.Top, area.Width, area.Height),
                "Bottom" => new Rect(area.Left, reference.Bottom - area.Height, area.Width, area.Height),
                "CenterX" => new Rect(reference.Left + (reference.Width - area.Width) / 2, area.Top, area.Width, area.Height),
                "CenterY" => new Rect(area.Left, reference.Top + (reference.Height - area.Height) / 2, area.Width, area.Height),
                "Width" => new Rect(area.Left, area.Top, reference.Width, area.Height),
                "Height" => new Rect(area.Left, area.Top, area.Width, reference.Height),
                "Size" => new Rect(area.TopLeft, reference.Size),
                _ => area,
            };
            if (result.Left < 0 || result.Top < 0 || result.Right > PreviewPixelWidth + .01 || result.Bottom > PreviewPixelHeight + .01)
            { StatusMessage = "ページ外にはみ出すため整列しませんでした。枠の位置や基準を変更してください。"; return; }
            updates[links[i].Id] = LinkFromPreview(result, metrics, SelectedPage.PageNumber);
        }
        var before = CaptureProjectAnnotationSnapshot();
        _project = _project with { InternalLinks = _project.InternalLinks.Select(link => updates.TryGetValue(link.Id, out var bounds)
            ? link with { SourceBounds = bounds, SourceAreas = [], IsModified = link.SourceAnnotationReference is not null } : link).ToArray() };
        RecordProjectAnnotationChange(before, "リンク枠の配置・サイズを調整");
        RefreshLinkAreaItems();
    }
}
