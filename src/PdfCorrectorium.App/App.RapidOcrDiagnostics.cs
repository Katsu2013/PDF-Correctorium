using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunRapidOcrTestAsync(MainWindow window, string[] arguments, int index)
    {
        var directory = Path.GetFullPath(arguments[index + 1]);
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add("PASS: " + message); }
        try
        {
            VerifyRapidOcrMargins(Check);
            VerifyRapidOcrAdjustment(Check);
            VerifyRapidOcrPadding(Check);
            VerifyRapidOcrMarginStability(Check, directory);
            window.ClosePromptOverride = () => MessageBoxResult.No;
            var vm = (MainWindowViewModel)window.DataContext;
            vm.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            var pdf = Path.Combine(directory, "rapid-source.pdf"); WriteRapidOcrPdf(pdf);
            var hash = SHA256.HashData(File.ReadAllBytes(pdf));
            // The fixture is our own ordinary PDF text; no screen capture or mocked OCR inference.
            var crop = new OcrLineCrop(72, 200, 1050, 120, 0, 1200, 1600, 0);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = await Task.Run(() => PdfNativeWorkerClient.Ocr.RecognizeLineAsync(pdf, 1, crop, default));
            var firstMs = clock.ElapsedMilliseconds;
            Check(result.Recognition.Reading.Text.Contains("12345", StringComparison.Ordinal), "Real bundled Japanese model recognizes the source PDF digits.");
            Check(OcrLineReconciler.Valid(result.Recognition.Reading, OcrLineReconciler.Elements(result.Recognition.Reading.Text).Length), "Real inference supplies valid per-character evidence.");
            Check(typeof(RapidLineRecognition).GetProperty("Second") is null && typeof(RapidLineRecognition).GetProperty("Reading") is not null,
                "The worker contract carries one recognized candidate, not an alternative image result.");
            Check(PdfNativeWorkerClient.Ocr.WorkerHasResourceJob, "OCR runs in the restricted resource job.");
            var process = PdfNativeWorkerClient.Ocr.WorkerProcessId; clock.Restart();
            var again = await Task.Run(() => PdfNativeWorkerClient.Ocr.RecognizeLineAsync(pdf, 1, crop, default));
            var secondMs = clock.ElapsedMilliseconds;
            Check(process == PdfNativeWorkerClient.Ocr.WorkerProcessId && PdfNativeWorkerClient.Ocr.InputStageCount == 1,
                "Repeated recognition reuses its process/model and stages the source PDF only once.");
            Check(result.Recognition.Reading.Text == again.Recognition.Reading.Text, "Repeated real recognition is deterministic.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result.Image));
            using (var stream = File.Create(Path.Combine(directory, "recognized-line.png"))) encoder.Save(stream);
            await File.WriteAllTextAsync(Path.Combine(directory, "real-recognition.json"), JsonSerializer.Serialize(new
            { result.Recognition, firstMs, secondMs, note = "Timings are informational; this is not a document accuracy benchmark." }));
            Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pdf))), "Re-recognition never modifies the source PDF.");
            var japanese = await Task.Run(() => PdfNativeWorkerClient.Ocr.RecognizeLineAsync(pdf, 1,
                new(80, 480, 1040, 80, 0, 1200, 1600, 0), default));
            await File.WriteAllTextAsync(Path.Combine(directory, "japanese-recognition.json"), JsonSerializer.Serialize(japanese.Recognition));
            Check(japanese.Recognition.Reading.Text.Contains("フレームワーク"), "Real Japanese page-image text is recognized by the bundled model.");
            Check(OcrLineReconciler.Evaluate("フレムワークを再認識します", japanese.Recognition.Reading,
                false, false, false).IsAutomatic, "Real single padded Japanese evidence qualifies the clear missing-character fixture for automatic completion.");
            var punctuationPdf = Path.Combine(directory, "rapid-punctuation.pdf");
            WriteRapidOcrPdf(punctuationPdf, "フレームワークを再認識します。");
            var preceding = new FormattedText("フレームワークを再認識します", System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                FlowDirection.LeftToRight, new Typeface("Yu Gothic"), 48, Brushes.Black, 1);
            // The old row ends BEFORE the full stop. White-only padding could not recover it.
            var punctuation = await Task.Run(() => PdfNativeWorkerClient.Ocr.RecognizeLineAsync(punctuationPdf, 1,
                new(80, 480, (4 + preceding.WidthIncludingTrailingWhitespace) * 1.04, 80, 0, 1200, 1600, 0), default));
            await File.WriteAllTextAsync(Path.Combine(directory, "punctuation-recognition.json"), JsonSerializer.Serialize(punctuation.Recognition));
            Check(punctuation.Recognition.Reading.Text == "フレームワークを再認識します。",
                "Real recognition retains a Japanese full stop outside the old OCR row bounds.");
            Check(!OcrLineReconciler.Evaluate("フレームワークを再認識します。", japanese.Recognition.Reading,
                false, false, false).IsAutomatic, "A recognition result missing existing punctuation can never be applied automatically.");
            var punctuationBefore = new OverlayRegionSnapshot("フレームワークを再認識します", 80, 480,
                (4 + preceding.WidthIncludingTrailingWhitespace) * 1.04, 80, 0);
            var punctuationFit = RapidOcrLineAdjuster.Prepare(punctuationBefore, punctuation, new());
            Check(punctuationFit.IsImageAdjusted && punctuationFit.Snapshot.Text.EndsWith('。') &&
                punctuationFit.Snapshot.Left + punctuationFit.Snapshot.Width > punctuationBefore.Left + punctuationBefore.Width,
                "Image fitting includes the recovered punctuation beyond the original row extent.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); var cancelled = false;
                try { await PdfNativeWorkerClient.Ocr.RecognizeLineAsync(pdf, 1, crop, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Recognition observes cancellation before starting new work.");
            }

            var id = Guid.NewGuid();
            var geometry = new TextGeometry { LocalBounds = new(new(20, 260), new(260, 20)), RotationCenter = new(150, 270),
                CharacterAdvances = Enumerable.Repeat(260d / 6, 6).ToArray() };
            var packages = new ProjectPackageService();
            var project = new PdfCorrectoriumProject { Name = "RapidOCR editing fixture", SourcePdf = await packages.CreateSourceReferenceAsync(pdf),
                Pages = [new OcrPage { Id = Guid.NewGuid(), PageNumber = 1, WidthPoints = 300, HeightPoints = 400,
                    TextRegions = [new OcrTextRegion { Id = id, OriginalText = "フレムワーク", OriginalGeometry = geometry, EditedGeometry = geometry }] }] };
            var projectPath = Path.Combine(directory, "rapid-project.pdfocrproj");
            await packages.SaveAsync(projectPath, project); await vm.LoadProjectForDiagnosticsAsync(projectPath);
            await vm.EnsureDocumentMetadataAsync();
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            vm.EditorModeIndex = 0; vm.EditUnitIndex = (int)OcrEditUnit.Line;
            var region = vm.OverlayItems.Single(); vm.SetOverlaySelection([region], region);
            Check(vm.CanRecognizeSelectedLine, "One unlocked horizontal OCR line enables recognition.");
            Check(((Button)window.FindName("RapidOcrLineButton")).Content.ToString()!.Contains("RapidOCR"), "Recognition has a distinct OCR property action.");
            var text = "フレームワーク"; var n = OcrLineReconciler.Elements(text).Length;
            var reading = new OcrLineReading(text, Enumerable.Repeat(.999, n).ToArray(),
                Enumerable.Range(0, n).Select(i => (i + .5) / n).ToArray(), Enumerable.Repeat(true, n).ToArray());
            // Capture a real-generation ticket through the normal method, but inject deterministic text
            // only AFTER real inference above so editing/undo edge cases do not depend on model accuracy.
            var realTicket = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No recognition ticket.");
            Check(realTicket.Adjustment.IsImageAdjusted && realTicket.Result.Recognition.Reading.Text == japanese.Recognition.Reading.Text,
                "The normal async recognition path prepares an image fit for the actual selected Japanese row.");
            var injected = realTicket.Result with { Recognition = realTicket.Result.Recognition with { Reading = reading } };
            var ticket = realTicket with { Result = injected,
                Completion = OcrLineReconciler.Evaluate(region.Text, reading, false, false, false),
                Adjustment = RapidOcrLineAdjuster.Prepare(realTicket.Before, injected, new()) };
            var before = region.Capture(); var originalText = region.OriginalText;
            vm.ShowOcrFrames = false;
            Check(vm.ApplyRapidOcrResult(ticket, true), "High-confidence insertion applies through the normal edit path.");
            Check(region.Text == text && region.OriginalText == originalText && region.ReviewStatus == ReviewStatus.Modified,
                "Insertion preserves original OCR and uses the existing modified status without requiring another confirmation.");
            var fitted = region.Capture();
            Check(fitted.Width == ticket.Adjustment.Snapshot.Width && fitted.Left == ticket.Adjustment.Snapshot.Left &&
                fitted.CharacterAdvancesText == ticket.Adjustment.Snapshot.CharacterAdvancesText && fitted.Height == before.Height &&
                fitted.RotationDegrees == before.RotationDegrees && fitted.ReadingOrder == before.ReadingOrder,
                "Automatic completion applies the prepared character fit while preserving height, rotation and reading order.");
            Check(region.CharacterCells.Count == n && region.CharacterCells.Any(c => c.IsSearchMatch), "Inserted characters have geometry and a marker even with OCR frames hidden.");
            Check(!vm.ApplyRapidOcrResult(ticket, true), "An already applied ticket cannot be replayed.");
            vm.UndoCommand.Execute(null);
            Check(region.Capture() == before, "One Undo restores the complete pre-recognition snapshot.");
            vm.RedoCommand.Execute(null);
            Check(region.Capture() == fitted, "One Redo restores both recognized text and image-adjusted geometry.");
            vm.UndoCommand.Execute(null);
            Check(!vm.ApplyRapidOcrResult(ticket, false), "Undo invalidates an old recognition ticket.");
            var fresh = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No fresh ticket.");
            await VerifyRapidOcrReviewAsync(window, vm, fresh, directory, Check);
            await VerifyRapidOcrLexiconAsync(window, vm, directory, Check);
            var reviewed = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No post-review ticket.");
            Check(!reviewed.Completion.IsAutomatic && !vm.ApplyRapidOcrResult(reviewed, true), "Large/ambiguous additions cannot bypass review.");
            var review = new RapidOcrReviewWindow(vm, reviewed) { Owner = window, ShowActivated = false };
            Exception? reviewError = null;
            review.Loaded += (_, _) => review.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var candidate = (TextBlock)review.FindName("CandidateText");
                    var button = (Button)review.FindName("ApplyButton");
                    Check(candidate.FontSize == 18 && PdfCorrectorium.App.Resources.ActionIcon.GetKey(button).Length == 0,
                        "Review has readable text and plain footer buttons.");
                    Check(review.FindName("SecondText") is null, "Review presents only the one padded recognition candidate.");
                    var raw = string.Concat(candidate.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text));
                    Check(raw == japanese.Recognition.Reading.Text, "Review displays the exact recognized document text.");
                    SaveIconDiagnosticImage((FrameworkElement)review.Content, Path.Combine(directory, "recognition-review.png"));
                    LocalizationService.SetLanguage(LocalizationService.EnglishLanguage); LocalizationService.Apply(review);
                    Check(review.Title.Contains("Review", StringComparison.Ordinal), "Review chrome switches to English.");
                    Check(string.Concat(candidate.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text)) == raw,
                        "Language changes never translate recognized text.");
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception error) { reviewError = error; review.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            Check(review.ShowDialog() == true && reviewError is null, "The reviewed candidate applies through the actual dialog action.");
            Check(region.Text == japanese.Recognition.Reading.Text && region.Width == reviewed.Adjustment.Snapshot.Width &&
                region.Capture().CharacterAdvancesText == reviewed.Adjustment.Snapshot.CharacterAdvancesText,
                "Reviewed replacement also applies the image-adjusted character geometry.");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveIconDiagnosticImage((FrameworkElement)window.Content, Path.Combine(directory, "fitted-row.png"));
            var reviewedState = region.Capture();
            var unchanged = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No unchanged-text ticket.");
            Check(unchanged.Result.Recognition.Reading.Text == region.Text && unchanged.Adjustment.IsImageAdjusted &&
                vm.ApplyRapidOcrResult(unchanged, false), "Unchanged recognized text still receives image fitting without a text-review dialog.");
            if (region.Capture() != reviewedState) vm.UndoCommand.Execute(null);
            Check(region.Capture() == reviewedState, "A same-text fitting edit is independently undoable without reverting the preceding text correction.");
            var repeatedMatches = true;
            for (var repeat = 0; repeat < 4; repeat++)
            {
                var repeated = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No repeated-recognition ticket.");
                repeatedMatches &= repeated.Result.Recognition.Reading.Text == reviewedState.Text && repeated.Adjustment.IsImageAdjusted &&
                    repeated.Adjustment.Snapshot == reviewedState && vm.ApplyRapidOcrResult(repeated, false) && region.Capture() == reviewedState;
            }
            Check(repeatedMatches, "Four real OCR/apply cycles preserve exact text and character geometry without margin growth.");
            var correctedPath = Path.Combine(directory, "recognized.pdfocrproj");
            await vm.SaveProjectForDiagnosticsAsync(correctedPath);
            var corrected = (await packages.OpenAsync(correctedPath)).Pages.Single().TextRegions.Single();
            Check(corrected.EffectiveText == japanese.Recognition.Reading.Text && corrected.OriginalText == originalText,
                "Recognized text and original OCR both survive project serialization.");
            Check(Math.Abs(corrected.EditedGeometry.LocalBounds.Size.Width - region.Width / 4) < .001 &&
                corrected.EditedGeometry.CharacterAdvances.Count == OcrLineReconciler.Elements(region.Text).Length,
                "Image-adjusted line extent and per-character sizes survive project serialization.");
            vm.UndoCommand.Execute(null); LocalizationService.SetLanguage("ja-JP");
            Check(region.Capture() == before, "One Undo restores a manually reviewed recognition edit.");
            var pending = vm.RecognizeSelectedLineAsync(default);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MainWindowViewModel).GetMethod("BeginBackgroundOperation", flags)!.Invoke(vm, ["Other diagnostic operation", true]);
            Check(await pending is null && vm.IsBackgroundOperationVisible && vm.BackgroundOperationMessage == "Other diagnostic operation",
                "A superseded OCR operation never clears or overwrites another operation's progress.");
            typeof(MainWindowViewModel).GetMethod("EndBackgroundOperation", flags)!.Invoke(vm, null);
            var changing = vm.RecognizeSelectedLineAsync(default);
            var intendedText = region.Text + "あ"; region.Text = intendedText;
            Check(await changing is null && region.Text == intendedText,
                "An edit during inference is preserved and the stale recognition result is discarded.");
            vm.UndoCommand.Execute(null);
            Check(region.Capture() == before, "Undo still restores the independent edit made during recognition.");
            var moving = vm.RecognizeSelectedLineAsync(default);
            region.Left += 1;
            Check(await moving is null, "In-flight RapidOCR still rejects geometry edits despite the separate text-analysis generation.");
            vm.UndoCommand.Execute(null);
            Check(region.Capture() == before, "Undo restores geometry changed during recognition without applying the stale OCR result.");
            region.IsGeometryLocked = true;
            Check(!vm.CanRecognizeSelectedLine, "Locked geometry cannot be re-recognized for editing.");
            region.IsGeometryLocked = false;
            region.SelectCharacter(0, false, false); region.SetSelectedCharacterLocks(true);
            Check(!vm.CanRecognizeSelectedLine, "Character locks also prevent recognition edits.");
            region.SetSelectedCharacterLocks(false);
            region.IsVertical = true;
            Check(!vm.CanRecognizeSelectedLine, "Unvalidated vertical text is not silently auto-completed.");
            region.IsVertical = false;
            Check(region.OriginalText == originalText, "Lock/writing-mode checks do not overwrite original text.");
            await vm.SaveProjectForDiagnosticsAsync(Path.Combine(directory, "saved.pdfocrproj"));
            using var archive = ZipFile.OpenRead(Path.Combine(directory, "saved.pdfocrproj"));
            using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
            Check(manifest.RootElement.GetProperty("applicationVersion").GetString() == PdfCorrectorium.Core.ApplicationBuildInfo.Version,
                "Saved manifest uses the running application revision.");
            Check(window.Title.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.Version) &&
                PdfCorrectorium.Core.ApplicationBuildInfo.AboutText.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.NumericVersion),
                "Title and About use the running product and numeric versions.");
            Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pdf))), "All recognition/edit/save tests leave the source PDF unchanged.");
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
            _diagnostics?.Write("rapid-ocr-test.passed", $"{checks.Count} checks. {directory}"); Shutdown(0);
        }
        catch (Exception error)
        { File.WriteAllText(Path.Combine(directory, "error.txt"), error.ToString()); _diagnostics?.Write("rapid-ocr-test.failed", error.ToString()); Shutdown(1); }
    }

    private static void VerifyRapidOcrMargins(Action<bool, string> check)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 240, 140));
            drawing.DrawRectangle(Brushes.Black, null, new Rect(30, 22, 105, 32));
            drawing.DrawRectangle(Brushes.Red, null, new Rect(150, 49, 3, 3));
            drawing.DrawRectangle(Brushes.Blue, null, new Rect(8, 49, 3, 3));
            drawing.DrawRectangle(Brushes.Green, null, new Rect(30, 72, 80, 4));
        }
        var page = new RenderTargetBitmap(240, 140, 96, 96, PixelFormats.Pbgra32); page.Render(visual); page.Freeze();
        var crop = new OcrLineCrop(20, 20, 120, 40, 0, 240, 140, 0);
        var output = RapidLineRecognizer.Crop(page, crop);
        var pixels = new byte[output.PixelWidth * output.PixelHeight * 4];
        output.CopyPixels(pixels, output.PixelWidth * 4, 0);
        bool Contains(byte[] samples, byte b, byte g, byte r) => Enumerable.Range(0, samples.Length / 4)
            .Any(i => samples[i * 4] == b && samples[i * 4 + 1] == g && samples[i * 4 + 2] == r && samples[i * 4 + 3] == 255);
        check(Contains(pixels, 0, 0, 255), "Source pixels beyond the old right edge retain trailing punctuation.");
        check(Contains(pixels, 255, 0, 0), "Source pixels beyond the old left edge retain leading punctuation.");
        check(!Contains(pixels, 0, 128, 0), "The smaller cross-axis margin excludes the adjacent line.");
        check(output.PixelWidth == 160 && output.PixelHeight == 64, "The expanded cross-axis source margin is 30% of line height.");
        var rotated = new TransformedBitmap(page, new RotateTransform(90)); rotated.Freeze();
        var rotatedCrop = new OcrLineCrop(40, 60, 120, 40, 90, 140, 240, 0);
        var rotatedOutput = RapidLineRecognizer.Crop(rotated, rotatedCrop);
        var rotatedPixels = new byte[rotatedOutput.PixelWidth * rotatedOutput.PixelHeight * 4];
        rotatedOutput.CopyPixels(rotatedPixels, rotatedOutput.PixelWidth * 4, 0);
        // WPF may resample edge pixels differently after two bitmap rotations;
        // the invariant is retained punctuation and excluded adjacent-line ink.
        check(rotatedOutput.PixelWidth == output.PixelWidth && rotatedOutput.PixelHeight == output.PixelHeight &&
            Contains(rotatedPixels, 0, 0, 255) && Contains(rotatedPixels, 255, 0, 0) && !Contains(rotatedPixels, 0, 128, 0),
            "Margins follow the local reading axis for rotated rows.");
        var edge = RapidLineRecognizer.Crop(page, crop with { Left = 0, Top = 0 });
        var edgePixels = new byte[edge.PixelWidth * edge.PixelHeight * 4]; edge.CopyPixels(edgePixels, edge.PixelWidth * 4, 0);
        check(edgePixels.Take(4).All(v => v == 255), "Sampling outside the page is safely filled with white.");
    }

    private static void WriteRapidOcrPdf(string path, string japaneseText = "フレームワークを再認識します")
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1000, 80));
            drawing.DrawText(new FormattedText(japaneseText, System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                FlowDirection.LeftToRight, new Typeface("Yu Gothic"), 48, Brushes.Black, 1), new Point(4, 4));
        }
        var bitmap = new RenderTargetBitmap(1000, 80, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var rgb = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var pixels = new byte[1000 * 80 * 3]; rgb.CopyPixels(pixels, 1000 * 3, 0);
        var hex = Convert.ToHexString(pixels) + ">";
        const string content = "BT /F1 20 Tf 25 325 Td (RapidOCR 12345) Tj ET\nq 260 0 0 20 20 260 cm /Im1 Do Q\n";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << /Font << /F1 4 0 R >> /XObject << /Im1 6 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {content.Length} >>\nstream\n{content}endstream",
            $"<< /Type /XObject /Subtype /Image /Width 1000 /Height 80 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream"];
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = pdf.Length; pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
}
