using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.IO;

namespace PdfCorrectorium.App.Services;

internal sealed record OcrTextExportPage(int PageNumber, IReadOnlyList<string> Lines);

/// <summary>Work-data exports, not sanitized sharing artifacts. No PDF parsing or image allocation.</summary>
internal static class DocumentDataWriter
{
    internal const long MaximumOutputBytes = 64L * 1024 * 1024;

    internal static async Task WriteTextAsync(Stream output, IAsyncEnumerable<OcrTextExportPage> pages,
        bool pageHeadings, CancellationToken token)
    {
        await using var limited = new LengthLimitedWriteStream(output, MaximumOutputBytes);
        await using var writer = new StreamWriter(limited, new UTF8Encoding(false), 8192, leaveOpen: true);
        var first = true;
        await foreach (var page in pages.WithCancellation(token))
        {
            if (!first) await writer.WriteLineAsync("\f".AsMemory(), token);
            first = false;
            if (pageHeadings) await writer.WriteLineAsync($"--- PDF {page.PageNumber} ---".AsMemory(), token);
            foreach (var line in page.Lines)
            {
                token.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(line.AsMemory(), token);
            }
        }
        await writer.FlushAsync(token);
    }

    internal static async Task WriteCommentsAsync(Stream output, PdfCorrectoriumProject project, CancellationToken token)
    {
        await using var limited = new LengthLimitedWriteStream(output, MaximumOutputBytes);
        await using var writer = new StreamWriter(limited, new UTF8Encoding(true), 8192, leaveOpen: true);
        await writer.WriteLineAsync("\"ID\",\"対象種別\",\"PDFページ\",\"コメント\",\"対応状態\",\"重要度\",\"タグ\",\"作成者\",\"作成日時\",\"更新日時\"".AsMemory(), token);
        var pages = project.PageSequence.Select((page, index) => (page.PageId, Number: index + 1))
            .GroupBy(page => page.PageId).ToDictionary(group => group.Key, group => group.First().Number);
        var tags = project.Tags.ToDictionary(tag => tag.Id, tag => tag.Name);
        foreach (var comment in project.Comments)
        {
            token.ThrowIfCancellationRequested();
            string[] fields = [comment.Id.ToString(), comment.Target.Kind.ToString(),
                comment.Target.PageId is { } id && pages.TryGetValue(id, out var number) ? number.ToString(CultureInfo.InvariantCulture) : "",
                comment.Body, comment.State.ToString(), comment.Importance.ToString(),
                string.Join("; ", comment.TagIds.Select(id => tags.GetValueOrDefault(id, id.ToString()))),
                comment.Author ?? "", comment.CreatedAtUtc.ToString("O"), comment.UpdatedAtUtc.ToString("O")];
            await writer.WriteLineAsync(string.Join(',', fields.Select(CsvCell)).AsMemory(), token);
        }
        await writer.FlushAsync(token);
    }

    internal static string CsvCell(string value)
    {
        var index = 0;
        while (index < value.Length && (char.IsWhiteSpace(value[index]) || char.IsControl(value[index]) || value[index] is '\uFEFF' or '\u200B')) index++;
        if ((index < value.Length && value[index] is '=' or '+' or '-' or '@') ||
            (value.Length > 0 && char.IsControl(value[0]))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    internal static async Task WriteAtomicAsync(string destination, IEnumerable<string?> protectedInputs,
        Func<Stream, Task> write, CancellationToken token)
    {
        var full = Path.GetFullPath(destination);
        var resolvedDestination = ResolveExistingPath(full);
        if (protectedInputs.Where(path => !string.IsNullOrWhiteSpace(path))
            .Any(path => string.Equals(ResolveExistingPath(Path.GetFullPath(path!)), resolvedDestination, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("入力PDF・プロジェクト・OCRデータは上書きできません。別の保存先を指定してください。");
        var directory = Path.GetDirectoryName(full) ?? throw new IOException("保存先フォルダーがありません。");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("リンクされた出力先は置換できません。別の保存先を指定してください。");
        var temporary = Path.Combine(directory, $".pdf-correctorium-data-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                await write(stream);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ResolveExistingPath(string path)
    {
        if (!File.Exists(path)) return path;
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new IOException("保存先または入力ファイルの実体パスを確認できません。");
        return buffer.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
