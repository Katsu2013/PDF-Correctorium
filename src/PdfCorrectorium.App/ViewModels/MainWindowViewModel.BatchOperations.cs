using System.Text.RegularExpressions;
using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly Regex TrailingPageNumber = new(@"(?<number>[0-9０-９]{1,6})\s*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public bool CanRepeatRedaction => CanEditDocument && SelectedRedaction is not null;
    public Guid? BatchProjectId => _project?.ProjectId;

    public async Task<IReadOnlyList<DocumentBatchCandidate>> BuildBatchCandidatesAsync(DocumentBatchKind kind,
        IReadOnlyList<int> pageNumbers, string searchText, int pageOffset, bool matchCase,
        IProgress<string>? progress, CancellationToken cancellationToken, bool useRegularExpression = false)
    {
        if (!CanEditDocument || _project is null || _resolvedPdfPath is null) return [];
        cancellationToken.ThrowIfCancellationRequested();
        SynchronizeProjectPages();
        var project = _project;
        var path = _resolvedPdfPath;
        var generation = _documentMetadataGeneration;
        var editState = _currentEditStateId;
        var ocrDocument = _ndlOcrDocument;
        var redactionColor = RedactionColorHex;
        void EnsureCurrentDocument()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_documentMetadataGeneration != generation || _project?.ProjectId != project.ProjectId ||
                _resolvedPdfPath != path || _currentEditStateId != editState)
                throw new OperationCanceledException("文書が変わりました。候補を作成し直してください。");
        }
        var originalRedaction = project.Redactions.FirstOrDefault(item => item.Id == SelectedRedaction?.Id);
        var originalPage = project.Pages.FirstOrDefault(page => page.Id == originalRedaction?.PageId);
        if (kind == DocumentBatchKind.RepeatedRedaction && (originalRedaction is null || originalPage is null))
            throw new InvalidOperationException("繰り返す墨消し範囲を先に選択してください。");
        if (kind == DocumentBatchKind.TextRedaction && string.IsNullOrWhiteSpace(searchText))
            throw new ArgumentException("検索する文字列を入力してください。");
        var pattern = kind == DocumentBatchKind.TextRedaction && useRegularExpression ? new DocumentRegexSearch(searchText, matchCase) : null;
        await using var batchWorker = new PdfNativeWorkerClient(reuseInputs: true);
        var originalRotation = 0;
        if (kind == DocumentBatchKind.RepeatedRedaction)
        {
            await EnsureLinkPageReadyAsync(originalPage!.PageNumber, cancellationToken);
            EnsureCurrentDocument();
            originalRotation = LinkRotation(originalPage.PageNumber);
        }
        var candidates = new List<DocumentBatchCandidate>();
        void AddCandidate(DocumentBatchCandidate candidate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidates.Count >= 10000)
                throw new InvalidOperationException("候補が10,000件を超えました。対象ページまたは検索文字を絞ってください。");
            candidates.Add(candidate);
        }
        foreach (var pageNumber in pageNumbers.Distinct().Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pageNumber < 1 || pageNumber > project.PageSequence.Count) throw new ArgumentOutOfRangeException(nameof(pageNumbers));
            progress?.Report($"{pageNumber} / {project.PageSequence.Count}ページを確認しています…");
            var page = project.PageSequence[pageNumber - 1];
            if (kind == DocumentBatchKind.RepeatedRedaction && originalRedaction!.PageId == page.PageId) continue;
            // Prioritize only the next required page; don't wait for offscreen pages/font inspection.
            await EnsureLinkPageReadyAsync(pageNumber, cancellationToken);
            EnsureCurrentDocument();
            var coordinates = CaptureLinkCoordinates(pageNumber);
            // The original document snapshot can predate background imports. Take the
            // ready page's links now, before entering the background geometry calculation.
            var existingLinks = _project!.InternalLinks.Where(link => link.IsEnabled && link.SourcePageId == page.PageId).ToArray();
            var preview = await Task.Run(() => RenderProjectPageAsync(path, project, pageNumber, 1200, cancellationToken,
                useBackgroundWorker: true, batchWorker: batchWorker), cancellationToken);
            EnsureCurrentDocument();
            var metrics = new PageMetrics(preview.Image.PixelWidth, preview.Image.PixelHeight, preview.PageWidthPoints, preview.PageHeightPoints);
            if (kind == DocumentBatchKind.RepeatedRedaction)
            {
                var compatible = Math.Abs(originalPage!.WidthPoints - metrics.WidthPoints) < .1 &&
                    Math.Abs(originalPage.HeightPoints - metrics.HeightPoints) < .1 && coordinates.Rotation == originalRotation;
                var copy = originalRedaction! with
                {
                    Id = Guid.NewGuid(),
                    PageId = page.PageId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    SourceRegionId = null,
                    SourceCharacterStart = null,
                    SourceCharacterLength = null
                };
                var bounds = ToPreviewRectangle(copy.Bounds, metrics);
                var duplicate = project.Redactions.Any(item => item.PageId == page.PageId && item.Bounds == copy.Bounds &&
                    item.ColorHex == copy.ColorHex && item.PathPoints.SequenceEqual(copy.PathPoints));
                AddCandidate(new()
                {
                    PageNumber = pageNumber,
                    Description = "選択範囲を同じ位置へ繰り返す",
                    CanInclude = compatible && !duplicate,
                    IsIncluded = compatible && !duplicate,
                    Status = !compatible ? "サイズ・回転が異なるため適用不可" : duplicate ? "同じ墨消しがあるため除外" : "確認して適用",
                    Redactions = [copy],
                    PreviewAreas = [bounds],
                    PreviewWidth = metrics.PixelWidth,
                    PreviewHeight = metrics.PixelHeight,
                    PreviewPath = copy.PathPoints.Select(point => new Point(point.X / metrics.WidthPoints * metrics.PixelWidth,
                        (metrics.HeightPoints - point.Y) / metrics.HeightPoints * metrics.PixelHeight)).ToArray()
                });
                continue;
            }
            // Geometry/text matching can be expensive on dense pages. It reads the
            // captured document snapshot and returns data, never WPF controls.
            await Task.Run(() =>
            {
                var extracted = GetCompanionRegions(ocrDocument, project, pageNumber, preview);
                var models = CreatePageOverlayModels(pageNumber, extracted.Count > 0 ? extracted : preview.TextRegions, metrics, project);
                var characters = CollectSelectableCharacters(preview.SelectableCharacters, models);
                var runs = DocumentTextRunService.Build(characters);
                if (kind == DocumentBatchKind.AutomaticLinks)
                {
                    foreach (var run in runs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var match = TrailingPageNumber.Match(run.Text);
                        if (!match.Success) continue;
                        var number = match.Groups["number"];
                        var digits = string.Concat(number.Value.Select(c => c is >= '０' and <= '９' ? (char)(c - '０' + '0') : c));
                        if (!int.TryParse(digits, out var printed)) continue;
                        var target = (long)printed + pageOffset;
                        var valid = target >= 1 && target <= project.PageSequence.Count;
                        var selected = run.Slice(number.Index, number.Length);
                        var areas = PdfTextSelectionService.BuildAreas(selected, false);
                        if (areas.Count == 0) continue;
                        var rawAreas = areas.Select(area => LinkFromPreview(area, metrics, coordinates)).ToArray();
                        var union = rawAreas.Aggregate(UnionPdfRectangles);
                        var overlap = existingLinks.Any(link =>
                            (link.SourceAreas.Count > 0 ? link.SourceAreas : link.SourceBounds is { } bounds ? [bounds] : [])
                            .Any(bounds => Overlaps(bounds, union)));
                        AddCandidate(new()
                        {
                            PageNumber = pageNumber,
                            Description = run.Text.Trim(),
                            DestinationPage = valid ? (int)target : null,
                            CanInclude = valid,
                            IsIncluded = valid && !overlap,
                            Status = !valid ? "移動先が文書外（適用不可）" : overlap ? "既存リンクと重複：既定で除外" : "移動先を確認して適用",
                            Link = valid ? new PdfInternalLink
                            {
                                SourcePageId = page.PageId,
                                SourceBounds = union,
                                SourceAreas = rawAreas,
                                DestinationPageId = project.PageSequence[(int)target - 1].PageId,
                                Description = run.Text.Trim()
                            } : null,
                            PreviewAreas = areas,
                            PreviewWidth = metrics.PixelWidth,
                            PreviewHeight = metrics.PixelHeight
                        });
                    }
                }
                else foreach (var hit in pattern is null ? DocumentTextRunService.Find(runs, searchText, matchCase) : DocumentTextRunService.Find(runs, pattern, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var selected = hit.Run.Slice(hit.Start, hit.Length);
                    var areas = BuildTextRedactionBands(selected, metrics.PixelWidth, metrics.PixelHeight);
                    var additions = areas.Select(area => new PdfRedaction
                    {
                        PageId = page.PageId,
                        Bounds = ToPdfRectangle(area, metrics),
                        ShapeKind = PdfRedactionShapeKind.TextSelection,
                        ColorHex = redactionColor
                    }).ToArray();
                    if (additions.Length == 0) continue;
                    var duplicate = additions.All(addition => project.Redactions.Any(existing => existing.PageId == addition.PageId &&
                        existing.Bounds == addition.Bounds && existing.ColorHex == addition.ColorHex));
                    AddCandidate(new()
                    {
                        PageNumber = pageNumber,
                        Description = hit.Run.Text.Trim(),
                        CanInclude = !duplicate,
                        IsIncluded = !duplicate,
                        Status = duplicate ? "同じ墨消しがあるため除外" : "一致した文字だけを墨消し",
                        Redactions = additions,
                        PreviewAreas = areas,
                        PreviewWidth = metrics.PixelWidth,
                        PreviewHeight = metrics.PixelHeight
                    });
                }
            }, cancellationToken);
            EnsureCurrentDocument();
            // Do not cache page bitmaps/models: working memory stays bounded to one page.
        }
        EnsureCurrentDocument();
        return candidates.DistinctBy(item => (item.PageNumber, item.Description, string.Join(";", item.PreviewAreas))).ToArray();
    }

    private static bool Overlaps(PdfRectangle first, PdfRectangle second) =>
        first.Left < second.Right && first.Right > second.Left && first.Bottom < second.Top && first.Top > second.Bottom;

    public void ApplyBatchCandidates(Guid projectId, IReadOnlyList<DocumentBatchCandidate> candidates)
    {
        if (!CanEditDocument || _project is null || _project.ProjectId != projectId)
            throw new InvalidOperationException("文書が変わりました。候補を作成し直してください。");
        var selected = candidates.Where(candidate => candidate.CanInclude && candidate.IsIncluded).ToArray();
        if (selected.Length == 0) return;
        var pageIds = _project.PageSequence.Select(page => page.PageId).ToHashSet();
        if (selected.Any(candidate => candidate.Link is { } link &&
            (!pageIds.Contains(link.SourcePageId) || !pageIds.Contains(link.DestinationPageId)) ||
            candidate.Redactions.Any(redaction => !pageIds.Contains(redaction.PageId) || !redaction.Bounds.IsValid)))
            throw new InvalidOperationException("候補のページや範囲が無効です。候補を作成し直してください。");
        var before = CaptureProjectAnnotationSnapshot();
        _project = _project with
        {
            InternalLinks = _project.InternalLinks.Concat(selected.Select(item => item.Link).OfType<PdfInternalLink>()).ToArray(),
            Redactions = _project.Redactions.Concat(selected.SelectMany(item => item.Redactions)).ToArray()
        };
        RecordProjectAnnotationChange(before, $"確認済み候補{selected.Length}件を一括適用");
        RefreshRedactionItems();
        StatusMessage = $"{selected.Length}件を一括適用しました。Undoでまとめて取り消せます。";
    }

    internal async Task<PdfPreviewResult?> RenderBatchPreviewAsync(int pageNumber, CancellationToken token)
    {
        if (_project is null || _resolvedPdfPath is null) return null;
        return await RenderProjectPageAsync(_resolvedPdfPath, _project, pageNumber, 1000, token, useBackgroundWorker: true);
    }
}
