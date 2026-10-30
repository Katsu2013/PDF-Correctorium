using System.Globalization;
using System.Text;

namespace PdfCorrectorium.Core.Analysis;

public sealed partial class OcrQualityAnalyzer
{
    // Heuristic width priors, not font metrics: calibrate against independent
    // book-local lines and decline to classify sparse or unstable references.
    private const double MinimumReferenceDensity = 0.5;
    private const double MaximumReferenceDensity = 2;
    private const double MaximumCentralSpread = 1.5;

    private static IReadOnlyList<OcrCharacterCountAnomaly> FindDensityAnomalies(
        IReadOnlyList<OcrQualitySample> samples, OcrCharacterCountAnalysisOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var tolerance = double.IsFinite(options.SizeTolerancePercent) ? Math.Clamp(options.SizeTolerancePercent, 1, 100) : 15;
        var threshold = double.IsFinite(options.CountRatioThreshold) ? Math.Max(1.05, options.CountRatioThreshold) : 1.6;
        var minimumPeers = Math.Max(2, options.MinimumPeerCount);
        var prepared = new List<DensityObservation>(samples.Count);
        var ids = new HashSet<Guid>();
        for (var i = 0; i < samples.Count; i++)
        {
            if ((i & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
            var sample = samples[i];
            if (!IsValidSample(sample) || sample.Text.AsSpan().Trim().IndexOfAny('\r', '\n') >= 0 || !ids.Add(sample.RegionId)) continue;
            var units = MeasureWidthUnits(sample.Text, sample.IsVertical, cancellationToken);
            var shape = sample.WritingExtent / sample.LineThickness;
            var density = units / shape;
            if (units <= 0 || !double.IsFinite(shape) || shape <= 0 || !double.IsFinite(density) || density <= 0) continue;
            prepared.Add(new(sample, sample.CharacterCount, density));
        }
        var results = new List<OcrCharacterCountAnomaly>();
        foreach (var direction in prepared.GroupBy(item => item.Sample.IsVertical))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = direction.OrderBy(item => item.Sample.LineThickness).ToArray();
            var values = rows.Where(item => item.CanCalibrate).Select(item => item.Density).Distinct().Order().ToArray();
            if (values.Length == 0) continue;
            var ranks = new DensityRanks(values);
            var lower = 0;
            var upper = 0;
            var retained = 1 - tolerance / 100;
            for (var i = 0; i < rows.Length; i++)
            {
                if ((i & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
                var row = rows[i];
                // Same max-denominator relative tolerance, now applied only to
                // line thickness. Length is normalized rather than matched.
                var minimum = row.Sample.LineThickness * retained;
                var maximum = retained > 0 ? row.Sample.LineThickness / retained : double.PositiveInfinity;
                while (upper < rows.Length && rows[upper].Sample.LineThickness <= maximum)
                {
                    if (rows[upper].CanCalibrate) ranks.Add(rows[upper].Density, 1);
                    upper++;
                }
                while (lower < upper && rows[lower].Sample.LineThickness < minimum)
                {
                    if (rows[lower].CanCalibrate) ranks.Add(rows[lower].Density, -1);
                    lower++;
                }
                if (row.CanCalibrate) ranks.Add(row.Density, -1);
                if (ranks.Count >= minimumPeers)
                {
                    var firstQuartile = ranks.Quantile(.25);
                    var thirdQuartile = ranks.Quantile(.75);
                    if (thirdQuartile <= firstQuartile * MaximumCentralSpread)
                    {
                        var reference = ranks.Quantile(.5);
                        var ratio = row.Density / reference;
                        // Estimate for this row's own character mix, never the
                        // raw character count of its English/CJK neighbours.
                        var expected = row.CharacterCount / ratio;
                        if (double.IsFinite(expected) && Math.Abs(row.CharacterCount - expected) >= 2)
                        {
                            if (ratio <= 1 / threshold)
                                results.Add(new(row.Sample, OcrCharacterCountAnomalyKind.TooFew, expected, ranks.Count, ratio));
                            else if (ratio >= threshold)
                                results.Add(new(row.Sample, OcrCharacterCountAnomalyKind.TooMany, expected, ranks.Count, ratio));
                        }
                    }
                }
                if (row.CanCalibrate) ranks.Add(row.Density, 1);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return results.OrderByDescending(result => Math.Abs(Math.Log(result.CountRatio)))
            .ThenBy(result => result.PageNumber).ToArray();
    }

    private static double MeasureWidthUnits(string text, bool vertical, CancellationToken cancellationToken)
    {
        text = text.Trim(); // padding is not line content; interior spaces remain
        var offsets = StringInfo.ParseCombiningCharacters(text);
        var total = 0d;
        for (var i = 0; i < offsets.Length; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var end = i + 1 < offsets.Length ? offsets[i + 1] : text.Length;
            var width = 0d;
            // One grapheme, including combining marks/dakuten/variation selectors
            // or joined emoji, contributes at most one cell estimate.
            foreach (var rune in text.AsSpan(offsets[i], end - offsets[i]).EnumerateRunes())
                width = Math.Max(width, RuneWidth(rune, vertical));
            total += width;
        }
        return total;
    }

    private static double RuneWidth(Rune rune, bool vertical)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or UnicodeCategory.Format or UnicodeCategory.Control) return 0;
        var value = rune.Value;
        if (value == 0x3000) return 1;
        if (Rune.IsWhiteSpace(rune)) return vertical ? .5 : .33;
        // CJK radicals/kana/ideographs/Hangul/fullwidth forms, supplementary
        // kana/ideographs and emoji. These are priors, not exact font metrics.
        if (value is >= 0x1100 and <= 0x11FF or >= 0x2E80 and <= 0xA4CF or
            >= 0xA960 and <= 0xA97F or >= 0xAC00 and <= 0xD7FF or
            >= 0xF900 and <= 0xFAFF or >= 0xFE10 and <= 0xFE19 or >= 0xFE30 and <= 0xFE6F or
            >= 0xFF01 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6 or
            >= 0x1B000 and <= 0x1B2FF or >= 0x1F000 and <= 0x1FAFF or >= 0x20000 and <= 0x3FFFD) return 1;
        if (value is >= 0xFF61 and <= 0xFF9F) return vertical ? 1 : .5;
        // Upright vertical cells are not horizontal Latin glyph advances.
        // Tate-chu-yoko and sideways runs cannot be resolved from this model.
        if (vertical) return 1;
        if (value is 'i' or 'l' or 'I' or '.' or ',' or ':' or ';' or '!' or '\'' or '`' or '|') return .3;
        if (value is 'M' or 'W' or 'm' or 'w') return .9;
        return .6;
    }

    private sealed record DensityObservation(OcrQualitySample Sample, int CharacterCount, double Density)
    {
        public bool CanCalibrate => Density is >= MinimumReferenceDensity and <= MaximumReferenceDensity;
    }

    /// <summary>Sliding-window order statistics: O(log n) updates/quantiles and O(n) memory.</summary>
    private sealed class DensityRanks(double[] values)
    {
        private readonly int[] _tree = new int[values.Length + 1];
        public int Count { get; private set; }
        public void Add(double density, int delta)
        {
            Count += delta;
            for (var index = Array.BinarySearch(values, density) + 1; index < _tree.Length; index += index & -index)
                _tree[index] += delta;
        }
        public double Quantile(double probability)
        {
            var position = probability * (Count - 1);
            var lower = (int)position;
            var first = AtRank(lower + 1);
            return lower + 1 < Count ? first + (AtRank(lower + 2) - first) * (position - lower) : first;
        }
        private double AtRank(int order)
        {
            var index = 0;
            var step = 1;
            while (step <= values.Length / 2) step <<= 1;
            for (; step > 0; step >>= 1)
            {
                var next = index + step;
                if (next >= _tree.Length || _tree[next] >= order) continue;
                index = next;
                order -= _tree[next];
            }
            return values[index];
        }
    }
}
