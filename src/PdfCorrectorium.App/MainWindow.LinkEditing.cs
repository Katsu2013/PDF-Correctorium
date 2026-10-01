using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

/// <summary>リンク編集だけの入力処理。OCR／墨消しのポインター操作には介入しません。</summary>
public partial class MainWindow
{
    private Rect? _linkTransformOriginal;
    private Rect? _linkTransformPending;
    private Point _linkMoveStart;
    private FrameworkElement? _linkMoveElement;

    private void ApplyLinkDestinationView(PdfCorrectorium.Core.Documents.PdfInternalLink link)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _previewFitMode = link.DestinationViewType switch
            {
                "/Fit" or "/FitB" => PreviewFitMode.Page,
                "/FitH" or "/FitBH" => PreviewFitMode.Width,
                "/FitV" or "/FitBV" => PreviewFitMode.Height,
                _ => PreviewFitMode.None,
            };
            if (_previewFitMode != PreviewFitMode.None) ApplyPreviewFit();
            var destination = ViewModel.GetLinkDestinationPreviewBounds(link);
            if (destination is not { } bounds) return;
            if (link.DestinationViewType == "/FitR")
                ViewModel.ZoomPercent = Math.Clamp(Math.Min(PreviewScrollViewer.ViewportWidth / bounds.Width,
                    PreviewScrollViewer.ViewportHeight / bounds.Height) * 100, 25, 400);
            Dispatcher.BeginInvoke(() =>
            {
                PreviewScrollViewer.UpdateLayout();
                var position = PreviewPageHost.TranslatePoint(
                    new Point(bounds.Left * ViewModel.ZoomFactor, bounds.Top * ViewModel.ZoomFactor), PreviewScrollViewer);
                PreviewScrollViewer.ScrollToHorizontalOffset(PreviewScrollViewer.HorizontalOffset + position.X);
                PreviewScrollViewer.ScrollToVerticalOffset(PreviewScrollViewer.VerticalOffset + position.Y);
            }, DispatcherPriority.ContextIdle);
        }, DispatcherPriority.Loaded);
    }

    private void LinkEditingMode_OnClick(object sender, RoutedEventArgs e)
    {
        CancelPointerRectangle();
        ViewModel.ActivateLinkEditMode();
    }

    private bool HandleLinkEditingKey(KeyEventArgs e)
    {
        if (!ViewModel.IsLinkEditMode) return false;
        if (ShouldDeleteSelectedRedaction(e.Key, true, ViewModel.CanEditLinkSelection, Keyboard.FocusedElement))
        {
            CancelLinkTransform();
            ViewModel.DeleteSelectedInternalLink();
            e.Handled = true; return true;
        }
        if (e.Key != Key.Escape) return false;
        if (_isAddingLinkArea || _linkTransformOriginal is not null)
        {
            CancelLinkTransform(); CancelPointerRectangle();
        }
        else if (ViewModel.SelectedLinkArea is not null) ViewModel.SelectedLinkArea = null;
        else ViewModel.EditorMode = EditorInteractionMode.OcrEditing;
        e.Handled = true; return true;
    }

    private async void LinkAreaOverlay_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.CanHitLinkAreas || sender is not FrameworkElement { DataContext: DocumentLinkOverlayViewModel item }) return;
        if (FindAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null) return;
        if (item.SourcePageNumber != ViewModel.SelectedPage?.PageNumber)
        {
            await ViewModel.SelectLinkPageAsync(item.SourcePageNumber, item.Id);
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) await ViewModel.NavigateSelectedInternalLinkAsync();
            e.Handled = true; return;
        }
        ViewModel.SelectedLinkArea = item;
        ViewModel.SelectedOverlay = null;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            await ViewModel.NavigateSelectedInternalLinkAsync();
        else if (e.ClickCount == 2 && item.IsEditable)
            InternalLinkEdit_OnClick(sender, new RoutedEventArgs());
        else if (ViewModel.CanTransformSelectedLink)
        {
            _linkTransformOriginal = new Rect(item.Left, item.Top, item.Width, item.Height);
            _linkTransformPending = _linkTransformOriginal;
            _linkMoveStart = e.GetPosition(OverlayCanvas);
            _linkMoveElement = (FrameworkElement)sender;
            _linkMoveElement.CaptureMouse();
        }
        e.Handled = true;
    }

    private async void LinkAreaOverlay_OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.CanHitLinkAreas || sender is not FrameworkElement { DataContext: DocumentLinkOverlayViewModel item }) return;
        if (item.SourcePageNumber != ViewModel.SelectedPage?.PageNumber)
            await ViewModel.SelectLinkPageAsync(item.SourcePageNumber, item.Id);
        else ViewModel.SelectedLinkArea = item;
        ViewModel.SelectedOverlay = null;
    }

    private void LinkList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.SelectedLinkArea is not null) ViewModel.SelectedOverlay = null;
    }

    private void UpdateLinkTextSelectionPreview(Rect selection)
    {
        MarqueeSelectionRectangle.Visibility = Visibility.Collapsed;
        TextRedactionMarkerPreviewCanvas.Children.Clear();
        foreach (var area in ViewModel.GetLinkTextSelectionPreview(selection))
        {
            var marker = new Border { Width = area.Width, Height = area.Height,
                Background = new SolidColorBrush(Color.FromArgb(70, 37, 99, 235)),
                BorderBrush = Brushes.RoyalBlue, BorderThickness = new Thickness(1) };
            Canvas.SetLeft(marker, area.Left); Canvas.SetTop(marker, area.Top);
            TextRedactionMarkerPreviewCanvas.Children.Add(marker);
        }
        TextRedactionMarkerPreviewCanvas.Visibility = Visibility.Visible;
    }

    private async Task CompleteLinkSelectionAsync(Rect bounds)
    {
        var text = ViewModel.LinkInputModeIndex == 1;
        if (text && ViewModel.GetLinkTextSelectionPreview(bounds).Count == 0)
        {
            ViewModel.StatusMessageForInteraction("選択できる文字がありません。画像内の文字は範囲指定をご利用ください。");
            return;
        }
        if (!text && (bounds.Width < 8 || bounds.Height < 8))
        {
            ViewModel.StatusMessageForInteraction("リンク範囲が小さすぎるため作成しませんでした。"); return;
        }
        var dialog = new InternalLinkWindow(ViewModel.PageItems.Count, null,
            Math.Min((ViewModel.SelectedPage?.PageNumber ?? 1) + 1, ViewModel.PageItems.Count)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (text) await ViewModel.CreateTextLinkAsync(bounds, dialog.ResultDestinationPage,
                dialog.ResultZoomPercent, dialog.Description, dialog.ResultExternalUri);
            else await ViewModel.CreateLinkAreaAsync(bounds, dialog.ResultDestinationPage,
                dialog.ResultZoomPercent, dialog.Description, dialog.ResultExternalUri);
            ViewModel.LinkInputModeIndex = 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.IO.IOException)
        {
            ViewModel.StatusMessageForInteraction("リンクを作成できませんでした：" + error.Message);
        }
    }

    private void LinkMove_OnMove(object sender, MouseEventArgs e)
    {
        if (_linkMoveElement is null || _linkTransformOriginal is not { } original || e.LeftButton != MouseButtonState.Pressed) return;
        var offset = e.GetPosition(OverlayCanvas) - _linkMoveStart;
        ShowLinkTransform(new Rect(original.Left + offset.X, original.Top + offset.Y, original.Width, original.Height));
        e.Handled = true;
    }
    private void LinkMove_OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_linkMoveElement is null) return;
        CompleteLinkTransform(); e.Handled = true;
    }
    private void LinkResize_OnStarted(object sender, DragStartedEventArgs e)
    {
        if (!ViewModel.CanTransformSelectedLink || ViewModel.SelectedLinkArea is not { } area) return;
        _linkTransformOriginal = _linkTransformPending = new Rect(area.Left, area.Top, area.Width, area.Height);
    }
    private void LinkResize_OnDelta(object sender, DragDeltaEventArgs e)
    {
        if (_linkTransformPending is not { } area || sender is not Thumb { Tag: string direction }) return;
        var left = area.Left; var right = area.Right; var top = area.Top; var bottom = area.Bottom;
        if (direction.Contains('W')) left = Math.Clamp(left + e.HorizontalChange, 0, right - 4);
        if (direction.Contains('E')) right = Math.Clamp(right + e.HorizontalChange, left + 4, ViewModel.PreviewPixelWidth);
        if (direction.Contains('N')) top = Math.Clamp(top + e.VerticalChange, 0, bottom - 4);
        if (direction.Contains('S')) bottom = Math.Clamp(bottom + e.VerticalChange, top + 4, ViewModel.PreviewPixelHeight);
        ShowLinkTransform(new Rect(left, top, right - left, bottom - top));
    }
    private void LinkResize_OnCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled) CancelLinkTransform(); else CompleteLinkTransform();
    }
    private void ShowLinkTransform(Rect bounds)
    {
        var left = Math.Clamp(bounds.Left, 0, Math.Max(0, ViewModel.PreviewPixelWidth - bounds.Width));
        var top = Math.Clamp(bounds.Top, 0, Math.Max(0, ViewModel.PreviewPixelHeight - bounds.Height));
        _linkTransformPending = new Rect(left, top, bounds.Width, bounds.Height);
        Canvas.SetLeft(MarqueeSelectionRectangle, left); Canvas.SetTop(MarqueeSelectionRectangle, top);
        MarqueeSelectionRectangle.Width = bounds.Width; MarqueeSelectionRectangle.Height = bounds.Height;
        MarqueeSelectionRectangle.Background = new SolidColorBrush(Color.FromArgb(45, 37, 99, 235));
        MarqueeSelectionRectangle.BorderBrush = Brushes.RoyalBlue;
        MarqueeSelectionRectangle.Visibility = Visibility.Visible;
    }
    private void CompleteLinkTransform()
    {
        var pending = _linkTransformPending; var original = _linkTransformOriginal;
        CancelLinkTransform();
        if (pending is { } bounds && bounds != original) ViewModel.UpdateSelectedLinkGeometry(bounds, commit: true);
    }
    private void CancelLinkTransform()
    {
        var capture = _linkMoveElement;
        _linkMoveElement = null; _linkTransformOriginal = null; _linkTransformPending = null;
        if (capture?.IsMouseCaptured == true) capture.ReleaseMouseCapture();
        ResetPointerRectangle();
    }

    private ItemsControl CreatePassiveLinkOverlay(int pageNumber, double width, double height)
    {
        var overlay = new ItemsControl
        {
            Tag = "passive-links", ItemTemplate = (DataTemplate)FindResource("LinkOverlayTemplate"),
            ItemsSource = ViewModel.GetLinkOverlaysForPage(pageNumber, width, height),
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(Canvas))),
        };
        var style = new Style(typeof(ContentPresenter));
        style.Setters.Add(new Setter(Canvas.LeftProperty, new Binding("Left")));
        style.Setters.Add(new Setter(Canvas.TopProperty, new Binding("Top")));
        overlay.ItemContainerStyle = style;
        overlay.SetBinding(VisibilityProperty, new Binding(nameof(ViewModel.IsLinkOverlayDisplayed)) { Source = ViewModel,
            Converter = (System.Windows.Data.IValueConverter)FindResource("BooleanToVisibilityConverter") });
        overlay.SetBinding(IsHitTestVisibleProperty, new Binding(nameof(ViewModel.CanHitLinkAreas)) { Source = ViewModel });
        Panel.SetZIndex(overlay, 70);
        return overlay;
    }
    private void RefreshPassiveLinkSurface(Grid surface, int pageNumber)
    {
        foreach (var old in surface.Children.OfType<ItemsControl>().Where(control => Equals(control.Tag, "passive-links")).ToArray())
            surface.Children.Remove(old);
        if (pageNumber > 0 && double.IsFinite(surface.Width) && double.IsFinite(surface.Height))
            surface.Children.Add(CreatePassiveLinkOverlay(pageNumber, surface.Width, surface.Height));
    }
    private void RefreshPassiveLinkOverlays()
    {
        if (PreviousPreviewPageHost.Child is Grid previous)
            RefreshPassiveLinkSurface(previous, ViewModel.PreviousPreviewPageNumber ?? 0);
        if (NextPreviewPageHost.Child is Grid next)
            RefreshPassiveLinkSurface(next, ViewModel.NextPreviewPageNumber ?? 0);
        foreach (var pair in _continuousPageVisuals)
            RefreshPassiveLinkSurface(pair.Value.Surface, pair.Key);
    }
}
