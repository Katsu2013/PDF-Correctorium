namespace PdfCorrectorium.App.Services;

internal static class PdfiumSynchronization
{
    private static bool _initialized;

    internal static object Gate { get; } = new();

    /// <summary>PDFium をプロセス全体で一度だけ初期化します。</summary>
    /// <remarks>
    /// プレビューとエクスポートは同じネイティブ DLL を共有するため、サービス単位の
    /// 二重初期化を防ぎ、初期化処理も他の PDFium 呼び出しと直列化します。
    /// </remarks>
    internal static void EnsureInitialized(Action initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        lock (Gate)
        {
            if (_initialized) return;
            initialize();
            _initialized = true;
        }
    }
}
