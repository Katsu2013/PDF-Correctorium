using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunPersistenceTestAsync(MainWindow window, string[] arguments, int optionIndex)
    {
        string? directory = null;
        var checks = new List<string>();
        try
        {
            if (arguments.Length <= optionIndex + 1) throw new ArgumentException("A new output directory is required.");
            directory = Path.GetFullPath(arguments[optionIndex + 1]);
            if (Directory.Exists(directory)) throw new IOException("Output directory already exists.");
            Directory.CreateDirectory(directory);
            var paths = new ApplicationPaths(StorageMode.Portable, Path.Combine(directory, "config"),
                Path.Combine(directory, "logs"), Path.Combine(directory, "cache"), Path.Combine(directory, "work"));
            ApplicationPathResolver.EnsureDirectories(paths);
            var packages = new ProjectPackageService();
            var vm = new MainWindowViewModel(packages, new PdfPreviewService(), new PdfExportService(),
                new NdlOcrCompanionService(), new DiagnosticLog(paths.LogDirectory), paths, () => { });
            window.DataContext = vm;
            window.ClosePromptOverride = () => MessageBoxResult.No;
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
            var errors = new List<string>();
            vm.ErrorDialogOverride = (message, ex) => errors.Add(message + ex.Message);
            void Check(bool value, string message)
            {
                if (!value) throw new InvalidOperationException(message);
                checks.Add("PASS: " + message);
            }
            void SetField(string name, object value) => typeof(MainWindowViewModel)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

            var pdf = Path.Combine(directory, "source.pdf");
            WriteDocumentUiTestPdf(pdf);
            var geometry = new TextGeometry { LocalBounds = new PdfRectangle(new PdfPoint(20, 250), new PdfSize(180, 20)), RotationCenter = new PdfPoint(110, 260) };
            var sourceRegion = new OcrTextRegion
            {
                OriginalText = "ABCDEF", OriginalGeometry = geometry, EditedGeometry = geometry,
                ParentRegionId = Guid.NewGuid(), FitMode = FitMode.Distribute,
                Output = new OutputAttributes { IncludeInSearch = false, IncludeInCopy = false, IncludeInSpeech = false, IncludeInPdf = false },
                FlowDirection = TextFlowDirection.RightToLeft, HasExplicitWritingMode = false,
            };
            var project = new PdfCorrectoriumProject
            {
                SourcePdf = await packages.CreateSourceReferenceAsync(pdf, directory),
                Pages = [new OcrPage { PageNumber = 1, WidthPoints = 300, HeightPoints = 400,
                    TextRegions = [sourceRegion], ReadingOrder = [sourceRegion.Id] },
                    new OcrPage { PageNumber = 2, WidthPoints = 300, HeightPoints = 400,
                    TextRegions = [sourceRegion with { Id = Guid.NewGuid() }] }],
            };
            var input = Path.Combine(directory, "input.pdfocrproj");
            await packages.SaveAsync(input, project);
            Check(await vm.OpenDocumentPathAsync(input), "Fixture project opens.");
            var region = vm.OverlayItems.Single();
            region.Text = "UNSAVED";
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.Cancel;
            Check(!await vm.OpenDocumentPathAsync(pdf), "Cancel stops document switching.");
            Check(ReferenceEquals(region, vm.OverlayItems.Single()) && region.Text == "UNSAVED" && vm.HasUnsavedChanges && vm.UndoCommand.CanExecute(null), "Cancel preserves edits, identity and Undo.");
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.Yes;
            vm.SaveBeforeSwitchOverride = () => Task.FromResult(false);
            Check(!await vm.OpenDocumentPathAsync(pdf) && vm.HasUnsavedChanges, "Canceled or failed save prevents switching.");
            vm.SaveBeforeSwitchOverride = null;
            Check(await vm.OpenDocumentPathAsync(pdf), "Successful Save then Open loads the new PDF.");
            Check((await packages.OpenAsync(input)).Pages[0].TextRegions[0].EffectiveText == "UNSAVED", "Save then Open preserves the prior text on disk.");
            await packages.SaveAsync(input, project);
            await vm.LoadProjectForDiagnosticsAsync(input);
            var pendingCommitted = false;
            vm.CommitPendingInputs = () => { pendingCommitted = true; vm.OverlayItems.Single().Text = "PENDING"; };
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.Cancel;
            Check(!await vm.OpenDocumentPathAsync(pdf) && pendingCommitted && vm.HasUnsavedChanges, "Pending editor input commits before dirty-state confirmation.");
            vm.CommitPendingInputs = null;
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
            var priorImage = vm.PreviewImage;
            var priorRegion = vm.OverlayItems.Single();
            var invalidPdf = Path.Combine(directory, "broken.pdf");
            await File.WriteAllTextAsync(invalidPdf, "not a PDF");
            Check(!await vm.OpenDocumentPathAsync(invalidPdf), "Malformed PDF fails replacement.");
            Check(ReferenceEquals(priorImage, vm.PreviewImage) && ReferenceEquals(priorRegion, vm.OverlayItems.Single()) && vm.HasDocument && vm.HasUnsavedChanges && vm.UndoCommand.CanExecute(null), "Failed replacement preserves preview, edits, Undo and document availability.");
            var missingProject = Path.Combine(directory, "missing-source.pdfocrproj");
            await packages.SaveAsync(missingProject, project with { SourcePdf = project.SourcePdf with { FileName = "missing.pdf", RelativePath = "missing.pdf", AbsolutePathHint = Path.Combine(directory, "missing.pdf") } });
            Check(!await vm.OpenDocumentPathAsync(missingProject) && ReferenceEquals(priorRegion, vm.OverlayItems.Single()), "Missing project source preserves the previous document.");
            Check(await vm.OpenDocumentPathAsync(pdf) && !vm.HasUnsavedChanges, "Explicit Discard allows a successful switch.");

            await vm.LoadProjectForDiagnosticsAsync(input);
            var resaved = Path.Combine(directory, "resaved.pdfocrproj");
            await vm.SaveProjectForDiagnosticsAsync(resaved);
            var restoredProject = await packages.OpenAsync(resaved);
            foreach (var restored in restoredProject.Pages.SelectMany(p => p.TextRegions))
            {
                Check(restored.ParentRegionId == sourceRegion.ParentRegionId && restored.FitMode == sourceRegion.FitMode, "Parent and fit metadata survive loaded/unvisited page round-trip.");
                Check(restored.Output == sourceRegion.Output && restored.FlowDirection == sourceRegion.FlowDirection, "All output flags and logical flow survive round-trip.");
                Check(restored.HasExplicitWritingMode == sourceRegion.HasExplicitWritingMode && restored.OriginalWritingMode == sourceRegion.OriginalWritingMode, "Explicit and original writing metadata are preserved.");
            }
            region = vm.OverlayItems.Single();
            region.Text = "";
            vm.UndoCommand.Execute(null);
            Check(region.Text == "ABCDEF", "Undo restores text before empty edit.");
            vm.RedoCommand.Execute(null);
            Check(region.Text == "", "Redo restores intentional empty edit.");
            await vm.SaveProjectForDiagnosticsAsync(resaved);
            await vm.LoadProjectForDiagnosticsAsync(resaved);
            Check(vm.OverlayItems.Single().Text == "", "Empty OCR text survives project save/reopen.");

            await vm.LoadProjectForDiagnosticsAsync(input);
            vm.EditorModeIndex = 2;
            region = vm.OverlayItems.Single();
            var width = region.Width;
            var sample = new OcrQualitySample(1, region.Id, region.Text, region.Width, region.Height, false, false, false, []);
            OcrKeywordWidthCandidate[] candidates = [new(sample, "ABCDEF", 0, 6, width, width * 1.5, 1, 1.5, 50)];
            Check(vm.ApplyKeywordWidthCorrections(candidates) == 0 && region.Width == width, "Quality corrections cannot bypass review-mode geometry protection.");
            var analysis = new OcrQualityAnalysisWindow(vm);
            ((TabControl)analysis.FindName("AnalysisTabs")).SelectedIndex = 1;
            var analysisContent = (FrameworkElement)analysis.Content;
            analysisContent.Measure(new Size(1000, 700));
            analysisContent.Arrange(new Rect(0, 0, 1000, 700));
            analysisContent.UpdateLayout();
            Button[] correctionButtons = [(Button)analysis.FindName("CorrectSelectedKeywordButton"), (Button)analysis.FindName("CorrectAllKeywordButton")];
            Check(correctionButtons.Length == 2 && correctionButtons.All(b => !b.IsEnabled), "Both quality correction buttons are disabled in review mode.");
            analysis.Close();
            var options = new OcrTextSearchOptions("ABC", InvisibleOnly: false);
            Check(vm.ReplaceAllOcrSearchMatches(await vm.SearchOcrTextAsync(options), options, "XYZ") == 2, "Bulk replace processes both pages.");
            Check(region.ReviewStatus == ReviewStatus.NeedsReview && vm.ReviewItems.Count == 1, "Bulk replacements remain in the default review filter.");
            vm.UndoCommand.Execute(null);
            Check(region.Text == "ABCDEF" && region.ReviewStatus == ReviewStatus.Unreviewed, "Bulk Undo restores text and review status.");
            vm.RedoCommand.Execute(null);
            await vm.SaveProjectForDiagnosticsAsync(resaved);
            Check((await packages.OpenAsync(resaved)).Pages.All(p => p.TextRegions.All(r => r.ReviewStatus == ReviewStatus.NeedsReview)), "NeedsReview survives save on all replaced pages.");

            await vm.LoadProjectForDiagnosticsAsync(input);
            SetField("_applicationSettings", new ApplicationSettings { AutoSaveEnabled = true, AutoSaveIntervalMinutes = 5 });
            vm.OverlayItems.Single().Text = "IDLE";
            await vm.AutoSaveIfDueAsync();
            Check(!File.Exists(vm.AutoSaveRecoveryPath), "Autosave does not run immediately after editing.");
            SetField("_lastUserActivityAtUtc", DateTimeOffset.UtcNow.AddSeconds(-31));
            await vm.AutoSaveIfDueAsync();
            Check(File.Exists(vm.AutoSaveRecoveryPath) && vm.HasUnsavedChanges, "Idle autosave writes recovery without marking the project saved.");
            Check((await packages.OpenAsync(vm.AutoSaveRecoveryPath!)).Pages[0].TextRegions[0].EffectiveText == "IDLE", "Idle recovery contains the latest text.");
            var idleWrite = File.GetLastWriteTimeUtc(vm.AutoSaveRecoveryPath!);
            await vm.AutoSaveIfDueAsync();
            Check(File.GetLastWriteTimeUtc(vm.AutoSaveRecoveryPath!) == idleWrite, "Unchanged edit state is not autosaved repeatedly.");
            vm.OverlayItems.Single().Text = "DISABLED";
            SetField("_applicationSettings", new ApplicationSettings { AutoSaveEnabled = false });
            SetField("_lastUserActivityAtUtc", DateTimeOffset.UtcNow.AddSeconds(-31));
            await vm.AutoSaveIfDueAsync();
            Check((await packages.OpenAsync(vm.AutoSaveRecoveryPath!)).Pages[0].TextRegions[0].EffectiveText == "IDLE", "Disabling autosave prevents idle writes.");
            SetField("_applicationSettings", new ApplicationSettings { AutoSaveEnabled = true });
            await vm.LoadPdfForDiagnosticsAsync(pdf);
            vm.UpdateDocumentProperties(vm.CurrentViewerSettings, new PdfDocumentMetadata { Title = "New unsaved document" }, vm.CurrentOutputPdfVersion, "ja-JP");
            SetField("_lastUserActivityAtUtc", DateTimeOffset.UtcNow.AddSeconds(-31));
            await vm.AutoSaveIfDueAsync();
            var neverSavedRecovery = vm.AutoSaveRecoveryPath!;
            Check(File.Exists(neverSavedRecovery), "Never-saved project gets a recovery package.");
            var recovered = await packages.OpenAsync(neverSavedRecovery);
            Check(recovered.SourcePdf.IsEmbedded && recovered.DocumentMetadata?.Title == "New unsaved document", "Never-saved recovery embeds the source and document properties.");
            Check(await vm.OpenDocumentPathAsync(neverSavedRecovery) && vm.HasDocument, "Never-saved recovery can be opened as a project.");
            var embeddedCopy = Path.Combine(directory, "embedded-resaved.pdfocrproj");
            await vm.SaveProjectForDiagnosticsAsync(embeddedCopy);
            Check((await packages.OpenAsync(embeddedCopy)).SourcePdf.IsEmbedded, "Re-saving an embedded project retains its embedded source.");

            var textPdf = Path.Combine(directory, "text-source.pdf");
            WritePersistenceTextPdf(textPdf);
            vm.EditorModeIndex = 0;
            Check(await vm.OpenDocumentPathAsync(textPdf), "PDF with real invisible text opens.");
            Check(vm.OverlayItems.Count > 0 && string.Concat(vm.OverlayItems.Select(r => r.Text)).Contains("ABCDEF"), "Fixture has extractable source text before editing.");
            foreach (var textRegion in vm.OverlayItems) textRegion.Text = "";
            var emptyProject = Path.Combine(directory, "empty-export.pdfocrproj");
            await vm.SaveProjectForDiagnosticsAsync(emptyProject);
            await vm.LoadProjectForDiagnosticsAsync(emptyProject);
            var exported = Path.Combine(directory, "empty-export.pdf");
            await vm.ExportPdfForDiagnosticsAsync(exported);
            var outputPreview = await new PdfPreviewService().RenderPageAsync(exported, 1);
            Check(outputPreview.TextRegions.All(r => !r.Text.Contains("ABCDEF")), "Cleared text does not reappear in extracted PDF output.");

            await File.WriteAllLinesAsync(Path.Combine(directory, "checks.txt"), checks);
            _diagnostics?.Write("persistence-test.passed", $"{checks.Count} checks passed. {directory}");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            if (directory is not null)
                await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), string.Join(Environment.NewLine, checks) + Environment.NewLine + ex);
            _diagnostics?.Write("persistence-test.failed", ex.ToString());
            Shutdown(1);
        }
    }

    private static void WritePersistenceTextPdf(string path)
    {
        var content = "BT /F1 18 Tf 3 Tr 25 300 Td (ABCDEF) Tj ET\n";
        string[] objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream"
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
}
