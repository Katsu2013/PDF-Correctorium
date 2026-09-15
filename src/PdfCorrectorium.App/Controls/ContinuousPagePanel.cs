using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App.Controls;

/// <summary>
/// 全ページの論理的な配置だけを保持し、実際の子要素は現在ページと可視範囲だけに限定するパネルです。
/// 単一ページと見開きの双方を同じ遅延描画経路で扱います。
/// </summary>
public sealed class ContinuousPagePanel : Panel
{
    private const double PageGap = 12;
    private readonly List<Size> _pageSizes = [];
    private Rect[] _pageSlots = [];
    private IReadOnlyList<LayoutRow> _rows = [];
    private Size _extent;
    private double _zoomFactor = 1;
    private bool _useFacingPages;
    private bool _showCoverSeparately = true;
    private FacingPageBindingDirection _bindingDirection = FacingPageBindingDirection.LeftBinding;

    private sealed record LayoutRow(double Top, double Height, int? LeftPageNumber, int? RightPageNumber);

    public int PageCount => _pageSizes.Count;

    /// <summary>ページ数、基準寸法、倍率とページ配置をまとめて更新します。</summary>
    public void Configure(
        int pageCount,
        Func<int, (double Width, double Height)> sizeProvider,
        double zoomFactor,
        bool useFacingPages,
        bool showCoverSeparately,
        FacingPageBindingDirection bindingDirection)
    {
        ArgumentNullException.ThrowIfNull(sizeProvider);
        _pageSizes.Clear();
        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            var size = sizeProvider(pageNumber);
            _pageSizes.Add(new Size(Math.Max(1, size.Width), Math.Max(1, size.Height)));
        }
        _zoomFactor = Math.Max(0.01, zoomFactor);
        _useFacingPages = useFacingPages;
        _showCoverSeparately = showCoverSeparately;
        _bindingDirection = Enum.IsDefined(bindingDirection)
            ? bindingDirection
            : FacingPageBindingDirection.LeftBinding;
        RecalculateExtent();
    }

    /// <summary>描画後に判明したページ寸法を反映し、以降のページ位置を更新します。</summary>
    public void SetPageSize(int pageNumber, double width, double height)
    {
        if (pageNumber < 1 || pageNumber > _pageSizes.Count) return;
        var normalized = new Size(Math.Max(1, width), Math.Max(1, height));
        if (_pageSizes[pageNumber - 1] == normalized) return;
        _pageSizes[pageNumber - 1] = normalized;
        RecalculateExtent();
    }

    /// <summary>倍率変更時もページ画像自体を作り直さず、論理配置だけを再計算します。</summary>
    public void SetZoomFactor(double zoomFactor)
    {
        var normalized = Math.Max(0.01, zoomFactor);
        if (Math.Abs(_zoomFactor - normalized) < 0.0001) return;
        _zoomFactor = normalized;
        RecalculateExtent();
    }

    public Rect GetPageSlotBounds(int pageNumber) =>
        pageNumber >= 1 && pageNumber <= _pageSlots.Length ? _pageSlots[pageNumber - 1] : Rect.Empty;

    /// <summary>指定した縦範囲と交差する実ページ番号だけを返します。空き側はページとして返しません。</summary>
    public IReadOnlyList<int> GetPagesIntersecting(double top, double bottom)
    {
        if (_pageSizes.Count == 0 || bottom < top) return [];
        var result = new List<int>();
        foreach (var row in _rows)
        {
            if (row.Top + row.Height < top) continue;
            if (row.Top > bottom) break;
            if (row.LeftPageNumber.HasValue) result.Add(row.LeftPageNumber.Value);
            if (row.RightPageNumber.HasValue) result.Add(row.RightPageNumber.Value);
        }
        return result;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _extent;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var horizontalShift = Math.Max(0, (finalSize.Width - _extent.Width) / 2);
        foreach (UIElement child in InternalChildren)
        {
            if (child is not FrameworkElement { Tag: int pageNumber } element) continue;
            var slot = GetPageSlotBounds(pageNumber);
            if (slot.IsEmpty)
            {
                element.Arrange(new Rect());
                continue;
            }
            element.Arrange(new Rect(slot.Left + horizontalShift, slot.Top, element.DesiredSize.Width, element.DesiredSize.Height));
        }
        return new Size(Math.Max(finalSize.Width, _extent.Width), Math.Max(finalSize.Height, _extent.Height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var fill = new SolidColorBrush(Color.FromRgb(245, 247, 249));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(145, 160, 174)), 1);
        fill.Freeze();
        border.Freeze();
        for (var pageNumber = 1; pageNumber <= _pageSlots.Length; pageNumber++)
        {
            var slot = GetPageSlotBounds(pageNumber);
            if (slot.IsEmpty) continue;
            var page = new Rect(
                slot.Left + PageGap / 2,
                slot.Top + PageGap / 2,
                Math.Max(1, slot.Width - PageGap),
                Math.Max(1, slot.Height - PageGap));
            drawingContext.DrawRectangle(fill, border, page);
        }
    }

    private void RecalculateExtent()
    {
        _pageSlots = new Rect[_pageSizes.Count];
        if (_pageSizes.Count == 0)
        {
            _rows = [];
            _extent = new Size(1, 1);
            InvalidateLayout();
            return;
        }

        if (_useFacingPages) RecalculateFacingExtent();
        else RecalculateSinglePageExtent();
        InvalidateLayout();
    }

    private void RecalculateSinglePageExtent()
    {
        var maximumWidth = _pageSizes.Max(size => size.Width * _zoomFactor + PageGap);
        var rows = new List<LayoutRow>(_pageSizes.Count);
        double top = 0;
        for (var index = 0; index < _pageSizes.Count; index++)
        {
            var width = _pageSizes[index].Width * _zoomFactor + PageGap;
            var height = _pageSizes[index].Height * _zoomFactor + PageGap;
            _pageSlots[index] = new Rect((maximumWidth - width) / 2, top, width, height);
            rows.Add(new LayoutRow(top, height, index + 1, null));
            top += height;
        }
        _rows = rows;
        _extent = new Size(Math.Max(1, maximumWidth), Math.Max(1, top));
    }

    private void RecalculateFacingExtent()
    {
        var layouts = new List<FacingPageLayout>();
        var seen = new HashSet<(int? Left, int? Right)>();
        for (var pageNumber = 1; pageNumber <= _pageSizes.Count; pageNumber++)
        {
            var layout = FacingPageLayoutCalculator.Calculate(
                pageNumber,
                _pageSizes.Count,
                _showCoverSeparately,
                _bindingDirection);
            if (seen.Add((layout.LeftPageNumber, layout.RightPageNumber))) layouts.Add(layout);
        }

        var measurements = layouts.Select(layout =>
        {
            var leftSize = SizeFor(layout.LeftPageNumber) ?? SizeFor(layout.RightPageNumber)!.Value;
            var rightSize = SizeFor(layout.RightPageNumber) ?? SizeFor(layout.LeftPageNumber)!.Value;
            var leftSlot = new Size(leftSize.Width * _zoomFactor + PageGap, leftSize.Height * _zoomFactor + PageGap);
            var rightSlot = new Size(rightSize.Width * _zoomFactor + PageGap, rightSize.Height * _zoomFactor + PageGap);
            return (Layout: layout, Left: leftSlot, Right: rightSlot,
                Width: leftSlot.Width + rightSlot.Width,
                Height: Math.Max(leftSlot.Height, rightSlot.Height));
        }).ToArray();
        var maximumWidth = measurements.Max(item => item.Width);
        var rows = new List<LayoutRow>(measurements.Length);
        double top = 0;
        foreach (var item in measurements)
        {
            var left = (maximumWidth - item.Width) / 2;
            if (item.Layout.LeftPageNumber is int leftPage)
                _pageSlots[leftPage - 1] = new Rect(left, top, item.Left.Width, item.Left.Height);
            if (item.Layout.RightPageNumber is int rightPage)
                _pageSlots[rightPage - 1] = new Rect(left + item.Left.Width, top, item.Right.Width, item.Right.Height);
            rows.Add(new LayoutRow(top, item.Height, item.Layout.LeftPageNumber, item.Layout.RightPageNumber));
            top += item.Height;
        }
        _rows = rows;
        _extent = new Size(Math.Max(1, maximumWidth), Math.Max(1, top));
    }

    private Size? SizeFor(int? pageNumber) =>
        pageNumber is int value && value >= 1 && value <= _pageSizes.Count
            ? _pageSizes[value - 1]
            : null;

    private void InvalidateLayout()
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }
}
