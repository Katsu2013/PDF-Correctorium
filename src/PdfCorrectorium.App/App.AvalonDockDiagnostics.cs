using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvalonDock.Controls;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App;

public partial class App
{
    private async void RunAvalonDockPreviewTest(MainWindow window, string[] arguments, int optionIndex)
    {
        string? directory = null;
        try
        {
            if (arguments.Length <= optionIndex + 1)
                throw new ArgumentException("--avalondock-test requires a new output directory.");
            directory = Path.GetFullPath(arguments[optionIndex + 1]);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory)
                    .Any(path => !string.Equals(Path.GetFileName(path), ".appstate", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Use a new output directory to avoid overwriting test results.");
            Directory.CreateDirectory(directory);
            window.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var manager = window.AvalonDockPreviewManager ??
                throw new InvalidOperationException("AvalonDock was not initialized.");
            var navigation = window.AvalonNavigation ??
                throw new InvalidOperationException("Navigation anchorable is missing.");
            var bookmarks = window.AvalonBookmarks ??
                throw new InvalidOperationException("Bookmarks anchorable is missing.");
            var properties = window.AvalonProperties ??
                throw new InvalidOperationException("Properties anchorable is missing.");
            var mainToolbar = window.AvalonMainToolbar ??
                throw new InvalidOperationException("Main toolbar anchorable is missing.");
            var pageToolbar = window.AvalonPageToolbar ??
                throw new InvalidOperationException("Page toolbar anchorable is missing.");
            if (manager.Layout?.RootPanel is null || manager.ActualWidth <= 0 || manager.ActualHeight <= 0)
                throw new InvalidOperationException("AvalonDock has no visible layout.");
            var layoutLines = new List<string>();
            void DescribeLayout(DependencyObject visual, List<string> lines)
            {
                if (visual is LayoutPanelControl panelControl)
                    lines.Add($"LayoutPanelControl model={panelControl.Model?.GetType().Name} " +
                        $"orientation={(panelControl.Model as LayoutPanel)?.Orientation} " +
                        $"dockHeight={(panelControl.Model as LayoutPanel)?.DockHeight} " +
                        $"actual={panelControl.ActualWidth:F1}x{panelControl.ActualHeight:F1} " +
                        $"y={panelControl.TranslatePoint(new Point(), window).Y:F1} " +
                        $"rows={string.Join(',', panelControl.RowDefinitions.Select(row => $"{row.Height}:{row.ActualHeight:F1}"))}");
                if (visual is LayoutGridResizerControl or LayoutAnchorablePaneControl or LayoutDocumentPaneControl)
                {
                    var parent = VisualTreeHelper.GetParent(visual);
                    var element = visual as FrameworkElement;
                    lines.Add($"{visual.GetType().Name} parent={parent?.GetType().Name} " +
                        $"row={Grid.GetRow((UIElement)visual)} col={Grid.GetColumn((UIElement)visual)} " +
                        $"size={element?.ActualWidth:F1}x{element?.ActualHeight:F1} " +
                        $"y={element?.TranslatePoint(new Point(), window).Y:F1} " +
                        $"items={((visual as LayoutAnchorablePaneControl)?.Model as LayoutAnchorablePane)?.Children.Count} " +
                        $"ids={string.Join(',', ((visual as LayoutAnchorablePaneControl)?.Model as LayoutAnchorablePane)?.Children.Select(item => item.ContentId) ?? [])}");
                }
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
                    DescribeLayout(VisualTreeHelper.GetChild(visual, index), lines);
            }
            DescribeLayout(manager, layoutLines);
            File.WriteAllLines(Path.Combine(directory, "layout-tree.txt"), layoutLines);
            if (!ReferenceEquals(navigation.Content, window.FindName("NavigationDockPane")) ||
                !ReferenceEquals(bookmarks.Content, window.FindName("BookmarkDockPane")) ||
                !ReferenceEquals(properties.Content, window.FindName("PropertiesDockPane")))
                throw new InvalidOperationException("The real page, bookmark or properties pane was not reused.");
            if (!navigation.CanFloat || !bookmarks.CanFloat || !properties.CanFloat)
                throw new InvalidOperationException("Dock panes must support native floating drag.");
            if (!mainToolbar.CanFloat || !pageToolbar.CanFloat ||
                mainToolbar.Content is not ToolBarTray mainTray ||
                pageToolbar.Content is not ToolBarTray pageTray ||
                !mainTray.ToolBars.Contains((ToolBar)window.FindName("MainToolbarPanel")!) ||
                !pageTray.ToolBars.Contains((ToolBar)window.FindName("PageLayoutToolBar")!))
                throw new InvalidOperationException("The real toolbars are not hosted by movable AvalonDock panes.");
            if (manager.Layout.RootPanel.Orientation != Orientation.Vertical ||
                mainToolbar.Parent is not LayoutAnchorablePane initialMainPane ||
                pageToolbar.Parent is not LayoutAnchorablePane initialPagePane ||
                !ReferenceEquals(initialMainPane.Parent, initialPagePane.Parent) ||
                initialMainPane.Parent is not LayoutPanel { Orientation: Orientation.Horizontal } topBand ||
                !ReferenceEquals(topBand.Parent, manager.Layout.RootPanel) ||
                window.GetAvalonDockSide(initialMainPane) != AnchorSide.Top ||
                window.GetAvalonDockSide(initialPagePane) != AnchorSide.Top ||
                window.GetAvalonDockSide(navigation.Parent as LayoutAnchorablePane) != AnchorSide.Left ||
                window.GetAvalonDockSide(properties.Parent as LayoutAnchorablePane) != AnchorSide.Right ||
                mainTray.Orientation != Orientation.Horizontal || pageTray.Orientation != Orientation.Horizontal)
                throw new InvalidOperationException("Initial full-width toolbar row and left/editor/right workspace are missing.");
            var initialMainControl = FindAvalonPaneControl(window, initialMainPane)!;
            var initialPageControl = FindAvalonPaneControl(window, initialPagePane)!;
            var initialDocumentControl = FindAvalonDocumentPaneControl(window)!;
            if (initialMainControl.ActualWidth < 100 || initialPageControl.ActualWidth < 100 ||
                Math.Abs(initialMainControl.TranslatePoint(new Point(), window).Y -
                    initialPageControl.TranslatePoint(new Point(), window).Y) > 2 ||
                initialDocumentControl.TranslatePoint(new Point(), window).Y <
                    initialMainControl.TranslatePoint(new Point(), window).Y + initialMainControl.ActualHeight)
                throw new InvalidOperationException("Top toolbars are not side by side above the editor.");
            var initialContent = (FrameworkElement)window.Content;
            var initialBitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(initialContent.ActualWidth)),
                Math.Max(1, (int)Math.Ceiling(initialContent.ActualHeight)),
                96, 96, PixelFormats.Pbgra32);
            initialBitmap.Render(initialContent);
            var initialEncoder = new PngBitmapEncoder();
            initialEncoder.Frames.Add(BitmapFrame.Create(initialBitmap));
            using (var stream = File.Create(Path.Combine(directory, "initial-layout.png")))
                initialEncoder.Save(stream);
            var viewModel = (MainWindowViewModel)window.DataContext;
            viewModel.SetWorkspaceDockPositions(WorkspaceDockPosition.Left,
                WorkspaceDockPosition.Left, WorkspacePaneGroupMode.Tabbed);
            viewModel.SetBookmarkDockPosition(WorkspaceDockPosition.Left);
            viewModel.SetToolbarLayout(ToolbarDockSide.Left, 0, 0,
                ToolbarDockSide.Top, 0, 0);
            window.SynchronizeAvalonDockLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            if (mainTray.Orientation != Orientation.Vertical || pageTray.Orientation != Orientation.Horizontal)
                throw new InvalidOperationException("Docked toolbar orientation does not match left/top placement.");
            foreach (var toolbarId in new[] { "main-toolbar", "page-toolbar" })
            {
                var toolbar = toolbarId == "main-toolbar" ? mainToolbar : pageToolbar;
                var toolbarPane = FindAvalonPaneControl(window, toolbar.Parent as LayoutAnchorablePane) ??
                    throw new InvalidOperationException($"The {toolbarId} pane was not rendered.");
                if (toolbarPane.Template != window.FindResource("ToolbarAvalonAnchorablePaneTemplate"))
                    throw new InvalidOperationException($"The {toolbarId} pane still reserves a title/tab row.");
                var dragTitle = toolbarPane.Template.FindName("ToolbarDragTitle", toolbarPane) as FrameworkElement ??
                    throw new InvalidOperationException($"The {toolbarId} drag title is missing.");
                var origin = dragTitle.TransformToAncestor(window).Transform(new Point());
                File.AppendAllText(Path.Combine(directory, "layout.txt"),
                    $"{toolbarId}: title={origin.X:F0},{origin.Y:F0} {dragTitle.ActualWidth:F0}x{dragTitle.ActualHeight:F0}; " +
                    $"pane={toolbarPane.ActualWidth:F0}x{toolbarPane.ActualHeight:F0}; " +
                    $"tray={(toolbar.Content as ToolBarTray)?.DesiredSize.Width:F0}x" +
                    $"{(toolbar.Content as ToolBarTray)?.DesiredSize.Height:F0}\n");
                CheckToolbarFitsWithoutFrame(window, toolbar);
            }
            var movedLayout = new List<string>();
            DescribeLayout(manager, movedLayout);
            File.WriteAllLines(Path.Combine(directory, "moved-layout-tree.txt"), movedLayout);
            if (!FindVisiblePanelSplitter(window))
                throw new InvalidOperationException("Removing toolbar splitters also removed panel resizing.");

            var navigationPane = navigation.Parent as LayoutAnchorablePane ??
                throw new InvalidOperationException("Navigation is not in an anchorable pane.");
            if (!ReferenceEquals(navigationPane, bookmarks.Parent) ||
                !ReferenceEquals(navigationPane, properties.Parent) || navigationPane.Children.Count != 3)
                throw new InvalidOperationException("The preview test did not start with all three panes tabbed together.");
            var paneControl = FindAvalonPaneControl(window) ??
                throw new InvalidOperationException("The tabbed anchorable control was not rendered.");
            var header = paneControl.Template.FindName("HeaderPanel", paneControl) as FrameworkElement;
            var paneContent = paneControl.Template.FindName("ContentPanel", paneControl) as FrameworkElement;
            if (paneControl.Items.Count != 3 || paneControl.TabStripPlacement != Dock.Top ||
                header is null || paneContent is null ||
                Grid.GetRow(header) != 0 || Grid.GetRow(paneContent) != 1)
                throw new InvalidOperationException("Anchorable tabs are not above their pane content.");

            var samplePdf = Path.Combine(directory, "two-pages.pdf");
            WriteDocumentUiTestPdf(samplePdf);
            await viewModel.LoadPdfForDiagnosticsAsync(samplePdf);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (viewModel.PageItems.Count != 2 || !viewModel.HasPreview)
                throw new InvalidOperationException("PDF preview did not load inside AvalonDock.");

            var content = (FrameworkElement)window.Content;
            var width = Math.Max(1, (int)Math.Ceiling(content.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(content.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(directory, "avalondock-preview.png")))
                encoder.Save(stream);
            mainToolbar.Float();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!mainToolbar.IsFloating || mainTray.Orientation != Orientation.Horizontal)
                throw new InvalidOperationException($"Floating a vertical toolbar did not produce a horizontal toolbar (floating={mainToolbar.IsFloating}, orientation={mainTray.Orientation}).");
            CheckFloatingToolbarSize(mainToolbar, mainTray);
            var floatingToolbarContent = Application.Current.Windows.Cast<Window>()
                .Select(floating => FindAvalonToolbarContent(floating, "main-toolbar"))
                .FirstOrDefault(content => content is not null);
            if (floatingToolbarContent is not null &&
                floatingToolbarContent.Template != window.FindResource("AvalonToolbarContentTemplate"))
                throw new InvalidOperationException("The floating toolbar regained the tall panel title.");
            mainToolbar.Dock();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            viewModel.SetToolbarLayout(ToolbarDockSide.Left, 0, 0,
                ToolbarDockSide.Top, 0, 0);
            window.SynchronizeAvalonDockLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (mainToolbar.IsFloating || mainTray.Orientation != Orientation.Vertical)
                throw new InvalidOperationException("Re-docking the toolbar did not restore its vertical layout: " +
                    $"floating={mainToolbar.IsFloating}, orientation={mainTray.Orientation}, " +
                    $"side={window.GetAvalonDockSide(mainToolbar.Parent as LayoutAnchorablePane)}, " +
                    $"parent={(mainToolbar.Parent as LayoutAnchorablePane)?.Parent?.GetType().Name}.");
            viewModel.SetToolbarLayout(ToolbarDockSide.Right, 0, 0,
                ToolbarDockSide.Bottom, 0, 0);
            window.SynchronizeAvalonDockLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            if (window.GetAvalonDockSide(mainToolbar.Parent as LayoutAnchorablePane) != AnchorSide.Right ||
                window.GetAvalonDockSide(pageToolbar.Parent as LayoutAnchorablePane) != AnchorSide.Bottom ||
                mainTray.Orientation != Orientation.Vertical || pageTray.Orientation != Orientation.Horizontal)
                throw new InvalidOperationException($"Toolbar positions from settings were not applied to AvalonDock: " +
                    $"main={(mainToolbar.Parent as LayoutAnchorablePane)?.GetSide()}, " +
                    $"page={(pageToolbar.Parent as LayoutAnchorablePane)?.GetSide()}, " +
                    $"orientations={mainTray.Orientation}/{pageTray.Orientation}, " +
                    $"settings={viewModel.CurrentApplicationSettings.MainToolbarDockSide}/" +
                    $"{viewModel.CurrentApplicationSettings.PageToolbarDockSide}.");
            foreach (var toolbarId in new[] { "main-toolbar", "page-toolbar" })
                CheckToolbarFitsWithoutFrame(window, toolbarId == "main-toolbar" ? mainToolbar : pageToolbar);
            viewModel.SetToolbarLayout(ToolbarDockSide.Top, 0, 0,
                ToolbarDockSide.Top, 0, 0);
            window.SynchronizeAvalonDockLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (mainToolbar.Parent is not LayoutAnchorablePane restoredMain ||
                pageToolbar.Parent is not LayoutAnchorablePane restoredPage ||
                !ReferenceEquals(restoredMain.Parent, restoredPage.Parent) ||
                restoredMain.Parent is not LayoutPanel { Orientation: Orientation.Horizontal } restoredTop ||
                !ReferenceEquals(restoredTop.Parent, manager.Layout.RootPanel) ||
                restoredTop.Children.IndexOf(restoredMain) >= restoredTop.Children.IndexOf(restoredPage) ||
                mainTray.Orientation != Orientation.Horizontal || pageTray.Orientation != Orientation.Horizontal)
                throw new InvalidOperationException("Returning both toolbars to the top did not restore their horizontal row.");
            CheckToolbarFitsWithoutFrame(window, mainToolbar);
            CheckToolbarFitsWithoutFrame(window, pageToolbar);
            // AvalonDock may create a second top-level row for an edge drop. It must
            // be merged into the existing row, not retained with a resize divider.
            restoredTop.Children.Remove(restoredPage);
            var extraTopBand = new LayoutPanel { Orientation = Orientation.Horizontal,
                DockHeight = new GridLength(60) };
            extraTopBand.Children.Add(restoredPage);
            manager.Layout.RootPanel.Children.Insert(
                manager.Layout.RootPanel.Children.IndexOf(restoredTop) + 1, extraTopBand);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!ReferenceEquals((pageToolbar.Parent as LayoutAnchorablePane)?.Parent, restoredTop) ||
                manager.Layout.RootPanel.Children.Contains(extraTopBand))
                throw new InvalidOperationException("Docking beside a top toolbar retained a second row and divider " +
                    $"(pageParent={(pageToolbar.Parent as LayoutAnchorablePane)?.Parent?.GetType().Name}, " +
                    $"extraInRoot={manager.Layout.RootPanel.Children.Contains(extraTopBand)}, " +
                    $"extraChildren={extraTopBand.Children.Count}, " +
                    $"detectedSide={window.GetAvalonDockSide(pageToolbar.Parent as LayoutAnchorablePane)}).");
            CheckToolbarFitsWithoutFrame(window, pageToolbar);
            // Native center-edge drops can wrap the established top row together
            // with a new row inside a vertical root child.
            var topIndex = manager.Layout.RootPanel.Children.IndexOf(restoredTop);
            manager.Layout.RootPanel.Children.Remove(restoredTop);
            restoredTop.Children.Remove(restoredPage);
            var nestedSecondRow = new LayoutPanel { Orientation = Orientation.Horizontal,
                DockHeight = new GridLength(60) };
            nestedSecondRow.Children.Add(restoredPage);
            var stackedWrapper = new LayoutPanel { Orientation = Orientation.Vertical,
                DockHeight = new GridLength(120) };
            stackedWrapper.Children.Add(restoredTop);
            stackedWrapper.Children.Add(nestedSecondRow);
            manager.Layout.RootPanel.Children.Insert(topIndex, stackedWrapper);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!manager.Layout.RootPanel.Children.Contains(restoredTop) ||
                manager.Layout.RootPanel.Children.Contains(stackedWrapper) ||
                !ReferenceEquals((pageToolbar.Parent as LayoutAnchorablePane)?.Parent, restoredTop))
                throw new InvalidOperationException("A nested stacked toolbar row retained its wrapper or divider " +
                    $"(topInRoot={manager.Layout.RootPanel.Children.Contains(restoredTop)}, " +
                    $"wrapperInRoot={manager.Layout.RootPanel.Children.Contains(stackedWrapper)}, " +
                    $"pageParent={restoredPage.Parent?.GetType().Name}, " +
                    $"topParent={restoredTop.Parent?.GetType().Name}, " +
                    $"wrapperChildren={stackedWrapper.Children.Count}, " +
                    $"topChildren={restoredTop.Children.Count}, " +
                    $"secondChildren={nestedSecondRow.Children.Count}, " +
                    $"topIds={string.Join(',', restoredTop.Children.OfType<LayoutAnchorablePane>().SelectMany(pane => pane.Children.Select(item => item.ContentId)))}, " +
                    $"secondIds={string.Join(',', nestedSecondRow.Children.OfType<LayoutAnchorablePane>().SelectMany(pane => pane.Children.Select(item => item.ContentId)))}, " +
                    $"root={string.Join(',', manager.Layout.RootPanel.Children.Select(child => child.GetType().Name))}).");
            // A native drop below a toolbar can turn the shared band into two
            // stacked rows. Their splitter must disappear, and floating one row
            // must not leave its former height reserved above the editor.
            restoredTop.Orientation = Orientation.Vertical;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CheckToolbarFitsWithoutFrame(window, mainToolbar);
            CheckToolbarFitsWithoutFrame(window, pageToolbar);
            pageToolbar.Float();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var floatingLayout = new List<string>();
            static void DescribeModel(ILayoutElement element, int depth, List<string> lines)
            {
                lines.Add($"{new string(' ', depth * 2)}{element.GetType().Name} " +
                    $"id={(element as LayoutContent)?.ContentId} " +
                    $"children={(element as ILayoutContainer)?.ChildrenCount}");
                if (element is ILayoutContainer container)
                    foreach (var child in container.Children)
                        DescribeModel(child, depth + 1, lines);
            }
            DescribeModel(manager.Layout.RootPanel, 0, floatingLayout);
            File.WriteAllLines(Path.Combine(directory, "floating-layout.txt"), floatingLayout);
            var floatingVisual = new List<string>();
            DescribeLayout(manager, floatingVisual);
            File.WriteAllLines(Path.Combine(directory, "floating-visual.txt"), floatingVisual);
            var remainingTopControl = FindAvalonPaneControl(window, restoredMain);
            if (remainingTopControl is null ||
                Math.Abs(restoredTop.DockHeight.Value - mainTray.DesiredSize.Height) > 2 ||
                Math.Abs(remainingTopControl.ActualHeight - mainTray.DesiredSize.Height) > 2)
                throw new InvalidOperationException("Floating a stacked toolbar left a blank toolbar row.");
            CheckNoVisibleToolbarGap(window, mainToolbar, navigation);
            if (manager.Layout.RootPanel.Children.OfType<LayoutPanel>()
                .Any(panel => panel.Children.Count == 0 || panel.Children
                    .OfType<LayoutAnchorablePane>().Any(pane => pane.Children.Count == 0)))
                throw new InvalidOperationException("Floating a toolbar left an empty dock band in the editor.");
            CheckFloatingToolbarSize(pageToolbar, pageTray);
            pageToolbar.Dock();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            // Repeat the user's sequence: stack two toolbar rows, float the lower
            // toolbar, dock it, then stack and float it a second time.
            for (var cycle = 0; cycle < 2; cycle++)
            {
                if (pageToolbar.Parent is not LayoutAnchorablePane pagePane ||
                    mainToolbar.Parent is not LayoutAnchorablePane mainPane ||
                    !ReferenceEquals(pagePane.Parent, mainPane.Parent) ||
                    pagePane.Parent is not LayoutPanel repeatedBand)
                    throw new InvalidOperationException($"Toolbar rows could not be restacked in cycle {cycle + 1}.");
                repeatedBand.Orientation = Orientation.Vertical;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                pageToolbar.Float();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(180);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var remainingControl = FindAvalonPaneControl(window, mainToolbar.Parent as LayoutAnchorablePane);
                if (remainingControl is null ||
                    Math.Abs(repeatedBand.DockHeight.Value - mainTray.DesiredSize.Height) > 2 ||
                    Math.Abs(remainingControl.ActualHeight - mainTray.DesiredSize.Height) > 2)
                    throw new InvalidOperationException($"The second float left a blank toolbar row (cycle={cycle + 1}, band={repeatedBand.DockHeight}, remaining={remainingControl?.ActualHeight:F1}, tray={mainTray.DesiredSize.Height:F1}).");
                CheckNoVisibleToolbarGap(window, mainToolbar, navigation);
                CheckFloatingToolbarSize(pageToolbar, pageTray);
                pageToolbar.Dock();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(100);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            // Reproduce a native drop onto a regular pane: hidden toolbar tabs must
            // not swallow either ToolBar, even when both land in the same tab group.
            var mixedPane = navigation.Parent as LayoutAnchorablePane ??
                throw new InvalidOperationException("Navigation pane is missing for the mixed-drop test.");
            (mainToolbar.Parent as LayoutAnchorablePane)?.Children.Remove(mainToolbar);
            (pageToolbar.Parent as LayoutAnchorablePane)?.Children.Remove(pageToolbar);
            mixedPane.Children.Add(mainToolbar);
            mixedPane.Children.Add(pageToolbar);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (ReferenceEquals(mainToolbar.Parent, mixedPane) ||
                ReferenceEquals(pageToolbar.Parent, mixedPane) ||
                ReferenceEquals(mainToolbar.Parent, pageToolbar.Parent))
                throw new InvalidOperationException("A native tab drop left a toolbar hidden inside a regular pane.");
            var mixedLayout = new List<string>();
            DescribeLayout(manager, mixedLayout);
            foreach (var toolbar in new[] { mainToolbar, pageToolbar })
            {
                var pane = toolbar.Parent as LayoutAnchorablePane;
                var control = FindAvalonPaneControl(window, pane);
                var grid = control is null ? null : VisualTreeHelper.GetParent(control) as Grid;
                var column = control is null ? -1 : Grid.GetColumn(control);
                mixedLayout.Add($"{toolbar.ContentId}: side={window.GetAvalonDockSide(pane)} " +
                    $"modelWidth={pane?.DockWidth} min={pane?.DockMinWidth} " +
                    $"tray={(toolbar.Content as ToolBarTray)?.Orientation} " +
                    $"column={column} gridWidth={(grid is not null && column >= 0 && column < grid.ColumnDefinitions.Count ? grid.ColumnDefinitions[column].Width.ToString() : "?")} " +
                    $"actualCol={(grid is not null && column >= 0 && column < grid.ColumnDefinitions.Count ? grid.ColumnDefinitions[column].ActualWidth : -1)} " +
                    $"visible={control?.IsVisible} parent={grid?.GetType().Name}");
            }
            File.WriteAllLines(Path.Combine(directory, "mixed-layout-tree.txt"), mixedLayout);
            CheckToolbarFitsWithoutFrame(window, mainToolbar);
            CheckToolbarFitsWithoutFrame(window, pageToolbar);
            viewModel.SetToolbarLayout(ToolbarDockSide.Top, 0, 0,
                ToolbarDockSide.Top, 0, 0);
            window.SynchronizeAvalonDockLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            viewModel.SetWorkspaceDockPositions(WorkspaceDockPosition.Right,
                WorkspaceDockPosition.Right, WorkspacePaneGroupMode.Stacked);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (window.GetAvalonDockSide(navigation.Parent as LayoutAnchorablePane) != AnchorSide.Right ||
                window.GetAvalonDockSide(properties.Parent as LayoutAnchorablePane) != AnchorSide.Right ||
                ReferenceEquals(navigation.Parent, properties.Parent))
                throw new InvalidOperationException("The two side panes did not follow the stacked layout setting.");
            viewModel.DocumentViewMode = DocumentViewMode.FacingPages;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            viewModel.DocumentViewMode = DocumentViewMode.SinglePage;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (window.GetAvalonDockSide(properties.Parent as LayoutAnchorablePane) != AnchorSide.Right)
                throw new InvalidOperationException("Changing page layout moved the properties pane from the right side.");
            await viewModel.FlushDisplaySettingsAsync();
            var diagnosticPaths = new ApplicationPaths(StorageMode.Portable,
                Path.Combine(directory, ".appstate", "config"),
                Path.Combine(directory, ".appstate", "logs"),
                Path.Combine(directory, ".appstate", "cache"),
                Path.Combine(directory, ".appstate", "workspaces"));
            if (new ApplicationSettingsService(diagnosticPaths).Load().PropertiesDockPosition !=
                WorkspaceDockPosition.Right)
                throw new InvalidOperationException("The right-side properties position was not saved for restart.");
            viewModel.SetWorkspaceDockPositions(WorkspaceDockPosition.Left,
                WorkspaceDockPosition.Left, WorkspacePaneGroupMode.Tabbed);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!ReferenceEquals(navigation.Parent, properties.Parent) ||
                window.GetAvalonDockSide(navigation.Parent as LayoutAnchorablePane) != AnchorSide.Left)
                throw new InvalidOperationException("The two side panes did not return to a left tab group.");
            bookmarks.Float();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!bookmarks.IsFloating || !navigation.IsVisible)
                throw new InvalidOperationException("The bookmark pane could not be detached independently.");
            bookmarks.Dock();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (bookmarks.IsFloating || !ReferenceEquals(bookmarks.Parent, navigation.Parent))
                throw new InvalidOperationException("The bookmark pane did not rejoin the page tab group.");
            File.WriteAllText(Path.Combine(directory, "checks.txt"),
                "PASS: AvalonDock displays the existing editor, page, bookmark and properties panes.\n" +
                "PASS: All three side panes allow floating and native docking guides.\n" +
                "PASS: Tabbed anchorables render their tabs above the content.\n" +
                "PASS: The initial full-width top row places both toolbars side by side above left/editor/right panes.\n" +
                "PASS: Both real toolbars are independently dockable and use slim left/top orientations.\n" +
                "PASS: Toolbar-only panes fit their content without a frame or resize splitter.\n" +
                "PASS: A vertical toolbar floats horizontally and docks back.\n" +
                "PASS: Floating toolbars keep the compact content template.\n" +
                "PASS: Bookmark and page panes detach and rejoin independently.\n" +
                "PASS: Settings move toolbars among four sides and switch side panes between stacked and tabbed groups.\n" +
                "PASS: Both toolbars return from side/bottom docking to one horizontal top row.\n" +
                "PASS: A native edge drop merges separate top toolbar rows without a divider.\n" +
                "PASS: Nested stacked toolbar rows return to the shared band without a divider.\n" +
                "PASS: Floating a toolbar from a stacked row leaves no empty band.\n" +
                "PASS: Repeated stack/float cycles release toolbar height and size the floating strip to its buttons.\n" +
                "PASS: The visible workspace begins directly below the remaining toolbar after each float.\n" +
                "PASS: Dropping toolbars into a regular tab group restores separate, visible toolbar panes.\n" +
                "PASS: A two-page PDF loads in the AvalonDock document pane.\n",
                Encoding.UTF8);
            window.ClosePromptOverride = () => MessageBoxResult.No;
            window.Close();
            Shutdown(0);
        }
        catch (Exception exception)
        {
            if (directory is not null && Directory.Exists(directory))
                File.WriteAllText(Path.Combine(directory, "error.txt"), exception.ToString(), Encoding.UTF8);
            window.Close();
            Shutdown(1);
        }
    }

    private static LayoutAnchorablePaneControl? FindAvalonPaneControl(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is LayoutAnchorablePaneControl control && control.Items.Count > 1)
                return control;
            var nested = FindAvalonPaneControl(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static LayoutAnchorablePaneControl? FindAvalonPaneControl(
        DependencyObject parent, LayoutAnchorablePane? model)
    {
        if (model is null) return null;
        if (parent is LayoutAnchorablePaneControl control && ReferenceEquals(control.Model, model))
            return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var found = FindAvalonPaneControl(VisualTreeHelper.GetChild(parent, index), model);
            if (found is not null) return found;
        }
        return null;
    }

    private static LayoutDocumentPaneControl? FindAvalonDocumentPaneControl(DependencyObject parent)
    {
        if (parent is LayoutDocumentPaneControl document) return document;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var found = FindAvalonDocumentPaneControl(VisualTreeHelper.GetChild(parent, index));
            if (found is not null) return found;
        }
        return null;
    }

    private static void CheckToolbarFitsWithoutFrame(MainWindow window, LayoutAnchorable toolbar)
    {
        var pane = FindAvalonPaneControl(window, toolbar.Parent as LayoutAnchorablePane) ??
            throw new InvalidOperationException("Toolbar pane control is missing.");
        var tray = toolbar.Content as ToolBarTray ??
            throw new InvalidOperationException("Toolbar tray is missing.");
        if (!tray.IsLoaded || !pane.IsAncestorOf(tray))
            throw new InvalidOperationException("Toolbar content is not visible in its dock pane.");
        var vertical = tray.Orientation == Orientation.Vertical;
        var actual = vertical ? pane.ActualWidth : pane.ActualHeight;
        var desired = vertical ? tray.DesiredSize.Width : tray.DesiredSize.Height;
        if (Math.Abs(actual - desired) > 1.5 || pane.BorderThickness != new Thickness(0))
            throw new InvalidOperationException($"Toolbar pane has excess frame or resizable cross-axis space ({actual:F1} vs {desired:F1}).");
        if (VisualTreeHelper.GetParent(pane) is not Grid grid) return;
        var paneIndex = vertical ? Grid.GetColumn(pane) : Grid.GetRow(pane);
        var splitter = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(grid))
            .Select(index => VisualTreeHelper.GetChild(grid, index))
            .OfType<LayoutGridResizerControl>()
            .FirstOrDefault(candidate => Math.Abs((vertical ? Grid.GetColumn(candidate) : Grid.GetRow(candidate)) - paneIndex) == 1);
        if (splitter is not null && (splitter.Visibility != Visibility.Collapsed ||
            (vertical ? splitter.ActualWidth : splitter.ActualHeight) > 0.5)
            )
            throw new InvalidOperationException($"The {toolbar.ContentId} still has a draggable size splitter " +
                $"(visible={splitter.Visibility}, size={splitter.ActualWidth:F0}x{splitter.ActualHeight:F0}).");
    }

    private static bool FindVisiblePanelSplitter(DependencyObject parent)
    {
        if (parent is LayoutGridResizerControl { Visibility: Visibility.Visible } splitter &&
            splitter.ActualWidth > 0 && splitter.ActualHeight > 0) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindVisiblePanelSplitter(VisualTreeHelper.GetChild(parent, index))) return true;
        return false;
    }

    private static void CheckFloatingToolbarSize(LayoutAnchorable toolbar, ToolBarTray tray)
    {
        var floating = PdfCorrectorium.App.MainWindow.FindFloatingToolbarWindow(toolbar) ??
            throw new InvalidOperationException("Floating toolbar is not hosted in an anchorable window.");
        tray.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (floating.ActualWidth < tray.DesiredSize.Width + 12 ||
            floating.ActualWidth > tray.DesiredSize.Width + 80 ||
            floating.ActualHeight < tray.DesiredSize.Height + 25 ||
            floating.MinHeight < tray.DesiredSize.Height + 25)
            throw new InvalidOperationException(
                $"Floating toolbar does not fit its content (window={floating.ActualWidth:F0}x{floating.ActualHeight:F0}, " +
                $"minimum={floating.MinHeight:F0}, tray={tray.DesiredSize.Width:F0}x{tray.DesiredSize.Height:F0}).");
    }

    private static void CheckNoVisibleToolbarGap(MainWindow window,
        LayoutAnchorable toolbar, LayoutAnchorable navigation)
    {
        var manager = window.AvalonDockPreviewManager ??
            throw new InvalidOperationException("Docking manager is missing.");
        var toolbarControl = FindAvalonPaneControl(manager, toolbar.Parent as LayoutAnchorablePane);
        var navigationControl = FindAvalonPaneControl(manager, navigation.Parent as LayoutAnchorablePane);
        if (toolbarControl is null || navigationControl is null)
            throw new InvalidOperationException("Docked toolbar or navigation pane visual is missing.");
        var toolbarTop = toolbarControl.TranslatePoint(new Point(), window).Y;
        var toolbarBottom = toolbarTop + toolbarControl.ActualHeight;
        var workspaceTop = navigationControl.TranslatePoint(new Point(), window).Y;
        if (workspaceTop - toolbarBottom > 28)
        {
            var root = manager.Layout.RootPanel;
            var rootControl = FindLayoutPanelControl(manager, root);
            var rows = rootControl is null ? "missing" : string.Join(",", rootControl.RowDefinitions
                .Select(row => $"{row.Height}:{row.ActualHeight:F1}:min{row.MinHeight:F1}"));
            throw new InvalidOperationException(
                $"A blank toolbar row remains visually above the workspace (gap={workspaceTop - toolbarBottom:F1}, toolbarTop={toolbarTop:F1}, toolbarHeight={toolbarControl.ActualHeight:F1}, workspaceTop={workspaceTop:F1}, rootRows={rows}, bands={string.Join(",", root.Children.OfType<LayoutPanel>().Select(panel => $"{panel.DockHeight}:min{panel.DockMinHeight:F1}"))}, controlMin={rootControl?.MinHeight:F1}).");
        }
    }

    private static LayoutPanelControl? FindLayoutPanelControl(DependencyObject parent, LayoutPanel model)
    {
        if (parent is LayoutPanelControl control && ReferenceEquals(control.Model, model)) return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var found = FindLayoutPanelControl(VisualTreeHelper.GetChild(parent, index), model);
            if (found is not null) return found;
        }
        return null;
    }

    private static LayoutAnchorableControl? FindAvalonToolbarContent(DependencyObject parent, string contentId)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is LayoutAnchorableControl control && control.Model?.ContentId == contentId)
                return control;
            var nested = FindAvalonToolbarContent(child, contentId);
            if (nested is not null) return nested;
        }
        return null;
    }

}
