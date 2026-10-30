using System.IO;
using System.Windows.Media.Imaging;

namespace PdfCorrectorium.App.Services;

internal static class PageImageExportService
{
    internal static async Task<string> ExportAsync(string completedPdf, string parentDirectory, IReadOnlyList<int> pages,
        int dpi, bool jpeg, int quality, IProgress<string>? progress, CancellationToken token)
    {
        if (dpi is < 72 or > 300 || quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(dpi));
        if (!Directory.Exists(parentDirectory)) throw new DirectoryNotFoundException(parentDirectory);
        var parent = Path.GetFullPath(parentDirectory);
        var result = Path.GetFullPath(Path.Combine(parent, $"PdfCorrectorium-images-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"));
        if (!result.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("画像出力先が親フォルダーの外です。");
        var selected = pages.Distinct().Order().ToArray();
        if (selected.Length == 0 || selected.Any(page => page < 1)) throw new ArgumentException("対象ページがありません。");
        var created = false;
        var createdFiles = new List<string>();
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(result); created = true;
            await using var worker = new PdfNativeWorkerClient(reuseInputs: true);
            for (var index = 0; index < selected.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var page = selected[index];
                progress?.Report($"完成ページ画像を書き出し中：{index + 1}/{selected.Length}（PDF {page}ページ）");
                var image = await Task.Run(() => worker.RenderImagePageAsync(completedPdf, page, dpi, token), token);
                var path = Path.Combine(result, $"page-{page:D6}.{(jpeg ? "jpg" : "png")}");
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = quality } : new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    createdFiles.Add(path);
                    using var stream = new LengthLimitedWriteStream(file, 64L * 1024 * 1024);
                    encoder.Save(stream);
                }, token);
            }
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch
        {
            // Only this operation's freshly generated, fixed-name flat folder is eligible.
            if (created && Directory.Exists(result) && (File.GetAttributes(result) & FileAttributes.ReparsePoint) == 0)
            {
                try
                {
                    foreach (var file in createdFiles) if (File.Exists(file)) File.Delete(file);
                    Directory.Delete(result, recursive: false);
                }
                catch (IOException error) { throw new IOException($"中断後の画像フォルダーを清掃できませんでした。未完成です：{result}", error); }
                catch (UnauthorizedAccessException error) { throw new IOException($"中断後の画像フォルダーを清掃できませんでした。未完成です：{result}", error); }
            }
            throw;
        }
    }
}
