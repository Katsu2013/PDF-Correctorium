namespace PdfCorrectorium.Core.Documents;

/// <summary>PDF出力時に使用するPDF仕様バージョンの選択肢です。</summary>
public enum PdfOutputVersion
{
    /// <summary>入力PDFと編集内容に応じて出力処理へ決定を委ねます。</summary>
    Automatic,
    /// <summary>PDF 1.4として出力します。</summary>
    Pdf14,
    /// <summary>PDF 1.5として出力します。</summary>
    Pdf15,
    /// <summary>PDF 1.6として出力します。</summary>
    Pdf16,
    /// <summary>PDF 1.7として出力します。</summary>
    Pdf17,
    /// <summary>PDF 2.0として出力します。</summary>
    Pdf20,
}

/// <summary>出力バージョンのモデル値とPDFのバージョン表記を相互変換します。</summary>
public static class PdfOutputVersionMapping
{
    /// <summary>明示選択をqpdfへ渡すバージョン文字列へ変換します。自動の場合はnullです。</summary>
    public static string? GetVersionString(PdfOutputVersion version) => version switch
    {
        PdfOutputVersion.Automatic => null,
        PdfOutputVersion.Pdf14 => "1.4",
        PdfOutputVersion.Pdf15 => "1.5",
        PdfOutputVersion.Pdf16 => "1.6",
        PdfOutputVersion.Pdf17 => "1.7",
        PdfOutputVersion.Pdf20 => "2.0",
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
    };

    /// <summary>PDFiumが返す整数形式のバージョン番号へ変換します。自動の場合はnullです。</summary>
    public static int? GetPdfiumVersion(PdfOutputVersion version) => version switch
    {
        PdfOutputVersion.Automatic => null,
        PdfOutputVersion.Pdf14 => 14,
        PdfOutputVersion.Pdf15 => 15,
        PdfOutputVersion.Pdf16 => 16,
        PdfOutputVersion.Pdf17 => 17,
        PdfOutputVersion.Pdf20 => 20,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
    };

    /// <summary>画面表示された元PDFのバージョンより低い出力が選択されているかを判定します。</summary>
    public static bool IsLowerThanSource(PdfOutputVersion version, string? sourceVersion)
    {
        var requested = GetPdfiumVersion(version);
        return requested is not null &&
               TryParsePdfiumVersion(sourceVersion, out var source) &&
               requested.Value < source;
    }

    /// <summary>「1.7」等のPDFバージョン文字列をPDFium形式へ変換します。</summary>
    public static bool TryParsePdfiumVersion(string? value, out int version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split('.', 2);
        return parts.Length == 2 &&
               int.TryParse(parts[0], out var major) &&
               int.TryParse(parts[1], out var minor) &&
               major is >= 1 and <= 2 && minor is >= 0 and <= 9 &&
               (version = major * 10 + minor) > 0;
    }
}
