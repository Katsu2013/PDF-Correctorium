using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PdfCorrectorium.App.Services;

/// <summary>Native, presentation-only column menus shared by result lists.</summary>
public static class ListDetails
{
    public static readonly DependencyProperty EnableColumnMenuProperty = DependencyProperty.RegisterAttached(
        "EnableColumnMenu", typeof(bool), typeof(ListDetails), new PropertyMetadata(false, OnEnableColumnMenuChanged));
    public static readonly DependencyProperty IsDetailProperty = DependencyProperty.RegisterAttached(
        "IsDetail", typeof(bool), typeof(ListDetails), new PropertyMetadata(false));
    private static readonly DependencyProperty OpenMenuProperty = DependencyProperty.RegisterAttached(
        "OpenMenu", typeof(ContextMenu), typeof(ListDetails), new PropertyMetadata(null));
    internal static ContextMenu? GetOpenMenu(DataGrid grid) => (ContextMenu?)grid.GetValue(OpenMenuProperty);

    public static bool GetEnableColumnMenu(DependencyObject target) => (bool)target.GetValue(EnableColumnMenuProperty);
    public static void SetEnableColumnMenu(DependencyObject target, bool value) => target.SetValue(EnableColumnMenuProperty, value);
    public static bool GetIsDetail(DependencyObject target) => (bool)target.GetValue(IsDetailProperty);
    public static void SetIsDetail(DependencyObject target, bool value) => target.SetValue(IsDetailProperty, value);

    private static void OnEnableColumnMenuChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not DataGrid grid) return;
        if ((bool)args.NewValue)
        {
            grid.ContextMenuOpening += OnContextMenuOpening;
            grid.Unloaded += OnUnloaded;
        }
        else
        {
            grid.ContextMenuOpening -= OnContextMenuOpening;
            grid.Unloaded -= OnUnloaded;
            if (GetOpenMenu(grid) is { } open) open.IsOpen = false;
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (GetOpenMenu((DataGrid)sender) is { } open) open.IsOpen = false;
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        var grid = (DataGrid)sender;
        var keyboard = args.CursorLeft < 0 && args.CursorTop < 0;
        var source = args.OriginalSource as DependencyObject;
        DependencyObject? header = null;
        while (source is not null && !ReferenceEquals(source, grid))
        {
            if (source is DataGridColumnHeader or DataGridColumnHeadersPresenter) { header = source; break; }
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        // Leave row context menus alone. The keyboard path also works when no rows exist.
        if (!keyboard && header is null) return;
        args.Handled = true;
        if (GetOpenMenu(grid) is { } previous) previous.IsOpen = false;
        var menu = CreateColumnMenu(grid);
        grid.SetValue(OpenMenuProperty, menu);
        menu.Closed += (_, _) => { if (ReferenceEquals(GetOpenMenu(grid), menu)) grid.ClearValue(OpenMenuProperty); };
        menu.PlacementTarget = keyboard ? grid : header as UIElement ?? grid;
        menu.Placement = keyboard ? PlacementMode.Bottom : PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    internal static ContextMenu CreateColumnMenu(DataGrid grid)
    {
        var menu = new ContextMenu();
        System.Windows.Automation.AutomationProperties.SetName(menu, LocalizationService.Translate("表示する列"));
        foreach (var column in grid.Columns.OrderBy(column => column.DisplayIndex))
        {
            var item = new MenuItem
            {
                Header = column.Header, Tag = column, IsCheckable = true, StaysOpenOnClick = true,
                IsChecked = column.Visibility == Visibility.Visible,
            };
            item.Click += (_, _) =>
            {
                if (item.IsChecked || grid.Columns.Count(value => value.Visibility == Visibility.Visible) > 1)
                    column.SetCurrentValue(DataGridColumn.VisibilityProperty, item.IsChecked ? Visibility.Visible : Visibility.Collapsed);
                Synchronize();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = LocalizationService.Translate("既定の列表示に戻す(_R)") };
        reset.Click += (_, _) =>
        {
            foreach (var column in grid.Columns)
                column.SetCurrentValue(DataGridColumn.VisibilityProperty, GetIsDetail(column) ? Visibility.Collapsed : Visibility.Visible);
            Synchronize();
        };
        menu.Items.Add(reset);
        Synchronize();
        return menu;

        void Synchronize()
        {
            var visibleCount = grid.Columns.Count(column => column.Visibility == Visibility.Visible);
            foreach (var item in menu.Items.OfType<MenuItem>().Where(item => item.Tag is DataGridColumn))
            {
                item.IsChecked = ((DataGridColumn)item.Tag).Visibility == Visibility.Visible;
                item.IsEnabled = !item.IsChecked || visibleCount > 1;
            }
        }
    }
}
