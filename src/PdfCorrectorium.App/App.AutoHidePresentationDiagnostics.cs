using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using AvalonDock.Controls;
using AvalonDock.Layout;
using PdfCorrectorium.App.Resources;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyAutoHidePresentationAsync(MainWindow main, string output, Action<bool, string> check)
    {
        var ids = new[] { "navigation", "bookmarks", "properties", "link-properties", "redaction-properties", "comment-properties", "read-aloud" };
        var keys = new[] { "Thumbnail", "Bookmark", "OcrEdit", "Link", "Redaction", "Comments", "ReadAloud" };
        static LayoutAnchorable Pane(MainWindow host, string id) => id switch
        {
            "navigation" => host.AvalonNavigation!, "bookmarks" => host.AvalonBookmarks!, _ => host.PropertyPanes[id],
        };
        static async Task Settle(MainWindow host)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        async Task<LayoutAnchorableControl> Open(MainWindow host, LayoutAnchorable pane)
        {
            var manager = host.AvalonDockPreviewManager!;
            var anchor = IconVisuals(manager).OfType<LayoutAnchorControl>().Single(item => ReferenceEquals(item.Model, pane));
            anchor.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.MouseDownEvent });
            await Settle(host);
            var flyout = IconVisuals(manager).OfType<LayoutAutoHideWindowControl>()
                .Single(item => item.IsVisible && ReferenceEquals(item.Model, pane));
            var content = (LayoutAnchorableControl)typeof(LayoutAutoHideWindowControl)
                .GetField("_internalHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flyout)!;
            check(Math.Abs(content.ActualWidth - pane.AutoHideWidth) < 2,
                pane.ContentId + ": actual native flyout content uses inherited width " + pane.AutoHideWidth + ", actual " + content.ActualWidth + ".");
            return content;
        }
        void Picture(FrameworkElement view, string name, double dpi = 96)
        {
            var scale = dpi / 96;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * scale), (int)Math.Ceiling(view.ActualHeight * scale), dpi, dpi, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(view), null, new Rect(view.RenderSize));
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        var vm = (MainWindowViewModel)main.DataContext;
        foreach (var id in ids)
        {
            var pane = Pane(main, id);
            main.SetPaneAutoHide(pane, false); await Settle(main);
            if (id is "navigation" or "bookmarks")
            {
                ((LayoutAnchorablePane)pane.Parent).Children.Remove(pane);
                var workspace = main.AvalonDockPreviewManager!.Layout.RootPanel.Children.OfType<LayoutPanel>()
                    .Single(item => item.Descendents().OfType<LayoutDocument>().Any(document => document.ContentId == "editor"));
                workspace.Children.Insert(0, new LayoutAnchorablePane(pane));
            }
            ((LayoutAnchorablePane)pane.Parent).DockWidth = new GridLength(id is "navigation" or "bookmarks" ? 280 : 360);
            await Settle(main);
            var content = pane.Content;
            var nativeItem = (LayoutAnchorableItem)main.AvalonDockPreviewManager!.GetLayoutItemFromModel(pane);
            check(nativeItem.AutoHideCommand.CanExecute(null), id + ": the real pin command is available.");
            nativeItem.AutoHideCommand.Execute(null); await Settle(main);
            check(pane.IsAutoHidden && pane.AutoHideWidth == (id is "navigation" or "bookmarks" ? 280 : 360) && ReferenceEquals(content, pane.Content),
                id + ": native pin preserves docked width and the existing view.");
            await Open(main, pane);
            var resizedWidth = pane.AutoHideWidth + 20;
            pane.AutoHideWidth = resizedWidth;
            ((LayoutAnchorableItem)main.AvalonDockPreviewManager.GetLayoutItemFromModel(pane)).HideCommand.Execute(null);
            pane.Show(); await Settle(main);
            check(pane.IsAutoHidden && pane.AutoHideWidth == resizedWidth,
                id + ": X-hide/show keeps a resized flyout width instead of reusing a stale fixed width.");
            await Open(main, pane);
        }
        check(main.AvalonDockPreviewManager!.Layout.LeftSide.Descendents().OfType<LayoutAnchorable>().Count() == 2 &&
            main.AvalonDockPreviewManager.Layout.RightSide.Descendents().OfType<LayoutAnchorable>().Count() == 5,
            "The actual presentation exercises both left navigation tabs and right property/speech tabs.");
        foreach (var mode in new[] { ApplicationThemeMode.Light, ApplicationThemeMode.Dark })
        {
            ApplicationThemeService.Apply(mode); await Settle(main);
            Picture((FrameworkElement)main.Content, "auto-hide-tabs-" + mode);
            for (var index = 0; index < ids.Length; index++)
            {
                var anchor = IconVisuals(main.AvalonDockPreviewManager!).OfType<LayoutAnchorControl>().Single(item => ReferenceEquals(item.Model, Pane(main, ids[index])));
                var chrome = (Border)anchor.Template.FindName("Chrome", anchor);
                var icon = (ActionIcon)anchor.Template.FindName("PaneIcon", anchor);
                var label = (TextBlock)anchor.Template.FindName("PaneLabel", anchor);
                check(Math.Abs(chrome.ActualHeight - 32) < .01 && Math.Abs(icon.ActualWidth - 20) < .01 && Math.Abs(icon.ActualHeight - 20) < .01 && icon.Key == keys[index] && label.FontSize == 12 && label.Text == Pane(main, ids[index]).Title,
                    ids[index] + ": " + mode + " tab has a 32-unit target, shared icon and readable title " +
                    $"(height={chrome.ActualHeight}, icon={icon.ActualWidth}x{icon.ActualHeight}/{icon.Key}, title={label.Text}, font={label.FontSize}).");
                var transform = icon.TransformToAncestor(main.AvalonDockPreviewManager!);
                var axis = transform.Transform(new Point(1, 0)) - transform.Transform(new Point());
                check(Math.Abs(axis.Y) < .01 && axis.X > .99, ids[index] + ": side-tab icon stays upright after native rotation.");
                check(icon.Foreground is SolidColorBrush brush && brush.Color == ((SolidColorBrush)Current.Resources["BodyTextBrush"]).Color,
                    ids[index] + ": side-tab icon follows the application's " + mode + " palette.");
            }
            Picture((FrameworkElement)main.Content, "auto-hide-tabs-" + mode + "-144dpi", 144);
            Picture(await Open(main, Pane(main, "bookmarks")), "auto-hide-bookmarks-" + mode);
            Picture(await Open(main, Pane(main, "properties")), "auto-hide-properties-" + mode);
        }
        ApplicationThemeService.Apply(ApplicationThemeMode.Light);
        var legacy = XDocument.Parse(main.CaptureNativeWorkspace());
        foreach (var node in legacy.Descendants().Where(node => ids.Contains((string?)node.Attribute("ContentId"))))
        {
            node.SetAttributeValue("AutoHideWidth", (string?)node.Attribute("ContentId") is "navigation" or "properties" ? 0 : 100);
            node.SetAttributeValue("AutoHideMinWidth", 100);
        }
        check(main.RestoreNativeWorkspace(legacy.ToString()), "A legacy narrow auto-hide workspace can be restored.");
        await Settle(main);
        foreach (var id in ids)
        {
            var pane = Pane(main, id);
            var expected = id is "navigation" or "bookmarks" ? Math.Max(200, vm.CurrentApplicationSettings.PageListWidth)
                : Math.Max(240, vm.CurrentApplicationSettings.PropertiesPanelWidth);
            check(pane.IsAutoHidden && pane.AutoHideWidth == expected, id + ": legacy 100-unit width is repaired using the saved panel preference.");
            await Open(main, pane);
            pane.AutoHideWidth = id is "navigation" or "bookmarks" ? 310 : 410;
        }
        await Settle(main);
        // Width changes in the native host must be persisted even without manager mouse events.
        await vm.FlushDisplaySettingsAsync();
        var saved = vm.CurrentApplicationSettings;
        var savedXml = XDocument.Parse(saved.WorkspaceLayoutXml!);
        check(ids.All(id => (double?)savedXml.Descendants().Single(node => (string?)node.Attribute("ContentId") == id).Attribute("AutoHideWidth") ==
            (id is "navigation" or "bookmarks" ? 310 : 410)), "Flyout width changes reach persisted XML through model notifications.");
        var restart = Path.Combine(output, "auto-hide-restart");
        var paths = new ApplicationPaths(StorageMode.Portable, Path.Combine(restart, ".appstate", "config"),
            Path.Combine(restart, ".appstate", "logs"), Path.Combine(restart, ".appstate", "cache"), Path.Combine(restart, ".appstate", "workspaces"));
        ApplicationPathResolver.EnsureDirectories(paths); await new ApplicationSettingsService(paths).SaveAsync(saved);
        var reopened = new MainWindow(diagnosticDirectory: restart) { Left = -20000, Top = -20000, ShowActivated = false,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, ClosePromptOverride = () => MessageBoxResult.No };
        try
        {
            reopened.Show(); await Settle(reopened);
            foreach (var id in ids)
            {
                var pane = Pane(reopened, id);
                check(pane.IsAutoHidden && pane.AutoHideWidth == (id is "navigation" or "bookmarks" ? 310 : 410),
                    id + ": restart retains a valid custom flyout width.");
                await Open(reopened, pane);
            }
            var bookmark = Pane(reopened, "bookmarks"); reopened.SetPaneAutoHide(bookmark, false); await Settle(reopened);
            ((LayoutAnchorablePane)bookmark.Parent).DockWidth = new GridLength(330); await Settle(reopened);
            reopened.SetPaneAutoHide(bookmark, true); await Settle(reopened);
            check(bookmark.AutoHideWidth == 330, "Menu auto-hide inherits the latest fixed width after a pin/unpin round trip.");
            await Open(reopened, bookmark);
        }
        finally { reopened.Close(); await Settle(main); }
    }
}
