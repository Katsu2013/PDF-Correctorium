using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;

namespace PdfCorrectorium.App;

public partial class App
{
    private static void VerifyRapidOcrMarginStability(Action<bool, string> check, string directory)
    {
        var measurements = new List<string>();
        static OverlayRegionViewModel Model(OverlayRegionSnapshot state) =>
            new(Guid.NewGuid(), state.Text, state, state, true, false, "margin-regression", null);
        static bool Close(OverlayRegionSnapshot first, OverlayRegionSnapshot second) =>
            Math.Abs(first.Left - second.Left) < .001 && Math.Abs(first.Top - second.Top) < .001 &&
            Math.Abs(first.Width - second.Width) < .001 && Math.Abs(first.Height - second.Height) < .001 &&
            Model(first).CharacterCells.Zip(Model(second).CharacterCells).All(p => Math.Abs(p.First.Width - p.Second.Width) < .001);
        foreach (var text in new[] { "ABCD", "。AB。", "ーABー", "「AB」" })
        foreach (var angle in new[] { 0d, 17d, 90d })
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 600, 220));
                drawing.PushTransform(new RotateTransform(angle, 260, 100));
                drawing.DrawRectangle(Brushes.Black, null, new Rect(228, 84, 20, 26));
                drawing.DrawRectangle(Brushes.Black, null, new Rect(268, 84, 20, 26));
                var first = text[0] == '。' ? new Rect(202, 107, 3, 3) : text[0] == 'ー'
                    ? new Rect(202, 96, 18, 2) : new Rect(202, 84, text[0] == '「' ? 3 : 18, 26);
                var last = text[^1] == '。' ? new Rect(311, 107, 3, 3) : text[^1] == 'ー'
                    ? new Rect(296, 96, 18, 2) : new Rect(text[^1] == '」' ? 311 : 296, 84, text[^1] == '」' ? 3 : 18, 26);
                drawing.DrawRectangle(Brushes.Black, null, first); drawing.DrawRectangle(Brushes.Black, null, last);
                drawing.Pop();
            }
            var image = new RenderTargetBitmap(600, 220, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
            var reading = new OcrLineReading(text, Enumerable.Repeat(.999, 4).ToArray(), [.125, .375, .625, .875], [true, true, true, true]);
            foreach (var scale in new[] { 1d, 2d })
            {
                RapidOcrLineAdjustment Fit(OverlayRegionSnapshot state)
                {
                    var (cropImage, bounds) = RapidLineRecognizer.CreateCrop(image, new(state.Left, state.Top, state.Width,
                        state.Height, state.RotationDegrees, image.PixelWidth / scale, image.PixelHeight / scale, 0));
                    var prepared = RapidLineRecognizer.PrepareImage(cropImage);
                    using var bitmap = prepared.Bitmap;
                    return RapidOcrLineAdjuster.Prepare(state, new(new(reading, bounds, prepared.ContentBounds), cropImage), new());
                }
                // Both inputs contain the same ink but very different amounts of blank context.
                var narrow = new OverlayRegionSnapshot(text, 200 / scale, 80 / scale, 120 / scale, 40 / scale, angle);
                var wide = narrow with { Left = 160 / scale, Width = 200 / scale };
                var initial = Fit(narrow); var wider = Fit(wide);
                check(initial.IsImageAdjusted && wider.IsImageAdjusted && Close(initial.Snapshot, wider.Snapshot),
                    $"Blank side context cannot enlarge edge characters: {text}, rotation {angle}, scale {scale}.");
                var current = initial.Snapshot;
                var stable = true;
                for (var iteration = 0; iteration < 8; iteration++)
                {
                    var next = Fit(current);
                    stable &= next.IsImageAdjusted && next.Snapshot == current && Close(initial.Snapshot, next.Snapshot);
                    measurements.Add($"{text} angle={angle} scale={scale} run={iteration + 1} left={next.Snapshot.Left:R} top={next.Snapshot.Top:R} width={next.Snapshot.Width:R} advances={next.Snapshot.CharacterAdvancesText}");
                    current = next.Snapshot;
                }
                check(stable, $"Eight crop/fit cycles preserve exact geometry without accumulating margins: {text}, rotation {angle}, scale {scale}.");
            }
        }
        File.WriteAllLines(Path.Combine(directory, "margin-stability.txt"), measurements);
    }
}
