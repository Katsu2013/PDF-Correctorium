using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyPdfReadingExportAsync(string output, Action<bool, string> check)
    {
        await VerifyPdfReadingHyphensAsync(output, check);
        var source = Path.Combine(output, "pronunciation-source.pdf"); WritePdfReadingFormFixture(source);
        var geometry = new TextGeometry { LocalBounds = new(new(25, 280), new(220, 24)), RotationCenter = new(135, 292), IsGeometryLocked = true };
        var first = new OcrTextRegion { OriginalText = "今日の日本語", OriginalGeometry = geometry, EditedGeometry = geometry,
            WordReadings = [new() { SurfaceText = "今日", ReadingText = "こんにち" }] };
        var second = first with { Id = Guid.NewGuid(), OriginalText = "今日", WordReadings = [],
            OriginalGeometry = geometry with { LocalBounds = new(new(25, 200), new(80, 24)) }, EditedGeometry = geometry with { LocalBounds = new(new(25, 200), new(80, 24)) } };
        var vertical = first with { Id = Guid.NewGuid(), OriginalText = "日本語", WordReadings = [], WritingMode = WritingMode.Vertical,
            OriginalGeometry = geometry with { LocalBounds = new(new(230, 100), new(24, 100)) }, EditedGeometry = geometry with { LocalBounds = new(new(230, 100), new(24, 100)) } };
        var pages = new OcrPage[] { new() { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [first, second], ReadingOrder = [first.Id, second.Id] },
            new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400, TextRegions = [vertical], ReadingOrder = [vertical.Id] } };
        PdfExportService.PopulateReadingFixture(source, pages);
        var packages = new ProjectPackageService();
        var project = new PdfCorrectoriumProject { SourcePdf = await packages.CreateSourceReferenceAsync(source), Pages = pages,
            WordReadings = [new() { SurfaceText = "今日", ReadingText = "きょう" }, new() { SurfaceText = "日本語", ReadingText = "にほんご" }], DocumentLanguage = "ja-JP" };
        var destination = Path.Combine(output, "pronunciation-export.pdf");
        var timer = Stopwatch.StartNew(); var exporter = new PdfExportService(); var exportResult = await exporter.ExportAsync(source, destination, project);
        var elapsed = timer.Elapsed.TotalMilliseconds;
        check(exportResult.SavedReadingRows == 3, "Reading-only export reports the three saved reading rows even when no glyph or geometry was edited.");
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(destination, 1).Order().SequenceEqual(new[] { "こんにちのにほんご", "きょう" }.Order()),
            "Fresh PDFium structure reads the real exported common readings and the row-specific override.");
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(destination, 2).SequenceEqual(new[] { "にほんご" }),
            "A vertical word split into separate native character objects shares one pronunciation structure element.");
        var exportedObjects = await ReadingPdfObjects(destination);
        var parentTrees = exportedObjects.Select(pair => pair.Value?["value"] as JsonObject)
            .Where(value => value?["/Nums"] is JsonArray).Select(value => value!["/Nums"]!.AsArray()).ToArray();
        check(parentTrees.Any(nums => nums.Where((_, index) => index % 2 == 1).OfType<JsonArray>().Select(array => array.Count).Order().SequenceEqual(new[] { 2, 4 })),
            "The complete parent map includes the ordinary text inside an untagged Form XObject, beyond the pronunciation rows.");
        var preview = new PdfPreviewService();
        static byte[] Pixels(BitmapSource image) { var stride = image.PixelWidth * 4; var data = new byte[stride * image.PixelHeight]; image.CopyPixels(data, stride, 0); return data; }
        for (var number = 1; number <= 2; number++)
        {
            var beforePixels = Pixels((await preview.RenderPageAsync(source, number, 600)).Image);
            var rendered = (await preview.RenderPageAsync(destination, number, 600)).Image;
            var afterPixels = Pixels(rendered);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(rendered));
            using (var file = File.Create(Path.Combine(output, $"pronunciation-page-{number}.png"))) png.Save(file);
            check(beforePixels.SequenceEqual(afterPixels), $"Page {number} remains pixel-identical; pronunciation metadata adds no visible ruby or glyphs.");
        }
        await using (var worker = new PdfNativeWorkerClient(reuseInputs: true))
        {
            for (var number = 1; number <= 2; number++)
            {
                var before = await worker.ReadSpeechPageAsync(source, number, default);
                var after = await worker.ReadSpeechPageAsync(destination, number, default);
                check(string.Concat(before.Regions.Select(row => row.Text)) == string.Concat(after.Regions.Select(row => row.Text)),
                    $"Page {number} keeps original extracted characters instead of substituting pronunciation kana.");
            }
        }
        var edited = project with { SourcePdf = await packages.CreateSourceReferenceAsync(destination),
            WordReadings = [new() { SurfaceText = "今日", ReadingText = "こんじつ" }, new() { SurfaceText = "日本語", ReadingText = "ニホンゴ" }] };
        var repeated = Path.Combine(output, "pronunciation-reexport.pdf"); await exporter.ExportAsync(destination, repeated, edited);
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(repeated, 1).Order().SequenceEqual(new[] { "こんにちのニホンゴ", "こんじつ" }.Order()),
            "Re-export updates exact existing tags while preserving their structure and row override priority.");
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(repeated, 2).Count == 1,
            "Repeated export does not duplicate vertical pronunciation nodes.");
        foreach (var version in new[] { PdfOutputVersion.Pdf14, PdfOutputVersion.Pdf20 })
        {
            var path = Path.Combine(output, "pronunciation-" + version + ".pdf");
            await exporter.ExportAsync(source, path, project with { OutputPdfVersion = version });
            check(PdfExportService.ReadReadingAlternativesForDiagnostics(path, 1).Count == 2,
                version + " retains standard alternative text without mislabeling kana as IPA phonemes.");
        }
        var redacted = Path.Combine(output, "pronunciation-redacted.pdf");
        await exporter.ExportAsync(destination, redacted, project with { SourcePdf = await packages.CreateSourceReferenceAsync(destination),
            Redactions = [new PdfRedaction { PageId = pages[0].Id, SourceRegionId = first.Id, Bounds = first.EditedGeometry.LocalBounds }] });
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(redacted, 1).SequenceEqual(new[] { "きょう" }),
            "Redaction removes the old pronunciation structure and never re-embeds the redacted row's reading.");
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(redacted, 2).SequenceEqual(new[] { "にほんご" }),
            "Redaction retains pronunciation metadata on unaffected pages through a rebuilt safe parent map.");
        var redactedObjects = await ReadingPdfObjects(redacted);
        check(!redactedObjects.Select(pair => pair.Value?["value"] as JsonObject)
            .Any(value => value?["/Alt"] is JsonValue alt && alt.TryGetValue<string>(out var reading) && reading.Contains("こんにち", StringComparison.Ordinal)),
            "The entire redacted PDF object graph contains no obsolete alternative reading, including orphaned old tags.");
        var unsafeOutput = Path.Combine(output, "unmatched-must-not-exist.pdf");
        var invalidFirst = first with { OriginalText = "存在しない語", WordReadings = [new() { SurfaceText = "存在", ReadingText = "そんざい" }] };
        var failed = false;
        try { await exporter.ExportAsync(source, unsafeOutput, project with { Pages = [pages[0] with { TextRegions = [invalidFirst] }, pages[1]] }); }
        catch (InvalidDataException) { failed = true; }
        check(failed && !File.Exists(unsafeOutput), "A reading that cannot be associated with real PDF text fails before committing an apparently successful output.");
        var progress = new List<PdfExportProgress>();
        var plain = Path.Combine(output, "plain-no-readings.pdf");
        await exporter.ExportAsync(source, plain, project with { WordReadings = [], Pages = pages.Select(page => page with { TextRegions = page.TextRegions.Select(row => row with { WordReadings = [] }).ToArray() }).ToArray() }, new CallbackProgress<PdfExportProgress>(progress.Add));
        check(progress.All(value => value.Phase != "readings"), "Documents without active readings do not scan pages to create pronunciation tags.");

        var linkFile = Path.Combine(output, "unchanged-links.pdf"); WriteDocumentUiTestPdf(linkFile, 20);
        var linkPages = Enumerable.Range(1, 20).Select(number => new OcrPage { PageNumber = number, WidthPoints = 300, HeightPoints = 400 }).ToArray();
        var sequence = linkPages.Select(page => new ProjectPageReference { PageId = page.Id, SourcePageNumber = page.PageNumber }).ToArray();
        var links = linkPages.Select(page => new PdfInternalLink { SourcePageId = page.Id, DestinationPageId = linkPages[0].Id,
            SourceBounds = new(new(10, 20), new(50, 20)), Description = "existing" }).ToArray();
        await new PdfBookmarkService().ApplyToPdfAsync(linkFile, [], null, null, PdfOutputVersion.Automatic, null, false, links, linkPages);
        var imported = await PdfSourceLinkService.ReadAsync(linkFile, sequence, default);
        check(imported.Links.Count == 20 && imported.Links.All(link => !link.IsModified && link.SourceAnnotationReference is not null),
            "The performance fixture contains real imported, unchanged annotations on twenty physical pages.");
        var calls = new List<string[]>(); var service = new PdfBookmarkService { QpdfInvocationObserver = args => calls.Add(args.ToArray()) };
        var linkTimer = Stopwatch.StartNew();
        await service.ApplyToPdfAsync(linkFile, [], null, null, PdfOutputVersion.Automatic, null, false, imported.Links, linkPages);
        var linkElapsed = linkTimer.Elapsed.TotalMilliseconds;
        check(calls.Count <= 4, "Twenty unchanged link pages require only the document transaction's four qpdf calls, with no per-page annotation checks.");
        var retained = await PdfSourceLinkService.ReadAsync(linkFile, sequence, default);
        check(retained.Links.Count == 20 && retained.Links.All(link => link.DestinationPageId == linkPages[0].Id),
            "Skipping unchanged annotation inspections preserves every original link and destination.");
        File.WriteAllText(Path.Combine(output, "pdf-reading-performance.json"), JsonSerializer.Serialize(new { readingExportMilliseconds = elapsed,
            unchangedLinkPages = 20, unchangedLinkQpdfInvocations = calls.Count, unchangedLinkMilliseconds = linkElapsed }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<JsonObject> ReadingPdfObjects(string path)
    {
        var qpdf = PdfBookmarkService.ResolveQpdfPath() ?? throw new FileNotFoundException("qpdf missing.");
        var result = await ExternalProcessRunner.RunAsync(qpdf, ["--json-output=2", "--json-stream-data=none", path, "-"], TimeSpan.FromSeconds(30), default, 64 * 1024 * 1024);
        if (result.ExitCode != 0) throw new InvalidDataException(result.StandardError);
        return JsonNode.Parse(result.StandardOutput)!["qpdf"]![1]!.AsObject();
    }

    private static void WritePdfReadingFormFixture(string path)
    {
        const string form = "BT /F1 10 Tf 1 0 0 1 15 45 Tm (Text inside a Form XObject) Tj ET\n";
        const string invocation = "/Fm Do\n";
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << /XObject << /Fm 5 0 R >> >> /Contents 6 0 R >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 300 100] /Resources << /Font << /F1 7 0 R >> >> /Length {form.Length} >>\nstream\n{form}endstream",
            $"<< /Length {invocation.Length} >>\nstream\n{invocation}endstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++) { offsets.Add(pdf.Length); pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n"); }
        var xref = pdf.Length; pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
