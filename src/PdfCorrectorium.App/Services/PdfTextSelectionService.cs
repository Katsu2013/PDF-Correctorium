using System.Windows;

namespace PdfCorrectorium.App.Services;

/// <summary>墨消しとリンクで共用する、PDF文字の選択とクリック範囲の生成です。</summary>
internal static class PdfTextSelectionService
{
    internal static Rect Normalize(Rect value, IReadOnlyList<PdfSelectableTextCharacter> characters,
        double pageWidth, double pageHeight)
    {
        var heights = characters.Where(c => c.Height > 0 && double.IsFinite(c.Height)).Select(c => c.Height).Order().ToArray();
        var widths = characters.Where(c => c.Width > 0 && double.IsFinite(c.Width)).Select(c => c.Width).Order().ToArray();
        var width = Math.Min(pageWidth, Math.Max(value.Width, Math.Clamp(widths.Length == 0 ? 4 : widths[widths.Length / 2] * .45, 3, 10)));
        var height = Math.Min(pageHeight, Math.Max(value.Height, Math.Clamp(heights.Length == 0 ? 6 : heights[heights.Length / 2] * .45, 4, 12)));
        return new Rect(Math.Clamp(value.Left + value.Width / 2 - width / 2, 0, Math.Max(0, pageWidth - width)),
            Math.Clamp(value.Top + value.Height / 2 - height / 2, 0, Math.Max(0, pageHeight - height)), width, height);
    }

    internal static PdfSelectableTextCharacter[] Select(IReadOnlyList<PdfSelectableTextCharacter> characters,
        Rect hitSelection, Rect pointerSelection)
    {
        var horizontal = pointerSelection.Width >= pointerSelection.Height;
        return characters.Where(c =>
        {
            if (string.IsNullOrWhiteSpace(c.Text) || c.Width <= 0 || c.Height <= 0) return false;
            var box = new Rect(c.Left, c.Top, c.Width, c.Height);
            var center = horizontal ? c.Left + c.Width / 2 : c.Top + c.Height / 2;
            var start = horizontal ? pointerSelection.Left : pointerSelection.Top;
            var end = horizontal ? pointerSelection.Right : pointerSelection.Bottom;
            return hitSelection.IntersectsWith(box) && center >= start - .01 && center <= end + .01;
        }).ToArray();
    }

    // Each row/column remains independent. Large gaps split separate words/columns rather
    // than turning the intervening page into a clickable link.
    internal static IReadOnlyList<Rect> BuildAreas(IReadOnlyList<PdfSelectableTextCharacter> characters, bool vertical)
    {
        var boxes = characters.Select(c => new Rect(c.Left, c.Top, c.Width, c.Height))
            .Where(b => !b.IsEmpty && b.Width > 0 && b.Height > 0)
            .Distinct().OrderBy(b => vertical ? b.Left : b.Top).ToArray();
        var lines = new List<List<Rect>>();
        foreach (var box in boxes)
        {
            var match = lines.FirstOrDefault(line =>
            {
                var first = line[0];
                var overlap = vertical ? Math.Min(first.Right, box.Right) - Math.Max(first.Left, box.Left)
                    : Math.Min(first.Bottom, box.Bottom) - Math.Max(first.Top, box.Top);
                return overlap >= .45 * (vertical ? Math.Min(first.Width, box.Width) : Math.Min(first.Height, box.Height));
            });
            if (match is null) lines.Add([box]); else match.Add(box);
        }
        var result = new List<Rect>();
        foreach (var line in lines)
        {
            Rect? run = null;
            foreach (var box in line.OrderBy(b => vertical ? b.Top : b.Left))
            {
                if (run is { } current)
                {
                    var gap = vertical ? box.Top - current.Bottom : box.Left - current.Right;
                    if (gap > (vertical ? box.Height : box.Width) * 1.5)
                    { result.Add(current); run = box; }
                    else { current.Union(box); run = current; }
                }
                else run = box;
            }
            if (run is { } last) result.Add(last);
        }
        return result;
    }
}
