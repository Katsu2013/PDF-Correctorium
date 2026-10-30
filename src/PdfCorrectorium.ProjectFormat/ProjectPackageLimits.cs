using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.ProjectFormat;

/// <summary>.pdfocrproj読込時の資源上限です。大規模案件では明示的に差し替えられます。</summary>
public sealed record ProjectPackageLimits
{
    /// <summary>旧形式のページ数・OCRページ番号を含む、正規化前の最大ページ数です。</summary>
    public int MaximumPageCount { get; init; } = ProjectPageSequence.DefaultMaximumPageCount;
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
    public int MaximumJsonTokens { get; init; } = 16_000_000;
    public int MaximumJsonObjects { get; init; } = 1_000_000;
    public int MaximumJsonArrayItems { get; init; } = 100_000;
    public int MaximumOcrRegionsPerPage { get; init; } = 10_000;
    public int MaximumOcrRegionCount { get; init; } = 200_000;
    public int MaximumOcrTextCharacters { get; init; } = 32_768;
    public long MaximumTotalJsonStringCharacters { get; init; } = 64L * 1024 * 1024;
    public int MaximumBookmarkCount { get; init; } = 100_000;
    public int MaximumCommentCount { get; init; } = 100_000;
    public int MaximumTagCount { get; init; } = 4_096;
    public int MaximumInternalLinkCount { get; init; } = 100_000;
    public int MaximumTextDecorationCount { get; init; } = 1_000;
    public int MaximumRedactionCount { get; init; } = 100_000;
    public int MaximumRedactionPathPoints { get; init; } = 4_096;
    public int MaximumCommentCharacters { get; init; } = 32_768;
    public int MaximumTagNameCharacters { get; init; } = 128;
    /// <summary>外部リンク1件のURIに許可する最大文字数です。</summary>
    public int MaximumLinkUriCharacters { get; init; } = 8_192;
}
