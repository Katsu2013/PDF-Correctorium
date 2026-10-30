using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunOcrNoticesTestAsync(MainWindow window, string[] args, int option)
    {
        string? directory = null;
        OcrQualityAnalysisWindow? analysis = null;
        var language = LocalizationService.CurrentLanguage;
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
            if (directory is not null) File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
        }
        try
        {
            if (args.Length <= option + 1) throw new ArgumentException("A new diagnostic directory is required.");
            directory = Path.GetFullPath(args[option + 1]);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory)
                .Any(path => Path.GetFileName(path) != ".appstate")) throw new IOException("Use a new diagnostic directory.");
            Directory.CreateDirectory(directory);
            window.ClosePromptOverride = () => MessageBoxResult.No;
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.Left = -10000; window.Top = -10000; window.Show();
            var vm = (MainWindowViewModel)window.DataContext;
            vm.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            async Task Layout()
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout(); analysis?.UpdateLayout();
            }
            foreach (var fixture in new[]
            {
                (Name: "invisible", Modes: new[] { 3 }, Nested: false, Invisible: true),
                (Name: "visible", Modes: new[] { 0 }, Nested: false, Invisible: false),
                (Name: "invisible-visible", Modes: new[] { 3, 0 }, Nested: false, Invisible: false),
                (Name: "visible-invisible", Modes: new[] { 0, 3 }, Nested: false, Invisible: false),
                (Name: "clipping", Modes: new[] { 7 }, Nested: false, Invisible: false),
                (Name: "form-invisible", Modes: new[] { 3 }, Nested: true, Invisible: true),
                (Name: "form-mixed", Modes: new[] { 3, 0 }, Nested: true, Invisible: false),
            })
            {
                var path = Path.Combine(directory, fixture.Name + ".pdf");
                WriteOcrNoticeFontPdf(path, fixture.Modes, fixture.Nested);
                var hash = SHA256.HashData(File.ReadAllBytes(path));
                var properties = await PdfDocumentPropertiesService.ReadAsync(path, CancellationToken.None, true);
                Check(properties.ErrorMessage.Length == 0 && properties.Fonts.Count == 1 &&
                      properties.Fonts[0].EmbeddedText == "いいえ", fixture.Name + ": unembedded font remains in document properties.");
                Check(properties.Fonts[0].IsInvisibleOnly == fixture.Invisible,
                    fixture.Name + ": invisible-only classification survives the isolated worker transport.");
                var issues = await InputPdfInspectionService.InspectAsync(path, CancellationToken.None);
                Check(issues.Any(issue => issue.Code == "fonts.not_embedded") != fixture.Invisible,
                    fixture.Name + ": only verified render-mode-3-only fonts omit the routine notice.");
                Check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash), fixture.Name + ": inspection leaves source bytes unchanged.");
            }
            Check(!new PdfFontPropertyInfo().IsInvisibleOnly, "Unknown font usage is conservatively treated as potentially visible.");
            var source = Path.Combine(directory, "count-source.pdf");
            WriteDocumentUiTestPdf(source);
            var pageId = Guid.NewGuid();
            var texts = Enumerable.Repeat("abcdefghijklmnopqrst", 5).Concat(["ab", "cd"]).ToArray();
            var regions = texts.Select((text, index) =>
            {
                var geometry = new TextGeometry
                {
                    LocalBounds = new(new(20, 330 - index * 30), new(250, 18)),
                    RotationCenter = new(145, 339 - index * 30), IsGeometryLocked = index == 5,
                };
                return new OcrTextRegion { PageId = pageId, OriginalText = text,
                    OriginalGeometry = geometry, EditedGeometry = geometry, HasExplicitWritingMode = true };
            }).ToArray();
            var packages = new ProjectPackageService();
            var project = Path.Combine(directory, "count-input.pdfocrproj");
            await packages.SaveAsync(project, new PdfCorrectoriumProject
            {
                Name = "外れ値の固定フィルター", SourcePdf = await packages.CreateSourceReferenceAsync(source, directory),
                Pages = [new() { Id = pageId, PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = regions },
                    new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400 }],
            });
            await vm.LoadProjectForDiagnosticsAsync(project);
            Check(vm.HasDocument, "The count fixture project opens successfully.");
            var all = await vm.AnalyzeOcrCharacterCountAnomaliesAsync(new());
            Check(all.Count == 2 && all.Any(item => item.Sample.IsGeometryLocked), "Locked regions remain in the analysis evidence and statistical baseline.");
            analysis = new(vm) { Owner = window, ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000 };
            analysis.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            analysis.Show();
            var grid = (DataGrid)analysis.FindName("CountResultsGrid");
            var toggle = (CheckBox)analysis.FindName("ShowLockedCountCheckBox");
            var status = (TextBlock)analysis.FindName("CountStatusTextBlock");
            var analyze = typeof(OcrQualityAnalysisWindow).GetMethod("AnalyzeCharacterCountsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)analyze.Invoke(analysis, null)!;
            await Layout();
            Check(toggle.IsChecked == false && grid.Items.Count == 1, "Count results initially hide whole-region position/size locks.");
            Check(status.Text.Contains("非表示1件", StringComparison.Ordinal), "The result summary distinguishes hidden locked candidates.");
            var unlockedRow = grid.Items[0];
            var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            view.SortDescriptions.Add(new("Text", ListSortDirection.Descending));
            grid.SelectedItem = unlockedRow;
            toggle.IsChecked = true;
            Check(grid.Items.Count == 2 && view.SortDescriptions.Count == 1 && ReferenceEquals(grid.SelectedItem, unlockedRow),
                "Showing locked candidates retains the cached row identity, selection and sorting without analysis.");
            toggle.IsChecked = false;
            Check(grid.Items.Count == 1 && !vm.HasUnsavedChanges, "Toggling the display filter does not edit or dirty the project.");
            var candidate = all.Single(item => !item.Sample.IsGeometryLocked);
            await vm.NavigateToOcrQualityCandidateAsync(1, candidate.RegionId);
            var region = vm.SelectedOverlay!;
            region.IsGeometryLocked = true;
            Check(grid.Items.Count == 0 && vm.HasUnsavedChanges, "Locking a visible candidate immediately hides it without re-analysis.");
            vm.UndoCommand.Execute(null);
            Check(grid.Items.Count == 1 && ReferenceEquals(grid.Items[0], unlockedRow) && !vm.HasUnsavedChanges,
                "Undoing a lock immediately restores the same candidate row and clean state.");
            vm.RedoCommand.Execute(null);
            Check(grid.Items.Count == 0, "Redoing a lock immediately hides the candidate again.");
            toggle.IsChecked = true;
            Check(grid.Items.Count == 2 && grid.Items.Cast<object>().All(row =>
                (string)row.GetType().GetProperty("LockDisplay")!.GetValue(row)! == LocalizationService.Translate("固定済み")),
                "The optional lock column reflects current locks, not the old analysis snapshot.");
            Check(grid.Items.Cast<object>().All(row => ((string)row.GetType().GetProperty("FindingDisplay")!.GetValue(row)!)
                    .Contains(LocalizationService.Translate("固定済み"), StringComparison.Ordinal)) &&
                  grid.Columns.Single(column => column.SortMemberPath == "FindingDisplay").Visibility == Visibility.Visible,
                "Compact findings retain current lock state even while the technical lock column is hidden.");
            vm.UndoCommand.Execute(null);
            toggle.IsChecked = false;
            var beforeMove = grid.Items[0];
            region.Left += 3;
            Check(grid.Items.Count == 1 && ReferenceEquals(beforeMove, grid.Items[0]), "Ordinary movement does not rebuild the count result view.");
            vm.UndoCommand.Execute(null);
            Check(!vm.HasUnsavedChanges && view.SortDescriptions.Count == 1, "Display filtering and geometry Undo retain sorting and clean state.");
            // Character locks are intentionally not whole-region position locks.
            region.SelectCharacter(0, false, false);
            region.SetSelectedCharacterLocks(true);
            Check(region.HasLockedCharacters && !region.IsGeometryLocked && grid.Items.Count == 1,
                "A per-character lock does not hide a row whose region position and size remain editable.");
            vm.UndoCommand.Execute(null);
            Check(!region.HasLockedCharacters && !vm.HasUnsavedChanges, "Character-lock Undo preserves the count filter and clean state.");
            await (Task)analyze.Invoke(analysis, null)!;
            view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            Check(grid.Items.Count == 1 && view.SortDescriptions.Count == 1,
                $"Explicit re-analysis binds the completed result once and retains sorting without deferred-refresh errors (rows={grid.Items.Count}, sorts={view.SortDescriptions.Count}).");
            await Layout();
            SaveIconDiagnosticImage((FrameworkElement)analysis.Content, Path.Combine(directory, "count-filter.png"));
            analysis.Close(); analysis = null;
            await VerifyOcrDensityUiAsync(window, vm, source, directory, Check);
            await VerifyListPresentationAsync(window, vm, directory, Check);
            var bookOption = args.FirstOrDefault(value => value.StartsWith("--density-project=", StringComparison.OrdinalIgnoreCase));
            if (bookOption is not null)
                await VerifyOcrDensityBookAsync(bookOption["--density-project=".Length..], directory, Check);
            var common = (Border)window.FindName("CommonInputPdfWarning");
            var global = (Border)window.FindName("GlobalInputPdfWarning");
            Check(ReferenceEquals(common.Parent, window.FindName("OcrPropertyContent")) &&
                  !ReferenceEquals(global.Parent, common.Parent), "Font notices reside in OCR properties; important notices have a separate global host.");
            vm.InputPdfIssues.Clear();
            vm.InputPdfIssues.Add(new("fonts.not_embedded", PdfInputIssueSeverity.Warning, "Font notice", "Details", "Fonts"));
            await Layout();
            Check(common.Visibility == Visibility.Visible && global.Visibility == Visibility.Collapsed && !vm.HasGlobalInputPdfIssues,
                "An ordinary unembedded-font notice is available in properties without a top banner.");
            vm.InputPdfIssues.Add(new("signature.present", PdfInputIssueSeverity.Critical, "Signature", "Details", "Signature"));
            await Layout();
            Check(global.Visibility == Visibility.Visible && vm.GlobalInputPdfIssueCount == 1 && vm.InputPdfIssueCount == 2,
                "Important signature notices remain globally visible while properties retain the full issue list.");
            vm.InputPdfIssues.Clear();
            vm.InputPdfIssues.Add(new("fonts.not_embedded", PdfInputIssueSeverity.Critical, "Critical", "Details", "Fonts"));
            await Layout();
            Check(global.Visibility == Visibility.Visible, "A future critical font issue cannot be hidden by its issue code.");
            vm.InputPdfIssues.Clear();
            vm.InputPdfIssues.Add(new("properties.partial", PdfInputIssueSeverity.Warning, "Incomplete inspection", "Details", "Structure"));
            await Layout();
            Check(global.Visibility == Visibility.Visible, "Inspection failures and other ordinary warnings remain globally visible.");
            vm.InputPdfIssues.Clear(); await Layout();
            Check(common.Visibility == Visibility.Collapsed && global.Visibility == Visibility.Collapsed, "No empty notice panel remains when there are no issues.");
            LocalizationService.SetLanguage(LocalizationService.EnglishLanguage);
            Check(LocalizationService.Translate("固定済みの領域も表示(_F)").StartsWith("Show position/size-locked", StringComparison.Ordinal),
                "The count filter has an English label and an access key.");
            Check(window.Title.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.Version) &&
                  PdfCorrectorium.Core.ApplicationBuildInfo.AboutText.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.NumericVersion),
                "Title and About identify this build consistently.");
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
            window.Close(); Shutdown(0);
        }
        catch (Exception ex)
        {
            if (directory is not null)
            {
                File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
                File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
            }
            _diagnostics?.Write("ocr-notices-test.failed", ex.ToString()); Shutdown(1);
        }
        finally { analysis?.Close(); LocalizationService.SetLanguage(language); }
    }

    private static void WriteOcrNoticeFontPdf(string path, IReadOnlyList<int> modes, bool nested)
    {
        var text = string.Join('\n', modes.Select((mode, i) => $"BT /F1 12 Tf {mode} Tr 20 {300 - i * 30} Td (OCR font test) Tj ET"));
        string Stream(string content, string extra = "") => $"<< /Length {Encoding.ASCII.GetByteCount(content)} {extra} >>\nstream\n{content}\nendstream";
        var resources = "<< /Font << /F1 4 0 R >> >>";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources {(nested ? "<< /XObject << /Fm 6 0 R >> >>" : resources)} /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            Stream(nested ? "q /Fm Do Q" : text),
        };
        if (nested) objects.Add(Stream(text, $"/Type /XObject /Subtype /Form /BBox [0 0 300 400] /Resources {resources}"));
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
}
