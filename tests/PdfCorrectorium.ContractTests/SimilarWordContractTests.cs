using PdfCorrectorium.Core.Analysis;

internal static class SimilarWordContractTests
{
    private static OcrWordSample Sample(int page, string text) => new(page, Guid.NewGuid(), text);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static Task DetectionAsync()
    {
        var samples = new[]
        {
            Sample(1, string.Join(' ', Enumerable.Repeat("Node.js mode window", 12))),
            Sample(9, "日本語😀：Node.is と rnode、Node.is。windovv"),
        };
        var before = samples[1].Text;
        var result = new OcrSimilarWordAnalyzer().Analyze(samples, new());
        Check(result.Candidates.Count == 4, "Both repeated Node.is occurrences, rnode and windovv must be found.");
        Check(result.Candidates.Count(c => c.Word == "Node.is" && c.SuggestedWord == "Node.js" && c.WordCount == 2 && c.ReferenceCount == 12) == 2,
            "The book-wide evidence and each occurrence must be retained.");
        Check(result.Candidates.Single(c => c.Word == "rnode").SuggestedWord == "mode", "rn is a many-to-one OCR confusion.");
        Check(result.Candidates.Single(c => c.Word == "windovv").SuggestedWord == "window", "vv/w confusion must be detected.");
        foreach (var c in result.Candidates)
        {
            Check(c.PageNumber == 9 && c.RegionId == samples[1].RegionId, "Candidates must retain their real page/region.");
            Check(c.Sample.Text.Substring(c.StartIndex, c.Length) == c.Word, "Offsets must remain exact after Japanese and surrogate pairs.");
            Check(c.UsesOcrConfusion, "Confusion explanations must be available.");
        }
        Check(samples[1].Text == before, "Analysis never changes its input.");
        return Task.CompletedTask;
    }

    internal static Task DictionaryAsync()
    {
        var samples = new[] { Sample(1, "Node.is rnode Node.js mode") };
        var analyzer = new OcrSimilarWordAnalyzer();
        Check(analyzer.Analyze(samples, new()).Candidates.Count == 0, "Equal-frequency words are not evidence of an error.");
        var dictionary = analyzer.Analyze(samples, new(KnownTerms: ["Node.js", "mode"]));
        Check(dictionary.Candidates.Count == 2 && dictionary.Candidates.All(c => c.IsDictionaryReference), "Explicit correct terms override frequency thresholds.");
        Check(analyzer.Analyze([Sample(3, "rnode")], new(KnownTerms: ["mode"])).Candidates.Single().ReferenceCount == 0,
            "A registered term may be absent from the book; its count must remain zero.");
        Check(analyzer.Analyze(samples, new(KnownTerms: ["Node.js", "mode"], IgnoredTerms: ["Node.is", "rnode"])).Candidates.Count == 0,
            "Ignored words must not be proposed for correction.");
        Check(analyzer.Analyze(samples, new(KnownTerms: ["Node.js", "Node.is", "mode", "rnode"])).Candidates.Count == 0,
            "All explicitly valid terms must be respected.");
        return Task.CompletedTask;
    }

    internal static Task TokenBoundariesAsync()
    {
        var analyzer = new OcrSimilarWordAnalyzer();
        var result = analyzer.Analyze([Sample(1, "Node.is. node.is my_Node.is Node.isExtra a is 12345 " + new string('x', 65))],
            new(KnownTerms: ["Node.js"]));
        Check(result.Candidates.Count == 1 && result.Candidates[0].Word == "Node.is", "Sentence punctuation is removed, but case, identifiers and long-token boundaries are preserved.");
        var ambiguous = analyzer.Analyze([Sample(1, "cxt")], new(KnownTerms: ["cat", "cut"]));
        Check(ambiguous.Candidates.Single().HasAlternative, "Ambiguous matches must be labelled, not hidden as certain corrections.");
        Check(ambiguous.Candidates.Single().SuggestedWord == "cat", "Ties must have deterministic ordinal ordering.");
        var transposed = analyzer.Analyze([Sample(1, "Ndoe.js")], new(KnownTerms: ["Node.js"]));
        Check(transposed.Candidates.Single().SuggestedWord == "Node.js", "A single adjacent transposition must be discoverable through deletion signatures.");
        var brackets = analyzer.Analyze([Sample(2, "😀 Node.」s 」avaScript 「JavaScript」 」")],
            new(KnownTerms: ["Node.js", "JavaScript"]));
        Check(brackets.Candidates.Count == 2 && brackets.Candidates.All(candidate => candidate.UsesOcrConfusion),
            "Unmatched brackets within Latin terms retain j/J confusion without selecting standalone or paired quotation marks.");
        Check(brackets.Candidates.Any(candidate => candidate.Word == "Node.」s" && candidate.SuggestedWord == "Node.js") &&
            brackets.Candidates.Any(candidate => candidate.Word == "」avaScript" && candidate.SuggestedWord == "JavaScript"), "Both lowercase j and uppercase J can be suggested.");
        Check(brackets.Candidates.All(candidate => candidate.Sample.Text.Substring(candidate.StartIndex, candidate.Length) == candidate.Word),
            "Bracket-confusion offsets remain exact after surrogate pairs.");
        Check(analyzer.Analyze([Sample(2, "Node.js」")], new(KnownTerms: ["Node.js"])).Candidates.Count == 0,
            "A real closing quote whose opening was in a different OCR region is not proposed for deletion.");
        return Task.CompletedTask;
    }

    internal static Task LimitsAsync()
    {
        var analyzer = new OcrSimilarWordAnalyzer();
        void MustFail<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        MustFail<OperationCanceledException>(() => analyzer.Analyze([], new(), cancelled.Token));
        using var midAnalysis = new CancellationTokenSource();
        MustFail<OperationCanceledException>(() => analyzer.Analyze([Sample(1, "Node.is")], new(KnownTerms: ["Node.js"]),
            midAnalysis.Token, new ImmediateProgress(value => { if (value >= 45) midAnalysis.Cancel(); })));
        MustFail<InvalidOperationException>(() => analyzer.Analyze([Sample(1, new string('x', OcrSimilarWordAnalyzer.MaximumTextCharacters + 1))], new()));
        MustFail<InvalidOperationException>(() => analyzer.Analyze([], new(KnownTerms: Enumerable.Repeat("term", 2001).ToArray())));
        MustFail<ArgumentException>(() => analyzer.Analyze([], new(KnownTerms: ["two words"])));
        MustFail<ArgumentOutOfRangeException>(() => analyzer.Analyze([], new(MinimumFrequencyRatio: 1)));
        var vocabulary = string.Join(' ', Enumerable.Range(0, 25001).Select(i => "term_" + i));
        MustFail<InvalidOperationException>(() => analyzer.Analyze([Sample(1, vocabulary)], new()));
        return Task.CompletedTask;
    }

    private sealed class ImmediateProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
