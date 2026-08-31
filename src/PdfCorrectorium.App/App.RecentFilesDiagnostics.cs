using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunRecentFilesTestAsync(MainWindow main, string[] args, int index)
    {
        string? output = null;
        var checks = new List<string>();
        var language = LocalizationService.CurrentLanguage;
        try
        {
            if (args.Length <= index + 1) throw new ArgumentException("A new recent-files-test output directory is required.");
            output = Path.GetFullPath(args[index + 1]);
            if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
            Directory.CreateDirectory(output);
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add("PASS: " + message);
            }
            var paths = new ApplicationPaths(StorageMode.Portable, Path.Combine(output, "config"),
                Path.Combine(output, "logs"), Path.Combine(output, "cache"), Path.Combine(output, "work"));
            ApplicationPathResolver.EnsureDirectories(paths);
            var settingsService = new ApplicationSettingsService(paths);
            await File.WriteAllTextAsync(settingsService.SettingsPath, "{\"formatVersion\":12}");
            Check(settingsService.Load().RecentFileLimit == 10 && settingsService.Load().FormatVersion == ApplicationSettings.CurrentFormatVersion,
                "Existing v12 settings receive the default recent-file count without a migration prompt.");
            await settingsService.SaveAsync(new ApplicationSettings { AutoSaveEnabled = false });
            var history = new RecentFilesService(paths);
            Check(history.Files.Count == 0, "Missing history loads as empty.");
            Check(new ApplicationSettings().RecentFileLimit == 10 && (new ApplicationSettings { RecentFileLimit = -1 }).Normalize().RecentFileLimit == 0 &&
                (new ApplicationSettings { RecentFileLimit = 100 }).Normalize().RecentFileLimit == 30, "Default and bounded display count are 10 and 0..30.");
            var pdf = Path.Combine(output, "_File 日本語 空白.PDF");
            var pdf2 = Path.Combine(output, "second.pdf");
            WriteDocumentUiTestPdf(pdf); WriteDocumentUiTestPdf(pdf2);
            var projectPath = Path.Combine(output, "編集_プロジェクト.pdfocrproj");
            var packages = new ProjectPackageService();
            var project = new PdfCorrectoriumProject
            {
                Name = "Recent files", SourcePdf = await packages.CreateSourceReferenceAsync(pdf, output),
                BookmarksInitialized = true, Bookmarks = [new PdfBookmark { Title = "Restored", PageNumber = 2 }],
                DocumentMetadata = new PdfDocumentMetadata { Title = "Recent project" },
            };
            await packages.SaveAsync(projectPath, project);
            var normalized = RecentFilesService.Normalize([pdf, pdf.ToUpperInvariant(), Path.Combine(output, "sub", "..", Path.GetFileName(pdf)),
                projectPath, "relative.pdf", "https://example.com/sample.pdf", Path.Combine(output, "other.txt"), "", "C:\\bad\n.pdf"]);
            Check(normalized.SequenceEqual(new[] { pdf, projectPath }), "Canonical absolute PDF/project paths are deduplicated case-insensitively; invalid/unsupported entries are ignored.");
            for (var i = 0; i < 35; i++) await history.RecordAsync(Path.Combine(output, $"history-{i}.pdf"));
            Check(history.Files.Count == 30 && history.Files[0].EndsWith("history-34.pdf") && history.Files[^1].EndsWith("history-5.pdf"), "History retains the newest 30 entries.");
            await history.RecordAsync(history.Files[10]);
            Check(history.Files.Count == 30 && history.Files[0].EndsWith("history-24.pdf"), "Reopening promotes an existing entry without duplication.");
            Check(new RecentFilesService(paths).Files.SequenceEqual(history.Files), "History order survives restarting the service.");
            await history.ClearAsync();
            var other = new RecentFilesService(paths);
            await Task.WhenAll(history.RecordAsync(pdf), other.RecordAsync(projectPath));
            history.Reload();
            Check(history.Files.Count == 2 && history.Files.Contains(pdf) && history.Files.Contains(projectPath), "Concurrent app instances merge updates without losing entries.");
            await other.ClearAsync();
            await history.RecordAsync(pdf2);
            Check(history.Files.SequenceEqual(new[] { pdf2 }), "A stale instance does not resurrect cleared history.");
            var before = await File.ReadAllTextAsync(history.HistoryPath);
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel(); var rejected = false;
                try { await history.ClearAsync(canceled.Token); } catch (OperationCanceledException) { rejected = true; }
                Check(rejected && await File.ReadAllTextAsync(history.HistoryPath) == before, "Canceled history write preserves the stored list.");
            }
            using (var locked = new FileStream(history.HistoryPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var rejected = false;
                try { await history.ClearAsync(); } catch (IOException) { rejected = true; }
                Check(rejected && await File.ReadAllTextAsync(history.HistoryPath) == before, "A locked history fails safely after bounded retries.");
            }
            Check(!Directory.EnumerateFiles(paths.ConfigurationDirectory, "*.tmp").Any(), "No temporary history files remain.");
            foreach (var invalid in new[] { "{", "null", "{\"formatVersion\":999,\"files\":[]}", "{\"formatVersion\":1,\"files\":null}", new string(' ', 8 * 1024 * 1024 + 1) })
            {
                await File.WriteAllTextAsync(history.HistoryPath, invalid);
                Check(new RecentFilesService(paths).Files.Count == 0, "Malformed/future/null/oversized history is ignored without crashing.");
            }
            await history.ClearAsync();
            var errors = new List<string>();
            MainWindowViewModel CreateVm() => new(packages, new PdfPreviewService(), new PdfExportService(),
                new NdlOcrCompanionService(), new DiagnosticLog(paths.LogDirectory), paths, () => { });
            var vm = CreateVm(); main.DataContext = vm;
            main.ClosePromptOverride = () => MessageBoxResult.No;
            vm.ErrorDialogOverride = (message, ex) => errors.Add(message);
            Check(!vm.CanOpenRecentFiles, "Empty history is disabled without a PDF loaded.");
            Check(await vm.OpenDocumentPathAsync(pdf) && vm.HasPreview && vm.RecentFiles.Single().FullPath == pdf, "Successful PDF loading adds its path after preview creation.");
            Check(await vm.OpenDocumentPathAsync(projectPath) && vm.CurrentDocumentMetadata?.Title == "Recent project" && vm.BookmarkItems.Count == 1 &&
                vm.RecentFiles[0].FullPath == projectPath, "Successful project loading records the project, not its source PDF.");
            var fresh = CreateVm();
            Check(!fresh.HasDocument && fresh.CanOpenRecentFiles && fresh.RecentFiles.Count == 2, "A new app can reopen history before any document is loaded.");
            Check(await fresh.OpenRecentFileAsync(pdf) && fresh.HasPreview && fresh.SourcePdfPath == pdf, "Recent entry uses normal loading from an empty app.");
            vm.ReloadRecentFiles();
            Check(vm.RecentFiles[0].FullPath == pdf, "Menu refresh observes another instance's latest file order.");
            Check(await vm.OpenRecentFileAsync(projectPath) && vm.RecentFiles[0].FullPath == projectPath, "Recent project reopens and moves to the top.");
            vm.AddManualOcrRegion(new Rect(20, 20, 140, 30));
            var commits = 0; vm.CommitPendingInputs = () => commits++;
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.Cancel;
            before = await File.ReadAllTextAsync(history.HistoryPath);
            Check(!await vm.OpenRecentFileAsync(pdf) && commits == 1 && vm.HasUnsavedChanges && vm.UndoCommand.CanExecute(null) && vm.ProjectPath == projectPath,
                "Canceling a recent-file switch commits pending UI text but retains current edits, project and Undo.");
            Check(await File.ReadAllTextAsync(history.HistoryPath) == before, "Canceled loading does not reorder history.");
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.Yes;
            vm.SaveBeforeSwitchOverride = () => Task.FromResult(false);
            Check(!await vm.OpenRecentFileAsync(pdf) && vm.HasUnsavedChanges && vm.ProjectPath == projectPath, "Canceled/failed Save blocks the recent-file switch.");
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
            var missing = Path.Combine(output, "missing.pdf");
            await history.RecordAsync(missing); vm.ReloadRecentFiles();
            before = await File.ReadAllTextAsync(history.HistoryPath);
            Check(!await vm.OpenRecentFileAsync(missing) && errors.Count > 0 && vm.ProjectPath == projectPath && vm.HasUnsavedChanges && vm.UndoCommand.CanExecute(null),
                "Missing recent file reports an error without discarding current document or Undo.");
            Check(await File.ReadAllTextAsync(history.HistoryPath) == before && vm.RecentFiles[0].FullPath == missing,
                "Missing entry remains available for removable drives, and a failed open does not reorder history.");
            var corrupt = Path.Combine(output, "corrupt.pdf"); await File.WriteAllTextAsync(corrupt, "not a PDF");
            Check(!await vm.OpenDocumentPathAsync(corrupt) && vm.RecentFiles.All(f => f.FullPath != corrupt), "A failed first open never enters history.");
            using (var lockedHistory = new FileStream(history.HistoryPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(await vm.OpenDocumentPathAsync(pdf2) && vm.SourcePdfPath == pdf2 && await File.ReadAllTextAsync(history.HistoryPath) == before,
                    "History storage failure warns but does not turn a successful PDF open into a load failure.");
            foreach (var property in new[] { "IsOpeningDocument", "IsPdfExporting", "IsBackgroundOperationVisible" })
            {
                typeof(MainWindowViewModel).GetProperty(property)!.SetValue(vm, true);
                Check(!vm.CanOpenRecentFiles && !vm.RecentFiles[0].OpenCommand.CanExecute(null) && !await vm.OpenRecentFileAsync(pdf), property + " disables recent-file actions.");
                typeof(MainWindowViewModel).GetProperty(property)!.SetValue(vm, false);
            }
            Check(await vm.ApplyApplicationSettingsAsync(vm.CurrentApplicationSettings with { RecentFileLimit = 1 }) && vm.RecentFiles.Count == 1 && vm.RecentFileCount == 3,
                "Reducing display count retains hidden history.");
            Check(await vm.ApplyApplicationSettingsAsync(vm.CurrentApplicationSettings with { RecentFileLimit = 30 }) && vm.RecentFiles.Count == 3,
                "Increasing display count restores hidden entries.");
            Check(await vm.ApplyApplicationSettingsAsync(vm.CurrentApplicationSettings with { RecentFileLimit = 0 }) && !vm.CanOpenRecentFiles && vm.RecentFiles.Count == 0,
                "Zero display count hides and disables the menu.");
            Check(await vm.OpenDocumentPathAsync(pdf2) && vm.RecentFileCount == 3 && await File.ReadAllTextAsync(history.HistoryPath) == before,
                "Zero display count stops recording new successful opens without clearing old history.");
            await vm.ApplyApplicationSettingsAsync(vm.CurrentApplicationSettings with { RecentFileLimit = 10 });
            var export = Path.Combine(output, "settings-export.json");
            await SettingsTransferService.ExportAsync(export, vm.CurrentApplicationSettings with { RecentFileLimit = 7 });
            var imported = await SettingsTransferService.ImportAsync(export);
            Check(imported.RecentFileLimit == 7 && !File.ReadAllText(export).Contains("_File") && !File.ReadAllText(export).Contains("recent-files.json"), "Settings transfer includes only display count, never recent file paths.");
            await vm.ApplyApplicationSettingsAsync(imported);
            Check(await File.ReadAllTextAsync(history.HistoryPath) == before, "Importing settings preserves local history.");

            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = -20000; main.Top = -20000; main.ShowActivated = false; main.ShowInTaskbar = false; main.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var menu = (MenuItem)main.FindName("RecentFilesMenuItem");
            Check(menu.IsEnabled && menu.Items.Count == 3, "Actual WPF recent menu displays all entries with the current count.");
            // Off-screen, non-activated windows cannot keep a popup open. Use WPF's actual
            // generator to prepare its containers without sending global input to the desktop.
            var generator = (System.Windows.Controls.Primitives.IItemContainerGenerator)menu.ItemContainerGenerator;
            MenuItem? entry = null;
            using (generator.StartAt(new System.Windows.Controls.Primitives.GeneratorPosition(-1, 0),
                System.Windows.Controls.Primitives.GeneratorDirection.Forward))
            {
                for (var i = 0; i < 2; i++)
                {
                    entry = (MenuItem)generator.GenerateNext();
                    generator.PrepareItemContainer(entry);
                }
            }
            Check(entry is not null && entry.Command == vm.RecentFiles[1].OpenCommand && (string)entry.ToolTip == vm.RecentFiles[1].FullPath && entry.HeaderTemplate is not null,
                "Menu entries bind reopening commands, full-path tooltips and literal filename templates.");
            entry!.Command.Execute(null);
            for (var i = 0; i < 1000 && vm.IsOpeningDocument; i++) await Task.Delay(10);
            Check(!vm.IsOpeningDocument && vm.ProjectPath == projectPath && vm.RecentFiles[0].FullPath == projectPath,
                "The generated menu command opens the selected project and refreshes history.");
            before = await File.ReadAllTextAsync(history.HistoryPath);
            foreach (var uiLanguage in new[] { "ja-JP", "en-US" })
            {
                LocalizationService.SetLanguage(uiLanguage);
                var dialog = new ApplicationSettingsWindow(vm.CurrentApplicationSettings, "Portable", settingsService.SettingsPath, vm.RecentFileCount)
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
                    ConfirmManagementAction = _ => false, ManagementMessageOverride = _ => { } };
                dialog.Show(); ((TabControl)dialog.FindName("SettingsTabs")).SelectedItem = dialog.FindName("ManagementTab");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var count = (TextBox)dialog.FindName("RecentFileLimitTextBox");
                var clear = (Button)dialog.FindName("ClearRecentFilesButton");
                Check(count.Text == "7" && clear.IsEnabled, uiLanguage + ": settings display the current count and enable history clearing.");
                foreach (var invalid in new[] { "", "-1", "31", "1.5", "abc" })
                {
                    count.Text = invalid; Check(!dialog.TryReadSettings(out _), "Settings reject invalid count: " + invalid);
                }
                foreach (var valid in new[] { "0", "10", "30" })
                {
                    count.Text = valid; Check(dialog.TryReadSettings(out var draft) && draft.RecentFileLimit == int.Parse(valid), "Settings accept display count " + valid);
                }
                count.Text = "7";
                AccessKeyManager.ProcessKey(PresentationSource.FromVisual(dialog), "C", false);
                Check(count.IsKeyboardFocusWithin || FocusManager.GetFocusedElement(dialog) == count, "Alt+C focuses the count field.");
                count.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                Check(FocusManager.GetFocusedElement(dialog) == clear, "Tab moves from count to Clear History.");
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!dialog.ClearRecentFilesRequested && await File.ReadAllTextAsync(history.HistoryPath) == before, "Declining Clear confirmation preserves history.");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap(620, 650, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 620, 650));
                    drawing.DrawRectangle(new VisualBrush((Visual)dialog.Content), null, new Rect(0, 0, 620, 650));
                }
                bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(output, "recent-settings-" + uiLanguage + ".png"))) encoder.Save(file);
                dialog.ConfirmManagementAction = _ => true; clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(dialog.ClearRecentFilesRequested && !clear.IsEnabled && await File.ReadAllTextAsync(history.HistoryPath) == before, "Confirmed Clear is staged until overall Save.");
                dialog.Close();
                Check(await File.ReadAllTextAsync(history.HistoryPath) == before, "Closing settings without Save discards the clear request.");
            }
            var saving = new ApplicationSettingsWindow(vm.CurrentApplicationSettings, "Portable", settingsService.SettingsPath, vm.RecentFileCount)
                { ConfirmManagementAction = _ => true, ManagementMessageOverride = _ => { } };
            ((Button)saving.FindName("ClearRecentFilesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using (var lockedSettings = new FileStream(settingsService.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!await main.ApplySettingsDialogAsync(saving) && await File.ReadAllTextAsync(history.HistoryPath) == before,
                    "Failed settings Save does not execute a pending history clear.");
            var activeSource = vm.SourcePdfPath;
            Check(await main.ApplySettingsDialogAsync(saving) && vm.RecentFileCount == 0 && !vm.CanOpenRecentFiles && new RecentFilesService(paths).Files.Count == 0,
                "Successful settings Save clears history on disk and in the menu.");
            Check(vm.HasDocument && vm.SourcePdfPath == activeSource && File.Exists(pdf) && File.Exists(pdf2) && File.Exists(projectPath),
                "Clearing history neither closes the active document nor deletes PDF/project files.");
            var empty = new ApplicationSettingsWindow(vm.CurrentApplicationSettings, "Portable", settingsService.SettingsPath);
            Check(!((Button)empty.FindName("ClearRecentFilesButton")).IsEnabled, "Clear History is disabled when the list is empty.");
            Check(main.Title.Contains(ApplicationBuildInfo.InformationalVersion.Split('+')[0], StringComparison.Ordinal), "Title reflects the current development revision.");
            await File.WriteAllLinesAsync(Path.Combine(output, "checks.txt"), checks);
            _diagnostics?.Write("recent-files-test.passed", $"{checks.Count} checks passed. {output}");
            Shutdown(0);
        }
        catch (Exception error)
        {
            if (output is not null)
            {
                await File.WriteAllLinesAsync(Path.Combine(output, "checks.txt"), checks);
                await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), error.ToString());
            }
            _diagnostics?.Write("recent-files-test.failed", error.ToString()); Shutdown(1);
        }
        finally { LocalizationService.SetLanguage(language); }
    }
}
