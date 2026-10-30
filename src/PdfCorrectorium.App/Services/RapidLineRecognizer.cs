using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.Core.Analysis;
using RapidOcrNet;
using SkiaSharp;

namespace PdfCorrectorium.App.Services;

internal sealed record OcrLineCrop(double Left, double Top, double Width, double Height,
    double Rotation, double PreviewWidth, double PreviewHeight, int PageRotation);
/// <summary>The old row's unrotated bounds in the returned source crop, including its subpixel origin.</summary>
internal sealed record RapidLineRasterBounds(double Left, double Top, double Width, double Height);
internal sealed record RapidLineRecognition(OcrLineReading Reading, RapidLineRasterBounds? RasterBounds = null,
    RapidLineRasterBounds? ContentBounds = null);
internal sealed record RapidLineResult(RapidLineRecognition Recognition, BitmapSource Image);

/// <summary>Worker-only recognition. No detector, classifier, model download or screen capture.</summary>
internal sealed class RapidLineRecognizer : IDisposable
{
    private TextRecognizer? _recognizer;
    private string? _pageKey;
    private BitmapSource? _page;

    internal RapidLineResult Recognize(string pdfPath, int page, OcrLineCrop crop, CancellationToken token)
    {
        var key = $"{pdfPath}|{page}|{crop.PageRotation}";
        if (key != _pageKey)
        {
            var rendered = PdfPreviewService.RenderImagePageInProcess(pdfPath, page, 300, token);
            _page = PdfPreviewTransform.Rotate(rendered, crop.PageRotation, page, rendered.PageCount).Image;
            _pageKey = key;
        }
        var (image, bounds) = CreateCrop(_page!, crop);
        var prepared = PrepareImage(image);
        using var bitmap = prepared.Bitmap;
        EnsureModel(); token.ThrowIfCancellationRequested();
        var reading = ReadPadded(bitmap, Read, token);
        return new(new(reading, bounds, prepared.ContentBounds), image);
    }

    /// <summary>Recognition-only white padding, wider vertically. Invokes recognition exactly once; never applied as character geometry.</summary>
    internal static OcrLineReading ReadPadded(SKBitmap bitmap,
        Func<SKBitmap, CancellationToken, OcrLineReading> recognize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var verticalPadding = Math.Clamp(bitmap.Height / 8, 6, 16);
        using var padded = new SKBitmap(bitmap.Width + 4, bitmap.Height + verticalPadding * 2);
        using (var canvas = new SKCanvas(padded)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(bitmap, 2, verticalPadding); }
        var reading = recognize(padded, token);
        if (string.IsNullOrWhiteSpace(reading.Text))
            throw new InvalidDataException("文字を再認識できませんでした。選択行の範囲や原画像を確認してください。");
        return reading;
    }

    /// <summary>Content bounds refer to source pixels, never to the white recognition border.</summary>
    internal static (SKBitmap Bitmap, RapidLineRasterBounds ContentBounds) PrepareImage(BitmapSource image)
    {
        using var source = ToSkia(image);
        var left = source.Width; var top = source.Height; var right = -1; var bottom = -1;
        var pixels = source.GetPixelSpan();
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var offset = y * source.RowBytes + x * 4;
            if (pixels[offset] + pixels[offset + 1] + pixels[offset + 2] >= 630) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top) throw new InvalidDataException("選択行の画像に文字を検出できませんでした。");
        // Retain all sampled ink, including detached punctuation, then add a small
        // scale-relative border. Long blank tails still must not enter the recognizer.
        var border = Math.Clamp((bottom - top + 1) / 12, 2, 8);
        var result = new SKBitmap(right - left + 1 + border * 2, bottom - top + 1 + border * 2,
            SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result); canvas.Clear(SKColors.White); canvas.DrawBitmap(source, border - left, border - top);
        return (result, new(left, top, right - left + 1, bottom - top + 1));
    }

    private void EnsureModel()
    {
        if (_recognizer is not null) return;
        var root = Path.Combine(AppContext.BaseDirectory, "components", "rapidocr");
        var model = Path.Combine(root, "japan_PP-OCRv4_rec_mobile.onnx");
        var dictionary = Path.Combine(root, "japan_dict.txt");
        CheckHash(model, "E1075A67DBA758ECFC7EBC78A10AE61C95AC8FB66A9C86FAB5541E33F085CB7A");
        CheckHash(dictionary, "1DCFCB41EEC90576A945B3084F22ADE11CED506E24F14879245B071698F308E8");
        var recognizer = new TextRecognizer();
        try { recognizer.InitModel(model, dictionary, Math.Clamp(Environment.ProcessorCount / 2, 1, 4)); _recognizer = recognizer; }
        catch { recognizer.Dispose(); throw; }
    }
    private static void CheckHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(stream)) != expected)
            throw new InvalidDataException("同梱OCRモデルまたは辞書が正しくありません。配布物を展開し直してください。");
    }
    private OcrLineReading Read(SKBitmap bitmap, CancellationToken token)
    {
        var line = _recognizer!.GetTextLine(bitmap, token);
        var chars = line.Chars ?? [];
        var first = 0; var end = chars.Length;
        // Recognition padding can produce edge spaces. Internal whitespace is never normalized.
        while (first < end && string.IsNullOrWhiteSpace(chars[first])) first++;
        while (end > first && string.IsNullOrWhiteSpace(chars[end - 1])) end--;
        var text = string.Concat(chars[first..end]);
        if (string.IsNullOrWhiteSpace(text)) return new("", [], [], []);
        if (text.Length > OcrLineReconciler.MaximumElements * 4 || end - first > OcrLineReconciler.MaximumElements)
            throw new InvalidDataException("再認識結果が1行の文字数上限を超えています。行を分割してください。");
        var scores = line.CharScores?.Skip(first).Take(end - first).Select(v => (double)v).ToArray() ?? [];
        var positions = line.CharCols?.Skip(first).Take(end - first).Select(v => (v + .5) / Math.Max(1, line.ColCount)).ToArray() ?? [];
        var ink = positions.Select(position => HasInk(bitmap, position)).ToArray();
        return new(text, scores, positions, ink);
    }
    private static bool HasInk(SKBitmap bitmap, double position)
    {
        var x = (int)(position * bitmap.Width); var radius = Math.Max(1, bitmap.Height / 4);
        var count = 0; var minimum = 765; var maximum = 0;
        var pixels = bitmap.GetPixelSpan();
        for (var y = 1; y < bitmap.Height - 1; y++)
        for (var i = Math.Max(0, x - radius); i <= Math.Min(bitmap.Width - 1, x + radius); i++)
        {
            var offset = y * bitmap.RowBytes + i * 4;
            var value = pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
            minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value);
            if (value < 690) count++;
        }
        return count >= 3 && maximum - minimum >= 90;
    }

    internal static BitmapSource Crop(BitmapSource image, OcrLineCrop crop) => CreateCrop(image, crop).Image;

    internal static (BitmapSource Image, RapidLineRasterBounds Bounds) CreateCrop(BitmapSource image, OcrLineCrop crop)
    {
        var values = new[] { crop.Left, crop.Top, crop.Width, crop.Height, crop.Rotation, crop.PreviewWidth, crop.PreviewHeight };
        if (values.Any(v => !double.IsFinite(v)) || crop.PreviewWidth <= 0 || crop.PreviewHeight <= 0 ||
            crop.Width <= 0 || crop.Height <= 0 || crop.Left < 0 || crop.Top < 0 ||
            crop.Width > crop.PreviewWidth * 2 || crop.Height > crop.PreviewHeight * 2)
            throw new InvalidDataException("OCR行の範囲が正しくありません。");
        var sx = image.PixelWidth / crop.PreviewWidth; var sy = image.PixelHeight / crop.PreviewHeight;
        var width = crop.Width * sx; var height = crop.Height * sy;
        // Existing OCR boxes can stop before a trailing comma/full stop. Sample
        // Sample actual source pixels beyond the row, not just white after clipping.
        // The cross-axis context is wider than before, but still bounded separately.
        var horizontalPadding = Math.Clamp(height * .5, 4, 96);
        var verticalPadding = Math.Clamp(height * .30, 4, 32);
        // Anchor rectified samples to a fixed page-pixel grid. A fit must not change
        // the sampling phase (and thus add a fresh fraction of a margin) next time.
        var matrix = Matrix.Identity;
        matrix.Rotate(-crop.Rotation);
        var center = matrix.Transform(new Point(crop.Left * sx + width / 2, crop.Top * sy + height / 2));
        var rowLeft = center.X - width / 2; var rowTop = center.Y - height / 2;
        var left = Math.Floor(Math.Round(rowLeft - horizontalPadding, 6));
        var top = Math.Floor(Math.Round(rowTop - verticalPadding, 6));
        var w = (int)(Math.Ceiling(Math.Round(rowLeft + width + horizontalPadding, 6)) - left);
        var h = (int)(Math.Ceiling(Math.Round(rowTop + height + verticalPadding, 6)) - top);
        if (w < 4 || h < 4 || w > 8192 || h > 512 || w / (double)h > 160 || w * (long)h > 2_000_000)
            throw new InvalidDataException("選択範囲が1行の再認識上限を超えています。行を分割してください。");
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            matrix.Translate(-left, -top);
            context.PushTransform(new MatrixTransform(matrix));
            context.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            context.Pop();
        }
        var result = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze();
        return (result, new(rowLeft - left, rowTop - top, width, height));
    }
    private static SKBitmap ToSkia(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var result = new SKBitmap(converted.PixelWidth, converted.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, result.GetPixels(), pixels.Length);
        return result;
    }
    public void Dispose() { _recognizer?.Dispose(); _page = null; }
}
