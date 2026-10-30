using System.Globalization;
using System.Text;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.Core.Documents;

public enum ReadAloudScope { Selection, CurrentPage, RemainingPages }
public sealed record SpeechTextSegment(Guid? RegionId, string Text, PdfRectangle Bounds)
{
    public Guid? ParentRegionId { get; init; }
    public Guid? ParagraphGroupId { get; init; }
    public string? LayoutText { get; init; }
    public WritingMode WritingMode { get; init; }
    public TextFlowDirection FlowDirection { get; init; }
    public double RotationDegrees { get; init; }
    public bool BreakBefore { get; init; }
    public bool IsPartialSelection { get; init; }
}

/// <summary>Plain spoken text, independent of engines and UI. Redactions fail closed at line boundaries.</summary>
public static class SpeechText
{
    public static IReadOnlyList<SpeechTextSegment> Build(OcrPage page, IReadOnlyList<PdfRedaction> redactions,
        IReadOnlyDictionary<Guid, IReadOnlyList<int>>? selection = null, IReadOnlyList<WordReading>? commonReadings = null)
    {
        var order = page.ReadingOrder.Select((id, index) => (id, index))
            .GroupBy(item => item.id).ToDictionary(group => group.Key, group => group.First().index);
        var result = new List<SpeechTextSegment>();
        var matcher = commonReadings is { Count: > 0 } ? new WordReadingMatcher(commonReadings) : WordReadingMatcher.Empty;
        var breakBefore = true;
        foreach (var region in page.TextRegions.OrderBy(region => order.GetValueOrDefault(region.Id, int.MaxValue)))
        {
            if (region.IsDeleted || !region.Output.IncludeInSpeech || selection is not null && !selection.ContainsKey(region.Id))
            { breakBefore = true; continue; }
            var bounds = GetBounds(region.EditedGeometry);
            var originalBounds = GetBounds(region.OriginalGeometry);
            if (!bounds.IsValid || redactions.Any(redaction => !redaction.Bounds.IsValid ||
                redaction.SourceRegionId == region.Id || Intersects(bounds, redaction.Bounds) ||
                !originalBounds.IsValid || Intersects(originalBounds, redaction.Bounds)))
            { breakBefore = true; continue; }
            var text = region.EffectiveText;
            if (selection?.GetValueOrDefault(region.Id) is { Count: > 0 } indices)
            {
                var elements = StringInfo.ParseCombiningCharacters(text);
                var builder = new StringBuilder();
                foreach (var index in indices.Distinct().Order())
                    if (index >= 0 && index < elements.Length)
                    {
                        var start = elements[index];
                        builder.Append(text.AsSpan(start, (index + 1 < elements.Length ? elements[index + 1] : text.Length) - start));
                    }
                text = builder.ToString();
            }
            var layoutText = text;
            text = ApplyMatches(text, matcher.FindMatches(text, region.WordReadings));
            if (string.IsNullOrWhiteSpace(text)) { breakBefore = true; continue; }
            result.Add(new(region.Id, text, bounds)
            {
                ParentRegionId = region.ParentRegionId, ParagraphGroupId = region.ParagraphGroupId,
                LayoutText = layoutText,
                WritingMode = region.WritingMode, FlowDirection = region.FlowDirection,
                RotationDegrees = region.EditedGeometry.RotationDegrees, BreakBefore = breakBefore,
                IsPartialSelection = selection?.GetValueOrDefault(region.Id) is { Count: > 0 },
            });
            breakBefore = false;
        }
        return result;
    }

    public static string ApplyReadings(string text, IReadOnlyList<WordReading> readings)
        => ApplyMatches(text, WordReadingRules.FindMatches(text, readings));

    private static string ApplyMatches(string text, IReadOnlyList<WordReadingMatch> matches)
    {
        if (matches.Count == 0) return text;
        var result = new StringBuilder();
        var index = 0;
        foreach (var match in matches)
        {
            result.Append(text.AsSpan(index, match.Start - index));
            result.Append(match.ReadingText);
            index = match.Start + match.Length;
        }
        result.Append(text.AsSpan(index));
        return result.ToString();
    }

    public static PdfRectangle GetBounds(TextGeometry geometry)
    {
        var rect = geometry.LocalBounds;
        var angle = geometry.RotationDegrees * Math.PI / 180;
        var cos = Math.Cos(angle); var sin = Math.Sin(angle); var center = geometry.RotationCenter;
        var points = new[] { new PdfPoint(rect.Left, rect.Bottom), new PdfPoint(rect.Left, rect.Top),
            new PdfPoint(rect.Right, rect.Top), new PdfPoint(rect.Right, rect.Bottom) }.Select(point =>
        {
            var x = (point.X - center.X) * geometry.HorizontalScale;
            var y = (point.Y - center.Y) * geometry.VerticalScale;
            return new PdfPoint(center.X + x * cos + y * sin, center.Y - x * sin + y * cos);
        }).ToArray();
        var left = points.Min(point => point.X); var bottom = points.Min(point => point.Y);
        return new(new(left, bottom), new(points.Max(point => point.X) - left, points.Max(point => point.Y) - bottom));
    }

    public static bool Intersects(PdfRectangle first, PdfRectangle second) =>
        first.Left < second.Right && second.Left < first.Right && first.Bottom < second.Top && second.Bottom < first.Top;
}
