using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.Core.Documents;

/// <summary>プロジェクトが編集対象PDFを保持する方法です。</summary>
public enum ProjectPdfStorageMode
{
    /// <summary>旧形式。<see cref="SourcePdfReference.IsEmbedded"/>から読み替えます。</summary>
    Legacy = 0,
    /// <summary>PDFを.pdfocrproj内へ内包するポータブルモードです。</summary>
    Embedded = 1,
    /// <summary>プロジェクトの保存場所を基準に外部PDFを相対参照する通常モードです。</summary>
    Relative = 2,
}

/// <summary>コメント等が参照する文書内対象の種類です。</summary>
public enum ProjectTargetKind { Document, Page, OcrRegion, Bookmark, Ruby, ReadingOrder, ValidationIssue }
/// <summary>コメントの解決状態です。</summary>
public enum ProjectCommentState { Open, Resolved }
/// <summary>コメントの重要度です。</summary>
public enum ProjectCommentImportance { None, Low, Normal, High, Critical }

/// <summary>文書、ページ、OCR領域等を固定IDで参照します。</summary>
public sealed record ProjectTargetReference
{
    public ProjectTargetKind Kind { get; init; } = ProjectTargetKind.Document;
    public Guid? PageId { get; init; }
    public Guid? ObjectId { get; init; }
    public int? CharacterStart { get; init; }
    public int? CharacterLength { get; init; }
    public string? ExternalKey { get; init; }
}

/// <summary>プロジェクト内で共有するタグ定義です。</summary>
public sealed record ProjectTag
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#64748B";
    public string Description { get; init; } = string.Empty;
}

/// <summary>任意の文書対象へ付けるコメントとタグです。</summary>
public sealed record ProjectComment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ProjectTargetReference Target { get; init; }
    public string Body { get; init; } = string.Empty;
    public ProjectCommentState State { get; init; } = ProjectCommentState.Open;
    public ProjectCommentImportance Importance { get; init; } = ProjectCommentImportance.Normal;
    public IReadOnlyList<Guid> TagIds { get; init; } = [];
    public string? Author { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>出力PDFにも保存する文書内ページリンクです。</summary>
public sealed record PdfSourceLinkAnnotation
{
    public string Reference { get; init; } = string.Empty;
    public string? Name { get; init; }
    public PdfRectangle Bounds { get; init; }
}

/// <summary>一つの編集対象に、複数行分の独立したクリック範囲を持てるPDFリンクです。</summary>
public sealed record PdfInternalLink
{
    /// <summary>枠の移動・サイズ変更・削除を防ぎます。リンク先の編集は可能です。</summary>
    public bool IsGeometryLocked { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SourcePageId { get; init; }
    public Guid? SourceRegionId { get; init; }
    public PdfRectangle? SourceBounds { get; init; }
    public IReadOnlyList<PdfRectangle> SourceAreas { get; init; } = [];
    public IReadOnlyList<PdfSourceLinkAnnotation> SourceAnnotations { get; init; } = [];
    public string? UnsupportedAction { get; init; }
    public string? DestinationViewType { get; init; }
    public IReadOnlyList<double?> DestinationViewParameters { get; init; } = [];
    public Guid DestinationPageId { get; init; }
    /// <summary>
    /// 外部Webページを開く絶対HTTP(S) URIです。値がある場合は
    /// <see cref="DestinationPageId"/>より優先し、PDFのURIリンクとして出力します。
    /// </summary>
    public string? ExternalUri { get; init; }
    public PdfPoint? DestinationPosition { get; init; }
    public double? DestinationZoomPercent { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;
    /// <summary>元PDFから取り込んだ注釈のオブジェクト参照です。新規リンクではnullです。</summary>
    public string? SourceAnnotationReference { get; init; }
    /// <summary>元注釈を再配置後のPDFから照合するための、変更前の矩形です。</summary>
    public PdfRectangle? OriginalSourceBounds { get; init; }
    /// <summary>元PDFリンクの内容を変更したときだけ、出力時に注釈を置き換えます。</summary>
    public bool IsModified { get; init; }
}

public enum PdfTextDecorationKind { PageNumber, Header, Footer, Watermark }
public enum PdfTextDecorationPosition
{
    TopLeft, TopCenter, TopRight,
    BottomLeft, BottomCenter, BottomRight,
    Center,
}

/// <summary>PDFページへ出力する文字装飾です。ページ番号には {page}、総ページ数には {total} を使います。</summary>
public sealed record PdfTextDecoration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public PdfTextDecorationKind Kind { get; init; }
    public string TextTemplate { get; init; } = "{page}";
    public PdfTextDecorationPosition Position { get; init; } = PdfTextDecorationPosition.BottomCenter;
    /// <summary>偶数ページだけに使用する位置です。nullなら全ページでPositionを使います。</summary>
    public PdfTextDecorationPosition? EvenPagePosition { get; init; }
    public int FirstPage { get; init; } = 1;
    public int? LastPage { get; init; }
    public int PageNumberOffset { get; init; }
    public double FontSizePoints { get; init; } = 10;
    public string ColorHex { get; init; } = "#333333";
    public double Opacity { get; init; } = 1;
    public double MarginPoints { get; init; } = 24;
    public bool IsEnabled { get; init; } = true;

    public bool AppliesToPage(int pageNumber, int pageCount) =>
        IsEnabled && pageNumber >= Math.Max(1, FirstPage) &&
        pageNumber <= Math.Min(pageCount, LastPage ?? pageCount);

    public PdfTextDecorationPosition PositionForPage(int pageNumber) =>
        pageNumber % 2 == 0 ? EvenPagePosition ?? Position : Position;

    public string TextForPage(int pageNumber, int pageCount) => TextTemplate
        .Replace("{page}", (pageNumber + PageNumberOffset).ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
        .Replace("{total}", ((LastPage ?? pageCount) + PageNumberOffset).ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
        .Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>墨消し範囲を作成した入力方法です。</summary>
public enum PdfRedactionShapeKind
{
    Rectangle,
    TextSelection,
    Polygon,
    Freehand,
}

/// <summary>
/// 元PDFを変更せず、最終PDF出力時にだけ確定する墨消し範囲です。
/// </summary>
/// <remarks>
/// 範囲はページ左下原点のPDFポイントで保持します。PDF文字選択は対象の直接テキスト
/// オブジェクトを除去して塗り矩形へ置換します。任意範囲や安全に直接編集できないPDFは、
/// 対象ページ全体を画像化してから範囲を塗りつぶします。
/// </remarks>
public sealed record PdfRedaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PageId { get; init; }
    public required PdfRectangle Bounds { get; init; }
    /// <summary>
    /// 自由形状の輪郭をページ左下原点のPDFポイントで保持します。
    /// 空の場合は旧形式と同じく<see cref="Bounds"/>全体を矩形として扱います。
    /// </summary>
    public IReadOnlyList<PdfPoint> PathPoints { get; init; } = [];
    public PdfRedactionShapeKind ShapeKind { get; init; } = PdfRedactionShapeKind.Rectangle;
    public string ColorHex { get; init; } = "#000000";
    public Guid? SourceRegionId { get; init; }
    public int? SourceCharacterStart { get; init; }
    public int? SourceCharacterLength { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 編集中の論理ページを、変更しない元PDFの物理ページへ対応付けます。
/// </summary>
/// <remarks>
/// 並べ替えと削除は配列の順序、回転は<see cref="RotationDegrees"/>だけを変更します。
/// これにより、ページ操作のたびにPDF全体を複製する必要がありません。
/// </remarks>
public sealed record ProjectPageReference
{
    /// <summary>コメントや内部リンクから参照される不変のページIDです。</summary>
    public Guid PageId { get; init; } = Guid.NewGuid();
    /// <summary>元PDF内の1から始まる物理ページ番号です。</summary>
    public int SourcePageNumber { get; init; }
    /// <summary>元ページへ追加適用する時計回りの回転角度です。</summary>
    public int RotationDegrees { get; init; }
}

/// <summary>
/// PDFビューアでページを進める方向を表します。
/// <c>LeftToRight</c>は左綴じ（右開き）、<c>RightToLeft</c>は右綴じ（左開き）です。
/// </summary>
public enum BindingDirection { LeftToRight, RightToLeft }
/// <summary>PDFを開いた直後に使用するページレイアウトを表します。</summary>
public enum InitialPageMode { SinglePage, Continuous, FacingPages, ContinuousFacingPages }

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

/// <summary>プロジェクト内で使用する編集画面のページ配置です。</summary>
public enum ProjectEditorPageLayout { SinglePage, FacingPages }
/// <summary>プロジェクト内で使用する編集画面のスクロール方式です。</summary>
public enum ProjectEditorPageFlow { PageByPage, Continuous }

/// <summary>保存する編集モード。値は既存の画面上の選択順と対応し、追加時も変更しません。</summary>
public enum ProjectEditingMode { OcrEditing = 0, ReadingOrder = 1, Review = 2, Redaction = 3, LinkEditing = 4 }

/// <summary>
/// 利用者が文書の初期表示とは異なる編集表示を明示的に選んだ場合だけ保存する上書きです。
/// </summary>
public sealed record ProjectEditorViewState
{
    public ProjectEditorPageLayout PageLayout { get; init; } = ProjectEditorPageLayout.SinglePage;
    public ProjectEditorPageFlow PageFlow { get; init; } = ProjectEditorPageFlow.PageByPage;
    public bool ShowCoverSeparately { get; init; } = true;
    public BindingDirection BindingDirection { get; init; } = BindingDirection.LeftToRight;
}

/// <summary>
/// PDFの文書情報辞書へ反映する、利用者が編集可能なメタデータです。
/// </summary>
/// <remarks>
/// このモデル自体が存在しない場合は元PDFの文書情報を変更しません。
/// 各項目の空文字列は、PDF出力時に対応する既存項目を削除する指定として扱います。
/// </remarks>
public sealed record PdfDocumentMetadata
{
    /// <summary>PDFビューア等で文書名として表示されるタイトルです。</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>文書の作者名です。</summary>
    public string Author { get; init; } = string.Empty;
    /// <summary>文書の主題です。</summary>
    public string Subject { get; init; } = string.Empty;
    /// <summary>検索や分類に使用するキーワードです。</summary>
    public string Keywords { get; init; } = string.Empty;
    /// <summary>文書を作成したアプリケーション名です。</summary>
    public string Creator { get; init; } = string.Empty;
    /// <summary>PDFへ変換したソフトウェア名です。</summary>
    public string Producer { get; init; } = string.Empty;
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
/// 読み順、文書表示設定および編集後の文書情報を保持します。
/// </remarks>
public sealed record PdfCorrectoriumProject
{
    /// <summary>全ページで共用する読み方。同じ表記の行指定が優先します。</summary>
    public IReadOnlyList<WordReading> WordReadings { get; init; } = [];
    public PdfCorrectorium.Core.Analysis.OcrLexicon OcrLexicon { get; init; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public PdfCorrectorium.Core.Analysis.OcrWordCountsCache? OcrWordCountsCache { get; init; }
    /// <summary>プロジェクトを一意に識別するIDです。</summary>
    public Guid ProjectId { get; init; } = Guid.NewGuid();
    /// <summary>画面タイトルと既定保存名に使用するプロジェクト名です。</summary>
    public string Name { get; init; } = "Untitled";
    /// <summary>編集対象となる元PDFの参照・検証情報です。</summary>
    public required SourcePdfReference SourcePdf { get; init; }
    /// <summary>PDFを内包するか、同一フォルダー以下から相対参照するかを保持します。</summary>
    public ProjectPdfStorageMode PdfStorageMode { get; init; } = ProjectPdfStorageMode.Legacy;
    /// <summary>PDFを開いた直後のページ表示設定です。</summary>
    public ViewerSettings ViewerSettings { get; init; } = new();
    /// <summary>
    /// 編集画面で利用者が明示的に選んだ表示状態です。nullの場合は<see cref="ViewerSettings"/>に従います。
    /// </summary>
    public ProjectEditorViewState? EditorViewState { get; init; }
    /// <summary>通常保存時の編集モード。旧プロジェクトのnullはアプリの初期値に従います。</summary>
    public ProjectEditingMode? EditingMode { get; init; }
    /// <summary>PDF出力時に使用する仕様バージョンです。既定では編集内容に応じて自動決定します。</summary>
    public PdfOutputVersion OutputPdfVersion { get; init; } = PdfOutputVersion.Automatic;
    /// <summary>
    /// 利用者が編集したPDF文書情報です。未編集の場合は<see langword="null"/>です。
    /// </summary>
    public PdfDocumentMetadata? DocumentMetadata { get; init; }
    /// <summary>
    /// PDFカタログのLangへ反映するBCP 47言語タグです。未編集の場合は<see langword="null"/>、
    /// 空文字列の場合は既存の言語指定を削除します。
    /// </summary>
    public string? DocumentLanguage { get; init; }
    /// <summary>ページごとのOCR、ルビ、読み順、画像最適化設定です。</summary>
    public IReadOnlyList<OcrPage> Pages { get; init; } = [];
    /// <summary>
    /// 表示・出力するページ順と、各ページの元PDF内番号および追加回転です。
    /// 空の場合は旧形式として、元PDFの全ページを同じ順序で使用します。
    /// </summary>
    public IReadOnlyList<ProjectPageReference> PageSequence { get; init; } = [];
    /// <summary>編集可能なPDFしおりの階層です。</summary>
    public IReadOnlyList<PdfBookmark> Bookmarks { get; init; } = [];
    /// <summary>文書、ページ、OCR領域等へ付けたコメントです。</summary>
    public IReadOnlyList<ProjectComment> Comments { get; init; } = [];
    /// <summary>コメントへ付与できるプロジェクト共通タグです。</summary>
    public IReadOnlyList<ProjectTag> Tags { get; init; } = [];
    /// <summary>アプリ内移動と出力PDFのGoTo注釈に使用する内部リンクです。</summary>
    public IReadOnlyList<PdfInternalLink> InternalLinks { get; init; } = [];
    /// <summary>元PDFの既存リンクを確認済みかを示し、再オープン時の二重取り込みを防ぎます。</summary>
    public bool SourceLinksInitialized { get; init; }
    public bool LinkOverlayVisible { get; init; }
    public int SourceLinkImportVersion { get; init; }
    /// <summary>ページ番号、ヘッダー／フッター、文字透かしの設定です。</summary>
    public IReadOnlyList<PdfTextDecoration> TextDecorations { get; init; } = [];
    /// <summary>
    /// PDF出力時に内容を復元不能な形で除去するページ内範囲です。PDF文字選択は対応する
    /// 文字オブジェクトを除去して矩形へ置換し、それ以外は安全性を優先してページを画像化します。
    /// </summary>
    public IReadOnlyList<PdfRedaction> Redactions { get; init; } = [];
    /// <summary>元PDFからしおりを読み込み済みかを示します。</summary>
    public bool BookmarksInitialized { get; init; }
    /// <summary>PDF出力時にしおりツリーを再構築する必要があるかを示します。</summary>
    public bool BookmarksModified { get; init; }
    /// <summary>プロジェクトを初めて作成したUTC日時です。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>プロジェクトを最後に正常保存したUTC日時です。</summary>
    public DateTimeOffset LastSavedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
