using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using PdfCorrectorium.App.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyMixedParagraphAsync(MainWindow main, string output, Action<bool, string> check,
        Func<FrameworkElement, string, int, int, Task> picture)
    {
        var vm = (MainWindowViewModel)main.DataContext; var packages = new ProjectPackageService();
        vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
        var pdf = Path.Combine(output, "chapter-mixed.pdf"); WriteDocumentUiTestPdf(pdf);
        static TextGeometry Geometry(double x, double y, double width, double height) => new()
        { LocalBounds = new(new(x, y), new(width, height)), RotationCenter = new(x + width / 2, y + height / 2), IsGeometryLocked = true };
        var label = new OcrTextRegion { OriginalText = "1", EditedText = "Chapter", OriginalGeometry = Geometry(20, 350, 28, 8), EditedGeometry = Geometry(20, 350, 28, 8),
            WritingMode = WritingMode.Horizontal, HasExplicitWritingMode = true, FlowDirection = TextFlowDirection.LeftToRight };
        var number = new OcrTextRegion { OriginalText = "1", OriginalGeometry = Geometry(28, 330, 10, 16), EditedGeometry = Geometry(28, 330, 10, 16),
            WritingMode = WritingMode.Vertical, HasExplicitWritingMode = true, FlowDirection = TextFlowDirection.TopToBottom };
        var title = label with { Id = Guid.NewGuid(), OriginalText = "Node.jsの基礎知識", EditedText = "",
            OriginalGeometry = Geometry(75, 330, 170, 16), EditedGeometry = Geometry(75, 330, 170, 16) };
        var tombstone = label with { Id = Guid.NewGuid(), OriginalText = "Deleted old OCR", EditedText = "", IsDeleted = true };
        var input = Path.Combine(output, "chapter-mixed-input.pdfocrproj");
        await packages.SaveAsync(input, new() { SourcePdf = await packages.CreateSourceReferenceAsync(pdf), Pages = [
            new() { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [label, tombstone, number, title], ReadingOrder = [label.Id, number.Id, title.Id] },
            new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400 }] });
        await vm.LoadProjectForDiagnosticsAsync(input); await vm.EnsureDocumentMetadataAsync();
        check(vm.OverlayItems.Any(row => row.Id == tombstone.Id && row.IsDeleted),
            "A deleted persisted OCR row retains a fallback ordinal colliding with the live number, reproducing the real-project condition.");
        vm.DocumentViewMode = DocumentViewMode.SinglePage; vm.DocumentPageFlowMode = DocumentPageFlowMode.PageByPage;
        vm.EditorModeIndex = 0; vm.EditUnitIndex = (int)OcrEditUnit.Paragraph; vm.IsOcrOverlayVisible = vm.ShowOcrFrames = true; vm.ZoomPercent = 50;
        var canvas = (ListBox)main.FindName("OverlayCanvas");
        async Task Settle() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        async Task Click(string name)
        {
            var button = (Button)main.FindName(name);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke(); await Settle();
        }
        var a = vm.OverlayItems.Single(row => row.Id == label.Id); var b = vm.OverlayItems.Single(row => row.Id == number.Id);
        canvas.SelectedItems.Clear(); canvas.SelectedItems.Add(a); canvas.SelectedItems.Add(b); await Settle();
        check(vm.SelectedOverlayCount == 2 && !a.IsVertical && b.IsVertical && a.IsGeometryLocked && b.IsGeometryLocked,
            "The user case selects horizontal Chapter plus a locked vertical digit, with different stored flow directions.");
        await Click("SeparateParagraphButton");
        check(a.ParagraphGroupId == Guid.Empty && b.ParagraphGroupId == Guid.Empty, "The real Separate button assigns the individual-row state.");
        var beforeA = a.Capture(); var beforeB = b.Capture();
        var groupButton = (Button)main.FindName("GroupParagraphButton");
        check(groupButton.IsEnabled && vm.GroupParagraphCommand.CanExecute(null), "Group stays enabled after Separate for the mixed-direction Chapter/number selection.");
        await Click("GroupParagraphButton");
        var group = a.ParagraphGroupId;
        check(group is { } id && id != Guid.Empty && group == b.ParagraphGroupId && vm.HasUnsavedChanges,
            "Invoking the real Group button records one paragraph for Chapter and 1.");
        check(a.Capture() with { ParagraphGroupId = beforeA.ParagraphGroupId } == beforeA && b.Capture() with { ParagraphGroupId = beforeB.ParagraphGroupId } == beforeB,
            "Grouping leaves corrected text, orientation, positions, character advances, review metadata and locks unchanged.");
        check(vm.ResolveEditUnitSelection(a).Select(row => row.Id).SequenceEqual(new[] { a.Id, b.Id }),
            "Paragraph editing selects both mixed-direction items as one paragraph.");
        await picture((FrameworkElement)main.Content, "chapter-one-grouped", 1400, 850);
        var outline = (OcrParagraphLayer)main.FindName("ParagraphOutlineLayer");
        check(outline.Frames.Any(frame => frame.RegionIds.SequenceEqual(new[] { a.Id, b.Id })), "One visible paragraph outline surrounds Chapter and its vertical number.");
        vm.UndoCommand.Execute(null); await Settle();
        check(a.ParagraphGroupId == Guid.Empty && b.ParagraphGroupId == Guid.Empty && groupButton.IsEnabled, "Undo restores Separate and immediately re-enables Group.");
        vm.RedoCommand.Execute(null); await Settle();
        check(a.ParagraphGroupId == group && b.ParagraphGroupId == group, "Redo restores the exact shared paragraph ID.");
        var saved = Path.Combine(output, "chapter-mixed-saved.pdfocrproj"); await vm.SaveProjectForDiagnosticsAsync(saved, ProjectPdfStorageMode.Embedded);
        var reopened = await packages.OpenAsync(saved); var stored = reopened.Pages[0].TextRegions;
        var savedLabel = stored.Single(row => row.Id == label.Id); var savedNumber = stored.Single(row => row.Id == number.Id);
        check(savedLabel.ParagraphGroupId == group && savedNumber.ParagraphGroupId == group && savedLabel.EffectiveText == "Chapter" && savedNumber.EffectiveText == "1" &&
            savedLabel.WritingMode == WritingMode.Horizontal && savedNumber.WritingMode == WritingMode.Vertical && savedNumber.FlowDirection == TextFlowDirection.TopToBottom,
            "Actual saving retains the mixed paragraph, corrected Chapter label and each item's original direction.");
        await vm.LoadProjectForDiagnosticsAsync(saved); await vm.EnsureDocumentMetadataAsync();
        a = vm.OverlayItems.Single(row => row.Id == label.Id); b = vm.OverlayItems.Single(row => row.Id == number.Id);
        check(vm.ResolveEditUnitSelection(a).Count == 2 && b.IsVertical, "Reloading preserves one mixed-direction paragraph without rotating the digit.");
        await using (var worker = new PdfNativeWorkerClient(reuseInputs: true))
        {
            var speechPage = await vm.ReadSpeechPageAsync(vm.CreateSpeechTicket(ReadAloudScope.CurrentPage), 1, worker, default);
            check(SpeechParagraphs.Build(speechPage.Segments)[0].Text == "Chapter 1", "Real application speech planning reads the saved mixed label as Chapter 1.");
            check(speechPage.Segments.All(row => row.RegionId != tombstone.Id), "Deleted OCR text is never spoken or reintroduced into the merged paragraph.");
        }
        vm.SetOverlaySelection([a, vm.OverlayItems.Single(row => row.Id == title.Id)], a); await Settle();
        check(!groupButton.IsEnabled && ToolTipService.GetShowOnDisabled(groupButton) && vm.GroupParagraphToolTip.Contains("未選択", StringComparison.Ordinal),
            "Nonconsecutive selection remains guarded and its disabled button exposes the specific reason.");
    }
}
