using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private const int MaximumContinuousPreviewImages = 12;
    private readonly Dictionary<int, ContinuousPageVisual> _continuousPageVisuals = [];
    private readonly Dictionary<int, BitmapSource> _continuousPreviewCache = [];
    private readonly Dictionary<int, long> _continuousPreviewUseOrder = [];
    private readonly DispatcherTimer _continuousPreviewRefreshTimer = new(DispatcherPriority.Background);
    private CancellationTokenSource? _continuousPreviewCancellation;
    private int? _continuousActivePageNumber;
    private long _continuousPreviewUseCounter;
    private string? _continuousDocumentKey;
    private bool _keepContinuousViewportForSelection;
    private bool _continuousLayoutUpdatePending;

    private sealed record ContinuousPageVisual(Border Host, Grid Surface, Image Image);

    /// <summary>連続表示の遅延描画タイマーとページ構成変更の監視を開始します。</summary>
    private void InitializeContinuousPageView()
    {
        _continuousPreviewRefreshTimer.Interval = TimeSpan.FromMilliseconds(90);
        _continuousPreviewRefreshTimer.Tick += ContinuousPreviewRefreshTimer_OnTick;
        ViewModel.PageItems.CollectionChanged += (_, _) => ScheduleContinuousLayoutUpdate();
    }

    /// <summary>大量ページの一覧生成時も、コレクション変更を1回のレイアウト更新へ集約します。</summary>
    private void ScheduleContinuousLayoutUpdate()
    {
        if (_continuousLayoutUpdatePending) return;
        _continuousLayoutUpdatePending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _continuousLayoutUpdatePending = false;
            ResetContinuousPreviewCache();
            if (ViewModel.IsContinuousView) UpdatePreviewDocumentLayout();
        }, DispatcherPriority.ContextIdle);
    }

    /// <summary>連続表示が所有する描画要求と再生成可能な画像を解放します。</summary>
    private void ReleaseContinuousPageView()
    {
        _continuousPreviewRefreshTimer.Stop();
        _continuousPreviewCancellation?.Cancel();
        _continuousPreviewCancellation?.Dispose();
        _continuousPreviewCancellation = null;
        _continuousPreviewCache.Clear();
        _continuousPreviewUseOrder.Clear();
    }

    /// <summary>全論理ページを縦に並べ、現在ページだけを既存の編集面へ置き換えます。</summary>
    private void ActivateContinuousPageLayout()
    {
        PreviewDocumentLayoutHost.Visibility = Visibility.Collapsed;
        ContinuousDocumentLayoutHost.Visibility = Visibility.Visible;

        var documentKey = $"{ViewModel.SourcePdfPath}|{ViewModel.PageItems.Count}";
        if (!string.Equals(documentKey, _continuousDocumentKey, StringComparison.Ordinal))
        {
            ResetContinuousPreviewCache();
            _continuousDocumentKey = documentKey;
        }

        var pageNumber = ViewModel.SelectedPage?.PageNumber;
        if (pageNumber is null || ViewModel.PageItems.Count == 0)
        {
            ContinuousDocumentLayoutHost.Children.Clear();
            _continuousPageVisuals.Clear();
            _continuousActivePageNumber = null;
            return;
        }

        ContinuousDocumentLayoutHost.Configure(
            ViewModel.PageItems.Count,
            ViewModel.GetContinuousPreviewSize,
            ViewModel.ZoomFactor,
            ViewModel.IsFacingPagesView,
            ViewModel.FacingPagesShowCoverSeparately,
            ViewModel.FacingPagesBindingDirection);
        if (_continuousActivePageNumber == pageNumber &&
            ReferenceEquals(PreviewPageHost.Parent, ContinuousDocumentLayoutHost))
        {
            ScheduleContinuousPreviewRefresh();
            return;
        }

        var horizontalOffset = PreviewScrollViewer.HorizontalOffset;
        var verticalOffset = PreviewScrollViewer.VerticalOffset;
        RemoveFromParent(PreviewPageHost);
        ContinuousDocumentLayoutHost.Children.Clear();
        _continuousPageVisuals.Clear();
        PreviewPageHost.Margin = new Thickness(6);
        PreviewPageHost.Tag = pageNumber.Value;
        ContinuousDocumentLayoutHost.Children.Add(PreviewPageHost);
        _continuousActivePageNumber = pageNumber;
        Dispatcher.BeginInvoke(() =>
        {
            PreviewScrollViewer.UpdateLayout();
            if (_keepContinuousViewportForSelection)
            {
                PreviewScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                PreviewScrollViewer.ScrollToVerticalOffset(verticalOffset);
            }
            else
            {
                PreviewPageHost.BringIntoView();
            }
            _keepContinuousViewportForSelection = false;
            ScheduleContinuousPreviewRefresh();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>通常／見開き表示へ戻すため、編集面をXAML上のページ配置へ戻します。</summary>
    private void DeactivateContinuousPageLayout()
    {
        _continuousPreviewRefreshTimer.Stop();
        _continuousPreviewCancellation?.Cancel();
        _continuousPreviewCancellation?.Dispose();
        _continuousPreviewCancellation = null;
        if (ReferenceEquals(PreviewPageHost.Parent, ContinuousDocumentLayoutHost))
        {
            ContinuousDocumentLayoutHost.Children.Remove(PreviewPageHost);
            PreviewDocumentLayoutHost.Children.Add(PreviewPageHost);
        }
        ContinuousDocumentLayoutHost.Children.Clear();
        _continuousPageVisuals.Clear();
        _continuousActivePageNumber = null;
        ContinuousDocumentLayoutHost.Visibility = Visibility.Collapsed;
        PreviewDocumentLayoutHost.Visibility = Visibility.Visible;
    }

    private ContinuousPageVisual CreateContinuousPageVisual(int pageNumber)
    {
        var (width, height) = ViewModel.GetContinuousPreviewSize(pageNumber);
        var image = new Image
        {
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        if (_continuousPreviewCache.TryGetValue(pageNumber, out var cached))
        {
            image.Source = cached;
            width = cached.PixelWidth;
            height = cached.PixelHeight;
            MarkContinuousPreviewUsed(pageNumber);
        }

        var surface = new Grid { Width = width, Height = height, Background = Brushes.White };
        var scale = new ScaleTransform();
        BindingOperations.SetBinding(scale, ScaleTransform.ScaleXProperty,
            new Binding(nameof(ViewModel.ZoomFactor)) { Source = ViewModel });
        BindingOperations.SetBinding(scale, ScaleTransform.ScaleYProperty,
            new Binding(nameof(ViewModel.ZoomFactor)) { Source = ViewModel });
        surface.LayoutTransform = scale;
        surface.Children.Add(image);
        surface.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(204, 38, 55, 70)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = LocalizationService.IsEnglish ? $"Page {pageNumber}" : $"{pageNumber} ページ",
                Foreground = Brushes.White,
            },
        });

        var host = new Border
        {
            Tag = pageNumber,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(145, 160, 174)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = LocalizationService.IsEnglish ? "Click to edit this page" : "クリックしてこのページを編集",
            Child = surface,
        };
        System.Windows.Automation.AutomationProperties.SetName(host,
            LocalizationService.IsEnglish ? $"Page {pageNumber} preview" : $"{pageNumber}ページのプレビュー");
        host.MouseLeftButtonUp += ContinuousPreviewPage_OnMouseLeftButtonUp;
        return new ContinuousPageVisual(host, surface, image);
    }

    private static void RemoveFromParent(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
        else if (element.Parent is Decorator decorator) decorator.Child = null;
        else if (element.Parent is ContentControl contentControl) contentControl.Content = null;
    }

    private void PreviewScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ViewModel.IsContinuousView)
            ScheduleContinuousPreviewRefresh();
    }

    private void ScheduleContinuousPreviewRefresh()
    {
        if (!ViewModel.IsContinuousView || !ViewModel.CanUsePreview) return;
        _continuousPreviewRefreshTimer.Stop();
        _continuousPreviewRefreshTimer.Start();
    }

    private async void ContinuousPreviewRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        _continuousPreviewRefreshTimer.Stop();
        await RefreshVisibleContinuousPreviewsAsync();
    }

    /// <summary>
    /// 表示領域の前後1画面にあるページだけを描画します。高速スクロール時は古い要求を中止し、
    /// 画像保持数も制限することで長大文書のメモリ使用量を一定範囲に保ちます。
    /// </summary>
    private async Task RefreshVisibleContinuousPreviewsAsync()
    {
        if (!ViewModel.IsContinuousView || !ViewModel.CanUsePreview) return;
        _continuousPreviewCancellation?.Cancel();
        _continuousPreviewCancellation?.Dispose();
        _continuousPreviewCancellation = new CancellationTokenSource();
        var token = _continuousPreviewCancellation.Token;

        PreviewScrollViewer.UpdateLayout();
        var viewportTop = PreviewScrollViewer.VerticalOffset;
        var viewportHeight = Math.Max(1, PreviewScrollViewer.ViewportHeight);
        var loadTop = Math.Max(0, viewportTop - viewportHeight);
        var loadBottom = viewportTop + viewportHeight * 2;
        var viewportCenter = viewportTop + viewportHeight / 2;
        var candidates = ContinuousDocumentLayoutHost.GetPagesIntersecting(loadTop, loadBottom)
            .Where(pageNumber => pageNumber != _continuousActivePageNumber)
            .Select(pageNumber =>
            {
                var bounds = ContinuousDocumentLayoutHost.GetPageSlotBounds(pageNumber);
                return (PageNumber: pageNumber, Bounds: bounds,
                    Distance: Math.Abs(bounds.Top + bounds.Height / 2 - viewportCenter));
            })
            .OrderBy(item => item.Distance)
            .Take(MaximumContinuousPreviewImages)
            .ToArray();
        var wanted = candidates.Select(item => item.PageNumber).ToHashSet();

        foreach (var pageNumber in _continuousPageVisuals.Keys.Where(page => !wanted.Contains(page)).ToArray())
        {
            var visual = _continuousPageVisuals[pageNumber];
            visual.Image.Source = null;
            ContinuousDocumentLayoutHost.Children.Remove(visual.Host);
            _continuousPageVisuals.Remove(pageNumber);
        }

        try
        {
            foreach (var candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                if (!_continuousPageVisuals.TryGetValue(candidate.PageNumber, out var visual))
                {
                    visual = CreateContinuousPageVisual(candidate.PageNumber);
                    _continuousPageVisuals.Add(candidate.PageNumber, visual);
                    ContinuousDocumentLayoutHost.Children.Add(visual.Host);
                }
                if (visual.Image.Source is not null)
                {
                    MarkContinuousPreviewUsed(candidate.PageNumber);
                    continue;
                }
                if (_continuousPreviewCache.TryGetValue(candidate.PageNumber, out var cached))
                {
                    visual.Image.Source = cached;
                    visual.Surface.Width = cached.PixelWidth;
                    visual.Surface.Height = cached.PixelHeight;
                    ContinuousDocumentLayoutHost.SetPageSize(candidate.PageNumber, cached.PixelWidth, cached.PixelHeight);
                    MarkContinuousPreviewUsed(candidate.PageNumber);
                    continue;
                }

                var result = await ViewModel.RenderContinuousPreviewPageAsync(candidate.PageNumber, token);
                token.ThrowIfCancellationRequested();
                if (result is null || !ViewModel.IsContinuousView ||
                    !_continuousPageVisuals.TryGetValue(candidate.PageNumber, out var currentVisual) ||
                    !ReferenceEquals(currentVisual, visual)) return;
                visual.Surface.Width = result.Image.PixelWidth;
                visual.Surface.Height = result.Image.PixelHeight;
                visual.Image.Source = result.Image;
                ContinuousDocumentLayoutHost.SetPageSize(candidate.PageNumber, result.Image.PixelWidth, result.Image.PixelHeight);
                _continuousPreviewCache[candidate.PageNumber] = result.Image;
                MarkContinuousPreviewUsed(candidate.PageNumber);
                TrimContinuousPreviewCache(wanted);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            await ViewModel.LogContinuousPreviewFailureAsync(error);
        }
    }

    private void MarkContinuousPreviewUsed(int pageNumber) =>
        _continuousPreviewUseOrder[pageNumber] = ++_continuousPreviewUseCounter;

    private void TrimContinuousPreviewCache(IReadOnlySet<int> wanted)
    {
        foreach (var pageNumber in _continuousPreviewCache.Keys
                     .Where(page => !wanted.Contains(page))
                     .OrderBy(page => _continuousPreviewUseOrder.GetValueOrDefault(page))
                     .Take(Math.Max(0, _continuousPreviewCache.Count - MaximumContinuousPreviewImages))
                     .ToArray())
        {
            _continuousPreviewCache.Remove(pageNumber);
            _continuousPreviewUseOrder.Remove(pageNumber);
            if (_continuousPageVisuals.TryGetValue(pageNumber, out var visual)) visual.Image.Source = null;
        }
    }

    private void ResetContinuousPreviewCache()
    {
        _continuousPreviewCancellation?.Cancel();
        _continuousPreviewCancellation?.Dispose();
        _continuousPreviewCancellation = null;
        _continuousPreviewCache.Clear();
        _continuousPreviewUseOrder.Clear();
        foreach (var visual in _continuousPageVisuals.Values) visual.Image.Source = null;
    }

    private void ContinuousPreviewPage_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: int pageNumber }) return;
        var horizontalOffset = PreviewScrollViewer.HorizontalOffset;
        var verticalOffset = PreviewScrollViewer.VerticalOffset;
        CommitPendingEditorBindings();
        _keepContinuousViewportForSelection = true;
        ViewModel.NavigateFromAdjacentPreview(pageNumber);
        Dispatcher.BeginInvoke(() =>
        {
            PreviewScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
            PreviewScrollViewer.ScrollToVerticalOffset(verticalOffset);
            ScheduleContinuousPreviewRefresh();
        }, DispatcherPriority.Loaded);
        e.Handled = true;
    }
}
