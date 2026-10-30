using System.Globalization;
using System.IO;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;

namespace PdfCorrectorium.App.Services;

internal sealed record RapidOcrLineAdjustment(OverlayRegionSnapshot Snapshot, bool IsImageAdjusted, string Detail, bool RequiresReview = false);

/// <summary>Prepares one atomic text/geometry edit from the already rendered OCR crop. No live UI model is mutated.</summary>
internal static class RapidOcrLineAdjuster
{
    internal static RapidOcrLineAdjustment Prepare(OverlayRegionSnapshot before, RapidLineResult result,
        CharacterAdvanceEstimationOptions options)
    {
        var reading = result.Recognition.Reading;
        var count = OcrLineReconciler.Elements(reading.Text).Length;
        if (count is 0 or > OcrLineReconciler.MaximumElements || reading.Text.Any(char.IsControl))
            throw new InvalidDataException("再認識した文字列が調整の対象外です。");
        var original = CreateModel(before);
        if (original.IsGeometryLocked || original.HasLockedCharacters || original.IsVertical)
            throw new InvalidOperationException("固定中または縦書きの行・文字は再認識で変更しません。");
        var widths = original.CharacterCells.Count == 0 ? Enumerable.Repeat(before.Width / count, count).ToArray()
            : OcrLineReconciler.ReconcileAdvances(before.Text, original.CharacterCells.Select(c => c.Width).ToArray(), reading);
        var state = before with { Text = reading.Text, CharacterLocksText = "",
            CharacterAdvancesText = Format(widths) };
        if (count < 2) return new(state, false, "1文字のため、画像からの文字境界調整は行いませんでした。");

        var bounds = result.Recognition.RasterBounds ?? throw new InvalidDataException("再認識画像の座標情報がありません。");
        var content = result.Recognition.ContentBounds ?? throw new InvalidDataException("再認識画像の文字範囲がありません。");
        bool ValidBounds(RapidLineRasterBounds value) => new[] { value.Left, value.Top, value.Width, value.Height }.All(double.IsFinite) &&
            value.Left >= 0 && value.Top >= 0 && value.Width > 0 && value.Height > 0 &&
            value.Left + value.Width <= result.Image.PixelWidth + .001 && value.Top + value.Height <= result.Image.PixelHeight + .001;
        if (!ValidBounds(bounds) || !ValidBounds(content) || !double.IsFinite(before.Width) || !double.IsFinite(before.Height) ||
            before.Width <= 0 || before.Height <= 0 || result.Image.PixelWidth > 8192 ||
            result.Image.PixelHeight > 512 || result.Image.PixelWidth * (long)result.Image.PixelHeight > 2_000_000)
            throw new InvalidDataException("再認識画像の座標・サイズが正しくありません。");
        var scale = bounds.Width / before.Width;
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidDataException("再認識画像の倍率が正しくありません。");
        try
        {
            // Fit only the ink range actually sent for recognition, not its context
            // or white border. Recovered punctuation may extend beyond the old row,
            // but blank margins must never enlarge the first or last character.
            // Retain cross-axis background for contrast estimation (tight bold
            // glyph bounds can otherwise mistake black strokes for background).
            var fitTop = Math.Max(0, Math.Min(bounds.Top, content.Top) - 2);
            var fitBottom = Math.Min(result.Image.PixelHeight, Math.Max(bounds.Top + bounds.Height, content.Top + content.Height) + 2);
            var local = CreateModel(state with { Left = content.Left, Top = fitTop, Width = content.Width,
                Height = fitBottom - fitTop, RotationDegrees = 0, IsVertical = false, CharacterAdvancesText = "" });
            var pixels = CharacterAdvanceEstimator.Estimate(result.Image, local, options);
            var estimate = pixels with { Advances = pixels.Advances.Select(v => v / scale).ToArray(),
                LeadingOffset = (content.Left + pixels.LeadingOffset - bounds.Left) / scale, Extent = pixels.Extent / scale };
            var candidate = CreateModel(state);
            candidate.ApplyCharacterAdvanceEstimation(estimate);
            var fitted = candidate.Capture();
            // Raster coordinates are stable; suppress only sub-millipixel conversion
            // noise so an unchanged re-run does not add a meaningless Undo record.
            if (before.Text == fitted.Text && Math.Abs(before.Left - fitted.Left) <= .001 &&
                Math.Abs(before.Top - fitted.Top) <= .001 && Math.Abs(before.Width - fitted.Width) <= .001 &&
                original.CharacterCells.Count == candidate.CharacterCells.Count &&
                original.CharacterCells.Zip(candidate.CharacterCells).All(pair => Math.Abs(pair.First.Width - pair.Second.Width) <= .001))
                fitted = before;
            return new(fitted, true, estimate.Message, estimate.Confidence < .48);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            // A blank/low-contrast/too-small crop must not discard recognized text
            // or leave half an edit. Retain the reconciled geometry and report the fallback.
            return new(state, false, error.Message);
        }
    }

    private static OverlayRegionViewModel CreateModel(OverlayRegionSnapshot state) =>
        new(Guid.NewGuid(), state.Text, state, state, true, false, "rapidocr-adjustment", null);
    private static string Format(IEnumerable<double> widths) =>
        string.Join(';', widths.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
}
