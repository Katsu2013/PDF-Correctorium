using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// ページ構成編集で生成したPDFをセッション単位で所有し、Undo/Redoから外れた時点で回収します。
/// </summary>
internal sealed class PageWorkingFileStore : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _sessionDirectory;
    private readonly FileStream _sessionLock;
    private bool _disposed;

    public PageWorkingFileStore(string workspaceDirectory)
    {
        _rootDirectory = Path.Combine(Path.GetFullPath(workspaceDirectory), "page-edits");
        // session.lockを開いたままにしておくことで、異常終了したセッションだけを次回起動時に判定できる。
        Directory.CreateDirectory(_rootDirectory);
        CleanupAbandonedSessions();
        _sessionDirectory = Path.Combine(_rootDirectory, $"{Environment.ProcessId:x}-{Guid.NewGuid():N}"[..18]);
        Directory.CreateDirectory(_sessionDirectory);
        _sessionLock = new FileStream(
            Path.Combine(_sessionDirectory, "session.lock"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read);
    }

    public string CreatePath()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Path.Combine(_sessionDirectory, $"p-{DateTime.UtcNow:HHmmssfff}-{Guid.NewGuid():N}"[..22] + ".pdf");
    }

    internal int FileCount => _disposed || !Directory.Exists(_sessionDirectory)
        ? 0
        : Directory.EnumerateFiles(_sessionDirectory, "*.pdf", SearchOption.TopDirectoryOnly).Count();

    public bool Owns(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var fullPath = Path.GetFullPath(path);
        return string.Equals(Path.GetDirectoryName(fullPath), _sessionDirectory, StringComparison.OrdinalIgnoreCase);
    }

    public void DeleteUnreferenced(IEnumerable<string> retainedPaths)
    {
        if (_disposed || !Directory.Exists(_sessionDirectory)) return;
        // Undo/Redoや現在の文書から到達できるPDFは残し、それ以外の中間成果物だけを回収する。
        var retained = retainedPaths.Where(Owns).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_sessionDirectory, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            if (!retained.Contains(Path.GetFullPath(file))) TryDelete(file);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionLock.Dispose();
        TryDeleteDirectory(_sessionDirectory);
    }

    private void CleanupAbandonedSessions()
    {
        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            var lockPath = Path.Combine(directory, "session.lock");
            try
            {
                using var abandonedLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                abandonedLock.Dispose();
                TryDeleteDirectory(directory);
            }
            catch (IOException)
            {
                // 別プロセスがロックを保持する稼働中セッションは変更しません。
            }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
