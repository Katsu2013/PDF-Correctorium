using System.IO;
using System.Text;

namespace PdfCorrectorium.App;

/// <summary>
/// 通常のログ機構を初期化する前に発生した起動障害を、書込み可能な場所へ記録します。
/// </summary>
internal sealed class StartupDiagnostics
{
    /// <summary>複数スレッドから起動ログへ同時追記しないための排他オブジェクトです。</summary>
    private readonly object _gate = new();

    private StartupDiagnostics(string logPath)
    {
        LogPath = logPath;
    }

    public string LogPath { get; }

    /// <summary>
    /// 配布フォルダー、ユーザーデータ、テンポラリの順に書込み先を確保します。
    /// </summary>
    public static StartupDiagnostics Create(string applicationDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(applicationDirectory, "logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdfCorrectorium", "Logs"),
            Path.Combine(Path.GetTempPath(), "PdfCorrectorium", "Logs"),
        };

        foreach (var directory in candidates)
        {
            try
            {
                Directory.CreateDirectory(directory);
                // A process-specific file prevents two application instances from
                // racing on the same daily log while still keeping related logs
                // adjacent and easy to identify.
                var path = Path.Combine(
                    directory,
                    $"startup-{DateTime.Now:yyyyMMdd}-p{Environment.ProcessId}.log");
                var diagnostics = new StartupDiagnostics(path);
                diagnostics.Write("diagnostics.ready", $"Base directory: {applicationDirectory}");
                return diagnostics;
            }
            catch (Exception) when (directory != candidates[^1])
            {
                // Try the next writable location.
            }
        }

        throw new IOException("No writable startup diagnostic directory is available.");
    }

    /// <summary>
    /// 利用者名をマスクした1件の起動イベントを追記します。
    /// </summary>
    /// <returns>診断ログの保存先。</returns>
    public string Write(string eventId, string? message = null)
    {
        var sanitized = Sanitize(message ?? string.Empty);
        var line = $"{DateTimeOffset.Now:O}\t{eventId}\t{sanitized}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                using var stream = new FileStream(
                    LogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(line);
            }
            catch (IOException)
            {
                // Diagnostics must never prevent the application from starting.
            }
            catch (UnauthorizedAccessException)
            {
                // The log location may become unavailable after initialization.
            }
        }
        return LogPath;
    }

    /// <summary>
    /// 例外の詳細を起動診断ログへ記録します。
    /// </summary>
    public string WriteException(string eventId, Exception exception) =>
        Write(eventId, exception.ToString());

    private static string Sanitize(string value)
    {
        var userName = Environment.UserName;
        return string.IsNullOrWhiteSpace(userName)
            ? value
            : value.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
    }
}
