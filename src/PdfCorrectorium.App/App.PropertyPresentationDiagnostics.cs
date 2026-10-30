using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyPropertyPresentationAsync(MainWindow window, MainWindowViewModel vm,
        Action<bool, string> check, Func<Task> layout, Action<string> snapshot)
    {
        var selector = (ComboBox)window.FindName("EditUnitSelector");
        var whole = (TextBlock)window.FindName("OcrWholeAdjustmentStatusText");
        var suffix = (TextBlock)window.FindName("OcrSuffixAdjustmentStatusText");
        var canvas = (ListBox)window.FindName("OverlayCanvas");
        var region = vm.SelectedOverlay!;
        var oldText = region.Text;
        var oldPreview = vm.PreviewImage;
        var dirty = vm.HasUnsavedChanges; var undo = vm.UndoCountForDiagnostics;
        var units = new[] { OcrEditUnit.Paragraph, OcrEditUnit.Line, OcrEditUnit.Character };
        check((int)OcrEditUnit.Line == 0 && (int)OcrEditUnit.Paragraph == 1 && (int)OcrEditUnit.Character == 2 &&
            selector.Items.Cast<ComboBoxItem>().Select(item => (OcrEditUnit)item.Tag).SequenceEqual(units),
            "The selector orders Paragraph/Line/Character while preserving the original enum identities.");
        foreach (var language in new[] { "ja-JP", "en-US" })
        {
            LocalizationService.SetLanguage(language); vm.RefreshLocalization(); LocalizationService.Apply(window);
            foreach (var view in window.PropertyPaneViews) LocalizationService.Apply(view);
            for (var index = 0; index < units.Length; index++)
            {
                selector.SelectedIndex = index; await layout();
                check(vm.EditUnit == units[index] && vm.EditUnitIndex == (int)units[index] &&
                    vm.IsParagraphEditMode == (index == 0) && vm.IsLineEditMode == (index == 1) && vm.IsCharacterEditMode == (index == 2),
                    $"{language}: choosing display item {index} activates {units[index]}, not the old positional mode.");
            }
            foreach (var unit in units)
            {
                vm.EditUnitIndex = (int)unit; await layout();
                check((OcrEditUnit)selector.SelectedValue == unit,
                    $"{language}: programmatic {unit} selection updates the correct displayed item.");
            }
        }
        check(vm.HasUnsavedChanges == dirty && vm.UndoCountForDiagnostics == undo &&
            ReferenceEquals(vm.PreviewImage, oldPreview) && region.Text == oldText,
            "Reordering/localizing/selecting units does not edit text, render a PDF page or add history.");
        foreach (var language in new[] { "ja-JP", "en-US" })
        {
            LocalizationService.SetLanguage(language); vm.RefreshLocalization(); LocalizationService.Apply(window);
            foreach (var view in window.PropertyPaneViews) LocalizationService.Apply(view);
            vm.EditorMode = EditorInteractionMode.OcrEditing; vm.EditUnit = OcrEditUnit.Character;
            canvas.SelectedItems.Clear(); vm.SetOverlaySelection([]); await layout();
            var phrase = language == "ja-JP" ? "OCR対象未選択" : "No OCR selection";
            check(PropertyLogicalElements((DependencyObject)window.FindName("OcrPropertyContent")).OfType<TextBlock>()
                .Count(text => text.IsVisible && text.Text.Contains(phrase, StringComparison.Ordinal)) == 1,
                $"{language}: no-selection feedback appears once in the current-target context.");
            snapshot("dedup-no-selection-" + language);
            region.Text = "F"; region.IsGeometryLocked = false;
            canvas.SelectedItems.Add(region); vm.SetOverlaySelection([region], region); region.SelectedCharacterIndex = 0;
            await layout();
            check(!vm.EstimateCharacterAdvancesCommand.CanExecute(null) && whole.IsVisible && !suffix.IsVisible &&
                whole.Text == vm.OcrWholeAdjustmentStatus &&
                whole.Text.Contains(language == "ja-JP" ? "2文字以上" : "at least two", StringComparison.Ordinal),
                $"{language}: a one-character line has one precise reason, not two ambiguous lock messages.");
            snapshot("dedup-one-character-" + language);
            region.IsGeometryLocked = true; await layout();
            check(whole.IsVisible && !suffix.IsVisible &&
                whole.Text.Contains(language == "ja-JP" ? "固定" : "locked", StringComparison.Ordinal),
                $"{language}: locking changes the shared reason without creating a second explanation.");
            region.IsGeometryLocked = false; region.Text = oldText; region.SelectedCharacterIndex = 1;
            vm.PreviewImage = null; await layout();
            check(whole.IsVisible && !suffix.IsVisible &&
                whole.Text.Contains(language == "ja-JP" ? "準備中" : "not ready", StringComparison.Ordinal),
                $"{language}: image readiness is explained once.");
            vm.PreviewImage = oldPreview; vm.EditorMode = EditorInteractionMode.Review; await layout();
            check(whole.IsVisible && !suffix.IsVisible,
                $"{language}: proofreading's unavailable adjustment reason is not duplicated.");
            // Review intentionally switches to Line editing. Explicitly return
            // to Character editing before checking its suffix-specific scope.
            vm.EditorMode = EditorInteractionMode.OcrEditing; vm.EditUnit = OcrEditUnit.Character;
            region.SelectedCharacterIndex = 1; await layout();
            check(whole.IsVisible && suffix.IsVisible && whole.Text != suffix.Text,
                $"{language}: available actions retain their two distinct scope descriptions.");
            region.SelectedCharacterIndex = region.TextElementCount - 1; await layout();
            check(whole.IsVisible && suffix.IsVisible && !vm.EstimateCharacterSuffixAdvancesCommand.CanExecute(null) &&
                suffix.Text.Contains(language == "ja-JP" ? "最終文字" : "Last character", StringComparison.Ordinal),
                $"{language}: suffix-specific last-character guidance remains visible.");
        }
        var general = (StatusBarItem)window.FindName("GeneralStatusItem");
        var progress = (StatusBarItem)window.FindName("BackgroundProgressStatusItem");
        var export = (Border)window.FindName("PdfExportOverlay");
        var begin = typeof(MainWindowViewModel).GetMethod("BeginBackgroundOperation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var end = typeof(MainWindowViewModel).GetMethod("EndBackgroundOperation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var exporting = typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.IsPdfExporting))!;
        var originalStatus = vm.StatusMessage;
        dirty = vm.HasUnsavedChanges; undo = vm.UndoCountForDiagnostics;
        foreach (var language in new[] { "ja-JP", "en-US" })
        {
            LocalizationService.SetLanguage(language); vm.RefreshLocalization(); LocalizationService.Apply(window);
            const string message = "プロジェクトを保存・検証しています...";
            vm.StatusMessageForInteraction(message); begin.Invoke(vm, [message, true]); await layout();
            check(progress.IsVisible && general.Content as string == string.Empty && vm.StatusMessage.Length > 0 && !export.IsVisible,
                $"{language}: progress owns a matching status message without clearing the underlying status.");
            exporting.SetValue(vm, true); await layout();
            check(export.IsVisible && !progress.IsVisible && general.Content as string == string.Empty &&
                ((TextBlock)window.FindName("PdfExportProgressText")).Text == vm.BackgroundOperationMessage,
                $"{language}: PDF export presents live progress once in its blocking overlay.");
            vm.StatusMessageForInteraction("別の重要なメッセージ"); await layout();
            check(general.Content as string == vm.StatusMessage,
                $"{language}: a different status or warning is retained, never blanket-hidden.");
            exporting.SetValue(vm, false); end.Invoke(vm, null); await layout();
            check(!progress.IsVisible && general.Content as string == vm.StatusMessage,
                $"{language}: normal status returns after progress finishes.");
        }
        check(vm.HasUnsavedChanges == dirty && vm.UndoCountForDiagnostics == undo && ReferenceEquals(vm.PreviewImage, oldPreview),
            "Progress presentation alone leaves document state, history and preview unchanged.");
        vm.StatusMessageForInteraction(originalStatus);
        LocalizationService.SetLanguage("ja-JP"); vm.RefreshLocalization(); LocalizationService.Apply(window);
    }
}
