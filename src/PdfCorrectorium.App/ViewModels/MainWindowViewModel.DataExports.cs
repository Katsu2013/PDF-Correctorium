using System.IO;
using System.Runtime.CompilerServices;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

public enum DocumentDataExportKind { OcrText, CommentsCsv, PageImages }

public sealed partial class MainWindowViewModel
{
    /// <summary>Explicit export operation. Work text/comments are deliberately not redaction-sanitized.</summary>
    internal async Task<string> ExportDocumentDataAsync(DocumentDataExportKind kind, string destination,
        IReadOnlyList<int> pages, bool pageHeadings, bool jpeg, int dpi, int jpegQuality,
        IProgress<string>? progress, CancellationToken token)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!CanEditDocument || _project is null || _resolvedPdfPath is null)
            throw new InvalidOperationException("編集可能な文書を開いてください。");
        var selected = pages.Distinct().Order().ToArray();
        if (selected.Length == 0 || selected.Any(page => page < 1 || page > PageItems.Count))
            throw new ArgumentException("対象ページが正しくありません。");
        // Only already-authorized local inputs. Source-reference hints remain untrusted and must not be probed.
        var protectedInputs = new string?[] { _resolvedPdfPath, _projectFilePath }
            .Concat(_ndlOcrDocument?.CompanionFiles ?? []).ToArray();
        if (kind != DocumentDataExportKind.PageImages && !string.Equals(Path.GetExtension(destination),
                kind == DocumentDataExportKind.OcrText ? ".txt" : ".csv", StringComparison.OrdinalIgnoreCase))
            throw new IOException("出力形式に合う拡張子（.txt／.csv）を指定してください。");
        _renderCancellation?.Cancel();
        CancelThumbnailLoading(clearImages: false);
        IsPdfExporting = true;
        var temporary = Path.Combine(_paths.CacheDirectory, "data-exports", Guid.NewGuid().ToString("N"));
        try
        {
            BeginBackgroundOperation("データを書き出しています…");
            SynchronizeProjectPages();
            var snapshot = _project;
            if (kind == DocumentDataExportKind.OcrText)
            {
                await DocumentDataWriter.WriteAtomicAsync(destination, protectedInputs,
                    stream => DocumentDataWriter.WriteTextAsync(stream,
                        ReadOcrExportPagesAsync(snapshot, selected, progress, token), pageHeadings, token), token);
                return Path.GetFullPath(destination);
            }
            if (kind == DocumentDataExportKind.CommentsCsv)
            {
                await Task.Run(() => DocumentDataWriter.WriteAtomicAsync(destination, protectedInputs,
                    stream => DocumentDataWriter.WriteCommentsAsync(stream, snapshot, token), token), token);
                return Path.GetFullPath(destination);
            }
            Directory.CreateDirectory(temporary);
            progress?.Report("編集内容と墨消しを反映したPDFを生成・検証しています…");
            await EnsureDocumentMetadataAsync();
            token.ThrowIfCancellationRequested();
            SynchronizeProjectPages();
            var prepared = await PrepareMaterializedProjectSourceAsync();
            string completed;
            try
            {
                var exportProgress = new Progress<PdfExportProgress>(value => progress?.Report(value.Message));
                var result = await Task.Run(() => _isolatedExportService.ExportAsync(prepared.PdfPath,
                    Path.Combine(temporary, "completed.pdf"), prepared.Project, exportProgress, token), token);
                completed = result.OutputPath;
            }
            finally { DeleteTemporaryDirectory(prepared.TemporaryDirectory); }
            return await PageImageExportService.ExportAsync(completed, destination, selected,
                dpi, jpeg, jpegQuality, progress, token);
        }
        finally
        {
            DeleteTemporaryDirectory(temporary);
            IsPdfExporting = false;
            EndBackgroundOperation();
            if (ShowPageThumbnails) StartThumbnailLoading();
        }
    }

    private async IAsyncEnumerable<OcrTextExportPage> ReadOcrExportPagesAsync(PdfCorrectoriumProject snapshot,
        IReadOnlyList<int> pages, IProgress<string>? progress,
        [EnumeratorCancellation] CancellationToken token)
    {
        var savedPages = snapshot.Pages.ToDictionary(page => page.PageNumber);
        await using var worker = new PdfNativeWorkerClient(reuseInputs: true);
        foreach (var page in pages)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"OCR作業文字を書き出し中：PDF {page}ページ");
            IReadOnlyList<string> lines;
            if (_pageOverlays.TryGetValue(page, out var cached))
                lines = cached.Where(region => !region.IsDeleted).OrderBy(region => region.ReadingOrder).Select(region => region.Text).ToArray();
            else if (savedPages.TryGetValue(page, out var saved) && saved.TextRegions.Count > 0)
            {
                var order = saved.ReadingOrder.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
                lines = saved.TextRegions.Where(region => !region.IsDeleted)
                    .OrderBy(region => order.GetValueOrDefault(region.Id, int.MaxValue)).Select(region => region.EffectiveText).ToArray();
            }
            else if (_ndlOcrDocument?.Pages.TryGetValue(snapshot.PageSequence[page - 1].SourcePageNumber, out var companion) == true)
                lines = companion.Lines.Where(line => !string.IsNullOrWhiteSpace(line.Text)).Select(line => line.Text).ToArray();
            else
            {
                // Unvisited native OCR is read one page at a time, with one staged input per distinct source.
                var preview = await Task.Run(() => RenderProjectPageAsync(_resolvedPdfPath!, snapshot, page, 96,
                    token, useBackgroundWorker: true, batchWorker: worker), token);
                lines = preview.TextRegions.Select(region => region.Text).ToArray();
            }
            yield return new(page, lines);
            await Task.Yield();
        }
    }
}
