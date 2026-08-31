using System.IO;
using System.Text.Json;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App.Services;

/// <summary>端末固有の履歴。設定の移出入や配置プリセットとは分離して保存します。</summary>
public sealed class RecentFilesService
{
    public const int MaximumCount = 30;
    private const int MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string HistoryPath { get; }
    public IReadOnlyList<string> Files { get; private set; } = [];

    public RecentFilesService(ApplicationPaths paths)
    {
        HistoryPath = Path.Combine(paths.ConfigurationDirectory, "recent-files.json");
        Reload();
    }

    public void Reload() => Files = Read();

    private IReadOnlyList<string> Read()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return [];
            using var stream = new FileStream(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > MaximumFileBytes) return [];
            var data = JsonSerializer.Deserialize<History>(stream, Options);
            if (data is null || data.FormatVersion != 1) return [];
            return Normalize(data.Files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    internal static IReadOnlyList<string> Normalize(IEnumerable<string>? paths) =>
        (paths ?? []).Select(NormalizePath).Where(p => p is not null).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumCount).ToArray();

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.Any(char.IsControl)) return null;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return null;
            var full = Path.GetFullPath(path);
            var extension = Path.GetExtension(full);
            return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".pdfocrproj", StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public Task RecordAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = NormalizePath(path) ?? throw new ArgumentException("Recent files must be absolute PDF/project paths.", nameof(path));
        return UpdateAsync(files => Normalize(new[] { full }.Concat(files)), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => UpdateAsync(_ => [], cancellationToken);

    private async Task UpdateAsync(Func<IReadOnlyList<string>, IReadOnlyList<string>> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            // Serialize read/merge/replace across application instances; never hold a PDF open.
            await using var fileLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
            var next = update(Read());
            await SettingsTransferService.WriteAtomicallyAsync(HistoryPath,
                JsonSerializer.Serialize(new History { Files = next }, Options), cancellationToken).ConfigureAwait(false);
            Files = next;
        }
        finally { _gate.Release(); }
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(HistoryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 20)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed record History
    {
        public int FormatVersion { get; init; } = 1;
        public IReadOnlyList<string>? Files { get; init; } = [];
    }
}
