using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfCorrectorium.App.Services;

/// <summary>旧キャッシュを、小さな単一画像としてのみ読み込みます。</summary>
internal static class ThumbnailDecoder
{
    internal const int MaximumDimension = 4096;
    internal const long MaximumPixels = 4L * 1024 * 1024;
    internal const int DisplayDimension = 220;

    internal static BitmapSource? Decode(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024) return null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            // OnDemand exposes header dimensions before materializing pixel data.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            if (decoder is not (JpegBitmapDecoder or PngBitmapDecoder) || decoder.Frames.Count != 1) return null;
            var frame = decoder.Frames[0];
            var width = frame.PixelWidth;
            var height = frame.PixelHeight;
            if (width < 1 || height < 1 || width > MaximumDimension || height > MaximumDimension ||
                (long)width * height > MaximumPixels) return null;
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            if (width >= height) image.DecodePixelWidth = Math.Min(width, DisplayDimension);
            else image.DecodePixelHeight = Math.Min(height, DisplayDimension);
            image.EndInit();
            if (image.PixelWidth > DisplayDimension || image.PixelHeight > DisplayDimension) return null;
            // Detach from the decoder and normalize to four bytes per pixel, so
            // high-bit-depth PNGs cannot inflate the retained thumbnail budget.
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var stride = checked(image.PixelWidth * 4);
            var pixels = new byte[checked(stride * image.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96,
                PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }
}
