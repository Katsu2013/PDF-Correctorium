using System.IO.Compression;
using System.Text.Json;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

internal static class ParagraphContractTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static SpeechTextSegment Line(string text, double x, double y, Guid? group = null) =>
        new(Guid.NewGuid(), text, new(new(x, y), new(100, 20))) { ParagraphGroupId = group };

    internal static Task OverridesAsync()
    {
        var group = Guid.NewGuid();
        var first = Line("短い先頭", 20, 700, group);
        var second = Line("字下げした続き", 50, 600, group);
        Check(SpeechParagraphs.Partition([first, second]).Single().Count == 2, "Explicit paragraphs override geometric gaps and indentation without merging OCR rows.");
        Check(SpeechParagraphs.Build([first, second]).Single().Text == first.Text + second.Text, "Speech uses the same explicit partition as paragraph editing.");
        Check(SpeechParagraphs.Partition([first, second with { BreakBefore = true }]).Count == 2, "An excluded speech row remains a boundary even inside a manual paragraph.");
        Check(SpeechParagraphs.Partition([first, second with { IsPartialSelection = true }]).Count == 2, "Manual grouping cannot bridge unrelated selected character fragments.");
        Check(SpeechParagraphs.Partition([first, second with { WritingMode = WritingMode.Vertical }]).Single().Count == 2, "Explicit grouping joins mixed writing directions without changing their geometry.");
        Check(SpeechParagraphs.Partition([first with { ParagraphGroupId = null }, second with { ParagraphGroupId = null, WritingMode = WritingMode.Vertical }]).Count == 2,
            "Automatic inference still separates mixed writing directions.");
        var chapter = first with { Text = "Chapter" };
        var digit = second with { Text = "1", WritingMode = WritingMode.Vertical, FlowDirection = TextFlowDirection.RightToLeft };
        Check(SpeechParagraphs.Build([chapter, digit]).Single().Text == "Chapter 1", "An explicit Chapter/number paragraph reads as one label while retaining each row's direction.");
        Check(SpeechParagraphs.Partition([chapter, digit with { BreakBefore = true }]).Count == 2 &&
            SpeechParagraphs.Partition([chapter, digit with { IsPartialSelection = true }]).Count == 2,
            "Mixed manual paragraphs still respect excluded content and partial-selection boundaries.");
        Check(SpeechParagraphs.Partition([first, second with { ParagraphGroupId = Guid.NewGuid() }]).Count == 2, "Separate manual groups are boundaries.");
        first = Line("先頭", 20, 700, Guid.Empty); second = Line("続き", 20, 676, Guid.Empty);
        Check(SpeechParagraphs.Partition([first, second]).Count == 2, "The explicit individual-row setting must suppress automatic joining.");
        Check(SpeechParagraphs.Partition([first with { ParagraphGroupId = null }, second with { ParagraphGroupId = null }]).Count == 1, "Resetting the override restores automatic layout inference.");
        var spokenReading = first with { ParagraphGroupId = null, Text = "・読み仮名", LayoutText = "本文" };
        Check(SpeechParagraphs.Partition([spokenReading, second with { ParagraphGroupId = null }]).Count == 1, "Pronunciation aliases must not change the shared layout classification.");
        return Task.CompletedTask;
    }

    internal static Task NavigationAsync()
    {
        var a = Line("一行目", 20, 700); var b = Line("二行目", 20, 676); var c = Line("別段落", 20, 600);
        var paragraphs = SpeechParagraphs.Build([a, b, c]);
        Check(paragraphs.Count == 2, "The navigation fixture has two paragraphs and three rows.");
        Check(SpeechNavigation.Move(paragraphs, new(0, 0), 1, SpeechNavigationUnit.Line) == new SpeechMove(new(0, 1), 0), "Next line stays within the joined paragraph.");
        Check(SpeechNavigation.Move(paragraphs, new(0, 1), 1, SpeechNavigationUnit.Line) == new SpeechMove(new(1, 0), 0), "Next line crosses to the next paragraph when needed.");
        Check(SpeechNavigation.Move(paragraphs, new(1, 0), -1, SpeechNavigationUnit.Line) == new SpeechMove(new(0, 1), 0), "Previous line returns to the final row of the preceding paragraph.");
        Check(SpeechNavigation.Move(paragraphs, new(0, 1), 1, SpeechNavigationUnit.Paragraph) == new SpeechMove(new(1, 0), 0), "Paragraph navigation skips the remainder and starts the next paragraph.");
        Check(SpeechNavigation.Move(paragraphs, new(1, 0), -1, SpeechNavigationUnit.Paragraph) == new SpeechMove(new(0, 0), 0), "Previous paragraph always starts at its first row.");
        Check(SpeechNavigation.Move(paragraphs, new(0, 0), -1, SpeechNavigationUnit.Line).PageDelta == -1, "A beginning boundary requests the previous in-scope page.");
        Check(SpeechNavigation.Move(paragraphs, new(1, 0), 1, SpeechNavigationUnit.Paragraph).PageDelta == 1, "An ending boundary requests the next in-scope page.");
        Check(SpeechNavigation.Move([], default, 1, SpeechNavigationUnit.Line).PageDelta == 1, "An empty page requests bounded forward traversal.");
        return Task.CompletedTask;
    }

    internal static async Task PersistenceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfCorrectorium-paragraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.pdf"); await File.WriteAllTextAsync(source, "%PDF-1.4\n%%EOF");
            var packages = new ProjectPackageService(); var pageId = Guid.NewGuid(); var group = Guid.NewGuid();
            var geometry = new TextGeometry { LocalBounds = new(new(20, 200), new(100, 20)), RotationCenter = new(70, 210), IsGeometryLocked = true };
            var region = new OcrTextRegion { PageId = pageId, OriginalText = "資料", OriginalGeometry = geometry, EditedGeometry = geometry, ParagraphGroupId = group };
            var split = region with { Id = Guid.NewGuid(), ParagraphGroupId = Guid.Empty };
            var auto = region with { Id = Guid.NewGuid(), ParagraphGroupId = null };
            var project = new PdfCorrectoriumProject { SourcePdf = await packages.CreateSourceReferenceAsync(source),
                Pages = [new() { Id = pageId, PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [region, split, auto], ReadingOrder = [region.Id, split.Id, auto.Id] }] };
            var path = Path.Combine(root, "paragraphs.pdfocrproj"); await packages.SaveAsync(path, project);
            var reopened = await packages.OpenAsync(path); var rows = reopened.Pages.Single().TextRegions;
            Check(rows[0].ParagraphGroupId == group && rows[1].ParagraphGroupId == Guid.Empty && rows[2].ParagraphGroupId is null, "Explicit, separate and automatic paragraph states survive a real project package round-trip.");
            Check(rows.All(row => row.OriginalGeometry.IsEquivalentTo(geometry) && row.EditedGeometry.IsEquivalentTo(geometry)), "Paragraph metadata must not change geometry or locks during persistence.");
            using var archive = ZipFile.OpenRead(path); using var stream = archive.GetEntry("manifest.json")!.Open(); using var manifest = JsonDocument.Parse(stream);
            Check(manifest.RootElement.GetProperty("formatVersion").GetString() == "1.14" &&
                manifest.RootElement.GetProperty("minimumApplicationVersion").GetString() == "1.0.0-dev.262", "The package prevents old readers from silently losing explicit paragraph metadata.");
            Check(ProjectManifest.IsSupportedVersion("1.12"), "Existing 1.12 projects remain readable.");
        }
        finally { Directory.Delete(root, true); }
    }
}
