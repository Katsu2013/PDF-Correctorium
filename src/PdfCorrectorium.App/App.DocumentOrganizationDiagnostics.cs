using System.IO;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunDocumentOrganizationTestAsync(string[] arguments, int optionIndex)
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        if (arguments.Length <= optionIndex + 1)
            throw new ArgumentException("--document-organization-test requires a new output directory.");
        var outputDirectory = Path.GetFullPath(arguments[optionIndex + 1]);
        if (Directory.Exists(outputDirectory))
            throw new IOException("The document-organization diagnostic output directory already exists.");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var pageManagement = new PdfPageManagementService();
            var organization = new PdfDocumentOrganizationService(pageManagement);
            var preview = new PdfPreviewService();
            var blank = Path.Combine(outputDirectory, "blank-5.pdf");
            await BlankPdfService.CreateAsync(blank, 5, 595.276, 841.89);
            var blankPreview = await preview.RenderPageAsync(blank, 1, 96);
            Check(blankPreview.PageCount == 5, "Blank PDF contains five pages.");

            var extracted = Path.Combine(outputDirectory, "extracted.pdf");
            await organization.ExtractAsync(blank, [1, 3, 5], extracted);
            var extractedPreview = await preview.RenderPageAsync(extracted, 1, 96);
            Check(extractedPreview.PageCount == 3, "Extraction keeps the requested three pages.");

            var splitDirectory = Path.Combine(outputDirectory, "split");
            var groups = PdfDocumentOrganizationService.BuildFixedSizeGroups(5, 2);
            var split = await organization.SplitAsync(blank, groups, splitDirectory, "blank");
            Check(split.Count == 3, "Fixed-size splitting produces three files.");
            var splitCounts = new List<int>();
            foreach (var path in split)
                splitCounts.Add((await preview.RenderPageAsync(path, 1, 96)).PageCount);
            Check(splitCounts.SequenceEqual([2, 2, 1]), "Split files contain 2, 2 and 1 pages.");
            var boundaries = PdfDocumentOrganizationService.BuildBoundaryGroups(5, [3, 5]);
            Check(boundaries.Select(group => group.Count).SequenceEqual([2, 2, 1]), "Boundary splitting implicitly starts at page one.");

            var pageId = Guid.NewGuid();
            var project = new PdfCorrectoriumProject
            {
                Name = "organization-diagnostic",
                SourcePdf = await new ProjectPackageService().CreateSourceReferenceAsync(blank, outputDirectory),
                Pages = [new OcrPage { Id = pageId, PageNumber = 1, WidthPoints = 595.276, HeightPoints = 841.89 }],
                InternalLinks =
                [
                    new PdfInternalLink
                    {
                        SourcePageId = pageId,
                        SourceBounds = new PdfRectangle(new PdfPoint(36, 760), new PdfSize(180, 36)),
                        DestinationPageId = Guid.Empty,
                        ExternalUri = "https://example.com/pdf-correctorium",
                        Description = "external diagnostic link",
                    },
                ],
            };
            var linked = Path.Combine(outputDirectory, "external-link.pdf");
            await new PdfExportService().ExportAsync(blank, linked, project);
            var qpdfPath = PdfBookmarkService.ResolveQpdfPath() ?? throw new FileNotFoundException("qpdf was not found.");
            var json = await ExternalProcessRunner.RunAsync(qpdfPath, ["--json-output=2", linked, "-"], TimeSpan.FromMinutes(1));
            Check(json.ExitCode == 0 && json.StandardOutput.Contains("/URI", StringComparison.Ordinal) &&
                  json.StandardOutput.Contains("example.com/pdf-correctorium", StringComparison.Ordinal),
                "Export writes a standard external URI link annotation.");

            var workspace = Path.Combine(outputDirectory, "workspace");
            var recoveryDirectory = Path.Combine(workspace, "recovery");
            Directory.CreateDirectory(recoveryDirectory);
            var packages = new ProjectPackageService();
            var recoveryPath = Path.Combine(recoveryDirectory, $"{project.ProjectId:N}.autosave.pdfocrproj");
            await packages.SaveAutoSaveAsync(recoveryPath, project, embedSourcePdf: true, thumbnailCache: new Dictionary<int, byte[]>());
            var recovered = await RecoveryProjectDiscoveryService.FindValidAsync(workspace, packages);
            Check(recovered.Count == 1 && recovered[0].Path == recoveryPath,
                "Startup recovery discovery finds a valid unsaved project.");

            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "result.txt"),
                "PASS document organization, blank pages, external links, and recovery discovery");
            Shutdown(0);
        }
        catch
        {
            Shutdown(1);
            throw;
        }
    }
}
