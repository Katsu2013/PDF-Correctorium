using System.IO;
using System.Text;

namespace PdfCorrectorium.App.Services;

public enum PdfInputIssueSeverity { Information, Warning, Critical, Blocking }

public sealed record PdfInputIssue(
    string Code,
    PdfInputIssueSeverity Severity,
    string Title,
    string Description,
    string Category)
{
    public string SeverityText => Severity switch
    {
        PdfInputIssueSeverity.Information => "情報",
        PdfInputIssueSeverity.Warning => "注意",
        PdfInputIssueSeverity.Critical => "重要",
        PdfInputIssueSeverity.Blocking => "処理不可",
        _ => Severity.ToString(),
    };
}

/// <summary>入力PDFの特性を読み取り、編集・出力時に注意が必要な項目を安定したコードで報告します。</summary>
internal static class InputPdfInspectionService
{
    private const int ScanLimitBytes = 64 * 1024 * 1024;

    public static async Task<IReadOnlyList<PdfInputIssue>> InspectAsync(string pdfPath, CancellationToken cancellationToken)
    {
        var issues = new List<PdfInputIssue>();
        var properties = await PdfDocumentPropertiesService.ReadAsync(pdfPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(properties.ErrorMessage))
            issues.Add(new("properties.partial", PdfInputIssueSeverity.Warning, "PDF情報の一部を取得できません", properties.ErrorMessage, "構造"));
        if (!properties.SecurityMethodText.Contains("なし", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("security.present", PdfInputIssueSeverity.Critical, "セキュリティ設定があります", $"{properties.SecurityMethodText}。権限設定により編集または出力が制限される場合があります。", "セキュリティ"));
        var unembedded = properties.Fonts.Where(font => string.Equals(font.EmbeddedText, "いいえ", StringComparison.Ordinal)).Select(font => font.Name).Distinct().Take(8).ToArray();
        if (unembedded.Length > 0)
            issues.Add(new("fonts.not_embedded", PdfInputIssueSeverity.Warning, "埋め込まれていないフォントがあります", string.Join("、", unembedded) + "。表示環境によって外観が変わる可能性があります。", "フォント"));

        var tokens = await ReadStructureSampleAsync(pdfPath, cancellationToken);
        AddTokenIssue(tokens, "/AcroForm", issues, "forms.present", PdfInputIssueSeverity.Warning, "フォームがあります", "フォームの入力値や外観は編集後に確認してください。", "対話機能");
        AddTokenIssue(tokens, "/XFA", issues, "xfa.present", PdfInputIssueSeverity.Critical, "XFAフォームがあります", "XFAはPDFビューアごとに互換性が異なり、編集後に失われる可能性があります。", "対話機能");
        AddTokenIssue(tokens, "/JavaScript", issues, "javascript.present", PdfInputIssueSeverity.Critical, "PDF JavaScriptがあります", "自動処理や画面動作が編集前後で変わる可能性があります。", "対話機能");
        AddTokenIssue(tokens, "/EmbeddedFile", issues, "attachments.present", PdfInputIssueSeverity.Warning, "添付ファイルがあります", "出力PDFで添付ファイルが維持されていることを確認してください。", "添付ファイル");
        AddTokenIssue(tokens, "/OCProperties", issues, "layers.present", PdfInputIssueSeverity.Warning, "レイヤーがあります", "表示レイヤーの状態を出力後に確認してください。", "表示");
        AddTokenIssue(tokens, "/Sig", issues, "signature.present", PdfInputIssueSeverity.Critical, "電子署名の可能性があります", "PDFを変更すると既存署名が無効になります。", "署名");
        AddTokenIssue(tokens, "/Launch", issues, "launch_action.present", PdfInputIssueSeverity.Critical, "外部起動アクションがあります", "安全のため、出力前にアクション内容を確認してください。", "アクション");
        AddTokenIssue(tokens, "/Prev", issues, "incremental_updates.present", PdfInputIssueSeverity.Information, "差分更新履歴があります", "保存時にPDF内部の更新履歴が再構成される場合があります。", "構造");
        return issues;
    }

    private static async Task<string> ReadStructureSampleAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        var headLength = (int)Math.Min(stream.Length, ScanLimitBytes / 2);
        var tailLength = (int)Math.Min(Math.Max(0, stream.Length - headLength), ScanLimitBytes - headLength);
        var buffer = new byte[headLength + tailLength];
        var offset = await ReadIntoAsync(stream, buffer.AsMemory(0, headLength), cancellationToken);
        if (tailLength > 0)
        {
            stream.Position = stream.Length - tailLength;
            offset += await ReadIntoAsync(stream, buffer.AsMemory(headLength, tailLength), cancellationToken);
        }
        return Encoding.Latin1.GetString(buffer, 0, offset);
    }

    private static async Task<int> ReadIntoAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = await stream.ReadAsync(destination[offset..], cancellationToken);
            if (read == 0) break;
            offset += read;
        }
        return offset;
    }

    private static void AddTokenIssue(string source, string token, ICollection<PdfInputIssue> issues,
        string code, PdfInputIssueSeverity severity, string title, string description, string category)
    {
        if (source.Contains(token, StringComparison.Ordinal))
            issues.Add(new(code, severity, title, description, category));
    }
}
