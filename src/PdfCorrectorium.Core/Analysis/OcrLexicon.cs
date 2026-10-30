using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.Core.Analysis;

/// <summary>Explicit, book-local knowledge. Frequency never confers permission to replace text.</summary>
public sealed record OcrLexicon
{
    public IReadOnlyList<string> KnownTerms { get; init; } = [];
    public IReadOnlyList<string> IgnoredTerms { get; init; } = [];
    public IReadOnlyList<OcrCorrectionRule> Rules { get; init; } = [];
}

public sealed record OcrCorrectionRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Source { get; init; } = "";
    public string Target { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool AllowAutomatic { get; init; }
}

public sealed record OcrLexiconMatch(int Start, int Length, string Source, string Target,
    Guid? RuleId, bool CanApplyAutomatically, string Reason);
public sealed record OcrLexiconQuery(string RawText, string SuggestedText,
    IReadOnlyList<OcrLexiconMatch> Matches, bool CanApplyAutomatically);
public sealed record OcrWordCount(string Word, int Count);

/// <summary>Optional, disposable frequency data. Never serialized inside the authoritative project JSON.</summary>
public sealed record OcrWordCountsCache
{
    public const int CurrentAlgorithmVersion = 1;
    public int AlgorithmVersion { get; init; } = CurrentAlgorithmVersion;
    public string Stamp { get; init; } = "";
    public IReadOnlyList<OcrWordCount> Words { get; init; } = [];
}

public static class OcrLexiconPolicy
{
    public const int MaximumRules = 2000;
    public static int ElementCount(string value) => StringInfo.ParseCombiningCharacters(value).Length;
    public static bool IsLatinTerm(string value) => OcrSimilarWordAnalyzer.Tokenize(value, default)
        .Any(token => token.Start == 0 && token.Word.Length == value.Length);
    public static bool IsValidTerm(string? value) => value is { Length: >= 2 and <= 256 } &&
        !value.Any(char.IsControl) && !value.Any(char.IsWhiteSpace) &&
        ElementCount(value) is >= 2 and <= OcrSimilarWordAnalyzer.MaximumTokenLength &&
        (IsLatinTerm(value) || value.EnumerateRunes().Any(r => r.Value is >= 0x3040 and <= 0x30ff or >= 0x3400 and <= 0x9fff or >= 0x20000 and <= 0x323af));

    public static void Validate(OcrLexicon? lexicon)
    {
        if (lexicon?.KnownTerms is null || lexicon.IgnoredTerms is null || lexicon.Rules is null ||
            lexicon.KnownTerms.Count > OcrSimilarWordAnalyzer.MaximumTerms ||
            lexicon.IgnoredTerms.Count > OcrSimilarWordAnalyzer.MaximumTerms || lexicon.Rules.Count > MaximumRules)
            throw new InvalidDataException("辞書の登録上限（正解語・無視語・ルール各2,000件）を超えているか、形式が不正です。");
        foreach (var terms in new[] { lexicon.KnownTerms, lexicon.IgnoredTerms })
            if (terms.Any(term => !IsValidTerm(term)) || terms.Distinct(StringComparer.Ordinal).Count() != terms.Count)
                throw new InvalidDataException("辞書の語句は重複なしの2～64文字の英数字用語／日本語語句にしてください。空白・制御文字は使えません。");
        var ids = new HashSet<Guid>(); var pairs = new HashSet<(string, string)>();
        foreach (var rule in lexicon.Rules)
            if (rule is null || rule.Id == Guid.Empty || !ids.Add(rule.Id) || !IsValidTerm(rule.Source) ||
                !IsValidTerm(rule.Target) || rule.Source == rule.Target || !pairs.Add((rule.Source, rule.Target)))
                throw new InvalidDataException("訂正ルールのID・語句・重複を確認してください。1文字の一括置換は登録できません。");
    }

    // Called in a worker at snapshot/save boundaries, never for each key press or line query.
    public static string GetCacheStamp(PdfCorrectoriumProject project)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) { var bytes = Encoding.UTF8.GetBytes(text); hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes); }
        Add(OcrWordCountsCache.CurrentAlgorithmVersion.ToString(CultureInfo.InvariantCulture));
        Add(project.SourcePdf.Sha256); Add(project.ProjectId.ToString());
        foreach (var page in project.PageSequence) { Add(page.PageId.ToString()); Add(page.SourcePageNumber.ToString(CultureInfo.InvariantCulture)); }
        foreach (var page in project.Pages.OrderBy(p => p.PageNumber))
        {
            Add(page.PageNumber.ToString(CultureInfo.InvariantCulture));
            foreach (var region in page.TextRegions.OrderBy(r => r.Id))
            { Add(region.Id.ToString()); Add(region.IsDeleted ? "deleted" : region.EffectiveText); }
        }
        foreach (var word in project.OcrLexicon.KnownTerms.Order(StringComparer.Ordinal)) Add("k" + word);
        foreach (var word in project.OcrLexicon.IgnoredTerms.Order(StringComparer.Ordinal)) Add("i" + word);
        foreach (var rule in project.OcrLexicon.Rules.OrderBy(r => r.Id))
        { Add(rule.Id.ToString()); Add(rule.Source); Add(rule.Target); Add(rule.Enabled.ToString()); Add(rule.AllowAutomatic.ToString()); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>Immutable shared query index; no UI, OCR, rendering, or full-document scans on query.</summary>
public sealed class OcrLexiconIndex
{
    private readonly OcrLexicon _lexicon;
    private readonly OcrSimilarWordAnalyzer.PreparedIndex _index;
    private readonly HashSet<string> _ignored;
    private readonly HashSet<string> _unsafeSources;
    private readonly Dictionary<string, List<string>> _japaneseIndex = new(StringComparer.Ordinal);
    private readonly HashSet<int> _japaneseLengths = [];
    public OcrLexiconIndex(OcrLexicon lexicon, IReadOnlyList<OcrWordCount> counts,
        OcrSimilarWordOptions? options = null, CancellationToken token = default)
    {
        OcrLexiconPolicy.Validate(lexicon); _lexicon = lexicon;
        _ignored = lexicon.IgnoredTerms.Concat(lexicon.KnownTerms).ToHashSet(StringComparer.Ordinal);
        _index = OcrSimilarWordAnalyzer.Prepare(counts, (options ?? new()) with
        { KnownTerms = lexicon.KnownTerms.Where(OcrLexiconPolicy.IsLatinTerm).ToArray(), IgnoredTerms = lexicon.IgnoredTerms.Where(OcrLexiconPolicy.IsLatinTerm).ToArray() }, token);
        foreach (var phrase in lexicon.KnownTerms.Where(w => !OcrLexiconPolicy.IsLatinTerm(w)))
        {
            token.ThrowIfCancellationRequested(); _japaneseLengths.Add(OcrLexiconPolicy.ElementCount(phrase));
            foreach (var key in PhraseSignatures(phrase))
            { if (!_japaneseIndex.TryGetValue(key, out var bucket)) _japaneseIndex.Add(key, bucket = []); bucket.Add(phrase); }
        }
        var rules = lexicon.Rules.Where(r => r.Enabled).ToArray();
        _unsafeSources = rules.GroupBy(r => r.Source).Where(g => g.Count() != 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        // All members of cycles are denied; no rule is cascaded onto another rule's output.
        var graph = rules.GroupBy(r => r.Source).ToDictionary(g => g.Key, g => g.Select(r => r.Target).ToArray(), StringComparer.Ordinal);
        foreach (var source in graph.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(graph[source]);
            while (pending.TryPop(out var next))
            { if (next == source) { _unsafeSources.Add(source); break; } if (seen.Add(next) && graph.TryGetValue(next, out var targets)) foreach (var target in targets) pending.Push(target); }
        }
    }

    public OcrSimilarWordResult Analyze(IReadOnlyList<OcrWordSample> samples, CancellationToken token = default)
    {
        var result = _index.Query(samples, token); var candidates = result.Candidates.ToList();
        foreach (var sample in samples)
        {
            token.ThrowIfCancellationRequested();
            if (sample.Text.Length > 2048 || OcrLexiconPolicy.ElementCount(sample.Text) > 512)
            {
                // A normal analysis region need not be a single OCR line. Preserve full-token rule matching there too.
                var tokens = OcrSimilarWordAnalyzer.Tokenize(sample.Text, token).ToDictionary(t => t.Start, t => t.Word);
                foreach (var rule in _lexicon.Rules.Where(r => r.Enabled && !_ignored.Contains(r.Source)))
                foreach (var start in Occurrences(sample.Text, rule.Source))
                {
                    token.ThrowIfCancellationRequested();
                    if (OcrLexiconPolicy.IsLatinTerm(rule.Source) && tokens.GetValueOrDefault(start) != rule.Source) continue;
                    if (!IsElementBoundary(sample.Text, start) || !IsElementBoundary(sample.Text, start + rule.Source.Length)) continue;
                    if (candidates.Count >= OcrSimilarWordAnalyzer.MaximumCandidates) throw new InvalidOperationException("訂正候補が安全上限を超えました。");
                    candidates.RemoveAll(c => c.Sample == sample && c.StartIndex == start);
                    candidates.Add(new(sample, start, rule.Source.Length, rule.Source, rule.Target, 0, 0, 0, false, true, _unsafeSources.Contains(rule.Source)));
                }
                continue;
            }
            var extras = Query(sample.Text, sample.IsLocked, token, sameCountOnly: false).Matches.Where(m => m.RuleId is not null || !OcrLexiconPolicy.IsLatinTerm(m.Source)).ToArray();
            foreach (var match in extras)
            {
                candidates.RemoveAll(c => c.Sample == sample && c.StartIndex < match.Start + match.Length && match.Start < c.StartIndex + c.Length);
                if (candidates.Count >= OcrSimilarWordAnalyzer.MaximumCandidates) throw new InvalidOperationException("訂正候補が安全上限を超えました。");
                candidates.Add(new(sample, match.Start, match.Length, match.Source, match.Target, 0, 0, 0, false, true,
                    extras.Any(m => m != match && m.Start < match.Start + match.Length && match.Start < m.Start + m.Length)));
            }
        }
        // Retain the analyzer's region order. Sorting offsets across different regions
        // would move locked rows ahead of editable rows after a partial correction.
        return result with { Candidates = candidates.ToArray() };
    }

    public OcrLexiconQuery Query(string raw, bool locked = false, CancellationToken token = default, bool sameCountOnly = true)
    {
        if (raw.Length > 2048 || OcrLexiconPolicy.ElementCount(raw) > 512) throw new ArgumentException("1行の文字数上限を超えました。");
        token.ThrowIfCancellationRequested();
        var matches = new List<OcrLexiconMatch>();
        var tokens = OcrSimilarWordAnalyzer.Tokenize(raw, token).ToArray();
        var protectedRanges = _ignored.SelectMany(word => Occurrences(raw, word)
            .Where(start => !OcrLexiconPolicy.IsLatinTerm(word) || tokens.Any(t => t.Start == start && t.Word == word))
            .Select(start => (Start: start, End: start + word.Length))).ToArray();
        bool Eligible(string source, int start) => !_ignored.Contains(source) && !protectedRanges.Any(r => r.Start < start + source.Length && start < r.End) &&
            (!OcrLexiconPolicy.IsLatinTerm(source) || tokens.Any(t => t.Start == start && t.Word == source));
        foreach (var rule in _lexicon.Rules.Where(r => r.Enabled))
        {
            token.ThrowIfCancellationRequested();
            for (var start = raw.IndexOf(rule.Source, StringComparison.Ordinal); start >= 0;
                 start = raw.IndexOf(rule.Source, start + rule.Source.Length, StringComparison.Ordinal))
            {
                if (!Eligible(rule.Source, start) || !IsElementBoundary(raw, start) || !IsElementBoundary(raw, start + rule.Source.Length)) continue;
                var same = OcrLexiconPolicy.ElementCount(rule.Source) == OcrLexiconPolicy.ElementCount(rule.Target);
                if (sameCountOnly && !same) continue;
                var automatic = !locked && same && rule.AllowAutomatic && !_unsafeSources.Contains(rule.Source);
                if (matches.Count >= 512) throw new InvalidOperationException("1行の辞書候補数が上限を超えました。ルールを絞ってください。");
                matches.Add(new(start, rule.Source.Length, rule.Source, rule.Target, rule.Id, automatic,
                    automatic ? "明示ルール・自動適用を許可済み" : "明示ルール・確認が必要"));
            }
        }
        foreach (var c in _index.Query([new(1, Guid.Empty, raw, locked)], token).Candidates)
            if (Eligible(c.Word, c.StartIndex) && !matches.Any(m => m.Start < c.StartIndex + c.Length && c.StartIndex < m.Start + m.Length) &&
                OcrLexiconPolicy.ElementCount(c.Word) == OcrLexiconPolicy.ElementCount(c.SuggestedWord))
                matches.Add(new(c.StartIndex, c.Length, c.Word, c.SuggestedWord, null, false,
                    c.IsDictionaryReference ? "正解語に類似・確認が必要" : "頻出語に類似・確認が必要"));
        // Japanese has no inferred word boundaries. Only registered phrases are compared; never auto-applied.
        var starts = StringInfo.ParseCombiningCharacters(raw);
        long cells = 0;
        foreach (var count in _japaneseLengths.Order())
        {
            for (var i = 0; i <= starts.Length - count; i++)
            {
                token.ThrowIfCancellationRequested();
                var start = starts[i]; var end = i + count < starts.Length ? starts[i + count] : raw.Length;
                var source = raw[start..end];
                if (!Eligible(source, start) || source.Any(char.IsWhiteSpace) ||
                    matches.Any(m => m.Start < end && start < m.Start + m.Length)) continue;
                var references = PhraseSignatures(source).Where(_japaneseIndex.ContainsKey).SelectMany(key => _japaneseIndex[key]).Distinct(StringComparer.Ordinal);
                foreach (var phrase in references)
                {
                    if (OcrLexiconPolicy.ElementCount(phrase) != count || source == phrase) continue;
                    cells += (long)(source.Length + 1) * (phrase.Length + 1);
                    if (cells > 30_000_000) throw new InvalidOperationException("辞書の比較上限を超えました。登録語を絞ってください。");
                    var distance = OcrSimilarWordAnalyzer.WeightedDistance(source, phrase, out _);
                    if (distance <= 1 && distance / count <= .34)
                    {
                        if (matches.Count >= 512) throw new InvalidOperationException("1行の辞書候補数が上限を超えました。ルールを絞ってください。");
                        matches.Add(new(start, source.Length, source, phrase, null, false, "登録した日本語語句に類似・確認が必要"));
                    }
                }
            }
        }
        var ordered = matches.OrderBy(m => m.Start).ThenBy(m => m.Target, StringComparer.Ordinal).ToArray();
        var ambiguous = ordered.Where((m, i) => ordered.Where((_, j) => i != j).Any(n => n.Start < m.Start + m.Length && m.Start < n.Start + n.Length)).ToHashSet();
        ordered = ordered.Select(m => ambiguous.Contains(m) ? m with { CanApplyAutomatically = false, Reason = "候補が競合・確認が必要" } : m).ToArray();
        var chosen = new List<OcrLexiconMatch>(); var boundary = 0;
        foreach (var match in ordered) if (match.Start >= boundary) { chosen.Add(match); boundary = match.Start + match.Length; }
        var suggested = raw;
        foreach (var match in chosen.OrderByDescending(m => m.Start)) suggested = suggested.Remove(match.Start, match.Length).Insert(match.Start, match.Target);
        return new(raw, suggested, ordered, ordered.Length > 0 && !locked && ordered.All(m => m.CanApplyAutomatically));
    }
    private static bool IsElementBoundary(string value, int offset) => offset == value.Length || StringInfo.ParseCombiningCharacters(value).Contains(offset);
    private static IEnumerable<int> Occurrences(string text, string word)
    { for (var i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + word.Length, StringComparison.Ordinal)) yield return i; }
    private static IEnumerable<string> PhraseSignatures(string value)
    {
        yield return value;
        var offsets = StringInfo.ParseCombiningCharacters(value);
        for (var i = 0; i < offsets.Length; i++) yield return value.Remove(offsets[i], (i + 1 < offsets.Length ? offsets[i + 1] : value.Length) - offsets[i]);
    }
}
