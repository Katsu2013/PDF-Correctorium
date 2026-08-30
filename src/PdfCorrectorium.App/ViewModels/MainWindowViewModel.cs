using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>
/// OCR領域を選択・編集するときの論理単位を指定します。
/// </summary>
public enum OcrEditUnit { Line, Paragraph, Character }

/// <summary>しおりをドラッグした際、対象ノードのどこへ挿入するかを表します。</summary>
public enum BookmarkDropPosition { Before, AsChild, After }

/// <summary>
/// ページ一覧に表示するページ番号と遅延生成サムネイルを保持します。
/// </summary>
public sealed class PdfPageItem(int pageNumber) : INotifyPropertyChanged
{
    /// <summary>バックグラウンドで生成され、ページ一覧へ表示される画像です。</summary>
    private ImageSource? _thumbnail;
    /// <summary>この項目がプレビューへ表示中のページかを保持します。</summary>
    private bool _isCurrent;

    /// <summary>ページ表示情報が変更されたときに発生します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>1から始まるPDFページ番号です。</summary>
    public int PageNumber { get; } = pageNumber;
    /// <summary>ページ一覧に表示するローカライズ済み名称です。</summary>
    public string DisplayName => LocalizationService.IsEnglish ? $"Page {PageNumber}" : $"{PageNumber} ページ";

    /// <summary>表示言語を変更した後、ページ名を再描画します。</summary>
    public void RefreshLocalization() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    /// <summary>ページ一覧で使用する遅延生成サムネイルです。</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    /// <summary>ページ一覧で、現在表示中ページ専用の強調枠を表示する場合は<c>true</c>。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }
}

/// <summary>画面選択肢として表示する確認状態です。</summary>
public sealed record ReviewStatusOption(ReviewStatus Value, string DisplayName);
/// <summary>画面選択肢として表示する書字方向です。</summary>
public sealed record WritingModeOption(WritingMode Value, string DisplayName);

/// <summary>透明テキストの検索条件を表します。</summary>
/// <param name="SearchText">検索する文字列。</param>
/// <param name="MatchCase">英字の大文字と小文字を区別する場合は<c>true</c>。</param>
/// <param name="CurrentPageOnly">現在ページだけを検索する場合は<c>true</c>。</param>
/// <param name="InvisibleOnly">不可視テキストだけを検索する場合は<c>true</c>。</param>
/// <param name="WholeRegionMatch">OCR行ブロック全体が条件に一致する場合だけ検索する場合は<c>true</c>。</param>
/// <param name="UseRegularExpression">検索文字列を正規表現として解釈する場合は<c>true</c>。</param>
public sealed record OcrTextSearchOptions(
    string SearchText,
    bool MatchCase = false,
    bool CurrentPageOnly = false,
    bool InvisibleOnly = true,
    bool WholeRegionMatch = false,
    bool UseRegularExpression = false);

/// <summary>ページを順に処理する長時間操作の、現在位置と画面表示用メッセージです。</summary>
/// <param name="Current">処理済みまたは処理中の1始まり位置。</param>
/// <param name="Total">処理対象の総数。</param>
/// <param name="Message">利用者へ表示する現在の処理内容。</param>
public sealed record OperationProgressUpdate(int Current, int Total, string Message)
{
    /// <summary>プログレスバーへ設定する0～100の進捗率です。</summary>
    public double Percentage => Total <= 0 ? 0 : Math.Clamp(Current * 100d / Total, 0, 100);
}

/// <summary>透明テキスト内で見つかった1件の検索位置を表します。</summary>
/// <param name="PageNumber">1始まりのページ番号。</param>
/// <param name="RegionId">検索対象OCR領域の識別子。</param>
/// <param name="StartIndex">OCR領域文字列内の開始位置。</param>
/// <param name="Length">一致した文字列の長さ。</param>
/// <param name="RegionText">検索時点のOCR領域文字列。</param>
/// <param name="PreviewText">検索結果一覧へ表示する前後の文脈。</param>
public sealed record OcrTextSearchMatch(
    int PageNumber,
    Guid RegionId,
    int StartIndex,
    int Length,
    string RegionText,
    string PreviewText)
{
    /// <summary>検索結果の前後へ表示する最大文字数です。</summary>
    private const int PreviewContextLength = 18;

    /// <summary>検索結果一覧へ表示するページ名です。</summary>
    public string PageDisplay => LocalizationService.IsEnglish ? $"Page {PageNumber}" : $"{PageNumber} ページ";

    /// <summary>検索結果一覧で強調表示する、一致した文字列です。</summary>
    public string MatchedText =>
        StartIndex >= 0 && Length > 0 && StartIndex + Length <= RegionText.Length
            ? RegionText.Substring(StartIndex, Length)
            : string.Empty;

    /// <summary>検索結果一覧で、一致部分より前に表示する文脈です。</summary>
    public string PreviewPrefix
    {
        get
        {
            if (!HasValidMatchRange) return PreviewText;
            var previewStart = Math.Max(0, StartIndex - PreviewContextLength);
            var omission = previewStart > 0 ? "…" : string.Empty;
            return omission + RegionText[previewStart..StartIndex].ReplaceLineEndings(" ");
        }
    }

    /// <summary>検索結果一覧の文脈内で赤色表示する、実際の一致文字列です。</summary>
    public string PreviewMatchedText => MatchedText.ReplaceLineEndings(" ");

    /// <summary>検索結果一覧で、一致部分より後に表示する文脈です。</summary>
    public string PreviewSuffix
    {
        get
        {
            if (!HasValidMatchRange) return string.Empty;
            var matchEnd = StartIndex + Length;
            var previewEnd = Math.Min(RegionText.Length, matchEnd + PreviewContextLength);
            var omission = previewEnd < RegionText.Length ? "…" : string.Empty;
            return RegionText[matchEnd..previewEnd].ReplaceLineEndings(" ") + omission;
        }
    }

    /// <summary>検索時の一致位置が元の領域文字列内に収まっているかを示します。</summary>
    private bool HasValidMatchRange =>
        StartIndex >= 0 && Length > 0 && StartIndex + Length <= RegionText.Length;
}

/// <summary>
/// メイン編集画面の文書状態、ページ描画、OCR選択、Undo/Redo、および各操作コマンドを統括します。
/// </summary>
/// <remarks>
/// PDFそのものは編集操作のたびに変更せず、<see cref="OverlayRegionViewModel"/> の編集状態として保持します。
/// プロジェクト保存時はその状態を.pdfocrprojへ格納し、PDF出力時にだけ元PDFへ反映します。
/// </remarks>
public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    /// <summary>極端に処理時間の長い正規表現から編集画面を保護するための上限時間です。</summary>
    private static readonly TimeSpan SearchRegexTimeout = TimeSpan.FromSeconds(2);
    /// <summary>拡大・縮小ボタンで順番に選択する、一般的なPDFビューア準拠の倍率です。</summary>
    private static readonly double[] StandardZoomSteps =
        [25, 33, 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300, 400];
    /// <summary>1領域の編集前後をまとめ、Undo/Redoで復元できる形にした変更単位です。</summary>
    private sealed record OverlayRegionChange(OverlayRegionViewModel Region, OverlayRegionSnapshot Before, OverlayRegionSnapshot After);
    /// <summary>一度に行われた複数領域の変更と、その履歴表示名・状態IDを保持します。</summary>
    private sealed record OverlayEdit(
        IReadOnlyList<OverlayRegionChange> Changes,
        string Description,
        long BeforeStateId = 0,
        long AfterStateId = 0);
    /// <summary>画像ピクセルとPDFポイントの座標変換に使うページ寸法です。</summary>
    private sealed record PageMetrics(int PixelWidth, int PixelHeight, double WidthPoints, double HeightPoints);
    /// <summary>検索対象文字列内で見つかった範囲と、正規表現のキャプチャ情報を保持します。</summary>
    private sealed record OcrSearchOccurrence(int StartIndex, int Length, Match? RegularExpressionMatch = null);
    /// <summary>置換前の位置・長さと、正規表現の展開後文字列を保持します。</summary>
    private sealed record OcrReplacementOperation(int StartIndex, int Length, string ReplacementText);

    /// <summary>.pdfocrprojの読込・保存・検証を担当するサービスです。</summary>
    private readonly ProjectPackageService _packages;
    /// <summary>ページ画像と既存PDF文字領域を生成するサービスです。</summary>
    private readonly PdfPreviewService _previewService;
    /// <summary>編集状態を検証済みPDFとして書き出すサービスです。</summary>
    private readonly PdfExportService _exportService;
    /// <summary>ネイティブPDF処理の異常終了から編集画面を保護する別プロセス出力サービスです。</summary>
    private readonly IsolatedPdfExportService _isolatedExportService;
    /// <summary>NDLOCR-Liteの付随ファイルを探索・統合するサービスです。</summary>
    private readonly NdlOcrCompanionService _ndlOcrCompanionService;
    /// <summary>操作失敗や診断情報を永続化するアプリケーションログです。</summary>
    private readonly DiagnosticLog _log;
    /// <summary>Portable／インストールモードに応じて解決された保存先です。</summary>
    private readonly ApplicationPaths _paths;
    /// <summary>利用者設定をJSONへ読み書きするサービスです。</summary>
    private readonly ApplicationSettingsService _settingsService;
    /// <summary>PDFアウトラインと交換用しおりファイルを扱うサービスです。</summary>
    private readonly PdfBookmarkService _bookmarkService = new();
    /// <summary>元PDFを保護したままページ構成を変更するサービスです。</summary>
    private readonly PdfPageManagementService _pageManagementService = new();
    /// <summary>終了確認完了後にメインウィンドウを閉じるコールバックです。</summary>
    private readonly Action _close;
    /// <summary>正規化済みの現在のアプリケーション設定です。</summary>
    private ApplicationSettings _applicationSettings;
    /// <summary>現在開いている編集プロジェクト。閲覧だけの場合はnullです。</summary>
    private PdfCorrectoriumProject? _project;
    /// <summary>プロジェクトモデルだけでなく、元PDFの読込も完了したことを保持します。</summary>
    private bool _hasDocument;
    private bool _isOpeningDocument;
    /// <summary>現在のPDFに対応付けられたNDLOCR-Lite取込結果です。</summary>
    private NdlOcrDocument? _ndlOcrDocument;
    /// <summary>ページ切替時に古いプレビュー描画を中止するためのトークン源です。</summary>
    private CancellationTokenSource? _renderCancellation;
    /// <summary>サムネイル表示を中断または再開するためのトークン源です。</summary>
    private CancellationTokenSource? _thumbnailCancellation;
    /// <summary>.pdfocrprojへ保存し、次回表示時に再利用するページ別JPEGサムネイルです。</summary>
    private readonly Dictionary<int, byte[]> _thumbnailCache = [];
    /// <summary>外部参照または内包PDFから解決した、実際に読み込むPDFパスです。</summary>
    private string? _resolvedPdfPath;
    /// <summary>上書き保存に使用する現在の.pdfocrprojファイルパスです。</summary>
    private string? _projectFilePath;
    // 以下の文字列フィールドは、文書プロパティとステータスバーに表示するための
    // バッキング値です。元PDFパス、プロジェクトパス、SHA-256、ページ情報、
    // OCRデータの由来、現在ページの領域数を、画面更新通知と一緒に管理します。
    private string _documentTitle = "PDFは開かれていません";
    private string _documentDescription = "PDFを開くとページを表示します。最初の保存時に安全な .pdfocrproj 作業ファイルを作成します。";
    private string _sourcePdfPath = "-";
    private string _projectPath = "未保存";
    private string _sourceHash = "-";
    private string _statusMessage = "準備完了";
    private string _pageSummary = "ページなし";
    private string _ocrDataSourceText = "OCRデータ未読込";
    private string _overlaySummary = "文字領域: 0件";
    /// <summary>PDF読込や保存など、画面全体に関係する長時間処理の進捗表示を有効にします。</summary>
    private bool _isBackgroundOperationVisible;
    /// <summary>処理量を事前に算出できない長時間処理を不確定表示にするためのフラグです。</summary>
    private bool _isBackgroundOperationIndeterminate = true;
    /// <summary>画面全体に関係する長時間処理の進捗率を0～100で保持します。</summary>
    private double _backgroundOperationProgress;
    /// <summary>ステータスバーの進捗表示に併記する現在の処理内容です。</summary>
    private string _backgroundOperationMessage = string.Empty;
    /// <summary>終了済みの処理から遅れて届いた進捗通知を識別し、画面へ反映しないための世代番号です。</summary>
    private long _backgroundOperationId;
    /// <summary>OCR文字と境界のオーバーレイを表示するかを保持します。</summary>
    private bool _isOcrOverlayVisible = true;
    /// <summary>プレビューの非同期描画中であることを画面へ通知するフラグです。</summary>
    private bool _isPreviewLoading;
    /// <summary>別プロセスでPDFを生成・検証・保存確定している間、競合する編集操作を抑止します。</summary>
    private bool _isPdfExporting;
    /// <summary>現在ページを描画したWPF画像です。</summary>
    private ImageSource? _previewImage;
    /// <summary>OCR座標の基準になる現在のプレビュー画像幅です。</summary>
    private int _previewPixelWidth;
    /// <summary>OCR座標の基準になる現在のプレビュー画像高さです。</summary>
    private int _previewPixelHeight;
    /// <summary>ページ一覧で現在選択されているページです。</summary>
    private PdfPageItem? _selectedPage;
    /// <summary>プロパティ欄と文字編集の基準になる主選択OCR領域です。</summary>
    private OverlayRegionViewModel? _selectedOverlay;
    /// <summary>ページ番号ごとの編集可能なOCR領域を保持する作業キャッシュです。</summary>
    private readonly Dictionary<int, List<OverlayRegionViewModel>> _pageOverlays = [];
    /// <summary>プレビューのピクセル寸法とPDFポイント寸法の対応表です。</summary>
    private readonly Dictionary<int, PageMetrics> _pageMetrics = [];
    /// <summary>自動的なプロパティ変更を1件の履歴へまとめるための直前状態です。</summary>
    private readonly Dictionary<Guid, OverlayRegionSnapshot> _lastOverlaySnapshots = [];
    /// <summary>取り消し可能な編集を新しい順に保持します。</summary>
    private readonly Stack<OverlayEdit> _undo = [];
    /// <summary>Undo後にやり直せる編集を保持します。</summary>
    private readonly Stack<OverlayEdit> _redo = [];
    /// <summary>囲み選択やCtrl選択を含む、現在選択中の全OCR領域です。</summary>
    private readonly List<OverlayRegionViewModel> _selectedOverlays = [];
    /// <summary>整列・同一サイズ操作で位置や寸法の基準にする領域です。</summary>
    private OverlayRegionViewModel? _alignmentReference;
    /// <summary>ドラッグ中の連続変更をまとめている対象領域です。</summary>
    private OverlayRegionViewModel? _batchedRegion;
    /// <summary>連続編集開始時点の状態です。終了時に差分を履歴化します。</summary>
    private OverlayRegionSnapshot? _batchStart;
    /// <summary>Undo/Redoの再適用中に新しい履歴が作られることを防ぐフラグです。</summary>
    private bool _applyingHistory;
    /// <summary>プレビューへ適用する表示倍率を百分率で保持します。</summary>
    private double _zoomPercent = 100;
    /// <summary>通常編集と読み順編集を切り替える画面選択インデックスです。</summary>
    private int _editorModeIndex;
    /// <summary>行・段落・文字の編集単位を表す画面選択インデックスです。</summary>
    private int _editUnitIndex;
    /// <summary>プレビュー上のドラッグをOCR領域追加として扱うかを示します。</summary>
    private bool _isAddOcrRegionMode;
    /// <summary>段落一括編集値に不整合がある場合の利用者向け説明です。</summary>
    private string _paragraphEditValidationMessage = string.Empty;
    /// <summary>現在の編集状態を識別し、保存済み状態との比較に使う番号です。</summary>
    private long _currentEditStateId;
    /// <summary>最後にプロジェクトへ保存した編集状態の識別番号です。</summary>
    private long _savedEditStateId;
    /// <summary>新しい編集状態へ割り当てる単調増加の識別番号です。</summary>
    private long _nextEditStateId;
    /// <summary>しおりツリーで選択されているノードです。</summary>
    private BookmarkNodeViewModel? _selectedBookmark;
    /// <summary>しおり取込中の変更通知を未保存編集として数えないためのフラグです。</summary>
    private bool _loadingBookmarks;
    /// <summary>ページ一覧で現在選択されている1始まりページ番号です。</summary>
    private readonly List<int> _selectedPageNumbers = [];
    /// <summary>ページ構成を変更した作業用PDFをプロジェクト保存時に内包する必要があるかを示します。</summary>
    private bool _hasPageStructureEdits;
    /// <summary>自動保存間隔を判定する基準となる、最後に自動保存または通常保存したUTC日時です。</summary>
    private DateTimeOffset _lastAutoSaveAtUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastUserActivityAtUtc = DateTimeOffset.UtcNow;
    private long _lastAutoSavedEditStateId = -1;

    /// <summary>操作停止による自動保存を、キーボード・ポインター入力から延期します。</summary>
    internal void NotifyUserActivity() => _lastUserActivityAtUtc = DateTimeOffset.UtcNow;

    /// <summary>未保存プロジェクトも復元できる、自動保存先の絶対パスです。</summary>
    public string? AutoSaveRecoveryPath => _project is null ? null : _projectFilePath is not null
        ? ProjectPackageService.GetAutoSavePath(_projectFilePath)
        : Path.Combine(_paths.WorkspaceDirectory, "recovery", $"{_project.ProjectId:N}.autosave.pdfocrproj");
    /// <summary>タイマーが重複して同じプロジェクトを自動保存しないための排他フラグです。</summary>
    private bool _autoSaveInProgress;

    /// <summary>
    /// PDF表示、プロジェクト保存、出力、ログの各サービスを受け取り、画面コマンドを初期化します。
    /// </summary>
    public MainWindowViewModel(
        ProjectPackageService packages,
        PdfPreviewService previewService,
        PdfExportService exportService,
        NdlOcrCompanionService ndlOcrCompanionService,
        DiagnosticLog log,
        ApplicationPaths paths,
        Action close)
    {
        _packages = packages;
        _previewService = previewService;
        _exportService = exportService;
        _ndlOcrCompanionService = ndlOcrCompanionService;
        _log = log;
        _paths = paths;
        _isolatedExportService = new IsolatedPdfExportService(packages, paths);
        _settingsService = new ApplicationSettingsService(paths);
        _applicationSettings = _settingsService.Load();
        _packages.BackupGenerationCount = _applicationSettings.BackupGenerationCount;
        _close = close;
        RefreshLocalizedOptions();
        OpenPdfCommand = new AsyncCommand(OpenPdfAsync, () => !IsOpeningDocument);
        OpenProjectCommand = new AsyncCommand(OpenProjectAsync, () => !IsOpeningDocument);
        ImportOcrDataCommand = new AsyncCommand(ImportOcrDataAsync, () => HasDocument);
        SaveProjectCommand = new AsyncCommand(SaveProjectAsync, () => HasDocument);
        SaveProjectAsCommand = new AsyncCommand(SaveProjectAsAsync, () => HasDocument);
        ExportPdfCommand = new AsyncCommand(ExportPdfAsync, () => HasDocument);
        OptimizeCurrentPageImageCommand = new AsyncCommand(
            OptimizeCurrentPageImageAsync,
            () => HasDocument && SelectedPage is not null);
        OptimizeDocumentImagesCommand = new AsyncCommand(
            OptimizeDocumentImagesAsync,
            () => HasDocument);
        InsertPagesCommand = new AsyncCommand(InsertPagesAsync, () => HasDocument);
        DeletePagesCommand = new AsyncCommand(DeleteSelectedPagesAsync, CanDeleteSelectedPages);
        RotatePagesLeftCommand = new AsyncCommand(() => RotateSelectedPagesAsync(-90), CanModifySelectedPages);
        RotatePagesRightCommand = new AsyncCommand(() => RotateSelectedPagesAsync(90), CanModifySelectedPages);
        PreviousPageCommand = new RelayCommand(GoToPreviousPage, () => CanGoPrevious);
        NextPageCommand = new RelayCommand(GoToNextPage, () => CanGoNext);
        ZoomInCommand = new RelayCommand(() => ChangeZoomStep(increase: true), () => CanUsePreview && ZoomPercent < 400);
        ZoomOutCommand = new RelayCommand(() => ChangeZoomStep(increase: false), () => CanUsePreview && ZoomPercent > 25);
        ActualSizeCommand = new RelayCommand(() => ZoomPercent = 100, () => CanUsePreview);
        AddBookmarkCommand = new RelayCommand(AddBookmark, () => HasDocument && SelectedPage is not null);
        AddChildBookmarkCommand = new RelayCommand(AddChildBookmark, () => HasDocument && SelectedBookmark is not null);
        DeleteBookmarkCommand = new RelayCommand(DeleteBookmark, () => HasDocument && SelectedBookmark is not null);
        MoveBookmarkUpCommand = new RelayCommand(() => MoveBookmark(-1), () => HasDocument && CanMoveBookmark(-1));
        MoveBookmarkDownCommand = new RelayCommand(() => MoveBookmark(1), () => HasDocument && CanMoveBookmark(1));
        GoToBookmarkCommand = new RelayCommand(GoToBookmark, () => HasDocument && SelectedBookmark is not null);
        ImportBookmarksCommand = new AsyncCommand(ImportBookmarksAsync, () => HasDocument);
        ExportBookmarksCommand = new AsyncCommand(ExportBookmarksAsync, () => HasDocument && BookmarkItems.Count > 0);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0);
        EqualWidthCommand = GeometryCommand(EqualizeSelectedWidths, () => _selectedOverlays.Count > 1);
        EqualHeightCommand = GeometryCommand(EqualizeSelectedHeights, () => _selectedOverlays.Count > 1);
        AlignLeftCommand = GeometryCommand(() => AlignSelection("left"), () => _selectedOverlays.Count > 1);
        AlignRightCommand = GeometryCommand(() => AlignSelection("right"), () => _selectedOverlays.Count > 1);
        AlignTopCommand = GeometryCommand(() => AlignSelection("top"), () => _selectedOverlays.Count > 1);
        AlignBottomCommand = GeometryCommand(() => AlignSelection("bottom"), () => _selectedOverlays.Count > 1);
        AlignHorizontalCenterCommand = GeometryCommand(() => AlignSelection("horizontal-center"), () => _selectedOverlays.Count > 1);
        AlignVerticalCenterCommand = GeometryCommand(() => AlignSelection("vertical-center"), () => _selectedOverlays.Count > 1);
        SetAlignmentReferenceCommand = GeometryCommand(SetAlignmentReference, () => HasMultipleSelection && SelectedOverlay is not null);
        MoveReadingEarlierCommand = GeometryCommand(() => MoveSelectedReadingOrder(-1), CanMoveReadingEarlier);
        MoveReadingLaterCommand = GeometryCommand(() => MoveSelectedReadingOrder(1), CanMoveReadingLater);
        RecalculateReadingOrderCommand = GeometryCommand(RecalculateReadingOrder, () => OverlayItems.Any(region => !region.IsDeleted));
        EqualizeCharacterAdvancesCommand = GeometryCommand(EqualizeCharacterAdvances, CanEqualizeCharacterAdvances);
        RestoreOriginalCharacterAdvancesCommand = GeometryCommand(RestoreOriginalCharacterAdvances, CanRestoreOriginalCharacterAdvances);
        EstimateCharacterAdvancesCommand = GeometryCommand(EstimateCharacterAdvances, CanEstimateCharacterAdvances);
        EstimateCharacterSuffixAdvancesCommand = GeometryCommand(EstimateCharacterSuffixAdvances, CanEstimateCharacterSuffixAdvances);
        PreviousCharacterCommand = new RelayCommand(() => MoveCharacterSelection(-1), CanMoveToPreviousCharacter);
        NextCharacterCommand = new RelayCommand(() => MoveCharacterSelection(1), CanMoveToNextCharacter);
        DecreaseCharacterAdvanceCommand = GeometryCommand(() => AdjustCharacterSelectionAdvance(-1), CanAdjustCharacterSelectionAdvance);
        IncreaseCharacterAdvanceCommand = GeometryCommand(() => AdjustCharacterSelectionAdvance(1), CanAdjustCharacterSelectionAdvance);
        SplitRegionAtSelectedCharacterCommand = GeometryCommand(SplitRegionAtSelectedCharacter, CanSplitRegionAtSelectedCharacter);
        MergeSelectedRegionsCommand = GeometryCommand(MergeSelectedRegions, CanMergeSelectedRegions);
        ToggleSelectedCharacterLockCommand = GeometryCommand(ToggleSelectedCharacterLock, CanToggleSelectedCharacterLock);
        ToggleGeometryLockCommand = GeometryCommand(
            ToggleGeometryLock,
            () => _selectedOverlays.Any(region => !region.IsDeleted) || SelectedOverlay is { IsDeleted: false });
        DecreaseLineCharacterSizeCommand = GeometryCommand(() => AdjustSelectedLineCharacterSizes(-1), CanAdjustSelectedLineCharacterSizes);
        IncreaseLineCharacterSizeCommand = GeometryCommand(() => AdjustSelectedLineCharacterSizes(1), CanAdjustSelectedLineCharacterSizes);
        DeleteOcrRegionsCommand = GeometryCommand(DeleteSelectedOcrRegions, () => _selectedOverlays.Count > 0);
        ToggleAddOcrRegionModeCommand = new RelayCommand(() => IsAddOcrRegionMode = !IsAddOcrRegionMode, () => CanAddOcrRegion);
        ExitCommand = new RelayCommand(_close);
        InitializeReview();
    }

    /// <summary>
    /// 現在の表示言語に合わせて、画面へバインドしている選択肢と動的表示を更新します。
    /// </summary>
    public void RefreshLocalization()
    {
        RefreshLocalizedOptions();
        OnPropertyChanged(nameof(ReviewSummary));
        foreach (var page in PageItems) page.RefreshLocalization();

        OnPropertyChanged(nameof(StorageModeText));
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(DocumentDescription));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(OcrDataSourceText));
        OnPropertyChanged(nameof(OverlaySummary));
        OnPropertyChanged(nameof(EqualizeCharacterAdvancesToolTip));
        OnPropertyChanged(nameof(RestoreOriginalCharacterAdvancesToolTip));
        OnPropertyChanged(nameof(EstimateCharacterAdvancesToolTip));
        OnPropertyChanged(nameof(EstimateCharacterSuffixAdvancesToolTip));
        OnPropertyChanged(nameof(SelectedCharacterLockToolTip));
    }

    /// <summary>確認状態と書字方向の選択肢を現在の表示言語で再構築します。</summary>
    private void RefreshLocalizedOptions()
    {
        _refreshingLocalizedOptions = true;
        try
        {
            // Suppress transient deselection writes while replacing localized options,
            // then explicitly restore the selection from the unchanged document values.
            ReviewStatusOptions =
            [
                new(ReviewStatus.Unreviewed, LocalizationService.Translate("未確認")),
                new(ReviewStatus.Verified, LocalizationService.Translate("確認済み")),
                new(ReviewStatus.Modified, LocalizationService.Translate("修正済み")),
                new(ReviewStatus.NeedsReview, LocalizationService.Translate("要再確認")),
                new(ReviewStatus.Excluded, LocalizationService.Translate("OCR対象外")),
                new(ReviewStatus.Deferred, LocalizationService.Translate("保留")),
            ];
            OnPropertyChanged(nameof(ReviewStatusOptions));

            WritingModeOptions =
            [
                new(WritingMode.Horizontal, LocalizationService.Translate("横書き")),
                new(WritingMode.Vertical, LocalizationService.Translate("縦書き")),
            ];
            OnPropertyChanged(nameof(WritingModeOptions));
            OnPropertyChanged(nameof(SelectedReviewStatus));
            OnPropertyChanged(nameof(SelectedWritingMode));
            foreach (var option in ReviewFilterOptions) option.RefreshLocalization();
        }
        finally { _refreshingLocalizedOptions = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>検索結果への移動時に、プレビューの選択表示を同期するよう画面へ通知します。</summary>
    public event EventHandler<OverlayRegionViewModel>? OcrSearchSelectionRequested;
    public ObservableCollection<PdfPageItem> PageItems { get; } = [];
    public ObservableCollection<OverlayRegionViewModel> OverlayItems { get; } = [];
    public ObservableCollection<BookmarkNodeViewModel> BookmarkItems { get; } = [];
    public ObservableCollection<ReviewStatusOption> ReviewStatusOptions { get; private set; } = [];
    public ObservableCollection<WritingModeOption> WritingModeOptions { get; private set; } = [];
    public AsyncCommand OpenPdfCommand { get; }
    public AsyncCommand OpenProjectCommand { get; }
    public AsyncCommand ImportOcrDataCommand { get; }
    public AsyncCommand SaveProjectCommand { get; }
    public AsyncCommand SaveProjectAsCommand { get; }
    public AsyncCommand ExportPdfCommand { get; }
    public AsyncCommand OptimizeCurrentPageImageCommand { get; }
    /// <summary>PDF全体を走査し、画像最適化候補の一覧と容量見込みを表示するコマンドです。</summary>
    public AsyncCommand OptimizeDocumentImagesCommand { get; }
    public AsyncCommand InsertPagesCommand { get; }
    public AsyncCommand DeletePagesCommand { get; }
    public AsyncCommand RotatePagesLeftCommand { get; }
    public AsyncCommand RotatePagesRightCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand ActualSizeCommand { get; }
    public RelayCommand AddBookmarkCommand { get; }
    public RelayCommand AddChildBookmarkCommand { get; }
    public RelayCommand DeleteBookmarkCommand { get; }
    public RelayCommand MoveBookmarkUpCommand { get; }
    public RelayCommand MoveBookmarkDownCommand { get; }
    public RelayCommand GoToBookmarkCommand { get; }
    public AsyncCommand ImportBookmarksCommand { get; }
    public AsyncCommand ExportBookmarksCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand EqualWidthCommand { get; }
    public RelayCommand EqualHeightCommand { get; }
    public RelayCommand AlignLeftCommand { get; }
    public RelayCommand AlignRightCommand { get; }
    public RelayCommand AlignTopCommand { get; }
    public RelayCommand AlignBottomCommand { get; }
    public RelayCommand AlignHorizontalCenterCommand { get; }
    public RelayCommand AlignVerticalCenterCommand { get; }
    public RelayCommand SetAlignmentReferenceCommand { get; }
    public RelayCommand MoveReadingEarlierCommand { get; }
    public RelayCommand MoveReadingLaterCommand { get; }
    public RelayCommand RecalculateReadingOrderCommand { get; }
    public RelayCommand EqualizeCharacterAdvancesCommand { get; }
    public RelayCommand RestoreOriginalCharacterAdvancesCommand { get; }
    public RelayCommand EstimateCharacterAdvancesCommand { get; }
    /// <summary>前処理付き一括自動調整で対象に指定できる、文書全体のページ数です。</summary>
    public int BatchCharacterAdjustmentPageCount =>
        _resolvedPdfPath is null ? 0 : PageItems.Count;
    public RelayCommand EstimateCharacterSuffixAdvancesCommand { get; }
    public RelayCommand PreviousCharacterCommand { get; }
    public RelayCommand NextCharacterCommand { get; }
    public RelayCommand DecreaseCharacterAdvanceCommand { get; }
    public RelayCommand IncreaseCharacterAdvanceCommand { get; }
    /// <summary>選択文字を後半領域の先頭として、OCR領域を2つへ分割します。</summary>
    public RelayCommand SplitRegionAtSelectedCharacterCommand { get; }
    /// <summary>同一行上で隣接する2つの選択OCR領域を1つへ結合します。</summary>
    public RelayCommand MergeSelectedRegionsCommand { get; }
    /// <summary>選択文字の位置と送り幅の固定状態を切り替えるコマンドです。</summary>
    public RelayCommand ToggleSelectedCharacterLockCommand { get; }
    /// <summary>選択中のOCR領域全体について、位置・寸法・回転の固定状態を切り替えます。</summary>
    public RelayCommand ToggleGeometryLockCommand { get; }
    public RelayCommand DecreaseLineCharacterSizeCommand { get; }
    public RelayCommand IncreaseLineCharacterSizeCommand { get; }
    public RelayCommand DeleteOcrRegionsCommand { get; }
    public RelayCommand ToggleAddOcrRegionModeCommand { get; }
    public RelayCommand ExitCommand { get; }
    public string StorageModeText => LocalizationService.Translate(
        _paths.Mode == StorageMode.Portable ? "ポータブルモード" : "インストールモード");
    public string SettingsFilePath => _settingsService.SettingsPath;
    public ApplicationSettings CurrentApplicationSettings => _applicationSettings;
    /// <summary>現在のプロジェクトに保存されているPDF初期表示設定です。</summary>
    public ViewerSettings CurrentViewerSettings => _project?.ViewerSettings ?? new ViewerSettings();
    /// <summary>現在のプロジェクトに保存されている、編集後のPDF文書情報です。</summary>
    public PdfDocumentMetadata? CurrentDocumentMetadata => _project?.DocumentMetadata;
    /// <summary>現在のプロジェクトで選択されているPDF出力バージョンです。</summary>
    public PdfOutputVersion CurrentOutputPdfVersion => _project?.OutputPdfVersion ?? PdfOutputVersion.Automatic;
    /// <summary>現在のプロジェクトで編集されたPDF文書全体の言語タグです。</summary>
    public string? CurrentDocumentLanguage => _project?.DocumentLanguage;
    public string EqualizeCharacterAdvancesToolTip => LocalizationService.IsEnglish
        ? $"Make all selected lines equal width ({DisplayShortcut(_applicationSettings.EqualizeCharacterAdvancesShortcut)})"
        : $"選択中のすべての行を等幅にする（{DisplayShortcut(_applicationSettings.EqualizeCharacterAdvancesShortcut)}）";
    public string RestoreOriginalCharacterAdvancesToolTip =>
        LocalizationService.IsEnglish
            ? $"Restore all selected lines to imported OCR widths ({DisplayShortcut(_applicationSettings.RestoreOriginalCharacterAdvancesShortcut)})"
            : $"選択中のすべての行をOCR取込時の文字幅へ戻す（{DisplayShortcut(_applicationSettings.RestoreOriginalCharacterAdvancesShortcut)}）";
    public string EstimateCharacterAdvancesToolTip => LocalizationService.IsEnglish
        ? $"Auto-adjust all selected OCR lines from the page image ({DisplayShortcut(_applicationSettings.EstimateCharacterAdvancesShortcut)})"
        : $"選択中のすべてのOCR行を画像から自動調整（{DisplayShortcut(_applicationSettings.EstimateCharacterAdvancesShortcut)}）";
    public string EstimateCharacterSuffixAdvancesToolTip => LocalizationService.IsEnglish
        ? $"Auto-adjust from the selected character onward ({DisplayShortcut(_applicationSettings.EstimateCharacterSuffixAdvancesShortcut)})"
        : $"選択文字以降を画像から自動調整（{DisplayShortcut(_applicationSettings.EstimateCharacterSuffixAdvancesShortcut)}）";
    public bool ShowToolbarText => _applicationSettings.ShowToolbarText;
    public double ToolbarButtonSize => _applicationSettings.ToolbarButtonSize;
    /// <summary>保存済みのサイズ設定とアイコン寸法を保ち、ボタン外周の余白だけを4px詰めます。</summary>
    public double CompactToolbarButtonSize => Math.Max(24, ToolbarButtonSize - 4);
    public double ToolbarIconSize => Math.Clamp(_applicationSettings.ToolbarButtonSize - 16, 14, 36);
    public bool ShowPropertyHelpText
    {
        get => _applicationSettings.ShowPropertyHelpText;
        set => UpdateDisplaySetting(
            _applicationSettings.ShowPropertyHelpText == value,
            _applicationSettings with { ShowPropertyHelpText = value },
            nameof(ShowPropertyHelpText));
    }
    public bool ShowPageListPanel
    {
        get => _applicationSettings.ShowPageListPanel;
        set => UpdateDisplaySetting(
            _applicationSettings.ShowPageListPanel == value,
            _applicationSettings with { ShowPageListPanel = value },
            nameof(ShowPageListPanel),
            nameof(PageListColumnWidth),
            nameof(PageListSplitterWidth));
    }
    public bool ShowPropertiesPanel
    {
        get => _applicationSettings.ShowPropertiesPanel;
        set => UpdateDisplaySetting(
            _applicationSettings.ShowPropertiesPanel == value,
            _applicationSettings with { ShowPropertiesPanel = value },
            nameof(ShowPropertiesPanel),
            nameof(PropertiesPanelColumnWidth),
            nameof(PropertiesPanelSplitterWidth));
    }
    public bool ShowStatusBar
    {
        get => _applicationSettings.ShowStatusBar;
        set => UpdateDisplaySetting(
            _applicationSettings.ShowStatusBar == value,
            _applicationSettings with { ShowStatusBar = value },
            nameof(ShowStatusBar));
    }
    public GridLength PageListColumnWidth => ShowPageListPanel
        ? new GridLength(_applicationSettings.PageListWidth)
        : new GridLength(0);
    public GridLength PageListSplitterWidth => ShowPageListPanel ? new GridLength(5) : new GridLength(0);
    public GridLength PropertiesPanelColumnWidth => ShowPropertiesPanel
        ? new GridLength(_applicationSettings.PropertiesPanelWidth)
        : new GridLength(0);
    public GridLength PropertiesPanelSplitterWidth => ShowPropertiesPanel ? new GridLength(5) : new GridLength(0);
    public bool ShowUnselectedCharacterCellBorders => _applicationSettings.ShowUnselectedCharacterCellBorders;
    public bool ShowPageThumbnails
    {
        get => _applicationSettings.ShowPageThumbnails;
        set
        {
            if (_applicationSettings.ShowPageThumbnails == value) return;
            _applicationSettings = _applicationSettings with { ShowPageThumbnails = value };
            OnPropertyChanged();
            if (value) StartThumbnailLoading();
            else CancelThumbnailLoading(clearImages: true);
            _ = SaveDisplaySettingsAsync();
        }
    }
    /// <summary>ページ一覧に表示するサムネイルの横幅です。</summary>
    public double PageThumbnailSize
    {
        get => _applicationSettings.PageThumbnailSize;
        set
        {
            var normalized = Math.Clamp(value, 72, 220);
            if (Math.Abs(_applicationSettings.PageThumbnailSize - normalized) < 0.01) return;
            _applicationSettings = _applicationSettings with { PageThumbnailSize = normalized };
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageThumbnailHeight));
            _ = SaveDisplaySettingsAsync();
        }
    }
    /// <summary>縦長ページを見やすく表示するためのサムネイル枠の高さです。</summary>
    public double PageThumbnailHeight => PageThumbnailSize * 1.28;
    /// <summary>ページ一覧で選択中のページ数です。</summary>
    public int SelectedPageCount => _selectedPageNumbers.Count;
    /// <summary>
    /// ページ一覧で選択中の1始まりページ番号です。処理中に選択が変わっても影響しないコピーを返します。
    /// </summary>
    public IReadOnlyList<int> SelectedPageNumbers => _selectedPageNumbers.ToArray();
    /// <summary>未選択文字セルの、表示倍率に依存しない画面上の枠線太さです。</summary>
    public Thickness CharacterCellBorderThickness => CreateCharacterCellBorderThickness(1.0);
    /// <summary>選択行内にある文字セルの枠線太さです。</summary>
    public Thickness CharacterRowCellBorderThickness => CreateCharacterCellBorderThickness(1.25);
    /// <summary>選択文字セルを識別するための枠線太さです。</summary>
    public Thickness SelectedCharacterCellBorderThickness => CreateCharacterCellBorderThickness(1.8);
    /// <summary>ロック済み文字セルを識別するための枠線太さです。</summary>
    public Thickness LockedCharacterCellBorderThickness => CreateCharacterCellBorderThickness(1.35);
    /// <summary>選択かつロック済みの文字セルを識別するための枠線太さです。</summary>
    public Thickness SelectedLockedCharacterCellBorderThickness => CreateCharacterCellBorderThickness(2.0);
    /// <summary>文字編集モードで選択中の行領域を示す枠線太さです。</summary>
    public Thickness CharacterRegionSelectionBorderThickness => CreateCharacterCellBorderThickness(1.6);
    public double CharacterAdvanceHandleThickness => _applicationSettings.CharacterHandleThickness;
    public double CharacterAdvanceHandleOpacity => _applicationSettings.CharacterHandleOpacity;
    public Thickness CharacterAdvanceHandleHorizontalMargin => new(-CharacterAdvanceHandleThickness / 2, 0, 0, 0);
    public Thickness CharacterAdvanceHandleVerticalMargin => new(0, -CharacterAdvanceHandleThickness / 2, 0, 0);
    public Brush CharacterAdvanceHandleBrush => CreateBrush(_applicationSettings.CharacterHandleColor);
    public Brush CharacterAdvanceHandleBorderBrush => CreateBrush(_applicationSettings.CharacterHandleColor, 0.95);
    public double ResizeHandleSize => _applicationSettings.ResizeHandleSize;
    public double ResizeHandleOpacity => _applicationSettings.ResizeHandleOpacity;
    public Brush ResizeHandleFillBrush => CreateBrush(_applicationSettings.ResizeHandleFillColor);
    public Brush ResizeHandleBorderBrush => CreateBrush(_applicationSettings.ResizeHandleBorderColor);
    public Brush OcrOverlayFillBrush => CreateBrush(_applicationSettings.OcrOverlayColor, _applicationSettings.OcrOverlayOpacity);
    public Brush OcrOverlayBorderBrush => CreateBrush(_applicationSettings.OcrOverlayColor, Math.Min(1, _applicationSettings.OcrOverlayOpacity * 3));
    public Brush OcrOverlayTextBrush => CreateBrush(_applicationSettings.OcrOverlayColor, Math.Min(1, _applicationSettings.OcrOverlayOpacity * 4));
    public string DocumentTitle { get => LocalizationService.Translate(_documentTitle); private set => Set(ref _documentTitle, value); }
    public string DocumentDescription { get => LocalizationService.Translate(_documentDescription); private set => Set(ref _documentDescription, value); }
    public string SourcePdfPath { get => _sourcePdfPath; private set => Set(ref _sourcePdfPath, value); }
    public string ProjectPath
    {
        get => _projectPath;
        private set
        {
            Set(ref _projectPath, value);
            OnPropertyChanged(nameof(CanRestoreProjectBackup));
        }
    }
    /// <summary>元PDFの初期ページまで正常に読み込めた場合だけ文書操作を許可します。</summary>
    public bool HasDocument
    {
        get => _hasDocument;
        private set
        {
            if (!Set(ref _hasDocument, value)) return;
            if (!value)
            {
                _renderCancellation?.Cancel();
                CancelReviewNavigation();
                IsAddOcrRegionMode = false;
            }
            OnPropertyChanged(nameof(CanRestoreProjectBackup));
            NotifyPreviewAvailability();
            NotifyNavigationState();
            SaveProjectCommand.RaiseCanExecuteChanged();
            SaveProjectAsCommand.RaiseCanExecuteChanged();
            ImportOcrDataCommand.RaiseCanExecuteChanged();
            ExportPdfCommand.RaiseCanExecuteChanged();
            OptimizeCurrentPageImageCommand.RaiseCanExecuteChanged();
            RaisePageManagementCommands();
            AddBookmarkCommand.RaiseCanExecuteChanged();
            AddChildBookmarkCommand.RaiseCanExecuteChanged();
            DeleteBookmarkCommand.RaiseCanExecuteChanged();
            MoveBookmarkUpCommand.RaiseCanExecuteChanged();
            MoveBookmarkDownCommand.RaiseCanExecuteChanged();
            GoToBookmarkCommand.RaiseCanExecuteChanged();
            ImportBookmarksCommand.RaiseCanExecuteChanged();
            ExportBookmarksCommand.RaiseCanExecuteChanged();
            NotifyReviewState();
        }
    }
    /// <summary>読込済み文書のプレビュー操作を許可する場合は<c>true</c>。</summary>
    public bool CanUsePreview => HasDocument && HasPreview;
    /// <summary>復旧対象となる保存先がある文書だけ、バックアップ復旧操作を許可します。</summary>
    public bool CanRestoreProjectBackup => HasDocument && _projectFilePath is not null;
    public bool IsOpeningDocument
    {
        get => _isOpeningDocument;
        private set
        {
            if (!Set(ref _isOpeningDocument, value)) return;
            OpenPdfCommand.RaiseCanExecuteChanged();
            OpenProjectCommand.RaiseCanExecuteChanged();
            NotifyReviewState();
        }
    }

    // Non-interactive diagnostics replace only the modal error display, not the loading path.
    internal Action<string, Exception>? ErrorDialogOverride { get; set; }
    internal Action? CommitPendingInputs { get; set; }
    internal Func<MessageBoxResult>? DocumentSwitchPromptOverride { get; set; }
    internal Func<Task<bool>>? SaveBeforeSwitchOverride { get; set; }

    private void NotifyPreviewAvailability()
    {
        OnPropertyChanged(nameof(CanUsePreview));
        OnPropertyChanged(nameof(CanAddOcrRegion));
        ZoomInCommand.RaiseCanExecuteChanged();
        ZoomOutCommand.RaiseCanExecuteChanged();
        ActualSizeCommand.RaiseCanExecuteChanged();
        ToggleAddOcrRegionModeCommand.RaiseCanExecuteChanged();
    }
    public string SourceHash { get => _sourceHash; private set => Set(ref _sourceHash, value); }
    public string StatusMessage { get => LocalizationService.Translate(_statusMessage); private set => Set(ref _statusMessage, value); }
    public string PageSummary { get => LocalizationService.Translate(_pageSummary); private set => Set(ref _pageSummary, value); }
    public string OcrDataSourceText { get => LocalizationService.Translate(_ocrDataSourceText); private set => Set(ref _ocrDataSourceText, value); }
    public string OverlaySummary { get => LocalizationService.Translate(_overlaySummary); private set => Set(ref _overlaySummary, value); }
    /// <summary>ステータスバーに長時間処理の進捗を表示する場合は<c>true</c>です。</summary>
    public bool IsBackgroundOperationVisible { get => _isBackgroundOperationVisible; private set => Set(ref _isBackgroundOperationVisible, value); }
    /// <summary>進捗バーを不確定表示にする場合は<c>true</c>です。</summary>
    public bool IsBackgroundOperationIndeterminate { get => _isBackgroundOperationIndeterminate; private set => Set(ref _isBackgroundOperationIndeterminate, value); }
    /// <summary>長時間処理の進捗率です。処理量を算出できる場合に0～100で更新します。</summary>
    public double BackgroundOperationProgress { get => _backgroundOperationProgress; private set => Set(ref _backgroundOperationProgress, Math.Clamp(value, 0, 100)); }
    /// <summary>現在実行している長時間処理の説明です。</summary>
    public string BackgroundOperationMessage { get => LocalizationService.Translate(_backgroundOperationMessage); private set => Set(ref _backgroundOperationMessage, value); }
    public ImageSource? PreviewImage
    {
        get => _previewImage;
        internal set
        {
            if (!Set(ref _previewImage, value)) return;
            OnPropertyChanged(nameof(HasPreview));
            NotifyPreviewAvailability();
            if (value is null) IsAddOcrRegionMode = false;
        }
    }
    public int PreviewPixelWidth { get => _previewPixelWidth; private set => Set(ref _previewPixelWidth, value); }
    public int PreviewPixelHeight { get => _previewPixelHeight; private set => Set(ref _previewPixelHeight, value); }
    public bool HasPreview => PreviewImage is not null;
    public bool IsAddOcrRegionMode
    {
        get => _isAddOcrRegionMode;
        set
        {
            if (value && !CanEditGeometry) return;
            if (!Set(ref _isAddOcrRegionMode, value)) return;
            StatusMessage = value
                ? "OCR領域追加モード: ページ上の追加位置をドラッグしてください。"
                : "OCR領域追加モードを終了しました。";
        }
    }
    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set { if (Set(ref _isPreviewLoading, value)) NotifyReviewState(); }
    }
    /// <summary>PDFの生成と保存後検証が進行中の場合は<c>true</c>です。</summary>
    public bool IsPdfExporting { get => _isPdfExporting; private set => Set(ref _isPdfExporting, value); }
    public bool IsOcrOverlayVisible { get => _isOcrOverlayVisible; set => Set(ref _isOcrOverlayVisible, value); }
    public int EditorModeIndex
    {
        get => _editorModeIndex;
        set
        {
            if (!Set(ref _editorModeIndex, Math.Clamp(value, 0, 2))) return;
            OnPropertyChanged(nameof(IsReadingOrderMode));
            OnEditorModeChanged();
        }
    }
    public bool IsReadingOrderMode => EditorModeIndex == 1;
    public int EditUnitIndex
    {
        get => _editUnitIndex;
        set
        {
            if (!Set(ref _editUnitIndex, Math.Clamp(value, 0, 2))) return;
            foreach (var region in OverlayItems) region.SelectedCharacterIndex = -1;
            ParagraphEditValidationMessage = string.Empty;
            OnPropertyChanged(nameof(EditUnit));
            OnPropertyChanged(nameof(IsLineEditMode));
            OnPropertyChanged(nameof(IsParagraphEditMode));
            OnPropertyChanged(nameof(IsCharacterEditMode));
            OnPropertyChanged(nameof(SelectedParagraphText));
            NotifyCharacterSelectionState();
            RaiseCharacterAdvanceCommands();
        }
    }
    public OcrEditUnit EditUnit => (OcrEditUnit)EditUnitIndex;
    public bool IsLineEditMode => EditUnit == OcrEditUnit.Line;
    public bool IsParagraphEditMode => EditUnit == OcrEditUnit.Paragraph;
    public bool IsCharacterEditMode => EditUnit == OcrEditUnit.Character;
    public string ParagraphEditValidationMessage
    {
        get => _paragraphEditValidationMessage;
        private set
        {
            if (!Set(ref _paragraphEditValidationMessage, value)) return;
            OnPropertyChanged(nameof(HasParagraphEditErrors));
        }
    }
    public bool HasParagraphEditErrors => ParagraphEditValidationMessage.Length > 0;
    public string SelectedParagraphText
    {
        get => string.Join(Environment.NewLine, _selectedOverlays.OrderBy(region => region.ReadingOrder).Select(region => region.Text));
        set => ApplyParagraphText(value ?? string.Empty);
    }
    public string SelectedCharacterText
    {
        get => SelectedOverlay?.GetSelectedCharacter() ?? string.Empty;
        set
        {
            if (SelectedOverlay is not { HasSingleCharacterSelection: true } region) return;
            region.ReplaceSelectedCharacter(value ?? string.Empty);
            OnPropertyChanged();
        }
    }
    public double SelectedCharacterAdvance
    {
        get => SelectedOverlay?.SelectedCharacterAdvance ?? 0;
        set
        {
            if (!CanEditGeometry) return;
            if (SelectedOverlay is not { HasCharacterSelection: true } region) return;
            region.SelectedCharacterAdvance = value;
            OnPropertyChanged();
        }
    }
    public bool HasSelectedCharacter => SelectedOverlay?.HasCharacterSelection == true;
    public bool HasSingleSelectedCharacter => SelectedOverlay?.HasSingleCharacterSelection == true;
    public bool HasMultipleSelectedCharacters => SelectedOverlay?.HasMultipleCharacterSelection == true;
    /// <summary>選択中の文字がすべて固定されているかを示します。</summary>
    public bool AreSelectedCharactersLocked => SelectedOverlay?.AreSelectedCharactersLocked == true;
    /// <summary>選択文字の現在の固定状態に対応する操作説明を返します。</summary>
    public string SelectedCharacterLockToolTip => LocalizationService.IsEnglish
        ? AreSelectedCharactersLocked ? "Unlock selected characters" : "Lock selected character positions and advances"
        : AreSelectedCharactersLocked ? "選択文字の固定を解除" : "選択文字の位置と送り幅を固定";
    /// <summary>選択中のOCR領域全体が固定されているかを示します。</summary>
    public bool IsSelectedGeometryLocked
    {
        get
        {
            var selected = GetSelectedGeometryLockTargets();
            return selected.Count > 0 && selected.All(region => region.IsGeometryLocked);
        }
    }
    /// <summary>選択中のOCR領域を移動、変形、回転できるかを示します。</summary>
    public bool IsSelectedGeometryEditable =>
        CanEditGeometry && GetSelectedGeometryLockTargets().Any(region => !region.IsGeometryLocked);
    public int SelectedCharacterCount => SelectedOverlay?.SelectedCharacterCount ?? 0;
    public string CharacterSelectionSummary => SelectedCharacterCount switch
    {
        0 => "文字を選択してください",
        1 => "選択中の文字",
        _ => $"選択中の文字（{SelectedCharacterCount}文字）",
    };
    public bool HasUnsavedChanges => _currentEditStateId != _savedEditStateId;
    public bool IsCurrentPageImageOptimizationEnabled =>
        SelectedPage is not null &&
        _project?.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage.PageNumber)?.ImageOptimization is { Enabled: true };
    public string CurrentPageImageOptimizationActionText =>
        IsCurrentPageImageOptimizationEnabled
            ? "このページの画像最適化を取り消す"
            : "このページ画像の余白・単色背景を削減...";
    public ReviewStatus SelectedReviewStatus
    {
        get => SelectedOverlay?.ReviewStatus ?? ReviewStatus.Unreviewed;
        set
        {
            if (_refreshingLocalizedOptions) return;
            var affected = _selectedOverlays.Count > 0
                ? _selectedOverlays.ToArray()
                : SelectedOverlay is null ? [] : [SelectedOverlay];
            if (affected.Length == 0 || affected.All(region => region.ReviewStatus == value)) return;
            ApplyRegionEdit("確認ステータスを変更", affected, () =>
            {
                foreach (var region in affected) region.ReviewStatus = value;
            });
            OnPropertyChanged();
        }
    }
    public WritingMode? SelectedWritingMode
    {
        get
        {
            var affected = _selectedOverlays.Count > 0
                ? _selectedOverlays.ToArray()
                : SelectedOverlay is null ? [] : [SelectedOverlay];
            if (affected.Length == 0) return null;
            var first = affected[0].IsVertical ? WritingMode.Vertical : WritingMode.Horizontal;
            return affected.All(region => (region.IsVertical ? WritingMode.Vertical : WritingMode.Horizontal) == first)
                ? first
                : null;
        }
        set
        {
            if (value is null || !CanEditGeometry || _refreshingLocalizedOptions) return;
            var affected = (_selectedOverlays.Count > 0
                    ? _selectedOverlays.ToArray()
                    : SelectedOverlay is null ? [] : [SelectedOverlay])
                .Where(region => !region.IsDeleted)
                .ToArray();
            var makeVertical = value == WritingMode.Vertical;
            if (affected.Length == 0 || affected.All(region => region.IsVertical == makeVertical)) return;
            ApplyRegionEdit(
                makeVertical ? "文字方向を縦書きへ変更" : "文字方向を横書きへ変更",
                affected,
                () =>
                {
                    foreach (var region in affected)
                    {
                        region.IsVertical = makeVertical;
                        region.ReviewStatus = ReviewStatus.Modified;
                    }
                });
            OnPropertyChanged();
            StatusMessage = affected.Length == 1
                ? $"文字方向を{(makeVertical ? "縦書き" : "横書き")}へ変更しました。"
                : $"{affected.Length}領域を{(makeVertical ? "縦書き" : "横書き")}へ変更しました。";
        }
    }
    public double ZoomPercent
    {
        get => _zoomPercent;
        set
        {
            var normalized = Math.Clamp(Math.Round(value), 25, 400);
            if (!Set(ref _zoomPercent, normalized)) return;
            OnPropertyChanged(nameof(ZoomFactor));
            OnPropertyChanged(nameof(InverseZoomFactor));
            OnPropertyChanged(nameof(ZoomDisplay));
            OnPropertyChanged(nameof(ZoomSliderPosition));
            NotifyCharacterCellBorderThicknesses();
            ZoomInCommand.RaiseCanExecuteChanged();
            ZoomOutCommand.RaiseCanExecuteChanged();
        }
    }
    public double ZoomFactor => ZoomPercent / 100d;
    public double InverseZoomFactor => 1d / ZoomFactor;
    public string ZoomDisplay => $"{ZoomPercent:0}%";

    /// <summary>倍率スライダー専用の位置（0～100）。実際の倍率とは分離し、中央を100%にします。</summary>
    public double ZoomSliderPosition
    {
        get => EditorInteractionMath.ZoomPercentToSliderPosition(ZoomPercent);
        set => ZoomPercent = EditorInteractionMath.SliderPositionToZoomPercent(value);
    }

    /// <summary>
    /// 現在値から次の標準倍率へ拡大または縮小します。
    /// 手入力された中間値の場合も、進行方向側にある最初の標準倍率へ移動します。
    /// </summary>
    private void ChangeZoomStep(bool increase)
    {
        ZoomPercent = increase
            ? StandardZoomSteps.First(step => step > ZoomPercent)
            : StandardZoomSteps.Last(step => step < ZoomPercent);
    }
    public BookmarkNodeViewModel? SelectedBookmark
    {
        get => _selectedBookmark;
        set
        {
            if (!Set(ref _selectedBookmark, value)) return;
            OnPropertyChanged(nameof(HasSelectedBookmark));
            AddChildBookmarkCommand.RaiseCanExecuteChanged();
            DeleteBookmarkCommand.RaiseCanExecuteChanged();
            MoveBookmarkUpCommand.RaiseCanExecuteChanged();
            MoveBookmarkDownCommand.RaiseCanExecuteChanged();
            GoToBookmarkCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HasSelectedBookmark => SelectedBookmark is not null;
    public OverlayRegionViewModel? SelectedOverlay
    {
        get => _selectedOverlay;
        set
        {
            if (!Set(ref _selectedOverlay, value)) return;
            NotifyReviewState();
            OnPropertyChanged(nameof(HasSelectedOverlay));
            NotifyCharacterSelectionState();
            OnPropertyChanged(nameof(SelectedReviewStatus));
            OnPropertyChanged(nameof(SelectedWritingMode));
            MoveReadingEarlierCommand.RaiseCanExecuteChanged();
            MoveReadingLaterCommand.RaiseCanExecuteChanged();
            RaiseCharacterAdvanceCommands();
        }
    }
    public bool HasSelectedOverlay => SelectedOverlay is not null;
    public bool HasOverlaySelection => _selectedOverlays.Count > 0;
    public int SelectedOverlayCount => _selectedOverlays.Count;
    /// <summary>現在ページで選択中のOCR領域を、画面上の選択順を保った読み取り専用配列として返します。</summary>
    public IReadOnlyList<OverlayRegionViewModel> SelectedOverlays => _selectedOverlays.ToArray();
    public bool HasMultipleSelection => _selectedOverlays.Count > 1;
    public string AlignmentReferenceDescription => _alignmentReference is null
        ? "基準領域は未設定です"
        : $"基準: 「{Abbreviate(_alignmentReference.Text)}」\n幅 {_alignmentReference.Width:0.0} / 高さ {_alignmentReference.Height:0.0}";
    public bool CanGoPrevious => HasDocument && SelectedPage?.PageNumber > 1;
    public bool CanGoNext => HasDocument && SelectedPage is not null && SelectedPage.PageNumber < PageItems.Count;

    /// <summary>
    /// アプリ設定を保存し、変更された表示・編集補助設定を実行中の画面へ反映します。
    /// </summary>
    /// <param name="settings">設定画面で確定した値。</param>
    /// <returns>保存と反映に成功した場合は<see langword="true"/>。</returns>
    public async Task<bool> ApplyApplicationSettingsAsync(ApplicationSettings settings)
    {
        try
        {
            var normalized = settings.Normalize();
            await _settingsService.SaveAsync(normalized);
            var thumbnailVisibilityChanged = _applicationSettings.ShowPageThumbnails != normalized.ShowPageThumbnails;
            _applicationSettings = normalized;
            _packages.BackupGenerationCount = normalized.BackupGenerationCount;
            OnPropertyChanged(nameof(CurrentApplicationSettings));
            OnPropertyChanged(nameof(ShowToolbarText));
            OnPropertyChanged(nameof(ToolbarButtonSize));
            OnPropertyChanged(nameof(CompactToolbarButtonSize));
            OnPropertyChanged(nameof(ToolbarIconSize));
            OnPropertyChanged(nameof(ShowPropertyHelpText));
            OnPropertyChanged(nameof(ShowPageListPanel));
            OnPropertyChanged(nameof(ShowPropertiesPanel));
            OnPropertyChanged(nameof(ShowStatusBar));
            OnPropertyChanged(nameof(PageListColumnWidth));
            OnPropertyChanged(nameof(PageListSplitterWidth));
            OnPropertyChanged(nameof(PropertiesPanelColumnWidth));
            OnPropertyChanged(nameof(PropertiesPanelSplitterWidth));
            OnPropertyChanged(nameof(ShowUnselectedCharacterCellBorders));
            NotifyCharacterCellBorderThicknesses();
            OnPropertyChanged(nameof(ShowPageThumbnails));
            OnPropertyChanged(nameof(PageThumbnailSize));
            OnPropertyChanged(nameof(PageThumbnailHeight));
            OnPropertyChanged(nameof(CharacterAdvanceHandleThickness));
            OnPropertyChanged(nameof(CharacterAdvanceHandleOpacity));
            OnPropertyChanged(nameof(CharacterAdvanceHandleHorizontalMargin));
            OnPropertyChanged(nameof(CharacterAdvanceHandleVerticalMargin));
            OnPropertyChanged(nameof(CharacterAdvanceHandleBrush));
            OnPropertyChanged(nameof(CharacterAdvanceHandleBorderBrush));
            OnPropertyChanged(nameof(ResizeHandleSize));
            OnPropertyChanged(nameof(ResizeHandleOpacity));
            OnPropertyChanged(nameof(ResizeHandleFillBrush));
            OnPropertyChanged(nameof(ResizeHandleBorderBrush));
            OnPropertyChanged(nameof(OcrOverlayFillBrush));
            OnPropertyChanged(nameof(OcrOverlayBorderBrush));
            OnPropertyChanged(nameof(OcrOverlayTextBrush));
            OnPropertyChanged(nameof(EqualizeCharacterAdvancesToolTip));
            OnPropertyChanged(nameof(RestoreOriginalCharacterAdvancesToolTip));
            OnPropertyChanged(nameof(EstimateCharacterAdvancesToolTip));
            OnPropertyChanged(nameof(EstimateCharacterSuffixAdvancesToolTip));
            if (thumbnailVisibilityChanged)
            {
                if (ShowPageThumbnails) StartThumbnailLoading();
                else CancelThumbnailLoading(clearImages: true);
            }
            TrimUndoHistory();
            StatusMessage = "アプリケーション設定を保存しました。";
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("アプリケーション設定を保存できませんでした。", ex);
            return false;
        }
    }

    /// <summary>文書プロパティで確定したPDFの初期表示設定を編集モデルへ反映します。</summary>
    public void UpdateViewerSettings(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_project is null || _project.ViewerSettings == settings) return;
        _project = _project with { ViewerSettings = settings };
        OnPropertyChanged(nameof(CurrentViewerSettings));
        MarkNonUndoableChange();
        StatusMessage = "PDFの初期表示設定を更新しました。";
    }

    /// <summary>文書プロパティで確定した初期表示設定と文書情報を編集モデルへ反映します。</summary>
    public void UpdateDocumentProperties(
        ViewerSettings settings,
        PdfDocumentMetadata metadata,
        PdfOutputVersion outputPdfVersion,
        string documentLanguage)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(metadata);
        if (_project is null ||
            (_project.ViewerSettings == settings &&
             _project.DocumentMetadata == metadata &&
             _project.OutputPdfVersion == outputPdfVersion &&
             string.Equals(_project.DocumentLanguage, documentLanguage, StringComparison.Ordinal)))
            return;

        _project = _project with
        {
            ViewerSettings = settings,
            DocumentMetadata = metadata,
            OutputPdfVersion = outputPdfVersion,
            DocumentLanguage = documentLanguage,
        };
        OnPropertyChanged(nameof(CurrentViewerSettings));
        OnPropertyChanged(nameof(CurrentDocumentMetadata));
        OnPropertyChanged(nameof(CurrentOutputPdfVersion));
        OnPropertyChanged(nameof(CurrentDocumentLanguage));
        MarkNonUndoableChange();
        StatusMessage = "PDFの文書情報と初期表示設定を更新しました。";
    }

    /// <summary>設定した間隔が経過し、未保存編集がある場合だけ作業用の自動保存ファイルを更新します。</summary>
    public async Task AutoSaveIfDueAsync()
    {
        if (_autoSaveInProgress || IsBackgroundOperationVisible || IsOpeningDocument) return;
        var idle = DateTimeOffset.UtcNow - _lastUserActivityAtUtc >= TimeSpan.FromSeconds(30);
        if (idle) CommitPendingInputs?.Invoke();
        if (_project is null || !HasUnsavedChanges ||
            !_applicationSettings.AutoSaveEnabled || _autoSaveInProgress || IsBackgroundOperationVisible || IsOpeningDocument)
            return;
        if (_lastAutoSavedEditStateId == _currentEditStateId) return;
        if (!idle && DateTimeOffset.UtcNow - _lastAutoSaveAtUtc <
            TimeSpan.FromMinutes(_applicationSettings.AutoSaveIntervalMinutes))
            return;

        _autoSaveInProgress = true;
        var stateId = _currentEditStateId;
        try
        {
            SynchronizeProjectPages();
            var autoSavePath = AutoSaveRecoveryPath!;
            await _packages.SaveAutoSaveAsync(autoSavePath, _project,
                _hasPageStructureEdits || _project.SourcePdf.IsEmbedded || _projectFilePath is null, _thumbnailCache);
            _lastAutoSaveAtUtc = DateTimeOffset.UtcNow;
            _lastAutoSavedEditStateId = stateId;
            if (_projectFilePath is null)
                StatusMessage = LocalizationService.IsEnglish
                    ? $"Recovery project saved: {autoSavePath}"
                    : $"復旧用プロジェクトを自動保存しました: {autoSavePath}";
            await _log.WriteAsync(LogLevel.Information, "project.autosave",
                $"Autosaved project {_project.ProjectId} to {autoSavePath}");
        }
        catch (Exception ex)
        {
            StatusMessage = "自動保存に失敗しました。通常保存ファイルは変更されていません。";
            await _log.WriteAsync(LogLevel.Error, "project.autosave.failed", ex.Message, ex);
        }
        finally { _autoSaveInProgress = false; }
    }

    /// <summary>現在の編集状態を一時パッケージへ保存して、ZIPとJSONの整合性を検証します。</summary>
    public async Task<ProjectValidationResult?> ValidateCurrentProjectAsync()
    {
        if (_project is null)
        {
            StatusMessage = "検証するプロジェクトが開かれていません。";
            return null;
        }

        var validationPath = Path.Combine(_paths.WorkspaceDirectory, $"validate-{Guid.NewGuid():N}.pdfocrproj");
        BeginBackgroundOperation("プロジェクトを検証しています...");
        try
        {
            SynchronizeProjectPages();
            await _packages.SaveAutoSaveAsync(validationPath, _project, _hasPageStructureEdits || _project.SourcePdf.IsEmbedded, _thumbnailCache);
            var result = await _packages.ValidateAsync(validationPath);
            StatusMessage = result.IsValid ? "プロジェクトの検証が完了しました。" : "プロジェクトの検証で問題が見つかりました。";
            return result;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("プロジェクトを検証できませんでした。", ex);
            return null;
        }
        finally
        {
            try { if (File.Exists(validationPath)) File.Delete(validationPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            EndBackgroundOperation();
        }
    }

    /// <summary>自動保存を優先し、次に新しい世代バックアップを探して現在のプロジェクトを復旧します。</summary>
    public async Task<bool> RestoreLatestProjectBackupAsync()
    {
        if (_projectFilePath is null)
        {
            StatusMessage = "保存済みのプロジェクトを開いてから復旧してください。";
            return false;
        }

        BeginBackgroundOperation("バックアップからプロジェクトを復旧しています...");
        try
        {
            var projectPath = _projectFilePath;
            var restoredFrom = await _packages.RestoreLatestValidBackupAsync(projectPath);
            if (restoredFrom is null) return false;
            await LoadProjectForDiagnosticsAsync(projectPath);
            _lastAutoSaveAtUtc = DateTimeOffset.UtcNow;
            StatusMessage = "バックアップからプロジェクトを復旧しました。";
            await _log.WriteAsync(LogLevel.Information, "project.restore", $"Restored {projectPath} from {restoredFrom}");
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("バックアップからプロジェクトを復旧できませんでした。", ex);
            return false;
        }
        finally { EndBackgroundOperation(); }
    }

    /// <summary>
    /// ページ全体の拡大率を相殺し、設定した枠線が画面上で一定の太さに見える値へ変換します。
    /// </summary>
    private Thickness CreateCharacterCellBorderThickness(double emphasis)
    {
        var screenThickness = _applicationSettings.CharacterCellBorderThickness * emphasis;
        return new Thickness(Math.Max(0.05, screenThickness * InverseZoomFactor));
    }

    /// <summary>文字セル枠の基準値または表示倍率が変わったことを描画側へ通知します。</summary>
    private void NotifyCharacterCellBorderThicknesses()
    {
        OnPropertyChanged(nameof(CharacterCellBorderThickness));
        OnPropertyChanged(nameof(CharacterRowCellBorderThickness));
        OnPropertyChanged(nameof(SelectedCharacterCellBorderThickness));
        OnPropertyChanged(nameof(LockedCharacterCellBorderThickness));
        OnPropertyChanged(nameof(SelectedLockedCharacterCellBorderThickness));
        OnPropertyChanged(nameof(CharacterRegionSelectionBorderThickness));
    }

    public PdfPageItem? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (ReferenceEquals(_selectedPage, value)) return;
            CancelReviewNavigation();
            if (_selectedPage is not null) _selectedPage.IsCurrent = false;
            if (!Set(ref _selectedPage, value)) return;
            if (_selectedPage is not null) _selectedPage.IsCurrent = true;
            NotifyNavigationState();
            OnPropertyChanged(nameof(IsCurrentPageImageOptimizationEnabled));
            OnPropertyChanged(nameof(CurrentPageImageOptimizationActionText));
            OptimizeCurrentPageImageCommand.RaiseCanExecuteChanged();
            AddBookmarkCommand.RaiseCanExecuteChanged();
            if (value is not null && _resolvedPdfPath is not null) _ = RenderSelectedPageAsync();
        }
    }

    private void UpdateDisplaySetting(
        bool unchanged,
        ApplicationSettings updatedSettings,
        params string[] propertyNames)
    {
        if (unchanged) return;
        _applicationSettings = updatedSettings.Normalize();
        foreach (var propertyName in propertyNames) OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(CurrentApplicationSettings));
        _ = SaveDisplaySettingsAsync();
    }

    private async Task SaveDisplaySettingsAsync()
    {
        try { await _settingsService.SaveAsync(_applicationSettings); }
        catch (Exception ex)
        {
            StatusMessage = "画面表示設定を保存できませんでした。";
            await _log.WriteAsync(LogLevel.Warning, "settings.display-save.failed", ex.Message, ex);
        }
    }

    /// <summary>ステータスバーに画面全体の処理開始を表示します。</summary>
    /// <param name="message">利用者へ表示する処理内容。</param>
    /// <param name="isIndeterminate">処理率を算出できず、不確定表示にする場合は<c>true</c>。</param>
    private void BeginBackgroundOperation(string message, bool isIndeterminate = true)
    {
        _backgroundOperationId++;
        BackgroundOperationMessage = message;
        BackgroundOperationProgress = 0;
        IsBackgroundOperationIndeterminate = isIndeterminate;
        IsBackgroundOperationVisible = true;
    }

    /// <summary>ステータスバーに表示中の画面全体処理を更新します。</summary>
    /// <param name="message">現在の処理内容。</param>
    /// <param name="current">完了した処理単位。</param>
    /// <param name="total">全処理単位。</param>
    private void UpdateBackgroundOperation(string message, int current, int total)
    {
        BackgroundOperationMessage = message;
        IsBackgroundOperationIndeterminate = total <= 0;
        BackgroundOperationProgress = total <= 0 ? 0 : current * 100d / total;
    }

    /// <summary>進捗通知が現在実行中の画面全体処理に属しているかを判定します。</summary>
    /// <param name="operationId">処理開始時に取得した世代番号。</param>
    /// <returns>現在の処理からの通知で、進捗表示が有効な場合は<c>true</c>。</returns>
    private bool IsCurrentBackgroundOperation(long operationId) =>
        IsBackgroundOperationVisible && _backgroundOperationId == operationId;

    /// <summary>ステータスバーの画面全体処理表示を終了します。</summary>
    private void EndBackgroundOperation()
    {
        _backgroundOperationId++;
        IsBackgroundOperationVisible = false;
        IsBackgroundOperationIndeterminate = true;
        BackgroundOperationProgress = 0;
        BackgroundOperationMessage = string.Empty;
    }

    private async Task OpenPdfAsync()
    {
        var dialog = new OpenFileDialog { Filter = "PDFファイル (*.pdf)|*.pdf", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        await OpenDocumentPathAsync(dialog.FileName);
    }

    /// <summary>メニューとファイル関連付けから共用する読込処理。初期ページの表示まで成功した場合だけtrueを返します。</summary>
    public async Task<bool> OpenDocumentPathAsync(string filePath)
    {
        if (IsOpeningDocument || IsPdfExporting || _autoSaveInProgress) return false;
        IsOpeningDocument = true;
        try
        {
            CommitPendingInputs?.Invoke();
            if (HasUnsavedChanges)
            {
                var choice = DocumentSwitchPromptOverride?.Invoke() ?? MessageBox.Show(
                    LocalizationService.IsEnglish
                        ? "Save your project changes before opening another document?"
                        : "別の文書を開く前に、プロジェクトへ変更を保存しますか？",
                    "PDF Correctorium", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
                if (choice == MessageBoxResult.Cancel || choice == MessageBoxResult.None) return false;
                if (choice == MessageBoxResult.Yes &&
                    !await (SaveBeforeSwitchOverride?.Invoke() ?? SaveBeforeCloseAsync())) return false;
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            var fullPath = Path.GetFullPath(filePath);
            var extension = Path.GetExtension(fullPath);
            var isProject = extension.Equals(ProjectPackageService.ProjectExtension, StringComparison.OrdinalIgnoreCase);
            if (!isProject && !extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("PDFファイル（.pdf）またはPDF Correctoriumプロジェクト（.pdfocrproj）を指定してください。");
            BeginBackgroundOperation(isProject ? "プロジェクトを検証して開いています..." : "PDFを開いています...");
            if (isProject) await LoadProjectAsync(fullPath);
            else await LoadPdfAsync(fullPath);
            // RenderPageAsync reports rendering errors itself; never overwrite those with a success message.
            if (!HasDocument) return false;
            StatusMessage = isProject ? "プロジェクトを検証して開きました。" : "PDFを開きました。編集内容はプロジェクトへ保存してください。";
            await _log.WriteAsync(LogLevel.Information, isProject ? "project.open" : "document.open",
                $"Opened {Path.GetFileName(fullPath)} with {PageItems.Count} pages");
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("指定したファイルを開けませんでした。", ex);
            return false;
        }
        finally
        {
            EndBackgroundOperation();
            IsOpeningDocument = false;
        }
    }

    internal Task LoadPdfForDiagnosticsAsync(string pdfPath) => LoadPdfAsync(pdfPath);
    internal Task LoadProjectForDiagnosticsAsync(string projectPath) => LoadProjectAsync(projectPath);

    private async Task LoadPdfAsync(string pdfPath)
    {
        StatusMessage = "元PDFを確認しています...";
        var source = await _packages.CreateSourceReferenceAsync(pdfPath);
        var project = new PdfCorrectoriumProject { Name = Path.GetFileNameWithoutExtension(pdfPath), SourcePdf = source };
        await ApplyProjectAsync(pdfPath, project, null, new Dictionary<int, byte[]>());
    }

    private async Task LoadProjectAsync(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var validation = await _packages.ValidateAsync(fullPath);
        if (!validation.IsValid)
            throw new InvalidDataException(string.Join(Environment.NewLine, validation.Issues.Select(issue => issue.Message)));
        var project = await _packages.OpenAsync(fullPath);
        var thumbnails = await _packages.ReadThumbnailCacheAsync(fullPath);
        var projectDirectory = Path.GetDirectoryName(fullPath)!;
        if (!await _packages.VerifySourceAsync(project.SourcePdf, projectDirectory))
            throw new InvalidDataException("The source PDF is missing or its SHA-256 fingerprint does not match this project.");
        var sourcePath = project.SourcePdf.IsEmbedded
            ? await _packages.MaterializeEmbeddedSourceAsync(fullPath, project.SourcePdf, _paths.CacheDirectory)
            : _packages.ResolveSourcePath(project.SourcePdf, projectDirectory);
        await ApplyProjectAsync(sourcePath, project, fullPath, thumbnails);
    }

    internal Task RenderPageForDiagnosticsAsync(int pageNumber) =>
        RenderPageAsync(pageNumber, populatePageList: false);

    /// <summary>
    /// 画面コードで捕捉した例外を、内部例外とスタックトレースを含めて診断ログへ記録します。
    /// </summary>
    /// <param name="eventName">障害箇所を識別するイベント名。</param>
    /// <param name="exception">記録する例外。</param>
    /// <returns>ログへの書き込みを表すタスク。</returns>
    internal Task WriteDiagnosticErrorAsync(string eventName, Exception exception) =>
        _log.WriteAsync(LogLevel.Error, eventName, exception.Message, exception);

    /// <summary>
    /// 現在のPDFに含まれるOCR文字列を検索します。未表示ページも必要に応じて読み込みます。
    /// </summary>
    /// <param name="options">検索文字列、検索範囲、比較方法。</param>
    /// <param name="progress">ページ走査状況を受け取る任意の進捗通知。</param>
    /// <param name="detailedProgress">進捗率表示に使う現在ページ数と総ページ数。</param>
    /// <param name="cancellationToken">検索を中断するための通知。</param>
    /// <returns>ページ順、読み順、文字位置順に並んだ検索結果。</returns>
    public async Task<IReadOnlyList<OcrTextSearchMatch>> SearchOcrTextAsync(
        OcrTextSearchOptions options,
        IProgress<string>? progress = null,
        IProgress<OperationProgressUpdate>? detailedProgress = null,
        CancellationToken cancellationToken = default)
    {
        var searchText = options.SearchText ?? string.Empty;
        if (string.IsNullOrEmpty(searchText) || _resolvedPdfPath is null || PageItems.Count == 0)
            return [];

        var pageNumbers = options.CurrentPageOnly && SelectedPage is not null
            ? new[] { SelectedPage.PageNumber }
            : Enumerable.Range(1, PageItems.Count).ToArray();
        var regularExpression = options.UseRegularExpression ? CreateSearchRegex(options) : null;
        var matches = new List<OcrTextSearchMatch>();

        for (var pageIndex = 0; pageIndex < pageNumbers.Length; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageNumber = pageNumbers[pageIndex];
            var message = $"{pageNumber}ページを検索しています（{pageIndex + 1}/{pageNumbers.Length}）...";
            progress?.Report(message);
            detailedProgress?.Report(new OperationProgressUpdate(pageIndex + 1, pageNumbers.Length, message));
            var regions = await EnsurePageOverlaysLoadedForSearchAsync(pageNumber);
            foreach (var region in regions
                         .Where(region => !region.IsDeleted && (!options.InvisibleOnly || region.IsInvisible))
                         .OrderBy(region => region.ReadingOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var occurrence in FindSearchOccurrences(region.Text, options, regularExpression))
                {
                    matches.Add(new OcrTextSearchMatch(
                        pageNumber,
                        region.Id,
                        occurrence.StartIndex,
                        occurrence.Length,
                        region.Text,
                        CreateSearchPreview(region.Text, occurrence.StartIndex, occurrence.Length)));
                }
            }
        }

        detailedProgress?.Report(new OperationProgressUpdate(pageNumbers.Length, pageNumbers.Length, "検索を完了しました。"));

        StatusMessage = matches.Count == 0
            ? $"「{searchText}」は見つかりませんでした。"
            : $"「{searchText}」が{matches.Count}件見つかりました。";
        return matches;
    }

    /// <summary>検索結果のページを表示し、該当OCR領域を選択します。</summary>
    /// <param name="match">表示する検索結果。</param>
    /// <returns>対象領域を選択できた場合は<c>true</c>。</returns>
    public async Task<bool> NavigateToOcrSearchMatchAsync(OcrTextSearchMatch match)
    {
        if (match.PageNumber < 1 || match.PageNumber > PageItems.Count) return false;
        var page = PageItems[match.PageNumber - 1];
        if (!ReferenceEquals(_selectedPage, page) || PreviewImage is null)
        {
            SetCurrentPageWithoutRendering(page);
            await RenderPageAsync(match.PageNumber, populatePageList: false);
        }

        if (!_pageOverlays.TryGetValue(match.PageNumber, out var regions)) return false;
        var region = regions.FirstOrDefault(candidate => candidate.Id == match.RegionId && !candidate.IsDeleted);
        if (region is null) return false;
        ClearOcrSearchHighlight();
        EditUnitIndex = (int)OcrEditUnit.Character;
        region.SelectCharacterRangeByTextOffset(match.StartIndex, match.Length);
        region.SetSearchHighlightByTextOffset(match.StartIndex, match.Length);
        SetOverlaySelection([region], region);
        OcrSearchSelectionRequested?.Invoke(this, region);
        StatusMessage = $"{match.PageNumber}ページの検索結果を選択しました。";
        return true;
    }

    /// <summary>検索結果へ移動した際の一致文字強調を、読み込み済みページから解除します。</summary>
    public void ClearOcrSearchHighlight()
    {
        foreach (var region in _pageOverlays.Values.SelectMany(regions => regions))
            region.ClearSearchHighlight();
    }

    /// <summary>
    /// 文書全体を走査し、寸法が近いOCR領域群に対して文字数だけが極端に異なる候補を返します。
    /// </summary>
    /// <param name="options">寸法許容差、比較に必要な件数、外れ値とする文字数比率。</param>
    /// <param name="progress">ページ読込状況を受け取る任意の進捗通知。</param>
    /// <param name="detailedProgress">進捗率表示に使う現在ページ数と総ページ数。</param>
    /// <param name="cancellationToken">分析を中断するための通知。</param>
    public async Task<IReadOnlyList<OcrCharacterCountAnomaly>> AnalyzeOcrCharacterCountAnomaliesAsync(
        OcrCharacterCountAnalysisOptions options,
        IProgress<string>? progress = null,
        IProgress<OperationProgressUpdate>? detailedProgress = null,
        CancellationToken cancellationToken = default)
    {
        var samples = await CollectOcrQualitySamplesAsync(progress, detailedProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var results = new OcrQualityAnalyzer().FindCharacterCountAnomalies(samples, options);
        StatusMessage = results.Count == 0
            ? "文字数が極端に異なるOCR領域は見つかりませんでした。"
            : $"文字数が極端に異なるOCR領域が{results.Count}件見つかりました。";
        return results;
    }

    /// <summary>
    /// 指定キーワードの全出現箇所を比較し、行の太さに対する文字幅比率が外れた候補を返します。
    /// </summary>
    /// <param name="options">キーワード、比較方法、許容差、基準値に必要な出現件数。</param>
    /// <param name="progress">ページ読込状況を受け取る任意の進捗通知。</param>
    /// <param name="detailedProgress">進捗率表示に使う現在ページ数と総ページ数。</param>
    /// <param name="cancellationToken">分析を中断するための通知。</param>
    public async Task<OcrKeywordWidthAnalysisResult> AnalyzeOcrKeywordWidthsAsync(
        OcrKeywordWidthAnalysisOptions options,
        IProgress<string>? progress = null,
        IProgress<OperationProgressUpdate>? detailedProgress = null,
        CancellationToken cancellationToken = default)
    {
        var samples = await CollectOcrQualitySamplesAsync(progress, detailedProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = new OcrQualityAnalyzer().AnalyzeKeywordWidths(samples, options);
        StatusMessage = result.Candidates.Count == 0
            ? $"「{options.Keyword}」の文字幅比率に外れた候補は見つかりませんでした。"
            : $"「{options.Keyword}」の文字幅比率が外れた候補が{result.Candidates.Count}件見つかりました。";
        return result;
    }

    /// <summary>OCR品質分析の候補を表示し、該当する文字範囲を選択します。</summary>
    public Task<bool> NavigateToOcrQualityCandidateAsync(
        int pageNumber,
        Guid regionId,
        int startIndex = 0,
        int length = 1)
    {
        var text = _pageOverlays.GetValueOrDefault(pageNumber)?
            .FirstOrDefault(region => region.Id == regionId)?.Text ?? string.Empty;
        var safeStart = Math.Clamp(startIndex, 0, Math.Max(0, text.Length - 1));
        var safeLength = Math.Clamp(length, 1, Math.Max(1, text.Length - safeStart));
        return NavigateToOcrSearchMatchAsync(new OcrTextSearchMatch(
            pageNumber,
            regionId,
            safeStart,
            safeLength,
            text,
            CreateSearchPreview(text, safeStart, safeLength)));
    }

    /// <summary>
    /// キーワード幅の候補を、正常例から求めた基準幅へ補正します。
    /// </summary>
    /// <param name="candidates">分析画面で確認済みの補正候補。</param>
    /// <returns>実際に補正した出現箇所数。</returns>
    /// <remarks>行または文字が固定された領域は常に除外します。</remarks>
    public int ApplyKeywordWidthCorrections(IReadOnlyList<OcrKeywordWidthCandidate> candidates)
    {
        if (!CanEditGeometry || candidates.Count == 0) return 0;
        var targets = candidates
            .Where(candidate => !candidate.IsLocked)
            .Select(candidate => new
            {
                Candidate = candidate,
                Region = _pageOverlays.GetValueOrDefault(candidate.PageNumber)?
                    .FirstOrDefault(region => region.Id == candidate.RegionId && !region.IsDeleted),
            })
            .Where(target => target.Region is not null &&
                             !target.Region.IsGeometryLocked &&
                             !target.Region.HasLockedCharacters)
            .ToArray();
        if (targets.Length == 0) return 0;

        var affected = targets.Select(target => target.Region!).Distinct().ToArray();
        var changedCount = 0;
        ApplyRegionEdit("キーワードの文字幅比率を補正", affected, () =>
        {
            foreach (var target in targets
                         .OrderBy(target => target.Candidate.PageNumber)
                         .ThenBy(target => target.Candidate.RegionId)
                         .ThenByDescending(target => target.Candidate.StartIndex))
            {
                if (!target.Region!.TrySetCharacterRangeExtent(
                        target.Candidate.StartIndex,
                        target.Candidate.Length,
                        target.Candidate.ReferenceSpan))
                    continue;
                target.Region.ReviewStatus = ReviewStatus.Modified;
                changedCount++;
            }
        });
        StatusMessage = changedCount == 0
            ? "補正対象はありませんでした。固定済み領域は変更されません。"
            : $"キーワードの文字幅比率を{changedCount}件補正しました。";
        return changedCount;
    }

    /// <summary>文書全体のOCR領域を、品質分析用の軽量なスナップショットへ変換します。</summary>
    private async Task<IReadOnlyList<OcrQualitySample>> CollectOcrQualitySamplesAsync(
        IProgress<string>? progress,
        IProgress<OperationProgressUpdate>? detailedProgress,
        CancellationToken cancellationToken)
    {
        if (_resolvedPdfPath is null || PageItems.Count == 0) return [];
        var samples = new List<OcrQualitySample>();
        for (var pageIndex = 0; pageIndex < PageItems.Count; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageNumber = pageIndex + 1;
            var message = $"{pageNumber}ページを分析用に読み込んでいます（{pageNumber}/{PageItems.Count}）...";
            progress?.Report(message);
            detailedProgress?.Report(new OperationProgressUpdate(pageNumber, PageItems.Count, message));
            var regions = await EnsurePageOverlaysLoadedForSearchAsync(pageNumber);
            samples.AddRange(regions
                .Where(region => !region.IsDeleted && !string.IsNullOrWhiteSpace(region.Text))
                .Select(region => new OcrQualitySample(
                    pageNumber,
                    region.Id,
                    region.Text,
                    region.Width,
                    region.Height,
                    region.IsVertical,
                    region.IsGeometryLocked,
                    region.HasLockedCharacters,
                    region.CharacterAdvances)));
        }
        detailedProgress?.Report(new OperationProgressUpdate(PageItems.Count, PageItems.Count, "分析用データの読み込みを完了しました。"));
        return samples;
    }

    /// <summary>検索結果1件を置換し、1回のUndoで戻せる編集として記録します。</summary>
    public bool ReplaceOcrSearchMatch(
        OcrTextSearchMatch match,
        OcrTextSearchOptions options,
        string replacementText)
    {
        if (!_pageOverlays.TryGetValue(match.PageNumber, out var regions)) return false;
        var region = regions.FirstOrDefault(candidate => candidate.Id == match.RegionId && !candidate.IsDeleted);
        if (region is null || string.IsNullOrEmpty(options.SearchText)) return false;
        var regularExpression = options.UseRegularExpression ? CreateSearchRegex(options) : null;
        var occurrence = FindSearchOccurrences(region.Text, options, regularExpression)
            .FirstOrDefault(candidate => candidate.StartIndex == match.StartIndex && candidate.Length == match.Length);
        if (occurrence is null) return false;
        var operation = CreateReplacementOperation(occurrence, replacementText ?? string.Empty);

        ApplyRegionEdit("透明テキストを1件置換", [region], () =>
        {
            region.Text = region.Text.Remove(operation.StartIndex, operation.Length)
                .Insert(operation.StartIndex, operation.ReplacementText);
            region.ReviewStatus = ReviewStatus.Modified;
        });
        return true;
    }

    /// <summary>現在の検索結果をすべて置換し、一括操作全体を1回のUndoで戻せるよう記録します。</summary>
    /// <returns>実際に置換した一致箇所数。</returns>
    public int ReplaceAllOcrSearchMatches(
        IReadOnlyList<OcrTextSearchMatch> matches,
        OcrTextSearchOptions options,
        string replacementText)
    {
        if (matches.Count == 0 || string.IsNullOrEmpty(options.SearchText)) return 0;
        var regularExpression = options.UseRegularExpression ? CreateSearchRegex(options) : null;
        var operationsByRegion = new Dictionary<OverlayRegionViewModel, IReadOnlyList<OcrReplacementOperation>>();
        foreach (var group in matches.GroupBy(match => (match.PageNumber, match.RegionId)))
        {
            if (!_pageOverlays.TryGetValue(group.Key.PageNumber, out var regions)) continue;
            var region = regions.FirstOrDefault(candidate => candidate.Id == group.Key.RegionId && !candidate.IsDeleted);
            if (region is null) continue;
            var requestedRanges = group.Select(match => (match.StartIndex, match.Length)).ToHashSet();
            var operations = FindSearchOccurrences(region.Text, options, regularExpression)
                .Where(occurrence => requestedRanges.Contains((occurrence.StartIndex, occurrence.Length)))
                .Select(occurrence => CreateReplacementOperation(occurrence, replacementText ?? string.Empty))
                .OrderByDescending(operation => operation.StartIndex)
                .ToArray();
            if (operations.Length > 0) operationsByRegion[region] = operations;
        }

        var affected = operationsByRegion.Keys.ToArray();
        var replacementCount = operationsByRegion.Values.Sum(operations => operations.Count);
        if (replacementCount == 0) return 0;

        ApplyRegionEdit($"透明テキストを一括置換（{replacementCount}件）", affected, () =>
        {
            foreach (var (region, operations) in operationsByRegion)
            {
                var updatedText = region.Text;
                foreach (var operation in operations)
                    updatedText = updatedText.Remove(operation.StartIndex, operation.Length)
                        .Insert(operation.StartIndex, operation.ReplacementText);
                region.Text = updatedText;
                region.ReviewStatus = ReviewStatus.NeedsReview;
            }
        });
        return replacementCount;
    }

    internal async Task SaveProjectForDiagnosticsAsync(string projectPath)
    {
        if (_project is null) throw new InvalidOperationException("No project is loaded.");
        SynchronizeProjectPages();
        await _packages.SaveAsync(projectPath, _project, _hasPageStructureEdits || _project.SourcePdf.IsEmbedded, _thumbnailCache);
        _projectFilePath = Path.GetFullPath(projectPath);
        ProjectPath = _projectFilePath;
        MarkSavedState();
        ProjectPackageService.DeleteAutoSave(_projectFilePath);
        _lastAutoSaveAtUtc = DateTimeOffset.UtcNow;
    }

    internal Task SaveCurrentProjectForDiagnosticsAsync() =>
        _projectFilePath is null
            ? throw new InvalidOperationException("No project save path is available.")
            : SaveProjectToPathAsync(_projectFilePath);

    internal async Task<PdfExportResult> ExportPdfForDiagnosticsAsync(string outputPath)
    {
        if (_project is null || _resolvedPdfPath is null) throw new InvalidOperationException("No PDF project is loaded.");
        SynchronizeProjectPages();
        return await _exportService.ExportAsync(_resolvedPdfPath, outputPath, _project);
    }

    private async Task OpenProjectAsync()
    {
        var dialog = new OpenFileDialog { Filter = "PDF Correctorium プロジェクト (*.pdfocrproj)|*.pdfocrproj", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        await OpenDocumentPathAsync(dialog.FileName);
    }

    private async Task SaveProjectAsync()
    {
        if (_project is null) return;
        if (_projectFilePath is null)
        {
            await SaveProjectAsAsync();
            return;
        }
        try { await SaveProjectToPathAsync(_projectFilePath); }
        catch (Exception ex) { await ShowErrorAsync("プロジェクトを上書き保存できませんでした。既存ファイルは変更していません。", ex); }
    }

    private async Task SaveProjectAsAsync()
    {
        if (_project is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "PDF Correctorium プロジェクト (*.pdfocrproj)|*.pdfocrproj",
            DefaultExt = ProjectPackageService.ProjectExtension,
            AddExtension = true,
            FileName = _project.Name + ProjectPackageService.ProjectExtension,
        };
        if (dialog.ShowDialog() != true) return;
        BeginBackgroundOperation("しおりを読み込んでいます...");
        try
        {
            await SaveProjectToPathAsync(Path.GetFullPath(dialog.FileName));
        }
        catch (Exception ex) { await ShowErrorAsync("プロジェクトを保存できませんでした。既存ファイルは変更していません。", ex); }
    }

    /// <summary>
    /// 未保存の編集がある場合にプロジェクトを保存し、安全に終了できるかを判定します。
    /// </summary>
    /// <returns>終了処理を続行してよい場合は<see langword="true"/>。保存が取り消された場合は<see langword="false"/>。</returns>
    public async Task<bool> SaveBeforeCloseAsync()
    {
        if (!HasUnsavedChanges) return true;
        if (_project is null) return true;
        var path = _projectFilePath;
        if (path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Correctorium プロジェクト (*.pdfocrproj)|*.pdfocrproj",
                DefaultExt = ProjectPackageService.ProjectExtension,
                AddExtension = true,
                FileName = _project.Name + ProjectPackageService.ProjectExtension,
            };
            if (dialog.ShowDialog() != true) return false;
            path = Path.GetFullPath(dialog.FileName);
        }
        try
        {
            await SaveProjectToPathAsync(path);
            return !HasUnsavedChanges;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("終了前にプロジェクトを保存できませんでした。アプリケーションは終了していません。", ex);
            return false;
        }
    }

    private async Task SaveProjectToPathAsync(string path)
    {
        if (_project is null) return;
        BeginBackgroundOperation("プロジェクトを保存・検証しています...");
        try
        {
            StatusMessage = "プロジェクトを保存・検証しています...";
            SynchronizeProjectPages();
            // ページ追加・削除・並べ替え・回転後の作業用PDFは、一時作業領域の消去後も
            // プロジェクトを開けるよう、.pdfocrproj 内へ自動的に内包します。
            await _packages.SaveAsync(path, _project, _hasPageStructureEdits || _project.SourcePdf.IsEmbedded, _thumbnailCache);
            _projectFilePath = Path.GetFullPath(path);
            ProjectPath = _projectFilePath;
            MarkSavedState();
            ProjectPackageService.DeleteAutoSave(_projectFilePath);
            _lastAutoSaveAtUtc = DateTimeOffset.UtcNow;
            StatusMessage = "プロジェクトを上書き保存し、検証が完了しました。";
            await _log.WriteAsync(LogLevel.Information, "project.save", $"Saved project {_project.ProjectId} to {_projectFilePath}");
        }
        finally { EndBackgroundOperation(); }
    }

    private async Task OptimizeCurrentPageImageAsync()
    {
        if (_project is null || _resolvedPdfPath is null || SelectedPage is null) return;
        try
        {
            BeginBackgroundOperation("現在ページの画像を解析しています...");
            SynchronizeProjectPages();
            var pageNumber = SelectedPage.PageNumber;
            var page = _project.Pages.FirstOrDefault(item => item.PageNumber == pageNumber);
            if (page is null)
                throw new InvalidOperationException("現在のページ情報をプロジェクトへ保存できませんでした。");

            if (page.ImageOptimization is { Enabled: true })
            {
                ReplaceProjectPage(page with { ImageOptimization = null });
                MarkNonUndoableChange();
                NotifyPageImageOptimizationState();
                StatusMessage = $"{pageNumber}ページの画像最適化を取り消しました。";
                return;
            }

            StatusMessage = $"{pageNumber}ページの画像を解析しています...";
            var options = new PageImageOptimization();
            var analysis = await _exportService.AnalyzePageImageOptimizationAsync(
                _resolvedPdfPath,
                pageNumber,
                options);
            if (!analysis.CanOptimize)
            {
                StatusMessage = analysis.Message;
                MessageBox.Show(
                    analysis.Message + "\n\n四辺または中央の空白帯を削減しても画像データが小さくならないため、今回は適用できません。",
                    "ページ画像の最適化",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            bool confirmed;
            if (PreviewImage is System.Windows.Media.Imaging.BitmapSource preview)
            {
                var previewWindow = new ImageOptimizationPreviewWindow(
                    preview,
                    analysis,
                    options.MinimumAreaReduction)
                {
                    Owner = Application.Current?.MainWindow,
                };
                confirmed = previewWindow.ShowDialog() == true;
                if (confirmed && analysis.RemovableBlankImages > 0 && previewWindow.KeepRegions.Count > 0)
                {
                    confirmed = false;
                    StatusMessage = "空白全面画像を元画像のまま保持しました。最適化は登録していません。";
                }
                else if (confirmed)
                    options = options with { KeepRegions = previewWindow.KeepRegions };
            }
            else
            {
                confirmed = MessageBox.Show(
                    analysis.Message + "\n\n実際の変更はPDF出力時に適用されます。続行しますか？",
                    "ページ画像の最適化",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;
            }
            if (!confirmed)
            {
                StatusMessage = "ページ画像の最適化をキャンセルしました。";
                return;
            }

            ReplaceProjectPage(page with { ImageOptimization = options });
            MarkNonUndoableChange();
            NotifyPageImageOptimizationState();
            StatusMessage = $"{pageNumber}ページの余白・単色背景削減をPDF出力対象に追加しました。";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("ページ画像を安全に最適化できませんでした。元PDFは変更していません。", ex);
        }
        finally { EndBackgroundOperation(); }
    }

    private void ReplaceProjectPage(OcrPage replacement)
    {
        if (_project is null) return;
        _project = _project with
        {
            Pages = _project.Pages
                .Select(page => page.PageNumber == replacement.PageNumber ? replacement : page)
                .OrderBy(page => page.PageNumber)
                .ToArray(),
        };
    }

    private void NotifyPageImageOptimizationState()
    {
        OnPropertyChanged(nameof(IsCurrentPageImageOptimizationEnabled));
        OnPropertyChanged(nameof(CurrentPageImageOptimizationActionText));
        OptimizeCurrentPageImageCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// PDF全ページを解析して最適化候補を一覧表示し、選択ページを一括または順次確認で登録します。
    /// </summary>
    private async Task OptimizeDocumentImagesAsync()
    {
        if (_project is null || _resolvedPdfPath is null) return;
        try
        {
            BeginBackgroundOperation("PDF全体の画像を解析しています...", isIndeterminate: false);
            var operationId = _backgroundOperationId;
            SynchronizeProjectPages();
            var options = new PageImageOptimization();
            var progress = new Progress<(int Current, int Total)>(value =>
            {
                if (!IsCurrentBackgroundOperation(operationId)) return;
                var message = $"PDF全体の画像を解析しています... {value.Current:N0}/{value.Total:N0}ページ";
                StatusMessage = message;
                UpdateBackgroundOperation(message, value.Current, value.Total);
            });
            var analysis = await _exportService.AnalyzeDocumentImageOptimizationAsync(
                _resolvedPdfPath,
                options,
                progress);
            if (analysis.Candidates.Count == 0)
            {
                StatusMessage = "PDF全体を解析しましたが、画像を小さくできる候補は見つかりませんでした。";
                MessageBox.Show(StatusMessage, "PDF全体の画像最適化", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var listWindow = new DocumentImageOptimizationWindow(analysis)
            {
                Owner = Application.Current?.MainWindow,
            };
            if (listWindow.ShowDialog() != true)
            {
                StatusMessage = "PDF全体の画像最適化をキャンセルしました。";
                return;
            }

            var selected = listWindow.SelectedAnalyses;
            var pages = _project.Pages.ToDictionary(page => page.PageNumber);
            var appliedAnalyses = new List<PdfImageOptimizationAnalysis>();
            var applied = 0;
            var skipped = 0;
            foreach (var candidate in selected)
            {
                var pageOptions = options;
                if (listWindow.ApplyMode == DocumentImageOptimizationApplyMode.Review)
                {
                    StatusMessage = $"{candidate.PageNumber}ページの最適化内容を準備しています...";
                    var rendered = await _previewService.RenderPageAsync(_resolvedPdfPath, candidate.PageNumber);
                    var previewWindow = new ImageOptimizationPreviewWindow(
                        rendered.Image,
                        candidate,
                        options.MinimumAreaReduction)
                    {
                        Owner = Application.Current?.MainWindow,
                    };
                    if (previewWindow.ShowDialog() != true)
                    {
                        skipped++;
                        continue;
                    }

                    // 空白全面画像の領域を「保持」にした場合は、そのページの削除設定を登録しません。
                    if (candidate.RemovableBlankImages > 0 && previewWindow.KeepRegions.Count > 0)
                    {
                        skipped++;
                        continue;
                    }
                    pageOptions = options with { KeepRegions = previewWindow.KeepRegions };
                }

                var page = pages.GetValueOrDefault(candidate.PageNumber) ?? new OcrPage
                {
                    PageNumber = candidate.PageNumber,
                };
                pages[candidate.PageNumber] = page with { ImageOptimization = pageOptions };
                appliedAnalyses.Add(candidate);
                applied++;
            }

            if (applied == 0)
            {
                StatusMessage = "画像最適化を適用するページはありませんでした。";
                return;
            }

            _project = _project with { Pages = pages.Values.OrderBy(page => page.PageNumber).ToArray() };
            MarkNonUndoableChange();
            NotifyPageImageOptimizationState();
            var imageSavings = appliedAnalyses.Sum(item => Math.Max(0L, item.OriginalEncodedBytes - item.EstimatedEncodedBytes));
            var estimatedPdfBytes = Math.Max(0L, analysis.SourcePdfBytes - imageSavings);
            StatusMessage = $"画像最適化を{applied:N0}ページへ登録しました。PDF出力時に適用されます。";
            MessageBox.Show(
                $"画像最適化を{applied:N0}ページへ登録しました。" +
                (skipped > 0 ? $"\n確認で見送ったページ: {skipped:N0}" : string.Empty) +
                $"\n\n現在のPDF: {FormatFileSize(analysis.SourcePdfBytes)}" +
                $"\n出力PDF概算: {FormatFileSize(estimatedPdfBytes)}" +
                $"\n画像データ削減見込み: {FormatFileSize(imageSavings)}" +
                "\n\n実際のファイルサイズはPDF出力後の再構築・圧縮により多少変動します。",
                "PDF全体の画像最適化",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("PDF全体の画像を解析・最適化できませんでした。元PDFは変更していません。", ex);
        }
        finally { EndBackgroundOperation(); }
    }

    private async Task ExportPdfAsync()
    {
        if (_project is null || _resolvedPdfPath is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "PDFファイル (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName = _project.Name + "_edited.pdf",
        };
        while (true)
        {
            if (dialog.ShowDialog() != true) return;
            try
            {
                IsolatedPdfExportService.ValidateDestination(dialog.FileName);
                break;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                var retry = MessageBox.Show(
                    $"{exception.Message}\n\n長時間のPDF生成処理はまだ開始していません。" +
                    "\n出力先PDFをAcrobat等で閉じてから再試行するか、別のファイル名を指定してください。",
                    "PDF Correctorium",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (retry != MessageBoxResult.OK) return;
            }
        }

        var sourceBytes = new FileInfo(_resolvedPdfPath).Length;
        var pageCount = Math.Max(_project.SourcePdf.PageCount ?? 0, _project.Pages.Count);
        if (pageCount >= 100 || sourceBytes >= 50L * 1024L * 1024L)
        {
            var proceed = MessageBox.Show(
                $"{pageCount:N0}ページ、{FormatFileSize(sourceBytes)}のPDFを生成・検証します。" +
                "\n処理には数分以上かかる場合があります。" +
                "\n\n処理中はPDF Correctoriumの編集操作と、プレビュー・サムネイルのバックグラウンド処理を一時停止します。" +
                "\nほかのアプリで出力先PDFを開かずに、そのままお待ちください。" +
                "\n\nPDF出力を開始しますか？",
                "PDF Correctorium",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (proceed != MessageBoxResult.Yes) return;
        }

        var restartThumbnailLoading = ShowPageThumbnails;
        _renderCancellation?.Cancel();
        CancelThumbnailLoading(clearImages: false);
        IsPdfExporting = true;
        try
        {
            BeginBackgroundOperation("PDF出力を準備しています...", isIndeterminate: false);
            StatusMessage = "PDF出力を準備しています...";
            SynchronizeProjectPages();
            var progress = new Progress<PdfExportProgress>(value =>
            {
                UpdateBackgroundOperation(value.Message, value.Current, value.Total);
                StatusMessage = value.Message;
            });
            var outcome = await _isolatedExportService.ExportAsync(
                _resolvedPdfPath,
                dialog.FileName,
                _project,
                progress,
                CancellationToken.None);
            var result = outcome.Result;
            var outputBytes = new FileInfo(outcome.OutputPath).Length;
            var sizeChange = sourceBytes <= 0 ? 0d : 1d - outputBytes / (double)sourceBytes;
            StatusMessage = $"PDFを出力しました（{FormatFileSize(outputBytes)}、{result.ModifiedPages}ページ、{result.ModifiedRegions}領域、画像最適化{result.OptimizedImages}件）。";
            await _log.WriteAsync(LogLevel.Information, "pdf.export", $"Exported {outcome.OutputPath}; pages={result.ModifiedPages}; regions={result.ModifiedRegions}");
            IsPdfExporting = false;
            MessageBox.Show(
                (outcome.Warning is null ? "PDFの出力と再検証が完了しました。" : outcome.Warning) +
                $"\n\n出力先: {outcome.OutputPath}" +
                $"\n出力PDFサイズ: {FormatFileSize(outputBytes)}" +
                $"\n元PDFサイズ: {FormatFileSize(sourceBytes)}" +
                $"\nサイズ変化: {sizeChange:P1}" +
                $"\n変更ページ: {result.ModifiedPages}\n変更領域: {result.ModifiedRegions}\n最適化画像: {result.OptimizedImages}",
                "PDF Correctorium",
                MessageBoxButton.OK,
                outcome.Warning is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            IsPdfExporting = false;
            await ShowErrorAsync("PDFを安全に出力できませんでした。元PDFと既存の出力ファイルは変更していません。", ex);
        }
        finally
        {
            IsPdfExporting = false;
            EndBackgroundOperation();
            if (restartThumbnailLoading) StartThumbnailLoading();
        }
    }

    /// <summary>ファイル容量をB、KB、MB、GBの適切な単位で表示します。</summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d * 1024d):N2} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024d * 1024d):N1} MB";
        if (bytes >= 1024L) return $"{bytes / 1024d:N1} KB";
        return $"{bytes:N0} B";
    }

    private async Task ImportOcrDataAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "NDLOCR-Liteデータ (*.json;*.xml;*.txt)|*.json;*.xml;*.txt|JSON (*.json)|*.json|XML / TEI (*.xml)|*.xml|テキスト (*.txt)|*.txt",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            BeginBackgroundOperation("OCR付随データを読み込んでいます...");
            StatusMessage = "OCR付随データを読み込んでいます...";
            _ndlOcrDocument = await _ndlOcrCompanionService.ImportAsync(dialog.FileName);
            if (_project is not null) _project = _project with { Pages = [] };
            ClearOverlaySession();
            OcrDataSourceText = $"{_ndlOcrDocument.SourceKind}（付随ファイル: {_ndlOcrDocument.CompanionFiles.Count}件）";
            if (SelectedPage is not null) await RenderPageAsync(SelectedPage.PageNumber, populatePageList: false);
            StatusMessage = _ndlOcrDocument.Pages.Count > 0
                ? "OCR座標データを読み込みました。"
                : "TXT/TEIを関連付けましたが、表示用座標はありません。";
            MarkNonUndoableChange();
            await _log.WriteAsync(LogLevel.Information, "ocr.companion.import", $"Imported {Path.GetFileName(dialog.FileName)} as {_ndlOcrDocument.SourceKind}");
        }
        catch (Exception ex) { await ShowErrorAsync("OCR付随データを読み込めませんでした。", ex); }
        finally { EndBackgroundOperation(); }
    }

    private async Task ApplyProjectAsync(string sourcePath, PdfCorrectoriumProject project,
        string? projectPath, IReadOnlyDictionary<int, byte[]> thumbnails)
    {
        // Prepare and validate the first page before replacing any live document state.
        // A bad PDF, missing source or invalid overlay must leave edits and Undo intact.
        var resolvedPath = Path.GetFullPath(sourcePath);
        var preview = await _previewService.RenderPageAsync(resolvedPath, 1);
        var companion = await _ndlOcrCompanionService.TryImportAsync(resolvedPath);
        var metrics = new PageMetrics(preview.Image.PixelWidth, preview.Image.PixelHeight,
            preview.PageWidthPoints, preview.PageHeightPoints);
        var companionRegions = companion?.GetScaledRegions(1, metrics.PixelWidth, metrics.PixelHeight) ?? [];
        var overlays = CreatePageOverlayModels(1,
            companionRegions.Count > 0 ? companionRegions : preview.TextRegions, metrics, project);
        if (!project.BookmarksInitialized)
        {
            try { project = project with { Bookmarks = await _bookmarkService.ReadFromPdfAsync(resolvedPath), BookmarksInitialized = true }; }
            catch (Exception ex)
            {
                await _log.WriteAsync(LogLevel.Warning, "bookmarks.read.failed", ex.Message, ex);
                project = project with { BookmarksInitialized = true };
            }
        }

        HasDocument = false;
        CancelThumbnailLoading(clearImages: true);
        _renderCancellation?.Cancel();
        _project = project.SourcePdf.IsEmbedded
            ? project with { SourcePdf = project.SourcePdf with { AbsolutePathHint = resolvedPath } }
            : project;
        _hasPageStructureEdits = false;
        _projectFilePath = projectPath;
        ProjectPath = projectPath ?? "未保存";
        ReplaceThumbnailCache(thumbnails);
        _lastAutoSaveAtUtc = DateTimeOffset.UtcNow;
        _resolvedPdfPath = resolvedPath;
        DocumentTitle = _project.Name;
        DocumentDescription = "PDFプレビューを読み込みました。左側のページ一覧からページを切り替えられます。";
        SourcePdfPath = _resolvedPdfPath;
        SourceHash = _project.SourcePdf.Sha256;
        PreviewImage = null;
        ClearOverlaySession();
        OverlaySummary = "文字領域: 0件";
        OcrDataSourceText = "OCR付随ファイルを検索しています...";
        _ndlOcrDocument = companion;
        OcrDataSourceText = _ndlOcrDocument is null
            ? "PDFテキストレイヤー"
            : $"{_ndlOcrDocument.SourceKind}（付随ファイル: {_ndlOcrDocument.CompanionFiles.Count}件）";
        LoadBookmarkItems(_project.Bookmarks);
        PageItems.Clear();
        _selectedPage = null;
        OnPropertyChanged(nameof(SelectedPage));
        _pageMetrics[1] = metrics;
        _pageOverlays[1] = overlays;
        foreach (var overlay in overlays) AttachOverlay(overlay);
        await RenderPageAsync(1, populatePageList: true, preparedPreview: preview);
        HasDocument = HasPreview && PageItems.Count > 0;
        ResetEditState();
        SaveProjectCommand.RaiseCanExecuteChanged();
        ImportOcrDataCommand.RaiseCanExecuteChanged();
        ExportPdfCommand.RaiseCanExecuteChanged();
        OptimizeDocumentImagesCommand.RaiseCanExecuteChanged();
        AddBookmarkCommand.RaiseCanExecuteChanged();
        ImportBookmarksCommand.RaiseCanExecuteChanged();
        ExportBookmarksCommand.RaiseCanExecuteChanged();
        RaisePageManagementCommands();
    }

    private void LoadBookmarkItems(IReadOnlyList<PdfBookmark> bookmarks)
    {
        _loadingBookmarks = true;
        try
        {
            SelectedBookmark = null;
            BookmarkItems.Clear();
            foreach (var bookmark in bookmarks)
                BookmarkItems.Add(new BookmarkNodeViewModel(bookmark, BookmarkChanged));
        }
        finally { _loadingBookmarks = false; }
        ExportBookmarksCommand.RaiseCanExecuteChanged();
    }

    private void BookmarkChanged()
    {
        if (_loadingBookmarks) return;
        SynchronizeBookmarks(markModified: true);
        MarkNonUndoableChange();
        StatusMessage = "しおりを変更しました。";
    }

    private void SynchronizeBookmarks(bool markModified = false)
    {
        if (_project is null) return;
        _project = _project with
        {
            Bookmarks = BookmarkItems.Select(item => item.ToModel()).ToArray(),
            BookmarksInitialized = true,
            BookmarksModified = _project.BookmarksModified || markModified,
        };
    }

    private void AddBookmark()
    {
        if (_project is null || SelectedPage is null) return;
        var bookmark = new BookmarkNodeViewModel(new PdfBookmark
        {
            Title = CreateBookmarkTitle(),
            PageNumber = SelectedPage.PageNumber,
        }, BookmarkChanged);
        BookmarkItems.Add(bookmark);
        SelectedBookmark = bookmark;
        BookmarkChanged();
        ExportBookmarksCommand.RaiseCanExecuteChanged();
    }

    private void AddChildBookmark()
    {
        if (SelectedBookmark is null || SelectedPage is null) return;
        var bookmark = new BookmarkNodeViewModel(new PdfBookmark
        {
            Title = CreateBookmarkTitle(),
            PageNumber = SelectedPage.PageNumber,
        }, BookmarkChanged);
        SelectedBookmark.Children.Add(bookmark);
        SelectedBookmark.IsExpanded = true;
        SelectedBookmark = bookmark;
        BookmarkChanged();
    }

    /// <summary>
    /// 新規しおりの初期タイトルを、選択中のOCR行または現在ページ番号から作成します。
    /// </summary>
    /// <remarks>
    /// OCR行に改行やタブが含まれる場合は、しおり一覧で1行に表示できるよう半角空白へ置き換えます。
    /// 文字編集モードでも、選択文字だけではなく、その文字が属する行全体をしおり名として使用します。
    /// </remarks>
    /// <returns>しおりへ設定する空でないタイトル。</returns>
    private string CreateBookmarkTitle()
    {
        if (SelectedOverlay is { IsDeleted: false } selectedRegion)
        {
            var selectedLineText = Regex.Replace(selectedRegion.Text ?? string.Empty, @"[\r\n\t]+", " ");
            selectedLineText = Regex.Replace(selectedLineText, " {2,}", " ").Trim();
            if (selectedLineText.Length > 0)
            {
                return selectedLineText;
            }
        }

        var pageNumber = SelectedPage?.PageNumber ?? 1;
        return LocalizationService.IsEnglish ? $"Page {pageNumber}" : $"ページ {pageNumber}";
    }

    private void DeleteBookmark()
    {
        if (SelectedBookmark is null) return;
        var target = SelectedBookmark;
        var collection = FindContainingCollection(target);
        if (collection is null) return;
        collection.Remove(target);
        SelectedBookmark = null;
        BookmarkChanged();
        ExportBookmarksCommand.RaiseCanExecuteChanged();
    }

    private bool CanMoveBookmark(int offset)
    {
        if (SelectedBookmark is null) return false;
        var collection = FindContainingCollection(SelectedBookmark);
        if (collection is null) return false;
        var index = collection.IndexOf(SelectedBookmark);
        var destination = index + offset;
        return index >= 0 && destination >= 0 && destination < collection.Count;
    }

    private void MoveBookmark(int offset)
    {
        if (SelectedBookmark is null) return;
        var selected = SelectedBookmark;
        var collection = FindContainingCollection(selected);
        if (collection is null) return;
        var index = collection.IndexOf(selected);
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= collection.Count) return;
        collection.Move(index, destination);
        BookmarkChanged();
        SelectedBookmark = selected;
        MoveBookmarkUpCommand.RaiseCanExecuteChanged();
        MoveBookmarkDownCommand.RaiseCanExecuteChanged();
    }

    private ObservableCollection<BookmarkNodeViewModel>? FindContainingCollection(BookmarkNodeViewModel target)
    {
        if (BookmarkItems.Contains(target)) return BookmarkItems;
        ObservableCollection<BookmarkNodeViewModel>? Search(IEnumerable<BookmarkNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Contains(target)) return node.Children;
                var nested = Search(node.Children);
                if (nested is not null) return nested;
            }
            return null;
        }
        return Search(BookmarkItems);
    }

    /// <summary>
    /// ドラッグしたしおりを対象の前、子階層、または後へ移動します。
    /// 自分自身や自分の子孫へ移動して循環構造になる操作は拒否します。
    /// </summary>
    public bool MoveBookmarkByDrop(
        BookmarkNodeViewModel source,
        BookmarkNodeViewModel target,
        BookmarkDropPosition position)
    {
        if (ReferenceEquals(source, target) || ContainsBookmark(source.Children, target)) return false;
        var sourceCollection = FindContainingCollection(source);
        var destinationCollection = position == BookmarkDropPosition.AsChild
            ? target.Children
            : FindContainingCollection(target);
        if (sourceCollection is null || destinationCollection is null) return false;

        var sourceIndex = sourceCollection.IndexOf(source);
        var targetIndex = destinationCollection.IndexOf(target);
        if (sourceIndex < 0 || (position != BookmarkDropPosition.AsChild && targetIndex < 0)) return false;

        sourceCollection.RemoveAt(sourceIndex);
        if (position == BookmarkDropPosition.AsChild)
        {
            destinationCollection.Add(source);
            target.IsExpanded = true;
        }
        else
        {
            if (ReferenceEquals(sourceCollection, destinationCollection) && sourceIndex < targetIndex)
                targetIndex--;
            var insertionIndex = targetIndex + (position == BookmarkDropPosition.After ? 1 : 0);
            destinationCollection.Insert(Math.Clamp(insertionIndex, 0, destinationCollection.Count), source);
        }

        SelectedBookmark = source;
        BookmarkChanged();
        MoveBookmarkUpCommand.RaiseCanExecuteChanged();
        MoveBookmarkDownCommand.RaiseCanExecuteChanged();
        return true;
    }

    /// <summary>指定ノードが子孫階層に含まれるかを再帰的に調べます。</summary>
    private static bool ContainsBookmark(IEnumerable<BookmarkNodeViewModel> nodes, BookmarkNodeViewModel target) =>
        nodes.Any(node => ReferenceEquals(node, target) || ContainsBookmark(node.Children, target));

    private void GoToBookmark()
    {
        if (SelectedBookmark is null || PageItems.Count == 0) return;
        var pageNumber = Math.Clamp(SelectedBookmark.PageNumber, 1, PageItems.Count);
        SelectedPage = PageItems[pageNumber - 1];
    }

    /// <summary>1始まりのページ番号を検証し、指定ページへ移動します。</summary>
    public bool GoToPageNumber(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > PageItems.Count) return false;
        SelectedPage = PageItems[pageNumber - 1];
        return true;
    }

    private async Task ImportBookmarksAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "すべての対応形式|*.pdfbookmarks.json;*.json;*.txt;*.xml|PDF Correctorium JSON (*.pdfbookmarks.json)|*.pdfbookmarks.json|pdf_as テキスト (*.txt)|*.txt|pdf_as/XML (*.xml)|*.xml|JSON (*.json)|*.json",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var bookmarks = await _bookmarkService.ImportAsync(dialog.FileName);
            LoadBookmarkItems(bookmarks);
            SynchronizeBookmarks(markModified: true);
            MarkNonUndoableChange();
            StatusMessage = $"{CountBookmarks(bookmarks)}件のしおりを読み込みました。";
        }
        catch (Exception ex) { await ShowErrorAsync("しおりを読み込めませんでした。", ex); }
        finally { EndBackgroundOperation(); }
    }

    private async Task ExportBookmarksAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDF Correctorium JSON (*.pdfbookmarks.json)|*.pdfbookmarks.json|pdf_as テキスト (*.txt)|*.txt|pdf_as/XML (*.xml)|*.xml|JSON (*.json)|*.json",
            DefaultExt = ".pdfbookmarks.json",
            AddExtension = true,
            FileName = _project?.Name ?? "bookmarks",
        };
        if (dialog.ShowDialog() != true) return;
        BeginBackgroundOperation("しおりを書き出しています...");
        try
        {
            SynchronizeBookmarks();
            await _bookmarkService.ExportAsync(dialog.FileName, _project?.Bookmarks ?? []);
            StatusMessage = "しおりを書き出しました。";
        }
        catch (Exception ex) { await ShowErrorAsync("しおりを書き出せませんでした。", ex); }
        finally { EndBackgroundOperation(); }
    }

    private static int CountBookmarks(IEnumerable<PdfBookmark> bookmarks) =>
        bookmarks.Sum(bookmark => 1 + CountBookmarks(bookmark.Children));

    private async Task RenderSelectedPageAsync()
    {
        if (SelectedPage is null) return;
        await RenderPageAsync(SelectedPage.PageNumber, populatePageList: false);
    }

    private async Task RenderPageAsync(int pageNumber, bool populatePageList, PdfPreviewResult? preparedPreview = null)
    {
        if (_resolvedPdfPath is null) return;
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = new CancellationTokenSource();
        var token = _renderCancellation.Token;
        try
        {
            IsPreviewLoading = true;
            StatusMessage = $"{pageNumber}ページを描画しています...";
            var result = preparedPreview ?? await _previewService.RenderPageAsync(_resolvedPdfPath, pageNumber, cancellationToken: token);
            if (token.IsCancellationRequested) return;

            if (populatePageList)
            {
                PageItems.Clear();
                for (var number = 1; number <= result.PageCount; number++) PageItems.Add(new PdfPageItem(number));
                SetCurrentPageWithoutRendering(PageItems[result.PageNumber - 1]);
                _selectedPageNumbers.Clear();
                _selectedPageNumbers.Add(result.PageNumber);
                OnPropertyChanged(nameof(SelectedPageCount));
                RaisePageManagementCommands();
                if (_project is not null)
                    _project = _project with { SourcePdf = _project.SourcePdf with { PageCount = result.PageCount } };
                if (ShowPageThumbnails) StartThumbnailLoading();
            }

            PreviewImage = result.Image;
            PreviewPixelWidth = result.Image.PixelWidth;
            PreviewPixelHeight = result.Image.PixelHeight;
            _pageMetrics[result.PageNumber] = new PageMetrics(result.Image.PixelWidth, result.Image.PixelHeight, result.PageWidthPoints, result.PageHeightPoints);
            var companionRegions = _ndlOcrDocument?.GetScaledRegions(result.PageNumber, result.Image.PixelWidth, result.Image.PixelHeight) ?? [];
            var overlayRegions = companionRegions.Count > 0 ? companionRegions : result.TextRegions;
            if (!_pageOverlays.TryGetValue(result.PageNumber, out var pageOverlayModels))
            {
                pageOverlayModels = CreatePageOverlayModels(result.PageNumber, overlayRegions, _pageMetrics[result.PageNumber]);
                _pageOverlays[result.PageNumber] = pageOverlayModels;
                foreach (var overlay in pageOverlayModels) AttachOverlay(overlay);
            }
            OverlayItems.Clear();
            foreach (var region in pageOverlayModels) OverlayItems.Add(region);
            RecalculateReadingOrderCommand.RaiseCanExecuteChanged();
            SelectedOverlay = null;
            UpdateOverlaySummary();
            if (companionRegions.Count > 0)
                OcrDataSourceText = $"{_ndlOcrDocument!.SourceKind}（このページ: {companionRegions.Count}領域）";
            else if (result.TextRegions.Count > 0)
                OcrDataSourceText = $"PDFテキストレイヤー（このページ: {result.TextRegions.Count}領域）";
            PageSummary = $"{result.PageNumber} / {result.PageCount} ページ";
            StatusMessage = $"{result.PageNumber}ページを表示しました。";
            NotifyNavigationState();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowErrorAsync($"{pageNumber}ページを描画できませんでした。", ex); }
        finally
        {
            if (!token.IsCancellationRequested) IsPreviewLoading = false;
        }
    }

    private void StartThumbnailLoading()
    {
        if (!ShowPageThumbnails || _resolvedPdfPath is null || PageItems.Count == 0) return;
        CancelThumbnailLoading(clearImages: false);
        _thumbnailCancellation = new CancellationTokenSource();
        _ = LoadThumbnailsAsync(_resolvedPdfPath, _thumbnailCancellation.Token);
    }

    private async Task LoadThumbnailsAsync(string pdfPath, CancellationToken cancellationToken)
    {
        try
        {
            var currentPage = SelectedPage?.PageNumber ?? 1;
            var items = PageItems
                .OrderBy(item => item.PageNumber == currentPage ? 0 : 1)
                .ThenBy(item => Math.Abs(item.PageNumber - currentPage))
                .ToArray();
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShowPageThumbnails || item.Thumbnail is not null) continue;
                if (_thumbnailCache.TryGetValue(item.PageNumber, out var cachedBytes))
                {
                    var cachedImage = DecodeThumbnail(cachedBytes);
                    if (cachedImage is not null)
                    {
                        item.Thumbnail = cachedImage;
                        continue;
                    }
                    _thumbnailCache.Remove(item.PageNumber);
                }
                // Render once at the largest supported thumbnail width so the
                // preview stays sharp while the user moves the size slider.
                var thumbnail = await _previewService.RenderPageAsync(pdfPath, item.PageNumber, 220, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                item.Thumbnail = thumbnail.Image;
                _thumbnailCache[item.PageNumber] = EncodeThumbnail(thumbnail.Image);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusMessage = "一部のページサムネイルを生成できませんでした。";
            await _log.WriteAsync(LogLevel.Warning, "thumbnail.render.failed", ex.Message, ex);
        }
    }

    private void CancelThumbnailLoading(bool clearImages)
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        if (!clearImages) return;
        foreach (var item in PageItems) item.Thumbnail = null;
    }

    private void SetCurrentPageWithoutRendering(PdfPageItem? page)
    {
        if (_selectedPage is not null) _selectedPage.IsCurrent = false;
        _selectedPage = page;
        if (_selectedPage is not null) _selectedPage.IsCurrent = true;
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(IsCurrentPageImageOptimizationEnabled));
        OnPropertyChanged(nameof(CurrentPageImageOptimizationActionText));
        OptimizeCurrentPageImageCommand.RaiseCanExecuteChanged();
        AddBookmarkCommand.RaiseCanExecuteChanged();
        NotifyNavigationState();
    }

    private void ReplaceThumbnailCache(IReadOnlyDictionary<int, byte[]> thumbnails)
    {
        _thumbnailCache.Clear();
        foreach (var thumbnail in thumbnails) _thumbnailCache[thumbnail.Key] = thumbnail.Value;
    }

    private static byte[] EncodeThumbnail(ImageSource image)
    {
        if (image is not BitmapSource bitmap) return [];
        var encoder = new JpegBitmapEncoder { QualityLevel = 72 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource? DecodeThumbnail(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var image = decoder.Frames[0];
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    /// <summary>ページ一覧の複数選択をページ操作コマンドへ反映します。</summary>
    public void SetPageSelection(IEnumerable<PdfPageItem> selectedPages)
    {
        _selectedPageNumbers.Clear();
        _selectedPageNumbers.AddRange(selectedPages.Select(page => page.PageNumber).Distinct().Order());
        OnPropertyChanged(nameof(SelectedPageCount));
        RaisePageManagementCommands();
    }

    /// <summary>選択ページを指定した挿入位置へ移動します。</summary>
    /// <param name="insertionIndex">移動対象を除去した一覧における0始まり挿入位置。</param>
    public async Task ReorderSelectedPagesAsync(int insertionIndex)
    {
        if (!CanModifySelectedPages() || _resolvedPdfPath is null) return;
        try
        {
            var selected = _selectedPageNumbers.ToHashSet();
            var remaining = Enumerable.Range(1, PageItems.Count).Where(page => !selected.Contains(page)).ToList();
            insertionIndex = Math.Clamp(insertionIndex, 0, remaining.Count);
            var ordered = remaining.Take(insertionIndex)
                .Concat(_selectedPageNumbers)
                .Concat(remaining.Skip(insertionIndex))
                .ToArray();
            if (ordered.SequenceEqual(Enumerable.Range(1, PageItems.Count))) return;
            await ComposeCurrentPdfAsync(
                [new PdfPageManagementService.PageSource(_resolvedPdfPath, string.Join(",", ordered))],
                ordered.Select(page => (int?)page).ToArray(),
                Math.Min(insertionIndex + 1, ordered.Length),
                "ページを並べ替えました。");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("ページを並べ替えできませんでした。元のPDFは変更していません。", ex);
        }
    }

    private bool CanModifySelectedPages() =>
        HasDocument && _selectedPageNumbers.Count > 0;

    private bool CanDeleteSelectedPages() =>
        CanModifySelectedPages() && _selectedPageNumbers.Count < PageItems.Count;

    private async Task InsertPagesAsync()
    {
        if (_resolvedPdfPath is null) return;
        var dialog = new OpenFileDialog
        {
            Filter = "PDFファイル (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false,
            Title = "挿入するPDFを選択",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var imported = await _previewService.RenderPageAsync(dialog.FileName, 1, 240);
            var insertAfter = SelectedPage?.PageNumber ?? PageItems.Count;
            var sources = new List<PdfPageManagementService.PageSource>();
            if (insertAfter > 0)
                sources.Add(new(_resolvedPdfPath, $"1-{insertAfter}"));
            sources.Add(new(dialog.FileName, "1-z"));
            if (insertAfter < PageItems.Count)
                sources.Add(new(_resolvedPdfPath, $"{insertAfter + 1}-z"));
            var oldOrder = Enumerable.Range(1, insertAfter).Select(page => (int?)page)
                .Concat(Enumerable.Repeat<int?>(null, imported.PageCount))
                .Concat(Enumerable.Range(insertAfter + 1, PageItems.Count - insertAfter).Select(page => (int?)page))
                .ToArray();
            await ComposeCurrentPdfAsync(sources, oldOrder, insertAfter + 1, $"{imported.PageCount}ページを追加しました。");
        }
        catch (Exception ex) { await ShowErrorAsync("ページを追加できませんでした。元PDFは変更していません。", ex); }
    }

    private async Task DeleteSelectedPagesAsync()
    {
        if (!CanDeleteSelectedPages() || _resolvedPdfPath is null) return;
        var answer = MessageBox.Show(
            $"選択した{_selectedPageNumbers.Count}ページを削除します。\n元PDFは変更されませんが、この操作はUndoでは戻せません。続行しますか？",
            "ページを削除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            var selected = _selectedPageNumbers.ToHashSet();
            var kept = Enumerable.Range(1, PageItems.Count).Where(page => !selected.Contains(page)).ToArray();
            var target = Math.Clamp(_selectedPageNumbers.Min(), 1, kept.Length);
            await ComposeCurrentPdfAsync(
                [new PdfPageManagementService.PageSource(_resolvedPdfPath, string.Join(',', kept))],
                kept.Select(page => (int?)page).ToArray(),
                target,
                $"{selected.Count}ページを削除しました。");
        }
        catch (Exception ex) { await ShowErrorAsync("ページを削除できませんでした。元PDFは変更していません。", ex); }
    }

    private async Task RotateSelectedPagesAsync(int clockwiseDegrees)
    {
        if (!CanModifySelectedPages() || _resolvedPdfPath is null || _project is null) return;
        BeginBackgroundOperation("ページを回転して再構成しています...");
        try
        {
            SynchronizeProjectPages();
            var outputPath = CreatePageWorkingPdfPath();
            await _pageManagementService.RotateAsync(_resolvedPdfPath, _selectedPageNumbers, clockwiseDegrees, outputPath);
            var selected = _selectedPageNumbers.ToHashSet();
            _project = _project with
            {
                Pages = _project.Pages.Select(page => selected.Contains(page.PageNumber)
                    ? RotateOcrPage(page, clockwiseDegrees)
                    : page).ToArray(),
            };
            await AdoptPageWorkingPdfAsync(outputPath, PageItems.Count, _selectedPageNumbers.Min(),
                $"選択した{selected.Count}ページを{(clockwiseDegrees > 0 ? "右" : "左")}へ90°回転しました。");
        }
        catch (Exception ex) { await ShowErrorAsync("ページを回転できませんでした。元PDFは変更していません。", ex); }
        finally { EndBackgroundOperation(); }
    }

    private async Task ComposeCurrentPdfAsync(
        IReadOnlyList<PdfPageManagementService.PageSource> sources,
        IReadOnlyList<int?> oldPageAtNewPosition,
        int targetPage,
        string completedMessage)
    {
        if (_resolvedPdfPath is null || _project is null) return;
        BeginBackgroundOperation("ページを再構成しています...");
        try
        {
            SynchronizeProjectPages();
            var outputPath = CreatePageWorkingPdfPath();
            await _pageManagementService.ComposeAsync(_resolvedPdfPath, sources, outputPath);
            var oldToNew = oldPageAtNewPosition
                .Select((oldPage, index) => (oldPage, newPage: index + 1))
                .Where(item => item.oldPage.HasValue)
                .ToDictionary(item => item.oldPage!.Value, item => item.newPage);
            _project = _project with
            {
                Pages = _project.Pages
                    .Where(page => oldToNew.ContainsKey(page.PageNumber))
                    .Select(page => page with { PageNumber = oldToNew[page.PageNumber] })
                    .OrderBy(page => page.PageNumber)
                    .ToArray(),
                Bookmarks = RemapBookmarks(_project.Bookmarks, oldToNew, oldPageAtNewPosition.Count),
                BookmarksModified = true,
            };
            LoadBookmarkItems(_project.Bookmarks);
            await AdoptPageWorkingPdfAsync(outputPath, oldPageAtNewPosition.Count, targetPage, completedMessage);
        }
        finally { EndBackgroundOperation(); }
    }

    private async Task AdoptPageWorkingPdfAsync(string pdfPath, int pageCount, int targetPage, string completedMessage)
    {
        if (_project is null) return;
        var source = await _packages.CreateSourceReferenceAsync(pdfPath);
        _project = _project with { SourcePdf = source with { PageCount = pageCount } };
        _resolvedPdfPath = pdfPath;
        _hasPageStructureEdits = true;
        SourcePdfPath = pdfPath;
        SourceHash = _project.SourcePdf.Sha256;
        _ndlOcrDocument = null;
        _thumbnailCache.Clear();
        CancelThumbnailLoading(clearImages: true);
        ClearOverlaySession();
        _selectedPageNumbers.Clear();
        OnPropertyChanged(nameof(SelectedPageCount));
        await RenderPageAsync(Math.Clamp(targetPage, 1, pageCount), populatePageList: true);
        MarkNonUndoableChange();
        StatusMessage = completedMessage;
        RaisePageManagementCommands();
        await _log.WriteAsync(LogLevel.Information, "page.structure.changed", completedMessage);
    }

    private string CreatePageWorkingPdfPath()
    {
        if (_project is null) throw new InvalidOperationException("プロジェクトが開かれていません。");
        var directory = Path.Combine(_paths.WorkspaceDirectory, _project.ProjectId.ToString("N"), "page-edits");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"pages-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.pdf");
    }

    private static OcrPage RotateOcrPage(OcrPage page, int clockwiseDegrees)
    {
        var clockwise = clockwiseDegrees > 0;
        TextGeometry Transform(TextGeometry geometry)
        {
            var bounds = geometry.LocalBounds;
            var rotated = clockwise
                ? new PdfRectangle(new PdfPoint(bounds.Bottom, page.WidthPoints - bounds.Right), new PdfSize(bounds.Size.Height, bounds.Size.Width))
                : new PdfRectangle(new PdfPoint(page.HeightPoints - bounds.Top, bounds.Left), new PdfSize(bounds.Size.Height, bounds.Size.Width));
            var center = clockwise
                ? new PdfPoint(geometry.RotationCenter.Y, page.WidthPoints - geometry.RotationCenter.X)
                : new PdfPoint(page.HeightPoints - geometry.RotationCenter.Y, geometry.RotationCenter.X);
            return geometry with
            {
                LocalBounds = rotated,
                RotationCenter = center,
                RotationDegrees = NormalizeRotation(geometry.RotationDegrees + clockwiseDegrees),
            };
        }
        return page with
        {
            WidthPoints = page.HeightPoints,
            HeightPoints = page.WidthPoints,
            RotationDegrees = (int)NormalizeRotation(page.RotationDegrees + clockwiseDegrees),
            TextRegions = page.TextRegions.Select(region => region with
            {
                OriginalGeometry = Transform(region.OriginalGeometry),
                EditedGeometry = Transform(region.EditedGeometry),
            }).ToArray(),
        };
    }

    private static double NormalizeRotation(double degrees)
    {
        var normalized = degrees % 360;
        return normalized <= -180 ? normalized + 360 : normalized > 180 ? normalized - 360 : normalized;
    }

    private static IReadOnlyList<PdfBookmark> RemapBookmarks(
        IReadOnlyList<PdfBookmark> bookmarks,
        IReadOnlyDictionary<int, int> oldToNew,
        int newPageCount)
    {
        int MapPage(int oldPage)
        {
            if (oldToNew.TryGetValue(oldPage, out var exact)) return exact;
            var nearest = oldToNew.OrderBy(pair => Math.Abs(pair.Key - oldPage)).ThenBy(pair => pair.Key).FirstOrDefault();
            return nearest.Key == 0 ? 1 : Math.Clamp(nearest.Value, 1, newPageCount);
        }
        PdfBookmark Map(PdfBookmark bookmark) => bookmark with
        {
            PageNumber = MapPage(bookmark.PageNumber),
            Children = bookmark.Children.Select(Map).ToArray(),
        };
        return bookmarks.Select(Map).ToArray();
    }

    private void RaisePageManagementCommands()
    {
        InsertPagesCommand.RaiseCanExecuteChanged();
        DeletePagesCommand.RaiseCanExecuteChanged();
        RotatePagesLeftCommand.RaiseCanExecuteChanged();
        RotatePagesRightCommand.RaiseCanExecuteChanged();
        OptimizeDocumentImagesCommand.RaiseCanExecuteChanged();
    }

    private void GoToPreviousPage()
    {
        if (!CanGoPrevious || SelectedPage is null) return;
        SelectedPage = PageItems[SelectedPage.PageNumber - 2];
    }

    private void GoToNextPage()
    {
        if (!CanGoNext || SelectedPage is null) return;
        SelectedPage = PageItems[SelectedPage.PageNumber];
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// ドラッグなど連続操作の開始状態を記録し、操作全体を1件のUndo履歴にまとめます。
    /// </summary>
    public void BeginOverlayEdit(OverlayRegionViewModel region)
    {
        if (_batchedRegion is not null) return;
        _batchedRegion = region;
        _batchStart = region.Capture();
    }

    /// <summary>
    /// 連続操作を終了し、開始時から変更があればUndo履歴へ記録します。
    /// </summary>
    /// <param name="description">履歴画面に表示する操作説明。</param>
    public void EndOverlayEdit(string description)
    {
        if (_batchedRegion is null || _batchStart is null) return;
        var region = _batchedRegion;
        var before = _batchStart;
        _batchedRegion = null;
        _batchStart = null;
        var after = region.Capture();
        if (before with { ReviewStatus = after.ReviewStatus } != after && after.ReviewStatus != ReviewStatus.Modified)
        {
            _applyingHistory = true;
            try { region.ReviewStatus = ReviewStatus.Modified; }
            finally { _applyingHistory = false; }
            after = region.Capture();
            OnPropertyChanged(nameof(SelectedReviewStatus));
        }
        _lastOverlaySnapshots[region.Id] = after;
        if (before != after) RecordEdit(new OverlayEdit([new OverlayRegionChange(region, before, after)], description));
    }

    /// <summary>
    /// OCR領域の複数選択と、プロパティ欄で扱う主選択を同時に更新します。
    /// </summary>
    /// <param name="regions">選択対象の領域。</param>
    /// <param name="primary">主選択にする領域。省略時は一覧の末尾を使用します。</param>
    public void SetOverlaySelection(IEnumerable<OverlayRegionViewModel> regions, OverlayRegionViewModel? primary = null)
    {
        _selectedOverlays.Clear();
        _selectedOverlays.AddRange(regions.Where(region => !region.IsDeleted).Distinct());
        SelectedOverlay = primary is not null && _selectedOverlays.Contains(primary)
            ? primary
            : _selectedOverlays.LastOrDefault();
        if (_alignmentReference is null || !_selectedOverlays.Contains(_alignmentReference))
            UpdateAlignmentReference(SelectedOverlay);
        OnPropertyChanged(nameof(SelectedOverlayCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(HasOverlaySelection));
        OnPropertyChanged(nameof(SelectedParagraphText));
        NotifyCharacterSelectionState();
        OnPropertyChanged(nameof(SelectedReviewStatus));
        OnPropertyChanged(nameof(SelectedWritingMode));
        RaiseMultiSelectionCommands();
        RaiseCharacterAdvanceCommands();
        DeleteOcrRegionsCommand.RaiseCanExecuteChanged();
        NotifyReviewState();
    }

    /// <summary>
    /// 現在ページへ手動OCR領域を追加し、Undo可能な編集として記録します。
    /// </summary>
    /// <param name="bounds">プレビュー画像のピクセル座標で指定した領域。</param>
    /// <returns>追加した領域。ページが未選択の場合は<see langword="null"/>。</returns>
    public OverlayRegionViewModel? AddManualOcrRegion(Rect bounds)
    {
        if (!CanEditGeometry) return null;
        if (SelectedPage is null ||
            !_pageOverlays.TryGetValue(SelectedPage.PageNumber, out var pageOverlays) ||
            !_pageMetrics.ContainsKey(SelectedPage.PageNumber))
            return null;

        var normalized = new Rect(
            Math.Clamp(bounds.Left, 0, Math.Max(0, PreviewPixelWidth - 4)),
            Math.Clamp(bounds.Top, 0, Math.Max(0, PreviewPixelHeight - 4)),
            Math.Clamp(bounds.Width, 4, Math.Max(4, PreviewPixelWidth - Math.Max(0, bounds.Left))),
            Math.Clamp(bounds.Height, 4, Math.Max(4, PreviewPixelHeight - Math.Max(0, bounds.Top))));
        var readingOrder = pageOverlays.Where(region => !region.IsDeleted).Select(region => region.ReadingOrder).DefaultIfEmpty(0).Max() + 1;
        var before = new OverlayRegionSnapshot(
            string.Empty,
            normalized.Left,
            normalized.Top,
            normalized.Width,
            normalized.Height,
            0,
            readingOrder,
            string.Empty,
            string.Empty,
            ReviewStatus.Modified,
            true);
        var after = before with { IsDeleted = false };
        var region = new OverlayRegionViewModel(
            Guid.NewGuid(),
            string.Empty,
            before,
            after,
            true,
            false,
            "manual",
            null,
            true,
            false);
        pageOverlays.Add(region);
        AttachOverlay(region);
        OverlayItems.Add(region);
        _lastOverlaySnapshots[region.Id] = after;
        EditUnitIndex = (int)OcrEditUnit.Line;
        SetOverlaySelection([region], region);
        RecordEdit(new OverlayEdit([new OverlayRegionChange(region, before, after)], "透明テキスト領域を追加"));
        UpdateOverlaySummary();
        RecalculateReadingOrderCommand.RaiseCanExecuteChanged();
        IsAddOcrRegionMode = false;
        StatusMessage = "透明テキスト領域を追加しました。右側の「行の文字列」へ文字を入力してください。";
        return region;
    }

    private void DeleteSelectedOcrRegions()
    {
        var targets = _selectedOverlays.Where(region => !region.IsDeleted).ToArray();
        if (targets.Length == 0) return;
        var ordered = GetActiveRegionsInReadingOrder();
        ApplyRegionEdit(
            targets.Length == 1 ? "透明テキスト領域を削除" : $"{targets.Length}件の透明テキスト領域を削除",
            ordered,
            () =>
            {
                foreach (var region in targets) region.IsDeleted = true;
                AssignSequentialReadingOrder(ordered.Where(region => !region.IsDeleted));
            });
        SetOverlaySelection([]);
        UpdateOverlaySummary();
        RecalculateReadingOrderCommand.RaiseCanExecuteChanged();
        StatusMessage = targets.Length == 1
            ? "透明テキスト領域を削除しました。Undoで元に戻せます。"
            : $"{targets.Length}件の透明テキスト領域を削除しました。Undoで元に戻せます。";
    }

    /// <summary>
    /// 現在の編集単位に従い、クリックした領域と一緒に操作する領域を解決します。
    /// </summary>
    /// <remarks>
    /// 段落編集では近接関係をたどって段落候補を集め、読み順に並べて返します。
    /// 行・文字編集では指定領域だけを返します。
    /// </remarks>
    public IReadOnlyList<OverlayRegionViewModel> ResolveEditUnitSelection(OverlayRegionViewModel region)
    {
        if (EditUnit != OcrEditUnit.Paragraph) return [region];
        var paragraph = new HashSet<OverlayRegionViewModel> { region };
        var pending = new Queue<OverlayRegionViewModel>();
        pending.Enqueue(region);
        while (pending.TryDequeue(out var current))
        {
            foreach (var candidate in OverlayItems.Where(candidate => !candidate.IsDeleted))
            {
                if (paragraph.Contains(candidate) || !BelongsToSameParagraph(current, candidate)) continue;
                paragraph.Add(candidate);
                pending.Enqueue(candidate);
            }
        }
        return paragraph.OrderBy(item => item.ReadingOrder).ToArray();
    }

    /// <summary>
    /// OCR領域内の座標から文字セルを求め、単独・追加・範囲選択を適用します。
    /// </summary>
    /// <param name="region">文字を選択するOCR領域。</param>
    /// <param name="localX">領域内のX座標。</param>
    /// <param name="localY">領域内のY座標。</param>
    /// <param name="toggle">既存選択へ追加または解除するか。</param>
    /// <param name="extendRange">直前の主選択との間を範囲選択するか。</param>
    public void SelectCharacterAt(
        OverlayRegionViewModel region,
        double localX,
        double localY,
        bool toggle = false,
        bool extendRange = false)
    {
        foreach (var item in OverlayItems)
            if (!ReferenceEquals(item, region)) item.ClearCharacterSelection();
        if (EditUnit != OcrEditUnit.Character || region.TextElementCount == 0)
        {
            region.ClearCharacterSelection();
        }
        else
        {
            region.SelectCharacter(region.FindCharacterIndexAt(localX, localY), toggle, extendRange);
        }
        NotifyCharacterSelectionState();
        RaiseCharacterAdvanceCommands();
    }

    private bool CanMoveToPreviousCharacter() =>
        IsCharacterEditMode &&
        SelectedOverlay is { TextElementCount: > 0 } region &&
        (region.SelectedCharacterIndex < 0 || region.SelectedCharacterIndex > 0);

    private bool CanMoveToNextCharacter() =>
        IsCharacterEditMode &&
        SelectedOverlay is { TextElementCount: > 0 } region &&
        (region.SelectedCharacterIndex < 0 || region.SelectedCharacterIndex < region.TextElementCount - 1);

    private void MoveCharacterSelection(int direction)
    {
        if (SelectedOverlay is not { TextElementCount: > 0 } region || !IsCharacterEditMode) return;
        var index = region.SelectedCharacterIndex < 0
            ? direction > 0 ? 0 : region.TextElementCount - 1
            : Math.Clamp(region.SelectedCharacterIndex + Math.Sign(direction), 0, region.TextElementCount - 1);
        region.SelectedCharacterIndex = index;
        NotifyCharacterSelectionState();
        RaiseCharacterAdvanceCommands();
        StatusMessage = $"{index + 1}/{region.TextElementCount}文字目を選択しました。";
    }

    private bool CanAdjustCharacterSelectionAdvance() =>
        IsCharacterEditMode && SelectedOverlay?.HasUnlockedSelectedCharacters == true;

    private void AdjustCharacterSelectionAdvance(double delta)
    {
        if (SelectedOverlay is not { HasCharacterSelection: true } region) return;
        var adjustedCount = region.SelectedCharacterIndices.Count;
        ApplyRegionEdit(
            delta > 0 ? "選択文字の幅を広げる" : "選択文字の幅を狭める",
            [region],
            () => region.AdjustSelectedCharacterAdvances(delta));
        StatusMessage = delta > 0
            ? $"選択した{adjustedCount}文字の送り幅をまとめて広げました。"
            : $"選択した{adjustedCount}文字の送り幅をまとめて狭めました。";
    }

    private bool CanSplitRegionAtSelectedCharacter() =>
        IsCharacterEditMode && _selectedOverlays.Count == 1 && SelectedOverlay?.CanSplitAtSelectedCharacter == true;

    private void SplitRegionAtSelectedCharacter()
    {
        if (SelectedPage is null || SelectedOverlay is not { CanSplitAtSelectedCharacter: true } source ||
            !_pageOverlays.TryGetValue(SelectedPage.PageNumber, out var pageOverlays))
            return;

        var (leadingSnapshot, trailingSnapshot) = source.CreateSplitSnapshots();
        var sourceBefore = source.Capture();
        var sourceAfter = sourceBefore with { IsDeleted = true, ReviewStatus = ReviewStatus.Modified };
        var leadingBefore = leadingSnapshot with { IsDeleted = true };
        var trailingBefore = trailingSnapshot with { IsDeleted = true };
        var leading = new OverlayRegionViewModel(
            Guid.NewGuid(), leadingSnapshot.Text, leadingBefore, leadingSnapshot,
            source.IsInvisible, source.IsVertical, source.ProviderId, source.Confidence, true, false);
        var trailing = new OverlayRegionViewModel(
            Guid.NewGuid(), trailingSnapshot.Text, trailingBefore, trailingSnapshot,
            source.IsInvisible, source.IsVertical, source.ProviderId, source.Confidence, true, false);

        var orderedBefore = GetActiveRegionsInReadingOrder();
        var existingBefore = orderedBefore.ToDictionary(region => region, region => region.Capture());
        var sourceIndex = orderedBefore.IndexOf(source);
        var orderedAfter = orderedBefore.ToList();
        if (sourceIndex >= 0)
        {
            orderedAfter.RemoveAt(sourceIndex);
            orderedAfter.Insert(sourceIndex, leading);
            orderedAfter.Insert(sourceIndex + 1, trailing);
        }

        _applyingHistory = true;
        try
        {
            source.Apply(sourceAfter);
            pageOverlays.Add(leading);
            pageOverlays.Add(trailing);
            AttachOverlay(leading);
            AttachOverlay(trailing);
            OverlayItems.Add(leading);
            OverlayItems.Add(trailing);
            AssignSequentialReadingOrder(orderedAfter);
        }
        finally { _applyingHistory = false; }

        _lastOverlaySnapshots[source.Id] = sourceAfter;
        _lastOverlaySnapshots[leading.Id] = leadingSnapshot;
        _lastOverlaySnapshots[trailing.Id] = trailingSnapshot;
        SetOverlaySelection([trailing], trailing);
        trailing.SelectedCharacterIndex = 0;
        var changes = orderedBefore
            .Select(region => new OverlayRegionChange(region, existingBefore[region], region.Capture()))
            .Where(change => change.Before != change.After)
            .Concat([
                new OverlayRegionChange(leading, leadingBefore, leading.Capture()),
                new OverlayRegionChange(trailing, trailingBefore, trailing.Capture()),
            ])
            .ToArray();
        foreach (var change in changes) _lastOverlaySnapshots[change.Region.Id] = change.After;
        RecordEdit(new OverlayEdit(changes,
        "選択文字を起点にOCR領域を2分割"));
        UpdateOverlaySummary();
        RecalculateReadingOrderCommand.RaiseCanExecuteChanged();
        StatusMessage = $"OCR領域を「{leading.Text}」と「{trailing.Text}」へ分割しました。";
    }

    private bool CanMergeSelectedRegions() =>
        IsCharacterEditMode && _selectedOverlays.Count == 2 && _selectedOverlays[0].CanMergeWith(_selectedOverlays[1]);

    private void MergeSelectedRegions()
    {
        if (SelectedPage is null || _selectedOverlays.Count != 2 ||
            !_pageOverlays.TryGetValue(SelectedPage.PageNumber, out var pageOverlays))
            return;
        var first = _selectedOverlays[0];
        var second = _selectedOverlays[1];
        if (!first.CanMergeWith(second)) return;

        var mergedSnapshot = first.CreateMergedSnapshotWith(second);
        var firstBefore = first.Capture();
        var secondBefore = second.Capture();
        var firstAfter = firstBefore with { IsDeleted = true, ReviewStatus = ReviewStatus.Modified };
        var secondAfter = secondBefore with { IsDeleted = true, ReviewStatus = ReviewStatus.Modified };
        var mergedBefore = mergedSnapshot with { IsDeleted = true };
        var merged = new OverlayRegionViewModel(
            Guid.NewGuid(), mergedSnapshot.Text, mergedBefore, mergedSnapshot,
            first.IsInvisible && second.IsInvisible, mergedSnapshot.IsVertical ?? first.IsVertical,
            first.ProviderId, first.Confidence, true, false);

        var orderedBefore = GetActiveRegionsInReadingOrder();
        var existingBefore = orderedBefore.ToDictionary(region => region, region => region.Capture());
        var insertionIndex = Math.Min(orderedBefore.IndexOf(first), orderedBefore.IndexOf(second));
        var orderedAfter = orderedBefore.Where(region => !ReferenceEquals(region, first) && !ReferenceEquals(region, second)).ToList();
        orderedAfter.Insert(Math.Clamp(insertionIndex, 0, orderedAfter.Count), merged);

        _applyingHistory = true;
        try
        {
            first.Apply(firstAfter);
            second.Apply(secondAfter);
            pageOverlays.Add(merged);
            AttachOverlay(merged);
            OverlayItems.Add(merged);
            AssignSequentialReadingOrder(orderedAfter);
        }
        finally { _applyingHistory = false; }

        _lastOverlaySnapshots[first.Id] = firstAfter;
        _lastOverlaySnapshots[second.Id] = secondAfter;
        _lastOverlaySnapshots[merged.Id] = mergedSnapshot;
        SetOverlaySelection([merged], merged);
        var changes = orderedBefore
            .Select(region => new OverlayRegionChange(region, existingBefore[region], region.Capture()))
            .Where(change => change.Before != change.After)
            .Concat([new OverlayRegionChange(merged, mergedBefore, merged.Capture())])
            .ToArray();
        foreach (var change in changes) _lastOverlaySnapshots[change.Region.Id] = change.After;
        RecordEdit(new OverlayEdit(changes,
        "隣接するOCR領域を結合"));
        UpdateOverlaySummary();
        RecalculateReadingOrderCommand.RaiseCanExecuteChanged();
        StatusMessage = $"2つのOCR領域を「{merged.Text}」へ結合しました。";
    }

    private bool CanToggleSelectedCharacterLock() =>
        IsCharacterEditMode && SelectedOverlay?.HasCharacterSelection == true;

    private void ToggleSelectedCharacterLock()
    {
        if (SelectedOverlay is not { HasCharacterSelection: true } region) return;
        SetSelectedCharacterLocks(!region.AreSelectedCharactersLocked);
    }

    private void SetSelectedCharacterLocks(bool isLocked)
    {
        if (SelectedOverlay is not { HasCharacterSelection: true } region) return;
        ApplyRegionEdit(
            isLocked ? "選択文字の位置と幅をロック" : "選択文字のロックを解除",
            [region],
            () => region.SetSelectedCharacterLocks(isLocked));
        NotifyCharacterSelectionState();
        RaiseCharacterAdvanceCommands();
        StatusMessage = isLocked
            ? $"選択した{region.SelectedCharacterCount}文字の位置と幅を固定しました。"
            : $"選択した{region.SelectedCharacterCount}文字の固定を解除しました。";
    }

    private void ToggleGeometryLock()
    {
        var affected = GetSelectedGeometryLockTargets();
        if (affected.Count == 0) return;

        // 複数選択にロック済みと未ロックが混在する場合は、まず全件をロックします。
        // 全件がすでにロック済みの場合にだけ、全件のロックを解除します。
        var lockGeometry = affected.Any(region => !region.IsGeometryLocked);
        ApplyRegionEdit(
            lockGeometry
                ? $"OCR領域{affected.Count}件の位置とサイズをロック"
                : $"OCR領域{affected.Count}件のロックを解除",
            affected,
            () =>
            {
                foreach (var region in affected)
                    region.IsGeometryLocked = lockGeometry;
            });
        NotifyCharacterSelectionState();
        RaiseCharacterAdvanceCommands();
        StatusMessage = lockGeometry
            ? $"選択したOCR領域{affected.Count}件の位置・サイズ・回転を固定しました。"
            : $"選択したOCR領域{affected.Count}件の固定を解除しました。";
    }

    /// <summary>
    /// 位置・サイズ・回転のロック操作対象を、現在の複数選択から取得します。
    /// 複数選択情報がない場合は、プロパティ欄に表示中の単一領域を対象にします。
    /// </summary>
    private IReadOnlyList<OverlayRegionViewModel> GetSelectedGeometryLockTargets()
    {
        var selected = _selectedOverlays
            .Where(region => !region.IsDeleted)
            .Distinct()
            .ToArray();
        if (selected.Length > 0) return selected;

        return SelectedOverlay is { IsDeleted: false } region
            ? [region]
            : [];
    }

    private void NotifyCharacterSelectionState()
    {
        OnPropertyChanged(nameof(SelectedCharacterText));
        OnPropertyChanged(nameof(SelectedCharacterAdvance));
        OnPropertyChanged(nameof(CanEditSelectedCharacterAdvance));
        OnPropertyChanged(nameof(HasSelectedCharacter));
        OnPropertyChanged(nameof(HasSingleSelectedCharacter));
        OnPropertyChanged(nameof(HasMultipleSelectedCharacters));
        OnPropertyChanged(nameof(AreSelectedCharactersLocked));
        OnPropertyChanged(nameof(SelectedCharacterLockToolTip));
        OnPropertyChanged(nameof(IsSelectedGeometryLocked));
        OnPropertyChanged(nameof(IsSelectedGeometryEditable));
        OnPropertyChanged(nameof(SelectedCharacterCount));
        OnPropertyChanged(nameof(CharacterSelectionSummary));
    }

    private bool CanEqualizeCharacterAdvances() =>
        (IsCharacterEditMode || IsLineEditMode) && GetSelectedCharacterLines().Any(region => region.CanEqualizeCharacterAdvances);

    private bool CanRestoreOriginalCharacterAdvances() =>
        (IsCharacterEditMode || IsLineEditMode) && GetSelectedCharacterLines().Any(region => region.CanRestoreOriginalCharacterAdvances);

    private bool CanEstimateCharacterAdvances() =>
        (IsCharacterEditMode || IsLineEditMode) &&
        GetCharacterEstimationTargets().Count > 0 &&
        PreviewImage is System.Windows.Media.Imaging.BitmapSource;

    private bool CanAdjustSelectedLineCharacterSizes() =>
        IsLineEditMode && GetSelectedCharacterLines().Any(region => !region.IsGeometryLocked && region.HasUnlockedCharacters && region.TextElementCount > 0);

    private void AdjustSelectedLineCharacterSizes(double delta)
    {
        var targets = GetSelectedCharacterLines()
            .Where(region => !region.IsDeleted && !region.IsGeometryLocked && region.HasUnlockedCharacters && region.TextElementCount > 0)
            .ToArray();
        if (targets.Length == 0) return;
        ApplyRegionEdit(
            delta > 0 ? "行内の全文字を一括で広げる" : "行内の全文字を一括で狭める",
            targets,
            () =>
            {
                foreach (var region in targets)
                {
                    region.AdjustAllCharacterAdvances(delta);
                    region.ReviewStatus = ReviewStatus.Modified;
                }
            });
        StatusMessage = $"{targets.Length}行の全文字を一括で{(delta > 0 ? "広げました" : "狭めました")}。";
    }

    private bool CanEstimateCharacterSuffixAdvances() =>
        IsCharacterEditMode &&
        SelectedOverlay is { HasSingleCharacterSelection: true } region &&
        region.CanAutomaticallyAdjust &&
        region.SelectedCharacterIndex >= 0 &&
        region.SelectedCharacterIndex < region.TextElementCount - 1 &&
        PreviewImage is System.Windows.Media.Imaging.BitmapSource;

    private void EqualizeCharacterAdvances()
    {
        var targets = GetSelectedCharacterLines()
            .Where(region => region.CanEqualizeCharacterAdvances)
            .ToArray();
        if (targets.Length == 0) return;
        ApplyRegionEdit(
            targets.Length == 1 ? "行内の文字幅を等分" : $"選択した{targets.Length}行の文字幅を等分",
            targets,
            () =>
            {
                foreach (var region in targets) region.EqualizeCharacterAdvances();
            });
        StatusMessage = $"{targets.Length}行の文字幅を等分しました。";
    }

    private void RestoreOriginalCharacterAdvances()
    {
        var targets = GetSelectedCharacterLines()
            .Where(region => region.CanRestoreOriginalCharacterAdvances)
            .ToArray();
        if (targets.Length == 0) return;
        ApplyRegionEdit(
            targets.Length == 1 ? "OCR取込時の文字幅へ戻す" : $"選択した{targets.Length}行をOCR取込時の文字幅へ戻す",
            targets,
            () =>
            {
                foreach (var region in targets) region.RestoreOriginalCharacterAdvances();
            });
        StatusMessage = $"{targets.Length}行をOCR取込時の文字幅へ戻しました。";
    }

    private void EstimateCharacterAdvances()
    {
        if (PreviewImage is not System.Windows.Media.Imaging.BitmapSource image) return;
        var targets = GetCharacterEstimationTargets();
        if (targets.Count == 0) return;

        var options = new CharacterAdvanceEstimationOptions(
            _applicationSettings.CharacterEstimationMinimumAspectRatio,
            _applicationSettings.CharacterEstimationMaximumAspectRatio,
            _applicationSettings.CharacterEstimationUniformity,
            _applicationSettings.CharacterEstimationInkCoverage,
            _applicationSettings.CharacterEstimationGlyphPrior);
        var estimates = new List<(OverlayRegionViewModel Region, CharacterAdvanceEstimationResult Estimate)>();
        var failures = new List<(OverlayRegionViewModel Region, Exception Error)>();
        foreach (var region in targets)
        {
            try
            {
                estimates.Add((region, CharacterAdvanceEstimator.Estimate(image, region, options)));
            }
            catch (Exception ex)
            {
                failures.Add((region, ex));
                _ = _log.WriteAsync(
                    LogLevel.Warning,
                    "character-advance-estimation.failed",
                    $"Region {region.Id}: {ex.Message}",
                    ex);
            }
        }

        if (estimates.Count > 0)
        {
            ApplyRegionEdit(
                estimates.Count == 1 ? "画像から文字幅を自動推定" : $"選択した{estimates.Count}行の文字幅を自動推定",
                estimates.Select(item => item.Region).ToArray(),
                () =>
                {
                    foreach (var (region, estimate) in estimates)
                    {
                        region.ApplyCharacterAdvanceEstimation(estimate);
                        region.ReviewStatus = ReviewStatus.Modified;
                    }
                });
        }

        StatusMessage = failures.Count == 0
            ? $"{estimates.Count}行の文字幅を順に自動調整しました。"
            : estimates.Count == 0
                ? $"選択した{failures.Count}行を自動調整できませんでした。最初のエラー: {failures[0].Error.Message}"
                : $"{estimates.Count}行を自動調整し、{failures.Count}行は判定できず変更しませんでした。";
    }

    /// <summary>
    /// 選択した前処理を行った後、指定されたページの文字送りを順に自動調整します。
    /// </summary>
    /// <param name="requestedPageNumbers">処理対象となる1始まりのページ番号。</param>
    /// <param name="options">鍵括弧、句読点、細長い行端文字、および近接行の高さに対する前処理設定。</param>
    /// <param name="progress">ページ数と調整済み行数を画面へ通知する進捗受信先。</param>
    /// <param name="cancellationToken">長時間処理を取り消すためのトークン。</param>
    public async Task RunBatchCharacterAdjustmentAsync(
        IReadOnlyCollection<int> requestedPageNumbers,
        BatchCharacterAdjustmentOptions options,
        IProgress<BatchCharacterAdjustmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_resolvedPdfPath is null || PageItems.Count == 0) return;

        var availablePageNumbers = PageItems
            .Select(item => item.PageNumber)
            .ToHashSet();
        var pageNumbers = requestedPageNumbers
            .Where(availablePageNumbers.Contains)
            .Distinct()
            .OrderBy(pageNumber => pageNumber)
            .ToArray();
        if (pageNumbers.Length == 0) return;

        var estimationOptions = new CharacterAdvanceEstimationOptions(
            _applicationSettings.CharacterEstimationMinimumAspectRatio,
            _applicationSettings.CharacterEstimationMaximumAspectRatio,
            _applicationSettings.CharacterEstimationUniformity,
            _applicationSettings.CharacterEstimationInkCoverage,
            _applicationSettings.CharacterEstimationGlyphPrior);
        var leadingExpansionCount = 0;
        var trailingExpansionCount = 0;
        var normalizedThicknessCount = 0;
        var adjustedCount = 0;
        var targetCount = 0;
        var lockedLineCount = 0;
        var lockedCharacterLineCount = 0;
        var failures = new List<(OverlayRegionViewModel Region, Exception Error)>();
        var pageFailures = new List<(int PageNumber, Exception Error)>();
        var before = new Dictionary<OverlayRegionViewModel, OverlayRegionSnapshot>();
        var restartThumbnailLoading = ShowPageThumbnails;

        CancelThumbnailLoading(clearImages: false);
        _applyingHistory = true;
        try
        {
            progress?.Report(new BatchCharacterAdjustmentProgress(
                0,
                pageNumbers.Length,
                pageNumbers[0],
                adjustedCount,
                targetCount));
            for (var pageIndex = 0; pageIndex < pageNumbers.Length; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageNumber = pageNumbers[pageIndex];
                StatusMessage = $"{pageNumber}ページを自動調整しています（{pageIndex + 1}/{pageNumbers.Length}）...";

                try
                {
                    var (image, pageRegions) = await LoadPageForBatchCharacterAdjustmentAsync(
                        pageNumber,
                        cancellationToken);
                    var activeRegions = pageRegions.Where(region => !region.IsDeleted).ToArray();
                    lockedLineCount += activeRegions.Count(region => region.IsGeometryLocked);
                    lockedCharacterLineCount += activeRegions.Count(region =>
                        !region.IsGeometryLocked &&
                        region.HasLockedCharacters);

                    var targets = activeRegions
                        .Where(region => region.CanAutomaticallyAdjust)
                        .OrderBy(region => region.ReadingOrder)
                        .ToArray();
                    targetCount += targets.Length;
                    progress?.Report(new BatchCharacterAdjustmentProgress(
                        pageIndex,
                        pageNumbers.Length,
                        pageNumber,
                        adjustedCount,
                        targetCount,
                        0,
                        targets.Length));
                    foreach (var region in targets)
                        before.TryAdd(region, region.Capture());

                    var beforePreprocessing = targets.ToDictionary(
                        region => region,
                        region => region.Capture());
                    var preprocessingResult = BatchCharacterAdjustmentPreprocessor.Apply(
                        targets,
                        activeRegions,
                        options,
                        image.PixelWidth,
                        image.PixelHeight);
                    leadingExpansionCount += preprocessingResult.LeadingExpansionCount;
                    trailingExpansionCount += preprocessingResult.TrailingExpansionCount;
                    normalizedThicknessCount += preprocessingResult.NormalizedThicknessCount;

                    for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var region = targets[targetIndex];
                        if (beforePreprocessing[region] != region.Capture())
                            region.ReviewStatus = ReviewStatus.Modified;
                        try
                        {
                            var estimate = CharacterAdvanceEstimator.Estimate(image, region, estimationOptions);
                            // ApplyCharacterAdvanceEstimationは、文字単位で固定したセルを保持し、
                            // 固定されていない文字だけへ推定結果を反映する。
                            region.ApplyCharacterAdvanceEstimation(estimate);
                            region.ReviewStatus = ReviewStatus.Modified;
                            adjustedCount++;
                        }
                        catch (Exception ex)
                        {
                            failures.Add((region, ex));
                            await _log.WriteAsync(
                                LogLevel.Warning,
                                "batch-character-adjustment.failed",
                                $"Page {pageNumber}, region {region.Id}: {ex.Message}",
                                ex);
                        }

                        var processedLineCount = targetIndex + 1;
                        if (processedLineCount == targets.Length || processedLineCount % 8 == 0)
                        {
                            progress?.Report(new BatchCharacterAdjustmentProgress(
                                pageIndex,
                                pageNumbers.Length,
                                pageNumber,
                                adjustedCount,
                                targetCount,
                                processedLineCount,
                                targets.Length));
                            // UIスレッドへ処理機会を返し、進捗描画と中止操作を受け付ける。
                            await Task.Yield();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    pageFailures.Add((pageNumber, ex));
                    await _log.WriteAsync(
                        LogLevel.Warning,
                        "batch-character-adjustment.page-failed",
                        $"Page {pageNumber}: {ex.Message}",
                        ex);
                }

                progress?.Report(new BatchCharacterAdjustmentProgress(
                    pageIndex + 1,
                    pageNumbers.Length,
                    pageNumber,
                    adjustedCount,
                    targetCount,
                    0,
                    0));
            }
        }
        catch
        {
            // 中断または予期しない失敗では、中途半端な対象ページの変更を残さない。
            foreach (var (region, snapshot) in before)
                region.Apply(snapshot);
            throw;
        }
        finally
        {
            _applyingHistory = false;
            if (restartThumbnailLoading) StartThumbnailLoading();
        }

        var changes = before
            .Select(item => new OverlayRegionChange(item.Key, item.Value, item.Key.Capture()))
            .Where(change => change.Before != change.After)
            .ToArray();
        foreach (var change in changes)
            _lastOverlaySnapshots[change.Region.Id] = change.After;
        if (changes.Length > 0)
            RecordEdit(new OverlayEdit(
                changes,
                $"前処理付き文字幅一括自動調整（対象{pageNumbers.Length}ページ）"));

        UpdateOverlaySummary();

        var preprocessingSummary =
            $"行高統一 {normalizedThicknessCount}行、" +
            $"行頭拡張 {leadingExpansionCount}行、" +
            $"行末拡張 {trailingExpansionCount}行";
        var lockedSummary = lockedLineCount + lockedCharacterLineCount > 0
            ? $" 行ロック{lockedLineCount}件は対象外、文字ロックを含む行{lockedCharacterLineCount}件は固定文字を保持しました。"
            : string.Empty;
        StatusMessage =
            $"対象{pageNumbers.Length}ページの{targetCount}行中{adjustedCount}行を自動調整しました" +
            $"（{preprocessingSummary}）。" +
            (failures.Count > 0 ? $" 判定できない行: {failures.Count}件。" : string.Empty) +
            (pageFailures.Count > 0 ? $" 読み込めないページ: {pageFailures.Count}件。" : string.Empty) +
            lockedSummary;
    }

    private IReadOnlyList<OverlayRegionViewModel> GetCharacterEstimationTargets()
    {
        return GetSelectedCharacterLines()
            .Where(region => region.CanAutomaticallyAdjust)
            .OrderBy(region => region.ReadingOrder)
            .ToArray();
    }

    private IReadOnlyList<OverlayRegionViewModel> GetSelectedCharacterLines()
    {
        // Take a snapshot. Property notifications raised while one line is
        // being adjusted must not change the remaining targets.
        return (_selectedOverlays.Count > 0
                ? _selectedOverlays.ToArray()
                : SelectedOverlay is null ? [] : [SelectedOverlay])
            .Distinct()
            .ToArray();
    }

    private void EstimateCharacterSuffixAdvances()
    {
        if (SelectedOverlay is not { HasSingleCharacterSelection: true } region ||
            PreviewImage is not System.Windows.Media.Imaging.BitmapSource image)
            return;
        var startIndex = region.SelectedCharacterIndex;
        if (startIndex < 0 || startIndex >= region.TextElementCount - 1) return;
        if (!region.HasUnlockedCharacterAtOrAfter(startIndex))
        {
            StatusMessage = "選択文字以降はすべて固定されています。調整する文字の固定を解除してください。";
            return;
        }
        try
        {
            var suffixRegion = region.CreateCharacterSuffixEstimationRegion(startIndex);
            var estimate = CharacterAdvanceEstimator.Estimate(
                image,
                suffixRegion,
                new CharacterAdvanceEstimationOptions(
                    _applicationSettings.CharacterEstimationMinimumAspectRatio,
                    _applicationSettings.CharacterEstimationMaximumAspectRatio,
                    _applicationSettings.CharacterEstimationUniformity,
                    _applicationSettings.CharacterEstimationInkCoverage,
                    _applicationSettings.CharacterEstimationGlyphPrior));
            var changed = false;
            ApplyRegionEdit("選択文字以降の文字幅を自動推定", [region], () =>
            {
                changed = region.ApplyCharacterSuffixAdvanceEstimation(startIndex, estimate);
                if (changed) region.ReviewStatus = ReviewStatus.Modified;
            });
            StatusMessage = changed
                ? $"選択文字以降を調整しました。{estimate.Message}"
                : "画像から再推定しましたが、現在の文字幅との差がないため変更されませんでした。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"選択文字以降を自動調整できませんでした: {ex.Message}";
            _ = _log.WriteAsync(LogLevel.Warning, "character-suffix-estimation.failed", ex.Message, ex);
        }
    }

    private void ApplyParagraphText(string value)
    {
        if (!IsParagraphEditMode || _selectedOverlays.Count == 0) return;
        var ordered = _selectedOverlays.OrderBy(region => region.ReadingOrder).ToArray();
        var lines = value.Replace("\r\n", "\n").Split('\n');
        if (lines.Length != ordered.Length)
        {
            ParagraphEditValidationMessage = $"段落は{ordered.Length}行です。行数を変えずに編集してください。";
            return;
        }
        ParagraphEditValidationMessage = string.Empty;
        ApplyRegionEdit("段落の文字列を変更", ordered, () =>
        {
            for (var index = 0; index < ordered.Length; index++) ordered[index].Text = lines[index];
        });
        OnPropertyChanged(nameof(SelectedParagraphText));
    }

    private static bool BelongsToSameParagraph(OverlayRegionViewModel first, OverlayRegionViewModel second)
    {
        if (first.IsVertical != second.IsVertical || Math.Abs(first.RotationDegrees - second.RotationDegrees) > 5) return false;
        if (first.IsVertical)
        {
            var verticalOverlap = Math.Max(0, Math.Min(first.Top + first.Height, second.Top + second.Height) - Math.Max(first.Top, second.Top));
            var overlapRatio = verticalOverlap / Math.Max(1, Math.Min(first.Height, second.Height));
            var horizontalGap = Math.Max(0, Math.Max(first.Left, second.Left) - Math.Min(first.Left + first.Width, second.Left + second.Width));
            return overlapRatio >= 0.35 && horizontalGap <= Math.Max(first.Width, second.Width) * 2.2;
        }
        var horizontalOverlap = Math.Max(0, Math.Min(first.Left + first.Width, second.Left + second.Width) - Math.Max(first.Left, second.Left));
        var horizontalRatio = horizontalOverlap / Math.Max(1, Math.Min(first.Width, second.Width));
        var verticalGap = Math.Max(0, Math.Max(first.Top, second.Top) - Math.Min(first.Top + first.Height, second.Top + second.Height));
        return horizontalRatio >= 0.35 && verticalGap <= Math.Max(first.Height, second.Height) * 2.2;
    }

    private void EqualizeSelectedWidths()
    {
        if (_selectedOverlays.Count < 2) return;
        var reference = _alignmentReference ?? SelectedOverlay ?? _selectedOverlays[0];
        var targetWidth = reference.Width;
        ApplySelectionEdit("選択領域の幅を統一", region =>
            region.Width = PreviewPixelWidth > 0
                ? Math.Min(targetWidth, Math.Max(4, PreviewPixelWidth - region.Left))
                : targetWidth);
    }

    private void EqualizeSelectedHeights()
    {
        if (_selectedOverlays.Count < 2) return;
        var reference = _alignmentReference ?? SelectedOverlay ?? _selectedOverlays[0];
        var targetHeight = reference.Height;
        ApplySelectionEdit("選択領域の高さを統一", region =>
            region.Height = PreviewPixelHeight > 0
                ? Math.Min(targetHeight, Math.Max(4, PreviewPixelHeight - region.Top))
                : targetHeight);
    }

    /// <summary>
    /// 選択中の全OCR領域を囲む矩形の中心を取得します。
    /// </summary>
    /// <returns>プレビュー座標の中心。選択がない場合は<see langword="null"/>。</returns>
    public (double X, double Y)? GetSelectionCenter()
    {
        if (_selectedOverlays.Count == 0) return null;
        var left = _selectedOverlays.Min(region => region.Left);
        var right = _selectedOverlays.Max(region => region.Left + region.Width);
        var top = _selectedOverlays.Min(region => region.Top);
        var bottom = _selectedOverlays.Max(region => region.Top + region.Height);
        return ((left + right) / 2d, (top + bottom) / 2d);
    }

    /// <summary>選択中の全OCR領域を囲む、プレビュー座標上の矩形を取得します。</summary>
    public (double Left, double Top, double Width, double Height)? GetSelectionBounds()
    {
        if (_selectedOverlays.Count == 0) return null;
        var left = _selectedOverlays.Min(region => region.Left);
        var right = _selectedOverlays.Max(region => region.Left + region.Width);
        var top = _selectedOverlays.Min(region => region.Top);
        var bottom = _selectedOverlays.Max(region => region.Top + region.Height);
        return (left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 選択領域をページ外へ出ない範囲で指定量だけ移動します。
    /// </summary>
    /// <param name="horizontalChange">水平方向の移動ピクセル数。</param>
    /// <param name="verticalChange">垂直方向の移動ピクセル数。</param>
    public void NudgeSelection(double horizontalChange, double verticalChange)
    {
        if (!CanEditGeometry) return;
        var movable = _selectedOverlays.Where(region => !region.IsGeometryLocked).ToArray();
        if (movable.Length == 0 || !double.IsFinite(horizontalChange) || !double.IsFinite(verticalChange)) return;
        var minimumLeft = movable.Min(region => region.Left);
        var maximumRight = movable.Max(region => region.Left + region.Width);
        var minimumTop = movable.Min(region => region.Top);
        var maximumBottom = movable.Max(region => region.Top + region.Height);
        var dx = Math.Max(-minimumLeft, horizontalChange);
        var dy = Math.Max(-minimumTop, verticalChange);
        if (PreviewPixelWidth > 0) dx = Math.Min(dx, PreviewPixelWidth - maximumRight);
        if (PreviewPixelHeight > 0) dy = Math.Min(dy, PreviewPixelHeight - maximumBottom);
        ApplyRegionEdit("選択領域をキー操作で移動", movable, () =>
        {
            foreach (var region in movable)
            {
                region.Left += dx;
                region.Top += dy;
            }
        });
    }

    /// <summary>
    /// ビュー側のマウス・キーボード操作結果をステータスバーへ表示します。
    /// </summary>
    public void StatusMessageForInteraction(string message) => StatusMessage = message;

    private bool CanMoveReadingEarlier() =>
        SelectedOverlay is { IsDeleted: false } selected &&
        OverlayItems.Any(region => !region.IsDeleted && region.ReadingOrder < selected.ReadingOrder);

    private bool CanMoveReadingLater() =>
        SelectedOverlay is { IsDeleted: false } selected &&
        OverlayItems.Any(region => !region.IsDeleted && region.ReadingOrder > selected.ReadingOrder);

    /// <summary>
    /// 現在ページの有効なOCR領域を、利用者が設定した読み順と画面上の安定した登録順で返します。
    /// </summary>
    /// <remarks>
    /// 分割前のデータなどに同じ読み順番号が含まれていても、登録順を第2キーにすることで
    /// 構造編集のたびに並びが入れ替わらないようにします。
    /// </remarks>
    private List<OverlayRegionViewModel> GetActiveRegionsInReadingOrder() =>
        OverlayItems
            .Select((region, index) => (region, index))
            .Where(item => !item.region.IsDeleted)
            .OrderBy(item => item.region.ReadingOrder)
            .ThenBy(item => item.index)
            .Select(item => item.region)
            .ToList();

    /// <summary>
    /// 指定された有効領域へ1から始まる欠番・重複のない読み順番号を割り当てます。
    /// </summary>
    /// <param name="orderedRegions">確定済みの読み順で並んだ領域。</param>
    private static void AssignSequentialReadingOrder(IEnumerable<OverlayRegionViewModel> orderedRegions)
    {
        var readingOrder = 1;
        foreach (var region in orderedRegions.Where(region => !region.IsDeleted))
            region.ReadingOrder = readingOrder++;
    }

    private void MoveSelectedReadingOrder(int direction)
    {
        if (SelectedOverlay is not { } selected) return;
        var ordered = OverlayItems.Where(region => !region.IsDeleted).OrderBy(region => region.ReadingOrder).ToList();
        var index = ordered.IndexOf(selected);
        var otherIndex = index + direction;
        if (index < 0 || otherIndex < 0 || otherIndex >= ordered.Count) return;
        var other = ordered[otherIndex];
        ApplyRegionEdit("読み順を変更", [selected, other], () =>
        {
            (selected.ReadingOrder, other.ReadingOrder) = (other.ReadingOrder, selected.ReadingOrder);
        });
        MoveReadingEarlierCommand.RaiseCanExecuteChanged();
        MoveReadingLaterCommand.RaiseCanExecuteChanged();
    }

    private void RecalculateReadingOrder()
    {
        var affected = OverlayItems.Where(region => !region.IsDeleted).ToArray();
        if (affected.Length == 0) return;
        var ordered = affected
            .OrderBy(region => region.Top)
            .ThenBy(region => region.IsVertical ? -region.Left : region.Left)
            .ToArray();
        ApplyRegionEdit("位置から読み順を再計算", affected, () =>
        {
            for (var index = 0; index < ordered.Length; index++) ordered[index].ReadingOrder = index + 1;
        });
        MoveReadingEarlierCommand.RaiseCanExecuteChanged();
        MoveReadingLaterCommand.RaiseCanExecuteChanged();
    }

    private void SetAlignmentReference() => UpdateAlignmentReference(SelectedOverlay);

    private void UpdateAlignmentReference(OverlayRegionViewModel? reference)
    {
        if (_alignmentReference is not null && !ReferenceEquals(_alignmentReference, reference))
            _alignmentReference.IsAlignmentReference = false;
        foreach (var region in _selectedOverlays) region.IsAlignmentReference = ReferenceEquals(region, reference);
        _alignmentReference = reference;
        OnPropertyChanged(nameof(AlignmentReferenceDescription));
        SetAlignmentReferenceCommand.RaiseCanExecuteChanged();
        MergeSelectedRegionsCommand.RaiseCanExecuteChanged();
    }

    private void AlignSelection(string alignment)
    {
        if (_selectedOverlays.Count < 2) return;
        var left = _selectedOverlays.Min(region => region.Left);
        var right = _selectedOverlays.Max(region => region.Left + region.Width);
        var top = _selectedOverlays.Min(region => region.Top);
        var bottom = _selectedOverlays.Max(region => region.Top + region.Height);
        var centerX = (left + right) / 2d;
        var centerY = (top + bottom) / 2d;
        ApplySelectionEdit("選択領域を整列", region =>
        {
            switch (alignment)
            {
                case "left": region.Left = left; break;
                case "right": region.Left = Math.Max(0, right - region.Width); break;
                case "top": region.Top = top; break;
                case "bottom": region.Top = Math.Max(0, bottom - region.Height); break;
                case "horizontal-center": region.Left = Math.Max(0, centerX - region.Width / 2d); break;
                case "vertical-center": region.Top = Math.Max(0, centerY - region.Height / 2d); break;
            }
        });
    }

    private void ApplySelectionEdit(string description, Action<OverlayRegionViewModel> apply)
    {
        var affected = _selectedOverlays.Where(region => !region.IsGeometryLocked).ToArray();
        if (affected.Length == 0) return;
        ApplyRegionEdit(description, affected, () =>
        {
            foreach (var region in affected) apply(region);
        });
    }

    private void ApplyRegionEdit(string description, IReadOnlyList<OverlayRegionViewModel> affected, Action apply)
    {
        var before = affected.ToDictionary(region => region, region => region.Capture());
        _applyingHistory = true;
        try { apply(); }
        finally { _applyingHistory = false; }
        var changes = affected
            .Select(region => new OverlayRegionChange(region, before[region], region.Capture()))
            .Where(change => change.Before != change.After)
            .ToArray();
        foreach (var change in changes) _lastOverlaySnapshots[change.Region.Id] = change.After;
        if (changes.Length > 0) RecordEdit(new OverlayEdit(changes, description));
    }

    private List<OverlayRegionViewModel> CreatePageOverlayModels(
        int pageNumber,
        IReadOnlyList<PdfTextOverlayRegion> extracted,
        PageMetrics metrics, PdfCorrectoriumProject? sourceProject = null)
    {
        var savedPage = (sourceProject ?? _project)?.Pages.FirstOrDefault(page => page.PageNumber == pageNumber);
        if (savedPage is null || savedPage.TextRegions.Count == 0)
            return extracted.Select((region, index) => new OverlayRegionViewModel(region, index + 1)).ToList();

        var readingOrder = savedPage.ReadingOrder
            .Select((id, index) => (id, order: index + 1))
            .ToDictionary(item => item.id, item => item.order);

        return savedPage.TextRegions.Select((region, index) =>
        {
            var order = readingOrder.GetValueOrDefault(region.Id, index + 1);
            var wordReadings = FormatWordReadings(region.WordReadings);
            var savedVertical = region.WritingMode == WritingMode.Vertical;
            var importedMatch = FindImportedRegion(region, extracted, index, metrics);
            var inferredVertical = importedMatch?.IsVertical == true ||
                                   WritingDirectionDetector.IsLikelyVertical(region.EffectiveText,
                                       region.EditedGeometry.LocalBounds.Size.Width,
                                       region.EditedGeometry.LocalBounds.Size.Height);
            var isVertical = region.HasExplicitWritingMode ? savedVertical : savedVertical || inferredVertical;
            var originalVertical = region.OriginalWritingMode is WritingMode originalMode
                ? originalMode == WritingMode.Vertical
                : isVertical;
            var directionCorrected = !region.HasExplicitWritingMode && isVertical != savedVertical;
            var original = FromPdfGeometry(region.OriginalText, region.OriginalGeometry, metrics, order, wordReadings, originalVertical, region.ReviewStatus, directionCorrected);
            var current = FromPdfGeometry(region.EffectiveText, region.EditedGeometry, metrics, order, wordReadings, isVertical, region.ReviewStatus, directionCorrected);
            return new OverlayRegionViewModel(
                region.Id,
                region.OriginalText,
                original,
                current,
                true,
                isVertical,
                region.OcrProviderId,
                region.Confidence,
                region.IsAdded,
                region.IsDeleted);
        }).ToList();
    }

    /// <summary>
    /// 検索対象ページのOCR領域を作業キャッシュへ読み込みます。プレビュー画像と現在ページは変更しません。
    /// </summary>
    private async Task<List<OverlayRegionViewModel>> EnsurePageOverlaysLoadedForSearchAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pageOverlays.TryGetValue(pageNumber, out var cached)) return cached;
        if (_resolvedPdfPath is null) return [];

        var overlaySession = _overlaySessionVersion;
        var pdfPath = _resolvedPdfPath;
        var result = await _previewService.RenderPageAsync(_resolvedPdfPath, pageNumber, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (overlaySession != _overlaySessionVersion || pdfPath != _resolvedPdfPath)
            throw new OperationCanceledException("Document changed while loading OCR regions.");
        if (_pageOverlays.TryGetValue(pageNumber, out cached)) return cached;
        var metrics = new PageMetrics(
            result.Image.PixelWidth,
            result.Image.PixelHeight,
            result.PageWidthPoints,
            result.PageHeightPoints);
        _pageMetrics[pageNumber] = metrics;
        var companionRegions = _ndlOcrDocument?.GetScaledRegions(
            pageNumber,
            result.Image.PixelWidth,
            result.Image.PixelHeight) ?? [];
        var extracted = companionRegions.Count > 0 ? companionRegions : result.TextRegions;
        var models = CreatePageOverlayModels(pageNumber, extracted, metrics);
        _pageOverlays[pageNumber] = models;
        foreach (var overlay in models) AttachOverlay(overlay);
        return models;
    }

    /// <summary>
    /// 全ページ自動調整用に、ページ画像と同じ座標系のOCR領域を読み込みます。
    /// 現在表示中のページや選択状態は変更しません。
    /// </summary>
    private async Task<(BitmapSource Image, List<OverlayRegionViewModel> Regions)>
        LoadPageForBatchCharacterAdjustmentAsync(
            int pageNumber,
            CancellationToken cancellationToken)
    {
        if (_resolvedPdfPath is null)
            throw new InvalidOperationException("PDFが読み込まれていません。");

        var result = await _previewService.RenderPageAsync(
            _resolvedPdfPath,
            pageNumber,
            cancellationToken: cancellationToken);
        var metrics = new PageMetrics(
            result.Image.PixelWidth,
            result.Image.PixelHeight,
            result.PageWidthPoints,
            result.PageHeightPoints);
        _pageMetrics[pageNumber] = metrics;

        if (_pageOverlays.TryGetValue(pageNumber, out var cached))
            return (result.Image, cached);

        var companionRegions = _ndlOcrDocument?.GetScaledRegions(
            pageNumber,
            result.Image.PixelWidth,
            result.Image.PixelHeight) ?? [];
        var extracted = companionRegions.Count > 0 ? companionRegions : result.TextRegions;
        var models = CreatePageOverlayModels(pageNumber, extracted, metrics);
        _pageOverlays[pageNumber] = models;
        foreach (var overlay in models) AttachOverlay(overlay);
        return (result.Image, models);
    }

    /// <summary>
    /// 定型領域の反映候補を確認するダイアログ向けに、ページ画像を読み込みます。
    /// 現在ページ、選択領域、編集モードは変更しません。
    /// </summary>
    /// <param name="candidate">プレビューする反映候補。</param>
    /// <param name="cancellationToken">画像読込を中断するための通知。</param>
    /// <returns>ページ画像と、その画像のピクセル寸法。</returns>
    public async Task<(BitmapSource? Image, int PixelWidth, int PixelHeight)>
        LoadRepeatedRegionCandidatePreviewAsync(
            RepeatedRegionCandidate candidate,
            CancellationToken cancellationToken = default)
    {
        if (candidate.PageNumber < 1 || candidate.PageNumber > PageItems.Count)
            return (null, 0, 0);

        var (image, _) = await LoadPageForBatchCharacterAdjustmentAsync(
            candidate.PageNumber,
            cancellationToken);
        return (image, image.PixelWidth, image.PixelHeight);
    }

    /// <summary>
    /// 定型領域を実際には変更せず、候補ページへ反映した場合の領域を計算します。
    /// 候補確認画面の「変更前／変更後」比較だけに使用します。
    /// </summary>
    /// <param name="candidate">確認対象の候補。</param>
    /// <param name="options">反映方法と文字列保持方法。</param>
    /// <param name="showAfter">反映後を返す場合は<c>true</c>、現在の領域を返す場合は<c>false</c>。</param>
    /// <returns>ページ画像のピクセル座標で表したプレビュー領域。</returns>
    public IReadOnlyList<RepeatedRegionPreviewRegion> GetRepeatedRegionPreviewRegions(
        RepeatedRegionCandidate candidate,
        RepeatedRegionPropagationOptions options,
        bool showAfter)
    {
        var matches = candidate.MatchedRegions
            .Where(region => !region.IsDeleted)
            .OrderBy(region => region.ReadingOrder)
            .ToArray();
        if (!showAfter)
        {
            return matches.Select(region => ToRepeatedRegionPreview(
                region,
                candidate.IsLocked ? RepeatedRegionPreviewKind.Locked : RepeatedRegionPreviewKind.Existing)).ToArray();
        }

        if (candidate.IsLocked)
        {
            return matches.Select(region =>
                ToRepeatedRegionPreview(region, RepeatedRegionPreviewKind.Locked)).ToArray();
        }

        if (options.Mode == RepeatedRegionPropagationMode.DeleteMatches)
        {
            return matches.Select(region =>
                ToRepeatedRegionPreview(region, RepeatedRegionPreviewKind.Deleted)).ToArray();
        }

        if (SelectedPage is null ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var sourceMetrics) ||
            !_pageMetrics.TryGetValue(candidate.PageNumber, out var targetMetrics))
            return [];

        var sourceRegions = _selectedOverlays
            .Where(region => !region.IsDeleted)
            .OrderBy(region => region.ReadingOrder)
            .ToArray();
        if (sourceRegions.Length == 0) return [];

        var scaleX = targetMetrics.PixelWidth / (double)Math.Max(1, sourceMetrics.PixelWidth);
        var scaleY = targetMetrics.PixelHeight / (double)Math.Max(1, sourceMetrics.PixelHeight);
        var projection = CreateRepeatedRegionProjection(
            sourceRegions,
            matches,
            scaleX,
            scaleY,
            options.PreserveTargetText);

        return projection.Select(segment => new RepeatedRegionPreviewRegion(
            segment.Snapshot.Left,
            segment.Snapshot.Top,
            Math.Max(1, segment.Snapshot.Width),
            Math.Max(1, segment.Snapshot.Height),
            segment.Snapshot.RotationDegrees,
            segment.Snapshot.Text,
            segment.Source.IsVertical,
            segment.CharacterAdvances,
            RepeatedRegionPreviewKind.Replacement)).ToArray();
    }

    private static RepeatedRegionPreviewRegion ToRepeatedRegionPreview(
        OverlayRegionViewModel region,
        RepeatedRegionPreviewKind kind) => new(
            region.Left,
            region.Top,
            Math.Max(1, region.Width),
            Math.Max(1, region.Height),
            region.RotationDegrees,
            region.Text,
            region.IsVertical,
            region.CharacterAdvances.ToArray(),
            kind);

    /// <summary>
    /// 候補ページをメイン画面に表示し、候補に含まれるすべてのOCR領域を選択します。
    /// </summary>
    /// <param name="candidate">確認する反映候補。</param>
    /// <returns>対象ページと領域を表示できた場合は<c>true</c>。</returns>
    public async Task<bool> NavigateToRepeatedRegionCandidateAsync(RepeatedRegionCandidate candidate)
    {
        if (candidate.PageNumber < 1 || candidate.PageNumber > PageItems.Count) return false;

        var page = PageItems[candidate.PageNumber - 1];
        if (!ReferenceEquals(_selectedPage, page) || PreviewImage is null)
        {
            SetCurrentPageWithoutRendering(page);
            await RenderPageAsync(candidate.PageNumber, populatePageList: false);
        }

        if (!_pageOverlays.TryGetValue(candidate.PageNumber, out var regions)) return false;
        var candidateIds = candidate.MatchedRegions.Select(region => region.Id).ToHashSet();
        var matches = regions
            .Where(region => candidateIds.Contains(region.Id) && !region.IsDeleted)
            .OrderBy(region => region.ReadingOrder)
            .ToArray();
        if (matches.Length == 0) return false;

        SetOverlaySelection(matches, matches[0]);
        OcrSearchSelectionRequested?.Invoke(this, matches[0]);
        StatusMessage = $"{candidate.PageNumber}ページの反映候補を表示しました。";
        return true;
    }

    /// <summary>
    /// 現在ページで選択したヘッダー／フッター等と、相対位置および文字列が似ている領域を
    /// 指定ページから検索します。現在の表示ページや選択状態は変更しません。
    /// </summary>
    public async Task<IReadOnlyList<RepeatedRegionCandidate>> FindRepeatedRegionCandidatesAsync(
        RepeatedRegionPropagationOptions options,
        IProgress<RepeatedRegionSearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (SelectedPage is null || _resolvedPdfPath is null)
            throw new InvalidOperationException("PDFを開いてから実行してください。");

        var sourceRegions = _selectedOverlays
            .Where(region => !region.IsDeleted)
            .OrderBy(region => region.ReadingOrder)
            .ToArray();
        if (sourceRegions.Length == 0)
            throw new InvalidOperationException("反映元にするOCR領域を選択してください。");

        var sourcePageNumber = SelectedPage.PageNumber;
        if (!_pageMetrics.TryGetValue(sourcePageNumber, out var sourceMetrics))
            await LoadPageForBatchCharacterAdjustmentAsync(sourcePageNumber, cancellationToken);
        sourceMetrics = _pageMetrics[sourcePageNumber];

        var sourceBounds = GetUnionBounds(sourceRegions);
        var sourceText = string.Concat(sourceRegions.Select(region => region.Text));
        var sourceIsVertical = sourceRegions.Count(region => region.IsVertical) > sourceRegions.Length / 2;
        var candidates = new List<RepeatedRegionCandidate>();

        var targetPages = options.TargetPageNumbers.Distinct().OrderBy(page => page)
            .Where(page => page != sourcePageNumber && page >= 1 && page <= PageItems.Count)
            .ToArray();
        progress?.Report(new RepeatedRegionSearchProgress(0, 0, targetPages.Length, 0));
        var completedPages = 0;
        foreach (var pageNumber in targetPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var (_, pageRegions) = await LoadPageForBatchCharacterAdjustmentAsync(pageNumber, cancellationToken);
                var targetMetrics = _pageMetrics[pageNumber];
                var scaleX = targetMetrics.PixelWidth / (double)Math.Max(1, sourceMetrics.PixelWidth);
                var scaleY = targetMetrics.PixelHeight / (double)Math.Max(1, sourceMetrics.PixelHeight);
                var expected = new RegionBounds(
                    sourceBounds.Left * scaleX,
                    sourceBounds.Top * scaleY,
                    sourceBounds.Width * scaleX,
                    sourceBounds.Height * scaleY);

                var matches = FindRegionsAtExpectedPosition(pageRegions, expected, sourceIsVertical);
                if (matches.Count == 0) continue;

                var targetBounds = GetUnionBounds(matches);
                var targetText = string.Concat(matches.OrderBy(region => region.ReadingOrder).Select(region => region.Text));
                var textScore = CalculateTextSimilarity(sourceText, targetText);
                var geometryScore = CalculateGeometrySimilarity(expected, targetBounds, targetMetrics);
                var orientationScore = matches.Count(region => region.IsVertical == sourceIsVertical) / (double)matches.Count;
                var similarity = 100d * ((geometryScore * 0.62) + (textScore * 0.30) + (orientationScore * 0.08));
                if (similarity + 0.001 < options.MinimumSimilarity) continue;

                candidates.Add(new RepeatedRegionCandidate
                {
                    PageNumber = pageNumber,
                    Similarity = similarity,
                    TargetText = targetText,
                    MatchedRegions = matches,
                    IsLocked = matches.Any(region => region.IsGeometryLocked || region.HasLockedCharacters),
                });
            }
            finally
            {
                completedPages++;
                progress?.Report(new RepeatedRegionSearchProgress(
                    pageNumber, completedPages, targetPages.Length, candidates.Count));
            }
        }

        return candidates;
    }

    /// <summary>
    /// 確認画面で選択された候補へ、参照領域の分割・削除・配置・文字送りを一括反映します。
    /// 複数ページに対する変更は一つのUndo操作として記録されます。
    /// </summary>
    public int ApplyRepeatedRegionPropagation(
        RepeatedRegionPropagationOptions options,
        IReadOnlyList<RepeatedRegionCandidate> candidates)
    {
        if (SelectedPage is null) return 0;
        var sourcePageNumber = SelectedPage.PageNumber;
        var sourceRegions = _selectedOverlays
            .Where(region => !region.IsDeleted)
            .OrderBy(region => region.ReadingOrder)
            .ToArray();
        if (sourceRegions.Length == 0 || !_pageMetrics.TryGetValue(sourcePageNumber, out var sourceMetrics)) return 0;

        var selectedCandidates = candidates.Where(candidate => candidate.IsSelected && !candidate.IsLocked).ToArray();
        if (selectedCandidates.Length == 0) return 0;

        var before = new Dictionary<OverlayRegionViewModel, OverlayRegionSnapshot>();
        var created = new List<(int PageNumber, OverlayRegionViewModel Region)>();
        var appliedPageCount = 0;
        _applyingHistory = true;
        try
        {
            foreach (var candidate in selectedCandidates)
            {
                if (!_pageOverlays.TryGetValue(candidate.PageNumber, out var pageRegions) ||
                    !_pageMetrics.TryGetValue(candidate.PageNumber, out var targetMetrics)) continue;

                var matches = candidate.MatchedRegions
                    .Where(region => pageRegions.Contains(region) && !region.IsDeleted)
                    .ToArray();
                if (matches.Length == 0 || matches.Any(region => region.IsGeometryLocked || region.HasLockedCharacters)) continue;

                foreach (var region in pageRegions) before.TryAdd(region, region.Capture());
                var activeBefore = pageRegions.Where(region => !region.IsDeleted)
                    .OrderBy(region => region.ReadingOrder).ThenBy(region => pageRegions.IndexOf(region)).ToList();
                var insertionIndex = matches.Select(region => activeBefore.IndexOf(region))
                    .Where(index => index >= 0)
                    .DefaultIfEmpty(activeBefore.Count)
                    .Min();
                foreach (var match in matches)
                {
                    match.IsDeleted = true;
                    match.ReviewStatus = ReviewStatus.NeedsReview;
                }

                var replacements = new List<OverlayRegionViewModel>();
                if (options.Mode == RepeatedRegionPropagationMode.ReplaceStructure)
                {
                    var scaleX = targetMetrics.PixelWidth / (double)Math.Max(1, sourceMetrics.PixelWidth);
                    var scaleY = targetMetrics.PixelHeight / (double)Math.Max(1, sourceMetrics.PixelHeight);
                    var projection = CreateRepeatedRegionProjection(
                        sourceRegions,
                        matches.OrderBy(region => region.ReadingOrder).ToArray(),
                        scaleX,
                        scaleY,
                        options.PreserveTargetText);

                    for (var index = 0; index < projection.Count; index++)
                    {
                        var segment = projection[index];
                        var source = segment.Source;
                        var current = segment.Snapshot with
                        {
                            ReadingOrder = insertionIndex + index + 1,
                        };
                        var original = current with { IsDeleted = true };
                        var replacement = new OverlayRegionViewModel(
                            Guid.NewGuid(), current.Text, original, current, source.IsInvisible, source.IsVertical,
                            source.ProviderId, source.Confidence, true, false);
                        AttachOverlay(replacement);
                        pageRegions.Add(replacement);
                        before[replacement] = original;
                        created.Add((candidate.PageNumber, replacement));
                        replacements.Add(replacement);
                    }
                }

                var reordered = activeBefore.Where(region => !matches.Contains(region)).ToList();
                reordered.InsertRange(Math.Min(insertionIndex, reordered.Count), replacements);
                AssignSequentialReadingOrder(reordered);
                appliedPageCount++;
            }

            var changes = before.Select(pair =>
                    new OverlayRegionChange(pair.Key, pair.Value, pair.Key.Capture()))
                .Where(change => change.Before != change.After)
                .ToArray();
            if (changes.Length == 0) return 0;

            foreach (var change in changes) _lastOverlaySnapshots[change.Region.Id] = change.After;
            RecordEdit(new OverlayEdit(changes,
                options.Mode == RepeatedRegionPropagationMode.DeleteMatches
                    ? $"定型領域を{appliedPageCount}ページから削除"
                    : $"定型領域の編集を{appliedPageCount}ページへ反映"));
            return appliedPageCount;
        }
        catch
        {
            foreach (var pair in before) pair.Key.Apply(pair.Value);
            foreach (var item in created)
                if (_pageOverlays.TryGetValue(item.PageNumber, out var pageRegions)) pageRegions.Remove(item.Region);
            throw;
        }
        finally
        {
            _applyingHistory = false;
        }
    }

    private readonly record struct RegionBounds(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
        public double CenterX => Left + (Width / 2d);
        public double CenterY => Top + (Height / 2d);
    }

    private static RegionBounds GetUnionBounds(IReadOnlyCollection<OverlayRegionViewModel> regions)
    {
        var left = regions.Min(region => region.Left);
        var top = regions.Min(region => region.Top);
        var right = regions.Max(region => region.Left + region.Width);
        var bottom = regions.Max(region => region.Top + region.Height);
        return new RegionBounds(left, top, Math.Max(0.1, right - left), Math.Max(0.1, bottom - top));
    }

    private static IReadOnlyList<OverlayRegionViewModel> FindRegionsAtExpectedPosition(
        IEnumerable<OverlayRegionViewModel> pageRegions,
        RegionBounds expected,
        bool sourceIsVertical)
    {
        var paddingX = Math.Max(8d, expected.Width * 0.18);
        var paddingY = Math.Max(8d, expected.Height * 0.35);
        var search = new RegionBounds(expected.Left - paddingX, expected.Top - paddingY,
            expected.Width + (paddingX * 2), expected.Height + (paddingY * 2));
        var candidates = pageRegions.Where(region => !region.IsDeleted)
            .Select(region => (Region: region, Bounds: new RegionBounds(region.Left, region.Top, region.Width, region.Height)))
            .Where(item => Intersects(item.Bounds, search))
            .Where(item => sourceIsVertical
                ? AxisOverlap(item.Bounds.Left, item.Bounds.Right, expected.Left, expected.Right) >= Math.Min(item.Bounds.Width, expected.Width) * 0.35
                : AxisOverlap(item.Bounds.Top, item.Bounds.Bottom, expected.Top, expected.Bottom) >= Math.Min(item.Bounds.Height, expected.Height) * 0.35)
            .OrderBy(item => item.Region.ReadingOrder)
            .ThenBy(item => sourceIsVertical ? item.Bounds.Top : item.Bounds.Left)
            .Select(item => item.Region)
            .ToArray();
        return candidates;
    }

    private static bool Intersects(RegionBounds first, RegionBounds second) =>
        first.Left < second.Right && first.Right > second.Left && first.Top < second.Bottom && first.Bottom > second.Top;

    private static double AxisOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd) =>
        Math.Max(0d, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));

    private static double CalculateGeometrySimilarity(RegionBounds expected, RegionBounds actual, PageMetrics metrics)
    {
        var centerDistance = Math.Sqrt(
            Math.Pow((expected.CenterX - actual.CenterX) / Math.Max(1, metrics.PixelWidth), 2) +
            Math.Pow((expected.CenterY - actual.CenterY) / Math.Max(1, metrics.PixelHeight), 2));
        var positionScore = 1d - Math.Min(1d, centerDistance / 0.12d);
        var widthScore = 1d - Math.Min(1d, Math.Abs(expected.Width - actual.Width) / Math.Max(expected.Width, actual.Width));
        var heightScore = 1d - Math.Min(1d, Math.Abs(expected.Height - actual.Height) / Math.Max(expected.Height, actual.Height));
        return Math.Clamp((positionScore * 0.7) + (widthScore * 0.15) + (heightScore * 0.15), 0d, 1d);
    }

    private static double CalculateTextSimilarity(string first, string second)
    {
        var left = NormalizeRepeatedRegionText(first);
        var right = NormalizeRepeatedRegionText(second);
        if (left.Length == 0 || right.Length == 0) return left.Length == right.Length ? 1d : 0d;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return 1d - (previous[right.Length] / (double)Math.Max(left.Length, right.Length));
    }

    private static string NormalizeRepeatedRegionText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var digitRun = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character)) continue;
            if (char.IsDigit(character))
            {
                if (!digitRun) builder.Append('#');
                digitRun = true;
                continue;
            }
            digitRun = false;
            builder.Append(char.ToUpperInvariant(character));
        }
        return builder.ToString();
    }

    private static string[] SplitTextByReferenceSegments(
        string targetText,
        IReadOnlyList<OverlayRegionViewModel> sourceRegions)
    {
        var targetStarts = StringInfo.ParseCombiningCharacters(targetText);
        var targetElements = targetStarts.Select((start, index) =>
            targetText.Substring(start, (index + 1 < targetStarts.Length ? targetStarts[index + 1] : targetText.Length) - start)).ToArray();
        var sourceCounts = sourceRegions.Select(region => Math.Max(1, StringInfo.ParseCombiningCharacters(region.Text).Length)).ToArray();
        var totalSource = Math.Max(1, sourceCounts.Sum());
        var result = new string[sourceRegions.Count];
        var previousEnd = 0;
        var cumulativeSource = 0;
        for (var index = 0; index < result.Length; index++)
        {
            cumulativeSource += sourceCounts[index];
            var end = index == result.Length - 1
                ? targetElements.Length
                : (int)Math.Round(targetElements.Length * cumulativeSource / (double)totalSource);
            end = Math.Clamp(end, previousEnd, targetElements.Length);
            result[index] = string.Concat(targetElements.Skip(previousEnd).Take(end - previousEnd));
            previousEnd = end;
        }
        return result;
    }

    /// <summary>
    /// 参照ページで確定した分割構造を、候補ページの座標系へ投影します。
    /// プレビューと実際の一括反映が同じ文字列分割・文字送り計算を使うための共通処理です。
    /// </summary>
    private static IReadOnlyList<RepeatedRegionProjectedSegment> CreateRepeatedRegionProjection(
        IReadOnlyList<OverlayRegionViewModel> sourceRegions,
        IReadOnlyList<OverlayRegionViewModel> targetRegions,
        double scaleX,
        double scaleY,
        bool preserveTargetText)
    {
        var targetText = string.Concat(targetRegions.Select(region => region.Text));
        var segmentTexts = preserveTargetText
            ? SplitTextByReferenceSegments(targetText, sourceRegions)
            : sourceRegions.Select(region => region.Text).ToArray();
        var result = new List<RepeatedRegionProjectedSegment>(sourceRegions.Count);

        for (var index = 0; index < sourceRegions.Count; index++)
        {
            var source = sourceRegions[index];
            var sourceSnapshot = source.Capture();
            var text = segmentTexts[index];
            var width = Math.Max(1, sourceSnapshot.Width * scaleX);
            var height = Math.Max(1, sourceSnapshot.Height * scaleY);
            var writingExtent = source.IsVertical ? height : width;
            var textElementCount = StringInfo.ParseCombiningCharacters(text).Length;
            var characterAdvances = ProjectCharacterAdvances(
                source.CharacterAdvances,
                textElementCount,
                writingExtent);
            var current = sourceSnapshot with
            {
                Text = text,
                Left = sourceSnapshot.Left * scaleX,
                Top = sourceSnapshot.Top * scaleY,
                Width = width,
                Height = height,
                ReadingOrder = index + 1,
                WordReadingsText = preserveTargetText ? string.Empty : sourceSnapshot.WordReadingsText,
                CharacterAdvancesText = SerializeCharacterAdvances(characterAdvances),
                ReviewStatus = ReviewStatus.NeedsReview,
                IsDeleted = false,
                IsGeometryLocked = false,
                CharacterLocksText = string.Empty,
            };
            result.Add(new RepeatedRegionProjectedSegment(source, current, characterAdvances));
        }

        return result;
    }

    /// <summary>
    /// 参照領域の文字送り比率を、候補文字列の文字数と領域寸法へ変換します。
    /// 文字数が同じ場合は各文字の比率をそのまま維持し、異なる場合は比率曲線を補間します。
    /// </summary>
    private static IReadOnlyList<double> ProjectCharacterAdvances(
        IReadOnlyList<double> sourceAdvances,
        int targetCount,
        double targetExtent)
    {
        if (targetCount <= 0) return [];
        targetExtent = Math.Max(1, targetExtent);

        var sourceWeights = sourceAdvances
            .Where(value => double.IsFinite(value) && value > 0)
            .ToArray();
        if (sourceWeights.Length == 0)
            return Enumerable.Repeat(targetExtent / targetCount, targetCount).ToArray();

        double[] targetWeights;
        if (sourceWeights.Length == targetCount)
        {
            targetWeights = sourceWeights.ToArray();
        }
        else
        {
            targetWeights = new double[targetCount];
            for (var index = 0; index < targetCount; index++)
            {
                var position = ((index + 0.5) * sourceWeights.Length / targetCount) - 0.5;
                var lower = Math.Clamp((int)Math.Floor(position), 0, sourceWeights.Length - 1);
                var upper = Math.Clamp(lower + 1, 0, sourceWeights.Length - 1);
                var fraction = Math.Clamp(position - Math.Floor(position), 0, 1);
                targetWeights[index] = sourceWeights[lower] +
                    ((sourceWeights[upper] - sourceWeights[lower]) * fraction);
            }
        }

        var totalWeight = targetWeights.Sum();
        if (!double.IsFinite(totalWeight) || totalWeight <= 0)
            return Enumerable.Repeat(targetExtent / targetCount, targetCount).ToArray();

        var projected = targetWeights.Select(weight => targetExtent * weight / totalWeight).ToArray();
        projected[^1] += targetExtent - projected.Sum();
        return projected;
    }

    private static string SerializeCharacterAdvances(IEnumerable<double> advances) =>
        string.Join(";", advances.Select(value =>
            value.ToString("0.###", CultureInfo.InvariantCulture)));

    private sealed record RepeatedRegionProjectedSegment(
        OverlayRegionViewModel Source,
        OverlayRegionSnapshot Snapshot,
        IReadOnlyList<double> CharacterAdvances);

    /// <summary>指定文字列が現れるすべての開始位置を、重複しない順序で返します。</summary>
    internal static IReadOnlyList<int> FindTextOccurrences(
        string source,
        string searchText,
        StringComparison comparison)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(searchText)) return [];
        var positions = new List<int>();
        var searchStart = 0;
        while (searchStart <= source.Length - searchText.Length)
        {
            var index = source.IndexOf(searchText, searchStart, comparison);
            if (index < 0) break;
            positions.Add(index);
            searchStart = index + searchText.Length;
        }
        return positions;
    }

    /// <summary>通常文字列、行ブロック完全一致、正規表現の各条件を共通の検索範囲へ変換します。</summary>
    private static IReadOnlyList<OcrSearchOccurrence> FindSearchOccurrences(
        string source,
        OcrTextSearchOptions options,
        Regex? regularExpression = null)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(options.SearchText)) return [];
        if (options.UseRegularExpression)
        {
            regularExpression ??= CreateSearchRegex(options);
            try
            {
                return regularExpression.Matches(source)
                    .Cast<Match>()
                    .Where(match => match.Success && match.Length > 0)
                    .Where(match => !options.WholeRegionMatch || (match.Index == 0 && match.Length == source.Length))
                    .Select(match => new OcrSearchOccurrence(match.Index, match.Length, match))
                    .ToArray();
            }
            catch (RegexMatchTimeoutException exception)
            {
                throw new InvalidOperationException("正規表現の処理に時間がかかりすぎたため、検索を中止しました。条件を簡潔にしてください。", exception);
            }
        }

        var comparison = options.MatchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        if (options.WholeRegionMatch)
            return string.Equals(source, options.SearchText, comparison)
                ? [new OcrSearchOccurrence(0, source.Length)]
                : [];
        return FindTextOccurrences(source, options.SearchText, comparison)
            .Select(index => new OcrSearchOccurrence(index, options.SearchText.Length))
            .ToArray();
    }

    /// <summary>検索条件から、タイムアウト付きの安全な正規表現を生成します。</summary>
    private static Regex CreateSearchRegex(OcrTextSearchOptions options)
    {
        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.MatchCase) regexOptions |= RegexOptions.IgnoreCase;
        try
        {
            return new Regex(options.SearchText, regexOptions, SearchRegexTimeout);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"正規表現が正しくありません。{exception.Message}", exception);
        }
    }

    /// <summary>正規表現のグループ参照を展開し、実際に適用する置換操作を生成します。</summary>
    private static OcrReplacementOperation CreateReplacementOperation(
        OcrSearchOccurrence occurrence,
        string replacementText)
    {
        try
        {
            return new OcrReplacementOperation(
                occurrence.StartIndex,
                occurrence.Length,
                occurrence.RegularExpressionMatch?.Result(replacementText) ?? replacementText);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"置換文字列のグループ参照が正しくありません。{exception.Message}", exception);
        }
    }

    /// <summary>検索結果一覧用に、一致箇所の前後を短く切り出します。</summary>
    private static string CreateSearchPreview(string source, int startIndex, int length)
    {
        const int contextLength = 18;
        var previewStart = Math.Max(0, startIndex - contextLength);
        var previewEnd = Math.Min(source.Length, startIndex + length + contextLength);
        var prefix = previewStart > 0 ? "…" : string.Empty;
        var suffix = previewEnd < source.Length ? "…" : string.Empty;
        return prefix + source[previewStart..previewEnd].ReplaceLineEndings(" ") + suffix;
    }

    private static PdfTextOverlayRegion? FindImportedRegion(
        OcrTextRegion saved,
        IReadOnlyList<PdfTextOverlayRegion> imported,
        int index,
        PageMetrics metrics)
    {
        if (index < imported.Count && NormalizeComparableText(imported[index].Text) == NormalizeComparableText(saved.OriginalText))
            return imported[index];
        var bounds = saved.OriginalGeometry.LocalBounds;
        var left = bounds.Left / metrics.WidthPoints * metrics.PixelWidth;
        var top = (metrics.HeightPoints - bounds.Top) / metrics.HeightPoints * metrics.PixelHeight;
        return imported
            .Where(candidate => NormalizeComparableText(candidate.Text) == NormalizeComparableText(saved.OriginalText))
            .OrderBy(candidate => Math.Abs(candidate.Left - left) + Math.Abs(candidate.Top - top))
            .FirstOrDefault();
    }

    private static string NormalizeComparableText(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    private static OverlayRegionSnapshot FromPdfGeometry(
        string text,
        TextGeometry geometry,
        PageMetrics metrics,
        int readingOrder,
        string wordReadingsText,
        bool isVertical,
        ReviewStatus reviewStatus,
        bool resetCharacterAdvances = false)
    {
        var bounds = geometry.LocalBounds;
        var textElementCount = System.Globalization.StringInfo.ParseCombiningCharacters(text).Length;
        var screenExtent = isVertical
            ? bounds.Size.Height / metrics.HeightPoints * metrics.PixelHeight
            : bounds.Size.Width / metrics.WidthPoints * metrics.PixelWidth;
        var screenAdvances = (resetCharacterAdvances ? [] : geometry.CharacterAdvances)
            .Select(advance => advance / (isVertical ? metrics.HeightPoints : metrics.WidthPoints) *
                               (isVertical ? metrics.PixelHeight : metrics.PixelWidth))
            .ToArray();
        if (textElementCount > 0)
        {
            if (screenAdvances.Length != textElementCount ||
                screenAdvances.Any(advance => !double.IsFinite(advance) || advance <= 0))
            {
                screenAdvances = Enumerable.Repeat(screenExtent / textElementCount, textElementCount).ToArray();
            }
            else
            {
                // PDF text APIs commonly expose glyph ink bounds rather than
                // advances. Preserve their proportional ratios, but make the
                // cells cover the complete OCR line geometry.
                var sum = screenAdvances.Sum();
                if (!double.IsFinite(sum) || sum <= 0)
                    screenAdvances = Enumerable.Repeat(screenExtent / textElementCount, textElementCount).ToArray();
                else
                {
                    var scale = screenExtent / sum;
                    for (var index = 0; index < screenAdvances.Length; index++)
                        screenAdvances[index] *= scale;
                    screenAdvances[^1] += screenExtent - screenAdvances.Sum();
                }
            }
        }
        return new OverlayRegionSnapshot(
            text,
            bounds.Left / metrics.WidthPoints * metrics.PixelWidth,
            (metrics.HeightPoints - bounds.Top) / metrics.HeightPoints * metrics.PixelHeight,
            bounds.Size.Width / metrics.WidthPoints * metrics.PixelWidth,
            bounds.Size.Height / metrics.HeightPoints * metrics.PixelHeight,
            geometry.RotationDegrees,
            readingOrder,
            wordReadingsText,
            string.Join(';', screenAdvances.Select(advance => advance.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture))),
            reviewStatus,
            IsVertical: isVertical,
            IsGeometryLocked: geometry.IsGeometryLocked,
            CharacterLocksText: string.Join(';', geometry.CharacterLocks.Select(value => value ? "1" : "0")));
    }

    private static TextGeometry ToPdfGeometry(OverlayRegionSnapshot region, PageMetrics metrics, bool isVertical)
    {
        var left = region.Left / metrics.PixelWidth * metrics.WidthPoints;
        var width = region.Width / metrics.PixelWidth * metrics.WidthPoints;
        var height = region.Height / metrics.PixelHeight * metrics.HeightPoints;
        var bottom = metrics.HeightPoints - (region.Top + region.Height) / metrics.PixelHeight * metrics.HeightPoints;
        var bounds = new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(width, height));
        return new TextGeometry
        {
            LocalBounds = bounds,
            RotationCenter = new PdfPoint(left + width / 2, bottom + height / 2),
            RotationDegrees = region.RotationDegrees,
            CharacterAdvances = region.CharacterAdvancesText
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var advance)
                    ? advance / (isVertical ? metrics.PixelHeight : metrics.PixelWidth) * (isVertical ? metrics.HeightPoints : metrics.WidthPoints)
                    : 0)
                .Where(advance => double.IsFinite(advance) && advance > 0)
                .ToArray(),
            IsGeometryLocked = region.IsGeometryLocked,
            CharacterLocks = region.CharacterLocksText
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value is "1" or "true" or "True")
                .ToArray(),
        };
    }

    private void SynchronizeProjectPages()
    {
        if (_project is null) return;
        SynchronizeBookmarks();
        var pages = _project.Pages.ToDictionary(page => page.PageNumber);
        foreach (var (pageNumber, overlays) in _pageOverlays)
        {
            if (!_pageMetrics.TryGetValue(pageNumber, out var metrics)) continue;
            var existing = pages.GetValueOrDefault(pageNumber);
            var existingRegions = existing?.TextRegions.ToDictionary(region => region.Id);
            var pageId = existing?.Id ?? Guid.NewGuid();
            pages[pageNumber] = new OcrPage
            {
                Id = pageId,
                PageNumber = pageNumber,
                WidthPoints = metrics.WidthPoints,
                HeightPoints = metrics.HeightPoints,
                RotationDegrees = existing?.RotationDegrees ?? 0,
                TextRegions = overlays
                    .Where(overlay => !(overlay.IsAdded && overlay.IsDeleted))
                    .Select(overlay => (existingRegions?.GetValueOrDefault(overlay.Id) ?? new OcrTextRegion
                    {
                        OriginalGeometry = ToPdfGeometry(overlay.Original, metrics, overlay.Original.IsVertical ?? overlay.IsVertical),
                        EditedGeometry = ToPdfGeometry(overlay.Capture(), metrics, overlay.IsVertical),
                    }) with
                {
                    Id = overlay.Id,
                    PageId = pageId,
                    OriginalText = overlay.OriginalText,
                    EditedText = overlay.Text == overlay.OriginalText ? string.Empty : overlay.Text,
                    HasEditedText = overlay.Text != overlay.OriginalText,
                    OriginalGeometry = ToPdfGeometry(overlay.Original, metrics, overlay.Original.IsVertical ?? overlay.IsVertical),
                    EditedGeometry = ToPdfGeometry(overlay.Capture(), metrics, overlay.IsVertical),
                    OriginalWritingMode = existingRegions?.GetValueOrDefault(overlay.Id) is { } originalRegion
                        ? originalRegion.OriginalWritingMode
                        : overlay.Original.IsVertical == true ? WritingMode.Vertical : WritingMode.Horizontal,
                    WritingMode = overlay.IsVertical ? WritingMode.Vertical : WritingMode.Horizontal,
                    HasExplicitWritingMode = existingRegions?.GetValueOrDefault(overlay.Id) is not { } savedRegion ||
                        savedRegion.HasExplicitWritingMode ||
                        overlay.IsVertical != overlay.LoadedIsVertical,
                    FlowDirection = existingRegions?.GetValueOrDefault(overlay.Id) is { } directionRegion &&
                        overlay.IsVertical == overlay.LoadedIsVertical
                            ? directionRegion.FlowDirection
                            : overlay.IsVertical ? TextFlowDirection.TopToBottom : TextFlowDirection.LeftToRight,
                    ReviewStatus = overlay.ReviewStatus,
                    OcrProviderId = overlay.ProviderId,
                    Confidence = overlay.Confidence,
                    IsAdded = overlay.IsAdded,
                    IsDeleted = overlay.IsDeleted,
                    WordReadings = ParseWordReadings(overlay.WordReadingsText),
                }).ToArray(),
                ReadingOrder = overlays
                    .Where(overlay => !overlay.IsDeleted)
                    .OrderBy(overlay => overlay.ReadingOrder)
                    .Select(overlay => overlay.Id)
                    .ToArray(),
                RubyRegions = existing?.RubyRegions ?? [],
                ImageOptimization = existing?.ImageOptimization,
            };
        }
        _project = _project with { Pages = pages.Values.OrderBy(page => page.PageNumber).ToArray() };
    }

    private static IReadOnlyList<WordReading> ParseWordReadings(string value)
    {
        var readings = new List<WordReading>();
        var lineNumber = 0;
        foreach (var rawLine in value.Replace("\r\n", "\n").Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
                throw new InvalidDataException($"読み方設定の{lineNumber}行目は「表記=よみ」で入力してください。");
            readings.Add(new WordReading
            {
                SurfaceText = line[..separator].Trim(),
                ReadingText = line[(separator + 1)..].Trim(),
            });
        }
        return readings;
    }

    private static string FormatWordReadings(IReadOnlyList<WordReading> readings) =>
        string.Join(Environment.NewLine, readings.Select(reading => $"{reading.SurfaceText}={reading.ReadingText}"));

    private void AttachOverlay(OverlayRegionViewModel overlay)
    {
        _lastOverlaySnapshots[overlay.Id] = overlay.Capture();
        overlay.PropertyChanged += OnOverlayPropertyChanged;
    }

    private void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applyingHistory || sender is not OverlayRegionViewModel region || e.PropertyName == nameof(OverlayRegionViewModel.IsModified)) return;
        if ((e.PropertyName is nameof(OverlayRegionViewModel.Text) or nameof(OverlayRegionViewModel.Left) or nameof(OverlayRegionViewModel.Top) or
            nameof(OverlayRegionViewModel.Width) or nameof(OverlayRegionViewModel.Height) or nameof(OverlayRegionViewModel.RotationDegrees) or
            nameof(OverlayRegionViewModel.ReadingOrder) or nameof(OverlayRegionViewModel.WordReadingsText) or
            nameof(OverlayRegionViewModel.SelectedCharacterAdvance) or nameof(OverlayRegionViewModel.IsVertical) or
            nameof(OverlayRegionViewModel.IsGeometryLocked) or nameof(OverlayRegionViewModel.HasLockedCharacters)) &&
            (!_lastOverlaySnapshots.TryGetValue(region.Id, out var previousSnapshot) || previousSnapshot != region.Capture()))
        {
            _applyingHistory = true;
            try { region.ReviewStatus = ReviewStatus.Modified; }
            finally { _applyingHistory = false; }
        }
        if (ReferenceEquals(region, SelectedOverlay) && e.PropertyName is nameof(OverlayRegionViewModel.Text) or
            nameof(OverlayRegionViewModel.SelectedCharacterIndex) or nameof(OverlayRegionViewModel.SelectedCharacterAdvance) or
            nameof(OverlayRegionViewModel.Width) or nameof(OverlayRegionViewModel.Height) or nameof(OverlayRegionViewModel.IsVertical) or
            nameof(OverlayRegionViewModel.IsGeometryLocked) or nameof(OverlayRegionViewModel.HasLockedCharacters) or
            nameof(OverlayRegionViewModel.AreSelectedCharactersLocked) or nameof(OverlayRegionViewModel.HasLockedSelectedCharacters))
        {
            NotifyCharacterSelectionState();
            OnPropertyChanged(nameof(SelectedReviewStatus));
            OnPropertyChanged(nameof(SelectedWritingMode));
            RaiseCharacterAdvanceCommands();
        }
        if (_selectedOverlays.Contains(region) && e.PropertyName == nameof(OverlayRegionViewModel.Text))
            OnPropertyChanged(nameof(SelectedParagraphText));
        if (e.PropertyName == nameof(OverlayRegionViewModel.IsDeleted))
        {
            UpdateOverlaySummary();
            DeleteOcrRegionsCommand.RaiseCanExecuteChanged();
        }
        var current = region.Capture();
        if (_batchedRegion == region)
        {
            _lastOverlaySnapshots[region.Id] = current;
            StatusMessage = "OCR領域の配置を変更しました。";
            return;
        }
        if (!_lastOverlaySnapshots.TryGetValue(region.Id, out var before) || before == current) return;
        if (ReferenceEquals(region, _alignmentReference)) OnPropertyChanged(nameof(AlignmentReferenceDescription));
        _lastOverlaySnapshots[region.Id] = current;
        RecordEdit(new OverlayEdit([new OverlayRegionChange(region, before, current)], e.PropertyName == nameof(OverlayRegionViewModel.Text) ? "OCR文字列を変更" : "OCR領域を変更"));
    }

    private void RecordEdit(OverlayEdit edit)
    {
        edit = edit with
        {
            BeforeStateId = _currentEditStateId,
            AfterStateId = ++_nextEditStateId,
        };
        _undo.Push(edit);
        _redo.Clear();
        SetCurrentEditState(edit.AfterStateId);
        TrimUndoHistory();
        StatusMessage = edit.Description;
        NotifyHistoryState();
    }

    private void Undo()
    {
        if (!_undo.TryPop(out var edit)) return;
        foreach (var change in edit.Changes) ApplyHistory(change.Region, change.Before);
        _redo.Push(edit);
        SetCurrentEditState(edit.BeforeStateId);
        StatusMessage = $"元に戻す: {edit.Description}";
        NotifyCharacterSelectionState();
        NotifyHistoryState();
    }

    private void Redo()
    {
        if (!_redo.TryPop(out var edit)) return;
        foreach (var change in edit.Changes) ApplyHistory(change.Region, change.After);
        _undo.Push(edit);
        SetCurrentEditState(edit.AfterStateId);
        StatusMessage = $"やり直す: {edit.Description}";
        NotifyCharacterSelectionState();
        NotifyHistoryState();
    }

    private void ApplyHistory(OverlayRegionViewModel region, OverlayRegionSnapshot snapshot)
    {
        _applyingHistory = true;
        try
        {
            region.Apply(snapshot);
            _lastOverlaySnapshots[region.Id] = snapshot;
            // 背景ページを含む一括編集のUndoでは、表示していないページの領域を
            // 現在ページの選択対象にしないようにします。
            if (OverlayItems.Contains(region))
            {
                if (IsReviewMode) SetOverlaySelection(snapshot.IsDeleted ? [] : [region], snapshot.IsDeleted ? null : region);
                else SelectedOverlay = snapshot.IsDeleted ? null : region;
            }
            OnPropertyChanged(nameof(SelectedReviewStatus));
            OnPropertyChanged(nameof(SelectedWritingMode));
        }
        finally { _applyingHistory = false; }
        MoveReadingEarlierCommand.RaiseCanExecuteChanged();
        MoveReadingLaterCommand.RaiseCanExecuteChanged();
        DeleteOcrRegionsCommand.RaiseCanExecuteChanged();
        RecalculateReadingOrderCommand.RaiseCanExecuteChanged();
        RaiseCharacterAdvanceCommands();
        UpdateOverlaySummary();
    }

    private void UpdateOverlaySummary()
    {
        RefreshReviewItems();
        var active = OverlayItems.Count(region => !region.IsDeleted);
        var deleted = OverlayItems.Count - active;
        OverlaySummary = deleted == 0
            ? $"文字領域: {active}件"
            : $"文字領域: {active}件（削除予定: {deleted}件）";
    }

    private void NotifyHistoryState()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        RefreshReviewItems();
    }

    private void TrimUndoHistory()
    {
        var limit = _applicationSettings.UndoHistoryLimit;
        if (_undo.Count <= limit) return;
        var retained = _undo.Take(limit).Reverse().ToArray();
        _undo.Clear();
        foreach (var item in retained) _undo.Push(item);
        NotifyHistoryState();
    }

    private void SetCurrentEditState(long stateId)
    {
        if (_currentEditStateId == stateId) return;
        _currentEditStateId = stateId;
        NotifyUserActivity();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void MarkSavedState()
    {
        _savedEditStateId = _currentEditStateId;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void MarkNonUndoableChange() => SetCurrentEditState(++_nextEditStateId);

    private void ResetEditState()
    {
        _lastAutoSavedEditStateId = -1;
        NotifyUserActivity();
        _currentEditStateId = 0;
        _savedEditStateId = 0;
        _nextEditStateId = 0;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void ClearOverlaySession()
    {
        _overlaySessionVersion++;
        CancelReviewNavigation();
        foreach (var overlay in _pageOverlays.Values.SelectMany(page => page))
            overlay.PropertyChanged -= OnOverlayPropertyChanged;
        _pageOverlays.Clear();
        _pageMetrics.Clear();
        _lastOverlaySnapshots.Clear();
        _undo.Clear();
        _redo.Clear();
        _selectedOverlays.Clear();
        _alignmentReference = null;
        _batchedRegion = null;
        _batchStart = null;
        OverlayItems.Clear();
        RecalculateReadingOrderCommand.RaiseCanExecuteChanged();
        SelectedOverlay = null;
        OnPropertyChanged(nameof(SelectedOverlayCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(HasOverlaySelection));
        OnPropertyChanged(nameof(AlignmentReferenceDescription));
        RaiseMultiSelectionCommands();
        RaiseCharacterAdvanceCommands();
        DeleteOcrRegionsCommand.RaiseCanExecuteChanged();
        NotifyHistoryState();
    }

    private void RaiseMultiSelectionCommands()
    {
        EqualWidthCommand.RaiseCanExecuteChanged();
        EqualHeightCommand.RaiseCanExecuteChanged();
        AlignLeftCommand.RaiseCanExecuteChanged();
        AlignRightCommand.RaiseCanExecuteChanged();
        AlignTopCommand.RaiseCanExecuteChanged();
        AlignBottomCommand.RaiseCanExecuteChanged();
        AlignHorizontalCenterCommand.RaiseCanExecuteChanged();
        AlignVerticalCenterCommand.RaiseCanExecuteChanged();
        SetAlignmentReferenceCommand.RaiseCanExecuteChanged();
    }

    private void RaiseCharacterAdvanceCommands()
    {
        EqualizeCharacterAdvancesCommand.RaiseCanExecuteChanged();
        RestoreOriginalCharacterAdvancesCommand.RaiseCanExecuteChanged();
        EstimateCharacterAdvancesCommand.RaiseCanExecuteChanged();
        EstimateCharacterSuffixAdvancesCommand.RaiseCanExecuteChanged();
        PreviousCharacterCommand.RaiseCanExecuteChanged();
        NextCharacterCommand.RaiseCanExecuteChanged();
        DecreaseCharacterAdvanceCommand.RaiseCanExecuteChanged();
        IncreaseCharacterAdvanceCommand.RaiseCanExecuteChanged();
        SplitRegionAtSelectedCharacterCommand.RaiseCanExecuteChanged();
        MergeSelectedRegionsCommand.RaiseCanExecuteChanged();
        ToggleSelectedCharacterLockCommand.RaiseCanExecuteChanged();
        ToggleGeometryLockCommand.RaiseCanExecuteChanged();
        DecreaseLineCharacterSizeCommand.RaiseCanExecuteChanged();
        IncreaseLineCharacterSizeCommand.RaiseCanExecuteChanged();
    }

    private static string Abbreviate(string value) => value.Length <= 18 ? value : value[..18] + "…";

    private static string DisplayShortcut(string value) =>
        string.IsNullOrWhiteSpace(value) ? "割り当てなし" : value;

    private static Brush CreateBrush(string value, double opacity = 1)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
            {
                var brush = new SolidColorBrush(color) { Opacity = Math.Clamp(opacity, 0, 1) };
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException) { }
        catch (NotSupportedException) { }
        return Brushes.Transparent;
    }

    private async Task ShowErrorAsync(string message, Exception exception)
    {
        StatusMessage = message;
        IsPreviewLoading = false;
        await _log.WriteAsync(LogLevel.Error, "ui.error", message, exception);
        if (ErrorDialogOverride is { } showError) showError(message, exception);
        else MessageBox.Show($"{message}\n\n{exception.Message}", "PDF Correctorium", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    internal Task ReportStartupFileErrorAsync(Exception exception) => ShowErrorAsync("指定したファイルを開けませんでした。", exception);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
