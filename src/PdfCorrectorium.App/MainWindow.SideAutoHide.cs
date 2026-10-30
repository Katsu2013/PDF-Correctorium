using AvalonDock.Layout;
using AvalonDock.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private sealed record SidePaneState(AnchorSide? Side, bool AutoHidden, bool Hidden, bool Floating);
    private readonly Dictionary<LayoutAnchorable, SidePaneState> _sidePaneStates = [];
    private readonly Dictionary<AnchorSide, bool> _sideAutoHideModes = new() { [AnchorSide.Left] = false, [AnchorSide.Right] = false };
    private readonly Dictionary<LayoutAnchorable, AnchorSide> _fixedSidePanes = [];
    private bool _synchronizingSideAutoHide;
    private bool _sideAutoHideInputAttached;
    private static bool _sideAnchorClassHandlerAttached;
    private static string SideAutoHideAttribute(AnchorSide side) => "PdfCorrectoriumAutoHide" + side;
    private static string FixedSideAttribute(AnchorSide side) => "PdfCorrectoriumFixed" + side;

    private sealed class SideAutoHideCommand(ICommand native, Action execute) : ICommand
    {
        public bool CanExecute(object? parameter) => native.CanExecute(parameter);
        public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
        public event EventHandler? CanExecuteChanged { add => native.CanExecuteChanged += value; remove => native.CanExecuteChanged -= value; }
    }

    private void AttachSideAutoHideCommands()
    {
        if (_avalonDockPreviewManager is not { } manager) return;
        if (!_sideAutoHideInputAttached)
        {
            _sideAutoHideInputAttached = true;
            manager.Loaded += (_, _) => AttachSideAutoHideCommands();
            if (!_sideAnchorClassHandlerAttached)
            {
                _sideAnchorClassHandlerAttached = true;
                EventManager.RegisterClassHandler(typeof(LayoutAnchorControl), Control.MouseDoubleClickEvent,
                    new MouseButtonEventHandler(SideAnchor_OnDoubleClick), true);
            }
        }
        foreach (var pane in _visibilityObservedPanes)
            if (manager.GetLayoutItemFromModel(pane) is LayoutAnchorableItem item && item.AutoHideCommand is ICommand native && native is not SideAutoHideCommand)
                item.AutoHideCommand = new SideAutoHideCommand(native, () => SetPaneAutoHide(pane, !pane.IsAutoHidden));
    }

    private static void SideAnchor_OnDoubleClick(object sender, MouseButtonEventArgs args)
    {
        // AvalonDock calls base.OnMouseDoubleClick before its built-in ToggleAutoHide.
        // Handle that direct event on the anchor itself, not a parent preview event.
        if (sender is not LayoutAnchorControl anchor || args.ChangedButton != MouseButton.Left ||
            anchor.Model is not LayoutAnchorable { IsAutoHidden: true } pane ||
            pane.Root?.Manager is not { AllowAnchorDoubleClickDock: true } manager || Window.GetWindow(manager) is not MainWindow host ||
            !host._visibilityObservedPanes.Contains(pane)) return;
        host.SetPaneAutoHide(pane, false); args.Handled = true;
    }

    private AnchorSide? PaneAutoHideSide(LayoutAnchorable pane)
    {
        if (pane.IsFloating || pane.IsHidden) return null;
        var side = pane.Parent is LayoutAnchorGroup { Parent: LayoutAnchorSide anchorSide }
            ? anchorSide.Side : GetAvalonDockSide(pane.Parent as LayoutAnchorablePane);
        return side is AnchorSide.Left or AnchorSide.Right ? side : null;
    }

    private SidePaneState CurrentSideState(LayoutAnchorable pane) =>
        new(PaneAutoHideSide(pane), pane.IsAutoHidden, pane.IsHidden, pane.IsFloating);

    private void RememberSidePaneStates()
    {
        _sidePaneStates.Clear();
        foreach (var pane in _visibilityObservedPanes) _sidePaneStates[pane] = CurrentSideState(pane);
    }

    private void SynchronizeSideAutoHide()
    {
        if (_restoringNativeWorkspace || _synchronizingSideAutoHide) return;
        // Observe the settled native operation. Hide, Float, Show and cross-side
        // dragging must not be mistaken for the user's pin/unpin command.
        foreach (var pane in _visibilityObservedPanes)
        {
            var current = CurrentSideState(pane);
            if (current.Side is { } actualSide && _fixedSidePanes.TryGetValue(pane, out var pinnedSide) && actualSide != pinnedSide)
                _fixedSidePanes[pane] = actualSide;
            if (_sidePaneStates.TryGetValue(pane, out var previous) && current.Side is { } side && previous.Side == side &&
                !previous.Hidden && !current.Hidden && !previous.Floating && !current.Floating && previous.AutoHidden != current.AutoHidden)
            {
                if (current.AutoHidden) { _sideAutoHideModes[side] = true; ClearFixedSidePanes(side); }
                else _fixedSidePanes[pane] = side; // Respect direct native/model docking as a fixed exception.
            }
        }
        EnforceSideAutoHide();
        RememberSidePaneStates();
        AttachSideAutoHideCommands();
        Dispatcher.BeginInvoke(AttachSideAutoHideCommands, DispatcherPriority.Loaded);
    }

    private void EnforceSideAutoHide()
    {
        if (_synchronizingSideAutoHide) return;
        _synchronizingSideAutoHide = true;
        try
        {
            // ToggleAutoHide can move every tab in the same group. Re-check each
            // model rather than relying on the original parent or pane index.
            foreach (var pane in _visibilityObservedPanes.ToArray())
                if (PaneAutoHideSide(pane) is { } side && _sideAutoHideModes[side] && !pane.IsAutoHidden && !_fixedSidePanes.ContainsKey(pane))
                {
                    HidePanePreservingPins(pane, side);
                }
        }
        finally { _synchronizingSideAutoHide = false; }
    }

    private void HidePanePreservingPins(LayoutAnchorable pane, AnchorSide side)
    {
        if (pane.Parent is LayoutAnchorablePane group && group.Children.Any(_fixedSidePanes.ContainsKey))
            PlaceAtSide(pane, side, GetDockedPaneWidth(pane), requireOwnPane: true);
        pane.ToggleAutoHide();
    }

    private void ClearFixedSidePanes(AnchorSide side)
    {
        // A drag may be followed by a pin command before queued synchronization.
        // Clear exceptions on the current side, using the remembered side only
        // for hidden/floating panels that do not have a current dock edge.
        foreach (var pane in _fixedSidePanes.Where(pair => (PaneAutoHideSide(pair.Key) ?? pair.Value) == side)
            .Select(pair => pair.Key).ToArray()) _fixedSidePanes.Remove(pane);
    }

    private void PinOneSidePane(LayoutAnchorable pane, AnchorSide side)
    {
        _fixedSidePanes[pane] = side;
        if (!pane.IsAutoHidden) return;
        var siblings = pane.Parent is LayoutAnchorGroup group ? group.Children.ToArray() : [pane];
        _synchronizingSideAutoHide = true;
        try
        {
            pane.ToggleAutoHide();
            if (pane.Parent is LayoutAnchorablePane parent && parent.Children.Count > 1)
                PlaceAtSide(pane, side, Math.Max(MinimumAutoHideWidth(pane), pane.AutoHideWidth), requireOwnPane: true);
            foreach (var sibling in siblings.Where(item => !ReferenceEquals(item, pane)))
                if (PaneAutoHideSide(sibling) is { } siblingSide && !sibling.IsAutoHidden && !_fixedSidePanes.ContainsKey(sibling)) HidePanePreservingPins(sibling, siblingSide);
        }
        finally { _synchronizingSideAutoHide = false; }
        EnforceSideAutoHide();
    }

    private void ApplySideAutoHideToRevealedPane(LayoutAnchorable pane)
    {
        if (PaneAutoHideSide(pane) is { } side && _sideAutoHideModes[side])
        {
            EnforceSideAutoHide();
            RememberSidePaneStates();
        }
    }

    private void RestoreSideAutoHideModes(XElement root)
    {
        _fixedSidePanes.Clear();
        foreach (var side in new[] { AnchorSide.Left, AnchorSide.Right })
        {
            var value = (string?)root.Attribute(SideAutoHideAttribute(side));
            _sideAutoHideModes[side] = value is not null ? bool.Parse(value) :
                _visibilityObservedPanes.Any(pane => PaneAutoHideSide(pane) == side && pane.IsAutoHidden);
            foreach (var id in ((string?)root.Attribute(FixedSideAttribute(side)) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (_visibilityObservedPanes.FirstOrDefault(pane => pane.ContentId == id) is { } pane) _fixedSidePanes[pane] = side;
        }
        EnforceSideAutoHide();
        RememberSidePaneStates();
    }
}
