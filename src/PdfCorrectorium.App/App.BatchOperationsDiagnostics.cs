using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunBatchAndPreferenceChecksAsync(MainWindow window, MainWindowViewModel vm, string blank,
        string directory, Action<bool, string> check, Func<Task> layout, Action<string> snapshot)
    {
        var originalSettings = vm.CurrentApplicationSettings;
        try
        {
            check(window.Title == PdfCorrectorium.Core.ApplicationBuildInfo.WindowTitle &&
                PdfCorrectorium.Core.ApplicationBuildInfo.AboutText.Contains(PdfCorrectorium.Core.ApplicationBuildInfo.NumericVersion),
                "Title and About text use the actual application build metadata.");
            await vm.ApplyApplicationSettingsAsync(originalSettings with { DefaultEditingMode = ProjectEditingMode.Review });
            await vm.LoadPdfForDiagnosticsAsync(blank);
            check(vm.IsReviewMode && !vm.HasUnsavedChanges, "New PDFs use the configured initial editing mode without a document edit.");
            vm.EditorMode = EditorInteractionMode.OcrEditing;
            check(!vm.HasUnsavedChanges, "A mode-only change does not dirty a document.");
            var saved = Path.Combine(directory, "mode-preferences.pdfocrproj");
            vm.EditorMode = EditorInteractionMode.LinkEditing;
            await vm.SaveProjectForDiagnosticsAsync(saved, ProjectPdfStorageMode.Relative);
            using (var archive = ZipFile.OpenRead(saved))
            using (var manifestStream = archive.GetEntry("manifest.json")!.Open())
            using (var manifest = await JsonDocument.ParseAsync(manifestStream))
                check(manifest.RootElement.GetProperty("applicationVersion").GetString() == PdfCorrectorium.Core.ApplicationBuildInfo.Version,
                    "Saved project application version matches the running binary.");
            vm.EditorMode = EditorInteractionMode.Redaction;
            check(!vm.HasUnsavedChanges, "Switching to redaction does not consolidate data or prompt to save.");
            await vm.OpenDocumentPathAsync(saved); await vm.EnsureDocumentMetadataAsync();
            check(vm.IsLinkEditMode, "Saved project mode takes priority over the application default.");
            var first = await vm.CreateLinkAreaAsync(new Rect(100, 150, 150, 40), 2, null, "基準", null);
            vm.SelectedLinkArea = null;
            var second = await vm.CreateLinkAreaAsync(new Rect(200, 250, 90, 25), 3, null, "調整対象", null);
            vm.SelectLink(vm.LinkListItems.Single(item => item.Id == first!.Id), false);
            vm.SelectLink(vm.LinkListItems.Single(item => item.Id == second!.Id), true);
            check(vm.SelectedLinkCount == 2 && vm.CanAlignLinks && vm.LinkAreaItems.All(item => item.IsSelected), "Ctrl-selection retains two frames and an ordered reference.");
            vm.AlignSelectedLinks("Left");
            check(vm.SelectedLinkCount == 2 && vm.LinkAreaItems.Select(item => item.Left).Distinct().Count() == 1, "Alignment retains selection and uses the first selected frame.");
            vm.AlignSelectedLinks("Size");
            check(vm.LinkAreaItems.Select(item => item.Width).Distinct().Count() == 1 && vm.LinkAreaItems.Select(item => item.Height).Distinct().Count() == 1, "Size matching updates all selected rectangle frames.");
            vm.LinkInputModeIndex = 1; await layout();
            check(!vm.CanAlignLinks && !vm.CanLockLinks && !vm.CanTransformSelectedLink &&
                !((FrameworkElement)window.FindName("LinkAlignmentIconPanel")).IsEnabled &&
                !((Button)window.FindName("LinkLockButton")).IsEnabled,
                "Switching to text input immediately disables frame alignment and lock controls through bindings.");
            vm.LinkInputModeIndex = 0; await layout();
            check(vm.CanAlignLinks && vm.CanLockLinks &&
                ((FrameworkElement)window.FindName("LinkAlignmentIconPanel")).IsEnabled &&
                ((Button)window.FindName("LinkLockButton")).IsEnabled,
                "Switching back immediately restores eligible frame controls without reselection.");
            vm.SetSelectedLinksLocked(true);
            check(vm.AreSelectedLinksLocked && !vm.CanAlignLinks && !vm.CanDeleteLinkSelection && !vm.CanTransformSelectedLink,
                "Locking blocks alignment, dragging, resizing and deletion.");
            var beforeDeletion = vm.ProjectForDiagnostics!.InternalLinks.Count;
            vm.DeleteSelectedInternalLink();
            check(vm.ProjectForDiagnostics.InternalLinks.Count == beforeDeletion, "The model refuses deletion even if invoked directly for locked links.");
            vm.SelectedLinkArea = vm.LinkListItems.First();
            var locked = vm.GetSelectedInternalLink()!;
            await vm.CreateLinkAreaAsync(new Rect(300, 350, 90, 30), 3, null, "ロック中も移動先編集", null);
            check(vm.GetSelectedInternalLink()!.SourceBounds == locked.SourceBounds && vm.GetSelectedInternalLink()!.IsGeometryLocked,
                "Destination edits cannot move a locked frame.");
            await vm.SaveProjectForDiagnosticsAsync(saved, ProjectPdfStorageMode.Relative);
            await vm.OpenDocumentPathAsync(saved); await vm.EnsureDocumentMetadataAsync(); vm.ActivateLinkEditMode();
            check(vm.LinkListItems.All(item => item.Link.IsGeometryLocked), "Link locks persist on project reload.");
            vm.SelectLink(vm.LinkListItems[0], false); vm.SelectLink(vm.LinkListItems[1], true);
            vm.SetSelectedLinksLocked(false); vm.DeleteSelectedInternalLink();
            check(vm.CurrentPageLinkCount == 0, "Unlocked multiple links can be deleted together.");
            vm.UndoCommand.Execute(null); check(vm.CurrentPageLinkCount == 2, "Multiple-link deletion is one Undo operation.");

            var phrase = DocumentTextRunService.Build([
                new("A", 0, 0, 10, 12, false), new(" ", 10, 0, 0, 0, false), new("B", 15, 0, 10, 12, false)]);
            check(DocumentTextRunService.Find(phrase, "A B", true).Count() == 1 && phrase[0].Slice(0, 3).Count == 2,
                "PDF spaces without an ink box remain searchable but do not create zero-area redactions.");

            var toolbar = window.CustomizableToolbars().First();
            var controls = toolbar.Items.OfType<FrameworkElement>().Where(item => ToolbarItem.GetKey(item).Length > 0).ToArray();
            check(controls.Length == 15 && controls.Select(ToolbarItem.GetKey).Distinct().Count() == controls.Length, "Every main toolbar operation has a unique stable visibility ID.");
            foreach (var control in controls) vm.SetToolbarItemVisible(ToolbarItem.GetKey(control), false);
            await layout();
            check(controls.All(control => control.Visibility == Visibility.Collapsed) && toolbar.Items.OfType<Separator>().All(separator => separator.Visibility == Visibility.Collapsed),
                "Hiding all toolbar items also removes empty separators; the menu recovery path remains available.");
            check(vm.OpenPdfCommand.CanExecute(null), "Hiding an operation does not disable its command.");
            vm.ResetToolbarItems(controls.Select(ToolbarItem.GetKey)); await layout();
            check(controls.All(control => control.Visibility == Visibility.Visible), "Restoring default visibility restores every button and input.");
            window.AvalonMainToolbar!.Float(); await layout();
            foreach (var control in controls) vm.SetToolbarItemVisible(ToolbarItem.GetKey(control), false);
            await layout(); await Task.Delay(150); await layout();
            var floating = PdfCorrectorium.App.MainWindow.FindFloatingToolbarWindow(window.AvalonMainToolbar);
            check(floating is not null && floating.Width < 200,
                "Hiding all floating toolbar operations shrinks the window rather than retaining an empty wide band.");
            vm.ResetToolbarItems(controls.Select(ToolbarItem.GetKey)); await layout(); await Task.Delay(150); await layout();
            check(floating!.Width > 400, "Restored floating controls receive enough width without manual resizing.");
            window.AvalonMainToolbar.Dock(); await layout();
            vm.SetToolbarLayout(ToolbarDockSide.Top, 0, 0, ToolbarDockSide.Top, 0, 1);
            window.SynchronizeAvalonDockLayout(); await layout();
            vm.SelectLink(vm.LinkListItems[0], false); vm.SelectLink(vm.LinkListItems[1], true);
            vm.ZoomPercent = 75; await layout();
            snapshot("04-preferences-and-link-layout");
            await RunIconConsistencyChecksAsync(window, vm, directory, check, layout, snapshot);
            await RunLinkLockGeometryChecksAsync(vm, blank, check);
            await RunBatchPageReadinessChecksAsync(vm, directory, check);

            var fontSingle = Path.Combine(directory, "batch-font-single.pdf");
            WritePersistenceTextPdf(fontSingle);
            var batchInput = Path.Combine(directory, "batch-font-input.pdf");
            await new PdfPageManagementService().MaterializeAsync(fontSingle, Enumerable.Range(1, 3)
                .Select(_ => new ProjectPageReference { SourcePageNumber = 1 }).ToArray(), batchInput);
            await vm.LoadPdfForDiagnosticsAsync(batchInput); vm.EditorMode = EditorInteractionMode.OcrEditing;
            var toc = vm.AddManualOcrRegion(new Rect(80, 200, 400, 32))!; toc.Text = "Chapter .... 2";
            var secret = vm.AddManualOcrRegion(new Rect(80, 300, 400, 32))!; secret.Text = "KEEP SECRET KEEP";
            var baselineCount = vm.ProjectForDiagnostics!.InternalLinks.Count;
            var auto = await vm.BuildBatchCandidatesAsync(DocumentBatchKind.AutomaticLinks, [1], "", 0, false, null, CancellationToken.None);
            check(auto.Count == 1 && auto[0].DestinationPage == 2 && vm.ProjectForDiagnostics.InternalLinks.Count == baselineCount,
                "Automatic page candidates map printed numbers and do not edit before confirmation.");
            var shifted = await vm.BuildBatchCandidatesAsync(DocumentBatchKind.AutomaticLinks, [1], "", 1, false, null, CancellationToken.None);
            check(shifted[0].DestinationPage == 3, "A user-defined printed/PDF offset maps destinations.");
            var invalid = await vm.BuildBatchCandidatesAsync(DocumentBatchKind.AutomaticLinks, [1], "", 20, false, null, CancellationToken.None);
            check(invalid.All(item => !item.CanInclude && !item.IsIncluded), "Out-of-document page targets cannot be selected.");
            vm.ApplyBatchCandidates(vm.BatchProjectId!.Value, auto);
            check(vm.ProjectForDiagnostics.InternalLinks.Count == baselineCount + 1, "Only confirmed automatic links are added.");
            var duplicates = await vm.BuildBatchCandidatesAsync(DocumentBatchKind.AutomaticLinks, [1], "", 0, false, null, CancellationToken.None);
            check(duplicates.All(item => !item.IsIncluded && item.Status.Contains("重複")), "Overlapping existing links are excluded by default.");
            vm.UndoCommand.Execute(null); check(vm.ProjectForDiagnostics.InternalLinks.Count == baselineCount, "Automatic links undo as a batch.");
            vm.RedoCommand.Execute(null);

            var textHits = await vm.BuildBatchCandidatesAsync(DocumentBatchKind.TextRedaction, [1], "SECRET", 0, true, null, CancellationToken.None);
            check(textHits.Count == 1 && textHits[0].Redactions.Count > 0 && textHits[0].PreviewAreas.Sum(area => area.Width) < secret.Width * .6,
                "Search redaction targets only matching glyphs, not the entire OCR row.");
            vm.ApplyBatchCandidates(vm.BatchProjectId.Value, textHits);
            check(vm.ProjectForDiagnostics.Redactions.Count == textHits[0].Redactions.Count, "Confirmed literal matches add non-destructive redaction ranges.");
            var textOutput = Path.Combine(directory, "search-redaction-result.pdf");
            await vm.ExportPdfForDiagnosticsAsync(textOutput);
            var textPreview = await new PdfPreviewService().RenderPageAsync(textOutput, 1);
            var outputText = string.Concat(textPreview.SelectableCharacters.Select(character => character.Text));
            check(!outputText.Contains("SECRET") && outputText.Split("KEEP").Length >= 3,
                "Search-redaction PDF output removes matched text while preserving both unaffected parts of the same OCR row.");
            vm.UndoCommand.Execute(null); check(vm.ProjectForDiagnostics.Redactions.Count == 0, "Search redaction has one batch Undo.");
            vm.EditorMode = EditorInteractionMode.Redaction;
            vm.AddManualRedaction(new Rect(50, 550, 100, 40));
            var repeat = await vm.BuildBatchCandidatesAsync(DocumentBatchKind.RepeatedRedaction, [1, 2, 3], "", 0, false, null, CancellationToken.None);
            check(repeat.Count == 2 && repeat.All(item => item.IsIncluded), "Range repetition excludes the original page and includes compatible pages.");
            vm.ApplyBatchCandidates(vm.BatchProjectId.Value, repeat);
            check(vm.ProjectForDiagnostics.Redactions.Count == 3, "Range repetition adds multiple pages in one operation.");
            vm.UndoCommand.Execute(null); check(vm.ProjectForDiagnostics.Redactions.Count == 1, "One Undo removes all repeated redactions.");
            await vm.RenderPageForDiagnosticsAsync(2); await vm.RotateSelectedPagesForDiagnosticsAsync(90);
            await vm.RenderPageForDiagnosticsAsync(1); vm.SelectedRedaction = vm.RedactionItems.First();
            var rotated = await vm.BuildBatchCandidatesAsync(DocumentBatchKind.RepeatedRedaction, [2], "", 0, false, null, CancellationToken.None);
            check(rotated.Count == 1 && !rotated[0].CanInclude, "Different page size/rotation requires manual redaction instead of silent coordinate reuse.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await vm.BuildBatchCandidatesAsync(DocumentBatchKind.TextRedaction, [1], "SECRET", 0, false, null, cancelled.Token); check(false, "Cancellation must stop candidates."); }
            catch (OperationCanceledException) { check(true, "Candidate cancellation does not apply edits."); }
            var batchWindow = new DocumentBatchWindow(vm, DocumentBatchKind.TextRedaction)
                { Owner = window, ShowActivated = false, ShowInTaskbar = false };
            batchWindow.Show();
            ((TextBox)batchWindow.FindName("SearchTextBox")).Text = "SECRET";
            await batchWindow.PreparePreviewForDiagnosticsAsync(textHits);
            var batchRoot = (FrameworkElement)batchWindow.Content;
            batchRoot.Measure(new Size(1080, 720)); batchRoot.Arrange(new Rect(0, 0, 1080, 720)); batchRoot.UpdateLayout();
            CheckDialogFooterIcons(batchWindow, check);
            check(((TextBox)batchWindow.FindName("PagesBox")).Text == "1-3", "The batch dialog provides an explicit editable page range.");
            var image = new RenderTargetBitmap(1080, 720, 96, 96, PixelFormats.Pbgra32); image.Render(batchRoot);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(directory, "05-batch-dialog.png"))) png.Save(stream);
            ((TextBox)batchWindow.FindName("SearchTextBox")).Text = "別の条件";
            check(batchWindow.Candidates.Count == 0 && !((Button)batchWindow.FindName("ApplyButton")).IsEnabled,
                "Changing a condition invalidates all old candidates before applying.");
            batchWindow.Close();
            await vm.SaveProjectForDiagnosticsAsync(Path.Combine(directory, "batch-result.pdfocrproj"), ProjectPdfStorageMode.Relative);
            var output = Path.Combine(directory, "batch-result.pdf");
            var exported = await vm.ExportPdfForDiagnosticsAsync(output);
            check(exported.AppliedRedactions > 0 && File.Exists(output), "Confirmed batches export through the existing redaction pipeline.");
            var links = await PdfSourceLinkService.ReadAsync(output, vm.ProjectForDiagnostics.PageSequence, CancellationToken.None);
            check(links.Links.Count > 0, "Automatic links are saved as PDF internal annotations alongside redactions.");
        }
        finally { await vm.ApplyApplicationSettingsAsync(originalSettings); }
    }
}
