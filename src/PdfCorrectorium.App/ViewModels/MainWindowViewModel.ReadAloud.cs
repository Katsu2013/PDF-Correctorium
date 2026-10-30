using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.ViewModels;

public partial class MainWindowViewModel
{
    internal event EventHandler? ReadAloudInvalidated;
    private int _speechContentVersion;
    private Task _selectedPageRenderTask = Task.CompletedTask;
    private int? _displayedLogicalPageNumber;
    internal async Task DisplaySpeechPageAsync(int page, CancellationToken token)
    {
        await NavigateToPageAsync(page).WaitAsync(token);
        await _selectedPageRenderTask.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (SelectedPage?.PageNumber != page || _displayedLogicalPageNumber != page || !HasPreview || IsPreviewLoading)
            throw new InvalidOperationException("読み上げるページを表示できませんでした。");
    }
    internal void SetSpeechPreferences(string voice, double rate)
    {
        var settings = (_applicationSettings with { SpeechVoiceName = voice, FineSpeechRate = rate }).Normalize();
        if (settings.SpeechVoiceName == _applicationSettings.SpeechVoiceName && settings.EffectiveSpeechRate == _applicationSettings.EffectiveSpeechRate) return;
        _applicationSettings = settings;
        QueueDisplaySettingsSave();
        OnPropertyChanged(nameof(CurrentApplicationSettings));
    }
    internal sealed record SpeechTicket(Guid ProjectId, int Generation, int ContentVersion, string Path, int FirstPage, int LastPage,
        IReadOnlyDictionary<Guid, IReadOnlyList<int>>? Selection);
    internal sealed record SpeechPage(int PageNumber, double WidthPoints, double HeightPoints, IReadOnlyList<SpeechTextSegment> Segments);

    internal SpeechTicket CreateSpeechTicket(ReadAloudScope scope)
    {
        if (_project is null || _resolvedPdfPath is null || SelectedPage is null || !HasDocument)
            throw new InvalidOperationException("PDFを開いてください。");
        IReadOnlyDictionary<Guid, IReadOnlyList<int>>? selection = null;
        if (scope == ReadAloudScope.Selection)
        {
            selection = GetSelectedCharacterLines().Where(region => !region.IsDeleted).ToDictionary(region => region.Id,
                region => IsCharacterEditMode ? region.SelectedCharacterIndices : (IReadOnlyList<int>)Array.Empty<int>());
            if (selection.Count == 0) throw new InvalidOperationException("読み上げる文字・行・段落を選択してください。");
        }
        return new(_project.ProjectId, _documentMetadataGeneration, _speechContentVersion, _resolvedPdfPath,
            SelectedPage.PageNumber, scope == ReadAloudScope.RemainingPages ? PageItems.Count : SelectedPage.PageNumber, selection);
    }
    internal bool IsSpeechTicketCurrent(SpeechTicket ticket) => HasDocument && _project?.ProjectId == ticket.ProjectId &&
        _documentMetadataGeneration == ticket.Generation && _speechContentVersion == ticket.ContentVersion && _resolvedPdfPath == ticket.Path;

    private void InvalidateReadAloud()
    {
        _speechContentVersion++;
        ReadAloudInvalidated?.Invoke(this, EventArgs.Empty);
    }
    private void InvalidateReadAloudForEdit(HistoryEdit? edit)
    {
        // Geometry/locks/review only: marker follows the live ID without rebuilding or stopping text.
        if (edit is OverlayEdit overlay && overlay.Changes.All(change => change.Before.Text == change.After.Text &&
            change.Before.WordReadingsText == change.After.WordReadingsText && change.Before.IsDeleted == change.After.IsDeleted &&
            change.Before.ReadingOrder == change.After.ReadingOrder && change.Before.ParagraphGroupId == change.After.ParagraphGroupId) && _project?.Redactions.Count is not > 0) return;
        InvalidateReadAloud();
    }

    internal async Task<SpeechPage> ReadSpeechPageAsync(SpeechTicket ticket, int pageNumber, PdfNativeWorkerClient worker, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsSpeechTicketCurrent(ticket)) throw new OperationCanceledException(token);
        var project = _project!;
        var reference = project.PageSequence.ElementAtOrDefault(pageNumber - 1);
        var saved = project.Pages.FirstOrDefault(page => page.PageNumber == pageNumber);
        var pageId = reference?.PageId ?? saved?.Id ?? Guid.Empty;
        OcrPage page;
        if (_pageOverlays.TryGetValue(pageNumber, out var loaded) && _pageMetrics.TryGetValue(pageNumber, out var metrics))
        {
            var originals = saved?.TextRegions.ToDictionary(region => region.Id) ?? [];
            page = new OcrPage
            {
                Id = pageId, PageNumber = pageNumber, WidthPoints = metrics.WidthPoints, HeightPoints = metrics.HeightPoints,
                TextRegions = loaded.Select(region =>
                {
                    var geometry = ToPdfGeometry(region.Capture(), metrics, region.IsVertical);
                    var original = originals.GetValueOrDefault(region.Id) ?? new OcrTextRegion
                    { OriginalGeometry = ToPdfGeometry(region.Original, metrics, region.Original.IsVertical ?? region.IsVertical), EditedGeometry = geometry };
                    return original with { Id = region.Id, PageId = pageId, EditedText = region.Text, HasEditedText = true,
                        EditedGeometry = geometry, ParagraphGroupId = region.ParagraphGroupId,
                        WritingMode = region.IsVertical ? WritingMode.Vertical : WritingMode.Horizontal,
                        IsDeleted = region.IsDeleted, WordReadings = ParseWordReadings(region.WordReadingsText) };
                }).ToArray(),
                // Deleted persisted rows are retained for editing/history, but
                // have no position in the active reading order. Their fallback
                // ordinals can collide with live rows after loading a page and
                // create false paragraph boundaries. Match paragraph editing
                // and project saving; excluded live rows still retain their
                // position so SpeechText keeps its protective boundaries.
                ReadingOrder = loaded.Where(region => !region.IsDeleted).OrderBy(region => region.ReadingOrder).Select(region => region.Id).ToArray(),
            };
        }
        else if (saved is { TextRegions.Count: > 0 }) page = saved;
        else
        {
            var physical = await Task.Run(() => worker.ReadSpeechPageAsync(ticket.Path, reference?.SourcePageNumber ?? pageNumber, token), token);
            token.ThrowIfCancellationRequested();
            if (!IsSpeechTicketCurrent(ticket)) throw new OperationCanceledException(token);
            var rotation = reference?.RotationDegrees ?? 0;
            var swap = rotation is 90 or 270;
            var regions = _ndlOcrDocument?.GetScaledRegions(reference?.SourcePageNumber ?? pageNumber, physical.PixelWidth, physical.PixelHeight);
            if (regions is not { Count: > 0 }) regions = physical.Regions;
            regions = PdfPreviewTransform.RotateRegions(regions, physical.PixelWidth, physical.PixelHeight, rotation);
            var pageMetrics = new PageMetrics(swap ? physical.PixelHeight : physical.PixelWidth, swap ? physical.PixelWidth : physical.PixelHeight,
                swap ? physical.HeightPoints : physical.WidthPoints, swap ? physical.WidthPoints : physical.HeightPoints);
            var textRegions = regions.Select((region, index) =>
            {
                var geometry = ToPdfGeometry(new(region.Text, region.Left, region.Top, region.Width, region.Height, region.RotationDegrees), pageMetrics, region.IsVertical);
                return new OcrTextRegion { Id = Guid.NewGuid(), PageId = pageId, OriginalText = region.Text, OriginalGeometry = geometry,
                    EditedGeometry = geometry, WritingMode = region.IsVertical ? WritingMode.Vertical : WritingMode.Horizontal };
            }).ToArray();
            page = new OcrPage { Id = pageId, PageNumber = pageNumber, WidthPoints = pageMetrics.WidthPoints, HeightPoints = pageMetrics.HeightPoints,
                TextRegions = textRegions, ReadingOrder = textRegions.Select(region => region.Id).ToArray() };
        }
        var redactions = project.Redactions.Where(redaction => redaction.PageId == pageId).ToArray();
        // A deleted-all saved page or loaded empty edit must never fall back to the original PDF text.
        return new(pageNumber, page.WidthPoints, page.HeightPoints, SpeechText.Build(page, redactions, ticket.Selection, project.WordReadings));
    }
}
