using System.Windows;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App.Services;

public static partial class CharacterAdvanceEstimator
{
    private sealed record RegionPixels(byte[] Pixels, int Width, int Height, int Left, int Top);

    /// <summary>
    /// Copies only the raster bounding box sampled by this OCR region. The one-pixel
    /// guard covers rotation and page-edge clamping without allocating a page-sized
    /// buffer per line. Rectification selects source pixel centers consistently.
    /// </summary>
    private static RegionPixels ReadRegionPixels(BitmapSource bitmap, OverlayRegionViewModel region)
    {
        var radians = region.RotationDegrees * Math.PI / 180d;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var radiusX = (cos * region.Width + sin * region.Height) / 2;
        var radiusY = (sin * region.Width + cos * region.Height) / 2;
        var centerX = region.Left + region.Width / 2;
        var centerY = region.Top + region.Height / 2;
        var left = (int)Math.Clamp(Math.Floor(centerX - radiusX) - 1, 0, bitmap.PixelWidth - 1);
        var top = (int)Math.Clamp(Math.Floor(centerY - radiusY) - 1, 0, bitmap.PixelHeight - 1);
        var right = (int)Math.Clamp(Math.Ceiling(centerX + radiusX) + 1, 0, bitmap.PixelWidth - 1);
        var bottom = (int)Math.Clamp(Math.Ceiling(centerY + radiusY) + 1, 0, bitmap.PixelHeight - 1);
        var width = right - left + 1;
        var height = bottom - top + 1;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        bitmap.CopyPixels(new Int32Rect(left, top, width, height), pixels, stride, 0);
        return new RegionPixels(pixels, width, height, left, top);
    }
}
