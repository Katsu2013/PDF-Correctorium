using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunLinkEditingTestAsync(MainWindow window, string[] args, int option)
    {
        string? directory = null;
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
        }
        try
        {
            if (args.Length <= option + 1) throw new ArgumentException("A new diagnostic output directory is required.");
            directory = Path.GetFullPath(args[option + 1]);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any(path => Path.GetFileName(path) != ".appstate"))
                throw new IOException("Use a new output directory.");
            Directory.CreateDirectory(directory);
            window.ClosePromptOverride = () => MessageBoxResult.No;
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.Left = -10000; window.Top = -10000;
            window.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var vm = (MainWindowViewModel)window.DataContext;
            vm.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            var root = (FrameworkElement)window.Content;
            async Task LayoutAsync()
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                root.Measure(new Size(1400, 850)); root.Arrange(new Rect(0, 0, 1400, 850)); root.UpdateLayout();
            }
            void Snapshot(string name)
            {
                var image = new RenderTargetBitmap(1400, 850, 96, 96, PixelFormats.Pbgra32);
                image.Render(root);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(Path.Combine(directory, name + ".png")); png.Save(stream);
            }
            var blank = Path.Combine(directory, "blank.pdf");
            await BlankPdfService.CreateAsync(blank, 3, 595, 842);
            var packages = new ProjectPackageService();
            var pages = Enumerable.Range(1, 3).Select(number => new OcrPage
                { Id = Guid.NewGuid(), PageNumber = number, WidthPoints = 595, HeightPoints = 842 }).ToArray();
            var rectangle = new PdfRectangle(new PdfPoint(36, 650), new PdfSize(120, 22));
            var project = new PdfCorrectoriumProject
            {
                SourcePdf = await packages.CreateSourceReferenceAsync(blank, directory), Pages = pages,
                PageSequence = pages.Select(page => new ProjectPageReference { PageId = page.Id, SourcePageNumber = page.PageNumber }).ToArray(),
                InternalLinks =
                [
                    new() { SourcePageId = pages[0].Id, SourceBounds = rectangle, DestinationPageId = pages[1].Id,
                        DestinationViewType = "/XYZ", DestinationViewParameters = [42, 700, 1.25],
                        DestinationPosition = new PdfPoint(42, 700), DestinationZoomPercent = 125, Description = "名前付き移動先" },
                    new() { SourcePageId = pages[0].Id, SourceBounds = new(new PdfPoint(36, 610), new PdfSize(120, 22)),
                        ExternalUri = "https://example.com/original", Description = "Webリンク" },
                ],
                TextDecorations = [new() { Kind = PdfTextDecorationKind.Header, TextTemplate = "日本語リンク選択テスト", Position = PdfTextDecorationPosition.TopLeft }],
            };
            var safe = Path.Combine(directory, "safe.pdf");
            await new PdfExportService().ExportAsync(blank, safe, project);
            var fixture = Path.Combine(directory, "named-indirect-unsafe.pdf");
            await CreateLinkDiagnosticFixtureAsync(safe, fixture, directory);
            var scanned = await PdfSourceLinkService.ReadAsync(fixture, project.PageSequence, CancellationToken.None);
            Check(scanned.Links.Count == 3 && scanned.UnsupportedActions == 1, "Safe and unsupported links are discoverable together.");
            var internalLink = scanned.Links.Single(link => link.DestinationPageId == pages[1].Id);
            Check(internalLink.DestinationViewType == "/XYZ" && internalLink.DestinationViewParameters.SequenceEqual(new double?[] { 42, 700, 1.25 }),
                "Indirect GoTo actions and names-tree destinations retain XYZ coordinates and zoom.");
            Check(scanned.Links.Single(link => link.UnsupportedAction is not null).UnsupportedAction == "/JavaScript",
                "JavaScript is classified as unsupported without execution.");

            var scanGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.DocumentMetadataScanOverride = async (path, sequence, token, coordinatesOnly) =>
            {
                await scanGate.Task.WaitAsync(token);
                return await PdfSourceLinkService.ReadAsync(path, sequence, token, coordinatesOnly);
            };
            await vm.LoadPdfPreviewForDiagnosticsAsync(fixture);
            Check(vm.HasDocument && vm.CanUsePreview && vm.IsDocumentMetadataLoading && !vm.CanEditDocument,
                "First-page preview is available before delayed metadata, without enabling premature edits.");
            await LayoutAsync(); Snapshot("00-progressive-loading");
            Check(await vm.CreateLinkAreaAsync(new Rect(10, 10, 100, 30), 2, null, "premature", null) is null &&
                vm.ProjectForDiagnostics!.InternalLinks.Count == 0,
                "Link creation cannot bypass the pending-metadata interaction guard.");
            await vm.SelectLinkPageAsync(2, Guid.Empty);
            Check(vm.SelectedPage?.PageNumber == 2 && vm.IsDocumentMetadataLoading,
                "Page navigation remains available while link import is pending.");
            var progressiveProject = Path.Combine(directory, "progressive.pdfocrproj");
            var pendingSave = vm.SaveProjectForDiagnosticsAsync(progressiveProject, ProjectPdfStorageMode.Relative);
            Check(!pendingSave.IsCompleted && !File.Exists(progressiveProject),
                "A save requested during background import waits before writing any project.");
            scanGate.SetResult();
            await pendingSave;
            Check((await packages.OpenAsync(progressiveProject)).InternalLinks.Count == 3 &&
                vm.ProjectForDiagnostics!.SourceLinksInitialized && vm.CanEditDocument,
                "The waiting save includes all imported links and editing is subsequently enabled.");
            Check(PdfNativeWorkerClient.Inspection.WorkerProcessId is { } inspectionPid &&
                inspectionPid != PdfNativeWorkerClient.Shared.WorkerProcessId &&
                inspectionPid != PdfNativeWorkerClient.Background.WorkerProcessId &&
                PdfNativeWorkerClient.Inspection.WorkerHasResourceJob,
                "Whole-document inspection runs in a separate resource-limited native worker.");

            var coordinateReads = 0;
            vm.DocumentMetadataScanOverride = async (path, sequence, token, coordinatesOnly) =>
            {
                if (coordinatesOnly) Interlocked.Increment(ref coordinateReads);
                var result = await PdfSourceLinkService.ReadAsync(path, sequence, token, coordinatesOnly);
                Check(!coordinatesOnly || result.Links.Count == 0, "Initialized projects skip annotation/action re-import.");
                return result;
            };
            await vm.LoadProjectForDiagnosticsAsync(progressiveProject);
            Check(coordinateReads == 1 && vm.ProjectForDiagnostics!.InternalLinks.Count == 3,
                "Opening a saved project reuses its imported link data.");

            var obsoleteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.DocumentMetadataScanOverride = async (path, sequence, token, coordinatesOnly) =>
            {
                scanStarted.TrySetResult();
                await obsoleteGate.Task.WaitAsync(token);
                return await PdfSourceLinkService.ReadAsync(path, sequence, token, coordinatesOnly);
            };
            await vm.LoadPdfPreviewForDiagnosticsAsync(fixture);
            await scanStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var obsoleteCompletion = vm.EnsureDocumentMetadataAsync();
            vm.DocumentMetadataScanOverride = null;
            await vm.LoadPdfForDiagnosticsAsync(blank);
            try { await obsoleteCompletion; Check(false, "Obsolete document waits must be cancelled."); }
            catch (OperationCanceledException) { Check(true, "Replacing a document cancels old metadata and pending snapshot operations."); }
            Check(vm.ProjectForDiagnostics!.InternalLinks.Count == 0 && vm.InputPdfIssues.All(issue => issue.Code != "links.unsupported_actions"),
                "Old links and warnings cannot appear in the replacement document.");

            vm.DocumentMetadataScanOverride = (_, _, _, _) =>
                Task.FromException<PdfSourceLinkService.ScanResult>(new InvalidDataException("Injected metadata read failure."));
            await vm.LoadPdfPreviewForDiagnosticsAsync(fixture);
            try { await vm.EnsureDocumentMetadataAsync(); Check(false, "Import failure must block saving."); }
            catch (InvalidOperationException) { Check(true, "A metadata failure is reported and does not permit an incomplete save."); }
            Check(vm.CanUsePreview && !vm.CanEditDocument && vm.InputPdfIssues.Any(issue => issue.Code == "links.read.failed"),
                "Import failure keeps the preview usable and shows its reason.");
            vm.DocumentMetadataScanOverride = null;
            await vm.LoadPdfForDiagnosticsAsync(fixture);
            vm.ActivateLinkEditMode();
            await LayoutAsync();
            Check(vm.IsLinkEditMode && vm.IsLinkOverlayDisplayed && vm.CurrentPageLinkCount == 3, "Link edit mode lists existing page links.");
            Check(((StackPanel)window.FindName("LinkEditingPanel")).Visibility == Visibility.Visible, "Link properties are shown in link mode.");
            var overlays = (ItemsControl)window.FindName("LinkAreaOverlayLayer");
            Check(overlays.Visibility == Visibility.Visible && overlays.IsHitTestVisible, "Link frames are visible and selectable in link mode.");
            Snapshot("01-existing-links");
            Check(vm.GetLinkOverlaysForPage(2, 1200, 1698).Count == 0, "A page without links has no stale link frames.");
            var unsafeItem = vm.LinkListItems.Single(item => !item.IsEditable);
            vm.SelectedLinkArea = unsafeItem;
            Check(!vm.CanEditLinkSelection && !await vm.NavigateSelectedInternalLinkAsync(), "Unsupported actions cannot be edited or followed.");
            vm.DeleteSelectedInternalLink();
            Check(vm.CurrentPageLinkCount == 3, "Unsupported source annotations remain untouched.");

            vm.SelectedLinkArea = vm.LinkListItems.Single(item => item.Link.DestinationViewType == "/XYZ");
            var original = vm.GetSelectedInternalLink()!;
            var bounds = new Rect(vm.SelectedLinkArea.Left, vm.SelectedLinkArea.Top, vm.SelectedLinkArea.Width, vm.SelectedLinkArea.Height);
            await vm.CreateLinkAreaAsync(bounds, vm.GetInternalLinkDestinationPage(original), original.DestinationZoomPercent, "説明を編集", null);
            Check(vm.GetSelectedInternalLink()!.DestinationViewParameters.SequenceEqual(original.DestinationViewParameters),
                "Description edits preserve original destination view coordinates.");
            vm.UpdateSelectedLinkGeometry(new Rect(bounds.Left + 10, bounds.Top + 5, bounds.Width + 15, bounds.Height + 4), commit: true);
            Check(vm.GetSelectedInternalLink()!.SourceBounds != original.SourceBounds, "Rectangle links support move and resize.");
            vm.UndoCommand.Execute(null);
            Check(vm.GetSelectedInternalLink()!.SourceBounds == original.SourceBounds, "Geometry change is one undo step.");
            vm.RedoCommand.Execute(null);
            Check(vm.GetSelectedInternalLink()!.SourceBounds != original.SourceBounds, "Link geometry can be redone.");

            vm.SelectedLinkArea = null;
            vm.BeginLinkTextSelection();
            var textAreas = vm.GetLinkTextSelectionPreview(new Rect(0, 0, vm.PreviewPixelWidth, 180));
            Check(textAreas.Count > 0, "Native Japanese opaque PDF text is selectable.");
            var created = await vm.CreateTextLinkAsync(new Rect(0, 0, vm.PreviewPixelWidth, 180), 3, null, "文字リンク", null);
            vm.EndLinkTextSelection();
            Check(created is not null && created.SourceAreas.Count > 0, "Text links store glyph-based click areas.");
            var count = vm.CurrentPageLinkCount;
            vm.DeleteSelectedInternalLink();
            Check(vm.CurrentPageLinkCount == count - 1, "Selected text link can be deleted.");
            vm.UndoCommand.Execute(null);
            Check(vm.CurrentPageLinkCount == count, "Deleted text link can be restored by undo.");

            vm.EditorMode = EditorInteractionMode.OcrEditing; vm.IsLinkOverlayVisible = false;
            await LayoutAsync();
            Check(overlays.Visibility == Visibility.Collapsed && !overlays.IsHitTestVisible, "Hidden links do not intercept OCR interaction.");
            vm.IsLinkOverlayVisible = true;
            await LayoutAsync();
            Check(overlays.Visibility == Visibility.Visible && !overlays.IsHitTestVisible, "Visible links outside link mode are display-only.");
            vm.ActivateLinkEditMode(); vm.EditorMode = EditorInteractionMode.OcrEditing;
            Check(vm.IsLinkOverlayVisible, "Leaving link edit mode preserves the explicit visibility preference.");
            vm.ActivateLinkEditMode();

            var horizontal = new PdfSelectableTextCharacter[]
            {
                new("A", 10, 10, 10, 12, false), new("B", 20, 10, 10, 12, false),
                new("C", 10, 40, 10, 12, false), new("D", 20, 40, 10, 12, false),
            };
            var bands = PdfTextSelectionService.BuildAreas(horizontal, false);
            Check(bands.Count == 2 && bands.All(band => !band.Contains(new Point(15, 30))), "Multiline click areas leave row gaps nonclickable.");
            var vertical = horizontal.Select(c => c with { Left = c.Top, Top = c.Left }).ToArray();
            Check(PdfTextSelectionService.BuildAreas(vertical, true).Count == 2, "Vertical text forms independent column areas.");
            foreach (var rotation in new[] { 90, 180, 270 })
            {
                var rotated = MainWindowViewModel.RotateLinkBounds(rectangle, 595, 842, rotation);
                var restored = MainWindowViewModel.RotateLinkBounds(rotated, rotation == 180 ? 595 : 842, rotation == 180 ? 842 : 595, 360 - rotation);
                Check(restored == rectangle, $"Link coordinate conversion round-trips {rotation} degrees.");
            }

            vm.DocumentViewMode = DocumentViewMode.FacingPages;
            vm.FacingPagesShowCoverSeparately = false;
            await vm.RenderPageForDiagnosticsAsync(1);
            await LayoutAsync();
            var companionReady = false;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (vm.HasNextPreview) { companionReady = true; break; }
                await Task.Delay(50);
            }
            Check(companionReady, "Facing view loads the companion preview.");
            await LayoutAsync();
            Snapshot("02-facing-links");
            vm.DocumentPageFlowMode = DocumentPageFlowMode.Continuous;
            await LayoutAsync();
            Snapshot("03-continuous-links");
            vm.DocumentPageFlowMode = DocumentPageFlowMode.PageByPage;
            vm.DocumentViewMode = DocumentViewMode.SinglePage;

            await vm.SelectLinkPageAsync(2, Guid.Empty);
            Check(vm.CurrentPageLinkCount == 0, "Navigation clears the previous page's link list.");
            await vm.SelectLinkPageAsync(1, Guid.Empty);
            Check(vm.CurrentPageLinkCount == count, "Returning restores the source page's link list.");

            var saved = Path.Combine(directory, "links.pdfocrproj");
            await vm.SaveProjectForDiagnosticsAsync(saved, ProjectPdfStorageMode.Embedded);
            var reopened = await packages.OpenAsync(saved);
            Check(reopened.LinkOverlayVisible && reopened.InternalLinks.Any(link => link.SourceAreas.Count > 0), "Visibility and text link areas survive project save.");
            var output = Path.Combine(directory, "edited.pdf");
            await vm.ExportPdfForDiagnosticsAsync(output);
            var readback = await PdfSourceLinkService.ReadAsync(output, reopened.PageSequence, CancellationToken.None);
            Check(readback.UnsupportedActions == 1 && readback.Links.Count == reopened.InternalLinks.Count(link => link.IsEnabled), "Export preserves unsafe originals and writes safe links without duplication.");
            Check(readback.Links.Single(link => link.Description == "説明を編集").DestinationViewParameters.SequenceEqual(original.DestinationViewParameters),
                "Output retains original XYZ destination view after description/geometry edits.");

            var multi = project.InternalLinks[1] with
            {
                SourceBounds = new(new PdfPoint(36, 400), new PdfSize(120, 62)),
                SourceAreas = [new(new PdfPoint(36, 400), new PdfSize(120, 22)), new(new PdfPoint(36, 440), new PdfSize(90, 22))],
            };
            var grouped = Path.Combine(directory, "grouped.pdf");
            await new PdfExportService().ExportAsync(blank, grouped, project with { InternalLinks = [multi], TextDecorations = [] });
            var groupImport = await PdfSourceLinkService.ReadAsync(grouped, project.PageSequence, CancellationToken.None);
            Check(groupImport.Links.Count == 1 && groupImport.Links[0].SourceAreas.Count == 2 && groupImport.Links[0].SourceAnnotations.Count == 2,
                "Separate PDF annotations reimport as one editable multiline link.");
            var groupedEdited = Path.Combine(directory, "grouped-edited.pdf");
            await new PdfExportService().ExportAsync(grouped, groupedEdited, project with
            { InternalLinks = [groupImport.Links[0] with { ExternalUri = "https://example.com/changed", IsModified = true }], TextDecorations = [] });
            var groupedReadback = await PdfSourceLinkService.ReadAsync(groupedEdited, project.PageSequence, CancellationToken.None);
            Check(groupedReadback.Links.Count == 1 && groupedReadback.Links[0].SourceAreas.Count == 2 &&
                groupedReadback.Links[0].ExternalUri == "https://example.com/changed", "Editing a multiline source link replaces every original annotation.");

            await vm.LoadPdfForDiagnosticsAsync(grouped);
            vm.ActivateLinkEditMode();
            vm.SelectedLinkArea = vm.LinkAreaItems[1];
            await LayoutAsync();
            Check(ReferenceEquals(vm.SelectedLinkArea, vm.LinkListItems[0]) && vm.LinkAreaItems.All(item => item.IsSelected),
                "Clicking a secondary multiline area selects its whole logical link and the canonical list item.");

            await vm.LoadPdfForDiagnosticsAsync(fixture);
            vm.EditorMode = EditorInteractionMode.OcrEditing;
            var ocr = vm.AddManualOcrRegion(new Rect(60, 500, 240, 36))!;
            ocr.Text = "修正済OCR";
            vm.ActivateLinkEditMode();
            Check(string.Concat(vm.LinkCharactersForDiagnostics.Where(character => character.IsInvisible).Select(character => character.Text)) == ocr.Text,
                "Edited OCR cells are used instead of stale invisible source text.");
            var ocrLink = await vm.CreateTextLinkAsync(new Rect(60, 500, 240, 36), 2, null, "OCR文字リンク", null);
            Check(ocrLink?.SourceAreas.Count > 0, "Transparent edited OCR supports character-based links.");

            await vm.LoadPdfForDiagnosticsAsync(safe);
            vm.ActivateLinkEditMode();
            var pageLink = vm.LinkListItems.First(item => item.Link.ExternalUri is null);
            var rawBounds = pageLink.Link.SourceBounds!.Value;
            await vm.RotateSelectedPagesForDiagnosticsAsync(90);
            vm.SelectedLinkArea = vm.LinkListItems.First(item => item.Link.ExternalUri is null);
            var rotatedArea = vm.SelectedLinkArea!;
            vm.UpdateSelectedLinkGeometry(new Rect(rotatedArea.Left, rotatedArea.Top, rotatedArea.Width, rotatedArea.Height), commit: true);
            var rawAfter = vm.GetSelectedInternalLink()!.SourceBounds!.Value;
            Check(Math.Abs(rawAfter.Left - rawBounds.Left) < .001 && Math.Abs(rawAfter.Bottom - rawBounds.Bottom) < .001 &&
                Math.Abs(rawAfter.Size.Width - rawBounds.Size.Width) < .001, "Logical page rotation preserves raw source link geometry.");

            var cropped = Path.Combine(directory, "rotated-cropped.pdf");
            await CreateLinkDiagnosticFixtureAsync(safe, cropped, directory, rotateAndCrop: true);
            var croppedScan = await PdfSourceLinkService.ReadAsync(cropped, project.PageSequence, CancellationToken.None);
            Check(croppedScan.PageCoordinates[1].Rotation == 90 && croppedScan.PageCoordinates[1].Origin == new PdfPoint(10, 20),
                "Inherited page rotation and nonzero crop origin are imported.");
            var baseline = await new PdfPreviewService().RenderPageAsync(safe, 1, 96);
            var croppedPreview = await new PdfPreviewService().RenderPageAsync(cropped, 1, 96);
            var firstGlyph = baseline.SelectableCharacters.First(character => !character.IsInvisible);
            var rawGlyph = new PdfRectangle(new PdfPoint(firstGlyph.Left / baseline.Image.PixelWidth * 595,
                842 - (firstGlyph.Top + firstGlyph.Height) / baseline.Image.PixelHeight * 842),
                new PdfSize(firstGlyph.Width / baseline.Image.PixelWidth * 595, firstGlyph.Height / baseline.Image.PixelHeight * 842));
            var cropRelative = rawGlyph with { Origin = new PdfPoint(rawGlyph.Left - 10, rawGlyph.Bottom - 20) };
            var rotatedGlyph = MainWindowViewModel.RotateLinkBounds(cropRelative, 575, 812, 90);
            var cropGlyph = croppedPreview.SelectableCharacters.First(character => !character.IsInvisible);
            Check(Math.Abs(cropGlyph.Left - rotatedGlyph.Left / 812 * croppedPreview.Image.PixelWidth) < .2 &&
                  Math.Abs(cropGlyph.Top - (575 - rotatedGlyph.Top) / 575 * croppedPreview.Image.PixelHeight) < .2,
                "Visible text selection follows PDFium rotation and crop rather than raw glyph coordinates.");
            await vm.LoadPdfForDiagnosticsAsync(cropped);
            vm.ActivateLinkEditMode();
            vm.SelectedLinkArea = vm.LinkListItems.First(item => item.Link.ExternalUri is null && item.IsEditable);
            var croppedOriginal = vm.GetSelectedInternalLink()!.SourceBounds!.Value;
            var cropArea = vm.SelectedLinkArea!;
            vm.UpdateSelectedLinkGeometry(new Rect(cropArea.Left, cropArea.Top, cropArea.Width, cropArea.Height), commit: true);
            var croppedAfter = vm.GetSelectedInternalLink()!.SourceBounds!.Value;
            static bool CloseBounds(PdfRectangle a, PdfRectangle b) => Math.Abs(a.Left - b.Left) < .001 &&
                Math.Abs(a.Bottom - b.Bottom) < .001 && Math.Abs(a.Size.Width - b.Size.Width) < .001 &&
                Math.Abs(a.Size.Height - b.Size.Height) < .001;
            Check(CloseBounds(croppedAfter, croppedOriginal),
                $"Rotated/cropped link geometry round-trips through the editor: {croppedAfter} / {croppedOriginal}.");
            var croppedOutput = Path.Combine(directory, "cropped-edited.pdf");
            await vm.ExportPdfForDiagnosticsAsync(croppedOutput);
            var croppedReadback = await PdfSourceLinkService.ReadAsync(croppedOutput, vm.ProjectForDiagnostics!.PageSequence, CancellationToken.None);
            Check(croppedReadback.Links.Count == 3 && croppedReadback.Links.Any(link => link.SourceBounds is { } bounds && CloseBounds(bounds, croppedOriginal)),
                "Rotated/cropped existing links export without duplication or geometry drift.");

            var legacyImported = internalLink with { Id = Guid.NewGuid(), DestinationViewType = null, DestinationViewParameters = [] };
            var upgraded = MainWindowViewModel.UpgradeImportedLinks(project with
            { InternalLinks = [legacyImported], SourceLinksInitialized = true, SourceLinkImportVersion = 0 }, scanned);
            Check(upgraded.InternalLinks.Count == 3 && upgraded.InternalLinks.First().Id == legacyImported.Id &&
                upgraded.InternalLinks.First().DestinationViewParameters.SequenceEqual(internalLink.DestinationViewParameters),
                "Legacy imported link IDs persist while complete source destination/annotation metadata is restored.");
            var deletedLegacy = legacyImported with { IsEnabled = false, IsModified = true };
            var upgradedDeletion = MainWindowViewModel.UpgradeImportedLinks(project with { InternalLinks = [deletedLegacy] }, scanned);
            Check(!upgradedDeletion.InternalLinks.First().IsEnabled && upgradedDeletion.InternalLinks.First().IsModified,
                "Import upgrades do not resurrect deleted legacy source links.");
            var oldDisplayed = MainWindowViewModel.RotateLinkBounds(
                rectangle with { Origin = new PdfPoint(rectangle.Left - 10, rectangle.Bottom - 20) }, 575, 812, 90);
            var oldRectangle = new PdfInternalLink { SourcePageId = pages[0].Id, SourceBounds = oldDisplayed, DestinationPageId = pages[1].Id };
            var upgradedRectangle = MainWindowViewModel.UpgradeImportedLinks(project with
            {
                Pages = pages.Select(page => page with { WidthPoints = 812, HeightPoints = 575 }).ToArray(),
                InternalLinks = [oldRectangle],
            }, croppedScan);
            Check(CloseBounds(upgradedRectangle.InternalLinks.First().SourceBounds!.Value, rectangle),
                "Legacy displayed rectangle links migrate once to rotated/cropped raw source coordinates.");

            var redactedProject = project with
            {
                SourcePdf = await packages.CreateSourceReferenceAsync(fixture, directory), TextDecorations = [],
                InternalLinks = scanned.Links.Select(link => link.ExternalUri is null ? link :
                    link with { ExternalUri = "https://example.com/outside-redaction", IsModified = true }).ToArray(),
                Redactions = [new() { PageId = pages[0].Id, Bounds = rectangle, ColorHex = "#000000" }],
            };
            var redactedOutput = Path.Combine(directory, "redacted-links.pdf");
            var redactionResult = await new PdfExportService().ExportAsync(fixture, redactedOutput, redactedProject);
            var redactedReadback = await PdfSourceLinkService.ReadAsync(redactedOutput, project.PageSequence, CancellationToken.None);
            Check(redactedReadback.Links.Count == 1 && redactedReadback.Links[0].ExternalUri == "https://example.com/outside-redaction",
                "Raster redaction excludes covered/unsupported links and retains edited safe links outside the redaction.");
            Check(redactionResult.AppliedRedactions == 1 && redactedProject.InternalLinks.Count == 3,
                "Redaction link filtering applies only to output, not the editable project.");

            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
            _diagnostics?.Write("link-editing-test.passed", $"{checks.Count} checks passed. {directory}");
            Shutdown(0);
        }
        catch (Exception error)
        {
            if (directory is not null)
            {
                File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
                File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString());
            }
            _diagnostics?.Write("link-editing-test.failed", error.ToString()); Shutdown(1);
        }
    }

    private static async Task CreateLinkDiagnosticFixtureAsync(string source, string destination, string directory, bool rotateAndCrop = false)
    {
        var qpdf = PdfBookmarkService.ResolveQpdfPath() ?? throw new FileNotFoundException("qpdf not found.");
        var result = await ExternalProcessRunner.RunAsync(qpdf, ["--json-output=2", source, "-"], TimeSpan.FromMinutes(1));
        if (result.ExitCode != 0) throw new InvalidDataException(result.StandardError);
        var json = JsonNode.Parse(result.StandardOutput)!;
        var objects = json["qpdf"]![1]!.AsObject();
        var nextId = objects.Where(pair => pair.Key.StartsWith("obj:")).Max(pair => int.Parse(pair.Key[4..].Split(' ')[0])) + 1;
        string Add(JsonNode value)
        {
            var reference = $"{nextId++} 0 R";
            objects["obj:" + reference] = new JsonObject { ["value"] = value };
            return reference;
        }
        var originalLink = objects.Select(pair => pair.Value?["value"] as JsonObject)
            .First(value => value?["/Subtype"]?.GetValue<string>() == "/Link" && value["/Dest"] is JsonArray)!;
        var target = originalLink["/Dest"]!.DeepClone();
        originalLink.Remove("/Dest");
        originalLink["/A"] = Add(new JsonObject { ["/S"] = "/GoTo", ["/D"] = "u:chapter" });
        var leaf = Add(new JsonObject { ["/Names"] = new JsonArray("u:chapter", target) });
        var tree = Add(new JsonObject { ["/Kids"] = new JsonArray(leaf) });
        var names = Add(new JsonObject { ["/Dests"] = tree });
        var catalog = objects.Select(pair => pair.Value?["value"] as JsonObject)
            .First(value => value?["/Type"]?.GetValue<string>() == "/Catalog")!;
        catalog["/Names"] = names;
        var unsupported = Add(new JsonObject { ["/Type"] = "/Annot", ["/Subtype"] = "/Link",
            ["/Rect"] = new JsonArray(36, 570, 156, 592), ["/Border"] = new JsonArray(0, 0, 0),
            ["/A"] = new JsonObject { ["/S"] = "/JavaScript", ["/JS"] = "u:do-not-execute" } });
        var page = objects.Select(pair => pair.Value?["value"] as JsonObject)
            .First(value => value?["/Type"]?.GetValue<string>() == "/Page" && value["/Annots"] is JsonArray)!;
        page["/Annots"]!.AsArray().Add(unsupported);
        if (rotateAndCrop)
        {
            foreach (var pageValue in objects.Select(pair => pair.Value?["value"] as JsonObject)
                         .Where(value => value?["/Type"]?.GetValue<string>() == "/Page"))
                pageValue!["/CropBox"] = new JsonArray(10, 20, 585, 832);
            foreach (var pagesValue in objects.Select(pair => pair.Value?["value"] as JsonObject)
                         .Where(value => value?["/Type"]?.GetValue<string>() == "/Pages"))
                pagesValue!["/Rotate"] = 90;
        }
        foreach (var key in objects.Where(pair => pair.Value?["stream"] is not null).Select(pair => pair.Key).ToArray()) objects.Remove(key);
        var update = Path.Combine(directory, "fixture-update.json");
        await File.WriteAllTextAsync(update, json.ToJsonString());
        var write = await ExternalProcessRunner.RunAsync(qpdf, [source, "--update-from-json=" + update, destination], TimeSpan.FromMinutes(1));
        if (write.ExitCode != 0) throw new InvalidDataException(write.StandardError);
    }
}
