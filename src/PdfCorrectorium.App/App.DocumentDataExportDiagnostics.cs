using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Resources;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunDocumentDataExportTestAsync(MainWindow window, string[] args, int option)
    {
        if (args.Length <= option + 1) throw new ArgumentException("Use a new document-data diagnostic directory.");
        var root = Path.GetFullPath(args[option + 1]);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any(path => Path.GetFileName(path) != ".appstate"))
            throw new IOException("Use a new diagnostic directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool success, string message)
        {
            if (!success) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
        }
        async Task Reject(Func<Task> action, string message)
        {
            var rejected = false;
            try { await action(); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or OperationCanceledException)
            { rejected = true; }
            Check(rejected, message);
        }
        try
        {
            window.ClosePromptOverride = () => MessageBoxResult.No;
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = -10000; window.Top = -10000; window.Show();
            var vm = (MainWindowViewModel)window.DataContext;
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
            vm.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            var source = Path.Combine(root, "source.pdf");
            await BlankPdfService.CreateAsync(source, 3, 595, 842);
            var original = await File.ReadAllBytesAsync(source);
            await vm.OpenDocumentPathAsync(source); await vm.EnsureDocumentMetadataAsync();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            vm.EditorMode = EditorInteractionMode.OcrEditing;
            var late = vm.AddManualOcrRegion(new Rect(40, 130, 140, 24))!; late.Text = "後の行"; late.ReadingOrder = 2;
            var early = vm.AddManualOcrRegion(new Rect(40, 100, 140, 24))!; early.Text = "先の行 123"; early.ReadingOrder = 1;
            var deleted = vm.AddManualOcrRegion(new Rect(40, 160, 140, 24))!; deleted.Text = "削除した文字"; deleted.IsDeleted = true;
            var undo = vm.UndoCountForDiagnostics; var dirty = vm.HasUnsavedChanges;
            var textFile = Path.Combine(root, "ocr.txt");
            await vm.ExportDocumentDataAsync(DocumentDataExportKind.OcrText, textFile, [1, 2, 3], true, false, 150, 90, null, default);
            var text = await File.ReadAllTextAsync(textFile);
            Check(text.IndexOf("先の行", StringComparison.Ordinal) >= 0 && text.IndexOf("先の行", StringComparison.Ordinal) < text.IndexOf("後の行", StringComparison.Ordinal), "TXT uses current reading order.");
            Check(text.Contains("先の行 123") && !text.Contains("削除した文字"), "TXT uses edited text and excludes deleted regions.");
            Check(text.Contains("--- PDF 1 ---") && text.Contains("--- PDF 3 ---") && text.Contains('\f'), "TXT preserves page boundaries including pages without OCR.");
            Check(vm.UndoCountForDiagnostics == undo && vm.HasUnsavedChanges == dirty, "Data export does not add edits or alter saved state.");
            Check(!vm.IsPdfExporting && vm.CanEditDocument, "Export releases the document editing guard.");
            var target = vm.ProjectForDiagnostics!.PageSequence[0].PageId;
            var tag = new ProjectTag { Name = "タグ,確認" };
            var comment = new ProjectComment { Target = new() { Kind = ProjectTargetKind.Page, PageId = target },
                Body = "=HYPERLINK(\"bad\")\r\n二行目", TagIds = [tag.Id], Author = " +SUM(1,2)", State = ProjectCommentState.Resolved };
            var project = vm.ProjectForDiagnostics with { Tags = [tag], Comments = [comment] };
            var csvFile = Path.Combine(root, "comments.csv");
            await DocumentDataWriter.WriteAtomicAsync(csvFile, [source], stream => DocumentDataWriter.WriteCommentsAsync(stream, project, default), default);
            var csvBytes = await File.ReadAllBytesAsync(csvFile); var csv = Encoding.UTF8.GetString(csvBytes);
            Check(csvBytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "CSV is UTF-8 with BOM.");
            Check(csv.Contains("\"'=HYPERLINK(\"\"bad\"\")\r\n二行目\"") && csv.Contains("タグ,確認"), "CSV quotes multiline text, quotes and tag delimiters.");
            Check(csv.Contains("' +SUM(1,2)") && csv.Contains("Resolved"), "CSV protects formula prefixes and exports comment state.");
            foreach (var value in new[] { "=1+1", " +1", "\t-1", "\uFEFF@SUM(1)" })
                Check(DocumentDataWriter.CsvCell(value).StartsWith("\"'", StringComparison.Ordinal), "CSV formula-prefix protection: " + value.Length);
            var protectedFile = Path.Combine(root, "input.txt"); await File.WriteAllTextAsync(protectedFile, "unchanged");
            await Reject(() => DocumentDataWriter.WriteAtomicAsync(protectedFile, [protectedFile], _ => Task.CompletedTask, default), "Input text paths cannot be overwritten.");
            Check(await File.ReadAllTextAsync(protectedFile) == "unchanged", "Protected input remains intact.");
            var previous = Path.Combine(root, "previous.csv"); await File.WriteAllTextAsync(previous, "previous");
            await Reject(() => DocumentDataWriter.WriteAtomicAsync(previous, [], _ => throw new OperationCanceledException(), default), "Canceled writes never commit a partial file.");
            Check(await File.ReadAllTextAsync(previous) == "previous" && !Directory.EnumerateFiles(root, ".pdf-correctorium-data-*.tmp").Any(), "Cancellation preserves old output and removes the temporary file.");
            var regex = new DocumentRegexSearch("[0-9]+", true);
            Check(regex.Find("甲12乙345", default).SequenceEqual(new[] { (1, 2), (4, 3) }), "Regex returns exact UTF-16 ranges.");
            Check(new DocumentRegexSearch("abc", false).Find("ABC", default).Count() == 1, "Regex case-insensitive option is explicit.");
            await Reject(() => { _ = new DocumentRegexSearch("[", true); return Task.CompletedTask; }, "Invalid regex is rejected before document processing.");
            await Reject(() => { _ = new DocumentRegexSearch("a*", true); return Task.CompletedTask; }, "Empty-string matching regex is rejected.");
            await Reject(() => { _ = new DocumentRegexSearch(new string('a', 1025), true); return Task.CompletedTask; }, "Oversized regex is rejected.");
            await Reject(() => { _ = new DocumentRegexSearch("(?=a)", true).Find("a", default).ToArray(); return Task.CompletedTask; }, "Context-dependent zero-length matches reject the complete result.");
            await Reject(() => { _ = new DocumentRegexSearch("(a+)+$", true).Find(new string('a', 200000) + "!", default).ToArray(); return Task.CompletedTask; }, "Pathological regex is bounded by timeout.");
            await Reject(() => { _ = new DocumentRegexSearch("1", true).Find(new string('1', 10001), default).ToArray(); return Task.CompletedTask; }, "Regex result count is bounded.");
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                await Reject(() => { _ = new DocumentRegexSearch("a", true).Find("abc", canceled.Token).ToArray(); return Task.CompletedTask; }, "Regex observes cancellation.");
            }
            var run = new DocumentTextRunService.Run("𠮷", [new("𠮷", 1, 1, 10, 20, true)]);
            Check(run.Slice(0, 1).Count == 1 && run.Slice(1, 1).Count == 1, "Partial surrogate matches map to the complete glyph.");
            var candidates = await vm.BuildBatchCandidatesAsync(DocumentBatchKind.TextRedaction, [1], "[0-9]+", 0, true, null, default, useRegularExpression: true);
            Check(candidates.Count > 0 && candidates.All(candidate => candidate.Redactions.Count > 0), "Regex integrates with existing redaction candidates.");
            var beforeRedact = vm.UndoCountForDiagnostics;
            vm.ApplyBatchCandidates(vm.BatchProjectId!.Value, candidates);
            Check(vm.UndoCountForDiagnostics == beforeRedact + 1, "Reviewed regex redactions apply as one Undo.");
            vm.UndoCommand.Execute(null); Check(vm.UndoCountForDiagnostics == beforeRedact, "Regex redactions undo together.");
            foreach (var kind in Enum.GetValues<DocumentDataExportKind>())
            {
                var dialog = new DocumentDataExportWindow(vm, kind) { Owner = window, ShowActivated = false, ShowInTaskbar = false };
                dialog.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var warning = (Border)dialog.FindName("WorkDataWarning");
                Check(warning.Visibility == (kind == DocumentDataExportKind.PageImages ? Visibility.Collapsed : Visibility.Visible), "Warning follows export kind: " + kind);
                Check(((StackPanel)dialog.FindName("DialogFooter")).Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<Button>()).All(button => ActionIcon.GetKey(button).Length == 0), "Export footer stays icon-free: " + kind);
                SaveIconDiagnosticImage((FrameworkElement)dialog.Content, Path.Combine(root, "dialog-" + kind + ".png")); dialog.Close();
            }
            using (var worker = new PdfNativeWorkerClient(reuseInputs: true))
            {
                var first = await worker.RenderImagePageAsync(source, 1, 72, default);
                var second = await worker.RenderImagePageAsync(source, 2, 150, default);
                Check(first.PixelWidth == 595 && first.PixelHeight == 842 && second.PixelWidth == 1240 && second.PixelHeight == 1755, "Image pixels follow DPI without preview clipping.");
                Check(worker.InputStageCount == 1 && worker.WorkerHasResourceJob, "Image session stages the input once and retains sandbox resource limits.");
            }
            var packages = new ProjectPackageService();
            var imageProject = project with
            {
                SourcePdf = (await packages.CreateSourceReferenceAsync(source, root)) with { PageCount = 3 },
                Pages = project.Pages.Select(page => page with { TextRegions = [], ReadingOrder = [] }).ToArray(),
                Redactions = [new() { PageId = target, Bounds = new PdfRectangle(new PdfPoint(10, 10), new PdfSize(50, 50)), ColorHex = "#000000" }],
                TextDecorations = [new() { Kind = PdfTextDecorationKind.Header, TextTemplate = "EXPORT", Position = PdfTextDecorationPosition.TopCenter, ColorHex = "#FF0000", FontSizePoints = 20 }]
            };
            var package = Path.Combine(root, "image-fixture.pdfocrproj");
            await packages.SaveAsync(package, imageProject, embedSourcePdf: false);
            await vm.OpenDocumentPathAsync(package); await vm.EnsureDocumentMetadataAsync();
            var output = await vm.ExportDocumentDataAsync(DocumentDataExportKind.PageImages, root, [1], true, false, 72, 90, null, default);
            var frame = BitmapFrame.Create(new Uri(Path.Combine(output, "page-000001.png")), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var rgba = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0); var bytes = new byte[rgba.PixelWidth * rgba.PixelHeight * 4]; rgba.CopyPixels(bytes, rgba.PixelWidth * 4, 0);
            var sample = ((rgba.PixelHeight - 25) * rgba.PixelWidth + 25) * 4;
            Check(bytes[sample] < 10 && bytes[sample + 1] < 10 && bytes[sample + 2] < 10, "PNG burns redaction as opaque black, not an editor outline.");
            Check(Enumerable.Range(0, bytes.Length / 4).Any(index => bytes[index * 4 + 2] > 150 && bytes[index * 4 + 1] < 100), "PNG includes the exported text decoration.");
            var jpeg = await vm.ExportDocumentDataAsync(DocumentDataExportKind.PageImages, root, [2], true, true, 150, 85, null, default);
            Check(File.ReadAllBytes(Path.Combine(jpeg, "page-000002.jpg")).Take(2).SequenceEqual(new byte[] { 255, 216 }), "JPEG output has real JPEG data in a distinct folder.");
            var directories = Directory.GetDirectories(root, "PdfCorrectorium-images-*").Length;
            using (var cancel = new CancellationTokenSource())
                await Reject(() => PageImageExportService.ExportAsync(source, root, [1, 2], 72, false, 90,
                    new DataDiagnosticProgress(message => { if (message.Contains("2/2")) cancel.Cancel(); }), cancel.Token), "Image export cancels after the first completed page.");
            Check(Directory.GetDirectories(root, "PdfCorrectorium-images-*").Length == directories, "Cancellation removes only the new incomplete image folder.");
            Check((await File.ReadAllBytesAsync(source)).SequenceEqual(original), "All exports preserve source PDF bytes.");
            Check(!vm.IsPdfExporting && vm.CanEditDocument, "Image export restores editing.");
            File.WriteAllLines(Path.Combine(root, "checks.txt"), checks); window.Close(); Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllLines(Path.Combine(root, "checks.txt"), checks); File.WriteAllText(Path.Combine(root, "failure.txt"), error.ToString());
            _diagnostics?.Write("document-data-test.failed", error.ToString()); Shutdown(1);
        }
    }
    private sealed class DataDiagnosticProgress(Action<string> action) : IProgress<string>
    {
        public void Report(string value) => action(value);
    }
}
