using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.Controls;

public sealed partial class OcrCharacterLayer
{
    public static readonly DependencyProperty ReadingsProperty = Register<IReadOnlyList<WordReadingMatch>?>(nameof(Readings), null);
    public static readonly DependencyProperty ShowReadingsProperty = Register(nameof(ShowReadings), true);
    public static readonly DependencyProperty IsVerticalWritingProperty = Register(nameof(IsVerticalWriting), false);
    public static readonly DependencyProperty InverseZoomProperty = Register(nameof(InverseZoom), 1d);
    public static readonly DependencyProperty PageLeftProperty = Register(nameof(PageLeft), 0d);
    public static readonly DependencyProperty PageTopProperty = Register(nameof(PageTop), 0d);
    public static readonly DependencyProperty PageWidthProperty = Register(nameof(PageWidth), double.PositiveInfinity);
    public IReadOnlyList<WordReadingMatch>? Readings { get => (IReadOnlyList<WordReadingMatch>?)GetValue(ReadingsProperty); set => SetValue(ReadingsProperty, value); }
    public bool ShowReadings { get => (bool)GetValue(ShowReadingsProperty); set => SetValue(ShowReadingsProperty, value); }
    public bool IsVerticalWriting { get => (bool)GetValue(IsVerticalWritingProperty); set => SetValue(IsVerticalWritingProperty, value); }
    public double InverseZoom { get => (double)GetValue(InverseZoomProperty); set => SetValue(InverseZoomProperty, value); }
    public double PageLeft { get => (double)GetValue(PageLeftProperty); set => SetValue(PageLeftProperty, value); }
    public double PageTop { get => (double)GetValue(PageTopProperty); set => SetValue(PageTopProperty, value); }
    public double PageWidth { get => (double)GetValue(PageWidthProperty); set => SetValue(PageWidthProperty, value); }
    internal int LastDrawnRubyCount { get; private set; }
    private readonly List<(Rect Bounds, WordReadingMatch Reading)> _rubyHitAreas = [];
    internal IReadOnlyList<(Rect Bounds, WordReadingMatch Reading)> RubyHitAreas => _rubyHitAreas;
    internal WordReadingMatch? FindReadingAt(Point point) => ShowReadings ?
        _rubyHitAreas.LastOrDefault(item => item.Bounds.Contains(point)).Reading : null;
    private Dictionary<string, FormattedText>? _rubyGlyphs;
    private static readonly Brush RubyInk = ColorBrush("#1D4ED8");
    private static readonly Brush RubyPaper = ColorBrush("#F0FFFFFF");

    private void DrawReadings(DrawingContext context, IReadOnlyList<CharacterOverlayCell> cells, CultureInfo culture, double dpi)
    {
        if (!ShowReadings || Readings is not { Count: > 0 } readings) return;
        var offsets = new int[cells.Count + 1];
        for (var index = 0; index < cells.Count; index++) offsets[index + 1] = offsets[index] + cells[index].Text.Length;
        var inverse = double.IsFinite(InverseZoom) && InverseZoom > 0 ? InverseZoom : 1;
        var gap = 1.5 * inverse;
        FormattedText Glyph(string value)
        {
            _rubyGlyphs ??= new(StringComparer.Ordinal);
            if (_rubyGlyphs.TryGetValue(value, out var cached)) return cached;
            if (_rubyGlyphs.Count >= MaximumCachedGlyphs) _rubyGlyphs.Clear();
            var glyph = new FormattedText(value, culture, FlowDirection.LeftToRight, _normalTypeface!, 100, RubyInk, dpi) { LineHeight = 100 };
            _rubyGlyphs.Add(value, glyph); return glyph;
        }
        void Paint(string value, Rect rectangle)
        {
            var glyph = Glyph(value);
            context.PushTransform(new MatrixTransform(rectangle.Width / Math.Max(1, glyph.WidthIncludingTrailingWhitespace), 0, 0,
                rectangle.Height / Math.Max(1, glyph.Height), rectangle.Left, rectangle.Top));
            context.DrawText(glyph, new Point()); context.Pop();
        }
        foreach (var reading in readings)
        {
            var first = Array.BinarySearch(offsets, reading.Start);
            var end = Array.BinarySearch(offsets, reading.Start + reading.Length);
            if (first < 0 || end <= first || end > cells.Count) continue; // Never split a Unicode text element.
            var bounds = Rect.Empty;
            for (var index = first; index < end; index++)
                bounds.Union(new Rect(cells[index].Left, cells[index].Top, Math.Max(0, cells[index].Width), Math.Max(0, cells[index].Height)));
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;
            var thickness = IsVerticalWriting ? bounds.Width : bounds.Height;
            var fontHeight = Math.Clamp(thickness * .5, 7 * inverse, 14 * inverse);
            if (!IsVerticalWriting)
            {
                var glyph = Glyph(reading.ReadingText);
                var width = Math.Min(glyph.WidthIncludingTrailingWhitespace / Math.Max(1, glyph.Height) * fontHeight, bounds.Width + thickness * .5);
                var minX = -PageLeft + gap;
                var x = Math.Clamp(bounds.Left + (bounds.Width - width) / 2, minX, Math.Max(minX, PageWidth - PageLeft - width - gap));
                var y = Math.Max(-PageTop + gap, bounds.Top - fontHeight - gap);
                var rectangle = new Rect(x, y, width, fontHeight);
                context.DrawRectangle(RubyPaper, null, new Rect(x - gap / 2, y, width + gap, fontHeight));
                Paint(reading.ReadingText, rectangle);
                _rubyHitAreas.Add((rectangle, reading));
            }
            else
            {
                var pieces = StringInfo.ParseCombiningCharacters(reading.ReadingText);
                if (pieces.Length == 0) continue;
                var height = Math.Min(fontHeight * pieces.Length, bounds.Height + thickness * .5);
                var y = Math.Max(-PageTop + gap, bounds.Top + (bounds.Height - height) / 2);
                var x = bounds.Right + gap;
                if (PageLeft + x + fontHeight > PageWidth) x = Math.Max(-PageLeft + gap, bounds.Left - fontHeight - gap);
                context.DrawRectangle(RubyPaper, null, new Rect(x, y - gap / 2, fontHeight, height + gap));
                _rubyHitAreas.Add((new Rect(x, y, fontHeight, height), reading));
                for (var index = 0; index < pieces.Length; index++)
                {
                    var next = index + 1 < pieces.Length ? pieces[index + 1] : reading.ReadingText.Length;
                    Paint(reading.ReadingText[pieces[index]..next], new Rect(x, y + height * index / pieces.Length, fontHeight, height / pieces.Length));
                }
            }
            LastDrawnRubyCount++;
        }
    }
}
