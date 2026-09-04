namespace PdfCorrectorium.ProjectFormat;

/// <summary>.pdfocrproj読込時の資源上限です。大規模案件では明示的に差し替えられます。</summary>
public sealed record ProjectPackageLimits
{
    /// <summary>圧縮済みプロジェクトファイル自体の最大バイト数です。</summary>
    public long MaximumArchiveBytes { get; init; } = 18L * 1024 * 1024 * 1024;
    /// <summary>アーカイブ内に許可するエントリ総数です。</summary>
    public int MaximumEntryCount { get; init; } = 8192;
    /// <summary>project.jsonなどJSONエントリを展開した後の最大バイト数です。</summary>
    public long MaximumJsonEntryBytes { get; init; } = 256L * 1024 * 1024;
    /// <summary>許可するサムネイルエントリ数です。</summary>
    public int MaximumThumbnailCount { get; init; } = 4096;
    /// <summary>1枚のサムネイルを展開した後の最大バイト数です。</summary>
    public long MaximumThumbnailEntryBytes { get; init; } = 4L * 1024 * 1024;
    /// <summary>サムネイル全体を展開した後の最大バイト数です。</summary>
    public long MaximumTotalThumbnailBytes { get; init; } = 512L * 1024 * 1024;
    /// <summary>内包元PDFを展開した後の最大バイト数です。</summary>
    public long MaximumEmbeddedPdfBytes { get; init; } = 16L * 1024 * 1024 * 1024;
    /// <summary>アーカイブ全体を展開した後の最大バイト数です。</summary>
    public long MaximumTotalUncompressedBytes { get; init; } = 17L * 1024 * 1024 * 1024;
    /// <summary>圧縮前サイズを圧縮後サイズで割った最大比率です。</summary>
    public double MaximumCompressionRatio { get; init; } = 1000;
}
