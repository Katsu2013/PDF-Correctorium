using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>Candidates belong to one document/overlay/text-input generation, independent of layout history.</summary>
public sealed record OcrSimilarWordDocumentResult(Guid ProjectId, int SessionVersion, long TextGeneration,
    OcrSimilarWordResult Analysis);

public sealed record OcrSimilarWordCorrectionResult(int ChangedCount, OcrSimilarWordDocumentResult Remaining);

public sealed partial class MainWindowViewModel
{
    // General edit generation also protects in-flight RapidOCR results; keep it strict.
    private long _similarWordHistoryGeneration;
    // Text analysis does not depend on position, size, reading order or review status.
    // This separate counter never rewinds, including a text edit followed by Undo.
    private long _similarWordTextGeneration;
    private OcrSimilarWordDocumentResult? _similarWordCachedResult;
    private string? _similarWordCachedOptions;
    internal OcrSimilarWordDocumentResult? CachedSimilarWordResult => _similarWordCachedResult is { } result && IsSimilarWordResultCurrent(result) ? result : null;

    private static bool ChangesSimilarWordInput(HistoryEdit? edit) => edit switch
    {
        OverlayEdit overlay => !ReferenceEquals(overlay.BeforeLexicon, overlay.AfterLexicon) || overlay.Changes.Any(change => change.Before.Text != change.After.Text ||
            change.Before.IsDeleted != change.After.IsDeleted || change.Before.IsGeometryLocked != change.After.IsGeometryLocked ||
            change.Before.CharacterLocksText != change.After.CharacterLocksText),
        ProjectAnnotationEdit => false,
        // Page structure and unknown/non-undoable changes remain conservative.
        _ => true,
    };
    public bool CanCorrectSimilarWords => CanEditDocument && !IsPdfExporting &&
        !IsBackgroundOperationVisible && (IsOcrEditMode || IsReviewMode);

    public bool IsSimilarWordResultCurrent(OcrSimilarWordDocumentResult result) =>
        _project?.ProjectId == result.ProjectId && _overlaySessionVersion == result.SessionVersion &&
        _similarWordTextGeneration == result.TextGeneration;

    /// <summary>No new OCR, raster analysis or network access. Reads the current edited OCR text.</summary>
    public async Task<OcrSimilarWordDocumentResult> AnalyzeOcrSimilarWordsAsync(OcrSimilarWordOptions options,
        IProgress<OperationProgressUpdate>? progress = null, CancellationToken cancellationToken = default, bool forceRebuild = false)
    {
        if (_project is null || _resolvedPdfPath is null) throw new InvalidOperationException("PDFを開いてください。");
        cancellationToken.ThrowIfCancellationRequested();
        var optionsKey = System.Text.Json.JsonSerializer.Serialize(options);
        if (!forceRebuild && optionsKey == _similarWordCachedOptions && CachedSimilarWordResult is { } cached) return cached;
        var projectId = _project.ProjectId;
        var session = _overlaySessionVersion;
        var textGeneration = _similarWordTextGeneration;
        void EnsureCurrent()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_project?.ProjectId != projectId || _overlaySessionVersion != session || _similarWordTextGeneration != textGeneration)
                throw new OperationCanceledException("文書が変更されました。再解析してください。");
        }
        var samples = new List<OcrWordSample>();
        long totalCharacters = 0;
        var pageCount = PageItems.Count;
        await using var worker = new PdfNativeWorkerClient(reuseInputs: true);
        for (var page = 1; page <= pageCount; page++)
        {
            EnsureCurrent();
            progress?.Report(new(page - 1, Math.Max(1, pageCount * 2), LocalizationService.IsEnglish
                ? $"Reading OCR text: page {page} / {pageCount}"
                : $"OCR文字列を読み込み中: {page} / {pageCount}ページ"));
            var regions = await EnsurePageOverlaysLoadedForSearchAsync(page, cancellationToken, worker);
            EnsureCurrent();
            foreach (var region in regions.Where(r => !r.IsDeleted && !string.IsNullOrWhiteSpace(r.Text)))
            {
                totalCharacters += region.Text.Length;
                if (samples.Count >= OcrSimilarWordAnalyzer.MaximumRegions || totalCharacters > OcrSimilarWordAnalyzer.MaximumTextCharacters)
                    throw new InvalidOperationException(LocalizationService.Translate("類似語分析の安全上限を超えました。文書を分割してください。"));
                samples.Add(new(page, region.Id, region.Text, region.IsGeometryLocked || region.HasLockedCharacters));
            }
            // Cached pages must not turn a whole-book scan into one uninterrupted dispatcher job.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }
        EnsureCurrent();
        IProgress<int> calculationProgress = new Progress<int>(value => progress?.Report(new(50 + value / 2, 100,
            LocalizationService.IsEnglish ? $"Comparing technical terms: {value}%" : $"類似語を比較中: {value}%")));
        var dictionary = CurrentOcrLexicon with
        {
            KnownTerms = CurrentOcrLexicon.KnownTerms.Concat(options.KnownTerms ?? []).Distinct(StringComparer.Ordinal).ToArray(),
            IgnoredTerms = CurrentOcrLexicon.IgnoredTerms.Concat(options.IgnoredTerms ?? []).Distinct(StringComparer.Ordinal).ToArray(),
        };
        var result = await Task.Run(() =>
        {
            var accumulator = new OcrWordAccumulator();
            foreach (var sample in samples) accumulator.Update(sample.PageNumber, sample.RegionId, sample.Text, cancellationToken);
            calculationProgress.Report(40);
            var index = new OcrLexiconIndex(dictionary, accumulator.Snapshot(), options, cancellationToken);
            var analysis = index.Analyze(samples, cancellationToken);
            calculationProgress.Report(100); return analysis;
        }, cancellationToken);
        EnsureCurrent();
        _similarWordCachedOptions = optionsKey;
        return _similarWordCachedResult = new(projectId, session, textGeneration, result);
    }

    /// <summary>Reviewed, non-overlapping exact snapshot matches only; one Undo for the whole batch.</summary>
    public int ApplySimilarWordCorrections(OcrSimilarWordDocumentResult result,
        IReadOnlyList<OcrSimilarWordCandidate> selected) => ApplySimilarWordCorrectionsAndRefresh(result, selected).ChangedCount;

    /// <summary>Rebases only unaffected, exact occurrences; frequency evidence remains from the original analysis.</summary>
    public OcrSimilarWordCorrectionResult ApplySimilarWordCorrectionsAndRefresh(OcrSimilarWordDocumentResult result,
        IReadOnlyList<OcrSimilarWordCandidate> selected, bool registerKnown = false, bool registerRules = false)
    {
        if (!CanCorrectSimilarWords || !IsSimilarWordResultCurrent(result)) return new(0, result);
        var allowed = result.Analysis.Candidates.ToHashSet();
        var targets = new Dictionary<OverlayRegionViewModel, OcrSimilarWordCandidate[]>();
        foreach (var group in selected.Where(allowed.Contains).Distinct().GroupBy(c => (c.PageNumber, c.RegionId)))
        {
            var region = _pageOverlays.GetValueOrDefault(group.Key.PageNumber)?
                .FirstOrDefault(r => r.Id == group.Key.RegionId && !r.IsDeleted);
            if (region is null || region.IsGeometryLocked || region.HasLockedCharacters) continue;
            var candidates = group.OrderByDescending(c => c.StartIndex).ToArray();
            var previousStart = region.Text.Length;
            var valid = true;
            foreach (var candidate in candidates)
            {
                if (region.Text != candidate.Sample.Text || candidate.StartIndex < 0 || candidate.Length <= 0 ||
                    candidate.StartIndex > region.Text.Length - candidate.Length ||
                    candidate.StartIndex + candidate.Length > previousStart ||
                    region.Text.Substring(candidate.StartIndex, candidate.Length) != candidate.Word)
                { valid = false; break; }
                previousStart = candidate.StartIndex;
            }
            if (valid) targets.Add(region, candidates);
        }
        if (targets.Count == 0) return new(0, result);
        var count = targets.Values.Sum(c => c.Length);
        ApplyRegionEdit("類似語の誤認識候補を修正", targets.Keys.ToArray(), () =>
        {
            RegisterOcrCorrections(targets.Values.SelectMany(c => c).Select(c => (c.Word, c.SuggestedWord)), registerKnown, registerRules);
            foreach (var (region, candidates) in targets)
            {
                // Apply separately, backwards: the existing OCR setter preserves unaffected
                // character advances, including the rn -> m many-to-one correction.
                foreach (var candidate in candidates)
                    region.Text = region.Text.Remove(candidate.StartIndex, candidate.Length)
                        .Insert(candidate.StartIndex, candidate.SuggestedWord);
                region.ReviewStatus = ReviewStatus.NeedsReview;
            }
        });
        StatusMessage = LocalizationService.IsEnglish ? $"Corrected {count} similar-word candidates. Undo restores the batch."
            : $"類似語の誤認識候補を{count}件修正しました。Undoで一括して戻せます。";
        var applied = targets.Values.SelectMany(candidates => candidates).ToHashSet();
        var changedRegions = targets.ToDictionary(pair => pair.Key.Id, pair =>
        {
            var edits = pair.Value.OrderBy(edit => edit.StartIndex).ToArray();
            var cumulative = 0;
            var shifts = edits.Select(edit => cumulative += edit.SuggestedWord.Length - edit.Length).ToArray();
            return (Region: pair.Key, Edits: edits, Shifts: shifts);
        });
        var remaining = new List<OcrSimilarWordCandidate>();
        foreach (var candidate in result.Analysis.Candidates)
        {
            if (applied.Contains(candidate)) continue;
            if (!changedRegions.TryGetValue(candidate.RegionId, out var changed)) { remaining.Add(candidate); continue; }
            // Sorted, non-overlapping edits: one binary search per remaining
            // occurrence, not a full scan of all corrections in a dense OCR line.
            var low = 0; var high = changed.Edits.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (changed.Edits[middle].StartIndex + changed.Edits[middle].Length <= candidate.StartIndex) low = middle + 1;
                else high = middle;
            }
            if (low < changed.Edits.Length && changed.Edits[low].StartIndex < candidate.StartIndex + candidate.Length) continue;
            var offset = candidate.StartIndex + (low > 0 ? changed.Shifts[low - 1] : 0);
            var text = changed.Region.Text;
            if (offset < 0 || offset > text.Length - candidate.Length || text.Substring(offset, candidate.Length) != candidate.Word) continue;
            remaining.Add(candidate with { StartIndex = offset, Sample = candidate.Sample with { Text = text,
                IsLocked = changed.Region.IsGeometryLocked || changed.Region.HasLockedCharacters } });
        }
        var refreshed = result with { TextGeneration = _similarWordTextGeneration, Analysis = result.Analysis with { Candidates = remaining } };
        _similarWordCachedResult = refreshed;
        return new(count, refreshed);
    }
}
