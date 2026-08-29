using System.Globalization;
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
    public ApplicationSettingsWindow(ApplicationSettings settings, string storageMode, string settingsPath)
    {
        InitializeComponent();
        ResultSettings = settings.Normalize();
        StorageModeTextBlock.Text = storageMode;
        SettingsPathTextBox.Text = settingsPath;
        Populate(ResultSettings);
        LocalizationService.Apply(this);
    }

    public ApplicationSettings ResultSettings { get; private set; }

    private void Populate(ApplicationSettings settings)
    {
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
            return;
        }
        if (!int.TryParse(UndoHistoryLimitTextBox.Text, out var undoLimit) || undoLimit is < 10 or > 1000)
        {
            MessageBox.Show("Undo履歴数は10～1000の整数で入力してください。", "設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            UndoHistoryLimitTextBox.Focus();
            UndoHistoryLimitTextBox.SelectAll();
            return;
        }
        if (!int.TryParse(AutoSaveIntervalMinutesTextBox.Text, out var autoSaveInterval) || autoSaveInterval is < 1 or > 120)
        {
            MessageBox.Show("自動保存の間隔は1～120分の整数で入力してください。", "設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            AutoSaveIntervalMinutesTextBox.Focus();
            AutoSaveIntervalMinutesTextBox.SelectAll();
            return;
        }
        if (!int.TryParse(BackupGenerationCountTextBox.Text, out var backupGenerationCount) || backupGenerationCount is < 1 or > 20)
        {
            MessageBox.Show("バックアップ世代数は1～20の整数で入力してください。", "設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            BackupGenerationCountTextBox.Focus();
            BackupGenerationCountTextBox.SelectAll();
            return;
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
            return;
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
            return;
        }
        var reserved = shortcuts.FirstOrDefault(item => EditorShortcutService.IsReserved(item.Item1.Text));
        if (reserved.Item1 is not null)
        {
            MessageBox.Show(
                $"{reserved.Item2}のショートカット「{reserved.Item1.Text}」は、保存・元に戻す・表示倍率などの既定操作で使用されています。別のキーを指定してください。",
                "設定",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            reserved.Item1.Focus();
            reserved.Item1.SelectAll();
            return;
        }

        ResultSettings = new ApplicationSettings
        {
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
        }.Normalize();
        DialogResult = true;
    }

    private void ResetButton_OnClick(object sender, RoutedEventArgs e) => Populate(new ApplicationSettings());

    private void ColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => UpdatePreviews();

    private void PreviewSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreviews();

    private void ShortcutTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
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
