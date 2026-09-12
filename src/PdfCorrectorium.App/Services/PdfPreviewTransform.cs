using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfCorrectorium.App.Services;

/// <summary>元PDFを変更せず、論理ページの回転をプレビュー結果へ適用します。</summary>
public static class PdfPreviewTransform
{
    public static PdfPreviewResult Rotate(
        PdfPreviewResult source,
        int clockwiseDegrees,
        int logicalPageNumber,
        int logicalPageCount)
    {
        var rotation = Normalize(clockwiseDegrees);
        if (rotation == 0)
            return source with { PageNumber = logicalPageNumber, PageCount = logicalPageCount };
        if (rotation % 90 != 0)
            throw new ArgumentOutOfRangeException(nameof(clockwiseDegrees), "Page rotation must be a multiple of 90 degrees.");

        var transformed = new TransformedBitmap(source.Image, new RotateTransform(rotation));
        transformed.Freeze();
        var regions = source.TextRegions
            .Select(region => RotateRegion(region, source.Image.PixelWidth, source.Image.PixelHeight, rotation))
            .ToArray();
        var swapsAxes = rotation is 90 or 270;
        return new PdfPreviewResult(
            transformed,
            logicalPageCount,
            logicalPageNumber,
            swapsAxes ? source.PageHeightPoints : source.PageWidthPoints,
            swapsAxes ? source.PageWidthPoints : source.PageHeightPoints,
            regions);
    }

    public static IReadOnlyList<PdfTextOverlayRegion> RotateRegions(
        IReadOnlyList<PdfTextOverlayRegion> regions,
        double sourceWidth,
        double sourceHeight,
        int clockwiseDegrees) => regions
        .Select(region => RotateRegion(region, sourceWidth, sourceHeight, Normalize(clockwiseDegrees)))
        .ToArray();

    private static PdfTextOverlayRegion RotateRegion(
        PdfTextOverlayRegion region,
        double sourceWidth,
        double sourceHeight,
        int rotation) => rotation switch
        {
            0 => region,
            90 => region with
            {
                Left = sourceHeight - region.Top - region.Height,
                Top = region.Left,
                Width = region.Height,
                Height = region.Width,
                RotationDegrees = Normalize(region.RotationDegrees + 90),
            },
            180 => region with
            {
                Left = sourceWidth - region.Left - region.Width,
                Top = sourceHeight - region.Top - region.Height,
                RotationDegrees = Normalize(region.RotationDegrees + 180),
            },
            270 => region with
            {
                Left = region.Top,
                Top = sourceWidth - region.Left - region.Width,
                Width = region.Height,
                Height = region.Width,
                RotationDegrees = Normalize(region.RotationDegrees + 270),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
        };

    private static int Normalize(double degrees)
    {
        var rounded = (int)Math.Round(degrees) % 360;
        if (rounded < 0) rounded += 360;
        return rounded;
    }
}
