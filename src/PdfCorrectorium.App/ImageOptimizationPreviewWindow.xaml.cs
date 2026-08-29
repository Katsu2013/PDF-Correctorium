using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

/// <summary>
/// ページ画像最適化の背景置換候補を表示し、領域ごとの適用可否を利用者が確認する画面です。
/// </summary>
public partial class ImageOptimizationPreviewWindow : Window
{
    /// <summary>プレビューへ描画するページ相対座標の背景置換候補です。</summary>
    private readonly IReadOnlyList<PdfImageOptimizationPreviewRegion> _regions;
    /// <summary>各候補を元画像のまま保持する場合に <see langword="true"/> となる選択状態です。</summary>
    private readonly bool[] _keptRegions;
    /// <summary>プレビュー画像の縦横比と表示領域を計算するために保持する元画像です。</summary>
    private readonly BitmapSource _pageImage;
    /// <summary>領域設定を変更する前の解析結果です。</summary>
    private readonly PdfImageOptimizationAnalysis _analysis;
    /// <summary>削減率が小さい場合に注意表示する基準値です。</summary>
    private readonly double _lowSavingsGuide;

    /// <summary>
    /// 利用者が背景置換を無効にし、元画像のまま保持するよう指定したページ相対矩形を取得します。
    /// </summary>
    public IReadOnlyList<ImageOptimizationKeepRegion> KeepRegions => _regions
        .Select((region, index) => (region, index))
        .Where(item => _keptRegions[item.index])
        .Select(item => new ImageOptimizationKeepRegion
        {
            LeftRatio = item.region.LeftRatio,
            TopRatio = item.region.TopRatio,
            WidthRatio = item.region.WidthRatio,
            HeightRatio = item.region.HeightRatio,
        })
        .ToArray();

    /// <summary>ページ画像、解析結果および削減率の案内基準を受け取り、確認画面を構築します。</summary>
    public ImageOptimizationPreviewWindow(
        BitmapSource pageImage,
        PdfImageOptimizationAnalysis analysis,
        double lowSavingsGuide)
    {
        InitializeComponent();
        _pageImage = pageImage;
        _analysis = analysis;
        _lowSavingsGuide = lowSavingsGuide;
        _regions = analysis.Regions;
        _keptRegions = new bool[_regions.Count];
        PageImage.Source = pageImage;

        var byteReduction = analysis.OriginalEncodedBytes <= 0
            ? 0d
            : 1d - analysis.EstimatedEncodedBytes / (double)analysis.OriginalEncodedBytes;
        PageSummaryText.Text =
            $"{analysis.PageNumber}ページ／対象画像 {analysis.EligibleImages}個\n" +
            $"保持画像 {analysis.RetainedRegionCount}領域／背景置換 {_regions.Count}領域\n" +
            $"画像面積の削減見込み 約{analysis.EstimatedAreaReduction:P0}";
        SavingsText.Text =
            $"画像データ 約{analysis.OriginalEncodedBytes / 1024d:N1} KB → " +
            $"約{analysis.EstimatedEncodedBytes / 1024d:N1} KB（約{byteReduction:P0}削減）";

        var alpha = (byte)(analysis.BackgroundArgb >> 24);
        var red = (byte)(analysis.BackgroundArgb >> 16);
        var green = (byte)(analysis.BackgroundArgb >> 8);
        var blue = (byte)analysis.BackgroundArgb;
        BackgroundColorSwatch.Background = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        BackgroundColorText.Text = $"#{red:X2}{green:X2}{blue:X2}";
        DetectionModeText.Text = analysis.UsesUniformColorBackground
            ? "白以外の単一色背景として検出しました。"
            : "白または白に近い単一色背景として検出しました。";

        Loaded += (_, _) =>
        {
            UpdateSelectionSummary();
            DrawOverlay();
        };
        LocalizationService.Apply(this);
    }

    /// <summary>プレビュー領域のサイズ変更に合わせて、背景候補の矩形を再描画します。</summary>
    private void PreviewHost_OnSizeChanged(object sender, SizeChangedEventArgs e) => DrawOverlay();

    /// <summary>候補矩形の保持状態を反転します。</summary>
    private void RegionRectangle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || index < 0 || index >= _keptRegions.Length)
            return;
        _keptRegions[index] = !_keptRegions[index];
        UpdateSelectionSummary();
        DrawOverlay();
        e.Handled = true;
    }

    /// <summary>すべての候補を背景置換対象へ戻します。</summary>
    private void ReplaceAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        Array.Fill(_keptRegions, false);
        UpdateSelectionSummary();
        DrawOverlay();
    }

    /// <summary>すべての候補を元画像のまま保持する設定へ切り替えます。</summary>
    private void KeepAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        Array.Fill(_keptRegions, true);
        UpdateSelectionSummary();
        DrawOverlay();
    }

    /// <summary>領域数、概算削減率および低削減率の注意表示を現在の選択状態へ更新します。</summary>
    private void UpdateSelectionSummary()
    {
        var keptCount = _keptRegions.Count(value => value);
        var replacedCount = _regions.Count - keptCount;
        var allArea = _regions.Sum(region => region.WidthRatio * region.HeightRatio);
        var replacedArea = _regions
            .Select((region, index) => (region, index))
            .Where(item => !_keptRegions[item.index])
            .Sum(item => item.region.WidthRatio * item.region.HeightRatio);
        var adjustedReduction = allArea <= 0d
            ? 0d
            : _analysis.EstimatedAreaReduction * replacedArea / allArea;
        SelectionSummaryText.Text =
            $"背景置換 {replacedCount}領域／元画像を保持 {keptCount}領域\n" +
            $"選択後の面積削減見込み 約{adjustedReduction:P0}";
        LowSavingsNotice.Visibility = adjustedReduction < _lowSavingsGuide
            ? Visibility.Visible
            : Visibility.Collapsed;
        RegionList.ItemsSource = new ObservableCollection<string>(
            _regions
                .Select((region, index) => (region, index))
                .GroupBy(item => item.region.Description)
                .Select(group =>
                    $"・{group.Key}: {group.Count()}箇所（保持 {group.Count(item => _keptRegions[item.index])}）"));
    }

    /// <summary>ページ相対座標を画面座標へ変換し、置換または保持状態の矩形を描画します。</summary>
    private void DrawOverlay()
    {
        if (!IsLoaded || PreviewHost.ActualWidth <= 0 || PreviewHost.ActualHeight <= 0) return;
        OverlayCanvas.Children.Clear();
        var imageAspect = _pageImage.PixelWidth / (double)Math.Max(1, _pageImage.PixelHeight);
        var hostAspect = PreviewHost.ActualWidth / PreviewHost.ActualHeight;
        var renderedWidth = hostAspect > imageAspect
            ? PreviewHost.ActualHeight * imageAspect
            : PreviewHost.ActualWidth;
        var renderedHeight = hostAspect > imageAspect
            ? PreviewHost.ActualHeight
            : PreviewHost.ActualWidth / imageAspect;
        var offsetX = (PreviewHost.ActualWidth - renderedWidth) / 2d;
        var offsetY = (PreviewHost.ActualHeight - renderedHeight) / 2d;
        for (var index = 0; index < _regions.Count; index++)
        {
            var region = _regions[index];
            var keep = _keptRegions[index];
            var rectangle = new Rectangle
            {
                Width = Math.Max(1d, region.WidthRatio * renderedWidth),
                Height = Math.Max(1d, region.HeightRatio * renderedHeight),
                Fill = new SolidColorBrush(keep
                    ? Color.FromArgb(72, 57, 169, 107)
                    : Color.FromArgb(82, 229, 57, 53)),
                Stroke = new SolidColorBrush(keep
                    ? Color.FromRgb(34, 139, 78)
                    : Color.FromRgb(211, 47, 47)),
                StrokeThickness = keep ? 2d : 1.25d,
                StrokeDashArray = keep ? new DoubleCollection([5d, 3d]) : null,
                Cursor = Cursors.Hand,
                ToolTip = keep
                    ? $"元画像として保持：{region.Description}\nクリックで背景置換へ戻します。"
                    : $"背景へ置換：{region.Description}\nクリックで元画像を保持します。",
                Tag = index,
            };
            rectangle.MouseLeftButtonDown += RegionRectangle_OnMouseLeftButtonDown;
            Canvas.SetLeft(rectangle, offsetX + region.LeftRatio * renderedWidth);
            Canvas.SetTop(rectangle, offsetY + region.TopRatio * renderedHeight);
            OverlayCanvas.Children.Add(rectangle);
        }
    }

    /// <summary>現在の領域設定を確定し、呼び出し元へ実行指示を返します。</summary>
    private void ApplyButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
