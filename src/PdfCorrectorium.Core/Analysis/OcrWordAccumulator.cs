namespace PdfCorrectorium.Core.Analysis;

/// <summary>Worker-owned contributions: subtract the actually-counted state, never an uncounted intermediate edit.</summary>
public sealed class OcrWordAccumulator
{
    private readonly Dictionary<(int, Guid), (int Characters, Dictionary<string, int> Words)> _regions = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private long _characters;
    public void Update(int page, Guid id, string? text, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var key = (page, id);
        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        if (text is not null)
            foreach (var word in OcrSimilarWordAnalyzer.Tokenize(text, token)) words[word.Word] = words.GetValueOrDefault(word.Word) + 1;
        var old = _regions.GetValueOrDefault(key);
        var nextCharacters = _characters - old.Characters + (text?.Length ?? 0);
        if (nextCharacters > OcrSimilarWordAnalyzer.MaximumTextCharacters ||
            (!_regions.ContainsKey(key) && _regions.Count >= OcrSimilarWordAnalyzer.MaximumRegions))
            throw new InvalidOperationException("類似語分析の安全上限を超えました。文書を分割してください。");
        if (old.Words is not null) foreach (var pair in old.Words)
        { var remaining = _counts[pair.Key] - pair.Value; if (remaining == 0) _counts.Remove(pair.Key); else _counts[pair.Key] = remaining; }
        foreach (var pair in words) _counts[pair.Key] = _counts.GetValueOrDefault(pair.Key) + pair.Value;
        _characters = nextCharacters;
        if (text is null) _regions.Remove(key); else _regions[key] = (text.Length, words);
        if (_counts.Count > 25000 || _counts.Values.Sum(v => (long)v) > 750000)
            throw new InvalidOperationException("類似語分析の語数上限を超えました。部分結果は利用しません。");
    }
    public IReadOnlyList<OcrWordCount> Snapshot() => _counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new OcrWordCount(p.Key, p.Value)).ToArray();
}
