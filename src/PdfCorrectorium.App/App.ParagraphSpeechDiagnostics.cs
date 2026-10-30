using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyParagraphEditingAndNavigationAsync(MainWindow main, string output, Action<bool, string> check,
        Func<FrameworkElement, string, int, int, Task> picture)
    {
        static T Control<T>(Window window, string name) where T : class => (T)window.FindName(name);
        static void Click(Window window, string name) => Control<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Paragraph/navigation condition timed out."); await Task.Delay(15); }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        var vm = (MainWindowViewModel)main.DataContext; var packages = new ProjectPackageService();
        var pdf = Path.Combine(output, "paragraph-navigation.pdf"); WriteDocumentUiTestPdf(pdf);
        TextGeometry Geometry(double y, double width = 200) => new() { LocalBounds = new(new(20, y), new(width, 24)), RotationCenter = new(20 + width / 2, y + 12) };
        var first = new OcrTextRegion { OriginalText = "これは同じ段落の", OriginalGeometry = Geometry(300, 80), EditedGeometry = Geometry(300, 80) with { IsGeometryLocked = true } };
        var second = first with { Id = Guid.NewGuid(), OriginalText = "続きです。", OriginalGeometry = Geometry(270), EditedGeometry = Geometry(270) };
        var third = first with { Id = Guid.NewGuid(), OriginalText = "次の段落です。", OriginalGeometry = Geometry(190), EditedGeometry = Geometry(190) };
        var last = first with { Id = Guid.NewGuid(), OriginalText = "第二ページ", OriginalGeometry = Geometry(280), EditedGeometry = Geometry(280) };
        var page = new OcrPage { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [first, second, third], ReadingOrder = [first.Id, second.Id, third.Id] };
        var project = new PdfCorrectoriumProject { SourcePdf = await packages.CreateSourceReferenceAsync(pdf),
            Pages = [page, new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400, TextRegions = [last], ReadingOrder = [last.Id] }] };
        var input = Path.Combine(output, "paragraph-manual-input.pdfocrproj"); await packages.SaveAsync(input, project);
        vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
        await vm.LoadProjectForDiagnosticsAsync(input); await vm.EnsureDocumentMetadataAsync();
        vm.EditorModeIndex = 0; vm.EditUnitIndex = (int)OcrEditUnit.Paragraph;
        var row1 = vm.OverlayItems.First(row => row.Id == first.Id); var row2 = vm.OverlayItems.First(row => row.Id == second.Id); var row3 = vm.OverlayItems.First(row => row.Id == third.Id);
        check(vm.ResolveEditUnitSelection(row1).Count == 1, "Editor and speech now use the same conservative automatic paragraph boundaries.");
        await using (var worker = new Services.PdfNativeWorkerClient(reuseInputs: true))
        {
            var speechPage = await vm.ReadSpeechPageAsync(vm.CreateSpeechTicket(ReadAloudScope.CurrentPage), 1, worker, default);
            var speechParagraphs = SpeechParagraphs.Build(speechPage.Segments);
            check(speechParagraphs.All(paragraph => vm.ResolveEditUnitSelection(vm.OverlayItems.First(row => row.Id == paragraph.Spans[0].Segment.RegionId))
                .Select(row => (Guid?)row.Id).SequenceEqual(paragraph.Spans.Select(span => span.Segment.RegionId))) && worker.InputStageCount == 0,
                "All automatic editor selections exactly match speech partitions without rendering or staging the current PDF.");
        }
        vm.SetOverlaySelection([row1, row3], row1);
        check(!vm.GroupParagraphCommand.CanExecute(null), "Nonconsecutive OCR rows cannot silently absorb unselected intervening text.");
        vm.SetOverlaySelection([row1, row2], row1); var before1 = row1.Capture(); var before2 = row2.Capture();
        main.UpdateLayout();
        check(Control<Button>(main, "GroupParagraphButton") is { IsEnabled: true, IsVisible: true } button && ReferenceEquals(button.Command, vm.GroupParagraphCommand), "Visible OCR property button is bound and enabled for consecutive multi-row grouping.");
        check(vm.GroupParagraphCommand.CanExecute(null) && row1.IsGeometryLocked, "Metadata-only grouping remains available for geometry-locked rows.");
        vm.GroupParagraphCommand.Execute(null); var group = row1.ParagraphGroupId;
        check(group is { } id && id != Guid.Empty && row2.ParagraphGroupId == group && vm.HasUnsavedChanges, "Grouping records a shared manual paragraph and marks the project dirty.");
        check(row1.Capture() with { ParagraphGroupId = before1.ParagraphGroupId } == before1 && row2.Capture() with { ParagraphGroupId = before2.ParagraphGroupId } == before2,
            "Grouping retains text, character advances, review state, geometry and locks.");
        check(vm.ResolveEditUnitSelection(row1).Select(row => row.Id).SequenceEqual(new[] { row1.Id, row2.Id }), "Paragraph editing honors the explicit group despite mismatched geometry.");
        await picture((FrameworkElement)main.Content, "manual-paragraph-properties", 1400, 850);
        vm.UndoCommand.Execute(null);
        check(row1.ParagraphGroupId is null && row2.ParagraphGroupId is null, "One Undo restores both rows' automatic paragraph state.");
        vm.RedoCommand.Execute(null); vm.SeparateParagraphLinesCommand.Execute(null);
        check(row1.ParagraphGroupId == Guid.Empty && row2.ParagraphGroupId == Guid.Empty && vm.ResolveEditUnitSelection(row1).Count == 1, "Individual-row override keeps inferred neighbors separate.");
        vm.UndoCommand.Execute(null); vm.ResetParagraphGroupingCommand.Execute(null);
        check(row1.ParagraphGroupId is null && row2.ParagraphGroupId is null, "Reset removes the paragraph's manual grouping.");
        vm.UndoCommand.Execute(null);
        var saved = Path.Combine(output, "paragraph-manual-saved.pdfocrproj"); await vm.SaveProjectForDiagnosticsAsync(saved, ProjectPdfStorageMode.Embedded);
        var reopened = await packages.OpenAsync(saved);
        check(reopened.Pages[0].TextRegions.Take(2).All(row => row.ParagraphGroupId == group) && reopened.Pages[0].TextRegions[0].EditedGeometry.IsGeometryLocked,
            "Actual application saving preserves manual groups and geometry locks.");
        await vm.LoadProjectForDiagnosticsAsync(saved); await vm.EnsureDocumentMetadataAsync();
        row1 = vm.OverlayItems.First(row => row.Id == first.Id); row2 = vm.OverlayItems.First(row => row.Id == second.Id);
        vm.EditUnitIndex = (int)OcrEditUnit.Paragraph;
        check(vm.ResolveEditUnitSelection(row1).Count == 2 && row1.ParagraphGroupId == group, "Reloaded paragraph selection uses the saved group.");
        var dirty = vm.HasUnsavedChanges; var fake = new DiagnosticSpeech();
        var reader = new ReadAloudWindow(main, vm, fake);
        try
        {
            reader.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            await Until(() => Control<Button>(reader, "PlayButton").IsEnabled);
            Click(reader, "PlayButton"); await Until(() => fake.Texts.Count == 1);
            check(fake.Texts[0] == first.OriginalText + second.OriginalText, "Speech reads the saved manual paragraph as one utterance.");
            fake.Progress(first.OriginalText.Length + 1); await Until(() => main.CurrentSpeechSegment?.RegionId == second.Id);
            Control<ComboBox>(reader, "NavigationUnitBox").SelectedIndex = 1;
            Click(reader, "NextButton"); await Until(() => fake.Texts.Count == 2);
            check(fake.Texts[1] == third.OriginalText, "Next line crosses the paragraph boundary from its final row.");
            Click(reader, "PreviousButton"); await Until(() => fake.Texts.Count == 3);
            check(fake.Texts[2] == second.OriginalText, "Previous line starts at the previous paragraph's final row without replaying its beginning.");
            Control<ComboBox>(reader, "NavigationUnitBox").SelectedIndex = 0;
            Click(reader, "NextButton"); await Until(() => fake.Texts.Count == 4);
            Click(reader, "PauseButton"); await Until(() => fake.Paused);
            Click(reader, "PreviousButton"); await Until(() => main.CurrentSpeechSegment?.RegionId == first.Id);
            check(fake.Texts.Count == 4 && fake.Paused, "Navigation while paused moves the marker without starting audio.");
            var content = (FrameworkElement)reader.Content;
            content.Measure(new(reader.Width, double.PositiveInfinity));
            await picture(content, "read-aloud-navigation-paused", (int)reader.Width, (int)Math.Ceiling(content.DesiredSize.Height));
            Click(reader, "PauseButton"); await Until(() => fake.Texts.Count == 5);
            check(fake.Texts[4] == first.OriginalText + second.OriginalText, "Resume reads the newly selected paragraph from its beginning.");
            reader.StopPlayback(); await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Click(reader, "NextButton"); await Until(() => fake.Texts.Count == 6);
            check(fake.Texts[5] == third.OriginalText, "Navigation after Stop resumes from the retained position.");
            fake.Advance(); await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            check(Control<Button>(reader, "PreviousButton").IsEnabled && !Control<Button>(reader, "NextButton").IsEnabled, "At current-page completion, Previous remains available and Next respects the scope boundary.");
            Click(reader, "PreviousButton"); await Until(() => fake.Texts.Count == 7);
            reader.StopPlayback(); await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            check(vm.HasUnsavedChanges == dirty, "Speech skipping and unit changes do not dirty the project.");
        }
        finally { reader.StopPlayback(); reader.Close(); }

        fake = new DiagnosticSpeech(); reader = new ReadAloudWindow(main, vm, fake);
        try
        {
            reader.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); await Until(() => Control<Button>(reader, "PlayButton").IsEnabled);
            Control<ComboBox>(reader, "ScopeBox").SelectedIndex = 2;
            Click(reader, "PlayButton"); await Until(() => fake.Texts.Count == 1);
            Click(reader, "NextButton"); await Until(() => fake.Texts.Count == 2);
            var stale = fake.CurrentProgress;
            Click(reader, "NextButton"); await Until(() => fake.Texts.Count == 3);
            check(vm.SelectedPage?.PageNumber == 2 && fake.Texts[2] == last.OriginalText, "Continuous-scope Next crosses to the following page without preparing the full PDF.");
            stale?.Invoke(0); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(main.CurrentSpeechSegment?.RegionId == last.Id, "A cancelled prior-page progress callback cannot replace the current marker.");
            Click(reader, "PreviousButton"); await Until(() => fake.Texts.Count == 4);
            check(vm.SelectedPage?.PageNumber == 1 && fake.Texts[3] == third.OriginalText, "Previous crosses back to the preceding page's last paragraph.");
            reader.StopPlayback(); await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            vm.SetOverlaySelection([vm.OverlayItems.First(row => row.Id == first.Id)], vm.OverlayItems.First(row => row.Id == first.Id));
            vm.ResetParagraphGroupingCommand.Execute(null);
            check(!Control<Button>(reader, "PreviousButton").IsEnabled && !Control<Button>(reader, "NextButton").IsEnabled, "Paragraph edits invalidate retained speech positions instead of replaying stale grouping.");
        }
        finally { reader.StopPlayback(); reader.Close(); }

        var blankTail = Path.Combine(output, "paragraph-blank-tail.pdfocrproj");
        await packages.SaveAsync(blankTail, project with { Pages = [reopened.Pages[0], project.Pages[1] with { TextRegions = [], ReadingOrder = [] }] });
        await vm.LoadProjectForDiagnosticsAsync(blankTail); await vm.EnsureDocumentMetadataAsync();
        fake = new DiagnosticSpeech(); reader = new ReadAloudWindow(main, vm, fake);
        try
        {
            reader.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); await Until(() => Control<Button>(reader, "PlayButton").IsEnabled);
            Control<ComboBox>(reader, "ScopeBox").SelectedIndex = 2;
            Click(reader, "PlayButton"); await Until(() => fake.Texts.Count == 1);
            Click(reader, "NextButton"); await Until(() => fake.Texts.Count == 2);
            Click(reader, "PauseButton"); await Until(() => fake.Paused);
            Click(reader, "NextButton"); await Until(() => !Control<Button>(reader, "NextButton").IsEnabled && Control<Button>(reader, "PreviousButton").IsEnabled);
            check(fake.Texts.Count == 2 && fake.Paused && vm.SelectedPage?.PageNumber == 1 && reader.PlaybackTask is { IsCompleted: false },
                "Skipping across a blank tail preserves pause and position, disables Next, and does not replay the last paragraph.");
            Click(reader, "PreviousButton"); await Until(() => main.CurrentSpeechSegment?.RegionId == first.Id);
            check(fake.Texts.Count == 2 && fake.Paused, "Previous remains usable while paused after reaching a blank tail.");
            Click(reader, "PauseButton"); await Until(() => fake.Texts.Count == 3);
            Click(reader, "NextButton"); await Until(() => fake.Texts.Count == 4);
            fake.Advance(); await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            check(fake.Texts.Count == 4 && !Control<Button>(reader, "NextButton").IsEnabled && Control<Button>(reader, "PreviousButton").IsEnabled,
                "Automatic completion skips known blank pages and retains a valid scope boundary without duplicate speech.");
        }
        finally { reader.StopPlayback(); reader.Close(); }
    }
}
