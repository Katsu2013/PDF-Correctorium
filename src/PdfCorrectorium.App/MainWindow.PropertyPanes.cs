using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using PdfCorrectorium.App.Resources;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private readonly Dictionary<string, LayoutAnchorable> _propertyPanes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> _propertyViews = new(StringComparer.Ordinal);
    private ItemsControl? _commentOverview;
    private TextBlock? _commentTargetDescription;
    private bool _restoringNativeWorkspace;
    private int _appliedWorkspaceRestoreVersion;
    internal IEnumerable<FrameworkElement> PropertyPaneViews => _propertyViews.Values;
    internal IReadOnlyDictionary<string, LayoutAnchorable> PropertyPanes => _propertyPanes;

    /// <summary>
    /// Transfer the four named, existing XAML sections once. Their namescope, bindings,
    /// selection models and event handlers stay intact; no document or visual scan runs on tab changes.
    /// </summary>
    private void InitializePropertyPanes()
    {
        _propertyViews.Add("properties", PropertiesDockPane);
        OcrPropertyContent.Children.Remove(LinkEditingPanel);
        OcrPropertyContent.Children.Remove(RedactionPanel);
        OcrPropertyContent.Children.Remove(AnnotationPropertiesContent);
        OcrPropertyContent.Children.Remove(GlobalInputPdfWarning);
        foreach (var separator in OcrPropertyContent.Children.OfType<Separator>()
                     .Where(separator => !ReferenceEquals(separator, PropertiesPaneTopSeparator)).ToArray())
            OcrPropertyContent.Children.Remove(separator);
        // Actionable errors/warnings cannot disappear behind an inactive pane.
        // Routine document-loading progress belongs to the status bar, not this host.
        var dock = (DockPanel)PropertiesDockPane.Child;
        var loading = DocumentLoadingErrorNotice;
        dock.Children.Remove(loading);
        var notices = new StackPanel();
        loading.Margin = new Thickness(8, 3, 8, 3);
        GlobalInputPdfWarning.Margin = new Thickness(8, 0, 8, 3);
        notices.Children.Add(loading); notices.Children.Add(GlobalInputPdfWarning);
        DockPanel.SetDock(notices, Dock.Top);
        MainWorkspaceDock.Children.Insert(MainWorkspaceDock.Children.IndexOf(WorkspaceLayoutGrid), notices);

        PropertiesPaneTitle.ClearValue(TextBlock.TextProperty);
        PropertiesPaneTitle.Text = LocalizationService.Translate("OCR・校正");
        PropertiesPaneSummaryText.SetBinding(TextBlock.TextProperty,
            new Binding("OverlayItems.Count") { StringFormat = LocalizationService.Translate("文字領域: {0}件") });
        InitializeOcrPropertyPresentation();
        var ocrMode = ModeButton("OCR編集へ切り替える", () => ViewModel.EditorMode = EditorInteractionMode.OcrEditing);
        ocrMode.SetBinding(VisibilityProperty, new Binding(nameof(MainWindowViewModel.ShowOcrEditModeAction))
            { Converter = new BooleanToVisibilityConverter() });
        OcrPropertyContent.Children.Insert(OcrPropertyContent.Children.IndexOf(PropertiesPaneTopSeparator), ocrMode);
        var quickLink = new Button { Content = "選択文字にリンクを設定(_K)", Margin = new Thickness(0, 4, 0, 0),
            Style = (Style)FindResource("PropertyTextActionButtonStyle"), ToolTip = "選択したOCR文字からリンクを設定" };
        ActionIcon.SetKey(quickLink, "Link");
        quickLink.SetBinding(IsEnabledProperty, new Binding(nameof(MainWindowViewModel.CanEditLinkSelection)));
        quickLink.Click += InternalLinkEdit_OnClick;
        OcrPropertyContent.Children.Insert(OcrPropertyContent.Children.IndexOf(PropertiesPaneTopSeparator), quickLink);
        _propertyViews.Add("link-properties", PropertyView("LinkPropertiesScroll", "リンク", LinkEditingPanel,
            ModeButton("リンク編集へ切り替える", () => ViewModel.ActivateLinkEditMode())));
        _propertyViews.Add("redaction-properties", PropertyView("RedactionPropertiesScroll", "墨消し", RedactionPanel,
            ModeButton("墨消し編集へ切り替える", () => ViewModel.ActivateRedactionModeCommand.Execute(null))));
        AnnotationPropertiesContent.Children.Remove(AnnotationLinkActionPanel);
        foreach (var text in AnnotationPropertiesContent.Children.OfType<TextBlock>())
            text.Text = text.Style == FindResource("PaneTitleStyle") ? "コメント・タグ" : "現在の選択対象のコメントとタグを確認・編集できます。";
        _commentTargetDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        var commentList = new ListBox { MaxHeight = 300, MinHeight = 40,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        VirtualizingStackPanel.SetIsVirtualizing(commentList, true);
        VirtualizingStackPanel.SetVirtualizationMode(commentList, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(commentList, true);
        _commentOverview = commentList;
        AnnotationPropertiesContent.Children.Add(_commentTargetDescription);
        AnnotationPropertiesContent.Children.Add(_commentOverview);
        _propertyViews.Add("comment-properties", PropertyView("CommentPropertiesScroll", "コメント・タグ", AnnotationPropertiesContent));
        InitializeReadAloudDock();
        _propertyPanes.Add("properties", _avalonProperties!);
        foreach (var (id, title) in new[] { ("link-properties", "リンク"), ("redaction-properties", "墨消し"), ("comment-properties", "コメント・タグ"), ("read-aloud", "読み上げ") })
            _propertyPanes.Add(id, new LayoutAnchorable { ContentId = id, Title = LocalizationService.Translate(title),
                Content = _propertyViews[id], CanClose = false, CanHide = true, CanAutoHide = true });
        _avalonProperties!.Title = LocalizationService.Translate("OCR・校正");
        foreach (var pane in _propertyPanes.Values)
            pane.PropertyChanged += (_, args) =>
            {
                if (pane.ContentId == "comment-properties" && args.PropertyName == nameof(LayoutAnchorable.IsSelected) && pane.IsSelected)
                    RefreshCommentOverview();
            };
        RefreshPropertyPaneLocalization();
    }

    private Button ModeButton(string caption, Action activate)
    {
        var button = new Button { Content = caption, Margin = new Thickness(0, 4, 0, 8),
            Style = (Style)FindResource("PropertyTextActionButtonStyle"), ToolTip = caption };
        ActionIcon.SetKey(button, "Edit");
        // The global mode selector already has Alt+M. Do not register another
        // global mnemonic when users dock several independent panels together.
        button.SetBinding(IsEnabledProperty, new Binding(nameof(MainWindowViewModel.CanEditDocument)));
        button.Click += (_, _) => { CommitPendingEditorBindings(); activate(); };
        return button;
    }

    private Border PropertyView(string scrollName, string title, FrameworkElement content, Button? mode = null)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var help = new ToggleButton { Content = "説明", ToolTip = "操作説明の表示／非表示", Padding = new Thickness(7, 3, 7, 3) };
        ActionIcon.SetKey(help, "Help");
        help.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(MainWindowViewModel.ShowPropertyHelpText)) { Mode = BindingMode.TwoWay });
        DockPanel.SetDock(help, Dock.Right); header.Children.Add(help);
        header.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("PaneTitleStyle") });
        var stack = new StackPanel(); stack.Children.Add(header);
        if (mode is not null) stack.Children.Add(mode);
        stack.Children.Add(content);
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(12, 8, 12, 8), Focusable = false };
        scroll.SetBinding(IsEnabledProperty, new Binding(nameof(MainWindowViewModel.CanEditDocument)));
        RegisterName(scrollName, scroll);
        var panel = new Border { Child = scroll, DataContext = ViewModel };
        panel.SetResourceReference(BackgroundProperty, "InputBrush");
        return panel;
    }

    private void ActivateModePropertyPane(bool revealHidden = true)
    {
        if (_restoringNativeWorkspace) return;
        var id = ViewModel.EditorMode switch
        {
            EditorInteractionMode.LinkEditing => "link-properties",
            EditorInteractionMode.Redaction => "redaction-properties",
            _ => "properties",
        };
        if (!_propertyPanes.TryGetValue(id, out var pane)) return;
        // IsSelected reveals the corresponding tab without stealing keyboard focus or redocking.
        if (!revealHidden && !pane.IsVisible) return;
        if (!pane.IsVisible) SetAvalonPaneVisible(pane, true);
        pane.IsSelected = true;
    }

    private void RefreshCommentOverview()
    {
        if (_commentOverview is null || !_propertyPanes.TryGetValue("comment-properties", out var pane) || !pane.IsSelected) return;
        var target = ViewModel.GetCurrentCommentTarget();
        _commentTargetDescription!.Text = LocalizationService.Translate(target.Kind switch
        {
            ProjectTargetKind.OcrRegion => "対象: 選択したOCR文字・領域",
            ProjectTargetKind.Page => "対象: 現在のページ",
            _ => "対象: 文書全体",
        });
        var tags = ViewModel.GetTags().ToDictionary(tag => tag.Id, tag => tag.Name);
        _commentOverview.ItemsSource = ViewModel.GetComments().Where(comment => comment.Target == target)
            .Select(comment => comment.Body + (comment.TagIds.Count == 0 ? "" : "\n" +
                    string.Join(" / ", comment.TagIds.Where(tags.ContainsKey).Select(id => tags[id])))).ToArray();
    }

    private void PropertyPanesMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu) return;
        menu.Items.Clear();
        foreach (var pane in _propertyPanes.Values)
        {
            var row = new MenuItem { Header = pane.Title, IsCheckable = true, IsChecked = !pane.IsHidden };
            row.Click += (_, args) => { if (!ReferenceEquals(args.OriginalSource, row)) return; SetAvalonPaneVisible(pane, row.IsChecked); CaptureAvalonDockingSettings(); };
            menu.Items.Add(row);
        }
        menu.Items.Add(new Separator());
        var displayModes = new MenuItem { Header = LocalizationService.Translate("固定／自動収納") };
        menu.Items.Add(displayModes);
        foreach (var pane in _propertyPanes.Values)
        {
            var row = new MenuItem { Header = pane.Title };
            displayModes.Items.Add(row);
            foreach (var (caption, autoHide) in new[] { ("このパネルを固定", false), ("同じ側をまとめて収納", true) })
            {
                var mode = new MenuItem { Header = LocalizationService.Translate(caption), IsCheckable = true,
                    IsChecked = !pane.IsHidden && pane.IsAutoHidden == autoHide };
                mode.Click += (_, args) => { SetPaneAutoHide(pane, autoHide); args.Handled = true; };
                row.Items.Add(mode);
            }
        }
    }

    private async void ResetPropertyWorkspace_OnClick(object sender, RoutedEventArgs e) => await ResetPropertyWorkspaceAsync();

    internal async Task ResetPropertyWorkspaceAsync()
    {
        var settings = ViewModel.CurrentApplicationSettings with { WorkspaceLayoutXml = null,
            NavigationDockPosition = WorkspaceDockPosition.Left, BookmarkDockPosition = WorkspaceDockPosition.Left,
            PropertiesDockPosition = WorkspaceDockPosition.Right, PaneGroupMode = WorkspacePaneGroupMode.Tabbed,
            MainToolbarDockSide = ToolbarDockSide.Top, PageToolbarDockSide = ToolbarDockSide.Top,
            ShowPageListPanel = true, ShowBookmarksPanel = true, ShowPropertiesPanel = true };
        if (!await ViewModel.ApplyApplicationSettingsAsync(settings)) return;
        SynchronizeAvalonDockLayout();
        _updatingAvalonSettings = true;
        try
        {
            PositionPane(_avalonNavigation!, WorkspaceDockPosition.Left, settings.PageListWidth, null);
            PositionPane(_avalonBookmarks!, WorkspaceDockPosition.Left, settings.PageListWidth,
                _avalonNavigation!.Parent as LayoutAnchorablePane);
            PositionPane(_avalonProperties!, WorkspaceDockPosition.Right, settings.PropertiesPanelWidth, null);
            var target = _avalonProperties!.Parent as LayoutAnchorablePane;
            foreach (var pane in _propertyPanes.Values.Where(pane => !ReferenceEquals(pane, _avalonProperties)))
                PositionPane(pane, WorkspaceDockPosition.Right, settings.PropertiesPanelWidth, target);
            PruneVacantWorkspacePanes();
            ActivateModePropertyPane();
        }
        finally { _updatingAvalonSettings = false; }
        CaptureAvalonDockingSettings();
    }

    internal string CaptureNativeWorkspace()
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        new XmlLayoutSerializer(_avalonDockPreviewManager!).Serialize(writer);
        var xml = XElement.Parse(writer.ToString());
        foreach (var side in new[] { AnchorSide.Left, AnchorSide.Right })
        {
            xml.SetAttributeValue(SideAutoHideAttribute(side), _sideAutoHideModes[side]);
            xml.SetAttributeValue(FixedSideAttribute(side), string.Join(',', _fixedSidePanes.Where(pair => pair.Value == side)
                .Select(pair => pair.Key.ContentId).OrderBy(id => id, StringComparer.Ordinal)));
        }
        return xml.ToString(SaveOptions.DisableFormatting);
    }

    internal bool RestoreNativeWorkspace(string? xml)
    {
        xml = WorkspaceDockLayout.Normalize(xml);
        if (xml is null || _avalonDockPreviewManager is not { } manager) return false;
        var fallback = manager.Layout;
        var views = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["navigation"] = NavigationDockPane, ["bookmarks"] = BookmarkDockPane, ["editor"] = DocumentWorkspacePane,
            ["main-toolbar"] = _avalonMainToolbarTray!, ["page-toolbar"] = _avalonPageToolbarTray!,
        };
        foreach (var pair in _propertyViews) views.Add(pair.Key, pair.Value);
        _restoringNativeWorkspace = _updatingAvalonSettings = true;
        _avalonLayoutGeneration++;
        try
        {
            var serializer = new XmlLayoutSerializer(manager);
            serializer.LayoutSerializationCallback += (_, args) =>
            {
                if (views.TryGetValue(args.Model.ContentId, out var content)) args.Content = content;
                else args.Cancel = true;
            };
            using var reader = WorkspaceDockLayout.Reader(xml);
            serializer.Deserialize(reader);
            var anchors = manager.Layout.Descendents().OfType<LayoutAnchorable>().ToDictionary(pane => pane.ContentId);
            // Validate the complete native tree before replacing our model references.
            foreach (var id in WorkspaceDockLayout.ContentIds.Where(id => id is not "editor" and not "read-aloud")) _ = anchors[id];
            var workspace = manager.Layout.RootPanel.Children.OfType<LayoutPanel>()
                .Single(panel => panel.Descendents().OfType<LayoutDocument>().Any(document => document.ContentId == "editor"));
            _avalonNavigation = anchors["navigation"]; _avalonBookmarks = anchors["bookmarks"];
            _avalonProperties = anchors["properties"]; _avalonMainToolbar = anchors["main-toolbar"]; _avalonPageToolbar = anchors["page-toolbar"];
            _avalonWorkspaceBand = workspace;
            if (!anchors.ContainsKey("read-aloud"))
            {
                var reading = new LayoutAnchorable { ContentId = "read-aloud", Title = LocalizationService.Translate("読み上げ"),
                    Content = _readAloudHost, CanClose = false, CanHide = true, CanAutoHide = true };
                PlaceAtSide(reading, AnchorSide.Right, 320, requireOwnPane: true); reading.Hide();
                anchors.Add("read-aloud", reading);
            }
            foreach (var id in _propertyPanes.Keys.ToArray()) _propertyPanes[id] = anchors[id];
            var bands = manager.Layout.RootPanel.Children.OfType<LayoutPanel>().ToArray();
            var workspaceIndex = Array.IndexOf(bands, _avalonWorkspaceBand);
            _avalonTopToolbarBand = bands.Take(workspaceIndex).FirstOrDefault();
            _avalonBottomToolbarBand = bands.Skip(workspaceIndex + 1).FirstOrDefault();
            _sizedFloatingToolbarWindows.Clear();
            _toolbarChromeNeedsRefresh = true;
            AttachAvalonToolbarEvents();
            AttachAvalonPaneVisibilityEvents();
            RestoreSideAutoHideModes(XElement.Parse(xml));
            RefreshPropertyPaneLocalization();
            foreach (var pane in _propertyPanes.Values)
                pane.PropertyChanged += (_, args) =>
                {
                    if (pane.ContentId == "comment-properties" && args.PropertyName == nameof(LayoutAnchorable.IsSelected) && pane.IsSelected)
                        RefreshCommentOverview();
                };
            // Native root bands are retained; toolbar normalization discovers their current sides.
            CaptureAvalonPaneVisibility();
            _lastAppliedDockingSettings = ViewModel.CurrentApplicationSettings;
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or XmlException or KeyNotFoundException or ArgumentException)
        {
            manager.Layout = fallback;
            ViewModel.ReportWorkspaceRestoreFailure();
            return false;
        }
        finally { _restoringNativeWorkspace = _updatingAvalonSettings = false; _avalonLayoutGeneration++; }
    }

    private void RefreshPropertyPaneLocalization()
    {
        foreach (var view in _propertyViews.Values) LocalizationService.Apply(view);
        foreach (var (id, title) in new[] { ("properties", "OCR・校正"), ("link-properties", "リンク"),
                     ("redaction-properties", "墨消し"), ("comment-properties", "コメント・タグ"), ("read-aloud", "読み上げ") })
            _propertyPanes[id].Title = LocalizationService.Translate(title);
        PropertiesPaneTitle.Text = LocalizationService.Translate("OCR・校正");
        RefreshCommentOverview();
    }
}
