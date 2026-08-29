using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// 画像から推定した各文字の送り量、先頭余白、および推定品質を表します。
/// </summary>
/// <param name="Advances">書字方向に沿った各Unicodeテキスト要素の送り量。</param>
/// <param name="LeadingOffset">領域先頭から最初の文字セルまでの余白。</param>
/// <param name="Extent">文字列全体が占める書字方向の長さ。</param>
/// <param name="Confidence">画像特徴と推定境界の一致度を示す0～1の値。</param>
/// <param name="Message">推定結果または信頼度低下の理由を示す説明。</param>
/// <param name="InkCoverages">文字セルごとの前景画素占有率。</param>
public sealed record CharacterAdvanceEstimationResult(
    IReadOnlyList<double> Advances,
    double LeadingOffset,
    double Extent,
    double Confidence,
    string Message,
    IReadOnlyList<double> InkCoverages);

/// <summary>
/// 文字送り推定で許容する字形比率と、画像特徴・字種事前知識の重みを指定します。
/// </summary>
/// <param name="MinimumAspectRatio">1文字セルに許容する最小縦横比。</param>
/// <param name="MaximumAspectRatio">1文字セルに許容する最大縦横比。</param>
/// <param name="UniformityStrength">隣接文字幅を均一に近づける制約の強さ。</param>
/// <param name="InkCoverageRequirement">有効な文字セルとみなす最小前景画素率。</param>
/// <param name="GlyphPriorStrength">句読点や仮名など、字種別の幅事前知識を反映する強さ。</param>
public sealed record CharacterAdvanceEstimationOptions(
    double MinimumAspectRatio = 0.20,
    double MaximumAspectRatio = 1.65,
    double UniformityStrength = 0.35,
    double InkCoverageRequirement = 0.12,
    double GlyphPriorStrength = 0.70)
{
    /// <summary>
    /// 各設定値を推定器が受け付ける安全な範囲へ収めたコピーを返します。
    /// </summary>
    public CharacterAdvanceEstimationOptions Normalize() => this with
    {
        MinimumAspectRatio = Math.Clamp(MinimumAspectRatio, 0.05, 0.60),
        MaximumAspectRatio = Math.Clamp(MaximumAspectRatio, 0.75, 4.00),
        UniformityStrength = Math.Clamp(UniformityStrength, 0, 1),
        InkCoverageRequirement = Math.Clamp(InkCoverageRequirement, 0.02, 0.50),
        GlyphPriorStrength = Math.Clamp(GlyphPriorStrength, 0, 1),
    };
}

/// <summary>
/// Estimates per-character advances from the rasterized page image. The estimator rectifies
/// rotated regions, projects foreground contrast onto the writing axis, and chooses the set
/// of low-energy boundaries that best fits the known number of Unicode text elements.
/// </summary>
public static class CharacterAdvanceEstimator
{
    /// <summary>
    /// OCR文字列の文字数とページ画像の濃淡を使い、書字方向に沿う文字送りを推定します。
    /// </summary>
    /// <param name="source">OCR領域を含むページのプレビュー画像。</param>
    /// <param name="region">文字列、位置、回転、書字方向を保持する対象領域。</param>
    /// <param name="options">推定パラメーター。省略時は既定値を使用します。</param>
    /// <returns>領域の長さへ正規化された文字送りと信頼度。</returns>
    /// <exception cref="InvalidOperationException">
    /// 対象が短すぎる、画像と背景の差が不足するなど、安定した境界を求められない場合。
    /// </exception>
    public static CharacterAdvanceEstimationResult Estimate(
        BitmapSource source,
        OverlayRegionViewModel region,
        CharacterAdvanceEstimationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(region);
        options = (options ?? new CharacterAdvanceEstimationOptions()).Normalize();

        var elementIndexes = StringInfo.ParseCombiningCharacters(region.Text);
        var elements = new string[elementIndexes.Length];
        for (var index = 0; index < elementIndexes.Length; index++)
        {
            var end = index + 1 < elementIndexes.Length ? elementIndexes[index + 1] : region.Text.Length;
            elements[index] = region.Text[elementIndexes[index]..end];
        }
        var elementCount = elements.Length;
        if (elementCount < 2)
            throw new InvalidOperationException("2文字以上のOCR領域を選択してください。");

        // 推定処理は、回転したOCR枠をいったん水平または垂直なローカル画像へ戻して行います。
        var bitmap = ConvertToBgra32(source);
        var localWidth = Math.Max(2, (int)Math.Round(region.Width));
        var localHeight = Math.Max(2, (int)Math.Round(region.Height));
        // primaryLengthは文字が進む方向、crossLengthは文字列の太さ方向の画素数です。
        var primaryLength = region.IsVertical ? localHeight : localWidth;
        var crossLength = region.IsVertical ? localWidth : localHeight;
        if (primaryLength < elementCount * 2 || crossLength < 2)
            throw new InvalidOperationException("文字数に対してOCR領域が小さすぎるため、自動判別できません。");

        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var rectified = SampleRectifiedRegion(pixels, bitmap.PixelWidth, bitmap.PixelHeight, region, localWidth, localHeight);
        var background = EstimateDominantColor(rectified, localWidth, localHeight);
        // projectionは書字方向の各位置に、背景と異なる画素がどれだけ存在するかを集計した配列です。
        var projection = BuildForegroundProjection(rectified, localWidth, localHeight, region.IsVertical, background);
        var normalized = NormalizeProjection(projection);
        if (normalized.Max() - normalized.Min() < 0.08)
            throw new InvalidOperationException("画像内の文字と背景の差が小さいため、文字境界を判別できませんでした。");

        // contentSpanはOCR枠のうち、実際に文字画素が存在すると推定した先頭から末尾までの範囲です。
        var contentSpan = FindContentSpan(normalized, elementCount, crossLength);
        // 小さな端余白は通常の字面にも含まれるため、枠が明確に過大な場合だけ外端を詰めます。
        if (contentSpan.End - contentSpan.Start >= primaryLength * 0.94)
            contentSpan = (0, primaryLength);
        var analysisEnergy = normalized[contentSpan.Start..contentSpan.End];
        var boundaries = FindBoundaries(analysisEnergy, elements, crossLength, options);
        var pixelAdvances = new double[elementCount];
        for (var index = 0; index < elementCount; index++)
            pixelAdvances[index] = boundaries[index + 1] - boundaries[index];

        // 画像解析は整数画素で行うため、結果を編集画面上の正確な小数座標へ戻します。
        var targetExtent = region.IsVertical ? region.Height : region.Width;
        var pixelScale = targetExtent / primaryLength;
        var estimatedExtent = (contentSpan.End - contentSpan.Start) * pixelScale;
        var scale = estimatedExtent / pixelAdvances.Sum();
        var advances = pixelAdvances.Select(value => Math.Max(1, value * scale)).ToArray();
        var correction = estimatedExtent / advances.Sum();
        for (var index = 0; index < advances.Length; index++) advances[index] *= correction;

        var confidence = CalculateConfidence(analysisEnergy, boundaries);
        var inkCoverages = CalculateInkCoverages(analysisEnergy, boundaries);
        var leadingOffset = contentSpan.Start * pixelScale;
        var adjustedExtent = estimatedExtent < targetExtent * 0.94;
        var message = confidence switch
        {
            >= 0.72 => $"画像から文字幅を推定しました（信頼度 {confidence:P0}）。",
            >= 0.48 => $"画像から文字幅を推定しました（信頼度 {confidence:P0}）。境界を確認してください。",
            _ => $"画像から文字幅を推定しましたが、重なりや背景の影響が考えられます（信頼度 {confidence:P0}）。必ず境界を確認してください。",
        };
        if (adjustedExtent) message += " 画像中の実文字範囲に合わせて行端も補正しました。";
        if (options.GlyphPriorStrength > 0)
            message += " OCR文字種による字幅補正を適用しました。";
        var emptyNonWhitespace = elements
            .Select((element, index) => (element, index))
            .Count(item => !IsWhitespace(item.element) &&
                           inkCoverages[item.index] < RequiredInkCoverage(item.element, options.InkCoverageRequirement) * 0.55);
        if (emptyNonWhitespace > 0)
            message += $" 文字画素が少ない区間が{emptyNonWhitespace}件あります。設定値または境界を確認してください。";
        return new CharacterAdvanceEstimationResult(advances, leadingOffset, estimatedExtent, confidence, message, inkCoverages);
    }

    private static (int Start, int End) FindContentSpan(IReadOnlyList<double> energy, int count, int crossLength)
    {
        var ordered = energy.Order().ToArray();
        var level = ordered[(int)Math.Floor((ordered.Length - 1) * 0.72)];
        var threshold = Math.Max(0.12, level * 0.32);
        var first = -1;
        var last = -1;
        for (var index = 0; index < energy.Count; index++)
        {
            if (energy[index] < threshold) continue;
            if (first < 0) first = index;
            last = index;
        }
        if (first < 0 || last <= first) return (0, energy.Count);

        var average = (double)energy.Count / count;
        var margin = Math.Max(1, (int)Math.Round(Math.Min(average * 0.18, crossLength * 0.12)));
        first = Math.Max(0, first - margin);
        last = Math.Min(energy.Count - 1, last + margin);
        var minimumBlank = Math.Max(3, (int)Math.Round(average * 0.4));
        var start = first >= minimumBlank ? first : 0;
        var end = energy.Count - 1 - last >= minimumBlank ? last + 1 : energy.Count;
        if (end - start < count * 2) return (0, energy.Count);
        return (start, end);
    }

    private static BitmapSource ConvertToBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32) return source;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte[] SampleRectifiedRegion(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        OverlayRegionViewModel region,
        int localWidth,
        int localHeight)
    {
        var result = new byte[checked(localWidth * localHeight * 4)];
        var radians = region.RotationDegrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var centerX = region.Left + region.Width / 2d;
        var centerY = region.Top + region.Height / 2d;
        for (var y = 0; y < localHeight; y++)
        {
            var localY = (y + 0.5) / localHeight * region.Height - region.Height / 2d;
            for (var x = 0; x < localWidth; x++)
            {
                var localX = (x + 0.5) / localWidth * region.Width - region.Width / 2d;
                var pageX = centerX + cos * localX - sin * localY;
                var pageY = centerY + sin * localX + cos * localY;
                var sampleX = Math.Clamp((int)Math.Round(pageX), 0, sourceWidth - 1);
                var sampleY = Math.Clamp((int)Math.Round(pageY), 0, sourceHeight - 1);
                var sourceOffset = (sampleY * sourceWidth + sampleX) * 4;
                var targetOffset = (y * localWidth + x) * 4;
                Buffer.BlockCopy(source, sourceOffset, result, targetOffset, 4);
            }
        }
        return result;
    }

    private static (double Blue, double Green, double Red) EstimateDominantColor(byte[] pixels, int width, int height)
    {
        var histogram = new Dictionary<int, (int Count, long Blue, long Green, long Red)>();
        var step = Math.Max(1, (int)Math.Sqrt(width * height / 16000d));
        var border = Math.Clamp((int)Math.Ceiling(Math.Min(width, height) * 0.09), 1, Math.Max(1, Math.Min(width, height) / 3));
        for (var y = 0; y < height; y += step)
        for (var x = 0; x < width; x += step)
        {
            // Large bold glyphs can occupy most of a line. Sampling the complete rectangle
            // would then identify the glyph color as the background. Page backgrounds are
            // much more likely to be represented around the OCR rectangle's perimeter.
            if (x >= border && x < width - border && y >= border && y < height - border) continue;
            var offset = (y * width + x) * 4;
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            var key = (red >> 4) << 8 | (green >> 4) << 4 | blue >> 4;
            histogram.TryGetValue(key, out var bin);
            histogram[key] = (bin.Count + 1, bin.Blue + blue, bin.Green + green, bin.Red + red);
        }
        var dominant = histogram.Values.MaxBy(bin => bin.Count);
        return dominant.Count == 0
            ? (255, 255, 255)
            : ((double)dominant.Blue / dominant.Count, (double)dominant.Green / dominant.Count, (double)dominant.Red / dominant.Count);
    }

    private static double[] BuildForegroundProjection(
        byte[] pixels,
        int width,
        int height,
        bool vertical,
        (double Blue, double Green, double Red) background)
    {
        var primaryLength = vertical ? height : width;
        var crossLength = vertical ? width : height;
        var result = new double[primaryLength];
        for (var primary = 0; primary < primaryLength; primary++)
        {
            var total = 0d;
            for (var cross = 0; cross < crossLength; cross++)
            {
                var x = vertical ? cross : primary;
                var y = vertical ? primary : cross;
                var offset = (y * width + x) * 4;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var colorDistance = Math.Sqrt(
                    Square(blue - background.Blue) +
                    Square(green - background.Green) +
                    Square(red - background.Red)) / 441.7;

                var neighborPrimary = Math.Min(primaryLength - 1, primary + 1);
                var neighborX = vertical ? cross : neighborPrimary;
                var neighborY = vertical ? neighborPrimary : cross;
                var neighborOffset = (neighborY * width + neighborX) * 4;
                var edge = (Math.Abs(blue - pixels[neighborOffset]) +
                            Math.Abs(green - pixels[neighborOffset + 1]) +
                            Math.Abs(red - pixels[neighborOffset + 2])) / 765d;
                total += colorDistance + edge * 0.4;
            }
            result[primary] = total / crossLength;
        }
        return result;
    }

    private static double[] NormalizeProjection(IReadOnlyList<double> projection)
    {
        var ordered = projection.Order().ToArray();
        var low = ordered[(int)Math.Floor((ordered.Length - 1) * 0.05)];
        var high = ordered[(int)Math.Floor((ordered.Length - 1) * 0.9)];
        var range = Math.Max(0.00001, high - low);
        var normalized = projection.Select(value => Math.Clamp((value - low) / range, 0, 1.5)).ToArray();
        var radius = Math.Clamp(projection.Count / 250, 1, 4);
        var smoothed = new double[normalized.Length];
        for (var index = 0; index < normalized.Length; index++)
        {
            var start = Math.Max(0, index - radius);
            var end = Math.Min(normalized.Length - 1, index + radius);
            var total = 0d;
            for (var current = start; current <= end; current++) total += normalized[current];
            smoothed[index] = total / (end - start + 1);
        }
        return smoothed;
    }

    private static int[] FindBoundaries(
        IReadOnlyList<double> energy,
        IReadOnlyList<string> elements,
        int crossLength,
        CharacterAdvanceEstimationOptions options)
    {
        var count = elements.Count;
        var length = energy.Count;
        var average = (double)length / count;
        var advanceWeights = elements.Select(ExpectedAdvanceWeight).ToArray();
        var weightTotal = advanceWeights.Sum();
        var expectedWidths = advanceWeights.Select(weight => length * weight / weightTotal).ToArray();
        var expectedPrefix = new double[count + 1];
        for (var index = 0; index < count; index++) expectedPrefix[index + 1] = expectedPrefix[index] + expectedWidths[index];
        var minimumWidths = elements.Select((element, index) =>
        {
            var expected = expectedWidths[index];
            if (IsWhitespace(element)) return Math.Max(1, (int)Math.Floor(expected * 0.08));
            var aspectMinimum = (int)Math.Ceiling(crossLength * options.MinimumAspectRatio);
            var glyphPrior = ElementGlyphPrior(element);
            var shapeMinimum = UsesFullEmAdvance(element)
                ? expected * (0.42 + options.GlyphPriorStrength * glyphPrior * 0.45)
                : IsDashLikeGlyph(element)
                    ? expected * (0.50 + options.GlyphPriorStrength * glyphPrior * 0.25)
                : IsEastAsianFullWidth(element)
                    ? expected * 0.40
                    : expected * 0.28;
            var feasibleMinimum = Math.Max(1, (int)Math.Floor(expected * 0.78));
            return Math.Clamp(Math.Max((int)Math.Ceiling(shapeMinimum), aspectMinimum), 1, feasibleMinimum);
        }).ToArray();
        if (minimumWidths.Sum() >= length)
            minimumWidths = Enumerable.Repeat(Math.Max(1, (int)Math.Floor(average * 0.55)), count).ToArray();
        var maximumWidths = elements.Select((element, index) =>
        {
            var aspectMaximum = crossLength * options.MaximumAspectRatio * (IsWhitespace(element) ? 2.2 : 1.0);
            var expected = expectedWidths[index];
            var feasibleMaximum = expected * (IsWhitespace(element)
                ? 3.2
                : UsesFullEmAdvance(element)
                    ? 1.58 - options.GlyphPriorStrength * ElementGlyphPrior(element) * 0.35
                    : IsDashLikeGlyph(element)
                        ? 1.55 - options.GlyphPriorStrength * ElementGlyphPrior(element) * 0.30
                    : IsEastAsianFullWidth(element) ? 1.48 : 2.0);
            return Math.Max(minimumWidths[index], (int)Math.Ceiling(Math.Min(aspectMaximum, feasibleMaximum)));
        }).ToArray();
        var minimumPrefix = new int[count + 1];
        for (var index = 0; index < count; index++) minimumPrefix[index + 1] = minimumPrefix[index] + minimumWidths[index];
        var energyPrefix = new double[length + 1];
        var inkPrefix = new int[length + 1];
        for (var index = 0; index < length; index++)
        {
            energyPrefix[index + 1] = energyPrefix[index] + energy[index];
            inkPrefix[index + 1] = inkPrefix[index] + (energy[index] >= 0.16 ? 1 : 0);
        }
        var previous = Enumerable.Repeat(double.PositiveInfinity, length + 1).ToArray();
        var paths = new int[count + 1, length + 1];
        previous[0] = 0;

        for (var character = 1; character < count; character++)
        {
            var current = Enumerable.Repeat(double.PositiveInfinity, length + 1).ToArray();
            var minimumPosition = minimumPrefix[character];
            var maximumPosition = length - (minimumPrefix[count] - minimumPrefix[character]);
            for (var position = minimumPosition; position <= maximumPosition; position++)
            {
                var previousStart = Math.Max(minimumPrefix[character - 1], position - maximumWidths[character - 1]);
                var previousEnd = position - minimumWidths[character - 1];
                var boundaryEnergy = BoundaryEnergy(energy, position, average);
                var boundaryPrior = BoundaryGlyphPrior(elements[character - 1], elements[character]);
                var boundaryScale = Math.Max(1, (expectedWidths[character - 1] + expectedWidths[character]) / 2d);
                var boundaryOffset = (position - expectedPrefix[character]) / boundaryScale;
                var maximumExpectedOffset = 0.60 - options.GlyphPriorStrength * boundaryPrior * 0.40;
                if (Math.Abs(boundaryOffset) > maximumExpectedOffset) continue;
                var cumulativePenalty =
                    (0.04 + options.UniformityStrength * 0.12 + options.GlyphPriorStrength * boundaryPrior * 0.95) *
                    Square(boundaryOffset);
                for (var prior = previousStart; prior <= previousEnd; prior++)
                {
                    if (!double.IsFinite(previous[prior])) continue;
                    var targetWidth = Math.Max(1, expectedWidths[character - 1]);
                    var widthRatio = (position - prior) / targetWidth;
                    var widthPenalty =
                        (0.025 + options.UniformityStrength * 0.18 +
                         options.GlyphPriorStrength * ElementGlyphPrior(elements[character - 1]) * 0.70) *
                        Square(widthRatio - 1);
                    var contentPenalty = CellContentPenalty(
                        prior,
                        position,
                        elements[character - 1],
                        energyPrefix,
                        inkPrefix,
                        options.InkCoverageRequirement);
                    var cost = previous[prior] + boundaryEnergy + cumulativePenalty + widthPenalty + contentPenalty;
                    if (cost >= current[position]) continue;
                    current[position] = cost;
                    paths[character, position] = prior;
                }
            }
            previous = current;
        }

        var bestPosition = -1;
        var bestCost = double.PositiveInfinity;
        for (var position = minimumPrefix[count - 1]; position <= length - minimumWidths[count - 1]; position++)
        {
            if (!double.IsFinite(previous[position])) continue;
            var finalWidth = length - position;
            if (finalWidth > maximumWidths[count - 1]) continue;
            var contentPenalty = CellContentPenalty(
                position,
                length,
                elements[count - 1],
                energyPrefix,
                inkPrefix,
                options.InkCoverageRequirement);
            var finalTargetWidth = Math.Max(1, expectedWidths[count - 1]);
            var cost = previous[position] +
                       (0.025 + options.UniformityStrength * 0.18 +
                        options.GlyphPriorStrength * ElementGlyphPrior(elements[count - 1]) * 0.70) *
                       Square(finalWidth / finalTargetWidth - 1) +
                       contentPenalty;
            if (cost >= bestCost) continue;
            bestCost = cost;
            bestPosition = position;
        }
        if (bestPosition < 0) throw new InvalidOperationException("文字境界の組合せを決定できませんでした。");

        var result = new int[count + 1];
        result[0] = 0;
        result[count] = length;
        var cursor = bestPosition;
        for (var character = count - 1; character >= 1; character--)
        {
            result[character] = cursor;
            cursor = paths[character, cursor];
        }
        return result;
    }

    private static double CellContentPenalty(
        int start,
        int end,
        string element,
        IReadOnlyList<double> energyPrefix,
        IReadOnlyList<int> inkPrefix,
        double requiredCoverage)
    {
        var width = Math.Max(1, end - start);
        var coverage = (inkPrefix[end] - inkPrefix[start]) / (double)width;
        var meanEnergy = (energyPrefix[end] - energyPrefix[start]) / width;
        if (IsWhitespace(element)) return coverage * 0.65 + meanEnergy * 0.15;
        var elementCoverage = RequiredInkCoverage(element, requiredCoverage);
        var coverageDeficit = Math.Max(0, elementCoverage - coverage) / elementCoverage;
        // Japanese brackets and punctuation can have most of their em box empty by design.
        // Requiring the same amount of ink as a kana or ideograph makes the optimizer widen
        // the cell until it captures ink from the adjacent character.
        var energyRequirement = IsLowInkFullWidthGlyph(element) ? 0.025 : 0.09;
        var energyDeficit = Math.Max(0, energyRequirement - meanEnergy) / energyRequirement;
        return 0.85 * Square(coverageDeficit) + 0.25 * Square(energyDeficit);
    }

    private static double RequiredInkCoverage(string element, double configuredRequirement) =>
        IsLowInkFullWidthGlyph(element)
            ? Math.Max(0.02, configuredRequirement * 0.22)
            : configuredRequirement;

    private static double[] CalculateInkCoverages(IReadOnlyList<double> energy, IReadOnlyList<int> boundaries)
    {
        var result = new double[boundaries.Count - 1];
        for (var cell = 0; cell < result.Length; cell++)
        {
            var start = boundaries[cell];
            var end = boundaries[cell + 1];
            var width = Math.Max(1, end - start);
            var ink = 0;
            for (var index = start; index < end; index++)
                if (energy[index] >= 0.16) ink++;
            result[cell] = ink / (double)width;
        }
        return result;
    }

    private static bool IsWhitespace(string element) => element.All(char.IsWhiteSpace);

    private static double ExpectedAdvanceWeight(string element)
    {
        if (element.Length == 0) return 1;
        var value = FirstCodePoint(element);
        if (value == 0x3000) return 1;
        if (IsWhitespace(element)) return 0.52;
        if (IsDashLikeGlyph(element)) return DashAdvanceWeight(value);
        // Full-width Japanese punctuation retains one complete em of advance even
        // when the visible mark occupies only the left/lower part of that cell.
        if (IsFullWidthJapanesePunctuation(element)) return 1;
        if (IsEastAsianFullWidth(element)) return 1;
        if (value is >= 0xFF61 and <= 0xFF9F) return 0.52;
        if (value <= 0x7F)
        {
            var character = (char)value;
            if ("ilI|!.,:;'`".Contains(character)) return 0.34;
            // Lowercase m/u/n are made from repeated stems.  Treating every
            // valley between those stems as a possible character boundary
            // makes one letter split into two cells.  Give these glyphs an
            // explicit typographic advance instead of relying on ink valleys.
            if (character == 'm') return 0.84;
            if (character is 'u' or 'n' or 'h') return 0.58;
            if (character == 'w') return 0.82;
            if ("MW@%&".Contains(character)) return 0.92;
            if (char.IsUpper(character)) return 0.68;
            if (char.IsDigit(character)) return 0.60;
            return 0.56;
        }
        return 0.72;
    }

    private static double BoundaryGlyphPrior(string left, string right) =>
        Math.Max(ElementGlyphPrior(left), ElementGlyphPrior(right));

    private static double ElementGlyphPrior(string element)
    {
        if (IsWhitespace(element)) return 0.15;
        var value = FirstCodePoint(element);
        // A dash has little ink in the cross-writing direction.  Its cell must be
        // guided by the known character kind rather than by ink coverage alone.
        if (IsDashLikeGlyph(element)) return 1.22;
        if (IsFullWidthPairedBracket(element)) return 1.32;
        if (IsDisconnectedKana(element)) return 1.15;
        if (IsFullWidthJapanesePunctuation(element)) return 1.05;
        if (value is >= 0x3040 and <= 0x30FF) return 1.0; // Hiragana/Katakana often contain separated strokes.
        if (IsEastAsianFullWidth(element)) return 0.82;
        if (value <= 0x7F)
        {
            var character = (char)value;
            // Arch-shaped lowercase letters contain strong internal gaps.
            // A higher prior keeps their left/right boundaries near the
            // font-derived expected positions instead of the internal gap.
            if (character == 'm') return 0.95;
            if (character is 'u' or 'n' or 'h') return 0.88;
            return 0.32;
        }
        return 0.50;
    }

    private static bool IsEastAsianFullWidth(string element)
    {
        if (element.Length == 0) return false;
        var value = FirstCodePoint(element);
        return value is >= 0x3040 and <= 0x30FF or // Hiragana and Katakana
               >= 0x3400 and <= 0x9FFF or          // CJK ideographs
               >= 0xF900 and <= 0xFAFF or          // CJK compatibility ideographs
               >= 0x3000 and <= 0x303F or          // CJK punctuation
               >= 0xFF01 and <= 0xFF60 or          // Full-width forms
               >= 0xFFE0 and <= 0xFFE6;
    }

    private static bool IsKanaOrIdeograph(string element)
    {
        if (element.Length == 0) return false;
        var value = FirstCodePoint(element);
        return value is >= 0x3040 and <= 0x30FF or
               >= 0x31F0 and <= 0x31FF or
               >= 0x3400 and <= 0x9FFF or
               >= 0xF900 and <= 0xFAFF or
               >= 0x20000 and <= 0x3134F;
    }

    private static bool UsesFullEmAdvance(string element) =>
        IsKanaOrIdeograph(element) || IsFullWidthJapanesePunctuation(element) || IsFullEmDashLikeGlyph(element);

    private static bool IsDisconnectedKana(string element)
    {
        if (element.Length == 0) return false;
        var value = FirstCodePoint(element);
        return value is 'い' or 'け' or 'に' or 'ふ' or 'こ' or 'ハ' or 'リ' or 'ニ';
    }

    private static bool IsSparseFullWidthPunctuation(string element)
    {
        if (element.Length == 0) return false;
        var value = FirstCodePoint(element);
        return value is 0x3001 or 0x3002 or 0xFF0C or 0xFF0E or 0x30FB;
    }

    private static bool IsLowInkFullWidthGlyph(string element) =>
        IsSparseFullWidthPunctuation(element) || IsFullWidthPairedBracket(element) || IsDashLikeGlyph(element);

    /// <summary>
    /// Returns whether the text element is a hyphen, dash, minus sign, or Japanese
    /// prolonged-sound mark whose visible ink is a thin line.
    /// </summary>
    private static bool IsDashLikeGlyph(string element)
    {
        if (element.Length == 0) return false;
        var value = FirstCodePoint(element);
        return value is
            0x002D or                         // HYPHEN-MINUS
            >= 0x2010 and <= 0x2015 or        // HYPHEN through HORIZONTAL BAR
            0x2212 or                         // MINUS SIGN
            0x2E3A or 0x2E3B or               // TWO-/THREE-EM DASH
            0x30A0 or 0x30FC or               // KATAKANA DOUBLE HYPHEN / PROLONGED SOUND MARK
            0xFE31 or 0xFE32 or               // VERTICAL EM/EN DASH
            0xFE58 or 0xFE63 or               // SMALL EM DASH / SMALL HYPHEN-MINUS
            0xFF0D or 0xFF70;                 // FULLWIDTH HYPHEN / HALFWIDTH PROLONGED MARK
    }

    /// <summary>
    /// Returns whether the dash normally occupies a complete Japanese em cell.
    /// </summary>
    private static bool IsFullEmDashLikeGlyph(string element)
    {
        if (element.Length == 0) return false;
        var value = FirstCodePoint(element);
        return value is
            0x2014 or 0x2015 or               // EM DASH / HORIZONTAL BAR
            0x2E3A or 0x2E3B or               // TWO-/THREE-EM DASH (kept at least one em)
            0x30A0 or 0x30FC or               // Japanese full-width marks
            0xFE31 or 0xFE58 or 0xFF0D;
    }

    /// <summary>
    /// Supplies a typographic advance prior for dash variants.  This prevents a thin
    /// stroke from borrowing ink from an adjacent glyph or collapsing into a gap.
    /// </summary>
    private static double DashAdvanceWeight(int value) => value switch
    {
        0x2014 or 0x2015 or 0x30A0 or 0x30FC or 0xFE31 or 0xFE58 or 0xFF0D => 1.00,
        0x2E3A => 2.00,
        0x2E3B => 3.00,
        0x2012 or 0x2212 or 0xFE32 => 0.68,
        0x2013 => 0.62,
        0xFF70 => 0.52,
        _ => 0.44,
    };

    private static bool IsFullWidthPairedBracket(string element)
    {
        if (element.Length == 0) return false;
        var value = FirstCodePoint(element);
        return value is
            >= 0x3008 and <= 0x3011 or // 〈〉《》「」『』【】
            >= 0x3014 and <= 0x301B or // 〔〕〖〗〘〙〚〛
            >= 0x301D and <= 0x301F or // 〝〞〟
            0xFF08 or 0xFF09 or       // （）
            0xFF3B or 0xFF3D or       // ［］
            0xFF5B or 0xFF5D or       // ｛｝
            0xFF5F or 0xFF60;         // ｟｠
    }

    private static bool IsFullWidthJapanesePunctuation(string element)
    {
        if (element.Length == 0) return false;
        var value = FirstCodePoint(element);
        return IsSparseFullWidthPunctuation(element) ||
               IsFullWidthPairedBracket(element) ||
               value is 0xFF01 or 0xFF1F or 0xFF1A or 0xFF1B;
    }

    private static int FirstCodePoint(string element)
    {
        if (element.Length == 0) return 0;
        return element.Length >= 2 &&
               char.IsHighSurrogate(element[0]) &&
               char.IsLowSurrogate(element[1])
            ? char.ConvertToUtf32(element, 0)
            : element[0];
    }

    private static double BoundaryEnergy(IReadOnlyList<double> energy, int position, double averageWidth)
    {
        var radius = Math.Max(1, (int)Math.Round(averageWidth * 0.07));
        var start = Math.Max(0, position - radius);
        var end = Math.Min(energy.Count - 1, position + radius);
        var total = 0d;
        for (var index = start; index <= end; index++) total += energy[index];
        return total / (end - start + 1);
    }

    private static double CalculateConfidence(IReadOnlyList<double> energy, IReadOnlyList<int> boundaries)
    {
        if (boundaries.Count <= 2) return 0.5;
        var contrasts = new List<double>();
        var averageWidth = (double)energy.Count / (boundaries.Count - 1);
        var neighborhood = Math.Max(2, (int)Math.Round(averageWidth * 0.35));
        for (var index = 1; index < boundaries.Count - 1; index++)
        {
            var position = boundaries[index];
            var start = Math.Max(0, position - neighborhood);
            var end = Math.Min(energy.Count - 1, position + neighborhood);
            var localAverage = 0d;
            for (var current = start; current <= end; current++) localAverage += energy[current];
            localAverage /= end - start + 1;
            var valley = BoundaryEnergy(energy, position, averageWidth);
            contrasts.Add(Math.Clamp((localAverage - valley) / Math.Max(0.05, localAverage), 0, 1));
        }
        var contrastScore = contrasts.Count == 0 ? 0.5 : contrasts.Average();
        var widths = boundaries.Zip(boundaries.Skip(1), (left, right) => (double)(right - left)).ToArray();
        var extremePenalty = widths.Count(width => width < averageWidth * 0.28 || width > averageWidth * 2.4) / (double)widths.Length;
        return Math.Clamp(0.28 + contrastScore * 0.72 - extremePenalty * 0.25, 0.05, 0.98);
    }

    private static double Square(double value) => value * value;
}
