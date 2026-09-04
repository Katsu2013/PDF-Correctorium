using System.Globalization;

namespace PdfCorrectorium.Core.Analysis;

/// <summary>OCR品質分析に使用する、ページ上の1文字列領域の寸法と文字送りを表します。</summary>
public sealed record OcrQualitySample(
    int PageNumber,
    Guid RegionId,
    string Text,
    double Width,
    double Height,
    bool IsVertical,
    bool IsGeometryLocked,
    bool HasLockedCharacters,
    IReadOnlyList<double> CharacterAdvances)
{
    /// <summary>結合文字を1文字として数えたUnicodeテキスト要素数です。</summary>
    public int CharacterCount => new StringInfo(Text ?? string.Empty).LengthInTextElements;
    /// <summary>書字方向と直交する領域の太さです。</summary>
    public double LineThickness => IsVertical ? Width : Height;
    /// <summary>書字方向に沿った領域の長さです。</summary>
    public double WritingExtent => IsVertical ? Height : Width;
    /// <summary>領域全体または文字送りの一部が固定されているかを示します。</summary>
    public bool IsLocked => IsGeometryLocked || HasLockedCharacters;
}

/// <summary>同程度の寸法を持つOCR領域群から文字数の外れ値を探す条件です。</summary>
public sealed record OcrCharacterCountAnalysisOptions(
    double SizeTolerancePercent = 15,
    int MinimumPeerCount = 4,
    double CountRatioThreshold = 1.6);

/// <summary>文字数が周辺の標準より少ないか、多いかを表します。</summary>
public enum OcrCharacterCountAnomalyKind
{
    /// <summary>同程度の領域より認識文字数が少なすぎる候補です。</summary>
    TooFew,
    /// <summary>同程度の領域より認識文字数が多すぎる候補です。</summary>
    TooMany,
}

/// <summary>同程度の寸法を持つ領域群と比べ、文字数だけが外れているOCR領域です。</summary>
public sealed record OcrCharacterCountAnomaly(
    OcrQualitySample Sample,
    OcrCharacterCountAnomalyKind Kind,
    double ExpectedCharacterCount,
    int PeerCount,
    double CountRatio)
{
    /// <summary>候補が存在する1始まりのページ番号です。</summary>
    public int PageNumber => Sample.PageNumber;
    /// <summary>候補領域の不変IDです。</summary>
    public Guid RegionId => Sample.RegionId;
    /// <summary>現在のOCR文字列です。</summary>
    public string Text => Sample.Text;
    /// <summary>現在のUnicodeテキスト要素数です。</summary>
    public int CharacterCount => Sample.CharacterCount;
    /// <summary>候補領域の幅です。</summary>
    public double Width => Sample.Width;
    /// <summary>候補領域の高さです。</summary>
    public double Height => Sample.Height;
    /// <summary>縦書き領域かを示します。</summary>
    public bool IsVertical => Sample.IsVertical;
}

/// <summary>同じキーワードの正常例から、文字幅比率の外れ値を探す条件です。</summary>
public sealed record OcrKeywordWidthAnalysisOptions(
    string Keyword,
    bool MatchCase = false,
    double DeviationTolerancePercent = 20,
    int MinimumReferenceCount = 3);

/// <summary>同一キーワードの標準的な幅比率から外れた出現箇所です。</summary>
public sealed record OcrKeywordWidthCandidate(
    OcrQualitySample Sample,
    string Keyword,
    int StartIndex,
    int Length,
    double CurrentSpan,
    double ReferenceSpan,
    double CurrentRatio,
    double ReferenceRatio,
    double DeviationPercent)
{
    /// <summary>候補が存在する1始まりのページ番号です。</summary>
    public int PageNumber => Sample.PageNumber;
    /// <summary>候補領域の不変IDです。</summary>
    public Guid RegionId => Sample.RegionId;
    /// <summary>現在のOCR文字列です。</summary>
    public string Text => Sample.Text;
    /// <summary>領域または文字送りが固定され、補正対象外かを示します。</summary>
    public bool IsLocked => Sample.IsLocked;
}

/// <summary>キーワード幅分析の基準値と補正候補をまとめます。</summary>
public sealed record OcrKeywordWidthAnalysisResult(
    double ReferenceRatio,
    int OccurrenceCount,
    IReadOnlyList<OcrKeywordWidthCandidate> Candidates);

/// <summary>
/// 文書全体のOCR領域を統計的に比較し、文字数または同一語の幅比率が不自然な候補を抽出します。
/// </summary>
/// <remarks>
/// 書字方向と寸法が近い領域だけを比較し、見出し、本文、縦書きが不用意に混ざらないようにします。
/// 判定結果は自動修正の確定値ではなく、人が画像と照合するための候補です。
/// </remarks>
public sealed class OcrQualityAnalyzer
{
    /// <summary>同程度の幅・高さを持つ領域群の中央値から文字数だけ外れた候補を返します。</summary>
    /// <remarks>
    /// 書字方向と領域寸法が近い領域だけをピアとし、中央値を期待値に使います。
    /// そのため、見出しと本文など異なる用途の領域が同じ文書に混在していても、比較対象を不用意に混ぜません。
    /// </remarks>
    public IReadOnlyList<OcrCharacterCountAnomaly> FindCharacterCountAnomalies(
        IReadOnlyList<OcrQualitySample> samples,
        OcrCharacterCountAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(options);
        var sizeTolerance = Math.Clamp(options.SizeTolerancePercent, 1, 100);
        var minimumPeerCount = Math.Max(2, options.MinimumPeerCount);
        var ratioThreshold = Math.Max(1.05, options.CountRatioThreshold);
        var validSamples = samples.Where(IsValidSample).ToArray();
        var results = new List<OcrCharacterCountAnomaly>();

        foreach (var sample in validSamples)
        {
            var peers = validSamples
                .Where(candidate => candidate.RegionId != sample.RegionId &&
                                    candidate.IsVertical == sample.IsVertical &&
                                    RelativeDifferencePercent(candidate.Width, sample.Width) <= sizeTolerance &&
                                    RelativeDifferencePercent(candidate.Height, sample.Height) <= sizeTolerance)
                .ToArray();
            if (peers.Length < minimumPeerCount) continue;

            var expected = Median(peers.Select(peer => (double)peer.CharacterCount));
            if (expected <= 0 || Math.Abs(sample.CharacterCount - expected) < 2) continue;
            var ratio = sample.CharacterCount / expected;
            if (ratio <= 1d / ratioThreshold)
                results.Add(new(sample, OcrCharacterCountAnomalyKind.TooFew, expected, peers.Length, ratio));
            else if (ratio >= ratioThreshold)
                results.Add(new(sample, OcrCharacterCountAnomalyKind.TooMany, expected, peers.Length, ratio));
        }

        return results
            .OrderByDescending(result => Math.Abs(Math.Log(Math.Max(0.0001, result.CountRatio))))
            .ThenBy(result => result.PageNumber)
            .ToArray();
    }

    /// <summary>同じキーワードの出現幅を行の太さで正規化し、中央値から外れた候補を返します。</summary>
    /// <remarks>横書きと縦書きは同じキーワードでも文字送りの基準軸が異なるため、別々の母集団で幅を比較します。</remarks>
    public OcrKeywordWidthAnalysisResult AnalyzeKeywordWidths(
        IReadOnlyList<OcrQualitySample> samples,
        OcrKeywordWidthAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(options);
        var keyword = options.Keyword?.Trim() ?? string.Empty;
        if (keyword.Length == 0) return new(0, 0, []);
        var comparison = options.MatchCase
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;
        var occurrences = new List<KeywordOccurrence>();

        foreach (var sample in samples.Where(IsValidSample))
        {
            foreach (var startIndex in FindOccurrences(sample.Text, keyword, comparison))
            {
                if (!TryMeasureTextRange(sample, startIndex, keyword.Length, out var span)) continue;
                occurrences.Add(new(sample, startIndex, keyword.Length, span, span / sample.LineThickness));
            }
        }

        if (occurrences.Count < Math.Max(2, options.MinimumReferenceCount))
            return new(0, occurrences.Count, []);

        // Horizontal and vertical writing can legitimately use different glyph metrics.
        // Establish the reference independently for each direction so that a vertical
        // occurrence is never stretched to match horizontal text (or vice versa).
        var minimumReferenceCount = Math.Max(2, options.MinimumReferenceCount);
        var referenceRatios = occurrences
            .GroupBy(value => value.Sample.IsVertical)
            .Where(group => group.Count() >= minimumReferenceCount)
            .ToDictionary(group => group.Key, group => Median(group.Select(value => value.Ratio)));
        if (referenceRatios.Count == 0)
            return new(0, occurrences.Count, []);

        var referenceRatio = Median(referenceRatios.Values);
        var tolerance = Math.Clamp(options.DeviationTolerancePercent, 1, 500);
        var candidates = occurrences
            .Where(value => referenceRatios.ContainsKey(value.Sample.IsVertical))
            .Select(value =>
            {
                var directionalReferenceRatio = referenceRatios[value.Sample.IsVertical];
                var deviation = Math.Abs(value.Ratio / directionalReferenceRatio - 1d) * 100d;
                return new OcrKeywordWidthCandidate(
                    value.Sample,
                    keyword,
                    value.StartIndex,
                    value.Length,
                    value.Span,
                    directionalReferenceRatio * value.Sample.LineThickness,
                    value.Ratio,
                    directionalReferenceRatio,
                    deviation);
            })
            .Where(candidate => candidate.DeviationPercent >= tolerance)
            .OrderByDescending(candidate => candidate.DeviationPercent)
            .ThenBy(candidate => candidate.PageNumber)
            .ToArray();
        return new(referenceRatio, occurrences.Count, candidates);
    }

    private static bool IsValidSample(OcrQualitySample sample) =>
        sample.CharacterCount > 0 &&
        double.IsFinite(sample.Width) && sample.Width > 0 &&
        double.IsFinite(sample.Height) && sample.Height > 0 &&
        double.IsFinite(sample.LineThickness) && sample.LineThickness > 0 &&
        double.IsFinite(sample.WritingExtent) && sample.WritingExtent > 0;

    private static double RelativeDifferencePercent(double left, double right) =>
        Math.Abs(left - right) / Math.Max(Math.Max(Math.Abs(left), Math.Abs(right)), 0.0001) * 100d;

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Where(double.IsFinite).OrderBy(value => value).ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2d : ordered[middle];
    }

    private static IReadOnlyList<int> FindOccurrences(string text, string keyword, StringComparison comparison)
    {
        var results = new List<int>();
        for (var index = 0; index <= text.Length - keyword.Length;)
        {
            var found = text.IndexOf(keyword, index, comparison);
            if (found < 0) break;
            results.Add(found);
            index = found + Math.Max(1, keyword.Length);
        }
        return results;
    }

    private static bool TryMeasureTextRange(OcrQualitySample sample, int startIndex, int length, out double span)
    {
        span = 0;
        var offsets = StringInfo.ParseCombiningCharacters(sample.Text);
        if (offsets.Length == 0 || startIndex < 0 || length <= 0 || startIndex + length > sample.Text.Length)
            return false;
        var firstElement = Array.IndexOf(offsets, startIndex);
        if (firstElement < 0) return false;
        var rangeEnd = startIndex + length;
        var lastExclusive = firstElement;
        while (lastExclusive < offsets.Length && offsets[lastExclusive] < rangeEnd) lastExclusive++;
        if (lastExclusive <= firstElement) return false;

        var advances = NormalizeAdvances(sample, offsets.Length);
        span = advances.Skip(firstElement).Take(lastExclusive - firstElement).Sum();
        return double.IsFinite(span) && span > 0;
    }

    private static IReadOnlyList<double> NormalizeAdvances(OcrQualitySample sample, int characterCount)
    {
        if (sample.CharacterAdvances.Count == characterCount &&
            sample.CharacterAdvances.All(value => double.IsFinite(value) && value > 0))
            return sample.CharacterAdvances;
        var equalAdvance = sample.WritingExtent / Math.Max(1, characterCount);
        return Enumerable.Repeat(equalAdvance, characterCount).ToArray();
    }

    private sealed record KeywordOccurrence(
        OcrQualitySample Sample,
        int StartIndex,
        int Length,
        double Span,
        double Ratio);
}
