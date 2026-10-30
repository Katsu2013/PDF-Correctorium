using System.IO;
using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private static async Task RunLinkLockGeometryChecksAsync(MainWindowViewModel vm, string blank, Action<bool, string> check)
    {
        await vm.LoadPdfForDiagnosticsAsync(blank);
        vm.EditorMode = EditorInteractionMode.OcrEditing;
        var region = vm.AddManualOcrRegion(new Rect(100, 200, 120, 40))!;
        region.Text = "LOCK";
        var link = await vm.CreateInternalLinkAsync(2, null, "OCR-following lock");
        region.Left += 37; region.Top += 17;
        vm.ActivateLinkEditMode();
        vm.SelectLink(vm.LinkListItems.Single(item => item.Id == link!.Id), false);
        var frame = vm.LinkAreaItems.Single();
        var displayed = new Rect(frame.Left, frame.Top, frame.Width, frame.Height);
        vm.SetSelectedLinksLocked(true);
        frame = vm.LinkAreaItems.Single();
        var frozen = vm.ProjectForDiagnostics!.InternalLinks.Single().SourceBounds;
        check(new Rect(frame.Left, frame.Top, frame.Width, frame.Height) == displayed && frozen != link!.SourceBounds,
            "Locking an OCR-following link freezes its latest displayed geometry, not the old project rectangle.");
        vm.UndoCommand.Execute(null);
        check(!vm.ProjectForDiagnostics.InternalLinks.Single().IsGeometryLocked &&
            new Rect(vm.LinkAreaItems.Single().Left, vm.LinkAreaItems.Single().Top,
                vm.LinkAreaItems.Single().Width, vm.LinkAreaItems.Single().Height) == displayed,
            "Undoing a lock preserves the latest OCR-following position.");
        vm.RedoCommand.Execute(null);
        region.Left += 25;
        vm.EditorMode = EditorInteractionMode.OcrEditing;
        vm.ActivateLinkEditMode();
        check(vm.ProjectForDiagnostics.InternalLinks.Single().IsGeometryLocked &&
            vm.ProjectForDiagnostics.InternalLinks.Single().SourceBounds == frozen,
            "A locked OCR-following frame does not follow subsequent OCR geometry changes.");
    }

    private static async Task RunBatchPageReadinessChecksAsync(MainWindowViewModel vm, string directory, Action<bool, string> check)
    {
        var single = Path.Combine(directory, "batch-ready-single.pdf");
        WritePersistenceTextPdf(single, "Chapter 2");
        var input = Path.Combine(directory, "batch-ready-20.pdf");
        await new PdfPageManagementService().MaterializeAsync(single, Enumerable.Range(1, 20)
            .Select(_ => new ProjectPageReference { SourcePageNumber = 1 }).ToArray(), input);
        var pages = Enumerable.Range(1, 20).Select(number => new ProjectPageReference
            { PageId = Guid.NewGuid(), SourcePageNumber = number }).ToArray();
        var linked = Path.Combine(directory, "batch-ready-linked.pdf");
        await new PdfExportService().ExportAsync(input, linked, new PdfCorrectoriumProject
        {
            SourcePdf = await new ProjectPackageService().CreateSourceReferenceAsync(input, directory),
            PageSequence = pages,
            InternalLinks = new[] { 0, 19 }.Select(index => new PdfInternalLink
            {
                SourcePageId = pages[index].PageId,
                SourceBounds = new PdfRectangle(new PdfPoint(0, 0), new PdfSize(300, 400)),
                DestinationPageId = pages[1].PageId
            }).ToArray()
        });
        var fixture = Path.Combine(directory, "batch-ready-rotated-crop.pdf");
        await CreateLinkDiagnosticFixtureAsync(linked, fixture, directory, rotateAndCrop: true);
        var backgroundStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var afterPriorityStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completionGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batches = new List<int[]>();
        PdfSourceLinkService.Session? session = null;
        vm.DocumentPageScanOverride = async (path, sequence, physicalPages, token, coordinatesOnly) =>
        {
            batches.Add(physicalPages.ToArray());
            session ??= await PdfSourceLinkService.CreateSessionAsync(path, sequence, token);
            if (batches.Count == 2)
            {
                backgroundStarted.TrySetResult();
                await backgroundGate.Task.WaitAsync(token);
            }
            if (batches.Count == 4)
            {
                afterPriorityStarted.TrySetResult();
                await completionGate.Task.WaitAsync(token);
            }
            return await session.ReadPagesAsync(physicalPages, coordinatesOnly);
        };
        try
        {
            await vm.LoadPdfPreviewForDiagnosticsAsync(fixture);
            await backgroundStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var cancellation = new CancellationTokenSource();
            var cancelled = vm.BuildBatchCandidatesAsync(DocumentBatchKind.AutomaticLinks, [20], "", 0, false, null, cancellation.Token);
            check(!cancelled.IsCompleted, "An offscreen batch waits for that page's metadata rather than using fallback coordinates.");
            cancellation.Cancel();
            try { await cancelled.WaitAsync(TimeSpan.FromSeconds(2)); check(false, "Metadata wait must cancel promptly."); }
            catch (OperationCanceledException) { check(vm.IsDocumentMetadataLoading && vm.CanEditDocument,
                "Cancelling a batch metadata wait leaves the shared reader and current-page editing available."); }
            var pending = vm.BuildBatchCandidatesAsync(DocumentBatchKind.AutomaticLinks, [20], "", 0, false, null, CancellationToken.None);
            backgroundGate.TrySetResult();
            await afterPriorityStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var candidates = await pending.WaitAsync(TimeSpan.FromSeconds(20));
            check(batches[2].Contains(20) && vm.IsDocumentMetadataLoading && vm.SelectedPage?.PageNumber == 1,
                "A batch prioritizes its required offscreen page without changing navigation or waiting for the whole PDF.");
            var candidate = candidates.Single(item => item.DestinationPage == 2);
            check(!candidate.IsIncluded && candidate.Status.Contains("重複"),
                "Overlap checks include links imported after the batch's initial project snapshot.");
            var preview = await new PdfPreviewService().RenderPageAsync(fixture, 20, 1200);
            var area = candidate.PreviewAreas.Single();
            var expected = new PdfRectangle(new PdfPoint(
                10 + area.Top / preview.Image.PixelHeight * preview.PageHeightPoints,
                20 + area.Left / preview.Image.PixelWidth * preview.PageWidthPoints),
                new PdfSize(area.Height / preview.Image.PixelHeight * preview.PageHeightPoints,
                    area.Width / preview.Image.PixelWidth * preview.PageWidthPoints));
            var actual = candidate.Link!.SourceBounds!.Value;
            check(Math.Abs(actual.Left - expected.Left) < .01 && Math.Abs(actual.Bottom - expected.Bottom) < .01 &&
                Math.Abs(actual.Size.Width - expected.Size.Width) < .01 && Math.Abs(actual.Size.Height - expected.Size.Height) < .01,
                "Background candidate geometry uses the ready page's inherited 90-degree rotation and nonzero crop origin.");
            var replaced = vm.BuildBatchCandidatesAsync(DocumentBatchKind.AutomaticLinks, [19], "", 0, false, null, CancellationToken.None);
            check(!replaced.IsCompleted, "A later unread batch waits independently.");
            vm.DocumentPageScanOverride = null;
            await vm.LoadPdfPreviewForDiagnosticsAsync(single);
            try { await replaced.WaitAsync(TimeSpan.FromSeconds(5)); check(false, "Old-document candidates must not survive replacement."); }
            catch (OperationCanceledException) { check(true, "Replacing a document discards a waiting batch's stale candidates."); }
        }
        finally
        {
            backgroundGate.TrySetResult(); completionGate.TrySetResult();
            vm.DocumentPageScanOverride = null;
            await vm.EnsureDocumentMetadataAsync();
        }
    }
}
