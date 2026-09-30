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
        static IEnumerable<PdfBookmark> FlattenBookmarks(PdfBookmark bookmark)
        {
            yield return bookmark;
            foreach (var child in bookmark.Children.SelectMany(FlattenBookmarks)) yield return child;
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
            var bookmarkService = new PdfBookmarkService();
            await bookmarkService.ApplyToPdfAsync(blank,
            [
                new PdfBookmark { Title = "First", PageNumber = 1, Children =
                [
                    new PdfBookmark { Title = "Removed child", PageNumber = 2 },
                    new PdfBookmark { Title = "Retained child", PageNumber = 3 },
                ] },
                new PdfBookmark { Title = "Removed parent", PageNumber = 4, Children =
                [new PdfBookmark { Title = "Promoted child", PageNumber = 5 }] },
            ]);
            var blankPreview = await preview.RenderPageAsync(blank, 1, 96);
            Check(blankPreview.PageCount == 5, "Blank PDF contains five pages.");

            var extracted = Path.Combine(outputDirectory, "extracted.pdf");
            await organization.ExtractAsync(blank, [1, 3, 5], extracted);
            var extractedPreview = await preview.RenderPageAsync(extracted, 1, 96);
            Check(extractedPreview.PageCount == 3, "Extraction keeps the requested three pages.");
            var extractedBookmarks = await bookmarkService.ReadFromPdfAsync(extracted);
            Check(extractedBookmarks.Count == 2 && extractedBookmarks[0].Title == "First" &&
                  extractedBookmarks[0].PageNumber == 1 &&
                  extractedBookmarks[0].Children.Count == 1 &&
                  extractedBookmarks[0].Children[0].Title == "Retained child" &&
                  extractedBookmarks[0].Children[0].PageNumber == 2 &&
                  extractedBookmarks[1].Title == "Promoted child" &&
                  extractedBookmarks[1].PageNumber == 3,
                "Extraction removes stale bookmarks, remaps retained pages and promotes children of removed parents.");

            var splitDirectory = Path.Combine(outputDirectory, "split");
            var groups = PdfDocumentOrganizationService.BuildFixedSizeGroups(5, 2);
            var split = await organization.SplitAsync(blank, groups, splitDirectory, "blank");
            Check(split.Count == 3, "Fixed-size splitting produces three files.");
            var splitCounts = new List<int>();
            foreach (var path in split)
            {
                var count = (await preview.RenderPageAsync(path, 1, 96)).PageCount;
                splitCounts.Add(count);
                var bookmarks = await bookmarkService.ReadFromPdfAsync(path);
                Check(bookmarks.SelectMany(FlattenBookmarks).All(bookmark => bookmark.PageNumber >= 1 && bookmark.PageNumber <= count),
                    "Split output contains no bookmark pointing outside its page range.");
            }
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

            var importedPage = new ProjectPageReference { PageId = pageId, SourcePageNumber = 1 };
            var importedLinks = await PdfSourceLinkService.ReadAsync(linked, [importedPage], CancellationToken.None);
            Check(importedLinks.Links.Count == 1 && importedLinks.UnsupportedActions == 0 &&
                  importedLinks.Links[0].ExternalUri == "https://example.com/pdf-correctorium" &&
                  importedLinks.Links[0].SourceAnnotationReference is not null,
                "Existing safe URI links are imported with their source annotation identity.");
            var inputIssues = await InputPdfInspectionService.InspectAsync(linked, CancellationToken.None);
            Check(inputIssues.All(issue => issue.Code != "inspection.failed"),
                "Structured input-PDF inspection completes for a valid linked PDF.");
            var editedLink = importedLinks.Links[0] with
            {
                ExternalUri = "https://example.com/edited",
                IsModified = true,
            };
            var editedOutput = Path.Combine(outputDirectory, "edited-link.pdf");
            await new PdfExportService().ExportAsync(linked, editedOutput, project with { InternalLinks = [editedLink] });
            var editedPdf = await PdfSourceLinkService.ReadAsync(editedOutput, [importedPage], CancellationToken.None);
            Check(editedPdf.Links.Count == 1 && editedPdf.Links[0].ExternalUri == "https://example.com/edited",
                "Editing an imported link replaces its annotation without duplication.");
            var deletedOutput = Path.Combine(outputDirectory, "deleted-link.pdf");
            await new PdfExportService().ExportAsync(linked, deletedOutput, project with
            {
                InternalLinks = [importedLinks.Links[0] with { IsEnabled = false, IsModified = true }],
            });
            var deletedPdf = await PdfSourceLinkService.ReadAsync(deletedOutput, [importedPage], CancellationToken.None);
            Check(deletedPdf.Links.Count == 0, "Deleting an imported link removes the original annotation.");

            var secondPageId = Guid.NewGuid();
            var internalOutput = Path.Combine(outputDirectory, "internal-link.pdf");
            var internalProject = project with
            {
                Pages =
                [
                    project.Pages[0],
                    new OcrPage { Id = secondPageId, PageNumber = 2, WidthPoints = 595.276, HeightPoints = 841.89 },
                ],
                InternalLinks =
                [
                    project.InternalLinks[0] with { ExternalUri = null, DestinationPageId = secondPageId },
                ],
            };
            await new PdfExportService().ExportAsync(blank, internalOutput, internalProject);
            var internalImport = await PdfSourceLinkService.ReadAsync(internalOutput,
                [importedPage, new ProjectPageReference { PageId = secondPageId, SourcePageNumber = 2 }],
                CancellationToken.None);
            Check(internalImport.Links.Count == 1 && internalImport.Links[0].DestinationPageId == secondPageId,
                "Existing safe internal page links retain their destination page identity.");

            var decoratedOutput = Path.Combine(outputDirectory, "japanese-decoration.pdf");
            await new PdfExportService().ExportAsync(blank, decoratedOutput, project with
            {
                InternalLinks = [],
                TextDecorations =
                [
                    new PdfTextDecoration
                    {
                        Kind = PdfTextDecorationKind.Header,
                        TextTemplate = "日本語ヘッダー {page}/{total}",
                        Position = PdfTextDecorationPosition.TopCenter,
                    },
                ],
            });
            var decoratedPreview = await preview.RenderPageAsync(decoratedOutput, 1, 96);
            var decoratedText = string.Concat(decoratedPreview.SelectableCharacters.Select(character => character.Text));
            Check(decoratedText.Normalize(System.Text.NormalizationForm.FormKC)
                    .Contains("日本語ヘッダー", StringComparison.Ordinal),
                "Japanese page decorations are embedded as searchable PDF text.");

            var numberedOutput = Path.Combine(outputDirectory, "skip-cover-odd-even.pdf");
            await new PdfExportService().ExportAsync(blank, numberedOutput, project with
            {
                InternalLinks = [],
                Pages = Enumerable.Range(1, 5).Select(number => new OcrPage
                {
                    Id = Guid.NewGuid(), PageNumber = number, WidthPoints = 595.276, HeightPoints = 841.89,
                }).ToArray(),
                TextDecorations =
                [
                    new PdfTextDecoration
                    {
                        Kind = PdfTextDecorationKind.PageNumber,
                        TextTemplate = "P{page}",
                        Position = PdfTextDecorationPosition.BottomRight,
                        EvenPagePosition = PdfTextDecorationPosition.BottomLeft,
                        FirstPage = 2,
                        PageNumberOffset = -1,
                    },
                ],
            });
            var cover = await preview.RenderPageAsync(numberedOutput, 1, 96);
            var even = await preview.RenderPageAsync(numberedOutput, 2, 96);
            var odd = await preview.RenderPageAsync(numberedOutput, 3, 96);
            Check(cover.SelectableCharacters.Count == 0,
                "Skipped cover has no added page number.");
            Check(string.Concat(even.SelectableCharacters.Select(character => character.Text)) == "P1" &&
                  string.Concat(odd.SelectableCharacters.Select(character => character.Text)) == "P2",
                "Numbering begins at one after the cover.");
            Check(even.SelectableCharacters.Average(character => character.Left) < even.Image.PixelWidth / 2d &&
                  odd.SelectableCharacters.Average(character => character.Left) > odd.Image.PixelWidth / 2d,
                "Even and odd page numbers use different PDF output positions.");

            var settingsDialog = new PageTextDecorationsWindow(5,
            [
                new PdfTextDecoration
                {
                    TextTemplate = "P{page}",
                    Position = PdfTextDecorationPosition.BottomRight,
                    EvenPagePosition = PdfTextDecorationPosition.BottomLeft,
                },
            ]);
            PageTextDecorationsWindow.ApplySkipCoverPreset(settingsDialog.Decorations.Single());
            Check(settingsDialog.PreviewTexts is ("P2", "P1"),
                $"The settings dialog previews odd and even numbering after skipping the cover: {settingsDialog.PreviewTexts}.");
            Check(settingsDialog.PreviewAlignments is (System.Windows.TextAlignment.Right, System.Windows.TextAlignment.Left),
                "The settings dialog previews distinct odd and even alignments.");
            var dialogContent = (System.Windows.FrameworkElement)settingsDialog.Content;
            dialogContent.Measure(new System.Windows.Size(850, 720));
            dialogContent.Arrange(new System.Windows.Rect(0, 0, 850, 720));
            dialogContent.UpdateLayout();
            var dialogImage = new System.Windows.Media.Imaging.RenderTargetBitmap(
                850, 720, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            dialogImage.Render(dialogContent);
            var dialogPng = new System.Windows.Media.Imaging.PngBitmapEncoder();
            dialogPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dialogImage));
            await using (var stream = File.Create(Path.Combine(outputDirectory, "numbering-settings-preview.png")))
                dialogPng.Save(stream);
            settingsDialog.Close();
            var defaultPositionDialog = new PageTextDecorationsWindow(5, [new PdfTextDecoration()]);
            Check(defaultPositionDialog.EvenPositionDisplay == "奇数ページと同じ",
                "The default even-page position is labeled as matching odd pages.");
            defaultPositionDialog.Close();

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
                "PASS document organization, source links, Japanese text decorations, cover skipping, odd-even positions, input warnings, and recovery discovery");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "failure.txt"), exception.ToString());
            Shutdown(1);
            throw;
        }
    }
}
