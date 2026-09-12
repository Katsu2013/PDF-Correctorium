namespace PdfCorrectorium.App.Services;

/// <summary>見開きの左右に配置する1始まりのページ番号です。値がない側は空白になります。</summary>
/// <param name="LeftPageNumber">左側のページ番号。</param>
/// <param name="RightPageNumber">右側のページ番号。</param>
public sealed record FacingPageLayout(int? LeftPageNumber, int? RightPageNumber)
{
    /// <summary>現在ページと同じ見開きにある、もう一方のページを返します。</summary>
    public int? GetCompanionPageNumber(int currentPageNumber) =>
        LeftPageNumber == currentPageNumber ? RightPageNumber :
        RightPageNumber == currentPageNumber ? LeftPageNumber : null;
}

/// <summary>
/// 表紙設定と綴じ方向から見開きの左右配置を決めます。
/// 画面配置と隣接ページの遅延描画が同じ規則を利用できるよう、計算を副作用なしで集約します。
/// </summary>
public static class FacingPageLayoutCalculator
{
    public static FacingPageLayout Calculate(
        int currentPageNumber,
        int pageCount,
        bool showCoverSeparately,
        FacingPageBindingDirection bindingDirection)
    {
        if (pageCount <= 0) return new FacingPageLayout(null, null);

        var current = Math.Clamp(currentPageNumber, 1, pageCount);
        if (showCoverSeparately && current == 1)
        {
            return bindingDirection == FacingPageBindingDirection.RightBinding
                ? new FacingPageLayout(1, null)
                : new FacingPageLayout(null, 1);
        }

        var firstInPair = showCoverSeparately
            ? current - current % 2
            : current - (current + 1) % 2;
        int? firstPage = Math.Clamp(firstInPair, 1, pageCount);
        int? secondPage = firstPage < pageCount ? firstPage + 1 : null;

        return bindingDirection == FacingPageBindingDirection.RightBinding
            ? new FacingPageLayout(secondPage, firstPage)
            : new FacingPageLayout(firstPage, secondPage);
    }
}
