using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// 検証済みのqpdfページ合成処理を使い、抽出・分割を一貫した方法で実行します。
/// </summary>
public sealed class PdfDocumentOrganizationService(PdfPageManagementService pageManagement)
{
    /// <summary>指定ページだけを元の並び順で抽出します。</summary>
    public async Task ExtractAsync(
        string sourcePdfPath,
        IReadOnlyList<int> pageNumbers,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (pageNumbers.Count == 0) throw new ArgumentException("抽出するページがありません。", nameof(pageNumbers));
        var range = string.Join(',', pageNumbers.Distinct().Order());
        var destination = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var stagingPath = destination + $".{Guid.NewGuid():N}.tmp.pdf";
        try
        {
            await pageManagement.ComposeAsync(
                sourcePdfPath,
                [new PdfPageManagementService.PageSource(sourcePdfPath, range)],
                stagingPath,
                cancellationToken).ConfigureAwait(false);
            File.Move(stagingPath, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(stagingPath)) File.Delete(stagingPath); } catch (IOException) { }
        }
    }

    /// <summary>
    /// ページグループごとにPDFを分割します。全ファイルを一時フォルダーで完成させてから出力先へ確定します。
    /// </summary>
    public async Task<IReadOnlyList<string>> SplitAsync(
        string sourcePdfPath,
        IReadOnlyList<IReadOnlyList<int>> groups,
        string outputDirectory,
        string baseFileName,
        CancellationToken cancellationToken = default)
    {
        if (groups.Count == 0 || groups.Any(group => group.Count == 0))
            throw new ArgumentException("分割するページグループが正しくありません。", nameof(groups));
        var destinationDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var safeBaseName = MakeSafeFileName(baseFileName);
        var stagingDirectory = Path.Combine(destinationDirectory, $".pdfcorrectorium-split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        var staged = new List<(string TemporaryPath, string DestinationPath)>();
        try
        {
            for (var index = 0; index < groups.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pages = groups[index].Distinct().Order().ToArray();
                var suffix = pages.Length == 1 ? $"p{pages[0]:0000}" : $"p{pages[0]:0000}-{pages[^1]:0000}";
                var fileName = FindAvailableFileName(destinationDirectory, $"{safeBaseName}-{suffix}.pdf");
                var temporaryPath = Path.Combine(stagingDirectory, fileName);
                await ExtractAsync(sourcePdfPath, pages, temporaryPath, cancellationToken).ConfigureAwait(false);
                staged.Add((temporaryPath, Path.Combine(destinationDirectory, fileName)));
            }

            foreach (var item in staged)
                File.Move(item.TemporaryPath, item.DestinationPath);
            return staged.Select(item => item.DestinationPath).ToArray();
        }
        catch
        {
            foreach (var item in staged)
            {
                try { if (File.Exists(item.DestinationPath)) File.Delete(item.DestinationPath); } catch (IOException) { }
            }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true); } catch (IOException) { }
        }
    }

    public static IReadOnlyList<IReadOnlyList<int>> BuildFixedSizeGroups(int pageCount, int pagesPerFile)
    {
        if (pageCount < 1) throw new ArgumentOutOfRangeException(nameof(pageCount));
        if (pagesPerFile < 1) throw new ArgumentOutOfRangeException(nameof(pagesPerFile));
        return Enumerable.Range(1, pageCount)
            .Chunk(pagesPerFile)
            .Select(group => (IReadOnlyList<int>)group)
            .ToArray();
    }

    public static IReadOnlyList<IReadOnlyList<int>> BuildBoundaryGroups(int pageCount, IReadOnlyList<int> starts)
    {
        if (pageCount < 1) throw new ArgumentOutOfRangeException(nameof(pageCount));
        var normalized = starts.Append(1).Where(page => page >= 1 && page <= pageCount).Distinct().Order().ToArray();
        return normalized.Select((start, index) =>
        {
            var end = index + 1 < normalized.Length ? normalized[index + 1] - 1 : pageCount;
            return (IReadOnlyList<int>)Enumerable.Range(start, end - start + 1).ToArray();
        }).ToArray();
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "document" : result;
    }

    private static string FindAvailableFileName(string directory, string preferredName)
    {
        if (!File.Exists(Path.Combine(directory, preferredName))) return preferredName;
        var stem = Path.GetFileNameWithoutExtension(preferredName);
        var extension = Path.GetExtension(preferredName);
        for (var index = 2; index < 10000; index++)
        {
            var candidate = $"{stem}-{index}{extension}";
            if (!File.Exists(Path.Combine(directory, candidate))) return candidate;
        }
        throw new IOException("分割PDFの重複しないファイル名を作成できませんでした。");
    }
}
