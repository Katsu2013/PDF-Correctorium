using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// qpdf を使用して、元PDFを直接変更せずにページの追加、削除、並べ替え、回転を行います。
/// </summary>
public sealed class PdfPageManagementService
{
    /// <summary>1回のページ構成処理に許可する既定の最大時間です。</summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>ページ構成へ渡す1つの入力PDFとページ範囲です。</summary>
    /// <param name="PdfPath">ページを取得するPDFの絶対パス。</param>
    /// <param name="PageRange">qpdf形式のページ範囲。例: <c>1-3</c>、<c>1,4,2</c>、<c>1-z</c>。</param>
    public sealed record PageSource(string PdfPath, string PageRange);

    /// <summary>複数PDFの指定ページを指定順で結合し、新しいPDFを生成します。</summary>
    public Task ComposeAsync(
        string primaryPdfPath,
        IReadOnlyList<PageSource> sources,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (sources.Count == 0) throw new ArgumentException("少なくとも1つのページ入力が必要です。", nameof(sources));
        if (!File.Exists(primaryPdfPath)) throw new FileNotFoundException("元PDFが見つかりません。", primaryPdfPath);
        var arguments = new List<string> { primaryPdfPath, "--pages" };
        foreach (var source in sources)
        {
            if (!File.Exists(source.PdfPath)) throw new FileNotFoundException("ページ入力PDFが見つかりません。", source.PdfPath);
            arguments.Add(source.PdfPath);
            arguments.Add(source.PageRange);
        }
        arguments.Add("--");
        arguments.Add(outputPath);
        return RunAsync(arguments, outputPath, OperationTimeout, cancellationToken);
    }

    /// <summary>指定ページを相対角度で回転した新しいPDFを生成します。</summary>
    public Task RotateAsync(
        string sourcePdfPath,
        IReadOnlyCollection<int> pageNumbers,
        int clockwiseDegrees,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePdfPath)) throw new FileNotFoundException("回転元PDFが見つかりません。", sourcePdfPath);
        if (pageNumbers.Count == 0) throw new ArgumentException("回転するページが選択されていません。", nameof(pageNumbers));
        if (clockwiseDegrees is not (90 or -90)) throw new ArgumentOutOfRangeException(nameof(clockwiseDegrees));
        var range = string.Join(',', pageNumbers.Order());
        var sign = clockwiseDegrees > 0 ? "+" : "-";
        return RunAsync(
            [sourcePdfPath, $"--rotate={sign}90:{range}", outputPath],
            outputPath,
            OperationTimeout,
            cancellationToken);
    }

    private static async Task RunAsync(
        IReadOnlyList<string> arguments,
        string outputPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var qpdfPath = ResolveQpdfPath();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var result = await ExternalProcessRunner.RunAsync(qpdfPath, arguments, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException(
                $"ページ操作に失敗しました（qpdf終了コード {result.ExitCode.ToString(CultureInfo.InvariantCulture)}）。\n" +
                string.Join(Environment.NewLine, new[] { result.StandardError, result.StandardOutput }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static string ResolveQpdfPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "qpdf.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "qpdf", "bin", "qpdf.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "qpdf", "bin", "qpdf.exe")),
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("ページ操作に必要なqpdf.exeが見つかりません。配布フォルダーへqpdfを同梱してください。");
    }
}
