using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public OcrLexicon CurrentOcrLexicon => _project?.OcrLexicon ?? new();
    public bool CanManageOcrLexicon => _project is not null && CanEditDocument && !IsOpeningDocument && !IsPdfExporting && !IsBackgroundOperationVisible;
    internal long SimilarWordGeneration => _similarWordTextGeneration;
    private CancellationTokenSource? _lexiconCancellation;
    private Task? _lexiconPreparation;
    private OcrWordAccumulator? _lexiconAccumulator;
    private readonly HashSet<int> _lexiconPendingPages = [];
    private readonly Dictionary<int, Guid[]> _lexiconPageIds = [];
    private IReadOnlyList<OcrWordCount> _lexiconCounts = [];
    private OcrLexiconIndex? _lexiconIndex;
    private bool _lexiconNeedsFull, _lexiconComplete;
    private long _lexiconGeneration;
    private string _lexiconPreparationStatus = "";
    public string LexiconPreparationStatus => _lexiconPreparationStatus;
    public bool IsOcrLexiconReady => _lexiconComplete && _lexiconIndex is not null;
    public IReadOnlyList<OcrWordCount> OcrWordFrequencies => _lexiconComplete ? _lexiconCounts : [];
    private void SetLexiconPreparationStatus(string value)
    { _lexiconPreparationStatus = value; OnPropertyChanged(nameof(LexiconPreparationStatus)); OnPropertyChanged(nameof(IsOcrLexiconReady)); }

    public bool SetOcrLexicon(OcrLexicon lexicon)
    {
        if (_project is null || !CanManageOcrLexicon) return false;
        OcrLexiconPolicy.Validate(lexicon);
        if (System.Text.Json.JsonSerializer.Serialize(lexicon) == System.Text.Json.JsonSerializer.Serialize(CurrentOcrLexicon)) return true;
        ApplyRegionEdit("資料のOCR辞書を変更", [], () => _project = _project with { OcrLexicon = lexicon });
        return true;
    }

    internal void RegisterOcrCorrections(IEnumerable<(string Source, string Target)> corrections, bool known, bool rules)
    {
        if (_project is null || (!known && !rules)) return;
        var next = CurrentOcrLexicon;
        var terms = next.KnownTerms.ToHashSet(StringComparer.Ordinal); var pairs = next.Rules.ToList();
        foreach (var pair in corrections.Distinct())
        {
            if (known && OcrLexiconPolicy.IsValidTerm(pair.Target)) terms.Add(pair.Target);
            if (rules && pair.Source != pair.Target && OcrLexiconPolicy.IsValidTerm(pair.Source) && OcrLexiconPolicy.IsValidTerm(pair.Target) &&
                !pairs.Any(r => r.Source == pair.Source && r.Target == pair.Target)) pairs.Add(new() { Source = pair.Source, Target = pair.Target });
        }
        next = next with { KnownTerms = terms.Order(StringComparer.Ordinal).ToArray(), Rules = pairs.ToArray() };
        OcrLexiconPolicy.Validate(next); _project = _project with { OcrLexicon = next };
    }

    private void CancelLexiconPreparation()
    {
        _similarWordCachedResult = null; _similarWordCachedOptions = null;
        _lexiconCancellation?.Cancel(); _lexiconCancellation = null;
        _lexiconGeneration++; _lexiconPreparation = null; _lexiconAccumulator = null;
        _lexiconPendingPages.Clear(); _lexiconPageIds.Clear(); _lexiconCounts = []; _lexiconIndex = null;
        _lexiconNeedsFull = false; _lexiconComplete = false; SetLexiconPreparationStatus("");
    }

    private void InvalidateLexiconForEdit(HistoryEdit? edit)
    {
        if (_project is null) return;
        if (edit is OverlayEdit overlay)
        {
            var changed = overlay.Changes.Where(c => c.Before.Text != c.After.Text || c.Before.IsDeleted != c.After.IsDeleted).ToArray();
            var dictionaryChanged = !ReferenceEquals(overlay.BeforeLexicon, overlay.AfterLexicon);
            if (changed.Length == 0 && !dictionaryChanged) return;
            _lexiconGeneration++; _lexiconIndex = null; _lexiconComplete = false;
            foreach (var page in _pageOverlays.Where(p => changed.Any(c => p.Value.Contains(c.Region)))) _lexiconPendingPages.Add(page.Key);
            _project = _project with { OcrWordCountsCache = null };
            OnPropertyChanged(nameof(CurrentOcrLexicon)); QueueLexiconPreparation();
        }
        else if (edit is not ProjectAnnotationEdit)
        {
            // A page/non-undoable change must not briefly republish an old completed cache while recounting.
            _project = _project with { OcrWordCountsCache = null };
            QueueLexiconPreparation(full: true);
        }
    }

    private void QueueLexiconPreparation(bool full = false)
    {
        if (_project is null || _resolvedPdfPath is null || PageItems.Count == 0) return;
        if (full) { _lexiconGeneration++; _lexiconNeedsFull = true; _lexiconComplete = false; _lexiconIndex = null; }
        if (_lexiconPreparation is { IsCompleted: false }) return;
        var cancellation = new CancellationTokenSource(); _lexiconCancellation = cancellation;
        _lexiconPreparation = PrepareLexiconInIdleAsync(cancellation);
    }

    private async Task PrepareLexiconInIdleAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token; var session = _overlaySessionVersion;
        try
        {
            // Delay allows opening/rendering/input to win. This job never enters the editing-blocking operation state.
            await Task.Delay(700, token);
            await using var worker = new PdfNativeWorkerClient(reuseInputs: true);
            while (_project is not null && session == _overlaySessionVersion)
            {
                token.ThrowIfCancellationRequested();
                if (_lexiconNeedsFull || _lexiconAccumulator is null)
                {
                    _lexiconNeedsFull = false; _lexiconAccumulator = new(); _lexiconPageIds.Clear();
                    _lexiconPendingPages.UnionWith(Enumerable.Range(1, PageItems.Count));
                    if (_project.OcrWordCountsCache is { } cache && (_ndlOcrDocument is null ||
                        Enumerable.Range(1, PageItems.Count).All(page => _project.Pages.Any(p => p.PageNumber == page && p.TextRegions.Count > 0))))
                    {
                        var cachedVersion = _lexiconGeneration; var cachedDictionary = CurrentOcrLexicon;
                        var cachedIndex = await Task.Run(() => new OcrLexiconIndex(cachedDictionary, cache.Words, token: token), token);
                        if (session != _overlaySessionVersion) return;
                        if (cachedVersion == _lexiconGeneration) { _lexiconCounts = cache.Words; _lexiconIndex = cachedIndex; _lexiconComplete = true; }
                    }
                }
                while (_lexiconPendingPages.Count > 0)
                {
                    await WaitForLexiconIdleAsync(token);
                    if (_lexiconNeedsFull) break;
                    var page = _lexiconPendingPages.Min(); var generation = _lexiconGeneration;
                    var accumulator = _lexiconAccumulator;
                    var project = _project; if (project is null) return;
                    (Guid Id, string? Text)[] samples;
                    if (_pageOverlays.TryGetValue(page, out var loaded)) samples = loaded.Select(r => (r.Id, r.IsDeleted ? null : r.Text)).ToArray();
                    else if (project.Pages.FirstOrDefault(p => p.PageNumber == page) is { TextRegions.Count: > 0 } saved)
                        samples = saved.TextRegions.Select(r => (r.Id, r.IsDeleted ? null : r.EffectiveText)).ToArray();
                    else
                    {
                        var sourcePage = project.PageSequence.ElementAtOrDefault(page - 1)?.SourcePageNumber ?? page;
                        var texts = _ndlOcrDocument?.Pages.GetValueOrDefault(sourcePage)?.Lines.Select(l => l.Text).ToArray() ??
                            await Task.Run(() => worker.ReadOcrWordsAsync(_resolvedPdfPath!, sourcePage, token), token);
                        samples = texts.Select((text, index) => (SyntheticWordRegionId(index), (string?)text)).ToArray();
                    }
                    token.ThrowIfCancellationRequested();
                    if (session != _overlaySessionVersion) return;
                    // If any edit arrived during extraction, retain the pending page and take a fresh snapshot.
                    if (generation != _lexiconGeneration || _lexiconNeedsFull) continue;
                    var oldIds = _lexiconPageIds.GetValueOrDefault(page) ?? [];
                    await Task.Run(() =>
                    {
                        foreach (var id in oldIds) accumulator.Update(page, id, null, token);
                        foreach (var sample in samples) accumulator.Update(page, sample.Id, sample.Text, token);
                    }, token);
                    if (session != _overlaySessionVersion) return;
                    _lexiconPageIds[page] = samples.Where(s => s.Text is not null).Select(s => s.Id).ToArray();
                    if (generation == _lexiconGeneration) _lexiconPendingPages.Remove(page);
                    SetLexiconPreparationStatus(LocalizationService.IsEnglish ? $"OCR vocabulary: {PageItems.Count - _lexiconPendingPages.Count}/{PageItems.Count} pages"
                        : $"OCR語彙を準備中: {PageItems.Count - _lexiconPendingPages.Count}/{PageItems.Count}ページ");
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                }
                if (_lexiconNeedsFull) continue;
                await WaitForLexiconIdleAsync(token);
                var dictionary = CurrentOcrLexicon; var version = _lexiconGeneration;
                var counts = await Task.Run(() => _lexiconAccumulator!.Snapshot(), token);
                var index = await Task.Run(() => new OcrLexiconIndex(dictionary, counts, token: token), token);
                if (session != _overlaySessionVersion) return;
                if (version != _lexiconGeneration || _lexiconPendingPages.Count > 0) continue;
                _lexiconCounts = counts; _lexiconIndex = index; _lexiconComplete = true;
                SetLexiconPreparationStatus(LocalizationService.IsEnglish ? $"OCR vocabulary ready: {counts.Count:N0} terms" : $"OCR語彙の準備完了: {counts.Count:N0}語");
                // Capture only existing models. Cache hashing happens off the dispatcher and never makes the project dirty.
                SynchronizeProjectPages(); var snapshot = _project!;
                var stamp = await Task.Run(() => OcrLexiconPolicy.GetCacheStamp(snapshot), token);
                if (session == _overlaySessionVersion && version == _lexiconGeneration && _project is not null)
                    _project = _project with { OcrWordCountsCache = new() { Stamp = stamp, Words = counts } };
                return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (session != _overlaySessionVersion) return;
            _lexiconAccumulator = null; _lexiconIndex = null; _lexiconComplete = false; _lexiconCounts = [];
            SetLexiconPreparationStatus(LocalizationService.IsEnglish ? "OCR vocabulary unavailable; registered rules remain usable."
                : "OCR語彙を準備できませんでした。登録済みルールは利用できます。");
            await _log.WriteAsync(LogLevel.Warning, "ocr.lexicon.prepare", error.Message, error);
        }
        finally
        {
            if (ReferenceEquals(_lexiconCancellation, cancellation)) { _lexiconCancellation = null; _lexiconPreparation = null; }
            cancellation.Dispose();
        }
    }

    private async Task WaitForLexiconIdleAsync(CancellationToken token)
    {
        while (IsOpeningDocument || IsBackgroundOperationVisible || IsPdfExporting || IsRapidOcrRunning ||
            DateTimeOffset.UtcNow - _lastUserActivityAtUtc < TimeSpan.FromMilliseconds(600)) await Task.Delay(250, token);
        token.ThrowIfCancellationRequested();
    }
    private static Guid SyntheticWordRegionId(int index) { var bytes = new byte[16]; BitConverter.GetBytes(index + 1).CopyTo(bytes, 0); return new(bytes); }

    internal async Task<OcrLexiconQuery> QueryOcrLexiconAsync(string raw, CancellationToken token)
    {
        var dictionary = CurrentOcrLexicon; var index = _lexiconIndex;
        // Do not wait for document counts. Explicit knowledge works immediately, partial counts are never evidence.
        return await Task.Run(() => (index ?? new OcrLexiconIndex(dictionary, [], token: token)).Query(raw, token: token), token);
    }

    internal void RestartLexiconForDiagnostics() => QueueLexiconPreparation(full: true);
}
