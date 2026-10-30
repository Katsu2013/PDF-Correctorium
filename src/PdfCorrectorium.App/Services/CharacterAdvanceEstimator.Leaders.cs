namespace PdfCorrectorium.App.Services;

public static partial class CharacterAdvanceEstimator
{
    private sealed record DetectedLeader(int TextStart, int TextCount, int ImageCount, double InkThreshold);
    private readonly record struct InkRun(int Start, int End, double Peak)
    {
        public double Center => (Start + End - 1) / 2d;
    }
    private sealed record RasterLeader(InkRun[] Dots, double Pitch);

    /// <summary>
    /// Detects complete periodic raster runs before assigning OCR elements to them.
    /// A dot-count discrepancy must not invalidate the neighbouring text anchors;
    /// it is reported to the user without rewriting the OCR string.
    /// </summary>
    private static bool TryFindLeaderBoundaries(
        double[] energy, double[] rawEnergy, string[] elements, int crossLength,
        CharacterAdvanceEstimationOptions options,
        out int[] boundaries, out DetectedLeader? leader)
    {
        boundaries = [];
        leader = null;
        var threshold = Math.Max(0.008, rawEnergy.Max() * 0.04);
        var rasterLeaders = DetectRasterLeaders(rawEnergy, crossLength, threshold);
        for (var textStart = 0; textStart < elements.Length;)
        {
            var textEnd = textStart + 1;
            while (textEnd < elements.Length && elements[textEnd] == elements[textStart]) textEnd++;
            var count = textEnd - textStart;
            if (count < 8 || elements[textStart] is not ("." or "．" or "·" or "・"))
            { textStart = textEnd; continue; }

            // Require an unambiguous, sufficiently similar long raster run. Large
            // count discrepancies or competing candidates remain ordinary fits.
            var plausible = rasterLeaders.Where(candidate =>
                candidate.Dots.Length >= Math.Max(8, count * 0.65) &&
                candidate.Dots.Length <= count * 1.35).ToArray();
            if (plausible.Length != 1) { textStart = textEnd; continue; }
            var raster = plausible[0];
            var dots = raster.Dots;
            var pixelStart = (int)Math.Round(dots[0].Center - raster.Pitch / 2, MidpointRounding.AwayFromZero);
            var pixelEnd = (int)Math.Round(dots[^1].Center + raster.Pitch / 2, MidpointRounding.AwayFromZero);
            if (textStart == 0) pixelStart = 0;
            if (textEnd == elements.Length) pixelEnd = energy.Length;
            if (pixelStart < textStart * 2 || energy.Length - pixelEnd < (elements.Length - textEnd) * 2 ||
                pixelEnd - pixelStart < count * 2)
            { textStart = textEnd; continue; }
            try
            {
                var candidate = new int[elements.Length + 1];
                var prefixEnergy = textStart > 0 ? NormalizeProjection(rawEnergy[..pixelStart]) : [];
                var suffixEnergy = textEnd < elements.Length ? NormalizeProjection(rawEnergy[pixelEnd..]) : [];
                if (textStart > 0)
                    FindSegmentBoundaries(prefixEnergy, elements[..textStart], crossLength, options).CopyTo(candidate, 0);
                candidate[textStart] = pixelStart;
                for (var index = 1; index < count; index++)
                    candidate[textStart + index] = (int)Math.Round(dots.Length == count
                        ? (dots[index - 1].Center + dots[index].Center) / 2
                        : pixelStart + (pixelEnd - pixelStart) * index / (double)count,
                        MidpointRounding.AwayFromZero);
                candidate[textEnd] = pixelEnd;
                if (textEnd < elements.Length)
                {
                    var suffix = FindSegmentBoundaries(suffixEnergy, elements[textEnd..], crossLength, options);
                    for (var index = 0; index < suffix.Length; index++) candidate[textEnd + index] = pixelEnd + suffix[index];
                }
                if (candidate.Zip(candidate.Skip(1)).Any(pair => pair.First >= pair.Second))
                { textStart = textEnd; continue; }
                prefixEnergy.CopyTo(energy, 0);
                NormalizeProjection(rawEnergy[pixelStart..pixelEnd]).CopyTo(energy, pixelStart);
                suffixEnergy.CopyTo(energy, pixelEnd);
                boundaries = candidate;
                leader = new DetectedLeader(textStart, count, dots.Length, threshold);
                return true;
            }
            catch (InvalidOperationException)
            {
                // Do not accept a visually periodic run when its neighbours cannot
                // be fitted safely. No partial result is applied to the document.
            }
            textStart = textEnd;
        }
        return false;
    }

    private static List<RasterLeader> DetectRasterLeaders(double[] energy, int crossLength, double threshold)
    {
        var runs = new List<InkRun>();
        for (var position = 0; position < energy.Length;)
        {
            if (energy[position] < threshold) { position++; continue; }
            var start = position;
            var peak = 0d;
            while (position < energy.Length && energy[position] >= threshold) peak = Math.Max(peak, energy[position++]);
            // Keep large components too: skipping letters could join unrelated dots.
            runs.Add(new InkRun(start, position, peak));
        }
        var result = new List<RasterLeader>();
        const int minimumCount = 8;
        for (var first = 0; first + minimumCount <= runs.Count; first++)
        {
            var pitch = (runs[first + minimumCount - 1].Center - runs[first].Center) / (minimumCount - 1);
            if (pitch < 3 || pitch > crossLength * 1.25) continue;
            var tolerance = Math.Max(1, pitch * 0.18);
            var seedValid = true;
            for (var index = first; index < first + minimumCount; index++)
                if (!IsLeaderDot(runs[index], pitch, crossLength) ||
                    index > first && Math.Abs(runs[index].Center - runs[index - 1].Center - pitch) > tolerance)
                { seedValid = false; break; }
            if (!seedValid) continue;
            // Accept maximal runs, never an OCR-sized window inside a longer run.
            if (first > 0 && IsLeaderDot(runs[first - 1], pitch, crossLength) &&
                Math.Abs(runs[first].Center - runs[first - 1].Center - pitch) <= tolerance) continue;
            var end = first + minimumCount;
            while (end < runs.Count && IsLeaderDot(runs[end], pitch, crossLength) &&
                   Math.Abs(runs[end].Center - runs[end - 1].Center - pitch) <= tolerance) end++;
            // A missing/irregular dot must not turn one leader into an accepted
            // partial run. Nearby faint components beyond a broken pitch make the
            // outer extent ambiguous; leave the entire candidate to the fallback.
            if ((first > 0 && IsLeaderDot(runs[first - 1], pitch, crossLength) &&
                 runs[first].Center - runs[first - 1].Center <= pitch * 3) ||
                (end < runs.Count && IsLeaderDot(runs[end], pitch, crossLength) &&
                 runs[end].Center - runs[end - 1].Center <= pitch * 3))
            { first = end - 1; continue; }
            var dots = runs.GetRange(first, end - first).ToArray();
            result.Add(new RasterLeader(dots, (dots[^1].Center - dots[0].Center) / (dots.Length - 1)));
            first = end - 1;
        }
        return result;
    }

    private static bool IsLeaderDot(InkRun run, double pitch, int crossLength) =>
        run.Peak <= 0.32 && run.End - run.Start <= Math.Min(crossLength * 0.45, pitch * 0.55);

    private static int[] FindSegmentBoundaries(double[] energy, string[] elements, int crossLength,
        CharacterAdvanceEstimationOptions options) =>
        elements.Length == 1 ? [0, energy.Length] : FindBoundaries(energy, elements, crossLength, options);
}
