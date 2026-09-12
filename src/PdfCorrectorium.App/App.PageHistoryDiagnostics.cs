using System.IO;
using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>ページ構成操作とOCR編集を混在させたUndo/Redoを、ダイアログなしで検証します。</summary>
    private async Task RunPageHistoryTestAsync(MainWindow window, string[] arguments, int optionIndex)
    {
        try
        {
            if (arguments.Length <= optionIndex + 1)
                throw new ArgumentException("--page-history-test requires a new output directory.");
            var directory = Path.GetFullPath(arguments[optionIndex + 1]);
            if (Directory.Exists(directory))
                throw new IOException("Use a new output directory to avoid overwriting test results.");
            Directory.CreateDirectory(directory);

            var checks = new List<string>();
            window.ClosePromptOverride = () => MessageBoxResult.No;
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add(message);
            }

            var viewModel = (MainWindowViewModel)window.DataContext;
            var basePdf = Path.Combine(directory, "base-two-pages.pdf");
            var insertedPdf = Path.Combine(directory, "inserted-two-pages.pdf");
            WriteDocumentUiTestPdf(basePdf);
            WriteDocumentUiTestPdf(insertedPdf);

            // OCR編集 → ページ並べ替え → Undo/Redo の順序と、OCRモデル参照の復元を検証します。
            await viewModel.LoadPdfForDiagnosticsAsync(basePdf);
            Check(PdfNativeWorkerClient.Shared.WorkerProcessId is int workerProcessId && workerProcessId != Environment.ProcessId,
                "PDF preview is rendered by a reusable isolated native worker process.");
            Check(PdfNativeWorkerClient.Shared.WorkerHasResourceJob,
                "The reusable native worker is contained by Windows process-count and memory limits.");
            var marker = viewModel.AddManualOcrRegion(new Rect(20, 30, 100, 24))
                ?? throw new InvalidOperationException("Could not create the OCR history marker.");
            marker.Text = "page-one-marker";
            var markerId = marker.Id;
            viewModel.SetPageSelection([viewModel.PageItems[0]]);
            var beforeReorderPath = viewModel.ResolvedPdfPathForDiagnostics;
            await viewModel.ReorderSelectedPagesAsync(1);
            Check(viewModel.PageItems.Count == 2 && viewModel.SelectedPage?.PageNumber == 2,
                "Reorder keeps two pages and selects the moved page at its new position.");
            Check(viewModel.ResolvedPdfPathForDiagnostics == beforeReorderPath &&
                  viewModel.PageWorkingFileCountForDiagnostics == 0 && viewModel.UndoCommand.CanExecute(null),
                "Reorder stores a logical page sequence without creating a working PDF.");
            Check(viewModel.OverlayItems.Any(region => region.Id == markerId && region.Text == "page-one-marker"),
                "Reorder carries the edited OCR region to its new page number.");

            await viewModel.UndoForDiagnosticsAsync();
            Check(viewModel.ResolvedPdfPathForDiagnostics == beforeReorderPath && viewModel.SelectedPage?.PageNumber == 1,
                "Undo reorder restores the logical order while retaining the immutable source PDF.");
            Check(viewModel.OverlayItems.Contains(marker) && marker.Text == "page-one-marker",
                "Undo reorder restores the same OCR model referenced by older OCR history.");
            await viewModel.UndoForDiagnosticsAsync();
            Check(marker.Text.Length == 0, "Undo after page undo reaches and reverses the preceding OCR text edit.");
            await viewModel.RedoForDiagnosticsAsync();
            Check(marker.Text == "page-one-marker", "Redo reapplies the OCR text edit before the page operation.");
            await viewModel.RedoForDiagnosticsAsync();
            Check(viewModel.SelectedPage?.PageNumber == 2 &&
                  viewModel.OverlayItems.Any(region => region.Id == markerId && region.Text == "page-one-marker"),
                "Redo reorder reapplies the page mapping and OCR content.");

            // 回転ではPDFとOCRページ寸法の双方が前後の復元点へ戻ることを確認します。
            await viewModel.LoadPdfForDiagnosticsAsync(basePdf);
            viewModel.SetPageSelection([viewModel.PageItems[0]]);
            var rotationSourcePath = viewModel.ResolvedPdfPathForDiagnostics;
            var originalWidth = viewModel.ProjectForDiagnostics!.Pages.FirstOrDefault()?.WidthPoints ?? 300;
            await viewModel.RotateSelectedPagesForDiagnosticsAsync(90);
            var rotatedPath = viewModel.ResolvedPdfPathForDiagnostics;
            Check(rotatedPath == rotationSourcePath &&
                  viewModel.ProjectForDiagnostics!.PageSequence[0].RotationDegrees == 90 &&
                  viewModel.ProjectForDiagnostics.Pages[0].RotationDegrees == 90 &&
                  viewModel.PageWorkingFileCountForDiagnostics == 0,
                "Rotate stores logical rotation and transformed OCR geometry without creating a working PDF.");
            await viewModel.UndoForDiagnosticsAsync();
            Check(viewModel.ResolvedPdfPathForDiagnostics == rotationSourcePath &&
                  viewModel.ProjectForDiagnostics!.PageSequence[0].RotationDegrees == 0 &&
                  viewModel.ProjectForDiagnostics.Pages[0].RotationDegrees == 0 &&
                  Math.Abs(viewModel.ProjectForDiagnostics.Pages[0].WidthPoints - originalWidth) < 0.01,
                "Undo rotate restores the source PDF and OCR page geometry.");
            await viewModel.RedoForDiagnosticsAsync();
            Check(viewModel.ResolvedPdfPathForDiagnostics == rotatedPath &&
                  viewModel.ProjectForDiagnostics!.PageSequence[0].RotationDegrees == 90 &&
                  viewModel.ProjectForDiagnostics.Pages[0].RotationDegrees == 90,
                "Redo rotate restores logical rotation and OCR page geometry.");
            var logicalExport = Path.Combine(directory, "logical-rotation-export.pdf");
            await viewModel.ExportPdfForDiagnosticsAsync(logicalExport);
            var exportedPreview = await new PdfPreviewService().RenderPageAsync(logicalExport, 1, 240);
            var qpdf = PdfBookmarkService.ResolveQpdfPath()
                       ?? throw new FileNotFoundException("qpdf was not found for logical-page verification.");
            var exportedStructure = await ExternalProcessRunner.RunAsync(
                qpdf,
                ["--json", logicalExport, "-"],
                TimeSpan.FromSeconds(10));
            Check(exportedPreview.PageCount == 2 && exportedStructure.StandardOutput.Contains("\"/Rotate\": 90", StringComparison.Ordinal),
                "Final export materializes logical page rotation exactly once.");

            // 追加と削除のページ数、選択状態、作業PDF切替を往復します。
            await viewModel.LoadPdfForDiagnosticsAsync(basePdf);
            var insertSourcePath = viewModel.ResolvedPdfPathForDiagnostics;
            await viewModel.InsertPagesForDiagnosticsAsync(insertedPdf, 1);
            var insertedWorkingPath = viewModel.ResolvedPdfPathForDiagnostics;
            Check(viewModel.PageItems.Count == 4 && viewModel.SelectedPage?.PageNumber == 2,
                "Insert adds all source pages at the requested location.");
            await viewModel.UndoForDiagnosticsAsync();
            Check(viewModel.PageItems.Count == 2 && viewModel.ResolvedPdfPathForDiagnostics == insertSourcePath,
                "Undo insert restores the original page count and PDF.");
            await viewModel.RedoForDiagnosticsAsync();
            Check(viewModel.PageItems.Count == 4 && viewModel.ResolvedPdfPathForDiagnostics == insertedWorkingPath,
                "Redo insert restores the inserted pages and working PDF.");
            Check(insertedWorkingPath is not null && insertedWorkingPath.Length < 260,
                "Page working filenames remain below the legacy qpdf Windows path limit in this workspace.");
            var insertedProject = Path.Combine(directory, "inserted-pages.pdfocrproj");
            await viewModel.SaveProjectForDiagnosticsAsync(insertedProject);
            await viewModel.LoadProjectForDiagnosticsAsync(insertedProject);
            Check(viewModel.PageItems.Count == 4,
                "A project saved after redo embeds and reloads the restored four-page working PDF.");

            await viewModel.LoadPdfForDiagnosticsAsync(basePdf);
            viewModel.SetPageSelection([viewModel.PageItems[1]]);
            var deleteSourcePath = viewModel.ResolvedPdfPathForDiagnostics;
            await viewModel.DeleteSelectedPagesForDiagnosticsAsync();
            var deletedWorkingPath = viewModel.ResolvedPdfPathForDiagnostics;
            Check(viewModel.PageItems.Count == 1, "Delete removes the selected page without modifying the source PDF.");
            await viewModel.UndoForDiagnosticsAsync();
            Check(viewModel.PageItems.Count == 2 && viewModel.ResolvedPdfPathForDiagnostics == deleteSourcePath,
                "Undo delete restores the removed logical page and retains the source PDF.");
            await viewModel.RedoForDiagnosticsAsync();
            Check(viewModel.PageItems.Count == 1 && viewModel.ResolvedPdfPathForDiagnostics == deletedWorkingPath,
                "Redo delete restores the one-page logical sequence without replacing the source PDF.");

            // Undo後の新規編集は通常どおりRedo枝を破棄します。
            await viewModel.UndoForDiagnosticsAsync();
            Check(viewModel.RedoCommand.CanExecute(null), "Undo page operation exposes redo.");
            var replacementMarker = viewModel.AddManualOcrRegion(new Rect(40, 50, 80, 20));
            Check(replacementMarker is not null && !viewModel.RedoCommand.CanExecute(null) && viewModel.RedoCountForDiagnostics == 0,
                "A new OCR edit after undo clears the obsolete page redo branch.");
            Check(File.Exists(deletedWorkingPath) && viewModel.PageWorkingFileCountForDiagnostics == 0,
                "Discarding a logical redo branch does not create or delete the immutable source PDF.");

            var timeoutObserved = false;
            try
            {
                await ExternalProcessRunner.RunAsync(
                    Path.Combine(AppContext.BaseDirectory, "PdfCorrectorium.exe"),
                    ["--diagnostic-delay-worker", "5"],
                    TimeSpan.FromMilliseconds(250));
            }
            catch (TimeoutException) { timeoutObserved = true; }
            Check(timeoutObserved, "External PDF tools are terminated when their operation deadline expires.");

            var outputLimitObserved = false;
            try
            {
                await ExternalProcessRunner.RunAsync(
                    Path.Combine(AppContext.BaseDirectory, "PdfCorrectorium.exe"),
                    ["--diagnostic-output-worker", "4096"],
                    TimeSpan.FromSeconds(5),
                    maximumOutputCharacters: 1024);
            }
            catch (InvalidDataException) { outputLimitObserved = true; }
            Check(outputLimitObserved, "External PDF tool output is rejected when it exceeds the collection limit.");

            var importLimitDirectory = Path.Combine(directory, "import-limits");
            Directory.CreateDirectory(importLimitDirectory);
            var oversizedOcr = Path.Combine(importLimitDirectory, "oversized.json");
            await File.WriteAllTextAsync(oversizedOcr, new string('x', 64));
            var ocrLimitObserved = false;
            try { await new NdlOcrCompanionService { MaximumImportBytes = 32 }.ImportAsync(oversizedOcr); }
            catch (InvalidDataException) { ocrLimitObserved = true; }
            Check(ocrLimitObserved, "Oversized NDLOCR companion files are rejected before parsing.");

            var oversizedBookmarks = Path.Combine(importLimitDirectory, "oversized.bookmarks.json");
            await File.WriteAllTextAsync(oversizedBookmarks, new string('x', 64));
            var bookmarkLimitObserved = false;
            try { await new PdfBookmarkService { MaximumImportBytes = 32 }.ImportAsync(oversizedBookmarks); }
            catch (InvalidDataException) { bookmarkLimitObserved = true; }
            Check(bookmarkLimitObserved, "Oversized bookmark exchange files are rejected before parsing.");

            // 回転は差分履歴だけを積み、PDFファイル数と容量を一切増やさないことを確認します。
            await viewModel.LoadPdfForDiagnosticsAsync(basePdf);
            viewModel.SetPageSelection([viewModel.PageItems[0]]);
            for (var index = 0; index < PageHistoryRetentionPolicy.DefaultMaximumWorkingFileCount + 1; index++)
                await viewModel.RotateSelectedPagesForDiagnosticsAsync(90);
            Check(viewModel.PageWorkingFileCountForDiagnostics == 0,
                "Repeated logical rotations create no working PDFs.");
            Check(viewModel.PageWorkingFileBytesForDiagnostics == 0,
                "Repeated logical rotations consume no working-PDF bytes.");
            Check(viewModel.UndoCountForDiagnostics == PageHistoryRetentionPolicy.DefaultMaximumWorkingFileCount + 1,
                "Logical page histories remain available because they do not consume the working-PDF budget.");

            // 複数プロセス／複数画面が同じ作業ルートを同時初期化しても、作成途中の
            // セッションを放棄済みと誤認して削除しないことを回帰検証します。
            var concurrentWorkspace = Path.Combine(directory, "concurrent-session-workspace");
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            {
                using var store = new PageWorkingFileStore(concurrentWorkspace);
                store.CreatePath();
            })));
            Check(!Directory.EnumerateDirectories(Path.Combine(concurrentWorkspace, "page-edits")).Any(),
                "Concurrent working-file sessions initialize and dispose without racing over session locks.");

            // ページ履歴の容量制限は、途中を抜かず新しい側の連続した履歴だけを残します。
            var retentionWorkspace = Path.Combine(directory, "retention-workspace");
            using (var retentionStore = new PageWorkingFileStore(retentionWorkspace))
            {
                var oldestPath = retentionStore.CreatePath();
                var middlePath = retentionStore.CreatePath();
                var currentPath = retentionStore.CreatePath();
                await File.WriteAllBytesAsync(oldestPath, new byte[40]);
                await File.WriteAllBytesAsync(middlePath, new byte[40]);
                await File.WriteAllBytesAsync(currentPath, new byte[40]);

                var policy = new PageHistoryRetentionPolicy(retentionStore, maximumWorkingFileCount: 2, maximumWorkingBytes: 100);
                string?[][] newestFirst = [[middlePath, currentPath], [], [oldestPath, middlePath]];
                var decision = policy.Evaluate(newestFirst, 100, [currentPath], entry => entry);
                Check(decision.RetainedEntryCount == 2 && decision.StorageLimitReached,
                    "Page-history storage limits retain only the newest contiguous history prefix.");
                Check(decision.RetainedWorkingFileCount == 2 && decision.RetainedWorkingBytes == 80,
                    "Shared working PDFs are counted once against both file and byte limits.");

                var countDecision = policy.Evaluate(newestFirst, 1, [currentPath], entry => entry);
                Check(countDecision.RetainedEntryCount == 1 && countDecision.CountLimitReached,
                    "The ordinary Undo entry limit and the page-working-file limits are enforced together.");

                var missingPath = retentionStore.CreatePath();
                var missingDecision = policy.Evaluate<string?[]>([[missingPath]], 100, [currentPath], entry => entry);
                Check(missingDecision.RetainedEntryCount == 0 && missingDecision.StorageLimitReached,
                    "A history boundary with a missing owned PDF is discarded before it can break Undo.");

                retentionStore.DeleteUnreferenced([middlePath, currentPath]);
                Check(!File.Exists(oldestPath) && retentionStore.FileCount == 2,
                    "Working PDFs outside the retained history prefix are reclaimed immediately.");
            }

            viewModel.ReleaseTransientResources();
            Check(viewModel.PageWorkingFileCountForDiagnostics == 0,
                "Closing an editing session releases all page working files.");

            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks.Select(check => "PASS: " + check));
            _diagnostics?.Write("page-history-test.passed", $"{checks.Count} checks passed. {directory}");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            _diagnostics?.Write("page-history-test.failed", exception.ToString());
            Shutdown(1);
        }
    }
}
