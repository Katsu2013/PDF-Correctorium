using System.Collections;
using System.Diagnostics;
using PdfCorrectorium.Core.Analysis;

internal static class OcrDensityContractTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static OcrQualitySample Row(string text, double width = 100, double height = 20, bool vertical = false) =>
        new(1, Guid.NewGuid(), text, width, height, vertical, false, false, []);
    private static readonly OcrCharacterCountAnalysisOptions Options = new();
    private static IReadOnlyList<OcrCharacterCountAnomaly> Analyze(IReadOnlyList<OcrQualitySample> rows) =>
        new OcrQualityAnalyzer().FindCharacterCountAnomalies(rows, Options);
    private static List<OcrQualitySample> NormalRows() =>
    [
        Row("日本語文章"), Row("ABCDEFGH", 96), Row("日本ABC123", 112),
        Row("ＡＢ１２かな", 120), Row("ｱｲｳｴｵｶｷｸｹｺ"), Row("CaféABCD", 96),
    ];

    internal static Task MixedAsync()
    {
        var rows = NormalRows();
        var screenshot = Row("通でした。", 100.4, 21.5);
        rows.Add(screenshot);
        rows.Add(Row("んと理解しておきましょう。", 277.8, 19.6));
        Check(Analyze(rows).Count == 0, "Healthy CJK, Latin and mixed lines, including screenshot proportions, must not be flagged.");
        var shortRow = Row("日本");
        var crowded = Row(new string('B', 18));
        rows.Add(shortRow); rows.Add(crowded);
        var results = Analyze(rows);
        Check(results.Count == 2 && results.Single(r => r.RegionId == shortRow.RegionId).Kind == OcrCharacterCountAnomalyKind.TooFew &&
              results.Single(r => r.RegionId == crowded.RegionId).Kind == OcrCharacterCountAnomalyKind.TooMany,
            "Missing CJK and crowded Latin text must still be detected using their own width model.");
        Check(Math.Abs(results.Single(r => r.RegionId == shortRow.RegionId).ExpectedCharacterCount - 5) < .01,
            "The estimate is five CJK characters, not the raw Latin peer count.");
        Check(Math.Abs(results.Single(r => r.RegionId == crowded.RegionId).ExpectedCharacterCount - 100d / 20 / .6) < .01,
            "The Latin estimate uses 0.6-em width, not the raw CJK peer count.");
        Check(results.All(r => r.PeerCount == 8), "Extreme candidates cannot distort the independent plausible calibration pool.");
        return Task.CompletedTask;
    }

    internal static Task UnicodeAsync()
    {
        var rows = NormalRows();
        rows.AddRange([
            Row("ががががが"), Row(string.Concat(Enumerable.Repeat("か\u3099", 5))),
            Row(string.Concat(Enumerable.Repeat("𠮷", 5))), Row("한국어문장"),
            Row(string.Concat(Enumerable.Repeat("\u1112\u1161\u11AB", 5))),
            Row(string.Concat(Enumerable.Repeat("ｶﾞ", 10))),
            Row(string.Concat(Enumerable.Repeat("e\u0301", 10)), 120),
            Row(string.Concat(Enumerable.Repeat("👩‍👩‍👧‍👦", 5))),
            Row("  日本語文章  "), Row("AB CD EF", 85.2), Row("iiiiiiiiii", 60), Row("WWWWW", 90),
        ]);
        Check(Analyze(rows).Count == 0, "Combining marks, surrogate pairs, joined emoji, halfwidth dakuten, padding and narrow/wide Latin retain healthy density.");
        return Task.CompletedTask;
    }

    internal static Task GeometryAsync()
    {
        var rows = new List<OcrQualitySample>();
        for (var i = 0; i < 5; i++)
        {
            rows.Add(Row(new string('漢', 5 + i), (5 + i) * 20));
            rows.Add(Row("日本語文章", 100, 40)); // distinct line thickness and reference density
            rows.Add(Row("日本語文章", 20, 150, true));
            rows.Add(Row("ABCDE", 20, 150, true)); // upright vertical cells
        }
        var missing = Row("日本", 100, 40);
        var missingVertical = Row("日本", 20, 150, true);
        rows.Add(missing); rows.Add(missingVertical);
        var results = Analyze(rows);
        Check(results.Count == 2 && results.Select(r => r.RegionId).ToHashSet().SetEquals([missing.RegionId, missingVertical.RegionId]),
            "Different lengths normalize; thickness and direction keep independent calibration.");
        var scaled = rows.Select(row => row with { Width = row.Width * 3.7, Height = row.Height * 3.7 }).ToArray();
        var scaledResults = Analyze(scaled).ToDictionary(row => row.RegionId);
        Check(results.All(r => Math.Abs(r.CountRatio - scaledResults[r.RegionId].CountRatio) < 1e-10),
            "Uniform preview/page scaling cannot change the diagnosis.");
        return Task.CompletedTask;
    }

    internal static Task SafetyAsync()
    {
        var sparse = NormalRows().Take(3).Append(Row("日")).ToArray();
        Check(Analyze(sparse).Count == 0, "Too few independent reference lines cannot establish a finding.");
        var unstable = Enumerable.Range(0, 8).Select(i => Row("日本語文章", i < 4 ? 200 : 50)).Append(Row("日")).ToArray();
        Check(Analyze(unstable).Count == 0, "A broad/bimodal central reference must be left for manual review, not treated as a confident median.");
        var row = Row("日本語文章");
        Check(Analyze(Enumerable.Repeat(row, 20).Append(Row("日")).ToArray()).Count == 0,
            "Duplicate region IDs cannot manufacture enough independent peers.");
        var invalid = NormalRows();
        invalid.AddRange([Row(""), Row("   "), Row("a\nb"), Row("a\rb"), Row("abc", 0), Row("abc", double.NaN), Row("abc", 100, double.PositiveInfinity)]);
        Check(Analyze(invalid).Count == 0, "Empty/blank and invalid geometry must not contaminate references.");
        Check(new OcrQualityAnalyzer().FindCharacterCountAnomalies(invalid, new(double.NaN, 4, double.NaN)).Count == 0,
            "Non-finite numeric options use safe defaults.");
        var source = NormalRows().ToArray(); var before = source.ToArray(); Analyze(source);
        Check(source.SequenceEqual(before), "Read-only analysis cannot mutate text, geometry, locks or advances.");
        return Task.CompletedTask;
    }

    internal static Task OracleAsync()
    {
        var random = new Random(42);
        var rows = Enumerable.Range(0, 320).Select(i =>
        {
            var count = random.Next(2, 25); var thickness = 8 + random.NextDouble() * 25;
            var density = i % 23 == 0 ? .2 : i % 29 == 0 ? 3 : .94 + random.NextDouble() * .12;
            var extent = count * thickness / density;
            return i % 2 == 0 ? Row(new string('漢', count), extent, thickness) : Row(new string('漢', count), thickness, extent, true);
        }).ToArray();
        static double Density(OcrQualitySample row) => row.CharacterCount / (row.WritingExtent / row.LineThickness);
        static double Quantile(double[] sorted, double p)
        {
            var position = p * (sorted.Length - 1); var lower = (int)position;
            return lower + 1 < sorted.Length ? sorted[lower] + (sorted[lower + 1] - sorted[lower]) * (position - lower) : sorted[lower];
        }
        var expected = new Dictionary<Guid, (int Count, double Ratio)>();
        foreach (var row in rows)
        {
            var peers = rows.Where(peer => peer.RegionId != row.RegionId && peer.IsVertical == row.IsVertical &&
                Math.Abs(peer.LineThickness - row.LineThickness) / Math.Max(peer.LineThickness, row.LineThickness) <= .15)
                .Select(Density).Where(value => value is >= .5 and <= 2).Order().ToArray();
            if (peers.Length < 4 || Quantile(peers, .75) > Quantile(peers, .25) * 1.5) continue;
            var ratio = Density(row) / Quantile(peers, .5);
            if (Math.Abs(row.CharacterCount - row.CharacterCount / ratio) >= 2 && (ratio <= 1 / 1.6 || ratio >= 1.6))
                expected.Add(row.RegionId, (peers.Length, ratio));
        }
        var actual = Analyze(rows);
        Check(actual.Count > 10 && actual.Count == expected.Count && actual.All(item => expected.ContainsKey(item.RegionId)),
            "The sliding thickness window must find exactly the independent brute-force candidates.");
        Check(actual.All(item => item.PeerCount == expected[item.RegionId].Count && Math.Abs(item.CountRatio - expected[item.RegionId].Ratio) < 1e-10),
            "Sliding order statistics must agree with exact leave-one-out median and peer counts.");
        return Task.CompletedTask;
    }

    internal static Task BudgetAsync()
    {
        var rows = Enumerable.Range(0, 25000).Select(i =>
        {
            var thickness = 20 + i % 20 * .1;
            return Row(i % 1000 == 0 ? "漢" : "日本語文章", thickness * 5, thickness);
        }).ToArray();
        var timer = Stopwatch.StartNew(); var results = Analyze(rows); timer.Stop();
        Check(results.Count == 25, "A large healthy book must retain exactly its deliberately sparse rows.");
        Console.WriteLine($"Density analysis: {rows.Length:N0} rows in {timer.Elapsed.TotalMilliseconds:0} ms.");
        Check(timer.Elapsed < TimeSpan.FromSeconds(20), "The large-book reference calculation must avoid all-pairs scanning.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { new OcrQualityAnalyzer().FindCharacterCountAnomalies(rows, Options, cancelled.Token); throw new InvalidOperationException("Pre-cancellation was ignored."); }
        catch (OperationCanceledException) { }
        using var midway = new CancellationTokenSource();
        try { new OcrQualityAnalyzer().FindCharacterCountAnomalies(new CancellingRows(rows, midway), Options, midway.Token); throw new InvalidOperationException("Mid-analysis cancellation was ignored."); }
        catch (OperationCanceledException) { }
        return Task.CompletedTask;
    }

    private sealed class CancellingRows(OcrQualitySample[] rows, CancellationTokenSource cancellation) : IReadOnlyList<OcrQualitySample>
    {
        public int Count => rows.Length;
        public OcrQualitySample this[int index] { get { if (index == 128) cancellation.Cancel(); return rows[index]; } }
        public IEnumerator<OcrQualitySample> GetEnumerator() => ((IEnumerable<OcrQualitySample>)rows).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
