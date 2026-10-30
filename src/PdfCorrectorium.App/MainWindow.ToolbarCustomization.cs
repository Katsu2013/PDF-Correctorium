using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.Resources;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private string[]? _appliedHiddenToolbarIds;
    private void InitializeToolbarCustomization()
    {
        foreach (var toolbar in CustomizableToolbars())
        {
            var menu = new ContextMenu();
            menu.Opened += (_, _) => PopulateToolbarMenu(menu.Items, toolbar);
            toolbar.ContextMenu = menu;
            if (toolbar.Parent is ToolBarTray tray) tray.ContextMenu = menu;
        }
        ApplyToolbarVisibility();
    }

    internal IEnumerable<ToolBar> CustomizableToolbars() => [MainToolbarPanel, PageLayoutToolBar];

    private static string ToolbarItemLabel(FrameworkElement item)
    {
        if (item is Panel panel && panel.Children.OfType<TextBox>().Any()) return "ページ番号入力";
        if (item is ComboBox combo) return combo.Name == "EditorModeSelector" ? "編集モード選択" : "OCR編集単位";
        if (item is ContentControl { Content: DependencyObject content })
        {
            var texts = ToolbarTexts(content).Where(text => !string.IsNullOrWhiteSpace(text.Text)).ToArray();
            if (texts.Length > 0) return texts[^1].Text;
        }
        return (item.ToolTip as string ?? System.Windows.Automation.AutomationProperties.GetName(item)).Split('(', '（')[0].Trim();
    }

    private static IEnumerable<TextBlock> ToolbarTexts(DependencyObject content)
    {
        if (content is TextBlock text) yield return text;
        foreach (var child in LogicalTreeHelper.GetChildren(content).OfType<DependencyObject>())
            foreach (var descendant in ToolbarTexts(child)) yield return descendant;
    }

    private void PopulateToolbarMenu(ItemCollection items, ToolBar toolbar)
    {
        items.Clear();
        var hidden = ViewModel.CurrentApplicationSettings.HiddenToolbarItems;
        foreach (var control in toolbar.Items.OfType<FrameworkElement>().Where(item => ToolbarItem.GetKey(item).Length > 0))
        {
            var key = ToolbarItem.GetKey(control);
            var entry = new MenuItem { Header = LocalizationService.Translate(ToolbarItemLabel(control)), IsCheckable = true,
                IsChecked = !hidden.Contains(key, StringComparer.Ordinal), StaysOpenOnClick = true };
            entry.Click += (_, _) => ViewModel.SetToolbarItemVisible(key, entry.IsChecked);
            items.Add(entry);
        }
        items.Add(new Separator());
        var reset = new MenuItem { Header = "既定の表示に戻す(_D)", StaysOpenOnClick = true };
        ActionIcon.SetKey(reset, "Reset");
        reset.Click += (_, _) => { ViewModel.ResetToolbarItems(toolbar.Items.OfType<FrameworkElement>().Select(ToolbarItem.GetKey)); PopulateToolbarMenu(items, toolbar); };
        items.Add(reset);
    }

    private void ToolbarButtonsMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender) || sender is not MenuItem menu) return;
        menu.Items.Clear();
        foreach (var toolbar in CustomizableToolbars())
        {
            var child = new MenuItem { Header = toolbar == MainToolbarPanel ? "メイン" : "ページ表示" };
            PopulateToolbarMenu(child.Items, toolbar); menu.Items.Add(child);
        }
    }

    internal void ApplyToolbarVisibility()
    {
        var hidden = ViewModel.CurrentApplicationSettings.HiddenToolbarItems.Order(StringComparer.Ordinal).ToArray();
        if (_appliedHiddenToolbarIds is not null && _appliedHiddenToolbarIds.SequenceEqual(hidden)) return;
        _appliedHiddenToolbarIds = hidden;
        foreach (var toolbar in CustomizableToolbars())
        {
            var elements = toolbar.Items.OfType<FrameworkElement>().ToArray();
            foreach (var item in elements)
                if (ToolbarItem.GetKey(item) is { Length: > 0 } key)
                    item.Visibility = ViewModel.CurrentApplicationSettings.HiddenToolbarItems.Contains(key, StringComparer.Ordinal)
                        ? Visibility.Collapsed : Visibility.Visible;
            var visibleInGroup = false;
            for (var i = 0; i < elements.Length; i++)
            {
                if (elements[i] is Separator separator)
                {
                    var laterGroup = elements.Skip(i + 1).Any(item => item is not Separator &&
                        ToolbarItem.GetKey(item).Length > 0 && item.Visibility == Visibility.Visible);
                    separator.Visibility = visibleInGroup && laterGroup ? Visibility.Visible : Visibility.Collapsed;
                    visibleInGroup = false;
                }
                else if (ToolbarItem.GetKey(elements[i]).Length > 0 && elements[i].Visibility == Visibility.Visible) visibleInGroup = true;
            }
            toolbar.InvalidateMeasure();
            (toolbar.Parent as ToolBarTray)?.InvalidateMeasure();
        }
        _toolbarChromeNeedsRefresh = true;
        // This is an intentional content-size change, not a drag. Permit one fresh
        // floating-window measurement while keeping the docking tree untouched.
        _sizedFloatingToolbarWindows.Clear();
        Dispatcher.BeginInvoke(() =>
        {
            if (_avalonDockPreviewManager is { } manager) { RefreshAvalonToolbarChrome(manager); SizeFloatingToolbars(); }
        }, DispatcherPriority.Loaded);
    }
}
