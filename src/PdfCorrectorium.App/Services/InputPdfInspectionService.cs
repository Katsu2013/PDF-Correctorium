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
    public static async Task<IReadOnlyList<PdfInputIssue>> InspectAsync(string pdfPath, CancellationToken cancellationToken)
    {
        var issues = new List<PdfInputIssue>();
        var properties = await PdfDocumentPropertiesService.ReadAsync(pdfPath, cancellationToken, useInspectionWorker: true);
        if (!string.IsNullOrWhiteSpace(properties.ErrorMessage))
            issues.Add(new("properties.partial", PdfInputIssueSeverity.Warning, "PDF情報の一部を取得できません", properties.ErrorMessage, "構造"));
        if (!properties.SecurityMethodText.Contains("なし", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("security.present", PdfInputIssueSeverity.Critical, "セキュリティ設定があります", $"{properties.SecurityMethodText}。権限設定により編集または出力が制限される場合があります。", "セキュリティ"));
        var unembedded = properties.Fonts.Where(font => !font.IsInvisibleOnly && string.Equals(font.EmbeddedText, "いいえ", StringComparison.Ordinal)).Select(font => font.Name).Distinct().Take(8).ToArray();
        if (unembedded.Length > 0)
            issues.Add(new("fonts.not_embedded", PdfInputIssueSeverity.Warning, "埋め込まれていないフォントがあります", string.Join("、", unembedded) + "。表示環境によって外観が変わる可能性があります。", "フォント"));

        var features = await PdfInputStructureService.ReadAsync(pdfPath, cancellationToken);
        if (features.HasForm)
            issues.Add(new("forms.present", PdfInputIssueSeverity.Warning, "フォームがあります", "入力値と外観が出力後も維持されているか確認してください。", "対話機能"));
        if (features.HasXfa)
            issues.Add(new("xfa.present", PdfInputIssueSeverity.Critical, "XFAフォームがあります", "XFAはビューアごとに互換性が異なります。出力結果を対応ビューアで確認してください。", "対話機能"));
        if (features.HasJavaScript || features.HasAdditionalActions || features.HasOpenAction)
            issues.Add(new("actions.present", PdfInputIssueSeverity.Critical, "自動実行の可能性がある動作設定があります", "PDF内の自動処理は実行せず、編集・出力後の動作を確認してください。", "アクション"));
        if (features.HasAttachments)
            issues.Add(new("attachments.present", PdfInputIssueSeverity.Warning, "添付ファイルがあります", "出力PDFで添付ファイルが維持されていることを確認してください。", "添付ファイル"));
        if (features.HasLayers)
            issues.Add(new("layers.present", PdfInputIssueSeverity.Warning, "レイヤーがあります", "表示状態が出力後も意図どおりか確認してください。", "表示"));
        if (features.HasSignature)
            issues.Add(new("signature.present", PdfInputIssueSeverity.Critical, "電子署名があります", "PDFを変更すると既存の署名は無効になります。", "署名"));
        if (features.HasCollection)
            issues.Add(new("portfolio.present", PdfInputIssueSeverity.Warning, "PDFポートフォリオです", "構成ファイルと表示方法が出力後も維持されているか確認してください。", "構造"));
        if (features.HasIncrementalUpdates)
            issues.Add(new("incremental_updates.present", PdfInputIssueSeverity.Information, "差分更新履歴があります", "出力時にはPDF内部の更新履歴が再構成される場合があります。", "構造"));
        return issues;
    }
}
