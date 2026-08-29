using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.Core.Documents;

/// <summary>OCR領域内で文字を組む論理方向を表します。</summary>
public enum WritingMode { Horizontal, Vertical }
/// <summary>文字または領域を読み進める方向を表します。</summary>
public enum TextFlowDirection { LeftToRight, RightToLeft, TopToBottom, BottomToTop }
/// <summary>認識文字列を目標領域へ収める際の補正方式を表します。</summary>
public enum FitMode { Stretch, Spacing, Distribute, Mixed, Automatic, PositionOnly }
/// <summary>人手による確認・修正の進捗状態を表します。</summary>
public enum ReviewStatus { Unreviewed, Verified, Modified, NeedsReview, Excluded, Deferred }

/// <summary>
/// OCR領域を検索、コピー、読み上げ、PDF出力の各経路へ含めるかを個別に指定します。
/// </summary>
public sealed record OutputAttributes
{
    /// <summary>PDFビューアの文字検索対象へ含めるかを指定します。</summary>
    public bool IncludeInSearch { get; init; } = true;
    /// <summary>コピー時に抽出される文字列へ含めるかを指定します。</summary>
    public bool IncludeInCopy { get; init; } = true;
    /// <summary>読み上げ順へ含めるかを指定します。</summary>
    public bool IncludeInSpeech { get; init; } = true;
    /// <summary>最終PDFのテキスト層へ出力するかを指定します。</summary>
    public bool IncludeInPdf { get; init; } = true;
}

/// <summary>表記と読みの対応を保持します。</summary>
public sealed record WordReading
{
    /// <summary>OCR文字列中に現れる表記です。</summary>
    public string SurfaceText { get; init; } = string.Empty;
    /// <summary>表記へ関連付ける読み仮名です。</summary>
    public string ReadingText { get; init; } = string.Empty;
}

/// <summary>
/// ページ上の編集可能なOCRテキスト領域を表します。
/// </summary>
/// <remarks>
/// 元の認識結果と編集後の状態を同時に保持することで、差分表示、Undo、
/// PDF再生成時の変更判定を行えるようにしています。
/// </remarks>
public sealed record OcrTextRegion
{
    /// <summary>編集履歴と読み順で領域を識別する不変IDです。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>この領域が属するページのIDです。</summary>
    public Guid PageId { get; init; }
    /// <summary>段落や親領域との包含関係を示すIDです。</summary>
    public Guid? ParentRegionId { get; init; }
    /// <summary>OCR取込時点の文字列です。</summary>
    public string OriginalText { get; init; } = string.Empty;
    /// <summary>利用者が修正した文字列。未修正時は空文字列です。</summary>
    public string EditedText { get; init; } = string.Empty;
    /// <summary>OCR取込時点の位置、寸法、回転、文字送りです。</summary>
    public required TextGeometry OriginalGeometry { get; init; }
    /// <summary>利用者が編集した現在の位置、寸法、回転、文字送りです。</summary>
    public required TextGeometry EditedGeometry { get; init; }
    /// <summary>取込時の書字方向。由来データに情報がなければnullです。</summary>
    public WritingMode? OriginalWritingMode { get; init; }
    /// <summary>現在の書字方向です。</summary>
    public WritingMode WritingMode { get; init; }
    /// <summary>書字方向が自動判定ではなく利用者指定かを示します。</summary>
    public bool HasExplicitWritingMode { get; init; }
    /// <summary>領域内で文字を読む進行方向です。</summary>
    public TextFlowDirection FlowDirection { get; init; }
    /// <summary>領域へ文字列を収める補正方法です。</summary>
    public FitMode FitMode { get; init; } = FitMode.Automatic;
    /// <summary>人手確認の進捗状態です。</summary>
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Unreviewed;
    /// <summary>検索、コピー、読み上げ、PDF出力への参加設定です。</summary>
    public OutputAttributes Output { get; init; } = new();
    /// <summary>単語表記と読み仮名の対応一覧です。</summary>
    public IReadOnlyList<WordReading> WordReadings { get; init; } = [];
    /// <summary>領域を生成したOCRエンジンまたは取込経路の識別子です。</summary>
    public string OcrProviderId { get; init; } = "imported-pdf";
    /// <summary>OCRエンジンが返した0～1の認識信頼度です。</summary>
    public double? Confidence { get; init; }
    /// <summary>元PDFになく、利用者が追加した領域かを示します。</summary>
    public bool IsAdded { get; init; }
    /// <summary>PDF出力時に既存領域を削除する予定かを示します。</summary>
    public bool IsDeleted { get; init; }
    /// <summary>編集文字列が存在する場合は編集値を、存在しない場合は元の認識値を返します。</summary>
    public string EffectiveText => string.IsNullOrEmpty(EditedText) ? OriginalText : EditedText;
    /// <summary>文字列、幾何情報、書字方向、追加・削除状態のいずれかが変更されたかを返します。</summary>
    public bool IsModified =>
        IsAdded ||
        IsDeleted ||
        (OriginalWritingMode ?? WritingMode) != WritingMode ||
        EffectiveText != OriginalText ||
        !EditedGeometry.IsEquivalentTo(OriginalGeometry);

    /// <summary>文字列を変更し、確認状態を修正済みにした新しい領域を返します。</summary>
    /// <param name="text">変更後のUnicode文字列。</param>
    /// <returns>変更を反映した不変レコード。</returns>
    public OcrTextRegion EditText(string text) => this with
    {
        EditedText = text,
        ReviewStatus = ReviewStatus.Modified,
    };

    /// <summary>検証済みの幾何情報を設定し、確認状態を修正済みにした新しい領域を返します。</summary>
    /// <param name="geometry">変更後の位置、サイズ、回転、文字送り。</param>
    /// <returns>変更を反映した不変レコード。</returns>
    /// <exception cref="ArgumentException">幾何情報に非有限値、ゼロ以下の倍率などが含まれる場合。</exception>
    public OcrTextRegion EditGeometry(TextGeometry geometry)
    {
        var errors = geometry.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(", ", errors), nameof(geometry));
        return this with { EditedGeometry = geometry, ReviewStatus = ReviewStatus.Modified };
    }
}

/// <summary>
/// 本文とは独立して配置されるルビ領域と、対応する本文および読み情報を保持します。
/// </summary>
public sealed record RubyRegion
{
    /// <summary>ルビ領域を識別する不変IDです。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>ルビが配置されているページのIDです。</summary>
    public Guid PageId { get; init; }
    /// <summary>読みを関連付ける本文OCR領域のIDです。</summary>
    public Guid? BaseTextRegionId { get; init; }
    /// <summary>画像上に見えているルビ文字列です。</summary>
    public string VisibleText { get; init; } = string.Empty;
    /// <summary>読み上げやアクセシブル出力へ使用する読み文字列です。</summary>
    public string? ReadingText { get; init; }
    /// <summary>ルビを検索、コピー、読み上げ、PDF出力へ含める方法です。</summary>
    public OutputAttributes Output { get; init; } = new() { IncludeInCopy = false, IncludeInSpeech = false };
}

/// <summary>
/// 全面画像から安全に余白または単一色の空白帯を除去するためのページ単位設定を保持します。
/// </summary>
public sealed record PageImageOptimization
{
    /// <summary>このページで画像余白の切り抜きを実行するかを指定します。</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>白背景として扱う場合に余白とみなす最小輝度です。</summary>
    public byte WhiteThreshold { get; init; } = 245;
    /// <summary>画像の外周から背景色を推定し、白以外の単一色背景も最適化対象に含めます。</summary>
    public bool DetectUniformColorBackground { get; init; } = true;
    /// <summary>
    /// ページ全面を覆う画像が白または白に近い単色だけで構成される場合、その画像オブジェクトを削除します。
    /// </summary>
    /// <remarks>PDFページ自体は削除せず、既定の白いページ背景と画像以外の文字・図形は維持します。</remarks>
    public bool RemoveBlankFullPageImage { get; init; } = true;
    /// <summary>推定背景色と同じとみなすRGB各成分の最大差です。</summary>
    public byte BackgroundColorTolerance { get; init; } = 18;
    /// <summary>画像の上端、下端、左端、右端に連続する背景領域を削減します。</summary>
    public bool RemoveOuterMargins { get; init; } = true;
    /// <summary>上下または左右に内容がある場合でも、途中にある連続した背景色の空白帯を削減します。</summary>
    public bool RemoveInternalBlankBands { get; init; } = true;
    /// <summary>内部空白帯として分離するために必要な、画像辺長に対する最小比率です。</summary>
    public double MinimumInternalBlankBandRatio { get; init; } = 0.02;
    /// <summary>1画像から生成できる保持領域数の上限です。過度な細分化を防ぎます。</summary>
    public int MaximumRetainedRegions { get; init; } = 16;
    /// <summary>検出した内容領域の外側へ残す安全余白です。</summary>
    public int PaddingPixels { get; init; } = 6;
    /// <summary>
    /// 容量削減率が小さいことを利用者へ強調表示する目安です。目安未満でも、確認後に実行できます。
    /// </summary>
    public double MinimumAreaReduction { get; init; } = 0.15;

    /// <summary>
    /// 自動検出された背景置換領域のうち、利用者が元画像のまま残すよう指定したページ相対矩形です。
    /// </summary>
    /// <remarks>
    /// 値はページ左上を原点とする 0～1 の比率です。プレビュー上で背景置換を個別に無効化した結果を
    /// プロジェクトへ保持し、再解析時とPDF出力時の双方で同じ保持範囲を再現します。
    /// </remarks>
    public IReadOnlyList<ImageOptimizationKeepRegion> KeepRegions { get; init; } = [];
}

/// <summary>
/// ページ画像最適化で背景へ置換せず、元画像の一部として保持するページ相対矩形を表します。
/// </summary>
public sealed record ImageOptimizationKeepRegion
{
    /// <summary>ページ左端から保持範囲左端までの比率です。</summary>
    public double LeftRatio { get; init; }
    /// <summary>ページ上端から保持範囲上端までの比率です。</summary>
    public double TopRatio { get; init; }
    /// <summary>ページ幅に対する保持範囲の幅の比率です。</summary>
    public double WidthRatio { get; init; }
    /// <summary>ページ高さに対する保持範囲の高さの比率です。</summary>
    public double HeightRatio { get; init; }
}

/// <summary>
/// PDFの1ページに属するOCR領域、ルビ、読み順、画像最適化設定をまとめます。
/// </summary>
/// <remarks>幅と高さの単位はPDFポイントです。</remarks>
public sealed record OcrPage
{
    /// <summary>プロジェクト内部でページを識別する不変IDです。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>1から始まるPDFページ番号です。</summary>
    public int PageNumber { get; init; }
    /// <summary>PDFページの幅をポイント単位で保持します。</summary>
    public double WidthPoints { get; init; }
    /// <summary>PDFページの高さをポイント単位で保持します。</summary>
    public double HeightPoints { get; init; }
    /// <summary>PDFページ辞書に設定された回転角度です。</summary>
    public int RotationDegrees { get; init; }
    /// <summary>ページ上の編集可能なOCR文字領域です。</summary>
    public IReadOnlyList<OcrTextRegion> TextRegions { get; init; } = [];
    /// <summary>本文とは独立して管理するルビ領域です。</summary>
    public IReadOnlyList<RubyRegion> RubyRegions { get; init; } = [];
    /// <summary>領域IDを検索・コピー・読み上げ順に並べた一覧です。</summary>
    public IReadOnlyList<Guid> ReadingOrder { get; init; } = [];
    /// <summary>全面画像の余白切り抜き設定。未設定なら最適化しません。</summary>
    public PageImageOptimization? ImageOptimization { get; init; }
}
