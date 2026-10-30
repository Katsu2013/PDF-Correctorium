using System.Text;
using System.Text.RegularExpressions;

namespace PdfCorrectorium.Core.Documents;

public sealed record SpeechTextSpan(int Start, int Length, SpeechTextSegment Segment);
public sealed record SpeechUtterance(string Text, IReadOnlyList<SpeechTextSpan> Spans)
{
    public SpeechTextSegment SegmentAt(int position) => Spans[SpanIndexAt(position)].Segment;
    public int SpanIndexAt(int position)
    {
        var low = 0; var high = Spans.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Spans[middle].Start <= position) low = middle; else high = middle - 1;
        }
        return low;
    }
}

/// <summary>Read-only paragraph inference in reading order; never modifies OCR or crosses an excluded line.</summary>
public static partial class SpeechParagraphs
{
    public static IReadOnlyList<SpeechUtterance> Build(IReadOnlyList<SpeechTextSegment> lines)
    {
        var result = new List<SpeechUtterance>();
        var text = new StringBuilder();
        var spans = new List<SpeechTextSpan>();
        foreach (var paragraph in Partition(lines))
        {
            foreach (var line in paragraph)
            {
                var content = line.Text.Trim();
                if (text.Length > 0 && NeedsSpace(text[^1], content[0])) text.Append(' ');
                spans.Add(new(text.Length, content.Length, line));
                text.Append(content);
            }
            Flush();
        }
        return result;

        void Flush()
        {
            if (text.Length == 0) return;
            result.Add(new(text.ToString(), spans.ToArray()));
            text.Clear(); spans.Clear();
        }
    }

    /// <summary>Shared layout-only partition for paragraph editing and speech. Inputs must be in reading order.</summary>
    public static IReadOnlyList<IReadOnlyList<SpeechTextSegment>> Partition(IReadOnlyList<SpeechTextSegment> lines)
    {
        var result = new List<IReadOnlyList<SpeechTextSegment>>();
        var current = new List<SpeechTextSegment>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text)) { Flush(); continue; }
            if (current.Count > 0 && !CanJoin(current[^1], line)) Flush();
            current.Add(line);
        }
        Flush();
        return result;
        void Flush() { if (current.Count == 0) return; result.Add(current.ToArray()); current.Clear(); }
    }

    private static bool CanJoin(SpeechTextSegment first, SpeechTextSegment second)
    {
        if (second.BreakBefore || first.IsPartialSelection || second.IsPartialSelection ||
            !first.Bounds.IsValid || !second.Bounds.IsValid) return false;
        if (first.ParagraphGroupId.HasValue || second.ParagraphGroupId.HasValue)
            return first.ParagraphGroupId is { } group && group != Guid.Empty && group == second.ParagraphGroupId;
        // A manual paragraph expresses semantic membership, including labels such
        // as horizontal "Chapter" plus a vertically laid-out digit. Automatic
        // inference still treats conflicting directions as separate paragraphs.
        if (first.WritingMode != second.WritingMode || first.FlowDirection != second.FlowDirection) return false;
        if (
            !double.IsFinite(first.RotationDegrees) || !double.IsFinite(second.RotationDegrees) ||
            Math.Abs(Math.IEEERemainder(first.RotationDegrees, 360)) > .5 ||
            Math.Abs(Math.IEEERemainder(second.RotationDegrees, 360)) > .5 ||
            first.ParentRegionId != second.ParentRegionId && (first.ParentRegionId.HasValue || second.ParentRegionId.HasValue) ||
            IsStructured(first.LayoutText ?? first.Text) || IsStructured(second.LayoutText ?? second.Text) ||
            (first.LayoutText ?? first.Text).Contains('\n') || (second.LayoutText ?? second.Text).Contains('\n')) return false;

        var vertical = first.WritingMode == WritingMode.Vertical;
        var a = first.Bounds; var b = second.Bounds;
        var thicknessA = vertical ? a.Size.Width : a.Size.Height;
        var thicknessB = vertical ? b.Size.Width : b.Size.Height;
        var thickness = Math.Min(thicknessA, thicknessB);
        if (thickness <= 0 || Math.Max(thicknessA, thicknessB) > thickness * 1.25) return false;
        var gap = vertical ? a.Left - b.Right : a.Bottom - b.Top;
        if (gap < -thickness * .2 || gap > thickness * .9) return false;
        var startDifference = vertical ? a.Top - b.Top :
            first.FlowDirection == TextFlowDirection.RightToLeft ? a.Right - b.Right : b.Left - a.Left;
        // A new first-line indent, large column shift or short completed line starts a new paragraph.
        if (startDifference > thickness * .55 || startDifference < -thickness * 1.6) return false;
        var advanceA = vertical ? a.Size.Height : a.Size.Width;
        var advanceB = vertical ? b.Size.Height : b.Size.Width;
        if (first.ParentRegionId is null && advanceA < advanceB * .72) return false;
        return true;
    }

    private static bool IsStructured(string text) => text.Contains('\t') || text.Contains("  ", StringComparison.Ordinal) ||
        StructuredLine().IsMatch(text);

    [GeneratedRegex(@"^\s*(?:[•●○■□◆◇▶►※・*]|[-+]\s|(?:\d{1,4}|[①-⑳])[.)、．:]|```|//|[{}<>])", RegexOptions.CultureInvariant)]
    private static partial Regex StructuredLine();

    private static bool NeedsSpace(char first, char second) => IsWestern(first) && IsWestern(second);
    private static bool IsWestern(char value) => value <= '\u024f' && char.IsLetterOrDigit(value);
}
