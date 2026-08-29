using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

/// <summary>定型領域として検出したページ候補を、ページ画像と対象位置を見ながら選択する画面です。</summary>
public partial class RepeatedRegionCandidateWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly RepeatedRegionPropagationOptions _options;
    private int _previewRequestNumber;
    private RepeatedRegionCandidate? _currentCandidate;
    private bool _previewImageLoaded;
    private bool _showAfter;

    /// <summary>候補一覧と利用者による選択状態を初期化します。</summary>
    public RepeatedRegionCandidateWindow(
        IReadOnlyList<RepeatedRegionCandidate> candidates,
        RepeatedRegionPropagationOptions options,
        MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _options = options;
        Candidates = new ObservableCollection<RepeatedRegionCandidate>(candidates);
        InitializeComponent();
        LocalizationService.Apply(this);
        CandidateGrid.ItemsSource = Candidates;
        Loaded += (_, _) =>
        {
            if (Candidates.Count > 0) CandidateGrid.SelectedIndex = 0;
        };
    }

    /// <summary>候補一覧と利用者による反映選択状態です。</summary>
    public ObservableCollection<RepeatedRegionCandidate> Candidates { get; }

    /// <summary>メイン画面での確認を依頼された候補です。通常のキャンセル時は<c>null</c>です。</summary>
    public RepeatedRegionCandidate? NavigationCandidate { get; private set; }

    private void SelectAll_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in Candidates) candidate.IsSelected = true;
    }

    private void ClearAll_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in Candidates) candidate.IsSelected = false;
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Candidates.Any(candidate => candidate.IsSelected))
        {
            MessageBox.Show(
                LocalizationService.IsEnglish
                    ? "Select at least one candidate to apply."
                    : "反映する候補を1件以上選択してください。",
                LocalizationService.IsEnglish ? "Propagation Candidates" : "反映候補",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private async void CandidateGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await UpdateCandidatePreviewAsync(CandidateGrid.SelectedItem as RepeatedRegionCandidate);
    }

    private void CandidateGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CandidateGrid.SelectedItem is RepeatedRegionCandidate) OpenSelectedCandidateInMainWindow();
    }

    private void PreviousCandidate_OnClick(object sender, RoutedEventArgs e)
    {
        if (Candidates.Count == 0) return;
        CandidateGrid.SelectedIndex = Math.Max(0, CandidateGrid.SelectedIndex - 1);
        CandidateGrid.ScrollIntoView(CandidateGrid.SelectedItem);
    }

    private void NextCandidate_OnClick(object sender, RoutedEventArgs e)
    {
        if (Candidates.Count == 0) return;
        CandidateGrid.SelectedIndex = Math.Min(Candidates.Count - 1, CandidateGrid.SelectedIndex + 1);
        CandidateGrid.ScrollIntoView(CandidateGrid.SelectedItem);
    }

    private void OpenCandidateInMainWindow_OnClick(object sender, RoutedEventArgs e) => OpenSelectedCandidateInMainWindow();

    private void OpenSelectedCandidateInMainWindow()
    {
        if (CandidateGrid.SelectedItem is not RepeatedRegionCandidate candidate) return;
        NavigationCandidate = candidate;
        DialogResult = false;
    }

    private void BeforePreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        _showAfter = false;
        BeforePreviewButton.IsChecked = true;
        AfterPreviewButton.IsChecked = false;
        RenderCandidateOverlay();
    }

    private void AfterPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        _showAfter = true;
        BeforePreviewButton.IsChecked = false;
        AfterPreviewButton.IsChecked = true;
        RenderCandidateOverlay();
    }

    private void ZoomOutButton_OnClick(object sender, RoutedEventArgs e) =>
        SetPreviewZoom(PreviewZoomSlider.Value - 10d);

    private void ZoomInButton_OnClick(object sender, RoutedEventArgs e) =>
        SetPreviewZoom(PreviewZoomSlider.Value + 10d);

    private void FitPreviewButton_OnClick(object sender, RoutedEventArgs e) => SetPreviewZoom(100d);

    private void PreviewZoomSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewZoomText is null) return;
        PreviewZoomText.Text = $"{e.NewValue:0}%";
        ApplyPreviewZoom();
    }

    private void CandidatePreviewScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyPreviewZoom();

    private void CandidatePreviewScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetPreviewZoom(PreviewZoomSlider.Value + (e.Delta > 0 ? 10d : -10d));
        e.Handled = true;
    }

    private void SetPreviewZoom(double value)
    {
        PreviewZoomSlider.Value = Math.Clamp(value, PreviewZoomSlider.Minimum, PreviewZoomSlider.Maximum);
        ApplyPreviewZoom();
    }

    /// <summary>100%をページ全体表示として、プレビューの表示領域だけを拡大縮小します。</summary>
    private void ApplyPreviewZoom()
    {
        if (CandidatePreviewViewbox is null || CandidatePreviewScrollViewer is null) return;
        var viewportWidth = CandidatePreviewScrollViewer.ViewportWidth;
        var viewportHeight = CandidatePreviewScrollViewer.ViewportHeight;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 1)
            viewportWidth = CandidatePreviewScrollViewer.ActualWidth;
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 1)
            viewportHeight = CandidatePreviewScrollViewer.ActualHeight;
        if (viewportWidth <= 1 || viewportHeight <= 1) return;

        var scale = PreviewZoomSlider.Value / 100d;
        CandidatePreviewViewbox.Width = Math.Max(1, (viewportWidth - 4) * scale);
        CandidatePreviewViewbox.Height = Math.Max(1, (viewportHeight - 4) * scale);
    }

    private async Task UpdateCandidatePreviewAsync(RepeatedRegionCandidate? candidate)
    {
        var requestNumber = ++_previewRequestNumber;
        _currentCandidate = candidate;
        _previewImageLoaded = false;
        CandidatePreviewImage.Source = null;
        CandidateOverlayCanvas.Children.Clear();
        CandidatePreviewPlaceholder.Visibility = Visibility.Visible;

        if (candidate is null)
        {
            CandidateDetailsText.Text = string.Empty;
            return;
        }

        CandidateDetailsText.Text = $"{candidate.PageDisplay}  |  {candidate.SimilarityDisplay}  |  {candidate.PositionDisplay}";
        CandidatePreviewPlaceholder.Text = LocalizationService.IsEnglish
            ? "Loading the page preview..."
            : "ページのプレビューを読み込んでいます...";

        try
        {
            var preview = await _viewModel.LoadRepeatedRegionCandidatePreviewAsync(candidate);
            if (requestNumber != _previewRequestNumber || preview.Image is null) return;

            CandidatePreviewPage.Width = preview.PixelWidth;
            CandidatePreviewPage.Height = preview.PixelHeight;
            CandidatePreviewImage.Source = preview.Image;
            CandidateOverlayCanvas.Width = preview.PixelWidth;
            CandidateOverlayCanvas.Height = preview.PixelHeight;
            _previewImageLoaded = true;
            RenderCandidateOverlay();
            ApplyPreviewZoom();
            CandidatePreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (requestNumber != _previewRequestNumber) return;
            CandidatePreviewPlaceholder.Text = LocalizationService.IsEnglish
                ? $"Could not load the preview.\n{ex.Message}"
                : $"プレビューを読み込めませんでした。\n{ex.Message}";
        }
    }

    /// <summary>現在選択中の候補を、変更前または変更後の状態で画像上へ重ねます。</summary>
    private void RenderCandidateOverlay()
    {
        CandidateOverlayCanvas.Children.Clear();
        var candidate = _currentCandidate;
        if (!_previewImageLoaded || candidate is null) return;

        var regions = _viewModel.GetRepeatedRegionPreviewRegions(candidate, _options, _showAfter);
        CandidateDetailsText.Text =
            $"{candidate.PageDisplay}  |  {candidate.SimilarityDisplay}  |  {candidate.PositionDisplay}  |  " +
            (_showAfter
                ? LocalizationService.IsEnglish ? "After" : "変更後"
                : LocalizationService.IsEnglish ? "Before" : "変更前");

        foreach (var region in regions)
        {
            var marker = CreateRegionMarker(region);
            Canvas.SetLeft(marker, region.Left);
            Canvas.SetTop(marker, region.Top);
            CandidateOverlayCanvas.Children.Add(marker);
        }
    }

    private static FrameworkElement CreateRegionMarker(RepeatedRegionPreviewRegion region)
    {
        var borderColor = region.Kind switch
        {
            RepeatedRegionPreviewKind.Replacement => Color.FromRgb(0, 112, 192),
            RepeatedRegionPreviewKind.Deleted => Color.FromRgb(205, 38, 38),
            RepeatedRegionPreviewKind.Locked => Color.FromRgb(104, 119, 131),
            _ => Color.FromRgb(255, 91, 20),
        };
        var fillColor = region.Kind switch
        {
            RepeatedRegionPreviewKind.Replacement => Color.FromArgb(58, 0, 150, 220),
            RepeatedRegionPreviewKind.Deleted => Color.FromArgb(72, 220, 50, 47),
            RepeatedRegionPreviewKind.Locked => Color.FromArgb(50, 104, 119, 131),
            _ => Color.FromArgb(55, 255, 137, 45),
        };
        var marker = new Border
        {
            Width = Math.Max(1, region.Width),
            Height = Math.Max(1, region.Height),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(fillColor),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(region.RotationDegrees),
            ClipToBounds = true,
        };

        if (region.Kind == RepeatedRegionPreviewKind.Deleted)
        {
            var deletedGrid = new Grid();
            deletedGrid.Children.Add(new Line
            {
                X1 = 0, Y1 = 0, X2 = 1, Y2 = 1,
                Stretch = Stretch.Fill,
                Stroke = new SolidColorBrush(borderColor),
                StrokeThickness = 2,
            });
            deletedGrid.Children.Add(new Line
            {
                X1 = 1, Y1 = 0, X2 = 0, Y2 = 1,
                Stretch = Stretch.Fill,
                Stroke = new SolidColorBrush(borderColor),
                StrokeThickness = 2,
            });
            marker.Child = deletedGrid;
            return marker;
        }

        var textElements = GetTextElements(region.Text);
        var characterAdvances = NormalizePreviewAdvances(
            region.CharacterAdvances,
            textElements.Count,
            region.IsVertical ? region.Height : region.Width);
        if (textElements.Count > 0 && characterAdvances.Count == textElements.Count)
        {
            var characterCanvas = new Canvas
            {
                Width = Math.Max(1, region.Width),
                Height = Math.Max(1, region.Height),
                ClipToBounds = true,
            };
            var offset = 0d;
            for (var index = 0; index < textElements.Count; index++)
            {
                var advance = characterAdvances[index];
                var characterCell = new Border
                {
                    Width = region.IsVertical ? Math.Max(1, region.Width) : Math.Max(0.5, advance),
                    Height = region.IsVertical ? Math.Max(0.5, advance) : Math.Max(1, region.Height),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(150, borderColor.R, borderColor.G, borderColor.B)),
                    BorderThickness = new Thickness(0.5),
                    Background = Brushes.Transparent,
                    Child = new Viewbox
                    {
                        Stretch = Stretch.Fill,
                        Child = new TextBlock
                        {
                            Text = textElements[index],
                            Foreground = new SolidColorBrush(borderColor),
                            Background = Brushes.Transparent,
                            TextAlignment = TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontWeight = FontWeights.SemiBold,
                        },
                    },
                };
                Canvas.SetLeft(characterCell, region.IsVertical ? 0 : offset);
                Canvas.SetTop(characterCell, region.IsVertical ? offset : 0);
                characterCanvas.Children.Add(characterCell);
                offset += advance;
            }
            marker.Child = characterCanvas;
        }
        else
        {
            var displayText = region.IsVertical ? JoinTextElementsVertically(region.Text) : region.Text;
            marker.Child = new Viewbox
            {
                Stretch = Stretch.Fill,
                Child = new TextBlock
                {
                    Text = displayText,
                    Foreground = new SolidColorBrush(borderColor),
                    Background = Brushes.Transparent,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                },
            };
        }
        return marker;
    }

    private static IReadOnlyList<string> GetTextElements(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
        return elements;
    }

    private static IReadOnlyList<double> NormalizePreviewAdvances(
        IReadOnlyList<double> advances,
        int characterCount,
        double targetExtent)
    {
        if (characterCount <= 0) return [];
        var usable = advances.Count == characterCount && advances.All(value => double.IsFinite(value) && value > 0)
            ? advances.ToArray()
            : Enumerable.Repeat(Math.Max(1, targetExtent) / characterCount, characterCount).ToArray();
        var total = usable.Sum();
        if (total <= 0) return [];
        var result = usable.Select(value => Math.Max(1, targetExtent) * value / total).ToArray();
        result[^1] += Math.Max(1, targetExtent) - result.Sum();
        return result;
    }

    private static string JoinTextElementsVertically(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
        return string.Join(Environment.NewLine, elements);
    }
}
