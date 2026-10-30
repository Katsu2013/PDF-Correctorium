using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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
    private async Task RunSimilarWordTestAsync(MainWindow window, string[] arguments, int optionIndex)
    {
        string? directory = null;
        var language = LocalizationService.CurrentLanguage;
        OcrQualityAnalysisWindow? analysisWindow = null;
        try
        {
            if (arguments.Length <= optionIndex + 1) throw new ArgumentException("A new test directory is required.");
            directory = Path.GetFullPath(arguments[optionIndex + 1]);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory)
                .Any(path => Path.GetFileName(path) != ".appstate")) throw new IOException("Use a new test directory.");
            Directory.CreateDirectory(directory);
            var vm = (MainWindowViewModel)window.DataContext;
            window.ClosePromptOverride = () => MessageBoxResult.No;
            vm.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            var checks = new List<string>();
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add("PASS: " + message);
            }
            var pdfPath = Path.Combine(directory, "source.pdf");
            WriteDocumentUiTestPdf(pdfPath);
            var originalHash = SHA256.HashData(File.ReadAllBytes(pdfPath));
            var packages = new ProjectPackageService();
            OcrPage Page(int number, params (string Text, bool Locked, bool Deleted)[] texts)
            {
                var id = Guid.NewGuid();
                return new()
                {
                    Id = id, PageNumber = number, WidthPoints = 300, HeightPoints = 400,
                    TextRegions = texts.Select((t, i) =>
                    {
                        var geometry = new TextGeometry
                        {
                            LocalBounds = new(new(20, 330 - i * 30), new(250, 18)),
                            RotationCenter = new(145, 339 - i * 30), IsGeometryLocked = t.Locked,
                        };
                        return new OcrTextRegion { PageId = id, OriginalText = t.Text,
                            OriginalGeometry = geometry, EditedGeometry = geometry,
                            ReviewStatus = ReviewStatus.Unreviewed, HasExplicitWritingMode = true, IsDeleted = t.Deleted };
                    }).ToArray(),
                };
            }
            var input = Path.Combine(directory, "input.pdfocrproj");
            await packages.SaveAsync(input, new PdfCorrectoriumProject
            {
                Name = "類似語の診断", SourcePdf = await packages.CreateSourceReferenceAsync(pdfPath, directory),
                Pages = [Page(1, (string.Join(' ', Enumerable.Repeat("Node.js mode", 8)), false, false)),
                    Page(2, ("前😀 Node.is rnode Node.is 後", false, false), ("Node.is", true, false), ("Node.is", false, true))],
            });
            await vm.LoadProjectForDiagnosticsAsync(input);
            Check(window.Title.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.Version) &&
                PdfCorrectorium.Core.ApplicationBuildInfo.AboutText.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.Version) &&
                PdfCorrectorium.Core.ApplicationBuildInfo.AboutText.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.NumericVersion),
                "The real title and About display use the current product and four-part build version.");
            var initialPage = vm.SelectedPage;
            var result = await vm.AnalyzeOcrSimilarWordsAsync(new(MaximumSuspectCount: 3, MinimumFrequencyRatio: 2));
            Check(result.Analysis.Candidates.Count == 4, "Full-book analysis includes unloaded pages but excludes deleted OCR.");
            Check(vm.SelectedPage == initialPage && !vm.HasUnsavedChanges, "Analysis does not navigate or edit the document.");
            var first = result.Analysis.Candidates.First(c => c.Word == "rnode");
            vm.ShowOcrFrames = false;
            Check(await vm.NavigateToOcrQualityCandidateAsync(first.PageNumber, first.RegionId, first.StartIndex, first.Length), "Candidates navigate to their exact OCR range.");
            var region = vm.SelectedOverlay!;
            Check(Math.Abs(region.Left - 20d / 300 * vm.PreviewPixelWidth) < .01 &&
                Math.Abs(region.Width - 250d / 300 * vm.PreviewPixelWidth) < .01 &&
                Math.Abs(region.Height - 18d / 400 * vm.PreviewPixelHeight) < .01,
                "Analysis-cached OCR geometry uses full preview coordinates when navigated to.");
            Check(vm.SelectedPage?.PageNumber == 2 && region.HasCharacterSelection && region.Id == first.RegionId &&
                string.Concat(region.CharacterCells.Where(c => c.IsSelected).Select(c => c.Text)) == "rnode",
                "Navigation selects exactly the candidate token after a surrogate pair.");
            Check(!vm.ShowOcrFrames && !vm.HasUnsavedChanges &&
                  string.Concat(region.CharacterCells.Where(cell => cell.IsSearchMatch).Select(cell => cell.Text)) == "rnode",
                "Similar-word navigation marks the exact token even with OCR frames hidden, without editing the document.");
            var nextOccurrence = result.Analysis.Candidates.First(candidate => candidate.Word == "Node.is" && !candidate.Sample.IsLocked);
            Check(await vm.NavigateToOcrQualityCandidateAsync(nextOccurrence.PageNumber, nextOccurrence.RegionId, nextOccurrence.StartIndex, nextOccurrence.Length) &&
                  string.Concat(vm.SelectedOverlay!.CharacterCells.Where(cell => cell.IsSearchMatch).Select(cell => cell.Text)) == "Node.is" &&
                  !vm.HasUnsavedChanges && !vm.ShowOcrFrames,
                "Navigating to the next candidate replaces the marker without enabling all OCR frames.");
            Check(await vm.NavigateToOcrQualityCandidateAsync(first.PageNumber, first.RegionId, first.StartIndex, first.Length),
                "Returning to an earlier candidate restores only that keyword marker.");
            var before = region.Capture();
            var continued = vm.ApplySimilarWordCorrectionsAndRefresh(result, [first]);
            Check(continued.ChangedCount == 1 && continued.Remaining.Analysis.Candidates.Count == 3 &&
                vm.IsSimilarWordResultCurrent(continued.Remaining), "A reviewed partial correction retains a fresh remaining snapshot.");
            Check(continued.Remaining.Analysis.Candidates.All(candidate => candidate.Sample.Text.Substring(candidate.StartIndex, candidate.Length) == candidate.Word),
                "Remaining same-region offsets rebase after a length-changing correction.");
            await VerifySimilarWordLayoutContinuityAsync(vm, continued.Remaining, Check);
            Check(vm.ApplySimilarWordCorrections(continued.Remaining, continued.Remaining.Analysis.Candidates) == 2,
                "Remaining candidates can be corrected without scanning the book again.");
            vm.UndoCommand.Execute(null);
            Check(!vm.IsSimilarWordResultCurrent(continued.Remaining), "Undo invalidates the retained candidate generation.");
            vm.UndoCommand.Execute(null);
            Check(region.Capture() == before && !vm.HasUnsavedChanges, "Partial correction batches undo separately without geometry drift.");
            result = await vm.AnalyzeOcrSimilarWordsAsync(new(MaximumSuspectCount: 3, MinimumFrequencyRatio: 2));
            vm.EditorModeIndex = 3;
            Check(vm.ApplySimilarWordCorrections(result, result.Analysis.Candidates) == 0, "Redaction mode cannot modify OCR through quality analysis.");
            vm.EditorModeIndex = 2;
            Check(vm.ApplySimilarWordCorrections(result, result.Analysis.Candidates.Concat(result.Analysis.Candidates).ToArray()) == 3,
                "Only unlocked exact occurrences are corrected once, even when selected twice.");
            Check(region.Text == "前😀 Node.js mode Node.js 後" && region.ReviewStatus == ReviewStatus.NeedsReview, "Many-to-one and repeated-token corrections work backwards and require review.");
            Check(region.Left == before.Left && region.Top == before.Top && region.Width == before.Width && region.Height == before.Height,
                "Text correction preserves the OCR region geometry.");
            Check(vm.OverlayItems.Single(r => r.IsGeometryLocked).Text == "Node.is", "Locked text is never changed.");
            Check(vm.ApplySimilarWordCorrections(result, result.Analysis.Candidates) == 0, "A stale result cannot be applied a second time.");
            vm.UndoCommand.Execute(null);
            Check(region.Capture() == before && !vm.HasUnsavedChanges, "One Undo restores all text, advances, review state and clean state.");
            vm.RedoCommand.Execute(null);
            Check(region.Text == "前😀 Node.js mode Node.js 後", "One Redo restores the whole correction batch.");
            var saved = Path.Combine(directory, "corrected.pdfocrproj");
            await vm.SaveProjectForDiagnosticsAsync(saved);
            Check((await packages.OpenAsync(saved)).Pages[1].TextRegions.First().EffectiveText == region.Text, "Corrected OCR survives project persistence.");
            using (var archive = System.IO.Compression.ZipFile.OpenRead(saved))
            using (var manifestStream = archive.GetEntry("manifest.json")!.Open())
            using (var manifest = System.Text.Json.JsonDocument.Parse(manifestStream))
                Check(manifest.RootElement.GetProperty("applicationVersion").GetString() == PdfCorrectorium.Core.ApplicationBuildInfo.Version,
                    "The actual saved project manifest records the current application version.");
            Check(SHA256.HashData(File.ReadAllBytes(pdfPath)).SequenceEqual(originalHash), "The original PDF is byte-for-byte unchanged.");
            await vm.LoadProjectForDiagnosticsAsync(input);
            Check(!vm.IsSimilarWordResultCurrent(result) && vm.ApplySimilarWordCorrections(result, result.Analysis.Candidates) == 0,
                "Reopening even the same project invalidates the old analysis session.");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try { await vm.AnalyzeOcrSimilarWordsAsync(new(), cancellationToken: cancellation.Token); throw new InvalidOperationException("Cancellation was ignored."); }
            catch (OperationCanceledException) { checks.Add("PASS: Cancelled analysis does not return partial candidates."); }
            var pending = vm.AnalyzeOcrSimilarWordsAsync(new());
            vm.OverlayItems.First().Text += " edit";
            try { await pending; throw new InvalidOperationException("Concurrent editing was ignored."); }
            catch (OperationCanceledException) { checks.Add("PASS: Editing while scanning invalidates the pending result."); }
            await vm.LoadProjectForDiagnosticsAsync(input);
            analysisWindow = new OcrQualityAnalysisWindow(vm) { ConfirmWordCorrectionsOverride = _ => false };
            // A real presentation source is needed for initial DataGrid column sizing.
            // Keep the diagnostic window off-screen, without activation or a taskbar entry.
            analysisWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            analysisWindow.Left = -20000; analysisWindow.Top = -20000; analysisWindow.ShowActivated = false;
            analysisWindow.Show();
            T Control<T>(string name) where T : FrameworkElement => (T)analysisWindow.FindName(name);
            var type = typeof(OcrQualityAnalysisWindow);
            async Task AnalyzeAsync() => await (Task)type.GetMethod("AnalyzeWordsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(analysisWindow, null)!;
            void Click(string handler) => type.GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(analysisWindow, [analysisWindow, new RoutedEventArgs()]);
            Control<TabControl>("AnalysisTabs").SelectedIndex = 2;
            Control<TextBox>("WordFrequencyRatioTextBox").Text = "2";
            await AnalyzeAsync();
            var grid = Control<DataGrid>("WordResultsGrid");
            static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
            {
                yield return parent;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    foreach (var child in Visuals(VisualTreeHelper.GetChild(parent, i))) yield return child;
            }
            void ClickSortHeader(DataGridColumn column)
            {
                analysisWindow.UpdateLayout();
                var header = Visuals(grid).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>()
                    .Single(item => ReferenceEquals(item.Column, column));
                // Exercise the real WPF header path (including CanUserSort and sort arrows),
                // rather than sorting the source collection in the diagnostic itself.
                typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)
                    .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(header, null);
                analysisWindow.UpdateLayout();
            }
            string RowValue(object row, string property) => (string)row.GetType().GetProperty(property)!.GetValue(row)!;
            Check(grid.Columns.Count(column => column.Visibility == Visibility.Visible) == 5 &&
                  grid.Columns.Where(ListDetails.GetIsDetail).All(column => column.Visibility == Visibility.Collapsed),
                "Similar words default to the two words, page, context and actionable attention rather than repeated frequency statistics.");
            var lockedRow = grid.Items.Cast<object>().Single(row =>
                ((OcrSimilarWordCandidate)row.GetType().GetProperty("Source")!.GetValue(row)!).Sample.IsLocked);
            Check(RowValue(lockedRow, "Attention").Contains(LocalizationService.Translate("固定済み・修正不可"), StringComparison.Ordinal),
                "Locked/unmodifiable similar-word candidates keep their warning in the basic view.");
            var selectedBeforeDetails = grid.Items[0]; grid.SelectedItem = selectedBeforeDetails;
            var columnMenu = OpenListColumnMenu(grid);
            foreach (var item in columnMenu.Items.OfType<MenuItem>().Where(item => item.Tag is DataGridColumn && !item.IsChecked))
                await ClickListColumnMenuItemAsync(item);
            columnMenu.IsOpen = false;
            Check(grid.Columns.All(column => column.Visibility == Visibility.Visible) && ReferenceEquals(grid.SelectedItem, selectedBeforeDetails),
                "Frequency evidence and full reasons remain available on demand without losing selection.");
            columnMenu = OpenListColumnMenu(grid, keyboard: true);
            await ClickListColumnMenuItemAsync(columnMenu.Items.OfType<MenuItem>().Last());
            columnMenu.IsOpen = false;
            foreach (var (columnIndex, member) in new[] { (1, "Word"), (2, "SuggestedWord") })
            {
                var column = grid.Columns[columnIndex];
                Check(grid.CanUserSortColumns && column.CanUserSort && column.SortMemberPath == member,
                    member + ": Highlighted template column retains its raw-text sort member.");
                var selected = grid.Items[0];
                grid.SelectedItem = selected;
                ClickSortHeader(column);
                var values = grid.Items.Cast<object>().Select(row => RowValue(row, member)).ToArray();
                Check(column.SortDirection == System.ComponentModel.ListSortDirection.Ascending &&
                      values.SequenceEqual(values.OrderBy(value => value, StringComparer.CurrentCulture)) && ReferenceEquals(grid.SelectedItem, selected),
                    member + ": Header click sorts ascending without changing the selected candidate.");
                ClickSortHeader(column);
                values = grid.Items.Cast<object>().Select(row => RowValue(row, member)).ToArray();
                Check(column.SortDirection == System.ComponentModel.ListSortDirection.Descending &&
                      values.SequenceEqual(values.OrderByDescending(value => value, StringComparer.CurrentCulture)),
                    member + ": Second header click sorts descending using the original text.");
            }
            var rowItem = grid.Items[0];
            grid.ScrollIntoView(rowItem);
            analysisWindow.UpdateLayout();
            foreach (var columnIndex in new[] { 1, 2 })
            {
                var wordText = Visuals(grid.Columns[columnIndex].GetCellContent(rowItem)).OfType<TextBlock>().Single();
                Check(wordText.FontSize == 16 && wordText.TextTrimming == TextTrimming.CharacterEllipsis &&
                      wordText.Inlines.OfType<System.Windows.Documents.Run>().Any() &&
                      grid.Columns[columnIndex].ClipboardContentBinding is System.Windows.Data.Binding,
                    "Word column " + columnIndex + ": Enlarged text retains difference runs, clipping feedback and raw clipboard values.");
            }
            Check(((TextBlock)grid.Columns[0].GetCellContent(rowItem)).FontSize < 16,
                "Page/count/context columns and headers are not enlarged with the two word columns.");
            var progressBar = Control<ProgressBar>("WordsProgressBar");
            progressBar.Visibility = Visibility.Visible; progressBar.Value = 25; progressBar.ApplyTemplate();
            analysisWindow.UpdateLayout();
            var track = (FrameworkElement)progressBar.Template.FindName("PART_Track", progressBar);
            var indicator = (FrameworkElement)progressBar.Template.FindName("PART_Indicator", progressBar);
            var firstWidth = indicator.ActualWidth;
            Check(track.ActualWidth > 100 && Math.Abs(firstWidth / track.ActualWidth - .25) < .02,
                "Determinate progress has a visible quarter-width indicator at 25 percent.");
            progressBar.Value = 75; analysisWindow.UpdateLayout();
            Check(indicator.ActualWidth > firstWidth && Math.Abs(indicator.ActualWidth / track.ActualWidth - .75) < .02,
                "Progress visibly grows with the reported value.");
            progressBar.Visibility = Visibility.Collapsed;
            var difference = WordDifference.Compare("Node.」s", "Node.js");
            Check(difference.Original.Select((changed, index) => (changed, index)).Where(item => item.changed).Select(item => item.index).SequenceEqual(new[] { 5 }) &&
                difference.Suggested.Select((changed, index) => (changed, index)).Where(item => item.changed).Select(item => item.index).SequenceEqual(new[] { 5 }),
                "Word differences mark only the changed glyph on each side.");
            var diffText = new TextBlock(); WordDifference.SetOriginal(diffText, "rnode"); WordDifference.SetSuggested(diffText, "mode");
            File.WriteAllText(Path.Combine(directory, "difference-render.txt"), "Text=" + diffText.Text + "\n" +
                string.Join("\n", diffText.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text + ":" + run.TextDecorations.Count)));
            Check(string.Concat(diffText.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text)) == "rnode" &&
                diffText.Inlines.OfType<System.Windows.Documents.Run>().Any(run => run.Text == "rn" && run.TextDecorations.Count > 0),
                "Difference rendering preserves text and emphasizes a many-to-one replacement.");
            Check(grid.Items.Count == 4, "The third quality-analysis tab displays full-book suspects.");
            grid.SelectAll();
            Click("CorrectWordsButton_OnClick");
            Check(!vm.HasUnsavedChanges && grid.Items.Count == 4, "Declining confirmation leaves every candidate and OCR unchanged.");
            Click("IgnoreWordButton_OnClick");
            Check(grid.Items.Count == 0 && vm.HasUnsavedChanges && vm.CurrentOcrLexicon.IgnoredTerms.Count == 2,
                "Ignoring selected terms is now explicit, undoable project dictionary data.");
            await AnalyzeAsync();
            Check(grid.Items.Count == 0, "Ignored terms survive a repeat analysis in the same window.");
            Click("ResetIgnoredWordsButton_OnClick");
            vm.UndoCommand.Execute(null); vm.UndoCommand.Execute(null);
            Check(!vm.HasUnsavedChanges && vm.CurrentOcrLexicon.IgnoredTerms.Count == 0, "One Undo per dictionary action restores the saved state.");
            await AnalyzeAsync();
            Check(grid.Items.Count == 4, "Resetting ignored terms restores candidates on analysis.");
            Check(Control<TextBlock>("WordsDescriptionTextBlock").Text.Contains("文書全体") &&
                !Control<TextBlock>("WordsDescriptionTextBlock").Text.Contains("Node.is") &&
                !Control<TextBlock>("WordsDescriptionTextBlock").Text.Contains("rnode"),
                "Japanese instructions describe the feature without concrete misspelling examples.");
            async Task SnapshotAsync(string name)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var root = (FrameworkElement)analysisWindow.Content;
                root.Measure(new(1080, 700)); root.Arrange(new(0, 0, 1080, 700)); root.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap(1080, 700, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new(0, 0, 1080, 700));
                bitmap.Render(background); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
            }
            await SnapshotAsync("similar-words-ja.png");
            LocalizationService.SetLanguage(LocalizationService.EnglishLanguage); LocalizationService.Apply(analysisWindow);
            var rawTerm = new TextBlock(); WordDifference.SetOriginal(rawTerm, "文書全体を分析(_A)"); WordDifference.SetSuggested(rawTerm, "文書全体を分析(_B)");
            LocalizationService.Apply(rawTerm);
            Check(string.Concat(rawTerm.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text)) == "文書全体を分析(_A)",
                "Formatted document text is never translated as a fixed UI caption.");
            await AnalyzeAsync();
            await SnapshotAsync("similar-words-en.png");
            Check(Control<Button>("CorrectWordsButton").Content.ToString()!.Contains("Correct selected"), "New actions localize to English.");
            Check(Control<TextBlock>("WordsDescriptionTextBlock").Text.Contains("throughout the document") &&
                !Control<TextBlock>("WordsDescriptionTextBlock").Text.Contains("Node.is") &&
                !Control<TextBlock>("WordsDescriptionTextBlock").Text.Contains("rnode"),
                "English instructions describe the feature without concrete misspelling examples.");
            grid.SelectedItem = grid.Items.Cast<object>().First(row => (string)row.GetType().GetProperty("Word")!.GetValue(row)! == "rnode");
            analysisWindow.ConfirmWordCorrectionsOverride = _ => true;
            Click("CorrectWordsButton_OnClick");
            Check(vm.HasUnsavedChanges && grid.Items.Count == 3 && Control<Button>("CorrectWordsButton").IsEnabled,
                $"UI partial correction keeps remaining rows selected and ready for the next reviewed correction. dirty={vm.HasUnsavedChanges}, rows={grid.Items.Count}, enabled={Control<Button>("CorrectWordsButton").IsEnabled}, status={Control<TextBlock>("WordsStatusTextBlock").Text}");
            var remainingWords = grid.Items.Cast<object>().Select(row => RowValue(row, "SuggestedWord")).ToArray();
            Check(grid.Columns[2].SortDirection == System.ComponentModel.ListSortDirection.Descending &&
                  remainingWords.SequenceEqual(remainingWords.OrderByDescending(value => value, StringComparer.CurrentCulture)),
                "The chosen sort survives partial corrections and the remaining candidate list refresh.");
            var retainedRows = grid.Items.Cast<object>().ToArray();
            grid.SelectedItem = retainedRows.First(row => !((PdfCorrectorium.Core.Analysis.OcrSimilarWordCandidate)
                row.GetType().GetProperty("Source")!.GetValue(row)!).Sample.IsLocked);
            await (Task)type.GetMethod("NavigateWordAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(analysisWindow, null)!;
            var layoutBefore = vm.SelectedOverlay!.Capture();
            vm.NudgeSelection(2, 1);
            Check(vm.SelectedOverlay.Left != layoutBefore.Left && Control<Button>("NavigateWordButton").IsEnabled &&
                Control<Button>("CorrectWordsButton").IsEnabled && retainedRows.SequenceEqual(grid.Items.Cast<object>()),
                "The real UI retains row objects, selection and actions after moving a partially corrected OCR row, without re-analysis.");
            vm.UndoCommand.Execute(null);
            Check(Control<Button>("CorrectWordsButton").IsEnabled && vm.SelectedOverlay.Capture() == layoutBefore,
                "The UI remains ready after undoing only the row movement.");
            vm.RedoCommand.Execute(null);
            Check(Control<Button>("CorrectWordsButton").IsEnabled && Control<Button>("NavigateWordButton").IsEnabled,
                "The UI remains ready after redoing only the row movement.");
            vm.UndoCommand.Execute(null);
            await SnapshotAsync("similar-words-after-partial.png");
            vm.UndoCommand.Execute(null);
            Check(!Control<Button>("CorrectWordsButton").IsEnabled, "UI rejects retained offsets after an external Undo.");
            await AnalyzeAsync();
            grid.SelectAll();
            analysisWindow.ConfirmWordCorrectionsOverride = _ => true;
            Click("CorrectWordsButton_OnClick");
            Check(vm.HasUnsavedChanges && grid.Items.Count == 1 && !Control<Button>("CorrectWordsButton").IsEnabled,
                "Confirmed UI correction retains locked remaining rows instead of clearing the list.");
            vm.UndoCommand.Execute(null);
            Check(!vm.HasUnsavedChanges, "UI batch correction is a single Undo operation.");
            analysisWindow.Close(); analysisWindow = null;
            // A rotated, initially unvisited page must use the same canonical
            // display coordinate system without rescaling live Undo objects later.
            var rotatedPage = Page(2, ("Node.is", false, false));
            var rotatedGeometry = new TextGeometry { LocalBounds = new(new(20, 230), new(250, 18)), RotationCenter = new(145, 239) };
            rotatedPage = rotatedPage with { WidthPoints = 400, HeightPoints = 300,
                TextRegions = rotatedPage.TextRegions.Select(region => region with { OriginalGeometry = rotatedGeometry, EditedGeometry = rotatedGeometry }).ToArray() };
            var referencePage = Page(1, (string.Join(' ', Enumerable.Repeat("Node.js", 8)), false, false));
            var rotatedProject = (await packages.OpenAsync(input)) with { Pages = [referencePage, rotatedPage],
                PageSequence = [new() { PageId = referencePage.Id, SourcePageNumber = 1 }, new() { PageId = rotatedPage.Id, SourcePageNumber = 2, RotationDegrees = 90 }] };
            var rotatedInput = Path.Combine(directory, "rotated.pdfocrproj");
            await packages.SaveAsync(rotatedInput, rotatedProject);
            await vm.LoadProjectForDiagnosticsAsync(rotatedInput);
            var rotatedResult = await vm.AnalyzeOcrSimilarWordsAsync(new(MinimumFrequencyRatio: 2));
            var rotatedCandidate = rotatedResult.Analysis.Candidates.Single();
            await vm.NavigateToOcrQualityCandidateAsync(2, rotatedCandidate.RegionId, rotatedCandidate.StartIndex, rotatedCandidate.Length);
            Check(vm.PreviewPixelWidth == 1600 && vm.PreviewPixelHeight == 1200 &&
                Math.Abs(vm.SelectedOverlay!.Left - 80) < .01 && Math.Abs(vm.SelectedOverlay.Width - 1000) < .01 && Math.Abs(vm.SelectedOverlay.Top - 208) < .01,
                "Analysis navigation preserves full-scale geometry on a 90-degree logical page.");
            vm.ApplySimilarWordCorrections(rotatedResult, rotatedResult.Analysis.Candidates);
            var rotatedOutput = Path.Combine(directory, "rotated-corrected.pdfocrproj");
            await vm.SaveProjectForDiagnosticsAsync(rotatedOutput);
            var savedGeometry = (await packages.OpenAsync(rotatedOutput)).Pages[1].TextRegions.Single().EditedGeometry.LocalBounds;
            Check(Math.Abs(savedGeometry.Left - 20) < .01 && Math.Abs(savedGeometry.Size.Width - 250) < .01 && Math.Abs(savedGeometry.Size.Height - 18) < .01,
                "Saving after analysis navigation preserves PDF-point geometry, not tiny extraction pixels.");
            Check(SHA256.HashData(File.ReadAllBytes(pdfPath)).SequenceEqual(originalHash), "Rotated navigation and correction leave the source PDF unchanged.");
            await vm.LoadProjectForDiagnosticsAsync(input);
            await VerifySimilarWordInputInvalidationAsync(vm, Check);
            await VerifyOcrLexiconWorkflowAsync(window, vm, input, directory, Check);
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
            _diagnostics?.Write("similar-word-test.passed", $"{checks.Count} checks passed. {directory}");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            if (directory is not null) File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
            _diagnostics?.Write("similar-word-test.failed", ex.ToString());
            Shutdown(1);
        }
        finally { analysisWindow?.Close(); LocalizationService.SetLanguage(language); }
    }
}
