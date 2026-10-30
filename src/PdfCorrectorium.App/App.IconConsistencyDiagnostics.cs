using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.Resources;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    private static void CheckDialogFooterIcons(Window host, Action<bool, string> check)
    {
        var footer = host.FindName("DialogFooter") as FrameworkElement;
        check(footer is not null, host.GetType().Name + " identifies its bottom dialog action area.");
        var buttons = KeyboardElements(footer!).OfType<System.Windows.Controls.Primitives.ButtonBase>().ToArray();
        check(buttons.Length > 0 && buttons.All(button =>
                button.Content is string && string.IsNullOrEmpty(ActionIcon.GetKey(button)) &&
                !IconVisuals(button).OfType<ActionIcon>().Any()),
            host.GetType().Name + " footer buttons use plain captions without icons, including disabled/hidden actions.");
    }

    private async Task RunIconConsistencyChecksAsync(MainWindow window, MainWindowViewModel vm,
        string directory, Action<bool, string> check, Func<Task> layout, Action<string> snapshot)
    {
        foreach (var property in window.PropertyPanes.Values) { property.IsSelected = true; await layout(); }
        window.PropertyPanes["link-properties"].IsSelected = true; await layout();
        var menu = IconVisuals(window).OfType<Menu>().Single();
        var entries = MenuEntries(menu.Items).ToArray();
        var keys = entries.Select(ActionIcon.GetKey).Where(key => key.Length > 0).Distinct().ToArray();
        check(entries.Count(item => ActionIcon.GetKey(item).Length > 0) >= 30 &&
            entries.Where(item => ActionIcon.GetKey(item).Length > 0)
                .All(item => item.Icon is ActionIcon icon && icon.Key == ActionIcon.GetKey(item)),
            "Menus instantiate independent shared icons for file, page, bookmark, OCR and link actions.");
        foreach (var key in new[] { "OpenPdf", "OpenProject", "SaveProject", "ExportPdf", "ImportOcr",
            "PageInsert", "PageDelete", "PageRotateLeft", "PageRotateRight", "BookmarkAdd", "BookmarkRemove", "LinkVisibility", "Link" })
            check(keys.Contains(key), "The menu has the toolbar-equivalent icon: " + key);
        foreach (var (id, key) in new[] { ("main.linkOverlay", "LinkVisibility"), ("main.linkMode", "Link") })
        {
            var control = window.CustomizableToolbars().SelectMany(toolbar => toolbar.Items.OfType<FrameworkElement>())
                .Single(item => ToolbarItem.GetKey(item) == id);
            check(IconVisuals(control).OfType<ActionIcon>().Single().Key == key,
                "Toolbar link actions no longer render legacy inline paths: " + id);
        }
        var characterNavigation = (ToolBar)window.FindName("CharacterNavigationToolBar");
        check(characterNavigation.Items.OfType<Button>().Select(button => (button.Content as ActionIcon)?.Key)
            .SequenceEqual(new[] { "PreviousPage", "NextPage", "Minus", "Plus" }),
            "OCR character navigation/advance controls render vector icons instead of font arrows and signs.");
        var sharedCaptionIcons = IconVisuals(window).Concat(window.PropertyPaneViews.SelectMany(IconVisuals))
            .OfType<Button>().Distinct().Where(button => ActionIcon.GetKey(button).Length > 0).ToArray();
        check(sharedCaptionIcons.Length >= 20 && sharedCaptionIcons.All(button =>
                button.ContentTemplate == window.FindResource("ActionIconLabelTemplate") && button.Content is string),
            "Added property operations use the shared icon/caption template and keep their readable labels.");

        var panel = (StackPanel)window.FindName("LinkEditingPanel");
        var alignments = IconVisuals(panel).OfType<Button>().Where(button => button.Tag is string tag &&
            new[] { "Left", "CenterX", "Right", "Top", "CenterY", "Bottom", "Width", "Height", "Size" }.Contains(tag)).ToArray();
        check(alignments.Length == 9 && alignments.All(button =>
            button.Style == window.FindResource("PropertyIconButtonStyle") &&
            button.Content is ActionIcon { Width: 24, Height: 24 } &&
            !string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)) &&
            button.ToolTip is not null),
            "All nine link alignment actions use the OCR property icon style, with names and tooltips.");
        check(alignments.Select(button => (string)button.Tag).SequenceEqual(
            new[] { "Size", "Width", "Height", "Left", "CenterX", "Right", "Top", "CenterY", "Bottom" }),
            "Size controls precede the same left/center/right and top/center/bottom rows as OCR.");
        var ocrAlignmentPanel = (System.Windows.Controls.Primitives.UniformGrid)window.FindName("AlignmentIconPanel");
        foreach (var (command, key) in new[] { (vm.EqualWidthCommand, "EqualWidth"), (vm.EqualHeightCommand, "EqualHeight") })
        {
            var button = ocrAlignmentPanel.Children.OfType<Button>().Single(item => ReferenceEquals(item.Command, command));
            check(button.Content is ActionIcon { Width: 24, Height: 24 } icon && icon.Key == key &&
                button.Style == window.FindResource("PropertyIconButtonStyle"),
                "OCR and link size actions share the fixed 24-unit renderer and 32px hit target: " + key);
            var geometry = (Geometry)window.FindResource("IconGeometry." + key);
            var pen = new Pen(Brushes.Black, 1.8)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var ink = geometry.GetWidenedPathGeometry(pen);
            var width = key == "EqualWidth";
            Point DirectionPoint(double across, double along) => width ? new Point(across, along) : new Point(along, across);
            check(ink.FillContains(DirectionPoint(12, 3)) && ink.FillContains(DirectionPoint(12, 9)) &&
                ink.FillContains(DirectionPoint(12, 17.5)) && ink.FillContains(DirectionPoint(4, 17.5)) &&
                ink.FillContains(DirectionPoint(20, 17.5)),
                "Size icon retains its frame and both ends of the directional arrow: " + key);
            check(Enumerable.Range(0, 25).All(coordinate => !ink.FillContains(DirectionPoint(coordinate, 11.5))) &&
                !ink.FillContains(DirectionPoint(3, 12)) && !ink.FillContains(DirectionPoint(21, 12)) &&
                ink.Bounds.Left >= 0 && ink.Bounds.Top >= 0 && ink.Bounds.Right <= 24 && ink.Bounds.Bottom <= 24,
                "Size icon has transparent frame/arrow separation, no outer guide lines and no clipping: " + key);
        }
        var sizeSheet = new StackPanel { Background = Brushes.White, Width = 520 };
        foreach (var key in new[] { "EqualWidth", "EqualHeight" })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            row.Children.Add(new TextBlock { Text = key, Width = 150, VerticalAlignment = VerticalAlignment.Center });
            foreach (var size in new[] { 18d, 22d, 24d, 88d })
                row.Children.Add(new ActionIcon { Key = key, Width = size, Height = size,
                    Foreground = new SolidColorBrush(Color.FromRgb(38, 55, 70)), Margin = new Thickness(8, 0, 8, 0) });
            sizeSheet.Children.Add(row);
        }
        sizeSheet.Measure(new Size(520, double.PositiveInfinity));
        sizeSheet.Arrange(new Rect(new Point(), sizeSheet.DesiredSize)); sizeSheet.UpdateLayout();
        SaveIconDiagnosticImage(sizeSheet, Path.Combine(directory, "12-approved-size-icons.png"));
        snapshot("07-link-icons-and-alignment");
        var previousWidth = panel.Width;
        try
        {
            panel.Width = 210; await layout();
            check(alignments.All(button => button.ActualWidth == 32 && button.ActualHeight == 32 &&
                IconVisuals(button).OfType<ActionIcon>().All(icon => icon.ActualWidth == 24 && icon.ActualHeight == 24)),
                "A narrow property panel keeps every 24px alignment icon and 32px hit target intact.");
            ((ScrollViewer)window.FindName("LinkPropertiesScroll")).ScrollToBottom(); await layout();
            snapshot("11-narrow-alignment-icons");
        }
        finally
        {
            panel.Width = previousWidth;
            ((ScrollViewer)window.FindName("LinkPropertiesScroll")).ScrollToTop(); await layout();
        }
        await RunPropertyPaneConsistencyChecksAsync(window, vm, check, layout, snapshot, directory);

        var oldMode = vm.EditorMode;
        var oldSelection = vm.SelectedLinkArea;
        try
        {
            var properties = (ScrollViewer)window.FindName("KeyboardPropertiesPane");
            var groups = (StackPanel)properties.Content;
            foreach (var mode in new[] { EditorInteractionMode.OcrEditing, EditorInteractionMode.LinkEditing, EditorInteractionMode.Redaction })
            {
                vm.EditorMode = mode; await layout();
                var separators = groups.Children.OfType<Separator>().Where(separator => separator.IsVisible).ToArray();
                check(separators.Length == (mode == EditorInteractionMode.OcrEditing ? 1 : 0),
                    "Only the active OCR pane renders its section separators: " + mode);
                if (mode == EditorInteractionMode.OcrEditing) snapshot("08-ocr-property-single-divider");
            }
        }
        finally { vm.EditorMode = oldMode; vm.SelectedLinkArea = oldSelection; await layout(); }

        // Render actual menu rows (including a disabled row) so Icon presentation is verified,
        // not only the attached resource IDs. No popup needs to steal the user's focus.
        var fileMenu = new ContextMenu { StaysOpen = true };
        foreach (var original in ((MenuItem)menu.Items[0]).Items.OfType<MenuItem>().Where(item => ActionIcon.GetKey(item).Length > 0))
        {
            var row = new MenuItem { Header = original.Header, InputGestureText = original.InputGestureText, IsEnabled = original.IsEnabled };
            ActionIcon.SetKey(row, ActionIcon.GetKey(original)); fileMenu.Items.Add(row);
        }
        var menuHost = new Border { Width = 420, ContextMenu = fileMenu };
        var host = new Window { Content = menuHost, Width = 450, Height = 420, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false };
        try
        {
            host.Show(); host.UpdateLayout(); await layout();
            fileMenu.PlacementTarget = menuHost; fileMenu.IsOpen = true;
            await Task.Delay(100); await layout();
            var rowParent = VisualTreeHelper.GetParent((MenuItem)fileMenu.Items[0]) as FrameworkElement;
            check(rowParent is not null && rowParent.ActualWidth > 0 &&
                IconVisuals(rowParent).OfType<ActionIcon>().Count() == fileMenu.Items.Count,
                $"Actual WPF file-menu rows render the icon column, including disabled actions (open={fileMenu.IsOpen}, parent={rowParent?.GetType().Name}, width={rowParent?.ActualWidth}, icons={(rowParent is null ? 0 : IconVisuals(rowParent).OfType<ActionIcon>().Count())}).");
            SaveIconDiagnosticImage(rowParent!, Path.Combine(directory, "09-file-menu-icons.png"));
        }
        finally { fileMenu.IsOpen = false; host.Close(); }

        string[] additionalKeys = ["Reset", "Apply", "Deselect", "SelectAll", "ReviewList", "Replace", "Keep", "Comments", "Thumbnail", "Eyedropper", "Info", "DocumentProperties", "Restore", "Redo", "Warning", "Undo", "Validate", "Edit", "Exit", "Settings", "Help", "MultiCharacterInput", "Propagate", "Unreviewed", "Excluded", "Analysis", "Search", "Stop", "Pending", "CommentsAdd", "PageExtract", "PdfSplit", "PageNumber", "Header", "Footer", "Decoration", "TextWatermark", "HorizontalWriting", "VerticalWriting", "ImageOptimize", "OpenFolder"];
        foreach (var key in additionalKeys)
        {
            var geometry = (Geometry)window.FindResource("IconGeometry." + key);
            check(!geometry.Bounds.IsEmpty && geometry.Bounds.Left >= -1 && geometry.Bounds.Top >= -1 &&
                geometry.Bounds.Right <= 25 && geometry.Bounds.Bottom <= 25,
                "Additional operation geometry stays within the shared canvas: " + key);
        }
        var sheet = new StackPanel { Background = Brushes.White, Width = 640 };
        foreach (var key in keys.Concat(alignments.Select(button => ((ActionIcon)button.Content).Key)).Concat(additionalKeys).Distinct())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
            row.Children.Add(new TextBlock { Text = key, Width = 210, VerticalAlignment = VerticalAlignment.Center });
            foreach (var size in new[] { 18d, 22d, 24d })
                row.Children.Add(new ActionIcon { Key = key, Width = size, Height = size, Margin = new Thickness(10, 0, 10, 0) });
            row.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(255, 225, 225)),
                Child = new ActionIcon { Key = key, Width = 22, Height = 22, Margin = new Thickness(8, 0, 8, 0) } });
            row.Children.Add(new ActionIcon { Key = key, Width = 22, Height = 22, IsEnabled = false, Margin = new Thickness(10, 0, 10, 0) });
            sheet.Children.Add(row);
        }
        sheet.Measure(new Size(640, double.PositiveInfinity)); sheet.Arrange(new Rect(new Point(), sheet.DesiredSize)); sheet.UpdateLayout();
        check(IconVisuals(sheet).OfType<ActionIcon>().All(icon => Math.Abs(icon.ActualWidth - icon.Width) < .01 &&
            Math.Abs(icon.ActualHeight - icon.Height) < .01),
            "Small icons measure to their requested size instead of being cropped from a 24px drawing.");
        SaveIconDiagnosticImage(sheet, Path.Combine(directory, "10-icon-sizes-and-states.png"));
        check(sheet.ActualHeight > 0, "Shared icons render at 18/22/24 px and on selected/disabled backgrounds.");
    }

    private static IEnumerable<MenuItem> MenuEntries(ItemCollection items)
    {
        foreach (var menu in items.OfType<MenuItem>())
        {
            yield return menu;
            foreach (var child in MenuEntries(menu.Items)) yield return child;
        }
    }
    private static IEnumerable<DependencyObject> IconVisuals(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in IconVisuals(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void SaveIconDiagnosticImage(FrameworkElement root, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            context.DrawRectangle(Brushes.White, null, bounds);
            context.DrawRectangle(new VisualBrush(root), null, bounds);
        }
        bitmap.Render(drawing);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
}
