using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>実際の設定保存・移出入と設定画面を、新規の隔離フォルダーで検証します。</summary>
    private async Task RunSettingsTestAsync(MainWindow main, string[] args, int index)
    {
        string? output = null;
        var checks = new List<string>();
        var language = LocalizationService.CurrentLanguage;
        try
        {
            if (args.Length <= index + 1) throw new ArgumentException("A new settings-test output directory is required.");
            output = Path.GetFullPath(args[index + 1]);
            if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
            Directory.CreateDirectory(output);
            void Check(bool value, string message)
            {
                if (!value) throw new InvalidOperationException(message);
                checks.Add("PASS: " + message);
            }
            var paths = new ApplicationPaths(StorageMode.Portable, Path.Combine(output, "config"),
                Path.Combine(output, "logs"), Path.Combine(output, "cache"), Path.Combine(output, "work"));
            ApplicationPathResolver.EnsureDirectories(paths);
            var service = new ApplicationSettingsService(paths);
            var original = new ApplicationSettings
            {
                UiLanguage = "en-US",
                AutoSaveEnabled = false,
                PageListWidth = 300,
                PropertiesPanelWidth = 410,
                PageThumbnailSize = 183,
                PreviousCharacterShortcut = "Ctrl+Shift+F8",
                NextCharacterShortcut = "",
                ShowPropertyHelpText = true,
                UseRibbonUi = false,
                DocumentViewMode = DocumentViewMode.FacingPages,
                DocumentPageFlowMode = DocumentPageFlowMode.Continuous,
                FacingPagesShowCoverSeparately = false,
                FacingPagesBindingDirection = FacingPageBindingDirection.RightBinding,
            };
            var preset = WorkspacePreset.Capture(" 校正用_Layout ", original with { ShowPropertiesPanel = false });
            original = original with { WorkspacePresets = [preset] };
            var export = Path.Combine(output, "settings-日本語.json");
            await service.SaveAsync(original);
            var storedBefore = await File.ReadAllTextAsync(service.SettingsPath);
            await SettingsTransferService.ExportAsync(export, original);
            var imported = await SettingsTransferService.ImportAsync(export);
            Check(JsonSerializer.Serialize(imported) == JsonSerializer.Serialize(original.Normalize()), "All settings and presets round-trip, including hidden thumbnail size and unassigned shortcuts.");
            Check(!imported.UseRibbonUi, "The selected classic UI mode round-trips through settings export/import.");
            Check(imported.DocumentViewMode == DocumentViewMode.FacingPages &&
                imported.DocumentPageFlowMode == DocumentPageFlowMode.Continuous &&
                !imported.FacingPagesShowCoverSeparately &&
                imported.FacingPagesBindingDirection == FacingPageBindingDirection.RightBinding &&
                (new ApplicationSettings
                {
                    DocumentViewMode = (DocumentViewMode)999,
                    DocumentPageFlowMode = (DocumentPageFlowMode)999,
                    FacingPagesBindingDirection = (FacingPageBindingDirection)999,
                }).Normalize() is
                {
                    DocumentViewMode: DocumentViewMode.SinglePage,
                    DocumentPageFlowMode: DocumentPageFlowMode.PageByPage,
                    FacingPagesBindingDirection: FacingPageBindingDirection.LeftBinding,
                },
                "Document and facing-page settings round-trip, and unknown future values fall back safely.");
            Check((new ApplicationSettings
            {
                FormatVersion = 15,
                DocumentViewMode = DocumentViewMode.Continuous,
            }).Normalize() is
            {
                DocumentViewMode: DocumentViewMode.SinglePage,
                DocumentPageFlowMode: DocumentPageFlowMode.Continuous,
            },
                "Legacy continuous-page settings migrate to independent single-page and continuous-scroll axes.");
            Check(
                FacingPageLayoutCalculator.Calculate(1, 6, true, FacingPageBindingDirection.LeftBinding) == new FacingPageLayout(null, 1) &&
                FacingPageLayoutCalculator.Calculate(1, 6, true, FacingPageBindingDirection.RightBinding) == new FacingPageLayout(1, null) &&
                FacingPageLayoutCalculator.Calculate(2, 6, true, FacingPageBindingDirection.LeftBinding) == new FacingPageLayout(2, 3) &&
                FacingPageLayoutCalculator.Calculate(2, 6, true, FacingPageBindingDirection.RightBinding) == new FacingPageLayout(3, 2) &&
                FacingPageLayoutCalculator.Calculate(1, 6, false, FacingPageBindingDirection.LeftBinding) == new FacingPageLayout(1, 2) &&
                FacingPageLayoutCalculator.Calculate(1, 6, false, FacingPageBindingDirection.RightBinding) == new FacingPageLayout(2, 1) &&
                FacingPageLayoutCalculator.Calculate(6, 6, true, FacingPageBindingDirection.LeftBinding) == new FacingPageLayout(6, null) &&
                FacingPageLayoutCalculator.Calculate(6, 6, true, FacingPageBindingDirection.RightBinding) == new FacingPageLayout(null, 6),
                "Facing-page calculation covers both binding directions, optional covers, regular spreads, and an unpaired final page.");
            Check(await File.ReadAllTextAsync(service.SettingsPath) == storedBefore, "Import/export alone do not change live settings.");
            var exported = await File.ReadAllTextAsync(export);
            Check(!exported.Contains(paths.ConfigurationDirectory) && !exported.Contains(paths.WorkspaceDirectory), "Export does not include local configuration/workspace paths.");
            Check(preset.Name == "校正用_Layout", "Preset names are trimmed without changing user text.");
            var applied = preset.ApplyTo(original with { UiLanguage = "ja-JP", AutoSaveIntervalMinutes = 19 });
            Check(!applied.ShowPropertiesPanel && applied.PageListWidth == 300 && applied.PropertiesPanelWidth == 410, "Preset applies only captured panel layout.");
            Check(applied.UiLanguage == "ja-JP" && applied.AutoSaveIntervalMinutes == 19 &&
                applied.PreviousCharacterShortcut == original.PreviousCharacterShortcut && applied.PageThumbnailSize == 183, "Preset does not change language, shortcuts, recovery settings or hidden thumbnail size.");
            Check(new ApplicationSettingsService(paths).Load().WorkspacePresets.Single() == preset, "Named preset survives reopening the settings service.");

            async Task Reject(string name, Action<JsonObject> mutate)
            {
                var node = JsonNode.Parse(exported)!.AsObject();
                mutate(node);
                var path = Path.Combine(output, name + ".json");
                await File.WriteAllTextAsync(path, node.ToJsonString());
                var rejected = false;
                try { await SettingsTransferService.ImportAsync(path); }
                catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
                Check(rejected, name + ": invalid import is rejected.");
                Check(await File.ReadAllTextAsync(service.SettingsPath) == storedBefore, name + ": existing settings are intact.");
            }
            await Reject("wrong-format", n => n["format"] = "OtherApplication");
            await Reject("future-transfer-version", n => n["formatVersion"] = 99);
            await Reject("wrong-version-type", n => n["formatVersion"] = "1");
            await Reject("future-settings-version", n => n["settings"]!["formatVersion"] = 999);
            await Reject("missing-settings-version", n => n["settings"]!.AsObject().Remove("formatVersion"));
            await Reject("null-settings", n => n["settings"] = null);
            await Reject("reserved-shortcut", n => n["settings"]!["previousCharacterShortcut"] = "Alt+O");
            await Reject("invalid-shortcut", n => n["settings"]!["previousCharacterShortcut"] = "invalid-key-name");
            await Reject("duplicate-shortcut", n => n["settings"]!["nextCharacterShortcut"] = original.PreviousCharacterShortcut);
            await Reject("null-presets", n => n["settings"]!["workspacePresets"] = null);
            await Reject("null-preset", n => n["settings"]!["workspacePresets"] = new JsonArray((JsonNode?)null));
            await Reject("empty-preset-name", n => n["settings"]!["workspacePresets"]![0]!["name"] = " ");
            await Reject("long-preset-name", n => n["settings"]!["workspacePresets"]![0]!["name"] = new string('A', 65));
            await Reject("duplicate-preset", n => n["settings"]!["workspacePresets"]!.AsArray().Add(n["settings"]!["workspacePresets"]![0]!.DeepClone()));
            await Reject("too-many-presets", n => n["settings"]!["workspacePresets"] = new JsonArray(Enumerable.Range(0, 21).Select(i =>
                JsonSerializer.SerializeToNode(preset with { Name = "Preset " + i }, new JsonSerializerOptions(JsonSerializerDefaults.Web))).ToArray()));
            foreach (var (name, text) in new[]
            {
                ("empty", ""), ("unrelated", "{}"), ("raw-settings", storedBefore),
                ("malformed", "{"), ("oversized", new string(' ', SettingsTransferService.MaximumFileBytes + 1)),
                ("duplicate-json-field", exported.Replace("\"formatVersion\": 1,", "\"formatVersion\": 1, \"formatVersion\": 1,", StringComparison.Ordinal)),
            })
            {
                var path = Path.Combine(output, name + ".json");
                await File.WriteAllTextAsync(path, text);
                var rejected = false;
                try { await SettingsTransferService.ImportAsync(path); }
                catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
                Check(rejected, name + " is rejected without changing settings.");
            }
            var legacy = JsonNode.Parse(exported)!;
            legacy["settings"]!["formatVersion"] = 11;
            legacy["settings"]!.AsObject().Remove("workspacePresets");
            legacy["settings"]!["pageListWidth"] = -1;
            await File.WriteAllTextAsync(Path.Combine(output, "legacy-export.json"), legacy.ToJsonString());
            var upgraded = await SettingsTransferService.ImportAsync(Path.Combine(output, "legacy-export.json"));
            Check(upgraded.FormatVersion == ApplicationSettings.CurrentFormatVersion && upgraded.WorkspacePresets.Count == 0 && upgraded.PageListWidth == 160, "Older exported settings receive preset defaults and bounded widths.");
            await File.WriteAllTextAsync(service.SettingsPath, """{"formatVersion":11,"pageListWidth":280,"previousCharacterShortcut":"Ctrl+Shift+F8"}""");
            Check(service.Load().PageListWidth == 280 && service.Load().WorkspacePresets.Count == 0 && service.Load().UseRibbonUi,
                "Existing raw local settings remain loadable and migrate to the ribbon default when no UI mode was saved.");
            await service.SaveAsync(original);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var canceled = false;
                try { await SettingsTransferService.ExportAsync(export, original with { UiLanguage = "ja-JP" }, cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && await File.ReadAllTextAsync(export) == exported, "Canceled export preserves an existing destination.");
                Check(!Directory.EnumerateFiles(output, "*.tmp").Any(), "Canceled export leaves no temporary files.");
            }
            await Task.WhenAll(service.SaveAsync(original), service.SaveAsync(original with { UiLanguage = "ja-JP" }));
            Check(service.Load().WorkspacePresets.Single() == preset, "Overlapping settings saves each commit a complete file.");

            // Two application processes can start from the same baseline and
            // save unrelated preferences later. A stale writer must merge only
            // its changed fields rather than erase the other process's update.
            await service.SaveAsync(original);
            var firstInstance = new ApplicationSettingsService(paths);
            var secondInstance = new ApplicationSettingsService(paths);
            var firstBaseline = firstInstance.Load();
            var secondBaseline = secondInstance.Load();
            await firstInstance.SaveAsync(firstBaseline with { PageListWidth = 333 });
            await secondInstance.SaveAsync(secondBaseline with { UiLanguage = LocalizationService.JapaneseLanguage });
            var mergedSettings = new ApplicationSettingsService(paths).Load();
            Check(
                mergedSettings.PageListWidth == 333 &&
                mergedSettings.UiLanguage == LocalizationService.JapaneseLanguage,
                "Stale application instances merge unrelated settings changes.");

            var vm = new MainWindowViewModel(new ProjectPackageService(), new PdfPreviewService(), new PdfExportService(),
                new NdlOcrCompanionService(), new DiagnosticLog(paths.LogDirectory), paths, () => { });
            main.DataContext = vm;
            // Mirror the constructor's subscription after replacing its VM with an isolated test VM.
            vm.PropertyChanged += typeof(MainWindow).GetMethod("ViewModel_OnPropertyChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .CreateDelegate<System.ComponentModel.PropertyChangedEventHandler>(main);
            main.ClosePromptOverride = () => MessageBoxResult.No;
            vm.ErrorDialogOverride = (message, ex) => throw new InvalidOperationException(message, ex);
            Check(await vm.ApplyApplicationSettingsAsync(original), "Settings apply without a PDF loaded.");
            Check(!vm.IsRibbonUiMode && vm.IsClassicUiMode, "Applying saved settings restores classic UI mode before the window is shown.");
            vm.IsRibbonUiMode = true;
            for (var attempt = 0; attempt < 50 && !new ApplicationSettingsService(paths).Load().UseRibbonUi; attempt++)
                await Task.Delay(20);
            var reopenedVm = new MainWindowViewModel(new ProjectPackageService(), new PdfPreviewService(), new PdfExportService(),
                new NdlOcrCompanionService(), new DiagnosticLog(paths.LogDirectory), paths, () => { });
            Check(reopenedVm.IsRibbonUiMode && !reopenedVm.IsClassicUiMode,
                "Switching UI mode persists immediately and is restored by the next application instance.");
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = -20000; main.Top = -20000; main.Width = 1400; main.Height = 850;
            main.ShowActivated = false; main.ShowInTaskbar = false; main.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var grid = (Grid)main.FindName("WorkspaceLayoutGrid");
            grid.ColumnDefinitions[0].Width = new GridLength(310);
            grid.ColumnDefinitions[4].Width = new GridLength(430);
            main.UpdateLayout();
            var captured = main.CaptureWorkspaceSettings();
            Check(Math.Abs(captured.PageListWidth - 310) < 1 && Math.Abs(captured.PropertiesPanelWidth - 430) < 1, "Settings capture splitter-adjusted widths.");

            foreach (var uiLanguage in new[] { "ja-JP", "en-US" })
            {
                LocalizationService.SetLanguage(uiLanguage);
                var dialog = new ApplicationSettingsWindow(captured, "Portable", service.SettingsPath)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    ConfirmManagementAction = _ => true,
                    ManagementMessageOverride = _ => { },
                };
                dialog.Show();
                ((TabControl)dialog.FindName("SettingsTabs")).SelectedItem = dialog.FindName("ManagementTab");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                void Click(string name) => ((Button)dialog.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var names = (TextBox)dialog.FindName("WorkspacePresetNameTextBox");
                var presets = (ComboBox)dialog.FindName("WorkspacePresetComboBox");
                names.Text = "_File";
                Click("SaveWorkspacePresetButton");
                Check(presets.Items.Count == 2 && ((WorkspacePreset)presets.SelectedItem).Name == "_File", uiLanguage + ": UI registers the draft layout without translating the user name.");
                Check(((WorkspacePreset)presets.SelectedItem).PageListWidth == 310, "Preset captures actual panel width.");
                ((Slider)dialog.FindName("PageListWidthSlider")).Value = 360;
                Click("SaveWorkspacePresetButton");
                Check(presets.Items.Count == 2 && ((WorkspacePreset)presets.SelectedItem).PageListWidth == 360, "Confirmed same-name update replaces, rather than duplicates.");
                dialog.ConfirmManagementAction = _ => false;
                ((Slider)dialog.FindName("PageListWidthSlider")).Value = 400;
                Click("SaveWorkspacePresetButton");
                Click("DeleteWorkspacePresetButton");
                Check(presets.Items.Count == 2 && ((WorkspacePreset)presets.SelectedItem).PageListWidth == 360, "Canceled update/delete preserve the preset.");
                ((TextBox)dialog.FindName("UndoHistoryLimitTextBox")).Text = "157";
                Click("ApplyWorkspacePresetButton");
                Check(((Slider)dialog.FindName("PageListWidthSlider")).Value == 360 &&
                    ((TextBox)dialog.FindName("UndoHistoryLimitTextBox")).Text == "157", "Applying a preset preserves unrelated pending edits.");
                Check(dialog.TryReadSettings(out var draft) && draft.WorkspacePresets.Count == 2 && draft.PageThumbnailSize == 183, "Settings save includes presets without resetting fields not exposed by the dialog.");
                Check(vm.CurrentApplicationSettings.WorkspacePresets.Count == 1, "Draft registration/apply do not change live settings.");
                var scope = PresentationSource.FromVisual(dialog);
                AccessKeyManager.ProcessKey(scope, "N", false);
                Check(names.IsKeyboardFocusWithin || FocusManager.GetFocusedElement(dialog) == names, "Alt+N focuses the preset name.");
                names.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                Check(FocusManager.GetFocusedElement(dialog) == dialog.FindName("SaveWorkspacePresetButton"), "Tab moves from name to registration.");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap(620, 650, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 620, 650));
                    drawing.DrawRectangle(new VisualBrush((Visual)dialog.Content), null, new Rect(0, 0, 620, 650));
                }
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(output, "management-" + uiLanguage + ".png"))) encoder.Save(file);
                dialog.ConfirmManagementAction = _ => true;
                Click("DeleteWorkspacePresetButton");
                Check(presets.Items.Count == 1, "Confirmed deletion removes only the selected preset.");
                dialog.LoadDraft(imported);
                Check(presets.Items.Count == 1 && dialog.TryReadSettings(out var importedDraft) && importedDraft.PreviousCharacterShortcut == original.PreviousCharacterShortcut,
                    "Import populates the dialog including shortcuts and presets.");
                dialog.Close();
                Check(vm.CurrentApplicationSettings.WorkspacePresets.Count == 1, "Closing without Save does not apply the draft.");
            }

            var pdf = Path.Combine(output, "source.pdf");
            WriteDocumentUiTestPdf(pdf, 6);
            await vm.LoadPdfForDiagnosticsAsync(pdf);
            vm.DocumentViewMode = DocumentViewMode.SinglePage;
            vm.DocumentPageFlowMode = DocumentPageFlowMode.Continuous;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            main.UpdateLayout();
            var continuousHost = (Controls.ContinuousPagePanel)main.FindName("ContinuousDocumentLayoutHost");
            Check(continuousHost.Visibility == Visibility.Visible &&
                  continuousHost.PageCount == vm.PageItems.Count &&
                  continuousHost.Children.Count <= 13 &&
                  ReferenceEquals(((Border)main.FindName("PreviewPageHost")).Parent, continuousHost),
                "Continuous view exposes every logical page through a virtualized layout while retaining only bounded visual children.");
            Border? passivePage = null;
            for (var attempt = 0; attempt < 200 && passivePage is null; attempt++)
            {
                passivePage = continuousHost.Children.OfType<Border>().SingleOrDefault(element => element.Tag is 2);
                if (passivePage is null) await Task.Delay(25);
            }
            Check(passivePage is not null, "Continuous view realizes the visible second page.");
            var passiveImage = ((Grid)passivePage!.Child).Children.OfType<Image>().Single();
            for (var attempt = 0; attempt < 200 && passiveImage.Source is null; attempt++)
            {
                await Task.Delay(25);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            Check(passiveImage.Source is not null && !vm.HasNextPreview,
                "Continuous view lazily renders a visible page through its bounded viewport cache, not the facing-page neighbor slots.");
            ((ScrollViewer)main.FindName("PreviewScrollViewer")).ScrollToEnd();
            Border? lastPage = null;
            Image? lastImage = null;
            for (var attempt = 0; attempt < 200 && lastImage?.Source is null; attempt++)
            {
                await Task.Delay(25);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                lastPage = continuousHost.Children.OfType<Border>().SingleOrDefault(element => element.Tag is 6);
                lastImage = lastPage?.Child is Grid lastSurface ? lastSurface.Children.OfType<Image>().Single() : null;
            }
            Check(lastImage?.Source is not null,
                "Scrolling to the end reaches and lazily renders the last page without replacing the document with a three-page window.");
            vm.NavigateFromAdjacentPreview(2);
            for (var attempt = 0; attempt < 200 && vm.SelectedPage?.PageNumber != 2; attempt++) await Task.Delay(10);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(vm.SelectedPage?.PageNumber == 2 &&
                  ReferenceEquals(((Border)main.FindName("PreviewPageHost")).Parent, continuousHost) &&
                  ((Border)main.FindName("PreviewPageHost")).Tag is 2,
                "Selecting any continuous preview promotes that position to the editable page without leaving continuous view.");
            vm.NavigateFromAdjacentPreview(1);
            for (var attempt = 0; attempt < 200 && vm.SelectedPage?.PageNumber != 1; attempt++) await Task.Delay(10);
            vm.FacingPagesShowCoverSeparately = true;
            vm.FacingPagesBindingDirection = FacingPageBindingDirection.LeftBinding;
            vm.DocumentViewMode = DocumentViewMode.FacingPages;
            vm.DocumentPageFlowMode = DocumentPageFlowMode.PageByPage;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var coverLayout = vm.GetCurrentFacingPageLayout();
            Check(Grid.GetColumn((Border)main.FindName("PreviewPageHost")) == 1,
                $"Left-bound facing view places a separate cover on the right (selected={vm.SelectedPage?.PageNumber}, left={coverLayout.LeftPageNumber}, right={coverLayout.RightPageNumber}, column={Grid.GetColumn((Border)main.FindName("PreviewPageHost"))}).");
            Check(((Border)main.FindName("FacingBlankPageHost")).Visibility == Visibility.Hidden,
                "Facing view reserves the cover's empty side without drawing a white blank page.");
            Check(main.GetPreviewLayoutDimensions().Width > vm.PreviewPixelWidth * 2,
                "Facing view fit dimensions include both sides of the spread.");
            Check(vm.DocumentViewModeDescription.Contains("表紙", StringComparison.Ordinal) ||
                  vm.DocumentViewModeDescription.Contains("cover", StringComparison.OrdinalIgnoreCase),
                "Facing view exposes the active cover behavior in its description.");
            vm.FacingPagesBindingDirection = FacingPageBindingDirection.RightBinding;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(Grid.GetColumn((Border)main.FindName("PreviewPageHost")) == 0 &&
                  Grid.GetColumn((Border)main.FindName("FacingBlankPageHost")) == 1,
                "Right-bound facing view places a separate cover on the left.");
            vm.FacingPagesShowCoverSeparately = false;
            for (var attempt = 0; attempt < 200 && !vm.HasNextPreview; attempt++) await Task.Delay(10);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(Grid.GetColumn((Border)main.FindName("PreviewPageHost")) == 1 &&
                  Grid.GetColumn((Border)main.FindName("NextPreviewPageHost")) == 0 &&
                  ((Border)main.FindName("FacingBlankPageHost")).Visibility == Visibility.Collapsed &&
                  vm.NextPreviewPageNumber == 2,
                "Without a separate cover, right-bound facing view pairs pages 1 and 2 in right-to-left order.");
            var visibleCompanionBeforeNavigation = vm.NextPreviewImage;
            vm.NextPageCommand.Execute(null);
            Check(vm.SelectedPage?.PageNumber == 1 && vm.HasNextPreview &&
                  ReferenceEquals(vm.NextPreviewImage, visibleCompanionBeforeNavigation),
                "Facing-page navigation retains the complete current spread while the destination spread is prepared.");
            for (var attempt = 0; attempt < 200 && (vm.SelectedPage?.PageNumber != 2 || !vm.HasPreviousPreview); attempt++) await Task.Delay(10);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(Grid.GetColumn((Border)main.FindName("PreviewPageHost")) == 0 &&
                  Grid.GetColumn((Border)main.FindName("PreviousPreviewPageHost")) == 1 &&
                  vm.PreviousPreviewPageNumber == 1,
                "Selecting the second page retains the right-bound page pair and correct companion.");
            Check(!vm.HasNextPreview,
                "Prepared facing-page navigation replaces both page slots together without leaving a stale companion.");
            vm.FacingPagesBindingDirection = FacingPageBindingDirection.LeftBinding;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(Grid.GetColumn((Border)main.FindName("PreviewPageHost")) == 1 &&
                  Grid.GetColumn((Border)main.FindName("PreviousPreviewPageHost")) == 0,
                "Changing to left binding reverses the same pair without rebuilding the source PDF.");
            vm.NavigateFromAdjacentPreview(1);
            for (var attempt = 0; attempt < 200 && vm.SelectedPage?.PageNumber != 1; attempt++) await Task.Delay(10);
            vm.FacingPagesShowCoverSeparately = true;
            vm.DocumentPageFlowMode = DocumentPageFlowMode.Continuous;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            main.UpdateLayout();
            var separateCover = continuousHost.GetPageSlotBounds(1);
            var followingSpread = continuousHost.GetPageSlotBounds(2);
            Check(continuousHost.Visibility == Visibility.Visible &&
                  separateCover.Left > 0 && followingSpread.Top > separateCover.Top,
                "Continuous facing view reserves an undrawn left side for a separate left-bound cover and places the next spread below it.");
            vm.FacingPagesShowCoverSeparately = false;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            main.UpdateLayout();
            var leftFirst = continuousHost.GetPageSlotBounds(1);
            var leftSecond = continuousHost.GetPageSlotBounds(2);
            Check(Math.Abs(leftFirst.Top - leftSecond.Top) < 0.1 && leftFirst.Left < leftSecond.Left,
                "Continuous facing view combines pages 1 and 2 in one left-bound spread when the cover is not separate.");
            vm.FacingPagesBindingDirection = FacingPageBindingDirection.RightBinding;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            main.UpdateLayout();
            var rightFirst = continuousHost.GetPageSlotBounds(1);
            var rightSecond = continuousHost.GetPageSlotBounds(2);
            Check(Math.Abs(rightFirst.Top - rightSecond.Top) < 0.1 && rightFirst.Left > rightSecond.Left,
                "Continuous facing view mirrors the same spread for right binding without duplicating page images.");
            vm.DocumentPageFlowMode = DocumentPageFlowMode.PageByPage;
            vm.DocumentViewMode = DocumentViewMode.SinglePage;
            vm.AddManualOcrRegion(new Rect(20, 20, 140, 30));
            // Use the actual UI zoom action to leave auto-fit; the fixture VM replaces the constructor VM.
            typeof(MainWindow).GetMethod("PreviewZoomInMenuItem_OnClick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(main, [main, new RoutedEventArgs()]);
            var wasDirty = vm.HasUnsavedChanges;
            var regionText = vm.OverlayItems.Last().Text;
            var zoom = vm.ZoomPercent;
            Check(await vm.ApplyApplicationSettingsAsync(preset.ApplyTo(original)), "Preset settings persist with an edited PDF open.");
            main.RestoreWorkspaceWidthBindings();
            main.UpdateLayout();
            Check(grid.ColumnDefinitions[4].ActualWidth == 0 && Math.Abs(grid.ColumnDefinitions[0].ActualWidth - 300) < 1, "Applying settings restores width bindings and hides the requested panel.");
            Check(vm.HasUnsavedChanges == wasDirty && vm.OverlayItems.Last().Text == regionText && vm.UndoCommand.CanExecute(null),
                "Applying a layout preserves PDF edits, dirty state and Undo.");
            Check(vm.ZoomPercent == zoom, $"Applying a layout preserves manual zoom ({zoom} -> {vm.ZoomPercent}).");
            Check(vm.PreviousCharacterToolTip.Contains("Ctrl+Shift+F8", StringComparison.Ordinal), "Imported custom shortcut remains visible in its tooltip.");
            await File.WriteAllLinesAsync(Path.Combine(output, "checks.txt"), checks);
            _diagnostics?.Write("settings-test.passed", $"{checks.Count} checks passed. {output}");
            Shutdown(0);
        }
        catch (Exception error)
        {
            if (output is not null)
            {
                await File.WriteAllLinesAsync(Path.Combine(output, "checks.txt"), checks);
                await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), error.ToString());
            }
            _diagnostics?.Write("settings-test.failed", error.ToString());
            Shutdown(1);
        }
        finally { LocalizationService.SetLanguage(language); }
    }
}
