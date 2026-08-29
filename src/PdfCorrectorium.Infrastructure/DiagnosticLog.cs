using System.Text;

namespace PdfCorrectorium.Infrastructure;

/// <summary>診断ログの重要度を表します。</summary>
public enum LogLevel { Trace, Debug, Information, Warning, Error, Fatal }

/// <summary>
/// 日付単位のUTF-8ログへ、複数の非同期処理から安全に診断情報を書き込みます。
/// </summary>
/// <param name="logDirectory">ログファイルの保存先。</param>
/// <param name="minimumLevel">記録対象とする最低重要度。</param>
public sealed class DiagnosticLog(string logDirectory, LogLevel minimumLevel = LogLevel.Information)
{
    /// <summary>複数スレッドから同じ日次ログへ同時追記しないための排他制御です。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 利用者名をマスクした1件の診断イベントをログへ追記します。
    /// </summary>
    /// <remarks>PDF本文、APIキー、認証情報をメッセージとして渡さないでください。</remarks>
    public async Task WriteAsync(LogLevel level, string eventId, string message, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (level < minimumLevel) return;
        Directory.CreateDirectory(logDirectory);
        var safeMessage = message.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
        var line = $"{DateTimeOffset.UtcNow:O}\t{level}\t{eventId}\t{safeMessage}";
        if (exception is not null) line += $"\t{exception.GetType().Name}: {exception.Message}";
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(Path.Combine(logDirectory, $"PdfCorrectorium-{DateTime.UtcNow:yyyyMMdd}.log"), line + Environment.NewLine, Encoding.UTF8, cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
