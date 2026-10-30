using System.Windows.Media.Imaging;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

internal sealed record RapidOcrTicket(Guid ProjectId, int Session, long Generation, int Page,
    OverlayRegionViewModel Region, OverlayRegionSnapshot Before, RapidLineResult Result, OcrLineCompletion Completion,
    RapidOcrLineAdjustment Adjustment);

public sealed partial class MainWindowViewModel
{
    private bool _isRapidOcrRunning;
    internal void ReportRapidOcrStatus(string message) => StatusMessage = LocalizationService.Translate(message);
    internal Task ReportRapidOcrFailureAsync(Exception error) => ShowErrorAsync("選択行を再認識できませんでした。", error);
    public bool IsRapidOcrRunning => _isRapidOcrRunning;
    public bool CanRecognizeSelectedLine => CanEditDocument && !IsOpeningDocument && !IsPdfExporting &&
        !IsBackgroundOperationVisible && !_isRapidOcrRunning && IsOcrEditMode && CanUsePreview &&
        (IsCharacterEditMode || IsLineEditMode) &&
        SelectedOverlay is { IsDeleted: false, IsVertical: false, IsGeometryLocked: false, HasLockedCharacters: false } region &&
        SelectedPage is { } page && _pageOverlays.GetValueOrDefault(page.PageNumber)?.Contains(region) == true &&
        GetSelectedCharacterLines().Count == 1 && PreviewPixelWidth > 0 && PreviewPixelHeight > 0;

    internal async Task<RapidOcrTicket?> RecognizeSelectedLineAsync(CancellationToken token)
    {
        if (!CanRecognizeSelectedLine || _project is null || _resolvedPdfPath is null || SelectedPage is null || SelectedOverlay is not { } region)
            return null;
        var before = region.Capture(); var project = _project.ProjectId; var session = _overlaySessionVersion;
        var pdfPath = _resolvedPdfPath;
        var generation = _similarWordHistoryGeneration; var page = SelectedPage.PageNumber;
        var source = EnsurePageSequence()[page - 1];
        var crop = new OcrLineCrop(region.Left, region.Top, region.Width, region.Height, region.RotationDegrees,
            PreviewPixelWidth, PreviewPixelHeight, source.RotationDegrees);
        var options = new CharacterAdvanceEstimationOptions(_applicationSettings.CharacterEstimationMinimumAspectRatio,
            _applicationSettings.CharacterEstimationMaximumAspectRatio, _applicationSettings.CharacterEstimationUniformity,
            _applicationSettings.CharacterEstimationInkCoverage, _applicationSettings.CharacterEstimationGlyphPrior);
        _isRapidOcrRunning = true; OnPropertyChanged(nameof(IsRapidOcrRunning));
        BeginBackgroundOperation(OcrText("RapidOCRで選択行を再認識・文字サイズ調整しています…（初回はモデルを読み込みます）",
            "Re-recognizing and fitting the selected line… (loading the model on first use)"));
        var operation = _backgroundOperationId;
        NotifyOcrPropertyContext();
        try
        {
            // Staging/native inference must never run synchronously on the dispatcher.
            var prepared = await Task.Run(async () =>
            {
                var recognized = await PdfNativeWorkerClient.Ocr.RecognizeLineAsync(
                    pdfPath, source.SourcePageNumber, crop, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var adjustment = RapidOcrLineAdjuster.Prepare(before, recognized, options);
                token.ThrowIfCancellationRequested();
                return (Result: recognized, Adjustment: adjustment);
            }, token);
            var result = prepared.Result;
            token.ThrowIfCancellationRequested();
            // Opening another document/starting another operation takes foreground ownership.
            if (!IsCurrentBackgroundOperation(operation)) return null;
            var completion = OcrLineReconciler.Evaluate(before.Text, result.Recognition.Reading,
                before.IsGeometryLocked || region.HasLockedCharacters, before.Text != region.OriginalText, before.IsVertical == true);
            var ticket = new RapidOcrTicket(project, session, generation, page, region, before, result, completion, prepared.Adjustment);
            if (!IsRapidOcrTicketCurrent(ticket))
            { StatusMessage = OcrText("対象行が変更されたため、再認識結果は適用しませんでした。もう一度実行してください。",
                "The target changed. Nothing was applied; re-recognize the line again."); return null; }
            return ticket;
        }
        finally
        {
            _isRapidOcrRunning = false; OnPropertyChanged(nameof(IsRapidOcrRunning));
            if (IsCurrentBackgroundOperation(operation)) EndBackgroundOperation();
            NotifyOcrPropertyContext();
        }
    }

    internal bool IsRapidOcrTicketCurrent(RapidOcrTicket ticket) =>
        _project?.ProjectId == ticket.ProjectId && _overlaySessionVersion == ticket.Session &&
        _similarWordHistoryGeneration == ticket.Generation && ticket.Region.Capture() == ticket.Before &&
        !ticket.Region.IsDeleted && !ticket.Region.IsGeometryLocked && !ticket.Region.HasLockedCharacters &&
        _pageOverlays.GetValueOrDefault(ticket.Page)?.Contains(ticket.Region) == true;

    internal static string? ValidateRapidOcrReviewText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "適用する文字を入力してください。";
        if (text.Length > OcrLineReconciler.MaximumElements * 4 || OcrLineReconciler.Elements(text).Length > OcrLineReconciler.MaximumElements)
            return "1行に入力できる文字は512文字までです。";
        return text.Any(char.IsControl) ? "改行・タブなどを含めず、1行の文字を入力してください。" : null;
    }

    internal async Task<RapidOcrTicket?> PrepareRapidOcrReviewAsync(RapidOcrTicket ticket, string text, CancellationToken token)
    {
        if (ValidateRapidOcrReviewText(text) is not null || !IsRapidOcrTicketCurrent(ticket)) return null;
        token.ThrowIfCancellationRequested();
        if (text == ticket.Result.Recognition.Reading.Text) return ticket;
        // Manual text is not model evidence: never manufacture recognition scores or permit auto-application.
        var result = ticket.Result with { Recognition = ticket.Result.Recognition with { Reading = new(text, [], [], []) } };
        var options = new CharacterAdvanceEstimationOptions(_applicationSettings.CharacterEstimationMinimumAspectRatio,
            _applicationSettings.CharacterEstimationMaximumAspectRatio, _applicationSettings.CharacterEstimationUniformity,
            _applicationSettings.CharacterEstimationInkCoverage, _applicationSettings.CharacterEstimationGlyphPrior);
        var adjustment = await Task.Run(() => RapidOcrLineAdjuster.Prepare(ticket.Before, result, options), token);
        token.ThrowIfCancellationRequested();
        return IsRapidOcrTicketCurrent(ticket) ? ticket with { Result = result, Adjustment = adjustment,
            Completion = new(false, "手動で修正した候補です。原画像と照合して適用してください。", []) } : null;
    }

    internal async Task<bool> ApplyAutomaticOcrRulesAsync(RapidOcrTicket ticket, OcrLexiconQuery query, CancellationToken token)
    {
        if (!IsRapidOcrTicketCurrent(ticket) || !query.CanApplyAutomatically || query.RawText != ticket.Result.Recognition.Reading.Text ||
            (ticket.Before.Text != query.RawText && !ticket.Completion.IsAutomatic)) return false;
        var current = await QueryOcrLexiconAsync(query.RawText, token);
        if (!current.CanApplyAutomatically || current.SuggestedText != query.SuggestedText) return false;
        var prepared = await PrepareRapidOcrReviewAsync(ticket, current.SuggestedText, token);
        // Prepare strips model scores. Permission comes exclusively from the validated explicit rules above.
        if (prepared is null || !ApplyRapidOcrResult(prepared, automatic: false)) return false;
        ReportRapidOcrStatus("許可済みの辞書ルールを適用し、文字サイズを調整しました。Undoでまとめて戻せます。"); return true;
    }

    internal static (string Source, string Target)[] GetOcrRegistrationPairs(string raw, string corrected)
    {
        if (raw == corrected) return [];
        if (OcrLexiconPolicy.IsValidTerm(raw) && OcrLexiconPolicy.IsValidTerm(corrected)) return [(raw, corrected)];
        var before = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var after = corrected.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (before.Length != after.Length) return [];
        return before.Zip(after).Where(p => p.First != p.Second && OcrLexiconPolicy.IsValidTerm(p.First) && OcrLexiconPolicy.IsValidTerm(p.Second))
            .Select(p => (p.First, p.Second)).ToArray();
    }

    internal bool ApplyRapidOcrResult(RapidOcrTicket ticket, bool automatic, bool registerKnown = false, bool registerRules = false, string? rawText = null)
    {
        if (!CanEditDocument || IsPdfExporting || IsOpeningDocument || IsBackgroundOperationVisible ||
            !IsRapidOcrTicketCurrent(ticket) || (automatic && !ticket.Completion.IsAutomatic)) return false;
        var reading = ticket.Result.Recognition.Reading;
        var elements = OcrLineReconciler.Elements(reading.Text);
        if (elements.Length is 0 or > OcrLineReconciler.MaximumElements || reading.Text.Any(char.IsControl) ||
            ticket.Adjustment.Snapshot.Text != reading.Text) return false;
        var region = ticket.Region;
        var description = reading.Text == ticket.Before.Text
            ? OcrText("再OCR後の文字サイズを画像から調整", "Fit character sizes after re-recognition")
            : automatic ? OcrText("RapidOCRで文字抜けを補完・サイズ調整", "RapidOCR completion and image fitting")
            : OcrText("RapidOCRの訂正・サイズ調整を適用", "Apply RapidOCR correction and image fitting");
        ApplyRegionEdit(description, [region], () =>
        {
            RegisterOcrCorrections(GetOcrRegistrationPairs(rawText ?? ticket.Result.Recognition.Reading.Text, reading.Text), registerKnown, registerRules);
            // Text and the prepared image fit are a single snapshot/Undo operation.
            region.Apply(ticket.Adjustment.Snapshot with {
                ReviewStatus = ticket.Adjustment.Snapshot == ticket.Before ? ticket.Before.ReviewStatus
                    : ticket.Before.ReviewStatus == ReviewStatus.NeedsReview ? ReviewStatus.NeedsReview : ReviewStatus.Modified });
        });
        var added = ticket.Completion.InsertedIndices;
        if (automatic && added.Length > 0)
        {
            var starts = System.Globalization.StringInfo.ParseCombiningCharacters(region.Text);
            var start = starts[added[0]]; var end = added[^1] + 1 < starts.Length ? starts[added[^1] + 1] : region.Text.Length;
            region.SetSearchHighlightByTextOffset(start, end - start);
        }
        var summary = reading.Text == ticket.Before.Text
            ? OcrText("再認識結果は既存の文字と一致しました。", "The recognized text matches the existing text.")
            : automatic ? OcrText($"RapidOCRで{added.Length}文字を自動補完しました。", $"RapidOCR completed {added.Length} missing characters.")
            : OcrText("RapidOCRの訂正候補を適用しました。", "Applied the reviewed OCR candidate.");
        StatusMessage = summary + (ticket.Adjustment.IsImageAdjusted
            ? OcrText(" 行全体の文字サイズを画像から調整しました。Undoでまとめて戻せます。", " Fitted character sizes to the image. One Undo restores both edits.")
            : OcrText(" 文字サイズは画像から調整できなかったため、元の範囲に保持しました。境界を確認してください。",
                " Image fitting was unavailable; the original bounds were retained. Check the character boundaries."));
        if (ticket.Adjustment.RequiresReview)
            StatusMessage += OcrText(" 画像判定の信頼度が低いため、境界を確認してください。", " Image-fit confidence is low; check the boundaries.");
        if (!ticket.Adjustment.IsImageAdjusted)
            _ = _log.WriteAsync(PdfCorrectorium.Infrastructure.LogLevel.Warning, "rapid-ocr.character-fit-unavailable", ticket.Adjustment.Detail);
        return true;
    }
}
