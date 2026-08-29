using System.Globalization;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App.Services;

/// <summary>文字の一括自動調整前に適用する、任意の行領域補正を表します。</summary>
public sealed record BatchCharacterAdjustmentOptions(
    bool ExpandLeadingOpeningQuote,
    bool ExpandTrailingPunctuationOrClosingQuote,
    bool NormalizeSimilarLineThicknesses,
    bool ExpandNarrowEdgeCharacters = true,
    bool AddLineEdgeSafetyMargin = true,
    int NeighborLineCount = 2,
    double SimilarityTolerance = 0.10,
    double EdgeExpansionRatio = 0.50,
    double EdgeSafetyMarginRatio = 0.05);

/// <summary>一括前処理で変更した行数を処理別に保持します。</summary>
public sealed record BatchCharacterPreprocessingResult(
    int LeadingExpansionCount,
    int TrailingExpansionCount,
    int NormalizedThicknessCount)
{
    /// <summary>前処理によって行った変更の合計件数です。</summary>
    public int TotalChangeCount => LeadingExpansionCount + TrailingExpansionCount + NormalizedThicknessCount;
}

/// <summary>
/// 対象行の文字送りを保持したまま、鍵括弧・句読点用の余白と近接行の太さを補正します。
/// </summary>
public static class BatchCharacterAdjustmentPreprocessor
{
    /// <summary>
    /// 字面が細いため、OCR の外接矩形だけでは行端側の余白を失いやすい文字です。
    /// 文字列ではなくテキスト要素単位で照合し、結合文字を誤判定しないようにします。
    /// </summary>
    private static readonly HashSet<string> NarrowEdgeCharacters = new(StringComparer.Ordinal)
    {
        "・", "･", "·", "•", "‧", "∙", "⋅",
        "|", "｜", "¦", "‖",
        "I", "L", "i", "l",
        ":", ";", "：", "；",
    };

    private const string OpeningQuotes = "「『";
    private const string TrailingPunctuationOrQuotes = "、。，．,.;:！？!?」』";

    /// <summary>選択された行へ、有効な前処理を一括適用します。</summary>
    public static BatchCharacterPreprocessingResult Apply(
        IReadOnlyList<OverlayRegionViewModel> targets,
        IReadOnlyList<OverlayRegionViewModel> pageRegions,
        BatchCharacterAdjustmentOptions options,
        double pageWidth,
        double pageHeight)
    {
        var leadingExpansionCount = 0;
        var trailingExpansionCount = 0;
        var normalizedThicknessCount = 0;

        if (options.NormalizeSimilarLineThicknesses)
        {
            // 全対象の変更前寸法を使うことで、処理順による平均値の連鎖変化を防ぐ。
            var metrics = pageRegions
                .Where(region => !region.IsDeleted && region.LineThickness > 0)
                .Select(LineMetric.FromRegion)
                .ToArray();
            var targetThicknesses = targets
                .Where(CanApplyGeometryPreprocessing)
                .ToDictionary(
                region => region,
                region => CalculateNeighborAverage(region, metrics, options));

            foreach (var (region, targetThickness) in targetThicknesses)
            {
                if (targetThickness is double thickness &&
                    region.SetLineThicknessPreservingAdvances(thickness, pageWidth, pageHeight))
                    normalizedThicknessCount++;
            }
        }

        foreach (var region in targets)
        {
            // 文字単位のロックが1つでもある行は、行領域を伸縮すると固定文字の
            // ページ上の位置まで変わるため、幾何前処理を行わない。
            if (!CanApplyGeometryPreprocessing(region)) continue;

            var elements = GetTextElements(region.Text);
            if (elements.Count == 0) continue;

            // OCR結果の前後に空白が含まれていても、見た目上の行頭・行末文字で判定する。
            var meaningfulElements = elements.Where(element => !string.IsNullOrWhiteSpace(element)).ToArray();
            if (meaningfulElements.Length == 0) continue;

            var hasLeadingSpecialExpansion =
                (options.ExpandLeadingOpeningQuote && IsOpeningQuote(meaningfulElements[0])) ||
                (options.ExpandNarrowEdgeCharacters && IsNarrowEdgeCharacter(meaningfulElements[0]));
            var hasTrailingSpecialExpansion =
                (options.ExpandTrailingPunctuationOrClosingQuote &&
                 IsTrailingPunctuationOrQuote(meaningfulElements[^1])) ||
                (options.ExpandNarrowEdgeCharacters && IsNarrowEdgeCharacter(meaningfulElements[^1]));

            // 全行へ付ける5%の安全余白と、特定文字向けの50%補正は加算しない。
            // 同じ行端では大きい方を採用し、55%へ過剰拡張されることを防ぐ。
            var leadingExpansionRatio = hasLeadingSpecialExpansion
                ? options.EdgeExpansionRatio
                : options.AddLineEdgeSafetyMargin
                    ? options.EdgeSafetyMarginRatio
                    : 0;
            var trailingExpansionRatio = hasTrailingSpecialExpansion
                ? options.EdgeExpansionRatio
                : options.AddLineEdgeSafetyMargin
                    ? options.EdgeSafetyMarginRatio
                    : 0;
            if (leadingExpansionRatio <= 0 && trailingExpansionRatio <= 0) continue;

            if (region.ExpandWritingBoundsPreservingAdvances(
                    region.LineThickness * leadingExpansionRatio,
                    region.LineThickness * trailingExpansionRatio,
                    pageWidth,
                    pageHeight))
            {
                if (leadingExpansionRatio > 0) leadingExpansionCount++;
                if (trailingExpansionRatio > 0) trailingExpansionCount++;
            }
        }

        return new BatchCharacterPreprocessingResult(
            leadingExpansionCount,
            trailingExpansionCount,
            normalizedThicknessCount);
    }

    private static double? CalculateNeighborAverage(
        OverlayRegionViewModel target,
        IReadOnlyList<LineMetric> metrics,
        BatchCharacterAdjustmentOptions options)
    {
        var own = metrics.FirstOrDefault(metric => ReferenceEquals(metric.Region, target));
        if (own is null || !CanApplyGeometryPreprocessing(target)) return null;

        var candidates = metrics
            .Where(metric => !ReferenceEquals(metric.Region, target) &&
                             metric.IsVertical == own.IsVertical &&
                             RotationDifference(metric.RotationDegrees, own.RotationDegrees) <= 5 &&
                             MainAxisOverlapRatio(own, metric) >= 0.25 &&
                             RelativeDifference(own.Thickness, metric.Thickness) < options.SimilarityTolerance)
            .ToArray();

        var before = candidates
            .Where(metric => metric.CrossCenter < own.CrossCenter)
            .OrderByDescending(metric => metric.CrossCenter)
            .Take(Math.Clamp(options.NeighborLineCount, 1, 2));
        var after = candidates
            .Where(metric => metric.CrossCenter > own.CrossCenter)
            .OrderBy(metric => metric.CrossCenter)
            .Take(Math.Clamp(options.NeighborLineCount, 1, 2));
        var neighbors = before.Concat(after).ToArray();
        if (neighbors.Length == 0) return null;

        return neighbors.Append(own).Average(metric => metric.Thickness);
    }

    private static IReadOnlyList<string> GetTextElements(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var indexes = StringInfo.ParseCombiningCharacters(text);
        return indexes.Select((start, index) =>
        {
            var end = index + 1 < indexes.Length ? indexes[index + 1] : text.Length;
            return text[start..end];
        }).ToArray();
    }

    /// <summary>
    /// 行領域の形を変更しても、利用者が固定した行または文字を動かさないかを判定します。
    /// </summary>
    private static bool CanApplyGeometryPreprocessing(OverlayRegionViewModel region) =>
        !region.IsGeometryLocked && !region.HasLockedCharacters;

    private static bool IsOpeningQuote(string textElement) =>
        textElement.Length > 0 && OpeningQuotes.Contains(textElement[0]);

    private static bool IsTrailingPunctuationOrQuote(string textElement) =>
        textElement.Length > 0 && TrailingPunctuationOrQuotes.Contains(textElement[0]);

    private static bool IsNarrowEdgeCharacter(string textElement) =>
        NarrowEdgeCharacters.Contains(textElement);

    private static double RelativeDifference(double left, double right) =>
        Math.Abs(left - right) / Math.Max(left, right);

    private static double RotationDifference(double left, double right)
    {
        var difference = Math.Abs((left - right) % 360);
        return Math.Min(difference, 360 - difference);
    }

    private static double MainAxisOverlapRatio(LineMetric left, LineMetric right)
    {
        var overlap = Math.Max(0, Math.Min(left.MainEnd, right.MainEnd) - Math.Max(left.MainStart, right.MainStart));
        var shorter = Math.Min(left.MainEnd - left.MainStart, right.MainEnd - right.MainStart);
        return shorter <= 0 ? 0 : overlap / shorter;
    }

    private sealed record LineMetric(
        OverlayRegionViewModel Region,
        bool IsVertical,
        double Thickness,
        double CrossCenter,
        double MainStart,
        double MainEnd,
        double RotationDegrees)
    {
        public static LineMetric FromRegion(OverlayRegionViewModel region) =>
            region.IsVertical
                ? new(region, true, region.Width, region.Left + region.Width / 2,
                    region.Top, region.Top + region.Height, region.RotationDegrees)
                : new(region, false, region.Height, region.Top + region.Height / 2,
                    region.Left, region.Left + region.Width, region.RotationDegrees);
    }
}
