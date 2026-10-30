using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class App
{
    private static async Task VerifySimilarWordLayoutContinuityAsync(MainWindowViewModel vm,
        OcrSimilarWordDocumentResult result, Action<bool, string> check)
    {
        var region = vm.SelectedOverlay ?? throw new InvalidOperationException("No selected OCR row.");
        var partial = region.Capture();
        vm.NudgeSelection(3, 2);
        check(region.Left != partial.Left && region.Text == partial.Text,
            "The reported post-correction move changes only the selected OCR row's placement.");
        check(vm.IsSimilarWordResultCurrent(result), "Moving an OCR row after partial correction keeps the remaining analysis current.");
        var moved = region.Capture();
        vm.UndoCommand.Execute(null);
        check(vm.IsSimilarWordResultCurrent(result) && region.Capture() == partial,
            "Undoing only a geometry move preserves the same analysis snapshot.");
        vm.RedoCommand.Execute(null);
        check(vm.IsSimilarWordResultCurrent(result) && region.Capture() == moved,
            "Redoing only a geometry move preserves the same analysis snapshot.");
        vm.UndoCommand.Execute(null);
        vm.BeginOverlayEdit(region);
        region.Left += 4; region.Top += 2; region.Width += 6; region.Height += 2; region.RotationDegrees += 3;
        check(vm.IsSimilarWordResultCurrent(result), "An in-progress layout drag does not expire text-only analysis.");
        vm.EndOverlayEdit("Similar-word layout continuity diagnostic");
        check(vm.IsSimilarWordResultCurrent(result), "Committing a move, resize and rotation does not expire text-only analysis.");
        var candidate = result.Analysis.Candidates.First(item => !item.Sample.IsLocked);
        check(await vm.NavigateToOcrQualityCandidateAsync(candidate.PageNumber, candidate.RegionId, candidate.StartIndex, candidate.Length) &&
            vm.IsSimilarWordResultCurrent(result) && vm.SelectedOverlay?.Id == candidate.RegionId,
            "Retained candidates navigate to the current OCR geometry after a layout edit.");
        vm.UndoCommand.Execute(null);
        check(vm.IsSimilarWordResultCurrent(result) && region.Capture() == partial,
            "Restoring layout keeps the exact partially corrected text and analysis.");
    }

    private static async Task VerifySimilarWordInputInvalidationAsync(MainWindowViewModel vm, Action<bool, string> check)
    {
        var options = new OcrSimilarWordOptions(MaximumSuspectCount: 3, MinimumFrequencyRatio: 2);
        var scanning = vm.AnalyzeOcrSimilarWordsAsync(options);
        var source = vm.OverlayItems.First();
        source.Left += 2;
        var result = await scanning;
        check(vm.IsSimilarWordResultCurrent(result) && result.Analysis.Candidates.Count == 4,
            "A layout edit during whole-book analysis does not cancel or restart text collection.");
        vm.UndoCommand.Execute(null);
        check(vm.IsSimilarWordResultCurrent(result) && !vm.HasUnsavedChanges,
            "Undoing that layout edit preserves the completed analysis and clean document state.");
        var candidate = result.Analysis.Candidates.First(item => !item.Sample.IsLocked);
        await vm.NavigateToOcrQualityCandidateAsync(candidate.PageNumber, candidate.RegionId, candidate.StartIndex, candidate.Length);
        var region = vm.SelectedOverlay!;
        vm.SelectedReviewStatus = ReviewStatus.Verified;
        check(vm.IsSimilarWordResultCurrent(result), "Review-status-only edits do not invalidate word frequency evidence.");
        vm.UndoCommand.Execute(null);
        check(vm.IsSimilarWordResultCurrent(result), "Undoing only review status also retains text analysis.");
        region.IsGeometryLocked = true;
        check(!vm.IsSimilarWordResultCurrent(result) && vm.ApplySimilarWordCorrections(result, result.Analysis.Candidates) == 0,
            "Region-lock changes invalidate analysis and cannot apply old candidates.");
        vm.UndoCommand.Execute(null);
        check(!vm.IsSimilarWordResultCurrent(result), "Undoing a lock change cannot resurrect an old analysis generation.");
        result = await vm.AnalyzeOcrSimilarWordsAsync(options);
        region.SelectCharacter(0, false, false); region.SetSelectedCharacterLocks(true);
        check(!vm.IsSimilarWordResultCurrent(result) && vm.ApplySimilarWordCorrections(result, result.Analysis.Candidates) == 0,
            "Character-lock changes also invalidate analysis without bypassing fixed text.");
        vm.UndoCommand.Execute(null);
        check(!vm.IsSimilarWordResultCurrent(result), "Undoing a character lock does not revive stale candidates.");
        result = await vm.AnalyzeOcrSimilarWordsAsync(options);
        region.IsDeleted = true;
        check(!vm.IsSimilarWordResultCurrent(result) && vm.ApplySimilarWordCorrections(result, result.Analysis.Candidates) == 0,
            "Deleting OCR invalidates the frequency analysis and prevents stale correction.");
        vm.UndoCommand.Execute(null);
        result = await vm.AnalyzeOcrSimilarWordsAsync(options);
        region.Text += " extra";
        check(!vm.IsSimilarWordResultCurrent(result), "Changing text still invalidates completed analysis immediately.");
        vm.UndoCommand.Execute(null);
        check(!vm.IsSimilarWordResultCurrent(result) && !vm.HasUnsavedChanges,
            "Restoring identical text with Undo cannot revive stale candidates even in a clean document.");
    }
}
