using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Xml.Linq;
using AvalonDock.Controls;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunPaneVisibilityTestAsync(MainWindow window, string[] args, int option)
    {
        string? directory = null;
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
            File.WriteAllLines(Path.Combine(directory!, "checks.txt"), checks);
        }
        static LayoutAnchorable Pane(MainWindow host, string id) => id switch
        {
            "navigation" => host.AvalonNavigation!, "bookmarks" => host.AvalonBookmarks!, _ => host.PropertyPanes[id],
        };
        static MenuItem Menu(MainWindow host, string id)
        {
            var path = id switch { "navigation" => "ShowPageListPanel", "bookmarks" => "ShowBookmarksPanel",
                "properties" => "ShowPropertiesPanel", _ => null };
            var entries = ((MenuItem)host.FindName("ViewMenu")).Items.OfType<MenuItem>();
            if (path is not null) return entries.Single(item =>
                BindingOperations.GetBinding(item, MenuItem.IsCheckedProperty)?.Path?.Path == path);
            var menu = entries.Single(item => item.Header is string text && text == "機能別プロパティ");
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, menu));
            return menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Pane(host, id).Title));
        }
        static void Click(MenuItem menu) => typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(menu, null); // Executes WPF's actual check/two-way-binding/click sequence, not a synthetic Click event.
        void HideWithNativeCommand(MainWindow host, string id)
        {
            var pane = Pane(host, id);
            var item = (LayoutAnchorableItem)host.AvalonDockPreviewManager!.GetLayoutItemFromModel(pane);
            Check(pane.CanHide && !pane.CanClose && item.HideCommand.CanExecute(null), id + ": the native X command hides rather than deletes this reusable pane.");
            item.HideCommand.Execute(null);
        }
        async Task Settle(MainWindow host)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            host.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
        var ids = new[] { "navigation", "bookmarks", "properties", "link-properties", "redaction-properties", "comment-properties" };
        try
        {
            if (args.Length <= option + 1) throw new ArgumentException("A new diagnostic directory is required.");
            directory = Path.GetFullPath(args[option + 1]);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory)
                .Any(path => Path.GetFileName(path) != ".appstate")) throw new IOException("Use a new diagnostic directory.");
            Directory.CreateDirectory(directory);
            window.ClosePromptOverride = () => MessageBoxResult.No;
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000; window.Show(); await Settle(window);
            var vm = (MainWindowViewModel)window.DataContext;
            if (args.Contains("--side-auto-hide-only", StringComparer.OrdinalIgnoreCase))
            {
                await VerifySideAutoHideAsync(directory, Check); Shutdown(0); return;
            }
            vm.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            Check(ids.All(id => Pane(window, id).IsVisible && Menu(window, id).IsChecked), "All six reusable panels and their View menu entries start visible.");
            foreach (var id in ids)
            {
                var pane = Pane(window, id); var content = pane.Content;
                HideWithNativeCommand(window, id); await Settle(window);
                Check(pane.IsHidden && !Menu(window, id).IsChecked, id + ": X immediately clears the actual View menu check.");
                Click(Menu(window, id)); await Settle(window);
                Check(pane.IsVisible && ReferenceEquals(pane.Root, window.AvalonDockPreviewManager!.Layout) && pane.IsSelected &&
                    Menu(window, id).IsChecked && ReferenceEquals(content, pane.Content), id + ": one View-menu click restores and selects the same content.");
                Click(Menu(window, id)); await Settle(window);
                Check(pane.IsHidden && !Menu(window, id).IsChecked, id + ": the View menu can hide a restored pane too.");
                Click(Menu(window, id)); await Settle(window);
                Check(pane.IsVisible && Menu(window, id).IsChecked, id + ": repeated View-menu toggles do not get stuck.");
            }

            // Close all siblings so that their previous container may be pruned.
            foreach (var id in ids) HideWithNativeCommand(window, id);
            await Settle(window);
            Check(ids.All(id => Pane(window, id).IsHidden && !Menu(window, id).IsChecked), "All panels can be hidden together with consistent menu checks.");
            window.SynchronizeAvalonDockLayout(); await Settle(window);
            var hiddenXml = window.CaptureNativeWorkspace();
            Check(WorkspaceDockLayout.Normalize(hiddenXml) == hiddenXml, "Hidden panels still serialize all nine known content IDs.");
            await vm.FlushDisplaySettingsAsync();
            var closedSettings = vm.CurrentApplicationSettings;
            Check(!closedSettings.ShowPageListPanel && !closedSettings.ShowBookmarksPanel && !closedSettings.ShowPropertiesPanel,
                "The saved panel booleans agree with native hidden state, not their old pre-X values.");
            foreach (var id in ids) { Click(Menu(window, id)); await Settle(window); }
            Check(ids.All(id => Pane(window, id).IsVisible && ReferenceEquals(Pane(window, id).Root, window.AvalonDockPreviewManager!.Layout)),
                "Closing all siblings and pruning their empty containers does not make any panel unrecoverable.");

            var orphanedLayout = XDocument.Parse(hiddenXml);
            foreach (var node in orphanedLayout.Descendants().Where(node => ids.Contains((string?)node.Attribute("ContentId"))))
            {
                node.Attribute("PreviousContainerId")?.Remove();
                node.Attribute("PreviousContainerIndex")?.Remove();
                node.SetAttributeValue("CanClose", true);
                node.SetAttributeValue("CanHide", false);
            }
            foreach (var node in orphanedLayout.Descendants().Where(node => (string?)node.Attribute("ContentId") is "main-toolbar" or "page-toolbar"))
            {
                node.SetAttributeValue("CanClose", true); node.SetAttributeValue("CanHide", true); node.SetAttributeValue("CanAutoHide", true);
            }
            Check(window.RestoreNativeWorkspace(orphanedLayout.ToString()), "A legacy hidden layout with missing restore destinations can still load.");
            await Settle(window);
            Check(ids.All(id => Pane(window, id) is { CanClose: false, CanHide: true }),
                "Legacy layout flags cannot make reusable panels permanently closeable or unhideable.");
            foreach (var id in ids) { Click(Menu(window, id)); await Settle(window); }
            Check(ids.All(id => Pane(window, id).IsVisible && ReferenceEquals(Pane(window, id).Root, window.AvalonDockPreviewManager!.Layout)),
                "The safe fallback restores every existing view when PreviousContainer references have already been lost.");
            Check(window.GetAvalonDockSide(Pane(window, "navigation").Parent as LayoutAnchorablePane) == AnchorSide.Left &&
                window.GetAvalonDockSide(Pane(window, "properties").Parent as LayoutAnchorablePane) == AnchorSide.Right,
                "Missing-destination fallback restores pages on the left and OCR properties on their recorded right side.");

            foreach (var id in ids)
            {
                var pane = Pane(window, id);
                pane.FloatingLeft = -15000; pane.FloatingTop = -15000;
                pane.FloatingWidth = 420; pane.FloatingHeight = 350;
                pane.Float(); await Settle(window);
                Check(pane.IsFloating, id + ": the test uses a genuine floating panel.");
                HideWithNativeCommand(window, id); await Settle(window);
                Check(pane.IsHidden && !Menu(window, id).IsChecked, id + ": floating X clears the menu check.");
                Click(Menu(window, id)); await Settle(window);
                Check(pane.IsVisible && pane.IsFloating && ReferenceEquals(pane.Root, window.AvalonDockPreviewManager!.Layout),
                    id + ": the menu restores the floating panel without dropping its content.");
                pane.Dock(); await Settle(window);
            }

            foreach (var id in new[] { "navigation", "bookmarks", "properties" })
            {
                var pane = Pane(window, id); pane.ToggleAutoHide(); await Settle(window);
                Check(pane.IsAutoHidden && Menu(window, id).IsChecked, id + ": auto-hide is still enabled, not mistaken for X-hidden.");
                HideWithNativeCommand(window, id); await Settle(window);
                Click(Menu(window, id)); await Settle(window);
                Check(pane.IsVisible && pane.IsAutoHidden && Menu(window, id).IsChecked,
                    id + ": a hidden auto-hide tab can be restored while retaining auto-hide.");
                pane.ToggleAutoHide(); await Settle(window);
            }
            var visibleXml = window.CaptureNativeWorkspace();
            Check(window.RestoreNativeWorkspace(visibleXml), "Replacing the native layout succeeds with the existing view instances.");
            await Settle(window);
            HideWithNativeCommand(window, "bookmarks"); await Settle(window);
            Check(!Menu(window, "bookmarks").IsChecked, "Visibility subscriptions follow the new models after layout restoration.");
            Click(Menu(window, "bookmarks")); await Settle(window);
            Check(Pane(window, "bookmarks").IsVisible, "The restored model also responds to its View menu.");

            var restartDirectory = Path.Combine(directory, "restart");
            var paths = new ApplicationPaths(StorageMode.Portable, Path.Combine(restartDirectory, ".appstate", "config"),
                Path.Combine(restartDirectory, ".appstate", "logs"), Path.Combine(restartDirectory, ".appstate", "cache"),
                Path.Combine(restartDirectory, ".appstate", "workspaces"));
            ApplicationPathResolver.EnsureDirectories(paths);
            // Simulate a saved layout affected by the old synchronization bug.
            await new ApplicationSettingsService(paths).SaveAsync(closedSettings with { WorkspaceLayoutXml = hiddenXml,
                ShowPageListPanel = true, ShowBookmarksPanel = true, ShowPropertiesPanel = true });
            var restarted = new MainWindow(diagnosticDirectory: restartDirectory)
                { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000, Top = -20000, ClosePromptOverride = () => MessageBoxResult.No };
            try
            {
                restarted.Show(); await Settle(restarted);
                Check(ids.All(id => Pane(restarted, id).IsHidden && !Menu(restarted, id).IsChecked),
                    "Startup trusts the complete hidden layout and repairs legacy stale visibility booleans without reopening panels.");
                foreach (var id in ids) { Click(Menu(restarted, id)); await Settle(restarted); }
                Check(ids.All(id => Pane(restarted, id).IsVisible), "Every persisted hidden panel is restored by one menu click after restart.");
                HideWithNativeCommand(restarted, "properties"); await Settle(restarted);
                var ocrEntry = Menu(restarted, "properties");
                var propertyMenu = ((MenuItem)restarted.FindName("ViewMenu")).Items.OfType<MenuItem>()
                    .Single(item => item.Header is string text && text == "機能別プロパティ");
                propertyMenu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, propertyMenu));
                var duplicateOcr = propertyMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Pane(restarted, "properties").Title));
                Click(duplicateOcr); await Settle(restarted);
                Check(ocrEntry.IsChecked && Pane(restarted, "properties").IsVisible, "Both OCR-property menu entries agree after closing and restoring through the submenu.");
                await ((MainWindowViewModel)restarted.DataContext).FlushDisplaySettingsAsync();
            }
            finally { restarted.Close(); await Settle(window); }
            Check(!vm.HasUnsavedChanges && !vm.HasDocument, "Closing/showing panels never edits or loads a document.");
            Check(window.AvalonMainToolbar is { CanHide: false, CanClose: false } &&
                window.AvalonPageToolbar is { CanHide: false, CanClose: false },
                "Both real toolbars are non-closeable; their existing button checklist remains the supported visibility control.");
            Check(window.Title.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.Version) &&
                PdfCorrectorium.Core.ApplicationBuildInfo.AboutText.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.NumericVersion),
                "Title and About use this build's product and numeric versions.");
            SaveIconDiagnosticImage((FrameworkElement)window.Content, Path.Combine(directory, "restored-panels.png"));
            await VerifyAutoHidePresentationAsync(window, directory, Check);
            await VerifySideAutoHideAsync(directory, Check);
            await vm.FlushDisplaySettingsAsync();
            Shutdown(0);
        }
        catch (Exception error)
        {
            if (directory is not null) File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString());
            _diagnostics?.Write("pane-visibility-test.failed", error.ToString());
            Shutdown(1);
        }
    }
}
