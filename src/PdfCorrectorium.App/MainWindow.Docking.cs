using System.Windows;
using System.Windows.Controls;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

/// <summary>
/// Keeps the existing ToolBar controls compact. Placement and drag feedback are owned by
/// AvalonDock; no window-level hit testing or custom drag targets are used here.
/// </summary>
public partial class MainWindow
{
    private ToolbarDockSide _mainToolbarSide = ToolbarDockSide.Top;
    private ToolbarDockSide _pageToolbarSide = ToolbarDockSide.Top;

    private void InitializeWorkspaceDocking() => InitializeAvalonDockPreview();

    private void SynchronizeWorkspaceDocking() => SynchronizeAvalonDockLayout();

    private void SynchronizeToolbarLayout()
    {
        SynchronizeAvalonDockLayout();
    }

    private void UpdateToolbarOrientation()
    {
        var mainVertical = _mainToolbarSide is ToolbarDockSide.Left or ToolbarDockSide.Right;
        var pageVertical = _pageToolbarSide is ToolbarDockSide.Left or ToolbarDockSide.Right;
        ToolBar.SetOverflowMode(EditUnitSelector, mainVertical ? OverflowMode.Always : OverflowMode.AsNeeded);
        ToolBar.SetOverflowMode(EditorModeSelector, mainVertical ? OverflowMode.Always : OverflowMode.AsNeeded);
        EditUnitSelector.Width = mainVertical ? 48 : 110;
        EditorModeSelector.Width = mainVertical ? 48 : 150;
        PageToolbarTitle.Visibility = pageVertical ? Visibility.Collapsed : Visibility.Visible;
        ToolbarPageNumberPanel.Orientation = pageVertical ? Orientation.Vertical : Orientation.Horizontal;
        ToolbarPageNumberPanel.Margin = pageVertical ? new Thickness(0, 3, 0, 3) : new Thickness(3, 0, 3, 0);
        ToolbarPageNumberSeparator.Visibility = pageVertical ? Visibility.Collapsed : Visibility.Visible;
        ToolbarPageNumberBox.Width = pageVertical ? 38 : 48;
        ToolbarPageCount.MinWidth = pageVertical ? 0 : 24;
        ToolbarPageCount.Margin = pageVertical ? new Thickness(0, 3, 0, 0) : new Thickness();
    }
}
