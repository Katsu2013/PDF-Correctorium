using System.Text;

namespace PdfCorrectorium.Core.Analysis;

/// <summary>A text-only snapshot; offsets always address the unmodified UTF-16 source.</summary>
public sealed record OcrWordSample(int PageNumber, Guid RegionId, string Text, bool IsLocked = false);

/// <summary>Book-local evidence, never an instruction to replace text automatically.</summary>
public sealed record OcrSimilarWordOptions(
    int MinimumReferenceCount = 3,
    int MinimumFrequencyRatio = 5,
    int MaximumSuspectCount = 3,
    IReadOnlyList<string>? KnownTerms = null,
    IReadOnlyList<string>? IgnoredTerms = null);

public sealed record OcrSimilarWordCandidate(
    OcrWordSample Sample, int StartIndex, int Length, string Word, string SuggestedWord,
    int WordCount, int ReferenceCount, double Distance, bool UsesOcrConfusion,
    bool IsDictionaryReference, bool HasAlternative)
{
    public int PageNumber => Sample.PageNumber;
    public Guid RegionId => Sample.RegionId;
}

public sealed record OcrSimilarWordResult(
    int TokenCount, int DistinctWordCount, IReadOnlyList<OcrSimilarWordCandidate> Candidates);

/// <summary>
/// Bounded, offline Latin technical-term analysis. Retains punctuation in Node.js and identifiers.
/// A deletion-signature index avoids an all-vocabulary-pairs comparison. rn/m and other common
/// OCR confusions are folded for lookup, then checked by a weighted distance on the ORIGINAL words.
/// </summary>
public sealed class OcrSimilarWordAnalyzer
{
    public const int MaximumTextCharacters = 8_000_000;
    public const int MaximumRegions = 100_000;
    public const int MaximumTokenLength = 64;
    public const int MaximumTerms = 2_000;
    public const int MaximumCandidates = 10_000;
    private const int MaximumTokens = 750_000;
    private const int MaximumDistinctWords = 25_000;
    private const int MaximumIndexEntries = 350_000;
    private const int MaximumComparisons = 250_000;
    private const long MaximumDistanceCells = 30_000_000;

    public OcrSimilarWordResult Analyze(IReadOnlyList<OcrWordSample> samples,
        OcrSimilarWordOptions options, CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (options.MinimumReferenceCount < 2 || options.MinimumReferenceCount > 1000 ||
            options.MinimumFrequencyRatio < 2 || options.MinimumFrequencyRatio > 1000 ||
            options.MaximumSuspectCount < 1 || options.MaximumSuspectCount > 100)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (samples.Count > MaximumRegions || samples.Sum(s => (long)s.Text.Length) > MaximumTextCharacters)
            throw LimitExceeded();
        _ = ReadTerms(options.KnownTerms);
        _ = ReadTerms(options.IgnoredTerms);
        var words = new Dictionary<string, WordEntry>(StringComparer.Ordinal);
        var tokenCount = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var token in Tokenize(samples[i].Text, cancellationToken))
            {
                if (++tokenCount > MaximumTokens) throw LimitExceeded();
                if (!words.TryGetValue(token.Word, out var entry))
                {
                    if (words.Count >= MaximumDistinctWords) throw LimitExceeded();
                    words.Add(token.Word, entry = new(token.Word));
                }
                entry.Count++;
            }
            if (i % 64 == 0) progress?.Report((int)(35L * (i + 1) / Math.Max(1, samples.Count)));
        }
        return Prepare(words.Values.Select(e => new OcrWordCount(e.Word, e.Count)).ToArray(), options, cancellationToken, progress).Query(samples, cancellationToken, progress);
    }

    public static PreparedIndex Prepare(IReadOnlyList<OcrWordCount> counts, OcrSimilarWordOptions options,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        if (options.MinimumReferenceCount is < 2 or > 1000 || options.MinimumFrequencyRatio is < 2 or > 1000 ||
            options.MaximumSuspectCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        if (counts.Count > MaximumDistinctWords || counts.Any(c => c.Count < 1 || !OcrLexiconPolicy.IsLatinTerm(c.Word)) ||
            counts.Sum(c => (long)c.Count) > MaximumTokens) throw LimitExceeded();
        var known = ReadTerms(options.KnownTerms); var ignored = ReadTerms(options.IgnoredTerms);
        var words = counts.ToDictionary(c => c.Word, c => new WordEntry(c.Word) { Count = c.Count }, StringComparer.Ordinal);
        foreach (var term in known)
        {
            if (!words.TryGetValue(term, out var entry))
            {
                if (words.Count >= MaximumDistinctWords) throw LimitExceeded();
                words.Add(term, entry = new(term));
            }
            entry.IsKnown = true;
        }
        var index = new Dictionary<string, List<WordEntry>>(StringComparer.Ordinal);
        var indexEntries = 0;
        foreach (var entry in words.Values.Where(e => e.IsKnown || e.Count >= options.MinimumReferenceCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var key in Signatures(FoldConfusions(entry.Word)))
            {
                if (++indexEntries > MaximumIndexEntries) throw LimitExceeded();
                if (!index.TryGetValue(key, out var bucket)) index.Add(key, bucket = []);
                bucket.Add(entry);
            }
        }
        progress?.Report(45);
        var suggestions = new Dictionary<string, Suggestion>(StringComparer.Ordinal);
        var comparisons = 0;
        long distanceCells = 0;
        var processed = 0;
        foreach (var suspect in words.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!suspect.IsKnown && !ignored.Contains(suspect.Word) && suspect.Count <= options.MaximumSuspectCount)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var matches = new List<Suggestion>();
                foreach (var signature in Signatures(FoldConfusions(suspect.Word)))
                {
                    if (!index.TryGetValue(signature, out var bucket)) continue;
                    foreach (var reference in bucket)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reference.Word == suspect.Word || !seen.Add(reference.Word) ||
                            (!reference.IsKnown && reference.Count < (long)suspect.Count * options.MinimumFrequencyRatio)) continue;
                        if (++comparisons > MaximumComparisons) throw LimitExceeded();
                        distanceCells += (long)(suspect.Word.Length + 1) * (reference.Word.Length + 1);
                        if (distanceCells > MaximumDistanceCells) throw LimitExceeded();
                        var distance = WeightedDistance(suspect.Word, reference.Word, out var usesConfusion);
                        // Do not suggest deleting an unmatched real closing quote merely
                        // because its opening quote was in a different OCR region.
                        if (suspect.Word.Contains('」') && !usesConfusion) continue;
                        if (distance > 1 || distance / Math.Max(suspect.Word.Length, reference.Word.Length) > .34) continue;
                        matches.Add(new(reference, distance, usesConfusion, false));
                    }
                }
                var ordered = matches.OrderBy(m => m.Distance).ThenByDescending(m => m.Reference.IsKnown)
                    .ThenByDescending(m => m.Reference.Count).ThenBy(m => m.Reference.Word, StringComparer.Ordinal).ToArray();
                if (ordered.Length > 0)
                    suggestions.Add(suspect.Word, ordered[0] with { HasAlternative = ordered.Length > 1 });
            }
            if (++processed % 64 == 0) progress?.Report(45 + (int)(40L * processed / Math.Max(1, words.Count)));
        }
        return new(words.Values.Sum(w => w.Count), words.Count, words, suggestions, index, options, ignored);
    }

    public sealed class PreparedIndex
    {
        private readonly int _tokenCount, _distinctCount;
        private readonly Dictionary<string, WordEntry> _words;
        private readonly Dictionary<string, Suggestion> _suggestions;
        private readonly Dictionary<string, List<WordEntry>> _index;
        private readonly OcrSimilarWordOptions _options;
        private readonly HashSet<string> _ignored;
        internal PreparedIndex(int tokens, int distinct, Dictionary<string, WordEntry> words, Dictionary<string, Suggestion> suggestions,
            Dictionary<string, List<WordEntry>> index, OcrSimilarWordOptions options, HashSet<string> ignored)
        { _tokenCount = tokens; _distinctCount = distinct; _words = words; _suggestions = suggestions; _index = index; _options = options; _ignored = ignored; }
        public OcrSimilarWordResult Query(IReadOnlyList<OcrWordSample> samples, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
        {
        if (samples.Count > MaximumRegions || samples.Sum(s => (long)s.Text.Length) > MaximumTextCharacters) throw LimitExceeded();
        var candidates = new List<OcrSimilarWordCandidate>();
        for (var i = 0; i < samples.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var token in Tokenize(samples[i].Text, cancellationToken))
            {
                if (!_suggestions.TryGetValue(token.Word, out var match))
                {
                    if (_words.ContainsKey(token.Word) || _ignored.Contains(token.Word)) continue;
                    // An unseen line term queries the already-built reference index, not the book.
                    var seen = new HashSet<string>(StringComparer.Ordinal); var matches = new List<Suggestion>();
                    long cells = 0;
                    foreach (var signature in Signatures(FoldConfusions(token.Word)))
                    {
                        if (!_index.TryGetValue(signature, out var bucket)) continue;
                        foreach (var reference in bucket)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!seen.Add(reference.Word) || (!reference.IsKnown && reference.Count < _options.MinimumFrequencyRatio)) continue;
                            cells += (long)(token.Word.Length + 1) * (reference.Word.Length + 1);
                            if (cells > MaximumDistanceCells || seen.Count > MaximumComparisons) throw LimitExceeded();
                            var distance = WeightedDistance(token.Word, reference.Word, out var confusion);
                            if (token.Word.Contains('」') && !confusion) continue;
                            if (distance <= 1 && distance / Math.Max(token.Word.Length, reference.Word.Length) <= .34)
                                matches.Add(new(reference, distance, confusion, false));
                        }
                    }
                    var ordered = matches.OrderBy(m => m.Distance).ThenByDescending(m => m.Reference.IsKnown)
                        .ThenByDescending(m => m.Reference.Count).ThenBy(m => m.Reference.Word, StringComparer.Ordinal).ToArray();
                    if (ordered.Length == 0) continue;
                    match = ordered[0] with { HasAlternative = ordered.Length > 1 };
                }
                if (candidates.Count >= MaximumCandidates) throw LimitExceeded();
                candidates.Add(new(samples[i], token.Start, token.Word.Length, token.Word,
                    match.Reference.Word, _words.GetValueOrDefault(token.Word)?.Count ?? 0, match.Reference.Count, match.Distance,
                    match.UsesConfusion, match.Reference.IsKnown, match.HasAlternative));
            }
            if (i % 64 == 0) progress?.Report(85 + (int)(15L * (i + 1) / Math.Max(1, samples.Count)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(100);
        return new(_tokenCount, _distinctCount, candidates);
        }
    }

    /// <summary>Only complete tokens qualify; long identifiers are skipped, never truncated.</summary>
    internal static IEnumerable<Token> Tokenize(string text, CancellationToken token)
    {
        var quoteDepth = 0;
        for (var offset = 0; offset < text.Length;)
        {
            if (offset % 1024 == 0) token.ThrowIfCancellationRequested();
            if (text[offset] == '「') { quoteDepth++; offset++; continue; }
            if (text[offset] == '」' && quoteDepth > 0) { quoteDepth--; offset++; continue; }
            if (!IsTokenCharacter(text[offset])) { offset++; continue; }
            var start = offset;
            var letters = 0;
            while (offset < text.Length && IsTokenCharacter(text[offset]) && !(text[offset] == '」' && quoteDepth > 0))
            {
                if (offset % 1024 == 0) token.ThrowIfCancellationRequested();
                if (char.IsAsciiLetter(text[offset])) letters++;
                offset++;
            }
            var end = offset;
            // Sentence punctuation is not part of the term; identifier underscores are retained.
            while (end > start && text[end - 1] is '.' or '-') end--;
            if (end - start is >= 3 and <= MaximumTokenLength && letters >= 2)
                yield return new(start, text[start..end]);
        }
    }

    private static bool IsTokenCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '#' or '」';

    private static HashSet<string> ReadTerms(IReadOnlyList<string>? terms)
    {
        if (terms is null) return new(StringComparer.Ordinal);
        if (terms.Count > MaximumTerms) throw LimitExceeded();
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            if (term.Length is < 3 or > MaximumTokenLength || !term.All(IsTokenCharacter) ||
                term.Count(char.IsAsciiLetter) < 2 || term.EndsWith('.') || term.EndsWith('-'))
                throw new ArgumentException("登録語は3～64文字の英数字用語を1語ずつ指定してください。");
            result.Add(term);
        }
        return result;
    }

    private static IEnumerable<string> Signatures(string word)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { word };
        yield return word;
        for (var i = 0; i < word.Length; i++)
        {
            var signature = word.Remove(i, 1);
            if (seen.Add(signature)) yield return signature;
        }
    }

    private static string FoldConfusions(string word)
    {
        var result = new StringBuilder(word.Length);
        for (var i = 0; i < word.Length; i++)
        {
            if (i + 1 < word.Length && word[i] == 'r' && word[i + 1] == 'n') { result.Append('m'); i++; }
            else if (i + 1 < word.Length && word[i] == 'v' && word[i + 1] == 'v') { result.Append('w'); i++; }
            else result.Append(FoldCharacter(word[i]));
        }
        return result.ToString();
    }

    internal static double WeightedDistance(string a, string b, out bool usesConfusion)
    {
        var d = new double[a.Length + 1, b.Length + 1];
        var confusion = new bool[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var isConfusion = a[i - 1] != b[j - 1] && FoldCharacter(a[i - 1]) == FoldCharacter(b[j - 1]);
            var substitution = a[i - 1] == b[j - 1] ? 0 : isConfusion ? .25 : 1;
            d[i, j] = d[i - 1, j - 1] + substitution;
            confusion[i, j] = confusion[i - 1, j - 1] || isConfusion;
            Relax(i, j, d[i - 1, j] + 1, confusion[i - 1, j]);
            Relax(i, j, d[i, j - 1] + 1, confusion[i, j - 1]);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                Relax(i, j, d[i - 2, j - 2] + .75, confusion[i - 2, j - 2]);
            if (i > 1 && IsMultiConfusion(a.AsSpan(i - 2, 2), b[j - 1]))
                Relax(i, j, d[i - 2, j - 1] + .25, true);
            if (j > 1 && IsMultiConfusion(b.AsSpan(j - 2, 2), a[i - 1]))
                Relax(i, j, d[i - 1, j - 2] + .25, true);
        }
        var distance = d[a.Length, b.Length];
        usesConfusion = confusion[a.Length, b.Length];
        return distance;
        void Relax(int i, int j, double value, bool hasConfusion)
        {
            if (value >= d[i, j]) return;
            d[i, j] = value;
            confusion[i, j] = hasConfusion;
        }
    }

    private static bool IsMultiConfusion(ReadOnlySpan<char> pair, char single) =>
        (pair.SequenceEqual("rn") && single == 'm') || (pair.SequenceEqual("vv") && single == 'w');

    private static char FoldCharacter(char c) => c switch
    {
        'I' or 'l' or '1' or 'j' or 'J' or '」' => 'i',
        'O' or '0' => 'o',
        _ => c,
    };

    private static InvalidOperationException LimitExceeded() => new(
        "類似語分析の安全上限を超えました。文書を分割するか、登録語・分析条件を絞ってください。部分結果は適用していません。");

    internal sealed class WordEntry(string word)
    {
        public string Word { get; } = word;
        public int Count { get; set; }
        public bool IsKnown { get; set; }
    }
    internal sealed record Suggestion(WordEntry Reference, double Distance, bool UsesConfusion, bool HasAlternative);
    internal readonly record struct Token(int Start, string Word);
}
