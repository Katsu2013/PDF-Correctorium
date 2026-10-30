using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

/// <summary>
/// AvalonDock による編集画面のネイティブなドッキング構成です。
/// </summary>
public partial class MainWindow
{
    private static bool _avalonVisualHandlersRegistered;
    private readonly bool _useAvalonDockDiagnosticLayout;
    private DockingManager? _avalonDockPreviewManager;
    private Style? _avalonTopTabStyle;
    private Style? _avalonPaneTabStyle;
    private ControlTemplate? _avalonToolbarContentTemplate;
    private ControlTemplate? _avalonVerticalToolbarContentTemplate;
    private ControlTemplate? _avalonToolbarPaneTemplate;
    private LayoutAnchorable? _avalonNavigation;
    private LayoutAnchorable? _avalonBookmarks;
    private LayoutAnchorable? _avalonProperties;
    private LayoutAnchorable? _avalonMainToolbar;
    private LayoutAnchorable? _avalonPageToolbar;
    private LayoutPanel? _avalonWorkspaceBand;
    private LayoutPanel? _avalonTopToolbarBand;
    private LayoutPanel? _avalonBottomToolbarBand;
    private ToolBarTray? _avalonMainToolbarTray;
    private ToolBarTray? _avalonPageToolbarTray;
    private bool _updatingAvalonSettings;
    private bool _toolbarChromeRefreshQueued;
    private bool _toolbarChromeNeedsRefresh = true;
    private bool _toolbarDropCaptureQueued;
    private bool _normalizeRestoredToolbarLayout;
    private readonly Dictionary<LayoutAnchorable, LayoutAnchorableFloatingWindowControl>
        _sizedFloatingToolbarWindows = [];
    private ApplicationSettings? _lastAppliedDockingSettings;
    private int _avalonLayoutGeneration;

    internal DockingManager? AvalonDockPreviewManager => _avalonDockPreviewManager;
    internal LayoutAnchorable? AvalonNavigation => _avalonNavigation;
    internal LayoutAnchorable? AvalonBookmarks => _avalonBookmarks;
    internal LayoutAnchorable? AvalonProperties => _avalonProperties;
    internal LayoutAnchorable? AvalonMainToolbar => _avalonMainToolbar;
    internal LayoutAnchorable? AvalonPageToolbar => _avalonPageToolbar;

    private void InitializeAvalonDockPreview()
    {
        if (_avalonDockPreviewManager is not null) return;
        if (!_avalonVisualHandlersRegistered)
        {
            // Floating windows create their pane controls after LayoutChanged.  Apply the
            // toolbar-only chrome when each control actually enters the visual tree.
            EventManager.RegisterClassHandler(typeof(LayoutAnchorablePaneControl),
                FrameworkElement.LoadedEvent, new RoutedEventHandler(OnAvalonPaneVisualLoaded));
            EventManager.RegisterClassHandler(typeof(LayoutAnchorableControl),
                FrameworkElement.LoadedEvent, new RoutedEventHandler(OnAvalonPaneVisualLoaded));
            EventManager.RegisterClassHandler(typeof(LayoutAnchorableFloatingWindowControl),
                FrameworkElement.LoadedEvent, new RoutedEventHandler(OnAvalonFloatingWindowLoaded));
            _avalonVisualHandlersRegistered = true;
        }
        var settings = ViewModel.CurrentApplicationSettings;
        if (_useAvalonDockDiagnosticLayout)
            settings = settings with
            {
                NavigationDockPosition = WorkspaceDockPosition.Left,
                BookmarkDockPosition = WorkspaceDockPosition.Left,
                PropertiesDockPosition = WorkspaceDockPosition.Right,
                PaneGroupMode = WorkspacePaneGroupMode.Tabbed,
                MainToolbarDockSide = ToolbarDockSide.Top,
                PageToolbarDockSide = ToolbarDockSide.Top,
            };

        // Reuse the existing view instances so OCR selection, keyboard bindings and scroll state
        // continue to use the same view model. AvalonDock owns their layout, not their contents.
        WorkspaceLayoutGrid.Children.Remove(NavigationDockPane);
        WorkspaceLayoutGrid.Children.Remove(BookmarkDockPane);
        WorkspaceLayoutGrid.Children.Remove(PropertiesDockPane);
        WorkspaceLayoutGrid.Children.Remove(DocumentWorkspacePane);
        // Inactive dock tabs are temporarily detached from the visual tree.  Keep their
        // commands and dynamic tooltips bound while another tab is selected.
        NavigationDockPane.DataContext = ViewModel;
        BookmarkDockPane.DataContext = ViewModel;
        PropertiesDockPane.DataContext = ViewModel;
        DocumentWorkspacePane.DataContext = ViewModel;
        NavigationDockSplitter.Visibility = Visibility.Collapsed;
        PropertiesDockSplitter.Visibility = Visibility.Collapsed;
        foreach (var column in new[] { 0, 1, 3, 4 })
            WorkspaceLayoutGrid.ColumnDefinitions[column].Width = new GridLength(0);

        // Give AvalonDock ownership of each real toolbar. The old grippers are hidden;
        // the native pane title/tab is now the drag handle and supplies the docking guides.
        _avalonMainToolbarTray = CreateAvalonToolbarTray(MainToolbarPanel, MainToolBarTray,
            settings.MainToolbarDockSide);
        _avalonPageToolbarTray = CreateAvalonToolbarTray(PageLayoutToolBar, MainToolBarTray,
            settings.PageToolbarDockSide);
        _avalonMainToolbar = CreateAvalonToolbar("メイン", "main-toolbar", _avalonMainToolbarTray);
        _avalonPageToolbar = CreateAvalonToolbar("ページ表示", "page-toolbar", _avalonPageToolbarTray);
        TopToolbarHost.Visibility = Visibility.Collapsed;
        BottomToolbarHost.Visibility = Visibility.Collapsed;
        LeftToolbarHost.Visibility = Visibility.Collapsed;
        RightToolbarHost.Visibility = Visibility.Collapsed;
        _mainToolbarSide = settings.MainToolbarDockSide;
        _pageToolbarSide = settings.PageToolbarDockSide;
        UpdateToolbarOrientation();

        _avalonNavigation = new LayoutAnchorable
        {
            Title = "ページ", ContentId = "navigation", Content = NavigationDockPane,
            CanClose = false, CanHide = true, CanAutoHide = true,
        };
        _avalonBookmarks = new LayoutAnchorable
        {
            Title = "しおり", ContentId = "bookmarks", Content = BookmarkDockPane,
            CanClose = false, CanHide = true, CanAutoHide = true,
        };
        _avalonProperties = new LayoutAnchorable
        {
            Title = "プロパティ", ContentId = "properties", Content = PropertiesDockPane,
            CanClose = false, CanHide = true, CanAutoHide = true,
        };
        InitializePropertyPanes();
        var document = new LayoutDocument
        {
            Title = "PDF編集", ContentId = "editor", Content = DocumentWorkspacePane,
            CanClose = false, CanFloat = false, CanMove = false,
        };
        var root = new LayoutRoot();
        // Toolbar bands span the whole window. The three-column workspace lives below
        // them, rather than owning them as children of the center/document column.
        var panel = new LayoutPanel { Orientation = Orientation.Vertical };
        var workspace = new LayoutPanel { Orientation = Orientation.Horizontal };
        var navigationSide = settings.NavigationDockPosition == WorkspaceDockPosition.Floating
            ? WorkspaceDockPosition.Left : settings.NavigationDockPosition;
        var propertiesSide = settings.PropertiesDockPosition == WorkspaceDockPosition.Floating
            ? WorkspaceDockPosition.Right : settings.PropertiesDockPosition;
        var bookmarkSide = settings.BookmarkDockPosition == WorkspaceDockPosition.Floating
            ? WorkspaceDockPosition.Left : settings.BookmarkDockPosition;
        var left = CreateAvalonSide(WorkspaceDockPosition.Left, navigationSide, bookmarkSide, propertiesSide, settings);
        var right = CreateAvalonSide(WorkspaceDockPosition.Right, navigationSide, bookmarkSide, propertiesSide, settings);
        _avalonTopToolbarBand = CreateInitialToolbarBand(ToolbarDockSide.Top, settings);
        if (_avalonTopToolbarBand is not null) panel.Children.Add(_avalonTopToolbarBand);
        AddInitialToolbar(workspace, ToolbarDockSide.Left, settings);
        if (left is not null) workspace.Children.Add(left);
        workspace.Children.Add(new LayoutDocumentPane(document));
        if (right is not null) workspace.Children.Add(right);
        AddInitialToolbar(workspace, ToolbarDockSide.Right, settings);
        _avalonWorkspaceBand = workspace;
        panel.Children.Add(workspace);
        _avalonBottomToolbarBand = CreateInitialToolbarBand(ToolbarDockSide.Bottom, settings);
        if (_avalonBottomToolbarBand is not null) panel.Children.Add(_avalonBottomToolbarBand);
        root.RootPanel = panel;
        if (panel.Children is INotifyCollectionChanged rootChildren)
            rootChildren.CollectionChanged += (_, _) =>
            {
                if (!_updatingAvalonSettings && HasNonCanonicalToolbarBand())
                    QueueToolbarDropCapture();
            };

        var manager = new DockingManager
        {
            Layout = root, AllowMixedOrientation = true, DataContext = ViewModel,
            AnchorTemplate = (ControlTemplate)FindResource("AccessibleAutoHideAnchorTemplate"),
            LayoutUpdateStrategy = new ToolbarFallbackLayoutStrategy(this),
        };
        _propertyPanes["read-aloud"].Hide();
        _avalonNavigation.IsSelected = true;
        manager.Loaded += (_, _) =>
        {
            if (_avalonTopTabStyle is not null) return;
            // The theme style becomes available only after the manager joins the visual tree.
            // Deriving from it keeps AvalonDock's tab bindings, commands and floating behavior.
            var defaultPaneStyle = manager.AnchorablePaneControlStyle ??
                manager.TryFindResource("AnchorablePaneControlStyle") as Style ??
                throw new InvalidOperationException("AvalonDock's anchorable pane style was not found.");
            var topTabStyle = new Style(typeof(LayoutAnchorablePaneControl), defaultPaneStyle);
            topTabStyle.Setters.Add(new Setter(TabControl.TabStripPlacementProperty, Dock.Top));
            topTabStyle.Setters.Add(new Setter(Control.TemplateProperty,
                FindResource("TopAvalonAnchorablePaneTemplate")));
            manager.AnchorablePaneControlStyle = topTabStyle;
            _avalonTopTabStyle = topTabStyle;
            _avalonPaneTabStyle = (Style)FindResource("AvalonPaneTabItemStyle");
            _avalonToolbarContentTemplate = (ControlTemplate)FindResource("AvalonToolbarContentTemplate");
            _avalonVerticalToolbarContentTemplate = (ControlTemplate)FindResource("AvalonVerticalToolbarContentTemplate");
            _avalonToolbarPaneTemplate = (ControlTemplate)FindResource("ToolbarAvalonAnchorablePaneTemplate");
            ApplyAvalonTopTabStyle(manager);
            Dispatcher.BeginInvoke(() =>
            {
                ApplyAvalonTopTabStyle(manager);
                if (settings.NavigationDockPosition == WorkspaceDockPosition.Floating)
                    _avalonNavigation.Float();
                if (settings.BookmarkDockPosition == WorkspaceDockPosition.Floating)
                    _avalonBookmarks.Float();
                if (settings.PropertiesDockPosition == WorkspaceDockPosition.Floating)
                    _avalonProperties.Float();
                if (!_useAvalonDockDiagnosticLayout) RestoreNativeWorkspace(settings.WorkspaceLayoutXml);
                ActivateModePropertyPane(revealHidden: false);
                SynchronizeAvalonDockPreviewVisibility();
                UpdateAvalonToolbarOrientation(manager);
                RefreshAvalonToolbarChrome(manager);
            });
        };
        manager.LayoutUpdated += (_, _) =>
        {
            // Moving a whole pane into a newly created root band does not change
            // the toolbar anchorable's Parent, so its PropertyChanged hook misses it.
            if (!_updatingAvalonSettings && HasNonCanonicalToolbarBand())
                QueueToolbarDropCapture();
            if (!_toolbarChromeNeedsRefresh || _toolbarChromeRefreshQueued) return;
            _toolbarChromeRefreshQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _toolbarChromeRefreshQueued = false;
                _toolbarChromeNeedsRefresh = false;
                RefreshAvalonToolbarChrome(manager);
            });
        };
        manager.LayoutChanged += (_, _) =>
        {
            _toolbarChromeNeedsRefresh = true;
            var generation = _avalonLayoutGeneration;
            Dispatcher.BeginInvoke(() =>
            {
                ApplyAvalonTopTabStyle(manager);
                foreach (Window floatingWindow in Application.Current.Windows)
                    if (!ReferenceEquals(floatingWindow, this)) ApplyAvalonTopTabStyle(floatingWindow);
                UpdateAvalonToolbarOrientation(manager);
                RefreshAvalonToolbarChrome(manager);
                if (generation == _avalonLayoutGeneration) CaptureAvalonDockingSettings();
            });
        };
        manager.AddHandler(Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler((_, _) => Dispatcher.BeginInvoke(CaptureAvalonDockingSettings)), true);
        AttachAvalonToolbarEvents();
        foreach (var tray in new[] { _avalonMainToolbarTray, _avalonPageToolbarTray })
            tray.SizeChanged += (_, _) => _toolbarChromeNeedsRefresh = true;
        _avalonDockPreviewManager = manager;
        AttachAvalonPaneVisibilityEvents();
        _lastAppliedDockingSettings = settings;
        Grid.SetColumn(manager, 2);
        WorkspaceLayoutGrid.Children.Add(manager);
    }

    private void AttachAvalonToolbarEvents()
    {
        foreach (var toolbar in new[] { _avalonMainToolbar!, _avalonPageToolbar! })
        {
            toolbar.CanClose = false; toolbar.CanHide = false; toolbar.CanAutoHide = false;
            toolbar.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LayoutAnchorable.IsFloating) && !toolbar.IsFloating)
                    _sizedFloatingToolbarWindows.Remove(toolbar);
                if (args.PropertyName is nameof(LayoutAnchorable.IsFloating) or "Parent")
                    QueueToolbarDropCapture();
            };
        }
    }

    private static void OnAvalonPaneVisualLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject visual) return;
        foreach (var window in Application.Current.Windows.OfType<MainWindow>())
            if (window._avalonTopTabStyle is not null)
                window.ApplyAvalonTopTabStyle(visual);
    }

    private static void OnAvalonFloatingWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not LayoutAnchorableFloatingWindowControl floatingWindow) return;
        floatingWindow.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in Application.Current.Windows.OfType<MainWindow>())
                window.SizeFloatingToolbars(floatingWindow);
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void QueueToolbarDropCapture()
    {
        if (_toolbarDropCaptureQueued) return;
        _toolbarDropCaptureQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _toolbarDropCaptureQueued = false;
            if (_updatingAvalonSettings) return;
            if (_avalonDockPreviewManager is { } manager)
            {
                UpdateAvalonToolbarOrientation(manager);
                _toolbarChromeNeedsRefresh = true;
            }
            SizeFloatingToolbars();
            if (ToolbarHasSettled(_avalonMainToolbar) && ToolbarHasSettled(_avalonPageToolbar))
                PruneVacantToolbarBands();
            if (_avalonDockPreviewManager is { } settledManager)
            {
                RefreshAvalonToolbarChrome(settledManager);
                _toolbarChromeNeedsRefresh = false;
            }
            CaptureAvalonDockingSettings();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private bool ToolbarHasSettled(LayoutAnchorable? toolbar) =>
        toolbar is not null && (toolbar.IsFloating || IsSettledDockedToolbar(toolbar));

    private bool HasNonCanonicalToolbarBand()
    {
        var root = _avalonDockPreviewManager?.Layout.RootPanel;
        return root is not null && root.Children.Any(element =>
            !ReferenceEquals(element, _avalonWorkspaceBand) &&
            !ReferenceEquals(element, _avalonTopToolbarBand) &&
            !ReferenceEquals(element, _avalonBottomToolbarBand) &&
            (ContainsToolbarLayout(element) || IsVacantDockLayout(element)));
    }

    private static bool ContainsToolbarLayout(ILayoutElement element) => element switch
    {
        LayoutAnchorablePane pane => IsToolbarOnlyPane(pane),
        ILayoutContainer container => container.Children.Any(ContainsToolbarLayout),
        _ => false,
    };

    private static bool IsVacantDockLayout(ILayoutElement element) => element switch
    {
        LayoutAnchorablePane pane => pane.Children.Count == 0,
        ILayoutContainer container => !container.Children.Any() ||
            container.Children.All(IsVacantDockLayout),
        _ => false,
    };

    private void PruneVacantToolbarBands()
    {
        var root = _avalonDockPreviewManager?.Layout.RootPanel;
        if (root is null) return;
        var changed = false;
        void Prune(ILayoutElement element)
        {
            if (element is LayoutPanel panel)
            {
                foreach (var child in panel.Children.ToArray())
                {
                    Prune(child);
                    if (!IsVacantDockLayout(child)) continue;
                    panel.Children.Remove(child);
                    changed = true;
                }
            }
            else if (element is LayoutAnchorablePaneGroup group)
            {
                foreach (var child in group.Children.ToArray())
                {
                    Prune(child);
                    if (!IsVacantDockLayout(child)) continue;
                    group.Children.Remove(child);
                    changed = true;
                }
            }
        }
        foreach (var band in root.Children.Where(element =>
                     !ReferenceEquals(element, _avalonWorkspaceBand)).ToArray())
        {
            Prune(band);
            if (!IsVacantDockLayout(band)) continue;
            root.Children.Remove(band);
            if (ReferenceEquals(band, _avalonTopToolbarBand)) _avalonTopToolbarBand = null;
            if (ReferenceEquals(band, _avalonBottomToolbarBand)) _avalonBottomToolbarBand = null;
            changed = true;
        }
        if (!changed) return;
        _avalonLayoutGeneration++;
        _toolbarChromeNeedsRefresh = true;
    }

    private void PruneVacantWorkspacePanes()
    {
        if (_avalonWorkspaceBand is null) return;
        // Empty panes can still be the native PreviousContainer of an X-hidden panel.
        // Removing them makes Show() insert into a detached tree (or do nothing).
        // Keep these invisible, bounded workspace containers while panels are hidden;
        // toolbar-only band compaction remains independent.
        if (_avalonDockPreviewManager?.Layout.Hidden.Count > 0) return;
        foreach (var pane in _avalonWorkspaceBand.Children.OfType<LayoutAnchorablePane>()
                     .Where(pane => pane.Children.Count == 0).ToArray())
            _avalonWorkspaceBand.Children.Remove(pane);
    }

    private static ToolBarTray CreateAvalonToolbarTray(ToolBar toolbar, ToolBarTray original,
        ToolbarDockSide side)
    {
        original.ToolBars.Remove(toolbar);
        toolbar.Band = 0;
        toolbar.BandIndex = 0;
        var tray = new ToolBarTray
        {
            IsLocked = true,
            Orientation = side is ToolbarDockSide.Left or ToolbarDockSide.Right
                ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        tray.ToolBars.Add(toolbar);
        return tray;
    }

    private static LayoutAnchorable CreateAvalonToolbar(string title, string contentId, ToolBarTray tray) =>
        new()
        {
            Title = title, ContentId = contentId, Content = tray,
            CanClose = false, CanHide = false, CanAutoHide = false,
            CanDockAsTabbedDocument = false,
        };

    private void AddInitialToolbar(LayoutPanel panel, ToolbarDockSide side, ApplicationSettings settings)
    {
        if (settings.MainToolbarDockSide == side)
            panel.Children.Add(CreateAvalonToolbarPane(_avalonMainToolbar!));
        if (settings.PageToolbarDockSide == side)
            panel.Children.Add(CreateAvalonToolbarPane(_avalonPageToolbar!));
    }

    private LayoutPanel? CreateInitialToolbarBand(ToolbarDockSide side, ApplicationSettings settings)
    {
        if (settings.MainToolbarDockSide != side && settings.PageToolbarDockSide != side) return null;
        var band = new LayoutPanel { Orientation = Orientation.Horizontal,
            DockHeight = new GridLength(60) };
        AddInitialToolbar(band, side, settings);
        return band;
    }

    private static LayoutAnchorablePane CreateAvalonToolbarPane(LayoutAnchorable toolbar) =>
        new(toolbar)
        {
            DockWidth = new GridLength(64, GridUnitType.Pixel),
            DockHeight = new GridLength(60, GridUnitType.Pixel),
        };

    private void RefreshAvalonToolbarChrome(DockingManager manager)
    {
        if (_avalonToolbarPaneTemplate is null) return;
        foreach (var toolbar in new[] { _avalonMainToolbar, _avalonPageToolbar })
        {
            if (toolbar?.IsFloating != false || toolbar.Parent is not LayoutAnchorablePane pane ||
                !IsToolbarOnlyPane(pane)) continue;
            var tray = toolbar.Content as ToolBarTray;
            if (tray is null) continue;
            var paneControl = FindAvalonToolbarPaneControl(manager, pane);
            if (paneControl?.Template.FindName("ToolbarDragTitle", paneControl) is FrameworkElement title)
            {
                var titleVertical = tray.Orientation == Orientation.Vertical;
                DockPanel.SetDock(title, titleVertical ? Dock.Top : Dock.Left);
                title.Width = titleVertical ? double.NaN : 12;
                title.Height = titleVertical ? 13 : double.NaN;
            }
            tray.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var vertical = tray.Orientation == Orientation.Vertical;
            var required = Math.Ceiling(vertical ? tray.DesiredSize.Width : tray.DesiredSize.Height);
            if (!double.IsFinite(required) || required < 20) continue;
            if (vertical)
            {
                if (!pane.DockWidth.IsAbsolute || Math.Abs(pane.DockWidth.Value - required) > 0.5)
                    pane.DockWidth = new GridLength(required);
                if (Math.Abs(pane.DockMinWidth - required) > 0.5) pane.DockMinWidth = required;
                if (paneControl is not null && VisualTreeHelper.GetParent(paneControl) is Grid grid &&
                    Grid.GetColumn(paneControl) is var column && column < grid.ColumnDefinitions.Count &&
                    Math.Abs(grid.ColumnDefinitions[column].Width.Value - required) > 0.5)
                {
                    grid.ColumnDefinitions[column].Width = new GridLength(required);
                    grid.InvalidateMeasure();
                }
            }
            else
            {
                if (!pane.DockHeight.IsAbsolute || Math.Abs(pane.DockHeight.Value - required) > 0.5)
                    pane.DockHeight = new GridLength(required);
                if (Math.Abs(pane.DockMinHeight - required) > 0.5) pane.DockMinHeight = required;
                if (paneControl is not null && VisualTreeHelper.GetParent(paneControl) is Grid rowGrid &&
                    Grid.GetRow(paneControl) is var row && row < rowGrid.RowDefinitions.Count &&
                    Math.Abs(rowGrid.RowDefinitions[row].Height.Value - required) > 0.5)
                {
                    rowGrid.RowDefinitions[row].Height = new GridLength(required);
                    rowGrid.InvalidateMeasure();
                }
                if (pane.Parent is LayoutPanel band &&
                    (ReferenceEquals(band, _avalonTopToolbarBand) ||
                     ReferenceEquals(band, _avalonBottomToolbarBand)))
                {
                    if (band.Children.OfType<LayoutAnchorablePane>().Count() > 1 &&
                        paneControl is not null && VisualTreeHelper.GetParent(paneControl) is Grid grid &&
                        Grid.GetColumn(paneControl) is var column && column < grid.ColumnDefinitions.Count)
                    {
                        var width = Math.Ceiling(tray.DesiredSize.Width + 12);
                        if (!pane.DockWidth.IsAbsolute || Math.Abs(pane.DockWidth.Value - width) > 0.5)
                            pane.DockWidth = new GridLength(width);
                        if (Math.Abs(grid.ColumnDefinitions[column].Width.Value - width) > 0.5)
                            grid.ColumnDefinitions[column].Width = new GridLength(width);
                    }
                }
            }
        }
        // A native drop can wrap the original band in another LayoutPanel. Updating
        // only panes directly parented by the canonical band misses that case and
        // leaves the old second-row height after the lower toolbar is floated.
        var root = manager.Layout.RootPanel;
        foreach (var band in root.Children.OfType<LayoutPanel>()
                     .Where(child => !ReferenceEquals(child, _avalonWorkspaceBand)))
        {
            var bandHeight = MeasureLiveToolbarLayoutHeight(band);
            if (bandHeight <= 0) continue;
            if (!band.DockHeight.IsAbsolute || Math.Abs(band.DockHeight.Value - bandHeight) > 0.5)
                band.DockHeight = new GridLength(bandHeight);
            var bandControl = FindAvalonToolbarBandControl(manager, band);
            if (bandControl is not null && VisualTreeHelper.GetParent(bandControl) is Grid bandGrid &&
                Grid.GetRow(bandControl) is var bandRow && bandRow < bandGrid.RowDefinitions.Count)
            {
                var row = bandGrid.RowDefinitions[bandRow];
                // AvalonDock keeps the former two-row minimum after a toolbar floats.
                // Changing Height alone cannot shrink a Grid row below MinHeight.
                var changed = false;
                if (Math.Abs(row.MinHeight - bandHeight) > 0.5)
                {
                    row.MinHeight = bandHeight;
                    changed = true;
                }
                if (!row.Height.IsAbsolute || Math.Abs(row.Height.Value - bandHeight) > 0.5)
                {
                    row.Height = new GridLength(bandHeight);
                    changed = true;
                }
                if (changed) bandGrid.InvalidateMeasure();
            }
        }
        SuppressAvalonToolbarSplitters(manager, manager);
    }

    private static double MeasureLiveToolbarLayoutHeight(ILayoutElement element)
    {
        if (element is LayoutAnchorablePane pane)
            return pane.Children.Any(child => !child.IsFloating &&
                (child.ContentId is "main-toolbar" or "page-toolbar")) && pane.DockHeight.IsAbsolute
                ? pane.DockHeight.Value : 0;
        if (element is not ILayoutContainer container) return 0;
        var heights = container.Children.Select(MeasureLiveToolbarLayoutHeight)
            .Where(height => height > 0).ToArray();
        if (heights.Length == 0) return 0;
        var orientation = element switch
        {
            LayoutPanel panel => panel.Orientation,
            LayoutAnchorablePaneGroup group => group.Orientation,
            _ => Orientation.Horizontal,
        };
        return orientation == Orientation.Vertical ? heights.Sum() : heights.Max();
    }

    private static bool IsToolbarOnlyPane(LayoutAnchorablePane pane) =>
        pane.Children.Count > 0 &&
        pane.Children.All(child => child.ContentId is "main-toolbar" or "page-toolbar");

    private static bool IsToolbarOnlyLayout(ILayoutElement element) => element switch
    {
        LayoutAnchorablePane pane => IsToolbarOnlyPane(pane),
        ILayoutContainer container => container.Children.Any() &&
            container.Children.All(IsToolbarOnlyLayout),
        _ => false,
    };

    private static LayoutPanelControl? FindAvalonToolbarBandControl(
        DependencyObject root, LayoutPanel model)
    {
        if (root is LayoutPanelControl control && ReferenceEquals(control.Model, model))
            return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindAvalonToolbarBandControl(VisualTreeHelper.GetChild(root, index), model);
            if (found is not null) return found;
        }
        return null;
    }

    private static LayoutAnchorableControl? FindAvalonToolbarContent(
        DependencyObject parent, LayoutAnchorable toolbar)
    {
        if (parent is LayoutAnchorableControl control && ReferenceEquals(control.Model, toolbar))
            return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var found = FindAvalonToolbarContent(VisualTreeHelper.GetChild(parent, index), toolbar);
            if (found is not null) return found;
        }
        return null;
    }

    private void SuppressAvalonToolbarSplitters(DependencyObject parent, DockingManager manager)
    {
        if (parent is LayoutPanelControl grid)
        {
            var children = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(grid))
                .Select(index => VisualTreeHelper.GetChild(grid, index)).ToArray();
            var horizontal = grid.ColumnDefinitions.Count > 1;
            foreach (var splitter in children.OfType<LayoutGridResizerControl>())
            {
                var index = horizontal ? Grid.GetColumn(splitter) : Grid.GetRow(splitter);
                var besideToolbar = children.OfType<LayoutAnchorablePaneControl>()
                    .Any(pane => pane.Model is LayoutAnchorablePane model && IsToolbarOnlyPane(model) &&
                        Math.Abs((horizontal ? Grid.GetColumn(pane) : Grid.GetRow(pane)) - index) == 1) ||
                    children.OfType<LayoutPanelControl>()
                    .Any(panel => panel.Model is ILayoutElement model && IsToolbarOnlyLayout(model) &&
                        Math.Abs((horizontal ? Grid.GetColumn(panel) : Grid.GetRow(panel)) - index) == 1);
                if (besideToolbar)
                {
                    if (splitter.Visibility != Visibility.Collapsed) splitter.Visibility = Visibility.Collapsed;
                    splitter.IsHitTestVisible = false;
                    var definition = horizontal ? grid.ColumnDefinitions[index] as DefinitionBase :
                        grid.RowDefinitions[index];
                    if (definition is ColumnDefinition column && column.Width.Value != 0)
                        column.Width = new GridLength(0);
                    if (definition is RowDefinition row && row.Height.Value != 0)
                        row.Height = new GridLength(0);
                }
                else if (splitter.Visibility == Visibility.Collapsed)
                {
                    splitter.Visibility = Visibility.Visible;
                    splitter.IsHitTestVisible = true;
                    if (horizontal && index < grid.ColumnDefinitions.Count)
                        grid.ColumnDefinitions[index].Width = new GridLength(manager.GridSplitterWidth);
                    else if (!horizontal && index < grid.RowDefinitions.Count)
                        grid.RowDefinitions[index].Height = new GridLength(manager.GridSplitterHeight);
                }
            }
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            SuppressAvalonToolbarSplitters(VisualTreeHelper.GetChild(parent, index), manager);
    }

    private void UpdateAvalonToolbarOrientation(DockingManager manager)
    {
        if (_avalonMainToolbar is null || _avalonPageToolbar is null ||
            _avalonMainToolbarTray is null || _avalonPageToolbarTray is null) return;
        _mainToolbarSide = UpdateAvalonToolbarOrientation(manager, _avalonMainToolbar,
            _avalonMainToolbarTray);
        _pageToolbarSide = UpdateAvalonToolbarOrientation(manager, _avalonPageToolbar,
            _avalonPageToolbarTray);
        UpdateToolbarOrientation();
        ApplyAvalonTopTabStyle(manager);
        SizeFloatingToolbars();
    }

    private void SizeFloatingToolbars(Window? targetWindow = null)
    {
        foreach (var toolbar in new[] { _avalonMainToolbar, _avalonPageToolbar })
        {
            if (toolbar is null) continue;
            if (!toolbar.IsFloating)
            {
                _sizedFloatingToolbarWindows.Remove(toolbar);
                continue;
            }
            if (toolbar.Content is not ToolBarTray tray ||
                FindFloatingToolbarWindow(toolbar) is not { } floatingWindow ||
                (targetWindow is not null && !ReferenceEquals(targetWindow, floatingWindow))) continue;
            if (_sizedFloatingToolbarWindows.TryGetValue(toolbar, out var sizedWindow) &&
                ReferenceEquals(sizedWindow, floatingWindow)) continue;

            // AvalonDock otherwise carries the wide dock band and its tiny height into
            // the floating window. Measure the horizontal toolbar, then allow for the
            // native title bar and pane chrome without resizing it again after the user
            // has adjusted the floating window manually.
            tray.InvalidateMeasure();
            foreach (var item in tray.ToolBars) item.InvalidateMeasure();
            tray.UpdateLayout();
            tray.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (tray.DesiredSize.Width <= 0 || tray.DesiredSize.Height <= 0) continue;
            var width = Math.Min(Math.Max(64, Math.Ceiling(tray.DesiredSize.Width + 32)),
                Math.Max(160, SystemParameters.WorkArea.Width - 40));
            var height = Math.Max(70, Math.Ceiling(tray.DesiredSize.Height +
                SystemParameters.WindowCaptionHeight + 6));
            toolbar.FloatingWidth = width;
            toolbar.FloatingHeight = height;
            floatingWindow.SizeToContent = SizeToContent.Manual;
            floatingWindow.Width = width;
            // AvalonDock may reapply the dock band's shallow height on mouse release.
            // A minimum prevents clipping without preventing a larger manual size.
            floatingWindow.MinHeight = height;
            floatingWindow.Height = height;
            _sizedFloatingToolbarWindows[toolbar] = floatingWindow;
            // A toolbar rotated from a vertical dock can complete its horizontal
            // reflow after the floating window is created. Correct that one-time
            // measurement without changing the user's later manual window size.
            var correctionTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle, Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(75) };
            correctionTimer.Tick += (_, _) =>
            {
                correctionTimer.Stop();
                if (!toolbar.IsFloating || !ReferenceEquals(FindFloatingToolbarWindow(toolbar), floatingWindow))
                    return;
                tray.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var correctedWidth = Math.Min(Math.Max(64, Math.Ceiling(tray.DesiredSize.Width + 32)),
                    Math.Max(160, SystemParameters.WorkArea.Width - 40));
                if (Math.Abs(correctedWidth - floatingWindow.Width) <= 1) return;
                toolbar.FloatingWidth = correctedWidth;
                floatingWindow.Width = correctedWidth;
            };
            correctionTimer.Start();
        }
    }

    internal static LayoutAnchorableFloatingWindowControl? FindFloatingToolbarWindow(LayoutAnchorable toolbar)
    {
        for (ILayoutContainer? ancestor = toolbar.Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor is LayoutAnchorableFloatingWindow model)
                return Application.Current.Windows.OfType<LayoutAnchorableFloatingWindowControl>()
                    .FirstOrDefault(window => ReferenceEquals(window.Model, model));
        return null;
    }

    private ToolbarDockSide UpdateAvalonToolbarOrientation(DockingManager manager,
        LayoutAnchorable toolbar, ToolBarTray tray)
    {
        if (toolbar.IsFloating)
        {
            tray.Orientation = Orientation.Horizontal;
            return ReferenceEquals(toolbar, _avalonMainToolbar) ? _mainToolbarSide : _pageToolbarSide;
        }
        var pane = FindAvalonToolbarPaneControl(manager, toolbar.Parent as LayoutAnchorablePane);
        if (pane is null) return tray.Orientation == Orientation.Vertical
            ? ToolbarDockSide.Left : ToolbarDockSide.Top;
        var side = GetAvalonDockSide(toolbar.Parent as LayoutAnchorablePane);
        var vertical = side is AnchorSide.Left or AnchorSide.Right;
        tray.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        return side switch
        {
            AnchorSide.Left => ToolbarDockSide.Left,
            AnchorSide.Right => ToolbarDockSide.Right,
            AnchorSide.Bottom => ToolbarDockSide.Bottom,
            _ => ToolbarDockSide.Top,
        };
    }

    private static LayoutAnchorablePaneControl? FindAvalonToolbarPaneControl(
        DependencyObject root, LayoutAnchorablePane? model)
    {
        if (model is null) return null;
        if (root is LayoutAnchorablePaneControl control && ReferenceEquals(control.Model, model))
            return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindAvalonToolbarPaneControl(VisualTreeHelper.GetChild(root, index), model);
            if (found is not null) return found;
        }
        return null;
    }

    private void ApplyAvalonTopTabStyle(DependencyObject parent)
    {
        if (_avalonTopTabStyle is null || _avalonPaneTabStyle is null ||
            _avalonToolbarContentTemplate is null || _avalonVerticalToolbarContentTemplate is null) return;
        if (parent is LayoutAnchorablePaneControl pane && !ReferenceEquals(pane.Style, _avalonTopTabStyle))
            pane.Style = _avalonTopTabStyle;
        if (parent is LayoutAnchorablePaneControl toolbarPane)
        {
            var toolbarOnly = toolbarPane.Model is LayoutAnchorablePane model && IsToolbarOnlyPane(model);
            var paneTemplate = toolbarOnly ? _avalonToolbarPaneTemplate :
                (ControlTemplate)FindResource("TopAvalonAnchorablePaneTemplate");
            if (paneTemplate is not null && !ReferenceEquals(toolbarPane.Template, paneTemplate))
                toolbarPane.Template = paneTemplate;
            if (toolbarOnly)
            {
                toolbarPane.Padding = new Thickness(0);
                toolbarPane.BorderThickness = new Thickness(0);
                if (toolbarPane.Items.Count > 0 && toolbarPane.SelectedIndex < 0)
                    toolbarPane.SelectedIndex = 0;
            }
        }
        if (parent is LayoutAnchorablePaneControl anchorablePane)
        {
            // Toolbar panes have no tab strip in their template. Collapsing their only
            // TabItem prevents WPF from recreating SelectedContent after a float/dock.
            var itemStyle = anchorablePane.Model is LayoutAnchorablePane model && IsToolbarOnlyPane(model)
                ? null : _avalonPaneTabStyle;
            if (!ReferenceEquals(anchorablePane.ItemContainerStyle, itemStyle))
                anchorablePane.ItemContainerStyle = itemStyle;
        }
        if (parent is LayoutDocumentPaneControl documentPane &&
            !ReferenceEquals(documentPane.ItemContainerStyle, _avalonPaneTabStyle))
            documentPane.ItemContainerStyle = _avalonPaneTabStyle;
        if (parent is LayoutAnchorableControl content &&
            content.Model?.ContentId is "main-toolbar" or "page-toolbar")
        {
            var toolbarTemplate = (content.Model.Content as ToolBarTray)?.Orientation == Orientation.Vertical
                ? _avalonVerticalToolbarContentTemplate : _avalonToolbarContentTemplate;
            if (!ReferenceEquals(content.Template, toolbarTemplate)) content.Template = toolbarTemplate;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            ApplyAvalonTopTabStyle(VisualTreeHelper.GetChild(parent, index));
    }

    private ILayoutPanelElement? CreateAvalonSide(
        WorkspaceDockPosition side, WorkspaceDockPosition navigationSide, WorkspaceDockPosition bookmarkSide,
        WorkspaceDockPosition propertiesSide, ApplicationSettings settings)
    {
        var navigationHere = navigationSide == side;
        var bookmarksHere = bookmarkSide == side;
        var propertiesHere = propertiesSide == side;
        if (!navigationHere && !bookmarksHere && !propertiesHere) return null;
        var width = new GridLength(Math.Max(navigationHere || bookmarksHere ? settings.PageListWidth : 0,
            propertiesHere ? settings.PropertiesPanelWidth : 0), GridUnitType.Pixel);
        if (navigationHere && propertiesHere && settings.PaneGroupMode == WorkspacePaneGroupMode.Stacked)
        {
            var stack = new LayoutAnchorablePaneGroup { Orientation = Orientation.Vertical, DockWidth = width };
            var navigationPane = new LayoutAnchorablePane(_avalonNavigation!);
            if (bookmarksHere) navigationPane.Children.Add(_avalonBookmarks!);
            stack.Children.Add(navigationPane);
            stack.Children.Add(new LayoutAnchorablePane(_avalonProperties!));
            foreach (var property in _propertyPanes.Values.Where(item => !ReferenceEquals(item, _avalonProperties)))
                ((LayoutAnchorablePane)stack.Children[1]).Children.Add(property);
            return stack;
        }
        var pane = new LayoutAnchorablePane { DockWidth = width };
        if (navigationHere) pane.Children.Add(_avalonNavigation!);
        if (bookmarksHere) pane.Children.Add(_avalonBookmarks!);
        if (propertiesHere) pane.Children.Add(_avalonProperties!);
        if (propertiesHere)
            foreach (var property in _propertyPanes.Values.Where(item => !ReferenceEquals(item, _avalonProperties))) pane.Children.Add(property);
        return pane;
    }

    private void SynchronizeAvalonDockPreviewVisibility()
    {
        if (_avalonDockPreviewManager is null) return;
        var settings = ViewModel.CurrentApplicationSettings;
        if (_avalonNavigation is not null && _avalonNavigation.IsVisible != settings.ShowPageListPanel)
            SetAvalonPaneVisible(_avalonNavigation, settings.ShowPageListPanel);
        if (_avalonBookmarks is not null && _avalonBookmarks.IsVisible != settings.ShowBookmarksPanel)
            SetAvalonPaneVisible(_avalonBookmarks, settings.ShowBookmarksPanel);
        if (_avalonProperties is not null && _avalonProperties.IsVisible != settings.ShowPropertiesPanel)
            SetAvalonPaneVisible(_avalonProperties, settings.ShowPropertiesPanel);
    }

    internal void SynchronizeAvalonDockLayout()
    {
        var manager = _avalonDockPreviewManager;
        if (_updatingAvalonSettings)
        {
            // A settings change raised while a native drag is being captured must not be lost.
            Dispatcher.BeginInvoke(SynchronizeAvalonDockLayout);
            return;
        }
        if (manager is null ||
            _avalonNavigation is null || _avalonBookmarks is null || _avalonProperties is null ||
            _avalonMainToolbar is null || _avalonPageToolbar is null) return;
        var settings = ViewModel.CurrentApplicationSettings;
        var restoreRequested = _appliedWorkspaceRestoreVersion != ViewModel.WorkspaceLayoutRestoreVersion;
        _appliedWorkspaceRestoreVersion = ViewModel.WorkspaceLayoutRestoreVersion;
        if (!_restoringNativeWorkspace && restoreRequested &&
            settings.WorkspaceLayoutXml is not null && RestoreNativeWorkspace(settings.WorkspaceLayoutXml))
        {
            UpdateAvalonToolbarOrientation(manager);
            _toolbarChromeNeedsRefresh = true;
            return;
        }
        _updatingAvalonSettings = true;
        _avalonLayoutGeneration++;
        try
        {
            var previous = _lastAppliedDockingSettings;
            if (previous is null || previous.NavigationDockPosition != settings.NavigationDockPosition)
                PositionPane(_avalonNavigation, settings.NavigationDockPosition, settings.PageListWidth, null);
            var bookmarkTarget = settings.BookmarkDockPosition == settings.NavigationDockPosition &&
                                 settings.BookmarkDockPosition != WorkspaceDockPosition.Floating
                ? _avalonNavigation.Parent as LayoutAnchorablePane : null;
            if (previous is null || previous.BookmarkDockPosition != settings.BookmarkDockPosition ||
                previous.NavigationDockPosition != settings.NavigationDockPosition && bookmarkTarget is not null)
                PositionPane(_avalonBookmarks, settings.BookmarkDockPosition, settings.PageListWidth, bookmarkTarget);
            var tabTarget = settings.NavigationDockPosition == settings.PropertiesDockPosition &&
                            settings.PaneGroupMode == WorkspacePaneGroupMode.Tabbed
                ? _avalonNavigation.Parent as LayoutAnchorablePane : null;
            if (previous is null || previous.PropertiesDockPosition != settings.PropertiesDockPosition ||
                previous.PaneGroupMode != settings.PaneGroupMode ||
                previous.NavigationDockPosition != settings.NavigationDockPosition && tabTarget is not null)
                PositionPane(_avalonProperties, settings.PropertiesDockPosition, settings.PropertiesPanelWidth, tabTarget);
            if (settings.NavigationDockPosition == settings.PropertiesDockPosition &&
                settings.PaneGroupMode == WorkspacePaneGroupMode.Stacked &&
                _avalonNavigation.Parent is LayoutAnchorablePane first &&
                _avalonProperties.Parent is LayoutAnchorablePane second &&
                !ReferenceEquals(first, second))
                StackPanes(first, second);
            PlaceAtSide(_avalonMainToolbar, ToAnchorSide(settings.MainToolbarDockSide), 64);
            PlaceAtSide(_avalonPageToolbar, ToAnchorSide(settings.PageToolbarDockSide), 64);
            PruneVacantWorkspacePanes();
            if (_avalonNavigation.Parent is LayoutAnchorablePane navigationPane)
                navigationPane.DockWidth = new GridLength(settings.PageListWidth);
            if (_avalonProperties.Parent is LayoutAnchorablePane propertiesPane)
                propertiesPane.DockWidth = new GridLength(settings.PropertiesPanelWidth);
            SynchronizeAvalonDockPreviewVisibility();
            UpdateAvalonToolbarOrientation(manager);
            _lastAppliedDockingSettings = settings;
            _toolbarChromeNeedsRefresh = true;
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(manager, _avalonDockPreviewManager))
                    RefreshAvalonToolbarChrome(manager);
            });
        }
        finally
        {
            _avalonLayoutGeneration++;
            _updatingAvalonSettings = false;
        }
    }

    private bool HasChangedDockingSettings(ApplicationSettings settings)
    {
        var previous = _lastAppliedDockingSettings;
        return previous is null ||
            previous.NavigationDockPosition != settings.NavigationDockPosition ||
            previous.BookmarkDockPosition != settings.BookmarkDockPosition ||
            previous.PropertiesDockPosition != settings.PropertiesDockPosition ||
            previous.PaneGroupMode != settings.PaneGroupMode ||
            previous.MainToolbarDockSide != settings.MainToolbarDockSide ||
            previous.PageToolbarDockSide != settings.PageToolbarDockSide ||
            previous.ShowPageListPanel != settings.ShowPageListPanel ||
            previous.ShowBookmarksPanel != settings.ShowBookmarksPanel ||
            previous.ShowPropertiesPanel != settings.ShowPropertiesPanel ||
            previous.WorkspaceLayoutXml != settings.WorkspaceLayoutXml ||
            Math.Abs(previous.PageListWidth - settings.PageListWidth) > 0.5 ||
            Math.Abs(previous.PropertiesPanelWidth - settings.PropertiesPanelWidth) > 0.5;
    }

    private void PositionPane(LayoutAnchorable pane, WorkspaceDockPosition position,
        double width, LayoutAnchorablePane? tabTarget)
    {
        if (!pane.IsVisible || !ReferenceEquals(pane.Root, _avalonDockPreviewManager?.Layout))
            SetAvalonPaneVisible(pane, true);
        if (position == WorkspaceDockPosition.Floating)
        {
            if (!pane.IsFloating) pane.Float();
            return;
        }
        if (pane.IsFloating) pane.Dock();
        if (tabTarget is not null)
        {
            if (!ReferenceEquals(pane.Parent, tabTarget))
            {
                (pane.Parent as LayoutAnchorablePane)?.Children.Remove(pane);
                tabTarget.Children.Add(pane);
            }
            return;
        }
        PlaceAtSide(pane, position == WorkspaceDockPosition.Left ? AnchorSide.Left : AnchorSide.Right,
            width, requireOwnPane: true);
    }

    private void PlaceAtSide(LayoutAnchorable anchorable, AnchorSide side, double width,
        bool requireOwnPane = false)
    {
        if (_avalonDockPreviewManager is null) return;
        if (anchorable.IsFloating) anchorable.Dock();
        var current = anchorable.Parent as LayoutAnchorablePane;
        var target = side is AnchorSide.Top or AnchorSide.Bottom
            ? GetToolbarBand(side) : _avalonWorkspaceBand;
        if (target is null) return;
        var toolbar = anchorable.ContentId is "main-toolbar" or "page-toolbar";
        if (GetAvalonDockSide(current) == side && ReferenceEquals(current?.Parent, target) &&
            (!requireOwnPane && !toolbar || current.Children.Count == 1))
        {
            if (toolbar && current is not null && side is AnchorSide.Left or AnchorSide.Right)
            {
                var edge = side == AnchorSide.Left ? 0 : target.Children.Count - 1;
                if (target.Children.IndexOf(current) != edge)
                {
                    target.Children.Remove(current);
                    target.Children.Insert(side == AnchorSide.Left ? 0 : target.Children.Count, current);
                }
            }
            return;
        }
        var toolbarContent = toolbar ? anchorable.Content : null;
        if (toolbarContent is not null) anchorable.Content = null;
        LayoutAnchorablePane pane;
        if (!requireOwnPane && current is { Children.Count: 1 } &&
            (ReferenceEquals(current.Parent, _avalonTopToolbarBand) ||
             ReferenceEquals(current.Parent, _avalonBottomToolbarBand) ||
             ReferenceEquals(current.Parent, _avalonWorkspaceBand)))
        {
            // Reparent the pane itself. Moving only its anchorable leaves AvalonDock's
            // selected-content control detached until an unrelated tab selection occurs.
            (current.Parent as LayoutPanel)?.Children.Remove(current);
            pane = current;
        }
        else
        {
            current?.Children.Remove(anchorable);
            if (current is { Children.Count: 0 } && current.Parent is LayoutPanel oldBand)
                oldBand.Children.Remove(current);
            pane = new LayoutAnchorablePane(anchorable);
        }
        pane.DockWidth = new GridLength(width);
        pane.DockHeight = new GridLength(60);
        pane.SelectedContentIndex = -1;
        pane.SelectedContentIndex = 0;
        if (anchorable.ContentId is "main-toolbar" or "page-toolbar") pane.DockMinWidth = 20;
        if (side is AnchorSide.Top or AnchorSide.Bottom && toolbar)
        {
            var index = anchorable.ContentId == "main-toolbar" ? 0 :
                target.Children.Cast<ILayoutPanelElement>().TakeWhile(child =>
                    child is LayoutAnchorablePane existing &&
                    existing.Children.Any(item => item.ContentId == "main-toolbar")).Count();
            target.Children.Insert(index, pane);
        }
        else if (side == AnchorSide.Left) target.Children.Insert(0, pane);
        else target.Children.Add(pane);
        if (toolbarContent is not null) anchorable.Content = toolbarContent;
        if (toolbarContent is not null) anchorable.IsSelected = true;
    }

    internal AnchorSide? GetAvalonDockSide(LayoutAnchorablePane? pane)
    {
        if (pane is null) return null;
        if (ReferenceEquals(pane.Parent, _avalonTopToolbarBand)) return AnchorSide.Top;
        if (ReferenceEquals(pane.Parent, _avalonBottomToolbarBand)) return AnchorSide.Bottom;
        // A native edge drop can create another root-level band instead of using our
        // existing toolbar row. Resolve its side from the root layout, then merge it
        // into the shared band rather than leaving a splitter between toolbar rows.
        var root = _avalonDockPreviewManager?.Layout.RootPanel;
        if (root is not null && _avalonWorkspaceBand is not null)
        {
            ILayoutElement rootChild = pane;
            while (rootChild.Parent is not null && !ReferenceEquals(rootChild.Parent, root))
                rootChild = rootChild.Parent;
            if (ReferenceEquals(rootChild.Parent, root) &&
                rootChild is ILayoutPanelElement panelElement)
            {
                var workspaceIndex = root.Children.IndexOf(_avalonWorkspaceBand);
                var bandIndex = root.Children.IndexOf(panelElement);
                if (workspaceIndex >= 0 && bandIndex >= 0 && bandIndex != workspaceIndex)
                    return bandIndex < workspaceIndex ? AnchorSide.Top : AnchorSide.Bottom;
            }
        }
        if (_avalonWorkspaceBand is not null)
        {
            ILayoutElement element = pane;
            while (element.Parent is not null &&
                   !ReferenceEquals(element.Parent, _avalonWorkspaceBand))
                element = element.Parent;
            if (ReferenceEquals(element.Parent, _avalonWorkspaceBand))
            {
                var documentIndex = _avalonWorkspaceBand.Children
                    .ToList().FindIndex(child => child is LayoutDocumentPane);
                var paneIndex = _avalonWorkspaceBand.Children.IndexOf((ILayoutPanelElement)element);
                if (documentIndex >= 0 && paneIndex >= 0)
                    return paneIndex < documentIndex ? AnchorSide.Left : AnchorSide.Right;
            }
        }
        return pane.GetSide();
    }

    private LayoutPanel? GetToolbarBand(AnchorSide side)
    {
        if (_avalonDockPreviewManager is null) return null;
        var root = _avalonDockPreviewManager.Layout.RootPanel;
        var band = side == AnchorSide.Top ? _avalonTopToolbarBand : _avalonBottomToolbarBand;
        if (band is not null && ReferenceEquals(band.Parent, root)) return band;
        band = new LayoutPanel { Orientation = Orientation.Horizontal, DockHeight = new GridLength(60) };
        if (side == AnchorSide.Top)
        {
            root.Children.Insert(0, band);
            _avalonTopToolbarBand = band;
        }
        else
        {
            root.Children.Add(band);
            _avalonBottomToolbarBand = band;
        }
        return band;
    }

    private static AnchorSide ToAnchorSide(ToolbarDockSide side) => side switch
    {
        ToolbarDockSide.Left => AnchorSide.Left,
        ToolbarDockSide.Right => AnchorSide.Right,
        ToolbarDockSide.Bottom => AnchorSide.Bottom,
        _ => AnchorSide.Top,
    };

    private static void StackPanes(LayoutAnchorablePane first, LayoutAnchorablePane second)
    {
        if (ReferenceEquals(first.Parent, second.Parent) && first.Parent is LayoutAnchorablePaneGroup
            { Orientation: Orientation.Vertical }) return;
        if (first.Parent is not LayoutPanel panel) return;
        var index = panel.Children.IndexOf(first);
        if (index < 0) return;
        (second.Parent as LayoutPanel)?.Children.Remove(second);
        panel.Children.Remove(first);
        var group = new LayoutAnchorablePaneGroup { Orientation = Orientation.Vertical,
            DockWidth = first.DockWidth };
        group.Children.Add(first);
        group.Children.Add(second);
        panel.Children.Insert(index, group);
    }

    private void CaptureAvalonDockingSettings()
    {
        var manager = _avalonDockPreviewManager;
        if (_updatingAvalonSettings || manager is null || !manager.IsLoaded ||
            _avalonNavigation is null || _avalonBookmarks is null || _avalonProperties is null ||
            _avalonMainToolbar is null || _avalonPageToolbar is null) return;
        // AvalonDock detaches an anchorable before setting IsFloating. Capturing that
        // transient state writes settings and immediately docks the in-flight toolbar.
        if (!ToolbarHasSettled(_avalonMainToolbar) ||
            !ToolbarHasSettled(_avalonPageToolbar)) return;

        // Native drops can tab a toolbar into a navigation/properties pane. Its tab is
        // intentionally hidden by the compact toolbar styling, so leaving it there
        // makes the complete strip disappear. Give each toolbar its own pane first.
        if (RestoreDedicatedToolbarPanes())
        {
            _normalizeRestoredToolbarLayout = true;
            PruneVacantToolbarBands();
            QueueToolbarDropCapture();
            return;
        }
        UpdateAvalonToolbarOrientation(manager);

        WorkspaceDockPosition PaneSide(LayoutAnchorable pane, WorkspaceDockPosition fallback) =>
            pane.IsFloating ? WorkspaceDockPosition.Floating :
            GetAvalonDockSide(pane.Parent as LayoutAnchorablePane) switch
            {
                AnchorSide.Left => WorkspaceDockPosition.Left,
                AnchorSide.Right => WorkspaceDockPosition.Right,
                _ => fallback,
            };
        var current = ViewModel.CurrentApplicationSettings;
        var navigation = PaneSide(_avalonNavigation, current.NavigationDockPosition);
        var bookmarks = PaneSide(_avalonBookmarks, current.BookmarkDockPosition);
        var properties = PaneSide(_avalonProperties, current.PropertiesDockPosition);
        var group = navigation == properties && navigation != WorkspaceDockPosition.Floating
            ? ReferenceEquals(_avalonNavigation.Parent, _avalonProperties.Parent)
                ? WorkspacePaneGroupMode.Tabbed : WorkspacePaneGroupMode.Stacked
            : current.PaneGroupMode;
        _updatingAvalonSettings = true;
        try
        {
            ViewModel.SetWorkspaceDockPositions(navigation, properties, group);
            ViewModel.SetBookmarkDockPosition(bookmarks);
            ViewModel.SetToolbarLayout(_mainToolbarSide, 0, 0, _pageToolbarSide, 0, 0);
            static double Width(LayoutAnchorable pane, double fallback) =>
                pane.Parent is LayoutAnchorablePane { DockWidth: { IsAbsolute: true, Value: > 0 } width }
                    ? width.Value : fallback;
            ViewModel.SetWorkspacePaneWidths(Width(_avalonNavigation, current.PageListWidth),
                Width(_avalonProperties, current.PropertiesPanelWidth));
            CaptureAvalonPaneVisibility();
            if (!_restoringNativeWorkspace) ViewModel.SetWorkspaceLayoutXml(CaptureNativeWorkspace());
            _lastAppliedDockingSettings = ViewModel.CurrentApplicationSettings;
        }
        finally { _updatingAvalonSettings = false; }
        if (_normalizeRestoredToolbarLayout)
        {
            _normalizeRestoredToolbarLayout = false;
            Dispatcher.BeginInvoke(SynchronizeAvalonDockLayout,
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private bool RestoreDedicatedToolbarPanes()
    {
        if (_avalonMainToolbar is null || _avalonPageToolbar is null) return false;
        var moved = false;
        _updatingAvalonSettings = true;
        _avalonLayoutGeneration++;
        try
        {
            moved = RestoreCanonicalToolbarBands();
            foreach (var toolbar in new[] { _avalonMainToolbar, _avalonPageToolbar })
            {
                if (toolbar.IsFloating || toolbar.Parent is not LayoutAnchorablePane pane) continue;
                if (!IsInDockedRoot(pane)) continue;
                var side = GetAvalonDockSide(pane);
                if (side is null) continue;
                var target = side is AnchorSide.Top or AnchorSide.Bottom
                    ? GetToolbarBand(side.Value) : _avalonWorkspaceBand;
                if (ReferenceEquals(pane.Parent, target) && IsToolbarOnlyPane(pane) &&
                    pane.Children.Count == 1) continue;
                PlaceAtSide(toolbar, side.Value, 64);
                moved = true;
            }
        }
        finally
        {
            _avalonLayoutGeneration++;
            _updatingAvalonSettings = false;
        }
        if (moved && _avalonDockPreviewManager is { } manager)
        {
            PruneVacantWorkspacePanes();
            _toolbarChromeNeedsRefresh = true;
            ApplyAvalonTopTabStyle(manager);
            UpdateAvalonToolbarOrientation(manager);
            RefreshAvalonToolbarChrome(manager);
        }
        return moved;
    }

    private bool RestoreCanonicalToolbarBands()
    {
        var root = _avalonDockPreviewManager?.Layout.RootPanel;
        if (root is null || _avalonWorkspaceBand is null) return false;
        var workspaceIndex = root.Children.IndexOf(_avalonWorkspaceBand);
        if (workspaceIndex < 0) return false;
        var moved = false;
        foreach (var (band, side) in new[]
                 {
                     (_avalonTopToolbarBand, AnchorSide.Top),
                     (_avalonBottomToolbarBand, AnchorSide.Bottom),
                 })
        {
            if (band is null || band.Children.Count == 0 || root.Children.Contains(band) ||
                band.Parent is not LayoutPanel parent || !IsInDockedRoot(band) ||
                !IsToolbarOnlyLayout(band)) continue;
            parent.Children.Remove(band);
            workspaceIndex = root.Children.IndexOf(_avalonWorkspaceBand);
            root.Children.Insert(side == AnchorSide.Top ? workspaceIndex : workspaceIndex + 1, band);
            moved = true;
        }
        return moved;
    }

    private bool IsInDockedRoot(ILayoutElement element)
    {
        var root = _avalonDockPreviewManager?.Layout.RootPanel;
        while (element.Parent is not null)
        {
            if (ReferenceEquals(element.Parent, root)) return true;
            element = element.Parent;
        }
        return false;
    }

    private bool IsSettledDockedToolbar(LayoutAnchorable toolbar) =>
        !toolbar.IsFloating && toolbar.Parent is LayoutAnchorablePane pane &&
        !pane.IsDirectlyHostedInFloatingWindow && IsInDockedRoot(pane);
}
