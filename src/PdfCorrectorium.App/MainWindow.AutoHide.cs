using System.ComponentModel;
using System.Windows;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private readonly Dictionary<LayoutAnchorable, double> _autoHideDockedWidths = [];
    private bool _adjustingAutoHideWidth;

    private static double MinimumAutoHideWidth(LayoutAnchorable pane) =>
        pane.ContentId is "navigation" or "bookmarks" ? 200 : 240;

    private double DefaultAutoHideWidth(LayoutAnchorable pane) => Math.Max(MinimumAutoHideWidth(pane),
        pane.ContentId is "navigation" or "bookmarks" ? ViewModel.CurrentApplicationSettings.PageListWidth
            : ViewModel.CurrentApplicationSettings.PropertiesPanelWidth);

    private double GetDockedPaneWidth(LayoutAnchorable pane)
    {
        var width = pane.Parent is LayoutAnchorablePane { DockWidth.IsAbsolute: true } parent
            ? parent.DockWidth.Value : (pane.Content as FrameworkElement)?.ActualWidth ?? 0;
        return double.IsFinite(width) && width > 0 ? Math.Max(MinimumAutoHideWidth(pane), width) : DefaultAutoHideWidth(pane);
    }

    private void NormalizeAutoHideWidth(LayoutAnchorable pane, bool useDockedWidth)
    {
        if (_adjustingAutoHideWidth) return;
        _adjustingAutoHideWidth = true;
        try
        {
            var minimum = MinimumAutoHideWidth(pane);
            pane.AutoHideMinWidth = minimum;
            // A fixed-to-auto-hide transition consumes its captured width once.
            // Later Hide/Show transitions must retain the user's resized flyout.
            var width = useDockedWidth && _autoHideDockedWidths.Remove(pane, out var dockedWidth)
                ? dockedWidth : pane.AutoHideWidth;
            // Zero means AvalonDock's 100px default. Keep valid persisted/resized widths.
            if (!double.IsFinite(width) || width < minimum) width = DefaultAutoHideWidth(pane);
            pane.AutoHideWidth = width;
        }
        finally { _adjustingAutoHideWidth = false; }
    }

    private void AutoHidePane_OnPropertyChanging(object? sender, PropertyChangingEventArgs args)
    {
        // Native pin commands move each child into an anchor group. Capture before
        // Parent changes; the menu is only one of several paths into auto-hide.
        if (args.PropertyName == nameof(LayoutAnchorable.Parent) && sender is LayoutAnchorable pane &&
            pane.Parent is LayoutAnchorablePane && !pane.IsFloating)
            _autoHideDockedWidths[pane] = GetDockedPaneWidth(pane);
    }

    private void AutoHidePane_OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_adjustingAutoHideWidth || sender is not LayoutAnchorable pane) return;
        if (args.PropertyName == nameof(LayoutAnchorable.IsAutoHidden) && pane.IsAutoHidden)
            NormalizeAutoHideWidth(pane, useDockedWidth: true);
        if (args.PropertyName is nameof(LayoutAnchorable.Parent) or nameof(LayoutAnchorable.IsAutoHidden))
            AvalonPane_OnVisibleChanged(sender, EventArgs.Empty);
        if (args.PropertyName is nameof(LayoutAnchorable.AutoHideWidth) or nameof(LayoutAnchorable.AutoHideMinWidth))
        {
            if (pane.IsAutoHidden) NormalizeAutoHideWidth(pane, useDockedWidth: false);
            // Flyout resizing happens in a native host, outside the manager's mouse route.
            AvalonPane_OnVisibleChanged(sender, EventArgs.Empty);
        }
    }

    internal void SetPaneAutoHide(LayoutAnchorable pane, bool autoHide)
    {
        SetAvalonPaneVisible(pane, true, reveal: false);
        if (pane.IsFloating) pane.Dock();
        if (PaneAutoHideSide(pane) is not { } side)
        {
            // Top/bottom edge panes retain AvalonDock's existing behavior.
            if (pane.IsAutoHidden != autoHide && pane.Parent is LayoutAnchorablePane or LayoutAnchorGroup) pane.ToggleAutoHide();
            CaptureAvalonDockingSettings(); return;
        }
        _updatingAvalonSettings = true;
        try
        {
            if (autoHide)
            {
                _sideAutoHideModes[side] = true; ClearFixedSidePanes(side); EnforceSideAutoHide();
            }
            else PinOneSidePane(pane, side);
            RememberSidePaneStates();
            AttachSideAutoHideCommands();
        }
        finally { _updatingAvalonSettings = false; }
        CaptureAvalonDockingSettings();
    }
}
