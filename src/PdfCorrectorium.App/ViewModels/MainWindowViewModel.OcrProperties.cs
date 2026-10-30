using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App.ViewModels;

public partial class MainWindowViewModel
{
    internal event EventHandler? OcrPositionLocksChanged;

    internal bool IsOcrPositionLocked(Guid regionId, bool fallback) =>
        _lastOverlaySnapshots.TryGetValue(regionId, out var snapshot) ? snapshot.IsGeometryLocked : fallback;

    private void NotifyOcrPositionLocksChanged(HistoryEdit? edit)
    {
        if (edit is OverlayEdit overlay &&
            overlay.Changes.Any(change => change.Before.IsGeometryLocked != change.After.IsGeometryLocked))
            OcrPositionLocksChanged?.Invoke(this, EventArgs.Empty);
    }

    // Selection-only projections: no document scan, image analysis or cached visual state.
    public string OcrContextTarget => SelectedPage is not { } page
        ? OcrText("PDF未選択", "No PDF selected")
        : SelectedOverlay is not { } region
            ? OcrText($"PDF {page.PageNumber}ページ／OCR対象未選択", $"PDF page {page.PageNumber} / No OCR selection")
            : OcrText($"PDF {page.PageNumber}ページ／OCR {Math.Max(1, SelectedOverlayCount)}領域／主選択行 {region.SelectedCharacterCount}文字選択",
                $"PDF page {page.PageNumber} / {Math.Max(1, SelectedOverlayCount)} OCR regions / {region.SelectedCharacterCount} characters in primary line");

    public string OcrContextState
    {
        get
        {
            var mode = EditorMode switch
            {
                EditorInteractionMode.OcrEditing => OcrText("OCR編集", "OCR editing"),
                EditorInteractionMode.Review => OcrText("校正：文字訂正のみ", "Proofreading: text corrections only"),
                EditorInteractionMode.ReadingOrder => OcrText("読み順編集", "Reading order"),
                _ => OcrText("別モードで操作中", "Another editing mode is active")
            };
            var targets = GetSelectedGeometryLockTargets();
            var locked = targets.Count(region => region.IsGeometryLocked);
            if (locked > 0) mode += OcrText($"／領域位置固定 {locked}/{targets.Count}件", $" / Region geometry locked: {locked}/{targets.Count}");
            if (SelectedOverlay is { HasLockedCharacters: true })
                mode += OcrText("／主選択行に固定文字あり", " / Primary line includes locked characters");
            return mode;
        }
    }

    public bool ShowOcrImageAdjustment => IsOcrInteractionMode && !IsReadingOrderMode && (IsCharacterEditMode || IsLineEditMode);
    public bool ShowOcrEditModeAction => HasDocument && !IsOcrEditMode;
    // Both actions share their unavailable reason. Render that reason once,
    // while retaining separate scopes/suffix-specific guidance when applicable.
    public bool ShowOcrSuffixAdjustmentStatus => IsCharacterEditMode && EstimateCharacterAdvancesCommand.CanExecute(null);

    public string OcrWholeAdjustmentStatus
    {
        get
        {
            if (!EstimateCharacterAdvancesCommand.CanExecute(null)) return OcrAdjustmentUnavailable();
            var count = GetCharacterEstimationTargets().Count;
            var skipped = GetSelectedGeometryLockTargets().Count - count;
            var text = OcrText($"対象：{count}行全体。固定境界は保持。", $"Scope: {count} entire lines; locked boundaries retained.");
            return skipped > 0 ? text + OcrText($" 対象外：{skipped}行。", $" Skipped: {skipped} lines.") : text;
        }
    }

    public string OcrSuffixAdjustmentStatus
    {
        get
        {
            if (!EstimateCharacterAdvancesCommand.CanExecute(null)) return OcrAdjustmentUnavailable();
            if (SelectedOverlay is not { HasSingleCharacterSelection: true } region)
                return OcrText("主選択行の文字を1つ選択してください。", "Select one character in the primary line.");
            if (!region.CanAutomaticallyAdjust) return OcrText("主選択行が固定中、または調整対象がありません。", "The primary line is locked or has no adjustable characters.");
            if (region.SelectedCharacterIndex >= region.TextElementCount - 1)
                return OcrText("最終文字です。行全体の調整を使用してください。", "Last character: use entire-line adjustment.");
            // Do not invent a second eligibility rule: the command remains authoritative.
            if (!EstimateCharacterSuffixAdvancesCommand.CanExecute(null)) return OcrAdjustmentUnavailable();
            return OcrText($"対象：主選択行の{region.SelectedCharacterIndex + 1}文字目〜末尾。前の文字は保持。",
                $"Scope: primary line, character {region.SelectedCharacterIndex + 1} to end; preceding characters retained.");
        }
    }

    private string OcrAdjustmentUnavailable()
    {
        if (!CanEditDocument) return OcrText("文書の準備完了後に操作できます。", "Available when the document is ready.");
        if (IsOpeningDocument || IsBackgroundOperationVisible || IsPdfExporting)
            return OcrText("処理が完了するまでお待ちください。", "Wait until the current operation finishes.");
        if (!IsOcrEditMode) return OcrText("画像調整はOCR編集モードで使用できます。", "Image adjustment requires OCR editing mode.");
        if (!HasSelectedOverlay) return OcrText("調整するOCR行を選択してください。", "Select OCR lines to adjust.");
        if (!CanUsePreview || PreviewImage is not System.Windows.Media.Imaging.BitmapSource)
            return OcrText("ページ画像の準備中です。", "The page image is not ready.");
        var targets = GetSelectedGeometryLockTargets();
        if (targets.Count > 0 && targets.All(region => region.IsGeometryLocked))
            return OcrText("選択行の位置・サイズが固定されています。", "The selected lines have locked geometry.");
        if (targets.Count > 0 && targets.All(region => region.TextElementCount < 2))
            return OcrText("画像調整には2文字以上の行が必要です。", "Image adjustment requires a line with at least two characters.");
        return OcrText("固定中、または調整できる複数文字がありません。", "Locked, or no adjustable multi-character line.");
    }

    private static string OcrText(string japanese, string english) => LocalizationService.IsEnglish ? english : japanese;

    private void NotifyOcrPropertyContext()
    {
        OnPropertyChanged(nameof(CanRecognizeSelectedLine));
        OnPropertyChanged(nameof(OcrContextTarget));
        OnPropertyChanged(nameof(OcrContextState));
        OnPropertyChanged(nameof(ShowOcrImageAdjustment));
        OnPropertyChanged(nameof(ShowOcrEditModeAction));
        OnPropertyChanged(nameof(OcrWholeAdjustmentStatus));
        OnPropertyChanged(nameof(OcrSuffixAdjustmentStatus));
        OnPropertyChanged(nameof(ShowOcrSuffixAdjustmentStatus));
    }
}
