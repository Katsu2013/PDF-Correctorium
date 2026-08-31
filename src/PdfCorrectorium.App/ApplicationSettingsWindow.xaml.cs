using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

/// <summary>
/// 画面表示、編集ハンドル、文字推定、およびショートカット設定を編集するダイアログです。
/// </summary>
public partial class ApplicationSettingsWindow : Window
{
    private readonly ObservableCollection<WorkspacePreset> _workspacePresets = [];
    private readonly int _recentFileCount;

    public ApplicationSettingsWindow(ApplicationSettings settings, string storageMode, string settingsPath, int recentFileCount = 0)
    {
        InitializeComponent();
        _recentFileCount = recentFileCount;
        ResultSettings = settings.Normalize();
        StorageModeTextBlock.Text = storageMode;
        SettingsPathTextBox.Text = settingsPath;
        WorkspacePresetComboBox.ItemsSource = _workspacePresets;
        LoadDraft(ResultSettings);
        LocalizationService.Apply(this);
    }

    public ApplicationSettings ResultSettings { get; private set; }
    public bool ClearRecentFilesRequested { get; private set; }
    internal Func<string, bool>? ConfirmManagementAction { get; set; }
    internal Action<string>? ManagementMessageOverride { get; set; }

    private void Populate(ApplicationSettings settings)
    {
        RecentFileLimitTextBox.Text = settings.RecentFileLimit.ToString(CultureInfo.InvariantCulture);
        UpdateRecentFilesStatus();
        UiLanguageComboBox.SelectedValue = settings.UiLanguage;
        ToolbarDisplayModeComboBox.SelectedIndex = settings.ShowToolbarText ? 1 : 0;
        ToolbarButtonSizeSlider.Value = settings.ToolbarButtonSize;
        ShowPropertyHelpTextCheckBox.IsChecked = settings.ShowPropertyHelpText;
        ShowPageListPanelCheckBox.IsChecked = settings.ShowPageListPanel;
        ShowPropertiesPanelCheckBox.IsChecked = settings.ShowPropertiesPanel;
        ShowStatusBarCheckBox.IsChecked = settings.ShowStatusBar;
        PageListWidthSlider.Value = settings.PageListWidth;
        PropertiesPanelWidthSlider.Value = settings.PropertiesPanelWidth;
        OcrOverlayColorTextBox.Text = settings.OcrOverlayColor;
        OcrOverlayOpacitySlider.Value = settings.OcrOverlayOpacity * 100;
        ShowUnselectedCharacterCellBordersCheckBox.IsChecked = settings.ShowUnselectedCharacterCellBorders;
        CharacterCellBorderThicknessSlider.Value = settings.CharacterCellBorderThickness;
        ShowPageThumbnailsCheckBox.IsChecked = settings.ShowPageThumbnails;
        CharacterHandleColorTextBox.Text = settings.CharacterHandleColor;
        CharacterHandleThicknessSlider.Value = settings.CharacterHandleThickness;
        CharacterHandleOpacitySlider.Value = settings.CharacterHandleOpacity * 100;
        ResizeHandleFillColorTextBox.Text = settings.ResizeHandleFillColor;
        ResizeHandleBorderColorTextBox.Text = settings.ResizeHandleBorderColor;
        ResizeHandleSizeSlider.Value = settings.ResizeHandleSize;
        ResizeHandleOpacitySlider.Value = settings.ResizeHandleOpacity * 100;
        EstimationMinimumAspectSlider.Value = settings.CharacterEstimationMinimumAspectRatio * 100;
        EstimationMaximumAspectSlider.Value = settings.CharacterEstimationMaximumAspectRatio * 100;
        EstimationUniformitySlider.Value = settings.CharacterEstimationUniformity * 100;
        EstimationInkCoverageSlider.Value = settings.CharacterEstimationInkCoverage * 100;
        EstimationGlyphPriorSlider.Value = settings.CharacterEstimationGlyphPrior * 100;
        UndoHistoryLimitTextBox.Text = settings.UndoHistoryLimit.ToString(CultureInfo.InvariantCulture);
        AutoSaveEnabledCheckBox.IsChecked = settings.AutoSaveEnabled;
        AutoSaveIntervalMinutesTextBox.Text = settings.AutoSaveIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        BackupGenerationCountTextBox.Text = settings.BackupGenerationCount.ToString(CultureInfo.InvariantCulture);
        PreviousCharacterShortcutTextBox.Text = settings.PreviousCharacterShortcut;
        NextCharacterShortcutTextBox.Text = settings.NextCharacterShortcut;
        DecreaseCharacterAdvanceShortcutTextBox.Text = settings.DecreaseCharacterAdvanceShortcut;
        IncreaseCharacterAdvanceShortcutTextBox.Text = settings.IncreaseCharacterAdvanceShortcut;
        EstimateCharacterAdvancesShortcutTextBox.Text = settings.EstimateCharacterAdvancesShortcut;
        EstimateCharacterSuffixAdvancesShortcutTextBox.Text = settings.EstimateCharacterSuffixAdvancesShortcut;
        EqualizeCharacterAdvancesShortcutTextBox.Text = settings.EqualizeCharacterAdvancesShortcut;
        RestoreOriginalCharacterAdvancesShortcutTextBox.Text = settings.RestoreOriginalCharacterAdvancesShortcut;
        UpdatePreviews();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out var settings)) return;
        ResultSettings = settings;
        DialogResult = true;
    }

    internal bool TryReadSettings(out ApplicationSettings settings)
    {
        settings = ResultSettings;
        if (!int.TryParse(RecentFileLimitTextBox.Text, out var recentFileLimit) || recentFileLimit is < 0 or > RecentFilesService.MaximumCount)
        {
            ShowManagementMessage("表示件数は0～30の整数で入力してください。");
            SettingsTabs.SelectedItem = ManagementTab;
            RecentFileLimitTextBox.Focus();
            RecentFileLimitTextBox.SelectAll();
            return false;
        }
        var colors = new[]
        {
            (OcrOverlayColorTextBox, "OCRオーバーレイの表示色"),
            (CharacterHandleColorTextBox, "文字幅ハンドルの色"),
            (ResizeHandleFillColorTextBox, "サイズ変更ハンドルの塗り色"),
            (ResizeHandleBorderColorTextBox, "サイズ変更ハンドルの枠線色"),
        };
        foreach (var (textBox, label) in colors)
        {
            if (ApplicationSettings.IsValidColor(textBox.Text)) continue;
            MessageBox.Show($"{label}を #RRGGBB 形式で入力してください。", "設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            textBox.Focus();
            textBox.SelectAll();
            return false;
        }
        if (!int.TryParse(UndoHistoryLimitTextBox.Text, out var undoLimit) || undoLimit is < 10 or > 1000)
        {
            MessageBox.Show("Undo履歴数は10～1000の整数で入力してください。", "設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            UndoHistoryLimitTextBox.Focus();
            UndoHistoryLimitTextBox.SelectAll();
            return false;
        }
        if (!int.TryParse(AutoSaveIntervalMinutesTextBox.Text, out var autoSaveInterval) || autoSaveInterval is < 1 or > 120)
        {
            MessageBox.Show("自動保存の間隔は1～120分の整数で入力してください。", "設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            AutoSaveIntervalMinutesTextBox.Focus();
            AutoSaveIntervalMinutesTextBox.SelectAll();
            return false;
        }
        if (!int.TryParse(BackupGenerationCountTextBox.Text, out var backupGenerationCount) || backupGenerationCount is < 1 or > 20)
        {
            MessageBox.Show("バックアップ世代数は1～20の整数で入力してください。", "設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            BackupGenerationCountTextBox.Focus();
            BackupGenerationCountTextBox.SelectAll();
            return false;
        }
        var shortcuts = new[]
        {
            (PreviousCharacterShortcutTextBox, "前の文字へ移動"),
            (NextCharacterShortcutTextBox, "次の文字へ移動"),
            (DecreaseCharacterAdvanceShortcutTextBox, "選択文字の幅を狭める"),
            (IncreaseCharacterAdvanceShortcutTextBox, "選択文字の幅を広げる"),
            (EstimateCharacterAdvancesShortcutTextBox, "選択行を自動調整"),
            (EstimateCharacterSuffixAdvancesShortcutTextBox, "選択文字以降を自動調整"),
            (EqualizeCharacterAdvancesShortcutTextBox, "文字幅を等分"),
            (RestoreOriginalCharacterAdvancesShortcutTextBox, "OCR取込時の幅へ戻す"),
        };
        foreach (var (textBox, label) in shortcuts)
        {
            if (EditorShortcutService.TryNormalize(textBox.Text, out var normalized))
            {
                textBox.Text = normalized;
                continue;
            }
            MessageBox.Show(
                $"{label}のショートカットを認識できません。Ctrl、Alt、Winのいずれかを含む組み合わせ、またはF1～F24を入力してください。",
                "設定",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            textBox.Focus();
            textBox.SelectAll();
            return false;
        }
        var duplicate = shortcuts
            .Where(item => !string.IsNullOrWhiteSpace(item.Item1.Text))
            .GroupBy(item => item.Item1.Text, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            MessageBox.Show(
                $"{string.Join("、", duplicate.Select(item => item.Item2))}に同じショートカット「{duplicate.Key}」が割り当てられています。",
                "設定",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            duplicate.First().Item1.Focus();
            return false;
        }
        var reserved = shortcuts.FirstOrDefault(item => EditorShortcutService.IsReserved(item.Item1.Text));
        if (reserved.Item1 is not null)
        {
            MessageBox.Show(
                $"{reserved.Item2}のショートカット「{reserved.Item1.Text}」は、アクセスキー・Tab移動・保存・元に戻す・表示倍率などの既定操作で使用されています。別のキーを指定してください。",
                "設定",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            reserved.Item1.Focus();
            reserved.Item1.SelectAll();
            return false;
        }

        settings = (ResultSettings with
        {
            RecentFileLimit = recentFileLimit,
            WorkspacePresets = _workspacePresets.ToArray(),
            UiLanguage = UiLanguageComboBox.SelectedValue as string ?? LocalizationService.JapaneseLanguage,
            ShowToolbarText = ToolbarDisplayModeComboBox.SelectedIndex == 1,
            ToolbarButtonSize = ToolbarButtonSizeSlider.Value,
            ShowPropertyHelpText = ShowPropertyHelpTextCheckBox.IsChecked == true,
            ShowPageListPanel = ShowPageListPanelCheckBox.IsChecked == true,
            ShowPropertiesPanel = ShowPropertiesPanelCheckBox.IsChecked == true,
            ShowStatusBar = ShowStatusBarCheckBox.IsChecked == true,
            PageListWidth = PageListWidthSlider.Value,
            PropertiesPanelWidth = PropertiesPanelWidthSlider.Value,
            OcrOverlayColor = OcrOverlayColorTextBox.Text,
            OcrOverlayOpacity = OcrOverlayOpacitySlider.Value / 100,
            ShowUnselectedCharacterCellBorders = ShowUnselectedCharacterCellBordersCheckBox.IsChecked == true,
            CharacterCellBorderThickness = CharacterCellBorderThicknessSlider.Value,
            ShowPageThumbnails = ShowPageThumbnailsCheckBox.IsChecked == true,
            CharacterHandleColor = CharacterHandleColorTextBox.Text,
            CharacterHandleThickness = CharacterHandleThicknessSlider.Value,
            CharacterHandleOpacity = CharacterHandleOpacitySlider.Value / 100,
            ResizeHandleFillColor = ResizeHandleFillColorTextBox.Text,
            ResizeHandleBorderColor = ResizeHandleBorderColorTextBox.Text,
            ResizeHandleSize = ResizeHandleSizeSlider.Value,
            ResizeHandleOpacity = ResizeHandleOpacitySlider.Value / 100,
            CharacterEstimationMinimumAspectRatio = EstimationMinimumAspectSlider.Value / 100,
            CharacterEstimationMaximumAspectRatio = EstimationMaximumAspectSlider.Value / 100,
            CharacterEstimationUniformity = EstimationUniformitySlider.Value / 100,
            CharacterEstimationInkCoverage = EstimationInkCoverageSlider.Value / 100,
            CharacterEstimationGlyphPrior = EstimationGlyphPriorSlider.Value / 100,
            UndoHistoryLimit = undoLimit,
            AutoSaveEnabled = AutoSaveEnabledCheckBox.IsChecked == true,
            AutoSaveIntervalMinutes = autoSaveInterval,
            BackupGenerationCount = backupGenerationCount,
            PreviousCharacterShortcut = PreviousCharacterShortcutTextBox.Text,
            NextCharacterShortcut = NextCharacterShortcutTextBox.Text,
            DecreaseCharacterAdvanceShortcut = DecreaseCharacterAdvanceShortcutTextBox.Text,
            IncreaseCharacterAdvanceShortcut = IncreaseCharacterAdvanceShortcutTextBox.Text,
            EstimateCharacterAdvancesShortcut = EstimateCharacterAdvancesShortcutTextBox.Text,
            EstimateCharacterSuffixAdvancesShortcut = EstimateCharacterSuffixAdvancesShortcutTextBox.Text,
            EqualizeCharacterAdvancesShortcut = EqualizeCharacterAdvancesShortcutTextBox.Text,
            RestoreOriginalCharacterAdvancesShortcut = RestoreOriginalCharacterAdvancesShortcutTextBox.Text,
        }).Normalize();
        return true;
    }

    private void ResetButton_OnClick(object sender, RoutedEventArgs e) =>
        LoadDraft(new ApplicationSettings { WorkspacePresets = _workspacePresets.ToArray() });

    private void ClearRecentFiles_OnClick(object sender, RoutedEventArgs e)
    {
        if (_recentFileCount == 0 || ClearRecentFilesRequested ||
            !ConfirmManagement("最近開いたファイルの履歴をクリアしますか？文書自体は削除しません。「保存」で確定します。")) return;
        ClearRecentFilesRequested = true;
        UpdateRecentFilesStatus();
    }

    private void UpdateRecentFilesStatus()
    {
        ClearRecentFilesButton.IsEnabled = _recentFileCount > 0 && !ClearRecentFilesRequested;
        RecentFilesStatusTextBlock.Text = ClearRecentFilesRequested
            ? LocalizationService.Translate("履歴のクリアは「保存」で確定します。「キャンセル」では履歴を残します。")
            : LocalizationService.Format("保存されている履歴: {0}件", _recentFileCount);
    }

    internal void LoadDraft(ApplicationSettings settings)
    {
        ResultSettings = settings.Normalize();
        _workspacePresets.Clear();
        foreach (var preset in ResultSettings.WorkspacePresets) _workspacePresets.Add(preset);
        Populate(ResultSettings);
        WorkspacePresetComboBox.SelectedIndex = _workspacePresets.Count > 0 ? 0 : -1;
        UpdatePresetButtons();
    }

    private void WorkspacePreset_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspacePresetComboBox.SelectedItem is WorkspacePreset preset) WorkspacePresetNameTextBox.Text = preset.Name;
        UpdatePresetButtons();
    }

    private void UpdatePresetButtons()
    {
        if (ApplyWorkspacePresetButton is null) return;
        ApplyWorkspacePresetButton.IsEnabled = DeleteWorkspacePresetButton.IsEnabled = WorkspacePresetComboBox.SelectedItem is WorkspacePreset;
    }

    private void SaveWorkspacePreset_OnClick(object sender, RoutedEventArgs e)
    {
        var name = WorkspacePresetNameTextBox.Text.Trim();
        if (!WorkspacePreset.IsValidName(name))
        {
            ShowManagementMessage("名前は1～64文字で入力してください。");
            WorkspacePresetNameTextBox.Focus();
            return;
        }
        var existing = _workspacePresets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null && _workspacePresets.Count >= WorkspacePreset.MaximumCount)
        {
            ShowManagementMessage("プリセットは20件まで登録できます。");
            return;
        }
        if (existing is not null && !ConfirmManagement("同じ名前のプリセットを更新しますか？")) return;
        // Only capture layout controls: an unrelated unfinished shortcut edit must not prevent registration.
        var preset = WorkspacePreset.Capture(name, ReadWorkspaceLayout());
        if (existing is not null) _workspacePresets[_workspacePresets.IndexOf(existing)] = preset;
        else _workspacePresets.Add(preset);
        WorkspacePresetComboBox.SelectedItem = preset;
        SetManagementStatus("プリセットを登録しました。設定画面の「保存」で確定します。");
    }

    private ApplicationSettings ReadWorkspaceLayout() => ResultSettings with
    {
        ShowPageListPanel = ShowPageListPanelCheckBox.IsChecked == true,
        ShowPropertiesPanel = ShowPropertiesPanelCheckBox.IsChecked == true,
        ShowStatusBar = ShowStatusBarCheckBox.IsChecked == true,
        ShowPageThumbnails = ShowPageThumbnailsCheckBox.IsChecked == true,
        PageListWidth = PageListWidthSlider.Value,
        PropertiesPanelWidth = PropertiesPanelWidthSlider.Value,
    };

    private void ApplyWorkspacePreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (WorkspacePresetComboBox.SelectedItem is not WorkspacePreset preset) return;
        ShowPageListPanelCheckBox.IsChecked = preset.ShowPageListPanel;
        ShowPropertiesPanelCheckBox.IsChecked = preset.ShowPropertiesPanel;
        ShowStatusBarCheckBox.IsChecked = preset.ShowStatusBar;
        ShowPageThumbnailsCheckBox.IsChecked = preset.ShowPageThumbnails;
        PageListWidthSlider.Value = preset.PageListWidth;
        PropertiesPanelWidthSlider.Value = preset.PropertiesPanelWidth;
        SetManagementStatus("配置を設定画面へ読み込みました。「保存」で本画面へ反映します。");
    }

    private void DeleteWorkspacePreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (WorkspacePresetComboBox.SelectedItem is not WorkspacePreset preset) return;
        if (!ConfirmManagement("選択したプリセットを削除しますか？")) return;
        var index = _workspacePresets.IndexOf(preset);
        _workspacePresets.Remove(preset);
        WorkspacePresetComboBox.SelectedIndex = Math.Min(index, _workspacePresets.Count - 1);
        SetManagementStatus("プリセットを削除しました。設定画面の「保存」で確定します。");
    }

    private async void ImportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF Correctorium settings (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            // Validate first. Failed/canceled imports leave every existing draft field unchanged.
            var imported = await SettingsTransferService.ImportAsync(dialog.FileName);
            if (!ConfirmManagement("設定画面の内容を、取り込んだ設定とプリセットで置き換えますか？")) return;
            LoadDraft(imported);
            SetManagementStatus("設定を取り込みました。「保存」で確定します。取り込んだプリセット一覧で置き換わります。");
        }
        catch (Exception error)
        {
            ShowManagementMessage("設定を取り込めませんでした。対応形式・容量・ショートカット・プリセット名を確認してください。", error);
        }
    }

    private async void ExportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out var settings)) return;
        var dialog = new SaveFileDialog { Filter = "PDF Correctorium settings (*.json)|*.json", DefaultExt = ".json",
            AddExtension = true, FileName = "PDF-Correctorium-settings.json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(SettingsPathTextBox.Text), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(LocalizationService.Translate("アプリが使用中の設定ファイルとは別の保存先を選んでください。"));
            await SettingsTransferService.ExportAsync(dialog.FileName, settings);
            SetManagementStatus("設定画面の内容を書き出しました。本画面の設定は変更していません。");
        }
        catch (Exception error) { ShowManagementMessage("設定を書き出せませんでした。保存先を確認してください。", error); }
    }

    private void SetManagementStatus(string message) => ManagementStatusTextBlock.Text = LocalizationService.Translate(message);

    private bool ConfirmManagement(string message) => ConfirmManagementAction?.Invoke(message) ??
        MessageBox.Show(this, LocalizationService.Translate(message), LocalizationService.Translate("設定"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ShowManagementMessage(string message, Exception? error = null)
    {
        var text = LocalizationService.Translate(message) + (error is null ? "" : "\n\n" + error.Message);
        if (ManagementMessageOverride is not null) ManagementMessageOverride(text);
        else MessageBox.Show(this, text, LocalizationService.Translate("設定"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => UpdatePreviews();

    private void PreviewSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreviews();

    private void ShortcutTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        // Keep navigation and dialog keys available even while recording a shortcut.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape or Key.Return ||
            (Keyboard.Modifiers == ModifierKeys.Alt && key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9)) return;
        if (e.Key is Key.Back or Key.Delete)
        {
            textBox.Clear();
            e.Handled = true;
            return;
        }
        if (!EditorShortcutService.TryCapture(e, out var shortcut)) return;
        textBox.Text = shortcut;
        textBox.SelectAll();
        e.Handled = true;
    }

    private void UpdatePreviews()
    {
        if (!IsInitialized) return;
        OcrOverlayPreview.Background = CreateBrush(OcrOverlayColorTextBox.Text, OcrOverlayOpacitySlider.Value / 100);
        CharacterHandlePreview.Background = CreateBrush(CharacterHandleColorTextBox.Text, CharacterHandleOpacitySlider.Value / 100);
        CharacterHandlePreview.Width = CharacterHandleThicknessSlider.Value;
        ResizeHandlePreview.Background = CreateBrush(ResizeHandleFillColorTextBox.Text, ResizeHandleOpacitySlider.Value / 100);
        ResizeHandlePreview.BorderBrush = CreateBrush(ResizeHandleBorderColorTextBox.Text, ResizeHandleOpacitySlider.Value / 100);
        ResizeHandlePreview.Width = ResizeHandlePreview.Height = ResizeHandleSizeSlider.Value;
    }

    private static Brush CreateBrush(string? value, double opacity)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
                return new SolidColorBrush(color) { Opacity = Math.Clamp(opacity, 0, 1) };
        }
        catch (FormatException) { }
        catch (NotSupportedException) { }
        return Brushes.Transparent;
    }
}
