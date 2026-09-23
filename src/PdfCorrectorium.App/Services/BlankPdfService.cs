using System.Globalization;
using System.IO;
using System.Text;

namespace PdfCorrectorium.App.Services;

/// <summary>外部ライブラリに依存せず、内容を持たない標準PDFページを生成します。</summary>
public static class BlankPdfService
{
    /// <summary>指定サイズの空白ページだけを持つPDFを作成します。</summary>
    public static async Task CreateAsync(
        string outputPath,
        int pageCount,
        double widthPoints,
        double heightPoints,
        CancellationToken cancellationToken = default)
    {
        if (pageCount is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(pageCount));
        if (!double.IsFinite(widthPoints) || !double.IsFinite(heightPoints) ||
            widthPoints < 36d || heightPoints < 36d || widthPoints > 14400d || heightPoints > 14400d)
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "ページ寸法がPDFの許容範囲外です。");

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var offsets = new List<long> { 0 };
            await WriteAsciiAsync(stream, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n", cancellationToken);

            var pageObjectStart = 3;
            var contentObjectStart = pageObjectStart + pageCount;
            var kids = string.Join(' ', Enumerable.Range(0, pageCount).Select(index => $"{pageObjectStart + index} 0 R"));
            await WriteObjectAsync(stream, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>", cancellationToken);
            await WriteObjectAsync(stream, offsets, 2, $"<< /Type /Pages /Count {pageCount} /Kids [{kids}] >>", cancellationToken);

            var width = widthPoints.ToString("0.###", CultureInfo.InvariantCulture);
            var height = heightPoints.ToString("0.###", CultureInfo.InvariantCulture);
            for (var index = 0; index < pageCount; index++)
            {
                await WriteObjectAsync(stream, offsets, pageObjectStart + index,
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << >> /Contents {contentObjectStart + index} 0 R >>",
                    cancellationToken);
            }
            for (var index = 0; index < pageCount; index++)
                await WriteObjectAsync(stream, offsets, contentObjectStart + index, "<< /Length 0 >>\nstream\n\nendstream", cancellationToken);

            var xrefOffset = stream.Position;
            await WriteAsciiAsync(stream, $"xref\n0 {offsets.Count}\n0000000000 65535 f \n", cancellationToken);
            foreach (var offset in offsets.Skip(1))
                await WriteAsciiAsync(stream, $"{offset:0000000000} 00000 n \n", cancellationToken);
            await WriteAsciiAsync(stream,
                $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n",
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Close();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { }
            throw;
        }
    }

    private static async Task WriteObjectAsync(
        Stream stream,
        List<long> offsets,
        int objectNumber,
        string body,
        CancellationToken cancellationToken)
    {
        if (objectNumber != offsets.Count)
            throw new InvalidOperationException("PDFオブジェクト番号が連続していません。");
        offsets.Add(stream.Position);
        await WriteAsciiAsync(stream, $"{objectNumber} 0 obj\n{body}\nendobj\n", cancellationToken);
    }

    private static Task WriteAsciiAsync(Stream stream, string value, CancellationToken cancellationToken) =>
        stream.WriteAsync(Encoding.Latin1.GetBytes(value), cancellationToken).AsTask();
}
