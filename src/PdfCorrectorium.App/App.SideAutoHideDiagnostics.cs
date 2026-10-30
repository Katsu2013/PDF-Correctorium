using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using AvalonDock.Controls;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifySideAutoHideAsync(string output, Action<bool, string> check)
    {
        var directory = Path.Combine(output, "side-cohort"); var fake = new DiagnosticSpeech();
        var host = new MainWindow(diagnosticDirectory: directory) { Left = -20000, Top = -20000, ShowActivated = false,
            ShowInTaskbar = false, ClosePromptOverride = () => MessageBoxResult.No, ReadAloudPlaybackOverride = fake };
        static LayoutAnchorable Pane(MainWindow main, string id) => id switch
        { "navigation" => main.AvalonNavigation!, "bookmarks" => main.AvalonBookmarks!, _ => main.PropertyPanes[id] };
        static async Task Settle(MainWindow main)
        { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        static void Pin(MainWindow main, LayoutAnchorable pane) =>
            ((LayoutAnchorableItem)main.AvalonDockPreviewManager!.GetLayoutItemFromModel(pane)).AutoHideCommand.Execute(null);
        var rightIds = new[] { "properties", "link-properties", "redaction-properties", "comment-properties", "read-aloud" };
        var leftIds = new[] { "navigation", "bookmarks" };
        try
        {
            host.Show(); await Settle(host); var vm = (MainWindowViewModel)host.DataContext;
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
            var pdf = Path.Combine(directory, "cohort.pdf"); WriteDocumentUiTestPdf(pdf);
            var packages = new ProjectPackageService(); var g = new TextGeometry { LocalBounds = new(new(20, 300), new(200, 20)), RotationCenter = new(120, 310) };
            var row = new OcrTextRegion { OriginalText = "パネルの読み上げ確認です。", OriginalGeometry = g, EditedGeometry = g };
            var project = Path.Combine(directory, "cohort.pdfocrproj");
            await packages.SaveAsync(project, new() { SourcePdf = await packages.CreateSourceReferenceAsync(pdf), Pages = [
                new() { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [row], ReadingOrder = [row.Id] },
                new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400 }] });
            await vm.LoadProjectForDiagnosticsAsync(project); await vm.EnsureDocumentMetadataAsync();
            var manager = host.AvalonDockPreviewManager!;
            var workspace = manager.Layout.RootPanel.Children.OfType<LayoutPanel>()
                .Single(item => item.Descendents().OfType<LayoutDocument>().Any(document => document.ContentId == "editor"));
            var left = new LayoutAnchorablePaneGroup { Orientation = Orientation.Vertical, DockWidth = new GridLength(240) };
            var right = new LayoutAnchorablePaneGroup { Orientation = Orientation.Vertical, DockWidth = new GridLength(340) };
            workspace.Children.Insert(0, left); workspace.Children.Add(right);
            foreach (var id in leftIds.Concat(rightIds.Where(id => id != "read-aloud")))
            {
                var pane = Pane(host, id); ((LayoutAnchorablePane)pane.Parent).Children.Remove(pane);
                var group = new LayoutAnchorablePane(pane) { DockWidth = new GridLength(leftIds.Contains(id) ? 240 : 340) };
                (leftIds.Contains(id) ? left : right).Children.Add(group);
            }
            manager.Layout.CollectGarbage(); await Settle(host);
            check(right.Children.Count == 4 && left.Children.Count == 2, "The fixture uses separate stacked dock groups on both sides, not only one tab group.");
            var contents = leftIds.Concat(rightIds).ToDictionary(id => id, id => Pane(host, id).Content);
            check(Pane(host, "read-aloud").IsHidden && host.DockedReadAloudPanel is null, "A late-opened reader starts hidden and uninitialized.");
            Pin(host, Pane(host, "properties")); await Settle(host);
            check(rightIds.Where(id => id != "read-aloud").All(id => Pane(host, id).IsAutoHidden) && leftIds.All(id => !Pane(host, id).IsAutoHidden),
                "One native right-side pin auto-hides every visible right group and preserves the left side.");
            check(Pane(host, "read-aloud").IsHidden && host.DockedReadAloudPanel is null, "Bulk auto-hide never reveals hidden panels or initializes speech early.");
            Invoke((Button)host.FindName("ReadAloudToolbarButton")); await Settle(host);
            check(rightIds.All(id => Pane(host, id).IsAutoHidden), "The actual headphone launcher puts the newly opened reader into the right auto-hide cohort.");
            check(host.DockedReadAloudPanel is { IsVisible: true } && Pane(host, "read-aloud").IsActive,
                "The newly created native reader flyout actually opens instead of leaving only an inactive side tab.");
            var reader = host.DockedReadAloudPanel;
            Pin(host, Pane(host, "properties")); await Settle(host);
            check(!Pane(host, "properties").IsAutoHidden && rightIds.Where(id => id != "properties").All(id => Pane(host, id).IsAutoHidden),
                "Pinning a right flyout fixes only the selected panel; all its siblings remain side icons.");
            Pane(host, "read-aloud").Hide(); await Settle(host);
            Invoke((Button)host.FindName("ReadAloudToolbarButton")); await Settle(host);
            check(Pane(host, "read-aloud").IsAutoHidden && !Pane(host, "properties").IsAutoHidden && ReferenceEquals(reader, host.DockedReadAloudPanel),
                "A later reader reveal follows the side policy without undoing another panel's individual pin or recreating playback.");
            host.SetPaneAutoHide(Pane(host, "comment-properties"), true); await Settle(host);
            check(rightIds.All(id => Pane(host, id).IsAutoHidden), "The View-menu path also auto-hides the whole right side and clears previous individual pins.");
            host.SetPaneAutoHide(Pane(host, "link-properties"), false); host.SetPaneAutoHide(Pane(host, "comment-properties"), false); await Settle(host);
            var link = Pane(host, "link-properties"); var comment = Pane(host, "comment-properties");
            ((LayoutAnchorablePane)link.Parent).Children.Remove(link);
            ((LayoutAnchorablePane)comment.Parent).Children.Add(link); await Settle(host);
            Pin(host, comment); await Settle(host);
            check(comment.IsAutoHidden && link.IsAutoHidden && ReferenceEquals(comment.Parent, link.Parent),
                "The fixture also uses a genuine shared auto-hide tab group, not only separate groups.");
            Pin(host, link); await Settle(host);
            check(!link.IsAutoHidden && comment.IsAutoHidden && rightIds.Where(id => id != "link-properties").All(id => Pane(host, id).IsAutoHidden),
                "The native pin fixes only its selected item even when AvalonDock originally grouped several auto-hide tabs together.");
            host.SetPaneAutoHide(comment, true); await Settle(host);
            Pin(host, Pane(host, "bookmarks")); await Settle(host);
            check(leftIds.All(id => Pane(host, id).IsAutoHidden) && rightIds.All(id => Pane(host, id).IsAutoHidden),
                "The left native pin acts on both left groups while preserving the right cohort.");
            host.SetPaneAutoHide(Pane(host, "navigation"), false); host.SetPaneAutoHide(Pane(host, "bookmarks"), false); await Settle(host);
            var nav = Pane(host, "navigation"); var bookmarks = Pane(host, "bookmarks");
            ((LayoutAnchorablePane)bookmarks.Parent).Children.Remove(bookmarks);
            ((LayoutAnchorablePane)nav.Parent).Children.Add(bookmarks); await Settle(host);
            Pin(host, bookmarks); await Settle(host);
            check(nav.IsAutoHidden && bookmarks.IsAutoHidden && ReferenceEquals(nav.Parent, bookmarks.Parent),
                "The optional double-click route also starts with multiple tabs in the same auto-hide group.");
            var navigationAnchor = IconVisuals(manager).OfType<LayoutAnchorControl>().Single(anchor => ReferenceEquals(anchor.Model, Pane(host, "navigation")));
            var doubleClickDock = manager.AllowAnchorDoubleClickDock;
            manager.AllowAnchorDoubleClickDock = true; // Exercise the optional native route without changing the app default.
            var chrome = (Border)navigationAnchor.Template.FindName("Chrome", navigationAnchor);
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left);
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(click, 2);
            click.RoutedEvent = Mouse.PreviewMouseDownEvent; chrome.RaiseEvent(click);
            click.RoutedEvent = Mouse.MouseDownEvent; chrome.RaiseEvent(click);
            await Settle(host);
            manager.AllowAnchorDoubleClickDock = doubleClickDock;
            check(!Pane(host, "navigation").IsAutoHidden && Pane(host, "bookmarks").IsAutoHidden,
                "An enabled native side-tab double-click fixes only the chosen left panel without re-toggling it or pinning its neighbor. " +
                $"navigationAuto={Pane(host, "navigation").IsAutoHidden}, bookmarksAuto={Pane(host, "bookmarks").IsAutoHidden}, clickCount={click.ClickCount}, handled={click.Handled}.");
            SaveIconDiagnosticImage((FrameworkElement)host.Content, Path.Combine(directory, "individual-fixed-and-side-icons.png"));
            check(contents.All(pair => ReferenceEquals(pair.Value, Pane(host, pair.Key).Content)) && !vm.HasUnsavedChanges,
                "Cohort operations reuse every original view and never edit the document.");
            var xml = host.CaptureNativeWorkspace(); var root = XElement.Parse(xml);
            check((bool?)root.Attribute("PdfCorrectoriumAutoHideLeft") == true && (bool?)root.Attribute("PdfCorrectoriumAutoHideRight") == true &&
                (string?)root.Attribute("PdfCorrectoriumFixedLeft") == "navigation", "Serialized layout records independent side modes and explicit fixed-panel exceptions.");
            var bad = XElement.Parse(xml); bad.SetAttributeValue("PdfCorrectoriumFixedRight", "main-toolbar");
            check(WorkspaceDockLayout.Normalize(bad.ToString()) is null, "Layout validation rejects fixed exceptions for toolbars or the document.");
            check(host.RestoreNativeWorkspace(xml), "Native AvalonDock deserialization accepts app-owned side metadata."); await Settle(host);
            check(!Pane(host, "navigation").IsAutoHidden && Pane(host, "bookmarks").IsAutoHidden && rightIds.All(id => Pane(host, id).IsAutoHidden),
                "Layout restore retains an individual fixed panel and all remaining side icons.");
            host.SetPaneAutoHide(Pane(host, "bookmarks"), true); await Settle(host);
            foreach (var id in rightIds) Pane(host, id).Hide();
            await Settle(host); await vm.FlushDisplaySettingsAsync();
            check(rightIds.All(id => Pane(host, id).IsHidden), "X-hidden right panels remain hidden instead of being reopened by cohort synchronization.");
            var saved = vm.CurrentApplicationSettings;
            var restartDirectory = Path.Combine(directory, "restart");
            var paths = new ApplicationPaths(StorageMode.Portable, Path.Combine(restartDirectory, ".appstate", "config"),
                Path.Combine(restartDirectory, ".appstate", "logs"), Path.Combine(restartDirectory, ".appstate", "cache"), Path.Combine(restartDirectory, ".appstate", "workspaces"));
            ApplicationPathResolver.EnsureDirectories(paths); await new ApplicationSettingsService(paths).SaveAsync(saved);
            var reopened = new MainWindow(diagnosticDirectory: restartDirectory) { Left = -20000, Top = -20000,
                ShowActivated = false, ShowInTaskbar = false, ClosePromptOverride = () => MessageBoxResult.No, ReadAloudPlaybackOverride = new DiagnosticSpeech() };
            try
            {
                reopened.Show(); await Settle(reopened);
                check(rightIds.All(id => Pane(reopened, id).IsHidden), "Restart preserves fully hidden cohorts without forcing their views open.");
                var nextVm = (MainWindowViewModel)reopened.DataContext; await nextVm.LoadProjectForDiagnosticsAsync(project); await nextVm.EnsureDocumentMetadataAsync();
                Invoke((Button)reopened.FindName("ReadAloudToolbarButton")); await Settle(reopened);
                check(Pane(reopened, "read-aloud").IsAutoHidden && rightIds.Where(id => !Pane(reopened, id).IsHidden).All(id => Pane(reopened, id).IsAutoHidden),
                    "Even when every right panel was X-hidden before restart, newly opened panels remember the right auto-hide policy.");
                check(reopened.AvalonMainToolbar is { IsAutoHidden: false, CanAutoHide: false } && reopened.AvalonPageToolbar is { IsAutoHidden: false, CanAutoHide: false },
                    "Both actual toolbars remain outside the side-panel policy.");
            }
            finally { reopened.Close(); await Settle(host); }
        }
        finally { host.Close(); }
    }
}
