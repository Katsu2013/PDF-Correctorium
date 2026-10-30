using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App.Controls;

/// <summary>Current-page paragraph outlines without a control per paragraph or character.</summary>
public sealed class OcrParagraphLayer : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(MainWindowViewModel),
        typeof(OcrParagraphLayer), new FrameworkPropertyMetadata(null, OnModelChanged));
    public static readonly DependencyProperty FrameOpacityProperty = DependencyProperty.Register(nameof(FrameOpacity), typeof(double),
        typeof(OcrParagraphLayer), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double),
        typeof(OcrParagraphLayer), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CoordinateOwnerProperty = DependencyProperty.Register(nameof(CoordinateOwner), typeof(ItemsControl),
        typeof(OcrParagraphLayer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (sender, _) => ((OcrParagraphLayer)sender)._coordinatePanel = null));
    public MainWindowViewModel? Model { get => (MainWindowViewModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public double FrameOpacity { get => (double)GetValue(FrameOpacityProperty); set => SetValue(FrameOpacityProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public ItemsControl? CoordinateOwner { get => (ItemsControl?)GetValue(CoordinateOwnerProperty); set => SetValue(CoordinateOwnerProperty, value); }

    internal sealed record Frame(Rect Bounds, IReadOnlyList<Guid> RegionIds);
    internal IReadOnlyList<Frame> Frames => _frames;
    internal int PartitionBuildCount { get; private set; }
    internal int LastDrawnFrameCount { get; private set; }
    internal int LastHighlightedFrameCount { get; private set; }
    internal Point SurfaceOffset { get; private set; }
    private readonly HashSet<OverlayRegionViewModel> _observed = [];
    private MainWindowViewModel? _attached;
    private IReadOnlyList<Frame> _frames = [];
    private HashSet<Guid> _selected = [];
    private bool _layoutDirty = true, _selectionDirty = true;
    private double _penZoom;
    private Pen? _normalPen, _selectedPen;
    private Panel? _coordinatePanel;

    public OcrParagraphLayer()
    {
        IsHitTestVisible = false; Focusable = false; ClipToBounds = true;
        Loaded += (_, _) => RefreshAttachment();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => RefreshAttachment();
    }

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((OcrParagraphLayer)sender).RefreshAttachment();

    private void RefreshAttachment()
    {
        var target = IsLoaded && IsVisible ? Model : null;
        if (ReferenceEquals(target, _attached)) return;
        Detach();
        if (target is null) return;
        _attached = target;
        target.PropertyChanged += OnModelPropertyChanged;
        target.OverlayItems.CollectionChanged += OnRowsChanged;
        ObserveCurrentRows(); MarkLayoutDirty();
    }

    private void Detach()
    {
        if (_attached is not null)
        {
            _attached.PropertyChanged -= OnModelPropertyChanged;
            _attached.OverlayItems.CollectionChanged -= OnRowsChanged;
        }
        foreach (var row in _observed) row.PropertyChanged -= OnRowPropertyChanged;
        _observed.Clear(); _attached = null; _frames = []; _selected.Clear(); _coordinatePanel = null;
        _layoutDirty = _selectionDirty = true;
        LastDrawnFrameCount = LastHighlightedFrameCount = 0;
        InvalidateVisual();
    }

    private void ObserveCurrentRows()
    {
        var current = _attached!.OverlayItems.ToHashSet();
        foreach (var row in _observed.Where(row => !current.Contains(row)).ToArray())
        { row.PropertyChanged -= OnRowPropertyChanged; _observed.Remove(row); }
        foreach (var row in current.Where(row => !_observed.Contains(row)))
        { row.PropertyChanged += OnRowPropertyChanged; _observed.Add(row); }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // Collection changes can arrive one row at a time during page loading. The
        // partition is rebuilt once on rendering, after the batch has settled.
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var row in _observed) row.PropertyChanged -= OnRowPropertyChanged;
            _observed.Clear(); _frames = [];
        }
        if (args.OldItems is not null)
            foreach (OverlayRegionViewModel row in args.OldItems)
                if (_observed.Remove(row)) row.PropertyChanged -= OnRowPropertyChanged;
        if (args.NewItems is not null)
            foreach (OverlayRegionViewModel row in args.NewItems)
                if (_observed.Add(row)) row.PropertyChanged += OnRowPropertyChanged;
        MarkLayoutDirty();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainWindowViewModel.SelectedOverlayCount))
        { _selectionDirty = true; InvalidateVisual(); }
        if (args.PropertyName is nameof(MainWindowViewModel.SelectedPage) or nameof(MainWindowViewModel.PreviewPixelWidth) or nameof(MainWindowViewModel.PreviewPixelHeight))
            MarkLayoutDirty();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(OverlayRegionViewModel.Text) or nameof(OverlayRegionViewModel.Left) or
            nameof(OverlayRegionViewModel.Top) or nameof(OverlayRegionViewModel.Width) or nameof(OverlayRegionViewModel.Height) or
            nameof(OverlayRegionViewModel.RotationDegrees) or nameof(OverlayRegionViewModel.IsVertical) or
            nameof(OverlayRegionViewModel.ReadingOrder) or nameof(OverlayRegionViewModel.IsDeleted) or nameof(OverlayRegionViewModel.ParagraphGroupId))
            MarkLayoutDirty();
    }

    private void MarkLayoutDirty() { _layoutDirty = _selectionDirty = true; InvalidateVisual(); }

    internal static Rect RowBounds(OverlayRegionViewModel row)
    {
        if (!double.IsFinite(row.Left) || !double.IsFinite(row.Top) || !double.IsFinite(row.Width) ||
            !double.IsFinite(row.Height) || !double.IsFinite(row.RotationDegrees) || row.Width <= 0 || row.Height <= 0) return Rect.Empty;
        var bounds = new Rect(row.Left, row.Top, row.Width, row.Height);
        var matrix = Matrix.Identity;
        matrix.RotateAt(row.RotationDegrees, row.Left + row.Width / 2, row.Top + row.Height / 2);
        return new MatrixTransform(matrix).TransformBounds(bounds);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        LastDrawnFrameCount = LastHighlightedFrameCount = 0;
        if (_attached is not { } model || !double.IsFinite(FrameOpacity) || FrameOpacity <= 0) return;
        var owner = CoordinateOwner;
        if (owner is null) return;
        if (_coordinatePanel is null || !_coordinatePanel.IsDescendantOf(owner))
            _coordinatePanel = FindItemsHost(owner, owner);
        if (_coordinatePanel is null) return;
        // The native ScrollViewer's content inset is not necessarily ListBox.Padding.
        // Match its actual items-panel origin without moving existing OCR text.
        SurfaceOffset = _coordinatePanel.TransformToVisual(this).Transform(new Point());
        if (_layoutDirty)
        {
            var frames = new List<Frame>();
            foreach (var group in model.GetParagraphGroups())
            {
                var bounds = Rect.Empty;
                foreach (var row in group) bounds.Union(RowBounds(row));
                if (!bounds.IsEmpty && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height))
                    frames.Add(new(bounds, group.Select(row => row.Id).ToArray()));
            }
            _frames = frames; _layoutDirty = false; PartitionBuildCount++;
        }
        if (_selectionDirty)
        { _selected = model.SelectedOverlays.Select(row => row.Id).ToHashSet(); _selectionDirty = false; }
        var zoom = double.IsFinite(Zoom) && Zoom > 0 ? Zoom : 1;
        if (_normalPen is null || _penZoom != zoom)
        {
            _penZoom = zoom;
            _normalPen = Pen(Color.FromArgb(150, 0, 140, 134), 1 / zoom);
            _selectedPen = Pen(Color.FromRgb(0, 121, 107), 2 / zoom);
        }
        drawing.PushOpacity(Math.Clamp(FrameOpacity, 0, 1));
        drawing.PushTransform(new TranslateTransform(SurfaceOffset.X, SurfaceOffset.Y));
        // Draw selected frames last, so neighboring outlines cannot cover them.
        Draw(selected: false); Draw(selected: true);
        drawing.Pop(); drawing.Pop();
        void Draw(bool selected)
        {
            foreach (var frame in _frames)
            {
                if (frame.RegionIds.Any(_selected.Contains) != selected) continue;
                var bounds = frame.Bounds; bounds.Inflate(3 / zoom, 3 / zoom);
                drawing.DrawRoundedRectangle(null, selected ? _selectedPen : _normalPen, bounds, 2 / zoom, 2 / zoom);
                LastDrawnFrameCount++; if (selected) LastHighlightedFrameCount++;
            }
        }
    }

    private static Pen Pen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness); pen.Freeze(); return pen;
    }

    private static Panel? FindItemsHost(DependencyObject root, ItemsControl owner)
    {
        if (root is Panel { IsItemsHost: true } panel && ReferenceEquals(ItemsControl.GetItemsOwner(panel), owner)) return panel;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindItemsHost(VisualTreeHelper.GetChild(root, index), owner) is { } match) return match;
        return null;
    }
}
