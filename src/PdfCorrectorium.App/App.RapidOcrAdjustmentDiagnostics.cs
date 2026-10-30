using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;
using SkiaSharp;

namespace PdfCorrectorium.App;

public partial class App
{
    private static void VerifyRapidOcrAdjustment(Action<bool, string> check)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 320, 70));
            foreach (var (x, width) in new[] { (30, 20), (64, 40), (120, 25), (168, 20) })
                drawing.DrawRectangle(Brushes.Black, null, new Rect(x, 18, width, 30));
        }
        var image = new RenderTargetBitmap(320, 70, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        OcrLineReading Reading(string text)
        {
            var count = OcrLineReconciler.Elements(text).Length;
            return new(text, Enumerable.Repeat(.999, count).ToArray(),
                Enumerable.Range(0, count).Select(i => (i + .5) / count).ToArray(), Enumerable.Repeat(true, count).ToArray());
        }
        var reading = Reading("abcd");
        var result = new RapidLineResult(new(reading, new(10, 10, 280, 50), new(30, 18, 158, 30)), image);
        var before = new OverlayRegionSnapshot("abcd", 100, 200, 140, 25, 0, ReadingOrder: 7, CharacterAdvancesText: "35;35;35;35");
        var options = new CharacterAdvanceEstimationOptions(GlyphPriorStrength: .25);
        var fit = RapidOcrLineAdjuster.Prepare(before, result, options);
        check(fit.IsImageAdjusted && fit.Snapshot.Text == before.Text && fit.Snapshot.Width != before.Width,
            "Image fitting runs even when recognized text is unchanged and corrects the over-wide row.");
        OverlayRegionViewModel Model(OverlayRegionSnapshot state) => new(Guid.NewGuid(), state.Text, state, state, true, false, "fixture", null);
        var local = Model(before with { Left = 30, Top = 8, Width = 158, Height = 54, CharacterAdvancesText = "" });
        var pixels = CharacterAdvanceEstimator.Estimate(image, local, options);
        var expected = Model(before);
        expected.ApplyCharacterAdvanceEstimation(pixels with { Advances = pixels.Advances.Select(v => v / 2).ToArray(),
            LeadingOffset = (30 + pixels.LeadingOffset - 10) / 2, Extent = pixels.Extent / 2 });
        check(fit.Snapshot == expected.Capture(), "Crop-to-page fitting matches the existing estimator and honors custom estimation settings.");
        var cells = Model(fit.Snapshot).CharacterCells;
        check(cells.Count == 4 && cells.Max(c => c.Width) - cells.Min(c => c.Width) > 1 &&
            Math.Abs(cells.Sum(c => c.Width) - fit.Snapshot.Width) < .001,
            "The applied character sizes follow non-uniform image spacing and sum to the fitted extent.");
        check(fit.Snapshot.Height == before.Height && fit.Snapshot.Top == before.Top && fit.Snapshot.ReadingOrder == before.ReadingOrder,
            "Image fitting preserves cross-axis geometry and reading order.");
        var rotated = before with { RotationDegrees = 90 };
        var rotatedFit = RapidOcrLineAdjuster.Prepare(rotated, result, options);
        check(rotatedFit.IsImageAdjusted && rotatedFit.Snapshot.RotationDegrees == 90 &&
            Math.Abs(rotatedFit.Snapshot.Left + rotatedFit.Snapshot.Width / 2 - (rotated.Left + rotated.Width / 2)) < .001,
            "Rectified crop fitting maps back along the original rotated row axis.");
        var blankVisual = new DrawingVisual();
        using (var drawing = blankVisual.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 320, 70));
        var blank = new RenderTargetBitmap(320, 70, 96, 96, PixelFormats.Pbgra32); blank.Render(blankVisual); blank.Freeze();
        var added = Reading("abxcd");
        var fallback = RapidOcrLineAdjuster.Prepare(before, result with { Image = blank,
            Recognition = result.Recognition with { Reading = added } }, options);
        check(!fallback.IsImageAdjusted && fallback.Snapshot.Text == "abxcd" && fallback.Snapshot.Left == before.Left &&
            fallback.Snapshot.Top == before.Top && fallback.Snapshot.Width == before.Width && fallback.Detail.Length > 0,
            "Unavailable image fitting retains recognized text and original bounds with an explicit fallback reason.");
        var single = Reading("a");
        check(!RapidOcrLineAdjuster.Prepare(before, result with { Recognition = new(single) }, options).IsImageAdjusted,
            "One-character recognition uses a bounded geometry fallback rather than failing text application.");
        bool Rejected(OverlayRegionSnapshot state, RapidLineResult input)
        {
            try { RapidOcrLineAdjuster.Prepare(state, input, options); return false; }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { return true; }
        }
        check(Rejected(before, result with { Recognition = new(reading) }) &&
            Rejected(before, result with { Recognition = result.Recognition with { ContentBounds = null } }) &&
            Rejected(before, result with { Recognition = result.Recognition with { ContentBounds = new(-1, 10, 100, 30) } }) &&
            Rejected(before, result with { Recognition = result.Recognition with { RasterBounds = new(10, 10, double.NaN, 50) } }),
            "Missing or invalid crop coordinates cannot silently create incorrect geometry.");
        var locked = Model(before); locked.SelectCharacter(0, false, false); locked.SetSelectedCharacterLocks(true);
        check(Rejected(before with { IsGeometryLocked = true }, result) && Rejected(locked.Capture(), result),
            "Fitting preparation independently rejects both region and character locks.");
    }

    private static void VerifyRapidOcrPadding(Action<bool, string> check)
    {
        using var source = new SKBitmap(6, 5);
        using (var canvas = new SKCanvas(source)) canvas.Clear(SKColors.Black);
        var calls = 0;
        var reading = RapidLineRecognizer.ReadPadded(source, (input, _) =>
        {
            calls++;
            check(input.Width == 10 && input.Height == 17 && input.GetPixel(0, 0) == SKColors.White &&
                input.GetPixel(2, 6) == SKColors.Black && input.GetPixel(9, 16) == SKColors.White,
                "The sole OCR input keeps side padding and increases top/bottom white padding.");
            return new("ok", [.999, .999], [.25, .75], [true, true]);
        }, default);
        check(calls == 1 && reading.Text == "ok", "Padded recognition invokes the OCR callback exactly once.");
        calls = 0; var rejected = false;
        try { RapidLineRecognizer.ReadPadded(source, (_, _) => { calls++; return new("", [], [], []); }, default); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && calls == 1, "An empty padded result fails without silently running an unpadded fallback.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); calls = 0; var cancelled = false;
        try { RapidLineRecognizer.ReadPadded(source, (_, _) => { calls++; return reading; }, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && calls == 0, "Cancellation prevents the sole recognition call from starting.");
    }
}
