using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    // A deterministic raster fixture models the reported TOC: full-em Japanese text,
    // narrow regularly spaced leader dots, then a three-digit page number.
    private async Task RunCharacterEstimationTestAsync(string[] args, int option)
    {
        string? directory = null;
        var checks = new List<string>();
        var measurements = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
        }
        try
        {
            if (args.Length <= option + 1) throw new ArgumentException("A new output directory is required.");
            directory = Path.GetFullPath(args[option + 1]);
            if (Directory.Exists(directory)) throw new IOException("Use a new diagnostic directory.");
            Directory.CreateDirectory(directory);
            foreach (var pitch in new[] { 6, 8, 12 })
            {
                var (image, region) = CreateLeaderFixture(pitch);
                var timer = Stopwatch.StartNew();
                var estimate = CharacterAdvanceEstimator.Estimate(image, region);
                timer.Stop();
                var titleEnd = estimate.LeadingOffset + estimate.Advances.Take(12).Sum();
                var maximumTitleError = Enumerable.Range(1, 12).Max(index =>
                    Math.Abs(estimate.LeadingOffset + estimate.Advances.Take(index).Sum() - index * 20));
                measurements.Add($"pitch={pitch}; titleEnd={titleEnd:F3}; expected=240; maxTitleError={maximumTitleError:F3}; milliseconds={timer.Elapsed.TotalMilliseconds:F3}");
                // Three raster pixels allow the existing nearest-neighbour sampling
                // and ink side-bearing ambiguity, not a whole-character drift.
                Check(maximumTitleError <= 3, $"One pass preserves TOC title boundaries with dot pitch {pitch}.");
                Check(Math.Abs(estimate.Advances.Take(62).Sum() - (240 + 50 * pitch)) <= 2,
                    $"Leader/page-number boundary follows the image with dot pitch {pitch}.");
                Check(estimate.Advances.All(value => double.IsFinite(value) && value > 0), "All advances are finite and positive.");
                Check(estimate.Message.Contains("点線の実測間隔", StringComparison.Ordinal), "A leader anchor requires matching image evidence.");
                Check(estimate.InkCoverages.Skip(12).Take(50).All(value => value >= 0.066),
                    "Confirmed low-ink dot cells are not misreported as empty text.");
            }
            var (horizontalImage, horizontalRegion) = CreateLeaderFixture(8);
            var horizontal = CharacterAdvanceEstimator.Estimate(horizontalImage, horizontalRegion);
            var horizontalPixels = new byte[horizontalImage.PixelWidth * horizontalImage.PixelHeight * 4];
            horizontalImage.CopyPixels(horizontalPixels, horizontalImage.PixelWidth * 4, 0);
            var verticalPixels = new byte[horizontalPixels.Length];
            for (var y = 0; y < horizontalImage.PixelHeight; y++)
                for (var x = 0; x < horizontalImage.PixelWidth; x++)
                    Buffer.BlockCopy(horizontalPixels, (y * horizontalImage.PixelWidth + x) * 4,
                        verticalPixels, (x * horizontalImage.PixelHeight + y) * 4, 4);
            var verticalImage = BitmapSource.Create(24, horizontalImage.PixelWidth, 96, 96,
                PixelFormats.Bgra32, null, verticalPixels, 24 * 4);
            verticalImage.Freeze();
            var verticalRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(horizontalRegion.Text,
                0, 0, 24, horizontalImage.PixelWidth, true, IsVertical: true));
            var vertical = CharacterAdvanceEstimator.Estimate(verticalImage, verticalRegion);
            Check(vertical.Advances.SequenceEqual(horizontal.Advances), "Vertical TOC fitting uses the same writing-axis anchors.");

            var mismatch = new OverlayRegionViewModel(new PdfTextOverlayRegion(
                "コントローラーを作成する" + new string('.', 49) + "124", 0, 0, horizontalImage.PixelWidth, 24, true));
            var mismatchEstimate = CharacterAdvanceEstimator.Estimate(horizontalImage, mismatch);
            Check(mismatchEstimate.Message.Contains("文字列49個・画像50個", StringComparison.Ordinal),
                "A dot-count discrepancy is reported without changing the OCR text.");
            Check(Math.Abs(mismatchEstimate.Advances.Take(12).Sum() - 240) <= 3 && mismatchEstimate.Advances.Count == 64,
                "A count-mismatched leader anchors its complete image range and preserves the text element count.");
            var excessDots = new OverlayRegionViewModel(new PdfTextOverlayRegion(
                "コントローラーを作成する" + new string('.', 51) + "124", 0, 0, horizontalImage.PixelWidth, 24, true));
            var excessEstimate = CharacterAdvanceEstimator.Estimate(horizontalImage, excessDots);
            Check(excessEstimate.Message.Contains("文字列51個・画像50個", StringComparison.Ordinal) &&
                Math.Abs(excessEstimate.Advances.Take(12).Sum() - 240) <= 3 && excessEstimate.Advances.Count == 66,
                "Excess OCR dots preserve the string and anchor the same complete raster extent.");
            var distantCount = new OverlayRegionViewModel(new PdfTextOverlayRegion(
                "コントローラーを作成する" + new string('.', 20) + "124", 0, 0, horizontalImage.PixelWidth, 24, true));
            Check(!CharacterAdvanceEstimator.Estimate(horizontalImage, distantCount).Message.Contains("点線の実測間隔", StringComparison.Ordinal),
                "An excessive count discrepancy does not accept an unrelated raster leader.");
            var brokenPixels = horizontalPixels.ToArray();
            for (var y = 18; y < 20; y++)
                for (var x = 240 + 40 * 8 + 3; x < 240 + 40 * 8 + 5; x++)
                {
                    var offset = (y * horizontalImage.PixelWidth + x) * 4;
                    brokenPixels[offset] = brokenPixels[offset + 1] = brokenPixels[offset + 2] = 255;
                }
            var brokenImage = BitmapSource.Create(horizontalImage.PixelWidth, 24, 96, 96,
                PixelFormats.Bgra32, null, brokenPixels, horizontalImage.PixelWidth * 4);
            brokenImage.Freeze();
            Check(!CharacterAdvanceEstimator.Estimate(brokenImage, horizontalRegion).Message.Contains("点線の実測間隔", StringComparison.Ordinal),
                "An interrupted leader does not silently anchor a partial periodic run.");
            var strongPixels = horizontalPixels.ToArray();
            for (var x = 240; x < 640; x++)
                for (var y = 3; y < 21; y++)
                {
                    var offset = (y * horizontalImage.PixelWidth + x) * 4;
                    var value = x % 8 is >= 2 and <= 5 ? (byte)0 : (byte)255;
                    strongPixels[offset] = strongPixels[offset + 1] = strongPixels[offset + 2] = value;
                }
            var strongImage = BitmapSource.Create(horizontalImage.PixelWidth, 24, 96, 96,
                PixelFormats.Bgra32, null, strongPixels, horizontalImage.PixelWidth * 4);
            strongImage.Freeze();
            Check(!CharacterAdvanceEstimator.Estimate(strongImage, horizontalRegion).Message.Contains("点線の実測間隔", StringComparison.Ordinal),
                "Repeated large glyph strokes are not treated as dot leaders.");

            // This line has no leader. Its wider first half accumulates more than
            // one expected-cell width of drift, which the former hard cutoff hid.
            var variedWidths = Enumerable.Repeat(24, 6).Concat(Enumerable.Repeat(16, 6)).ToArray();
            var variedPixels = Enumerable.Repeat((byte)255, 240 * 24 * 4).ToArray();
            var variedPosition = 0;
            foreach (var cellWidth in variedWidths)
            {
                for (var y = 3; y < 21; y++)
                    for (var x = variedPosition + 2; x < variedPosition + cellWidth - 2; x++)
                    {
                        var offset = (y * 240 + x) * 4;
                        variedPixels[offset] = variedPixels[offset + 1] = variedPixels[offset + 2] = 0;
                    }
                variedPosition += cellWidth;
            }
            var variedImage = BitmapSource.Create(240, 24, 96, 96, PixelFormats.Bgra32, null, variedPixels, 240 * 4);
            variedImage.Freeze();
            var variedRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(new string('字', 12), 0, 0, 240, 24, true));
            var variedEstimate = CharacterAdvanceEstimator.Estimate(variedImage, variedRegion);
            var variedError = Enumerable.Range(1, 12).Max(index => Math.Abs(
                variedEstimate.LeadingOffset + variedEstimate.Advances.Take(index).Sum() - variedWidths.Take(index).Sum()));
            measurements.Add($"noLeader: maximumBoundaryError={variedError:F3}; expectedMiddle=144");
            Check(variedError <= 3, "Local image boundaries can correct accumulated drift without a dot leader.");

            var paths = new ApplicationPaths(StorageMode.Portable, Path.Combine(directory, "config"),
                Path.Combine(directory, "logs"), Path.Combine(directory, "cache"), Path.Combine(directory, "work"));
            ApplicationPathResolver.EnsureDirectories(paths);
            var vm = new MainWindowViewModel(new ProjectPackageService(), new PdfPreviewService(),
                new PdfExportService(), new NdlOcrCompanionService(), new DiagnosticLog(paths.LogDirectory), paths, () => { });
            var blank = Path.Combine(directory, "blank.pdf");
            await BlankPdfService.CreateAsync(blank, 1, 595, 842);
            await vm.LoadPdfForDiagnosticsAsync(blank);
            var editRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(horizontalRegion.Text,
                0, 0, horizontalImage.PixelWidth, 24, true)) { ReadingOrder = 1 };
            editRegion.SetCharacterAdvances(Enumerable.Repeat(20d, 4).Concat(Enumerable.Repeat(25d, 8))
                .Concat(Enumerable.Repeat(7.2, 50)).Concat(Enumerable.Repeat(12d, 3)).ToArray());
            editRegion.SelectedCharacterIndex = 2;
            editRegion.SetSelectedCharacterLocks(true);
            editRegion.SelectedCharacterIndex = 4;
            vm.OverlayItems.Add(editRegion);
            vm.PreviewImage = horizontalImage;
            vm.EditUnitIndex = (int)OcrEditUnit.Character;
            vm.SetOverlaySelection([editRegion], editRegion);
            editRegion.SelectedCharacterIndex = 4;
            var before = editRegion.Capture();
            Check(vm.EstimateCharacterSuffixAdvancesCommand.CanExecute(null), "Suffix adjustment is available in character mode.");
            vm.EstimateCharacterSuffixAdvancesCommand.Execute(null);
            var after = editRegion.Capture();
            Check(Math.Abs(editRegion.CharacterCells[12].Left - 240) <= 3, "A single suffix command anchors the title end without manual character stepping.");
            Check(editRegion.CharacterCells.Take(4).All(cell => Math.Abs(cell.Width - 20) < 0.001) &&
                editRegion.CharacterCells[2].IsLocked, "The prefix and locked character keep their original position and width.");
            Check(editRegion.SelectedCharacterIndex == 4, "Automatic adjustment does not advance the selected character.");
            vm.UndoCommand.Execute(null);
            Check(EquivalentCharacterSnapshot(editRegion.Capture(), before), "One Undo restores the entire suffix operation.");
            vm.RedoCommand.Execute(null);
            Check(EquivalentCharacterSnapshot(editRegion.Capture(), after), "One Redo restores the anchored suffix operation.");
            vm.EstimateCharacterSuffixAdvancesCommand.Execute(null);
            measurements.Add($"suffixRepeat: previousExtent={after.Width}; currentExtent={editRegion.Width}; previousAdvances={after.CharacterAdvancesText}; currentAdvances={editRegion.Capture().CharacterAdvancesText}");
            Check(EquivalentCharacterSnapshot(editRegion.Capture(), after), "Repeating the same anchored operation does not accumulate drift.");

            // Allocation is checked after the large fixture has been created; a
            // page-sized byte copy (formerly 32 MiB per line) would fail this bound.
            var largePixels = Enumerable.Repeat((byte)255, 4096 * 2048 * 4).ToArray();
            for (var row = 0; row < 24; row++)
                Buffer.BlockCopy(horizontalPixels, row * horizontalImage.PixelWidth * 4,
                    largePixels, ((1024 + row) * 4096 + 256) * 4, horizontalImage.PixelWidth * 4);
            var largeImage = BitmapSource.Create(4096, 2048, 96, 96, PixelFormats.Bgra32, null, largePixels, 4096 * 4);
            largeImage.Freeze();
            var cropRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(horizontalRegion.Text,
                256, 1024, horizontalImage.PixelWidth, 24, true));
            var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            var cropped = CharacterAdvanceEstimator.Estimate(largeImage, cropRegion);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            measurements.Add($"largePage=4096x2048; region={horizontalImage.PixelWidth}x24; allocatedBytes={allocated}");
            Check(allocated < 2 * 1024 * 1024, "Per-line estimation does not allocate a full-page pixel buffer.");
            Check(cropped.Advances.SequenceEqual(horizontal.Advances), "Cropped page sampling preserves the original estimator's coordinates.");
            CheckCroppedSampling(Check);
            File.WriteAllLines(Path.Combine(directory, "measurements.txt"), measurements);
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
            _diagnostics?.Write("character-estimation-test.pass", $"Checks: {checks.Count}; {directory}");
            Shutdown(0);
        }
        catch (Exception error)
        {
            if (directory is not null && Directory.Exists(directory))
            {
                File.WriteAllLines(Path.Combine(directory, "measurements.txt"), measurements);
                File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
                File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString());
            }
            _diagnostics?.Write("character-estimation-test.failed", error.ToString());
            Shutdown(1);
        }
    }

    private static bool EquivalentCharacterSnapshot(OverlayRegionSnapshot actual, OverlayRegionSnapshot expected) =>
        Math.Abs(actual.Left - expected.Left) < 0.000001 && Math.Abs(actual.Top - expected.Top) < 0.000001 &&
        Math.Abs(actual.Width - expected.Width) < 0.000001 && Math.Abs(actual.Height - expected.Height) < 0.000001 &&
        actual with { Left = 0, Top = 0, Width = 0, Height = 0 } ==
        expected with { Left = 0, Top = 0, Width = 0, Height = 0 };

    private static void CheckCroppedSampling(Action<bool, string> check)
    {
        // Compare the cropped sampler against the same sampler supplied with the
        // full page. Reflection is confined to diagnostics, not normal operations.
        var pixels = Enumerable.Range(0, 80 * 80 * 4).Select(index => (byte)(index % 251)).ToArray();
        var image = BitmapSource.Create(80, 80, 96, 96, PixelFormats.Bgra32, null, pixels, 80 * 4);
        image.Freeze();
        var read = typeof(CharacterAdvanceEstimator).GetMethod("ReadRegionPixels", BindingFlags.Static | BindingFlags.NonPublic)!;
        var sample = typeof(CharacterAdvanceEstimator).GetMethod("SampleRectifiedRegion", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (left, top, rotation, vertical) in new[]
                 { (30, 20, 0d, false), (0, 0, 35d, false), (70, 70, -70d, false), (20, 20, 90d, true) })
        {
            var region = new OverlayRegionViewModel(new PdfTextOverlayRegion("AB", left, top,
                40, 24, true, IsVertical: vertical, RotationDegrees: rotation));
            var crop = read.Invoke(null, [image, region])!;
            object Property(string name) => crop.GetType().GetProperty(name)!.GetValue(crop)!;
            var full = (byte[])sample.Invoke(null, [pixels, 80, 80, region, 40, 24, 0, 0])!;
            var actual = (byte[])sample.Invoke(null,
                [Property("Pixels"), Property("Width"), Property("Height"), region, 40, 24, Property("Left"), Property("Top")])!;
            check(full.SequenceEqual(actual), $"Cropped sampling matches full-page sampling: rotation={rotation}, vertical={vertical}, origin={left},{top}.");
            if (rotation == 0)
            {
                var direct = new byte[40 * 24 * 4];
                image.CopyPixels(new System.Windows.Int32Rect(left, top, 40, 24), direct, 40 * 4, 0);
                check(full.SequenceEqual(direct), "An unrotated region preserves every original raster column and row.");
                var near = new OverlayRegionViewModel(new PdfTextOverlayRegion("AB", left + 1e-8, top + 1e-8,
                    40 + 1e-8, 24 + 1e-8, true));
                var perturbed = (byte[])sample.Invoke(null, [pixels, 80, 80, near, 40, 24, 0, 0])!;
                check(full.SequenceEqual(perturbed), "Subpixel geometry round-off does not flip half-pixel samples.");
            }
        }
    }

    private static (BitmapSource Image, OverlayRegionViewModel Region) CreateLeaderFixture(int pitch)
    {
        const string title = "コントローラーを作成する";
        const int height = 24;
        var width = title.Length * 20 + 50 * pitch + 36;
        var pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        void Ink(int left, int top, int right, int bottom)
        {
            for (var y = top; y < bottom; y++)
                for (var x = left; x < right; x++)
                {
                    var offset = (y * width + x) * 4;
                    pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 0;
                }
        }
        for (var index = 0; index < title.Length; index++)
            Ink(index * 20 + 2, title[index] == 'ー' ? 11 : 3,
                index * 20 + 18, title[index] == 'ー' ? 13 : 21);
        for (var index = 0; index < 50; index++)
            Ink(240 + index * pitch + pitch / 2 - 1, 18, 240 + index * pitch + pitch / 2 + 1, 20);
        for (var index = 0; index < 3; index++)
            Ink(240 + 50 * pitch + index * 12 + 2, 3, 240 + 50 * pitch + index * 12 + 10, 21);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return (bitmap, new OverlayRegionViewModel(new PdfTextOverlayRegion(
            title + new string('.', 50) + "124", 0, 0, width, height, true)));
    }
}
