namespace PdfCorrectorium.Core.Documents;

/// <summary>
/// PDFビューアでページを進める方向を表します。
/// <c>LeftToRight</c>は左綴じ（右開き）、<c>RightToLeft</c>は右綴じ（左開き）です。
/// </summary>
public enum BindingDirection { LeftToRight, RightToLeft }
/// <summary>PDFを開いた直後に使用するページレイアウトを表します。</summary>
public enum InitialPageMode { SinglePage, Continuous, FacingPages }

/// <summary>
/// PDFカタログのページレイアウトおよびViewerPreferencesへ反映する表示設定です。
/// </summary>
public sealed record ViewerSettings
{
    /// <summary>ページを左から右、または右から左へ進める方向です。</summary>
    public BindingDirection BindingDirection { get; init; } = BindingDirection.LeftToRight;
    /// <summary>PDFを開いた直後のページ配置方式です。</summary>
    public InitialPageMode PageMode { get; init; } = InitialPageMode.FacingPages;
    /// <summary>見開き時に1ページ目を表紙として単独表示するかを指定します。</summary>
    public bool ShowCoverSeparately { get; init; } = true;
}

/// <summary>
/// プロジェクトと元PDFを安全に対応付けるための参照情報です。
/// </summary>
/// <remarks>
/// パスだけでなくSHA-256とファイルサイズも保持し、同名の別PDFへ編集内容を
/// 誤適用することを防ぎます。
/// </remarks>
public sealed record SourcePdfReference
{
    /// <summary>利用者へ表示する元PDFのファイル名です。</summary>
    public required string FileName { get; init; }
    /// <summary>プロジェクト位置を基準に元PDFを探す相対パスです。</summary>
    public string? RelativePath { get; init; }
    /// <summary>前回開いた場所を再探索するための絶対パス候補です。</summary>
    public string? AbsolutePathHint { get; init; }
    /// <summary>別PDFへの誤適用を防ぐ元PDFのSHA-256です。</summary>
    public required string Sha256 { get; init; }
    /// <summary>再探索時の候補照合に使う元PDFのバイト数です。</summary>
    public long FileSize { get; init; }
    /// <summary>元PDF作成時点のページ数です。</summary>
    public int? PageCount { get; init; }
    /// <summary>元PDFが.pdfocrproj内に内包されているかを示します。</summary>
    public bool IsEmbedded { get; init; }
}

/// <summary>PDFのしおりを階層構造として表します。</summary>
public sealed record PdfBookmark
{
    /// <summary>並び替え後も同じしおりを識別するIDです。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    // TitleはPDFビューアのしおり一覧に表示する名前です。
    public string Title { get; init; } = "新しいしおり";
    /// <summary>選択時に移動する1から始まるページ番号です。</summary>
    public int PageNumber { get; init; } = 1;
    /// <summary>しおりパネルで子階層を展開して表示するかを示します。</summary>
    public bool IsExpanded { get; init; } = true;
    /// <summary>このしおりの直下に属する子しおりです。</summary>
    public IReadOnlyList<PdfBookmark> Children { get; init; } = [];
}

/// <summary>
/// PdfCorrectoriumで編集中の文書状態を表す最上位モデルです。
/// </summary>
/// <remarks>
/// 元PDFそのものは不変の入力として扱い、このモデルに編集差分、しおり、
/// 読み順および文書表示設定を保持します。
/// </remarks>
public sealed record PdfCorrectoriumProject
{
    /// <summary>プロジェクトを一意に識別するIDです。</summary>
    public Guid ProjectId { get; init; } = Guid.NewGuid();
    /// <summary>画面タイトルと既定保存名に使用するプロジェクト名です。</summary>
    public string Name { get; init; } = "Untitled";
    /// <summary>編集対象となる元PDFの参照・検証情報です。</summary>
    public required SourcePdfReference SourcePdf { get; init; }
    /// <summary>PDFを開いた直後のページ表示設定です。</summary>
    public ViewerSettings ViewerSettings { get; init; } = new();
    /// <summary>ページごとのOCR、ルビ、読み順、画像最適化設定です。</summary>
    public IReadOnlyList<OcrPage> Pages { get; init; } = [];
    /// <summary>編集可能なPDFしおりの階層です。</summary>
    public IReadOnlyList<PdfBookmark> Bookmarks { get; init; } = [];
    /// <summary>元PDFからしおりを読み込み済みかを示します。</summary>
    public bool BookmarksInitialized { get; init; }
    /// <summary>PDF出力時にしおりツリーを再構築する必要があるかを示します。</summary>
    public bool BookmarksModified { get; init; }
    /// <summary>プロジェクトを初めて作成したUTC日時です。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>プロジェクトを最後に正常保存したUTC日時です。</summary>
    public DateTimeOffset LastSavedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
