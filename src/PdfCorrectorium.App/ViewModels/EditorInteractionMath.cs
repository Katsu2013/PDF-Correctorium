namespace PdfCorrectorium.App.ViewModels;

/// <summary>
/// プレビューの表示倍率、スクロール位置、およびOCR枠のリサイズに使う座標計算を提供します。
/// </summary>
public static class EditorInteractionMath
{
    /// <summary>
    /// ページ幅が表示領域へ収まる倍率をパーセントで計算します。
    /// </summary>
    public static double CalculateFitWidthPercent(double viewportWidth, double pageWidth, double reservedWidth = 10) =>
        !double.IsFinite(viewportWidth) || !double.IsFinite(pageWidth) || viewportWidth <= reservedWidth || pageWidth <= 0
            ? 100
            : Math.Clamp((viewportWidth - reservedWidth) / pageWidth * 100, 25, 400);

    /// <summary>
    /// ページ高さが表示領域へ収まる倍率をパーセントで計算します。
    /// </summary>
    public static double CalculateFitHeightPercent(double viewportHeight, double pageHeight, double reservedHeight = 10) =>
        !double.IsFinite(viewportHeight) || !double.IsFinite(pageHeight) || viewportHeight <= reservedHeight || pageHeight <= 0
            ? 100
            : Math.Clamp((viewportHeight - reservedHeight) / pageHeight * 100, 25, 400);

    /// <summary>
    /// ページ全体が表示領域へ収まるよう、幅基準と高さ基準の小さい倍率を返します。
    /// </summary>
    public static double CalculateFitPagePercent(
        double viewportWidth,
        double viewportHeight,
        double pageWidth,
        double pageHeight,
        double reservedSize = 10) =>
        Math.Min(
            CalculateFitWidthPercent(viewportWidth, pageWidth, reservedSize),
            CalculateFitHeightPercent(viewportHeight, pageHeight, reservedSize));

    /// <summary>
    /// 論理座標の対象を表示領域中央へ保つためのスクロールオフセットを計算します。
    /// </summary>
    public static double CalculateCenteredScrollOffset(double logicalCenter, double zoomFactor, double viewportSize) =>
        Math.Max(0, logicalCenter * zoomFactor - Math.Max(0, viewportSize) / 2d);

    /// <summary>
    /// ドラッグした辺または角に応じてOCR領域をリサイズし、ページ内へ制限します。
    /// </summary>
    /// <param name="region">変更するOCR領域。</param>
    /// <param name="direction">N、S、E、Wを組み合わせたハンドル方向。</param>
    /// <param name="horizontalChange">水平方向のドラッグ量。</param>
    /// <param name="verticalChange">垂直方向のドラッグ量。</param>
    /// <param name="pageWidth">プレビュー画像の幅。</param>
    /// <param name="pageHeight">プレビュー画像の高さ。</param>
    public static void Resize(
        OverlayRegionViewModel region,
        string direction,
        double horizontalChange,
        double verticalChange,
        double pageWidth,
        double pageHeight)
    {
        if (direction.Contains('W'))
        {
            var right = region.Left + region.Width;
            var left = Math.Clamp(region.Left + horizontalChange, 0, right - 4);
            region.Left = left;
            region.Width = right - left;
        }
        else if (direction.Contains('E'))
        {
            region.Width = Math.Clamp(region.Width + horizontalChange, 4, Math.Max(4, pageWidth - region.Left));
        }

        if (direction.Contains('N'))
        {
            var bottom = region.Top + region.Height;
            var top = Math.Clamp(region.Top + verticalChange, 0, bottom - 4);
            region.Top = top;
            region.Height = bottom - top;
        }
        else if (direction.Contains('S'))
        {
            region.Height = Math.Clamp(region.Height + verticalChange, 4, Math.Max(4, pageHeight - region.Top));
        }
    }
}
