using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private static async Task VerifyPdfReadingHyphensAsync(string output, Action<bool, string> check)
    {
        check(PdfExportService.ReadingTextMatches("package.json/package\u0002", "package.json/package-"),
            "A PDFium line-ending hyphen matches the real ASCII hyphen without changing the PDF glyph.");
        check(PdfExportService.ReadingTextMatches("soft\u0002", "soft\u00ad"),
            "PDFium's marker also accepts its documented soft-hyphen source character.");
        check(!PdfExportService.ReadingTextMatches("package\u0002", "package") &&
              !PdfExportService.ReadingTextMatches("package\u0002", "packagex"),
            "A hyphen marker cannot hide a missing hyphen or substitute for an unrelated letter.");
        check(!PdfExportService.ReadingTextMatches("package\u0003", "package-") &&
              !PdfExportService.ReadingTextMatches("package", "Package"),
            "Other control characters and genuine text differences still reject a reading association.");

        var source = Path.Combine(output, "wrapped-hyphen-source.pdf");
        WriteWrappedHyphenFixture(source);
        var row = new OcrTextRegion { OriginalText = "package.json/package-",
            OriginalGeometry = new() { LocalBounds = new(new(40, 220), new(140, 12)), RotationCenter = new(110, 226) },
            EditedGeometry = new() { LocalBounds = new(new(40, 220), new(140, 12)), RotationCenter = new(110, 226) } };
        var continuation = new OcrTextRegion { OriginalText = "lock.json",
            OriginalGeometry = new() { LocalBounds = new(new(40, 204), new(60, 12)), RotationCenter = new(70, 210) },
            EditedGeometry = new() { LocalBounds = new(new(40, 204), new(60, 12)), RotationCenter = new(70, 210) } };
        var page = new OcrPage { PageNumber = 1, WidthPoints = 300, HeightPoints = 400,
            TextRegions = [row, continuation], ReadingOrder = [row.Id, continuation.Id] };
        var packages = new ProjectPackageService();
        var project = new PdfCorrectoriumProject { SourcePdf = await packages.CreateSourceReferenceAsync(source), Pages = [page],
            WordReadings = [new() { SurfaceText = "package", ReadingText = "パッケージ" }, new() { SurfaceText = "json", ReadingText = "ジェイソン" }] };
        var destination = Path.Combine(output, "wrapped-hyphen-export.pdf");
        var result = await new PdfExportService().ExportAsync(source, destination, project);
        check(result.SavedReadingRows == 2, "Both lines of a wrapped filename export their reading metadata successfully.");
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(destination, 1).Contains("パッケージ.ジェイソン/パッケージ-"),
            "The exported reading retains the real end-of-line hyphen.");
        var preview = new PdfPreviewService();
        static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource image)
        { var stride = image.PixelWidth * 4; var pixels = new byte[stride * image.PixelHeight]; image.CopyPixels(pixels, stride, 0); return pixels; }
        var sourcePixels = Pixels((await preview.RenderPageAsync(source, 1, 600)).Image);
        var destinationPixels = Pixels((await preview.RenderPageAsync(destination, 1, 600)).Image);
        check(sourcePixels.SequenceEqual(destinationPixels),
            "Wrapped filename glyphs and page appearance remain pixel-identical after the reading fix.");
        await using (var worker = new PdfNativeWorkerClient(reuseInputs: true))
        {
            var before = await worker.ReadSpeechPageAsync(source, 1, default);
            var after = await worker.ReadSpeechPageAsync(destination, 1, default);
            check(string.Concat(before.Regions.Select(value => value.Text)) == string.Concat(after.Regions.Select(value => value.Text)),
                "Adding a wrapped filename reading does not change extracted source text.");
        }
        // Keep a second native save in this regression: PDFium may change its
        // extraction marker on reload even though the glyph has not changed.
        var repeated = Path.Combine(output, "wrapped-hyphen-reexport.pdf");
        await new PdfExportService().ExportAsync(destination, repeated, project with { SourcePdf = await packages.CreateSourceReferenceAsync(destination) });
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(repeated, 1).Count == 2,
            "A wrapped filename can be re-exported without losing or duplicating its reading tags.");
    }

    private static async Task VerifyUserProjectReadingExportAsync(string output, string projectPath, Action<bool, string> check)
    {
        var packages = new ProjectPackageService();
        var project = await packages.OpenAsync(projectPath);
        var source = await packages.MaterializeEmbeddedSourceAsync(projectPath, project.SourcePdf, Path.Combine(output, "source-cache"));
        var timer = Stopwatch.StartNew();
        var destination = Path.Combine(output, "actual-project-export.pdf");
        var result = await new PdfExportService().ExportAsync(source, destination, project,
            new CallbackProgress<PdfExportProgress>(value => File.WriteAllText(Path.Combine(output, "actual-project-progress.json"), JsonSerializer.Serialize(value))));
        check(result.SavedReadingRows > 0, "The complete supplied project exports with its actual readings and edits.");
        check(PdfExportService.ReadReadingAlternativesForDiagnostics(destination, 269)
                .Any(value => value.StartsWith("パッケージ.jソン/パッケージ-", StringComparison.Ordinal)),
            "The supplied project's page 269 retains the previously failing filename's configured readings.");
        File.WriteAllText(Path.Combine(output, "actual-project-result.json"), JsonSerializer.Serialize(new
        { projectPages = project.Pages.Count, result.SavedReadingRows, milliseconds = timer.Elapsed.TotalMilliseconds, destination }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteWrappedHyphenFixture(string path)
    {
        const string text = "BT /F1 12 Tf 40 220 Td (package.json/package-) Tj 0 -16 Td (lock.json) Tj ET\n";
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {text.Length} >>\nstream\n{text}endstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        { offsets.Add(pdf.Length); pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n"); }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), System.Text.Encoding.ASCII);
    }
}
