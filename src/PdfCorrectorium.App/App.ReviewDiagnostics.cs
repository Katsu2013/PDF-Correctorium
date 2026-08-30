using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunReviewModeTestAsync(MainWindow window, string[] arguments, int optionIndex)
    {
        string? directory = null;
        try
        {
            if (arguments.Length <= optionIndex + 1) throw new ArgumentException("A new test output directory is required.");
            var requestedDirectory = Path.GetFullPath(arguments[optionIndex + 1]);
            if (Directory.Exists(requestedDirectory)) throw new IOException("Test output directory already exists.");
            Directory.CreateDirectory(requestedDirectory);
            directory = requestedDirectory;
            var vm = (MainWindowViewModel)window.DataContext;
            // This hidden fixture window must never show a save prompt, including on assertion failure.
            window.ClosePromptOverride = () => MessageBoxResult.No;
            vm.ErrorDialogOverride = (message, exception) => throw new InvalidOperationException(message, exception);
            var root = (FrameworkElement)window.Content;
            var checks = new List<string>();
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add("PASS: " + message);
            }
            T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            async Task LayoutAsync()
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                root.Measure(new Size(1400, 850));
                root.Arrange(new Rect(0, 0, 1400, 850));
                root.UpdateLayout();
            }
            void Snapshot(string name)
            {
                var bitmap = new RenderTargetBitmap(1400, 850, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, name + ".png"));
                encoder.Save(stream);
            }
            void InvokeHandler(string name, object sender, RoutedEventArgs args) =>
                typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [sender, args]);

            vm.EditorModeIndex = 2;
            Check(!vm.NextReviewCommand.CanExecute(null) && !vm.VerifyAndNextCommand.CanExecute(null), "No-document review commands are disabled.");
            vm.EditorModeIndex = 0;
            var pdfPath = Path.Combine(directory, "review-source.pdf");
            WriteDocumentUiTestPdf(pdfPath);
            var package = new ProjectPackageService();
            OcrPage Page(int pageNumber, params (string Text, ReviewStatus Status, bool Deleted, bool Locked)[] entries)
            {
                var pageId = Guid.NewGuid();
                var regions = entries.Select((entry, index) =>
                {
                    var geometry = new TextGeometry
                    {
                        LocalBounds = new PdfRectangle(new PdfPoint(25, 340 - index * 36), new PdfSize(240, 22)),
                        RotationCenter = new PdfPoint(145, 351 - index * 36),
                        IsGeometryLocked = entry.Locked,
                    };
                    return new OcrTextRegion
                    {
                        PageId = pageId, OriginalText = entry.Text,
                        OriginalGeometry = geometry, EditedGeometry = geometry,
                        ReviewStatus = entry.Status, IsDeleted = entry.Deleted,
                        HasExplicitWritingMode = true,
                    };
                }).ToArray();
                return new OcrPage
                {
                    Id = pageId, PageNumber = pageNumber, WidthPoints = 300, HeightPoints = 400,
                    TextRegions = regions.AsEnumerable().Reverse().ToArray(), ReadingOrder = regions.Select(region => region.Id).ToArray(),
                };
            }
            var projectPath = Path.Combine(directory, "review-project.pdfocrproj");
            var project = new PdfCorrectoriumProject
            {
                Name = "校正テスト", SourcePdf = await package.CreateSourceReferenceAsync(pdfPath, directory),
                Pages =
                [
                    Page(1, ("本文の誤字を確認します", ReviewStatus.Unreviewed, false, false),
                        ("読み取り結果を再確認します", ReviewStatus.NeedsReview, false, true),
                        ("確認済みの領域", ReviewStatus.Verified, false, false),
                        ("修正済みの領域", ReviewStatus.Modified, false, false),
                        ("OCR対象外の領域", ReviewStatus.Excluded, false, false),
                        ("保留中の領域", ReviewStatus.Deferred, false, false),
                        ("削除予定の領域", ReviewStatus.Unreviewed, true, false)),
                    Page(2, ("次のページの未確認領域", ReviewStatus.Unreviewed, false, false),
                        ("最後の確認対象です", ReviewStatus.NeedsReview, false, false)),
                ],
            };
            await package.SaveAsync(projectPath, project);
            await vm.LoadProjectForDiagnosticsAsync(projectPath);
            var first = vm.OverlayItems.Single(region => !region.IsDeleted && region.ReadingOrder == 1);
            var second = vm.OverlayItems.Single(region => !region.IsDeleted && region.ReadingOrder == 2);
            vm.IsAddOcrRegionMode = true;
            vm.EditorModeIndex = 2;
            await LayoutAsync();
            Check(!vm.IsAddOcrRegionMode && !vm.CanAddOcrRegion, "Entering review cancels region creation.");
            Check(!vm.HasUnsavedChanges && !first.IsGeometryLocked && second.IsGeometryLocked, "Entering review preserves saved geometry locks and clean state.");
            Check(Control<StackPanel>("ReviewPanel").Visibility == Visibility.Visible, "Review panel is visible in review mode.");
            Check(vm.ReviewItems.SequenceEqual(new[] { first, second }), "Default filter contains unreviewed / needs-review regions in reading order.");
            vm.ReviewFilterIndex = 1;
            Check(vm.ReviewItems.SequenceEqual(new[] { first }), "Unreviewed filter excludes deleted regions.");
            vm.ReviewFilterIndex = 2;
            Check(vm.ReviewItems.SequenceEqual(new[] { second }), "Needs-review filter works.");
            vm.ReviewFilterIndex = 3;
            Check(vm.ReviewItems.Count == 6, "All-status filter includes verified, modified, excluded and deferred, but not deleted regions.");
            vm.ReviewFilterIndex = 0;
            await vm.NavigateReviewAsync(1);
            await LayoutAsync();
            Check(vm.SelectedOverlay == first && vm.SelectedOverlays.SequenceEqual(new[] { first }), "Next target selects the first region in the preview and editor.");
            Check(Equals(Control<ListBox>("ReviewTargetList").SelectedItem, first), "Review list selection follows navigation.");
            Check(Control<Button>("VerifyAndNextButton").IsEnabled, "Verify-and-next button is enabled for one selected region.");
            var statusCombo = Descendants(root).OfType<ComboBox>().Single(combo => ReferenceEquals(combo.ItemsSource, vm.ReviewStatusOptions));
            var statusContent = (ContentPresenter)statusCombo.Template.FindName("SelectionContent", statusCombo);
            Check(Descendants(statusContent).OfType<TextBlock>().Any(text => text.Text == LocalizationService.Translate("未確認")),
                "The closed status dropdown displays its localized name, not the option record's type name.");
            var reviewList = Control<ListBox>("ReviewTargetList");
            reviewList.SetCurrentValue(Selector.SelectedItemProperty, second);
            Check(vm.SelectedOverlay == second && vm.SelectedOverlays.SequenceEqual(new[] { second }), "Selecting a review-list row selects the corresponding preview region.");
            reviewList.SetCurrentValue(Selector.SelectedItemProperty, first);
            var originalLanguage = LocalizationService.CurrentLanguage;
            LocalizationService.SetLanguage(LocalizationService.EnglishLanguage);
            vm.RefreshLocalization();
            LocalizationService.Apply(window);
            await LayoutAsync();
            Check(vm.ReviewFilterOptions[0].DisplayName == "Unreviewed / Needs review" && vm.ReviewSummary.StartsWith("Page 1") &&
                Equals(Control<Button>("VerifyAndNextButton").Content, "Verify and Next"), "Review controls and summaries support English without changing stored preferences.");
            Check(Descendants(statusContent).OfType<TextBlock>().Any(text => text.Text == "Unreviewed"),
                $"Status display follows language changes (display={string.Join('|', Descendants(statusContent).OfType<TextBlock>().Select(text => text.Text))}, selected={statusCombo.SelectedValue}, model={vm.SelectedReviewStatus}).");
            LocalizationService.SetLanguage(originalLanguage);
            vm.RefreshLocalization();
            LocalizationService.Apply(window);
            await LayoutAsync();
            var filterCombo = Control<ComboBox>("ReviewFilterComboBox");
            var filterContent = (ContentPresenter)filterCombo.Template.FindName("SelectionContent", filterCombo);
            Check(filterCombo.SelectedIndex == 0 && Descendants(filterContent).OfType<TextBlock>().Any(text => text.Text == vm.ReviewFilterOptions[0].DisplayName),
                "Switching language back keeps the default filter selected and visibly labeled.");
            Snapshot("review-first-target");

            var beforeGeometry = first.Capture();
            vm.NudgeSelection(20, 20);
            Check(first.Capture() == beforeGeometry, "Arrow-key geometry movement is ignored in review mode.");
            Check(vm.AddManualOcrRegion(new Rect(10, 10, 50, 20)) is null, "Manual region creation is rejected in review mode.");
            Check(!vm.IsSelectedGeometryEditable && !Control<ToolBar>("RotationPresetToolBar").IsEnabled, "Coordinate fields and rotation toolbar are disabled.");
            vm.SetOverlaySelection([first, second], first);
            foreach (var command in new[] { vm.EqualWidthCommand, vm.EqualHeightCommand, vm.AlignLeftCommand,
                         vm.AlignRightCommand, vm.AlignTopCommand, vm.AlignBottomCommand,
                         vm.AlignHorizontalCenterCommand, vm.AlignVerticalCenterCommand, vm.SetAlignmentReferenceCommand,
                         vm.ToggleGeometryLockCommand, vm.DecreaseLineCharacterSizeCommand, vm.IncreaseLineCharacterSizeCommand,
                         vm.EqualizeCharacterAdvancesCommand, vm.EstimateCharacterAdvancesCommand, vm.DeleteOcrRegionsCommand,
                         vm.MoveReadingEarlierCommand, vm.MoveReadingLaterCommand, vm.RecalculateReadingOrderCommand })
            {
                Check(!command.CanExecute(null), "A geometry/structure command is disabled in review mode.");
                command.Execute(null);
            }
            Check(!vm.VerifyAndNextCommand.CanExecute(null), "Verify-and-next requires exactly one selected region.");
            Check(first.Capture() == beforeGeometry, "Disabled commands leave the region unchanged.");
            vm.SelectedReviewItem = first;
            await LayoutAsync();
            var container = (ListBoxItem)Control<ListBox>("OverlayCanvas").ItemContainerGenerator.ContainerFromItem(first);
            static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    yield return child;
                    foreach (var descendant in Descendants(child)) yield return descendant;
                }
            }
            Check(Descendants(container).OfType<Thumb>().All(thumb => !thumb.IsVisible), "All geometry handles are hidden in review mode.");
            var thumb = new Thumb { DataContext = first, Tag = "SE" };
            InvokeHandler("ResizeThumb_OnDragDelta", thumb, new DragDeltaEventArgs(30, 20));
            InvokeHandler("RotationThumb_OnDragDelta", thumb, new DragDeltaEventArgs(30, 20));
            InvokeHandler("RotatePresetButton_OnClick", new Button { Tag = "90" }, new RoutedEventArgs());
            Check(first.Capture() == beforeGeometry, "Direct resize/rotation handler calls cannot change geometry in review mode.");
            vm.EditUnitIndex = (int)OcrEditUnit.Character;
            first.SelectedCharacterIndex = 0;
            var beforeCharacter = first.Capture();
            vm.SelectedCharacterAdvance += 5;
            InvokeHandler("CharacterAdvanceThumb_OnDragDelta", thumb, new DragDeltaEventArgs(30, 20));
            await LayoutAsync();
            Check(!Control<TextBox>("SelectedCharacterAdvanceTextBox").IsEnabled && first.Capture() == beforeCharacter,
                "Character widths cannot be edited through fields or drag handlers in review mode.");
            Check(Descendants(container).OfType<Thumb>().All(handle => !handle.IsVisible), "Character-advance handles also remain hidden in character-edit mode.");
            vm.EditUnitIndex = (int)OcrEditUnit.Line;

            Check(first.ReviewStatus == ReviewStatus.Unreviewed && !vm.HasUnsavedChanges, "Selection and mode changes alone do not mark regions modified.");

            await vm.VerifyAndNextAsync();
            Check(first.ReviewStatus == ReviewStatus.Verified && vm.SelectedOverlay == second && !vm.ReviewItems.Contains(first),
                "Verify-and-next marks the current region, removes it from the filter and selects the next.");
            vm.UndoCommand.Execute(null);
            Check(first.ReviewStatus == ReviewStatus.Unreviewed && vm.ReviewItems.Contains(first) && vm.SelectedOverlays.SequenceEqual(new[] { first }),
                $"Undo restores review status, filtered membership and matching editor selection (status={first.ReviewStatus}, listed={vm.ReviewItems.Contains(first)}, selection={vm.SelectedOverlay?.ReadingOrder}).");
            vm.RedoCommand.Execute(null);
            Check(first.ReviewStatus == ReviewStatus.Verified && !vm.ReviewItems.Contains(first), "Redo reapplies review status and filter.");
            vm.ReviewFilterIndex = 3;
            vm.SelectedReviewItem = first;
            LocalizationService.SetLanguage(LocalizationService.EnglishLanguage);
            vm.RefreshLocalization();
            LocalizationService.Apply(window);
            await LayoutAsync();
            Check(vm.ReviewFilterIndex == 3 && Control<ComboBox>("ReviewFilterComboBox").SelectedIndex == 3, "Localization preserves a non-default review filter.");
            Check(first.ReviewStatus == ReviewStatus.Verified && Equals(statusCombo.SelectedValue, ReviewStatus.Verified), "Localization preserves an already-reviewed status and its selection.");
            LocalizationService.SetLanguage(originalLanguage);
            vm.RefreshLocalization();
            LocalizationService.Apply(window);
            vm.ReviewFilterIndex = 0;
            vm.SelectedReviewItem = second;
            await LayoutAsync();
            var editor = Control<TextBox>("SelectedLineTextBox");
            Check(editor.IsEnabled, "Line text remains editable in review mode.");
            editor.SetCurrentValue(TextBox.TextProperty, "読み取り結果を修正しました");
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Check(second.Text == "読み取り結果を修正しました" && second.ReviewStatus == ReviewStatus.Modified && vm.SelectedOverlay == second,
                "Text editing marks the region modified without clearing its editor.");
            vm.UndoCommand.Execute(null);
            Check(second.ReviewStatus == ReviewStatus.NeedsReview && vm.ReviewItems.Contains(second), "Undo text editing restores needs-review membership.");
            vm.RedoCommand.Execute(null);
            await vm.VerifyAndNextAsync();
            Check(vm.SelectedPage?.PageNumber == 2 && vm.SelectedOverlay?.ReadingOrder == 1 && second.ReviewStatus == ReviewStatus.Verified,
                "Navigation lazily loads the next page and continues its reading order.");
            var pageTwoFirst = vm.SelectedOverlay!;
            await LayoutAsync();
            Snapshot("review-next-page");
            await vm.NavigateReviewAsync(1);
            var pageTwoLast = vm.SelectedOverlay!;
            Check(pageTwoLast.ReadingOrder == 2, "Next finds the last review target.");
            await vm.NavigateReviewAsync(-1);
            Check(vm.SelectedOverlay == pageTwoFirst, "Previous returns to the preceding target.");
            vm.ReviewFilterIndex = 3;
            await vm.NavigateReviewAsync(-1);
            Check(vm.SelectedPage?.PageNumber == 1 && vm.SelectedOverlay?.ReadingOrder == 6, "Previous crosses pages and respects the all-status filter.");
            vm.ReviewFilterIndex = 0;
            Check(vm.HasNoReviewItems, "Empty current-page filter is explicitly reported.");
            await vm.NavigateReviewAsync(1);
            Check(vm.SelectedOverlay == pageTwoFirst, "Navigation skips pages without matching regions.");
            await vm.VerifyAndNextAsync();
            await vm.VerifyAndNextAsync();
            Check(vm.SelectedOverlay == pageTwoLast && pageTwoLast.ReviewStatus == ReviewStatus.Verified && vm.HasNoReviewItems && !vm.IsReviewNavigating,
                "Reaching the end keeps the last editor open and finishes without wrapping or getting stuck.");
            await vm.SaveProjectForDiagnosticsAsync(projectPath);
            await vm.LoadProjectForDiagnosticsAsync(projectPath);
            first = vm.OverlayItems.Single(region => !region.IsDeleted && region.ReadingOrder == 1);
            second = vm.OverlayItems.Single(region => !region.IsDeleted && region.ReadingOrder == 2);
            Check(first.ReviewStatus == ReviewStatus.Verified && second.ReviewStatus == ReviewStatus.Verified && second.Text == "読み取り結果を修正しました",
                "Review states and corrected text survive project save/reload.");
            Check(!first.IsGeometryLocked && second.IsGeometryLocked && !vm.HasUnsavedChanges, "Saved geometry locks are unchanged after reviewing and reloading.");
            var navigation = vm.NavigateReviewAsync(1);
            Check(vm.IsReviewNavigating && !vm.NextReviewCommand.CanExecute(null), "Background navigation prevents reentry.");
            vm.ReviewFilterIndex = 2;
            await navigation;
            Check(!vm.IsReviewNavigating && vm.SelectedPage?.PageNumber == 1, "Changing the filter cancels in-flight navigation without moving the page.");
            navigation = vm.NavigateReviewAsync(1);
            vm.CancelReviewNavigationCommand.Execute(null);
            await navigation;
            Check(!vm.IsReviewNavigating && vm.SelectedPage?.PageNumber == 1, "Cancel Search ends navigation safely.");
            navigation = vm.NavigateReviewAsync(1);
            vm.EditorModeIndex = 0;
            await navigation;
            Check(vm.CanEditGeometry && !vm.IsReviewNavigating, "Changing mode cancels pending navigation and restores geometry editing.");
            vm.SetOverlaySelection([first], first);
            Check(vm.IsSelectedGeometryEditable && vm.ToggleGeometryLockCommand.CanExecute(null), "Unlocked regions are editable again after leaving review.");
            await LayoutAsync();
            Check(Control<StackPanel>("ReviewPanel").Visibility == Visibility.Collapsed, "Review panel is hidden outside review mode.");
            vm.EditorModeIndex = 2;
            navigation = vm.NavigateReviewAsync(1);
            await vm.LoadPdfForDiagnosticsAsync(pdfPath);
            await navigation;
            Check(vm.OverlayItems.Count == 0 && vm.ReviewItems.Count == 0 && !vm.IsReviewNavigating, "Opening a different document cancels navigation without leaking old regions.");
            await vm.NavigateReviewAsync(1);
            Check(vm.SelectedOverlay is null && !vm.IsReviewNavigating, "A document with no OCR regions finishes navigation safely.");
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
            _diagnostics?.Write("review-mode-test.passed", $"{checks.Count} checks passed. {directory}");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            if (directory is not null) File.WriteAllText(Path.Combine(directory, "failure.txt"), exception.ToString());
            _diagnostics?.Write("review-mode-test.failed", exception.ToString());
            Shutdown(1);
        }
    }
}
