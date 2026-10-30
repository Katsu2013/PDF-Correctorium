using System.Windows.Threading;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private readonly List<LayoutAnchorable> _visibilityObservedPanes = [];
    private bool _paneVisibilityCaptureQueued;

    private void AttachAvalonPaneVisibilityEvents()
    {
        DetachAvalonPaneVisibilityEvents();
        if (_avalonNavigation is not null) _visibilityObservedPanes.Add(_avalonNavigation);
        if (_avalonBookmarks is not null) _visibilityObservedPanes.Add(_avalonBookmarks);
        _visibilityObservedPanes.AddRange(_propertyPanes.Values);
        foreach (var pane in _visibilityObservedPanes)
        {
            // Persisted layout attributes must not turn a reusable tool pane into
            // a permanently closable document or remove its supported Hide action.
            pane.CanClose = false;
            pane.CanHide = true;
            pane.CanAutoHide = true;
            NormalizeAutoHideWidth(pane, useDockedWidth: false);
            pane.IsVisibleChanged += AvalonPane_OnVisibleChanged;
            pane.PropertyChanging += AutoHidePane_OnPropertyChanging;
            pane.PropertyChanged += AutoHidePane_OnPropertyChanged;
        }
        RememberSidePaneStates();
        AttachSideAutoHideCommands();
        Dispatcher.BeginInvoke(AttachSideAutoHideCommands, DispatcherPriority.Loaded);
    }

    private void DetachAvalonPaneVisibilityEvents()
    {
        foreach (var pane in _visibilityObservedPanes)
        {
            pane.IsVisibleChanged -= AvalonPane_OnVisibleChanged;
            pane.PropertyChanging -= AutoHidePane_OnPropertyChanging;
            pane.PropertyChanged -= AutoHidePane_OnPropertyChanged;
        }
        _visibilityObservedPanes.Clear();
        _autoHideDockedWidths.Clear();
        _sidePaneStates.Clear();
        _fixedSidePanes.Clear();
    }

    private void AvalonPane_OnVisibleChanged(object? sender, EventArgs args)
    {
        if (_restoringNativeWorkspace || _synchronizingSideAutoHide || _paneVisibilityCaptureQueued) return;
        _paneVisibilityCaptureQueued = true;
        // Parent transitions fire several times during Hide/Show/Float. Capture only
        // the settled tree, and before the next input can operate a stale menu check.
        Dispatcher.BeginInvoke(() =>
        {
            _paneVisibilityCaptureQueued = false;
            if (_visibilityObservedPanes.Count == 0 || _avalonDockPreviewManager is not { IsLoaded: true } manager ||
                _visibilityObservedPanes.Any(pane => !ReferenceEquals(pane.Root, manager.Layout))) return;
            SynchronizeSideAutoHide();
            CaptureAvalonDockingSettings();
        }, DispatcherPriority.DataBind);
    }

    private void CaptureAvalonPaneVisibility()
    {
        if (_avalonNavigation is null || _avalonBookmarks is null || _avalonProperties is null) return;
        ViewModel.SetWorkspacePanelVisibility(_avalonNavigation.IsVisible, _avalonBookmarks.IsVisible, _avalonProperties.IsVisible);
    }

    private void SetAvalonPaneVisible(LayoutAnchorable pane, bool visible, bool reveal = true)
    {
        if (!visible)
        {
            if (pane.IsVisible) pane.Hide();
            return;
        }
        if (pane.IsHidden) pane.Show();
        if (_avalonDockPreviewManager is not { } manager) return;
        if (!pane.IsVisible || !ReferenceEquals(pane.Root, manager.Layout))
        {
            // Older layouts may have already lost the previous container. Reuse the
            // existing content on the recorded side rather than recreating its view.
            var settings = ViewModel.CurrentApplicationSettings;
            var position = pane.ContentId switch
            {
                "navigation" => settings.NavigationDockPosition,
                "bookmarks" => settings.BookmarkDockPosition,
                "properties" => settings.PropertiesDockPosition,
                _ => WorkspaceDockPosition.Right,
            };
            var side = position == WorkspaceDockPosition.Left ||
                position == WorkspaceDockPosition.Floating && pane.ContentId is "navigation" or "bookmarks"
                ? AnchorSide.Left : AnchorSide.Right;
            PlaceAtSide(pane, side, pane.ContentId is "navigation" or "bookmarks"
                ? settings.PageListWidth : settings.PropertiesPanelWidth, requireOwnPane: true);
            if (position == WorkspaceDockPosition.Floating) pane.Float();
        }
        ApplySideAutoHideToRevealedPane(pane);
        if (!reveal) return;
        if (pane.IsAutoHidden)
        {
            // New side tabs may not exist until the next layout pass. Reveal the
            // flyout after its native anchor is created, using the model's API.
            pane.IsSelected = false;
            Dispatcher.BeginInvoke(() =>
            {
                if (!pane.IsHidden && ReferenceEquals(pane.Root, manager.Layout))
                {
                    pane.IsActive = false;
                    pane.IsSelected = true;
                    pane.IsActive = true;
                }
            }, DispatcherPriority.ContextIdle);
        }
        else pane.IsSelected = true;
    }
}
