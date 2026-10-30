using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;
using System.ComponentModel;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private ReadAloudPanel? _readAloudPanel;
    private readonly Border _readAloudHost = new();
    internal ReadAloudPanel? DockedReadAloudPanel => _readAloudPanel;
    internal ISpeechPlayback? ReadAloudPlaybackOverride { get; set; }
    private void EnsureReadAloudPanel()
    {
        if (_readAloudPanel is not null) return;
        _readAloudPanel = new ReadAloudPanel(this, ViewModel, ReadAloudPlaybackOverride);
        _readAloudPanel.CloseRequested += (_, _) => { _propertyPanes["read-aloud"].Hide(); CaptureAvalonDockingSettings(); };
        _readAloudHost.Child = _readAloudPanel;
    }
    private void InitializeReadAloudDock()
    {
        _readAloudHost.DataContext = ViewModel;
        void LoadWhenShown()
        {
            if (_readAloudHost.IsVisible && _propertyPanes.TryGetValue("read-aloud", out var pane) && !pane.IsHidden)
                EnsureReadAloudPanel();
        }
        _readAloudHost.Loaded += (_, _) => LoadWhenShown();
        _readAloudHost.IsVisibleChanged += (_, _) => LoadWhenShown();
        Closed += async (_, _) => { if (_readAloudPanel is not null) await _readAloudPanel.DisposeAsync(); };
        _propertyViews.Add("read-aloud", _readAloudHost);
    }
    private SpeechMarkerAdorner? _speechMarker;
    private AdornerLayer? _speechMarkerLayer;
    internal SpeechTextSegment? CurrentSpeechSegment { get; private set; }
    private double _speechPageWidth, _speechPageHeight;
    private OverlayRegionViewModel? _speechRegion;
    internal bool HasSpeechMarker => _speechMarker is not null && _speechMarkerLayer is not null;

    private void ReadAloud_OnClick(object sender, RoutedEventArgs e)
    {
        var pane = _propertyPanes["read-aloud"];
        EnsureReadAloudPanel(); SetAvalonPaneVisible(pane, true);
        pane.IsActive = true;
        CaptureAvalonDockingSettings();
    }
    internal void CommitSpeechEditorBindings() => CommitPendingEditorBindings();
    internal Task FollowSpeechPageAsync(int page, CancellationToken token = default) => ViewModel.DisplaySpeechPageAsync(page, token);
    internal void SetSpeechMarker(int page, SpeechTextSegment segment, double width, double height)
    {
        if (ViewModel.SelectedPage?.PageNumber != page) return;
        CurrentSpeechSegment = segment; _speechPageWidth = width; _speechPageHeight = height;
        var live = ViewModel.OverlayItems.FirstOrDefault(region => region.Id == segment.RegionId);
        if (!ReferenceEquals(live, _speechRegion))
        {
            if (_speechRegion is not null) _speechRegion.PropertyChanged -= SpeechRegionChanged;
            _speechRegion = live;
            if (live is not null) live.PropertyChanged += SpeechRegionChanged;
        }
        if (_speechMarker is null && AdornerLayer.GetAdornerLayer(PreviewPageHost) is { } layer)
        {
            _speechMarkerLayer = layer;
            _speechMarker = new SpeechMarkerAdorner(PreviewPageHost, this) { IsHitTestVisible = false };
            layer.Add(_speechMarker);
        }
        RefreshSpeechMarker();
    }
    internal void RefreshSpeechMarker() => _speechMarker?.InvalidateVisual();
    private void SpeechRegionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayRegionViewModel.Left) or nameof(OverlayRegionViewModel.Top) or
            nameof(OverlayRegionViewModel.Width) or nameof(OverlayRegionViewModel.Height) or nameof(OverlayRegionViewModel.RotationDegrees)) RefreshSpeechMarker();
    }
    internal void ClearSpeechMarker()
    {
        CurrentSpeechSegment = null;
        if (_speechRegion is not null) _speechRegion.PropertyChanged -= SpeechRegionChanged;
        _speechRegion = null;
        if (_speechMarker is not null) _speechMarkerLayer?.Remove(_speechMarker);
        _speechMarker = null; _speechMarkerLayer = null;
    }
    private sealed class SpeechMarkerAdorner(UIElement element, MainWindow owner) : Adorner(element)
    {
        private static readonly Brush Marker = (Brush)new SolidColorBrush(Color.FromArgb(75, 255, 190, 0)).GetAsFrozen();
        protected override void OnRender(DrawingContext context)
        {
            IsHitTestVisible = false;
            if (owner.CurrentSpeechSegment is not { } segment || owner._speechPageWidth <= 0 || owner._speechPageHeight <= 0) return;
            Rect rect;
            if (owner._speechRegion is { } live)
                rect = new(live.Left, live.Top, live.Width, live.Height);
            else
                rect = new(segment.Bounds.Left / owner._speechPageWidth * owner.ViewModel.PreviewPixelWidth,
                    (owner._speechPageHeight - segment.Bounds.Top) / owner._speechPageHeight * owner.ViewModel.PreviewPixelHeight,
                    segment.Bounds.Size.Width / owner._speechPageWidth * owner.ViewModel.PreviewPixelWidth,
                    segment.Bounds.Size.Height / owner._speechPageHeight * owner.ViewModel.PreviewPixelHeight);
            var zoom = owner.ViewModel.ZoomFactor;
            context.PushClip(new RectangleGeometry(new Rect(RenderSize)));
            var drawRect = new Rect(rect.X * zoom + 1, rect.Y * zoom + 1, rect.Width * zoom, rect.Height * zoom);
            var angle = owner._speechRegion?.RotationDegrees ?? 0;
            if (angle != 0) context.PushTransform(new RotateTransform(angle, drawRect.X + drawRect.Width / 2, drawRect.Y + drawRect.Height / 2));
            context.DrawRoundedRectangle(Marker, null, drawRect, 2, 2);
            if (angle != 0) context.Pop();
            context.Pop();
        }
    }
}
