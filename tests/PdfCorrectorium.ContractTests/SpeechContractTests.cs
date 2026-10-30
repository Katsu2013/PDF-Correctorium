using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

internal static class SpeechContractTests
{
    private static OcrTextRegion Region(string text, double x = 0, double y = 0) => new()
    {
        OriginalText = text,
        OriginalGeometry = new() { LocalBounds = new(new(x, y), new(100, 20)), RotationCenter = new(x + 50, y + 10) },
        EditedGeometry = new() { LocalBounds = new(new(x, y), new(100, 20)), RotationCenter = new(x + 50, y + 10) },
    };
    private static OcrPage Page(params OcrTextRegion[] regions) => new()
    { PageNumber = 1, WidthPoints = 600, HeightPoints = 800, TextRegions = regions, ReadingOrder = regions.Select(region => region.Id).ToArray() };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static Task EligibilityAsync()
    {
        var first = Region("original") with { EditedText = "corrected", HasEditedText = true };
        var second = Region("second");
        var excluded = Region("private") with { Output = new() { IncludeInSpeech = false } };
        var empty = Region("must not return") with { HasEditedText = true, EditedText = "" };
        var deleted = Region("deleted") with { IsDeleted = true };
        var page = Page(first, second, excluded, empty, deleted) with { ReadingOrder = [second.Id, first.Id] };
        Check(SpeechText.Build(page, []).Select(segment => segment.Text).SequenceEqual(["second", "corrected"]), "Speech must use effective text, explicit order and output flags without original-text fallback.");
        return Task.CompletedTask;
    }
    internal static Task RedactionsAsync()
    {
        var rotated = Region("secret", 100, 100);
        rotated = rotated with { EditedGeometry = rotated.EditedGeometry with { RotationDegrees = 90 } };
        var safe = Region("safe", 400, 400);
        var mark = new PdfRedaction { Bounds = new(new(145, 65), new(5, 5)) };
        Check(SpeechText.Build(Page(rotated, safe), [mark]).Select(segment => segment.Text).SequenceEqual(["safe"]), "Rotated text intersecting planned redaction must never reach the voice.");
        Check(SpeechText.Build(Page(safe), [mark with { Bounds = new(new(0, 0), new(double.NaN, 1)) }]).Count == 0, "Unknown redaction geometry must fail closed.");
        var moved = Region("redacted source", 0, 0) with { EditedGeometry = safe.EditedGeometry };
        var originalMark = new PdfRedaction { Bounds = new(new(1, 1), new(5, 5)) };
        Check(SpeechText.Build(Page(moved), [originalMark]).Count == 0, "Moving OCR away from the original redaction cannot make protected text audible.");
        Check(SpeechText.Build(Page(safe), [originalMark with { SourceRegionId = safe.Id }]).Count == 0, "An explicit source-region redaction excludes speech even when its geometry was moved.");
        return Task.CompletedTask;
    }
    internal static Task UnicodeAndReadingsAsync()
    {
        var region = Region("Aか\u3099😀日本語Z") with { WordReadings = [new() { SurfaceText = "日本語", ReadingText = "にほんご" }] };
        var selected = SpeechText.Build(Page(region), [], new Dictionary<Guid, IReadOnlyList<int>> { [region.Id] = [1, 2, 3, 4, 5] });
        Check(selected.Single().Text == "か\u3099😀にほんご", "Selection indices are graphemes, and exact readings apply to the selected text.");
        var readings = new WordReading[] { new() { SurfaceText = "AB", ReadingText = "C" }, new() { SurfaceText = "C", ReadingText = "wrong" } };
        Check(SpeechText.ApplyReadings("AB", readings) == "C", "Readings must not cascade.");
        Check(SpeechText.ApplyReadings("X", [new() { SurfaceText = "X", ReadingText = "one" }, new() { SurfaceText = "X", ReadingText = "two" }]) == "X", "Ambiguous readings must not invent a pronunciation.");
        Check(SpeechText.Build(Page(Region("<speak>plain & text</speak>")), []).Single().Text == "<speak>plain & text</speak>", "Document text must remain literal text, never SSML.");
        return Task.CompletedTask;
    }

    private static OcrTextRegion Line(string text, double y, double x = 20, double width = 400, double height = 18) => new()
    {
        OriginalText = text,
        OriginalGeometry = new() { LocalBounds = new(new(x, y), new(width, height)), RotationCenter = new(x + width / 2, y + height / 2) },
        EditedGeometry = new() { LocalBounds = new(new(x, y), new(width, height)), RotationCenter = new(x + width / 2, y + height / 2) },
    };

    internal static Task ParagraphsAsync()
    {
        var first = Line("JavaScriptという言語のフレーム", 700, x: 38);
        var second = Line("ワークについて説明します。", 676);
        var paragraph = SpeechParagraphs.Build(SpeechText.Build(Page(first, second), [])).Single();
        Check(paragraph.Text == "JavaScriptという言語のフレームワークについて説明します。", "A wrapped Japanese paragraph must be synthesized together without invented spaces.");
        Check(paragraph.Spans.Count == 2 && paragraph.SegmentAt(0).RegionId == first.Id &&
            paragraph.SegmentAt(first.OriginalText.Length).RegionId == second.Id && paragraph.SegmentAt(int.MaxValue).RegionId == second.Id,
            "Utterance character positions must map back to individual OCR markers, including the last span.");
        var english = SpeechParagraphs.Build(SpeechText.Build(Page(Line("A technical", 700), Line("description.", 676)), [])).Single();
        Check(english.Text == "A technical description.", "Latin words across a visual wrap need one separating space.");
        var compound = SpeechParagraphs.Build(SpeechText.Build(Page(Line("frame-", 700), Line("work", 676)), [])).Single();
        Check(compound.Text == "frame-work", "An existing hyphen must be retained without an extra space or speculative spelling edits.");
        var verticalFirst = Line("縦書きの", 200, x: 400, width: 18, height: 400) with { WritingMode = WritingMode.Vertical };
        var verticalSecond = Line("続きです。", 200, x: 376, width: 18, height: 400) with { WritingMode = WritingMode.Vertical };
        Check(SpeechParagraphs.Build(SpeechText.Build(Page(verticalFirst, verticalSecond), [])).Single().Text == "縦書きの続きです。",
            "Adjacent top-aligned vertical columns in reading order can form one paragraph.");
        return Task.CompletedTask;
    }

    internal static Task ParagraphBoundariesAsync()
    {
        var first = Line("本文の続き", 700);
        var next = Line("次の行", 676);
        void Separate(OcrTextRegion a, OcrTextRegion b, string reason) => Check(
            SpeechParagraphs.Build(SpeechText.Build(Page(a, b), [])).Count == 2, reason);
        Separate(first, Line("字下げした新段落", 676, x: 38), "A new first-line indent must preserve a paragraph break.");
        Separate(first, Line("空行の後", 650), "An inter-paragraph blank must preserve the pause.");
        Separate(Line("見出し", 700, height: 28), next, "A title in a different size must stay separate from body text.");
        Separate(first, Line("別の段", 676, x: 250), "Different columns must not be joined across reading-order transitions.");
        Separate(first, Line("同じ高さの表セル", 700, x: 450), "Separate cells on one baseline must not be joined as wrapped lines.");
        Separate(first, next with { WritingMode = WritingMode.Vertical }, "Mixed writing directions must remain separate.");
        Separate(first, next with { EditedGeometry = next.EditedGeometry with { RotationDegrees = 90 } }, "Rotated text must not be inferred as an unrotated paragraph.");
        Separate(Line("短い末尾", 700, width: 80), next, "A short completed line followed by a longer line must start a new paragraph.");
        Separate(first, Line("・箇条書き", 676), "List entries must retain their boundaries.");
        Separate(first, Line("1. numbered item", 676), "Numbered list entries must retain their boundaries.");
        Separate(first, Line("<div>code</div>", 676), "Code-shaped lines must not be joined to body text.");
        Separate(first with { ParentRegionId = Guid.NewGuid() }, next with { ParentRegionId = Guid.NewGuid() }, "Different explicit paragraph parents must not be joined.");
        var partial = new Dictionary<Guid, IReadOnlyList<int>> { [first.Id] = [0, 1], [next.Id] = [0, 1] };
        Check(SpeechParagraphs.Build(SpeechText.Build(Page(first, next), [], partial)).Count == 2, "Partial character selections must not invent a continuous paragraph.");
        var skipped = Line("秘匿行", 688) with { Output = new() { IncludeInSpeech = false } };
        Check(SpeechParagraphs.Build(SpeechText.Build(Page(first, skipped, next), [])).Count == 2, "Speech exclusions are boundaries even when remaining geometry could join.");
        var redaction = new PdfRedaction { SourceRegionId = skipped.Id, Bounds = new(new(500, 500), new(1, 1)) };
        skipped = skipped with { Output = new() };
        var safe = SpeechParagraphs.Build(SpeechText.Build(Page(first, skipped, next), [redaction]));
        Check(safe.Count == 2 && safe.All(item => !item.Text.Contains("秘匿", StringComparison.Ordinal)), "Redacted lines must not be spoken or bridged when joining surrounding text.");
        return Task.CompletedTask;
    }

    internal static Task ParagraphScaleAsync()
    {
        var lines = Enumerable.Range(0, 10000).Select(index => new SpeechTextSegment(Guid.NewGuid(), "日本語😀", new(new(20, 300000 - index * 24), new(400, 18)))).ToArray();
        var paragraph = SpeechParagraphs.Build(lines).Single();
        Check(paragraph.Spans.Count == lines.Length && paragraph.Text == string.Concat(lines.Select(line => line.Text)), "Long paragraph assembly must retain every line and Unicode text in one pass.");
        foreach (var index in new[] { 0, 1, 4999, 9999 })
            Check(paragraph.SegmentAt(paragraph.Spans[index].Start).RegionId == lines[index].RegionId, "Large span tables must preserve progress lookup.");
        Check(SpeechParagraphs.Build([]).Count == 0, "Empty speech input must remain empty.");
        return Task.CompletedTask;
    }
}
