using PdfCorrectorium.Core.Analysis;

internal static class RapidOcrContractTests
{
    internal static OcrLineReading Reading(string text, double score = .995)
    {
        var n = OcrLineReconciler.Elements(text).Length;
        return new(text, Enumerable.Repeat(score, n).ToArray(),
            Enumerable.Range(0, n).Select(i => (i + .5) / n).ToArray(), Enumerable.Repeat(true, n).ToArray());
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static Task AutomaticAsync()
    {
        var reading = Reading("フレームワーク");
        var result = OcrLineReconciler.Evaluate("フレムワーク", reading, false, false, false);
        Check(result.IsAutomatic && result.InsertedIndices.SequenceEqual(new[] { 2 }), "Unique high-confidence missing text must be inserted.");
        var supplementary = Reading("A😀Bあいう");
        Check(OcrLineReconciler.Evaluate("ABあいう", supplementary, false, false, false).IsAutomatic,
            "Grapheme insertion must not split supplementary characters.");
        Check(!OcrLineReconciler.Evaluate("あいう", Reading("ああいう"), false, false, false).IsAutomatic,
            "Repeated-character ambiguity must be reviewed.");
        Check(!OcrLineReconciler.Evaluate("Node.is", Reading("Node.js"), false, false, false).IsAutomatic,
            "Substitutions must never be automatic.");
        Check(!OcrLineReconciler.Evaluate("Nodee.js", Reading("Node.js"), false, false, false).IsAutomatic,
            "Deletions must never be automatic.");
        foreach (var punctuation in new[] { "。", "、", ".", ",", "！", "？" })
            Check(!OcrLineReconciler.Evaluate("認識した文字" + punctuation, Reading("認識した文字"),
                false, false, false).IsAutomatic, "Existing trailing punctuation must never be deleted automatically.");
        return Task.CompletedTask;
    }
    internal static Task SafetyAsync()
    {
        var r = Reading("フレームワーク");
        bool Auto(OcrLineReading reading, bool locked = false, bool edited = false, bool vertical = false) =>
            OcrLineReconciler.Evaluate("フレムワーク", reading, locked, edited, vertical).IsAutomatic;
        Check(!Auto(r, locked: true), "Locked characters must not be auto-edited.");
        Check(!Auto(r, edited: true), "Reviewed text must not be silently changed.");
        Check(!Auto(r, vertical: true), "Unvalidated writing mode must require review.");
        Check(!Auto(Reading("フレームワ一ク")), "A single result that substitutes existing text must require review.");
        Check(!Auto(Reading(r.Text, .9)), "Low score must require review.");
        var weak = r.Scores.ToArray(); weak[2] = .97;
        Check(!Auto(r with { Scores = weak }), "High line average cannot conceal a weak added character.");
        var ink = r.Ink.ToArray(); ink[2] = false;
        Check(!Auto(r with { Ink = ink }), "Added text must have image evidence.");
        Check(!Auto(r with { Positions = [] }), "Missing character evidence must require review.");
        Check(!Auto(r with { Scores = r.Scores.Select(_ => double.NaN).ToArray() }), "NaN scores are not confidence.");
        Check(!Auto(Reading(r.Text, 1.01)), "Out-of-range scores cannot bypass the single-result evidence check.");
        Check(!Auto(r with { Positions = r.Positions.AsEnumerable().Reverse().ToArray() }), "Invalid character order is rejected.");
        var whitespace = Reading("Node .js");
        Check(!OcrLineReconciler.Evaluate("Node.js", whitespace, false, false, false).IsAutomatic, "Whitespace changes require review.");
        return Task.CompletedTask;
    }
    internal static Task GeometryAsync()
    {
        var reading = Reading("フレームワーク");
        double[] old = [10, 20, 30, 40, 50, 60];
        var widths = OcrLineReconciler.ReconcileAdvances("フレムワーク", old, reading);
        Check(widths.Length == 7 && widths.All(v => v > 0) && Math.Abs(widths.Sum() - old.Sum()) < 1e-8, "Inserting text preserves line bounds.");
        Check(widths[0] == old[0] && widths[^1] == old[^1] && widths[^2] == old[^2], "Distant manually adjusted widths remain unchanged.");
        var sameLength = OcrLineReconciler.ReconcileAdvances("Node.is", Enumerable.Repeat(10d, 7).ToArray(), Reading("Node.js"));
        Check(sameLength.All(v => v == 10), "Substitution preserves existing character geometry.");
        var removed = OcrLineReconciler.ReconcileAdvances("Nodee.js", Enumerable.Repeat(10d, 8).ToArray(), Reading("Node.js"));
        Check(removed.Length == 7 && Math.Abs(removed.Sum() - 80) < 1e-8, "Reviewed deletion also retains line extent.");
        return Task.CompletedTask;
    }
}
