using System.Text.Json;
using System.IO;
using System.Windows.Media;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// 画面表示、編集補助、履歴、およびショートカットに関する利用者設定です。
/// </summary>
/// <remarks>
/// 設定は不正値や旧バージョンの値を受け取る可能性があるため、利用前に
/// <see cref="Normalize"/> で安全な範囲へ正規化します。
/// </remarks>
public sealed record ApplicationSettings
{
    public const int CurrentFormatVersion = 13;
    /// <summary>最近開いたファイルの表示件数。0は表示と新規記録を停止します。</summary>
    public int RecentFileLimit { get; init; } = 10;
    /// <summary>設定ファイルの移行判定に使用する形式バージョンです。</summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;
    /// <summary>文書や編集状態を含まない、名前付きパネル配置です。</summary>
    public IReadOnlyList<WorkspacePreset> WorkspacePresets { get; init; } = [];
    /// <summary>画面表示に使用する言語コードです。</summary>
    public string UiLanguage { get; init; } = LocalizationService.JapaneseLanguage;
    /// <summary>ツールバーにアイコンだけでなく説明文字も表示するかを指定します。</summary>
    public bool ShowToolbarText { get; init; }
    /// <summary>ツールバーボタンとアイコンのサイズ基準（DIP）。コンパクト表示ではボタン外周の余白を4DIP詰めます。</summary>
    public double ToolbarButtonSize { get; init; } = 36;
    /// <summary>プロパティ欄に長い操作説明を表示するかを指定します。</summary>
    public bool ShowPropertyHelpText { get; init; }
    /// <summary>左側のページ一覧を表示するかを指定します。</summary>
    public bool ShowPageListPanel { get; init; } = true;
    /// <summary>右側のOCRプロパティ欄を表示するかを指定します。</summary>
    public bool ShowPropertiesPanel { get; init; } = true;
    /// <summary>画面下部の状態・倍率表示を表示するかを指定します。</summary>
    public bool ShowStatusBar { get; init; } = true;
    /// <summary>ページ一覧パネルの幅をDIP単位で保持します。</summary>
    public double PageListWidth { get; init; } = 230;
    /// <summary>OCRプロパティパネルの幅をDIP単位で保持します。</summary>
    public double PropertiesPanelWidth { get; init; } = 320;
    /// <summary>ページ番号だけでなくページ画像のサムネイルも生成するかを指定します。</summary>
    public bool ShowPageThumbnails { get; init; }
    /// <summary>ページ一覧に表示するサムネイルの横幅をDIP単位で保持します。</summary>
    public double PageThumbnailSize { get; init; } = 150;
    /// <summary>OCR文字、領域枠、半透明塗りに使う基準色です。</summary>
    public string OcrOverlayColor { get; init; } = "#C40000";
    /// <summary>OCR領域を重ねる塗りの不透明度です。</summary>
    public double OcrOverlayOpacity { get; init; } = 0.22;
    /// <summary>文字編集時に未選択の文字セル境界も表示するかを指定します。</summary>
    public bool ShowUnselectedCharacterCellBorders { get; init; } = true;
    /// <summary>文字編集時の文字セル枠を、画面上に描く基準太さです。</summary>
    public double CharacterCellBorderThickness { get; init; } = 0.8;
    /// <summary>文字送り調整ハンドルに使用する色です。</summary>
    public string CharacterHandleColor { get; init; } = "#F57C00";
    /// <summary>文字送り調整ハンドルの太さです。</summary>
    public double CharacterHandleThickness { get; init; } = 4;
    /// <summary>文字送り調整ハンドルの不透明度です。</summary>
    public double CharacterHandleOpacity { get; init; } = 0.55;
    /// <summary>OCR領域リサイズハンドルの塗り色です。</summary>
    public string ResizeHandleFillColor { get; init; } = "#F7F7F7";
    /// <summary>OCR領域リサイズハンドルの枠線色です。</summary>
    public string ResizeHandleBorderColor { get; init; } = "#B42318";
    /// <summary>OCR領域リサイズハンドルの画面上の大きさです。</summary>
    public double ResizeHandleSize { get; init; } = 9;
    /// <summary>OCR領域リサイズハンドルの不透明度です。</summary>
    public double ResizeHandleOpacity { get; init; } = 0.85;
    /// <summary>メモリ上に保持するUndo履歴の最大件数です。</summary>
    public int UndoHistoryLimit { get; init; } = 100;
    /// <summary>未保存の編集を復旧用プロジェクトへ定期保存するかを指定します。</summary>
    public bool AutoSaveEnabled { get; init; } = true;
    /// <summary>自動保存を実行する最短間隔（分）です。</summary>
    public int AutoSaveIntervalMinutes { get; init; } = 5;
    /// <summary>通常保存時に保持する世代バックアップ数です。</summary>
    public int BackupGenerationCount { get; init; } = 5;
    /// <summary>自動文字分割で許容する文字セルの最小縦横比です。</summary>
    public double CharacterEstimationMinimumAspectRatio { get; init; } = 0.20;
    /// <summary>自動文字分割で許容する文字セルの最大縦横比です。</summary>
    public double CharacterEstimationMaximumAspectRatio { get; init; } = 1.65;
    /// <summary>極端に不揃いなセルを避け、均等配置へ寄せる強さです。</summary>
    public double CharacterEstimationUniformity { get; init; } = 0.35;
    /// <summary>文字境界候補として必要とする画像上のインク量です。</summary>
    public double CharacterEstimationInkCoverage { get; init; } = 0.12;
    /// <summary>句読点や仮名など、認識文字の字形知識を推定へ反映する強さです。</summary>
    public double CharacterEstimationGlyphPrior { get; init; } = 0.70;
    /// <summary>文字編集で前の文字へ移動するキー割り当てです。</summary>
    public string PreviousCharacterShortcut { get; init; } = "Alt+Left";
    /// <summary>文字編集で次の文字へ移動するキー割り当てです。</summary>
    public string NextCharacterShortcut { get; init; } = "Alt+Right";
    /// <summary>選択文字の送り量を狭めるキー割り当てです。</summary>
    public string DecreaseCharacterAdvanceShortcut { get; init; } = "Alt+Down";
    /// <summary>選択文字の送り量を広げるキー割り当てです。</summary>
    public string IncreaseCharacterAdvanceShortcut { get; init; } = "Alt+Up";
    /// <summary>選択行全体の文字送りを画像から再推定するキー割り当てです。</summary>
    public string EstimateCharacterAdvancesShortcut { get; init; } = "Ctrl+Shift+A";
    /// <summary>選択文字から行末までを画像から再推定するキー割り当てです。</summary>
    public string EstimateCharacterSuffixAdvancesShortcut { get; init; } = "Ctrl+Shift+Right";
    /// <summary>文字送りを等分へ戻すキー割り当てです。</summary>
    public string EqualizeCharacterAdvancesShortcut { get; init; } = "Ctrl+Shift+E";
    /// <summary>取込時の文字送りへ復元するキー割り当てです。</summary>
    public string RestoreOriginalCharacterAdvancesShortcut { get; init; } = "Ctrl+Shift+R";

    /// <summary>
    /// 数値範囲、色、ショートカットを検証し、利用可能な設定のコピーを返します。
    /// </summary>
    public ApplicationSettings Normalize() => this with
    {
        FormatVersion = CurrentFormatVersion,
        RecentFileLimit = Math.Clamp(RecentFileLimit, 0, RecentFilesService.MaximumCount),
        WorkspacePresets = WorkspacePreset.NormalizeList(WorkspacePresets),
        UiLanguage = string.Equals(UiLanguage, LocalizationService.EnglishLanguage, StringComparison.OrdinalIgnoreCase)
            ? LocalizationService.EnglishLanguage
            : LocalizationService.JapaneseLanguage,
        ToolbarButtonSize = Math.Clamp(ToolbarButtonSize, 28, 64),
        PageListWidth = Math.Clamp(PageListWidth, 160, 420),
        PropertiesPanelWidth = Math.Clamp(PropertiesPanelWidth, 240, 600),
        PageThumbnailSize = Math.Clamp(PageThumbnailSize, 72, 220),
        OcrOverlayColor = NormalizeColor(OcrOverlayColor, "#C40000"),
        OcrOverlayOpacity = Math.Clamp(OcrOverlayOpacity, 0.05, 1),
        CharacterCellBorderThickness = Math.Clamp(CharacterCellBorderThickness, 0.25, 2.0),
        CharacterHandleColor = NormalizeColor(CharacterHandleColor, "#F57C00"),
        CharacterHandleThickness = Math.Clamp(CharacterHandleThickness, 2, 10),
        CharacterHandleOpacity = Math.Clamp(CharacterHandleOpacity, 0.15, 1),
        ResizeHandleFillColor = NormalizeColor(ResizeHandleFillColor, "#F7F7F7"),
        ResizeHandleBorderColor = NormalizeColor(ResizeHandleBorderColor, "#B42318"),
        ResizeHandleSize = Math.Clamp(ResizeHandleSize, 5, 18),
        ResizeHandleOpacity = Math.Clamp(ResizeHandleOpacity, 0.2, 1),
        UndoHistoryLimit = Math.Clamp(UndoHistoryLimit, 10, 1000),
        AutoSaveIntervalMinutes = Math.Clamp(AutoSaveIntervalMinutes, 1, 120),
        BackupGenerationCount = Math.Clamp(BackupGenerationCount, 1, 20),
        CharacterEstimationMinimumAspectRatio = Math.Clamp(CharacterEstimationMinimumAspectRatio, 0.05, 0.60),
        CharacterEstimationMaximumAspectRatio = Math.Clamp(CharacterEstimationMaximumAspectRatio, 0.75, 4.00),
        CharacterEstimationUniformity = Math.Clamp(CharacterEstimationUniformity, 0, 1),
        CharacterEstimationInkCoverage = Math.Clamp(CharacterEstimationInkCoverage, 0.02, 0.50),
        CharacterEstimationGlyphPrior = Math.Clamp(CharacterEstimationGlyphPrior, 0, 1),
        PreviousCharacterShortcut = EditorShortcutService.NormalizeOrDefault(PreviousCharacterShortcut, "Alt+Left"),
        NextCharacterShortcut = EditorShortcutService.NormalizeOrDefault(NextCharacterShortcut, "Alt+Right"),
        DecreaseCharacterAdvanceShortcut = EditorShortcutService.NormalizeOrDefault(DecreaseCharacterAdvanceShortcut, "Alt+Down"),
        IncreaseCharacterAdvanceShortcut = EditorShortcutService.NormalizeOrDefault(IncreaseCharacterAdvanceShortcut, "Alt+Up"),
        EstimateCharacterAdvancesShortcut = EditorShortcutService.NormalizeOrDefault(EstimateCharacterAdvancesShortcut, "Ctrl+Shift+A"),
        EstimateCharacterSuffixAdvancesShortcut = EditorShortcutService.NormalizeOrDefault(EstimateCharacterSuffixAdvancesShortcut, "Ctrl+Shift+Right"),
        EqualizeCharacterAdvancesShortcut = EditorShortcutService.NormalizeOrDefault(EqualizeCharacterAdvancesShortcut, "Ctrl+Shift+E"),
        RestoreOriginalCharacterAdvancesShortcut = EditorShortcutService.NormalizeOrDefault(RestoreOriginalCharacterAdvancesShortcut, "Ctrl+Shift+R"),
    };

    /// <summary>
    /// WPFの色コンバーターで解釈できる色文字列かを判定します。
    /// </summary>
    /// <param name="value">色名または16進色表現。</param>
    /// <returns>画面表示に使用できる場合は<see langword="true"/>。</returns>
    public static bool IsValidColor(string? value)
    {
        try { return ColorConverter.ConvertFromString(value) is Color; }
        catch (FormatException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static string NormalizeColor(string? value, string fallback) =>
        IsValidColor(value) ? value!.Trim().ToUpperInvariant() : fallback;
}

/// <summary>
/// Portableまたはインストールモードの保存先からアプリ設定を永続化します。
/// </summary>
public sealed class ApplicationSettingsService
{
    /// <summary>設定ファイルを人が確認しやすい形式で保存するJSON設定です。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public ApplicationSettingsService(ApplicationPaths paths) =>
        SettingsPath = Path.Combine(paths.ConfigurationDirectory, "settings.json");

    public string SettingsPath { get; }

    /// <summary>
    /// 設定ファイルを読み込み、正規化済みの設定を返します。
    /// </summary>
    /// <returns>保存済み設定。未作成または読込不能の場合は既定設定。</returns>
    public ApplicationSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new ApplicationSettings();
            var json = File.ReadAllText(SettingsPath);
            return (JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions) ?? new ApplicationSettings()).Normalize();
        }
        catch (IOException) { return new ApplicationSettings(); }
        catch (UnauthorizedAccessException) { return new ApplicationSettings(); }
        catch (JsonException) { return new ApplicationSettings(); }
    }

    /// <summary>
    /// 正規化した設定をJSONとして保存します。
    /// </summary>
    /// <param name="settings">保存対象の設定。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await SettingsTransferService.WriteAtomicallyAsync(SettingsPath,
            JsonSerializer.Serialize(normalized, JsonOptions), cancellationToken).ConfigureAwait(false);
    }
}
