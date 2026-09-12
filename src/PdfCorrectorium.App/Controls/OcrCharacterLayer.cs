using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App.Controls;

/// <summary>
/// Draws all text cells of one OCR region without a WPF control tree per character.
/// Hit testing and editing remain owned by the containing region and its handles.
/// </summary>
public sealed class OcrCharacterLayer : FrameworkElement
{
    public static readonly DependencyProperty CellsProperty = Register<IReadOnlyList<CharacterOverlayCell>?>(nameof(Cells), null);
    public static readonly DependencyProperty TextBrushProperty = Register<Brush>(nameof(TextBrush), Brushes.Black);
    public static readonly DependencyProperty IsCharacterEditModeProperty = Register(nameof(IsCharacterEditMode), false);
    public static readonly DependencyProperty IsRegionSelectedProperty = Register(nameof(IsRegionSelected), false);
    public static readonly DependencyProperty ShowUnselectedBordersProperty = Register(nameof(ShowUnselectedBorders), false);
    public static readonly DependencyProperty CellBorderThicknessProperty = Register(nameof(CellBorderThickness), new Thickness(1));
    public static readonly DependencyProperty RowBorderThicknessProperty = Register(nameof(RowBorderThickness), new Thickness(1.25));
    public static readonly DependencyProperty SelectedBorderThicknessProperty = Register(nameof(SelectedBorderThickness), new Thickness(1.8));
    public static readonly DependencyProperty LockedBorderThicknessProperty = Register(nameof(LockedBorderThickness), new Thickness(1.35));
    public static readonly DependencyProperty SelectedLockedBorderThicknessProperty = Register(nameof(SelectedLockedBorderThickness), new Thickness(2));
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(OcrCharacterLayer),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<CharacterOverlayCell>? Cells { get => (IReadOnlyList<CharacterOverlayCell>?)GetValue(CellsProperty); set => SetValue(CellsProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public bool IsCharacterEditMode { get => (bool)GetValue(IsCharacterEditModeProperty); set => SetValue(IsCharacterEditModeProperty, value); }
    public bool IsRegionSelected { get => (bool)GetValue(IsRegionSelectedProperty); set => SetValue(IsRegionSelectedProperty, value); }
    public bool ShowUnselectedBorders { get => (bool)GetValue(ShowUnselectedBordersProperty); set => SetValue(ShowUnselectedBordersProperty, value); }
    public Thickness CellBorderThickness { get => (Thickness)GetValue(CellBorderThicknessProperty); set => SetValue(CellBorderThicknessProperty, value); }
    public Thickness RowBorderThickness { get => (Thickness)GetValue(RowBorderThicknessProperty); set => SetValue(RowBorderThicknessProperty, value); }
    public Thickness SelectedBorderThickness { get => (Thickness)GetValue(SelectedBorderThicknessProperty); set => SetValue(SelectedBorderThicknessProperty, value); }
    public Thickness LockedBorderThickness { get => (Thickness)GetValue(LockedBorderThicknessProperty); set => SetValue(LockedBorderThicknessProperty, value); }
    public Thickness SelectedLockedBorderThickness { get => (Thickness)GetValue(SelectedLockedBorderThicknessProperty); set => SetValue(SelectedLockedBorderThicknessProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    private static DependencyProperty Register<T>(string name, T value) => DependencyProperty.Register(name, typeof(T),
        typeof(OcrCharacterLayer), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    private static SolidColorBrush ColorBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush UnselectedFill = ColorBrush("#0D64748B");
    private static readonly Brush UnselectedStroke = ColorBrush("#806B7280");
    private static readonly Brush RowFill = ColorBrush("#2000A6B2");
    private static readonly Brush RowStroke = ColorBrush("#DD00838F");
    private static readonly Brush SelectedFill = ColorBrush("#60FFD54F");
    private static readonly Brush SelectedStroke = ColorBrush("#FFFF8A00");
    private static readonly Brush LockedFill = ColorBrush("#285197D6");
    private static readonly Brush LockedStroke = ColorBrush("#E0286DA8");
    private static readonly Brush SelectedLockedFill = ColorBrush("#70FFD54F");
    private static readonly Brush SelectedLockedStroke = ColorBrush("#FFD84315");
    private static readonly Brush SearchFill = ColorBrush("#70FFF1F2");
    private static readonly Brush SearchStroke = ColorBrush("#FFFF0000");
    private static readonly Brush SearchText = ColorBrush("#FFD50000");

    // Bounded, element-owned cache: no document or UI object enters a static cache.
    // Selection/zoom changes reuse glyph layout; font, brush, culture or DPI changes reset it.
    private const int MaximumCachedGlyphs = 128;
    private readonly Dictionary<(string Text, bool Search), FormattedText> _glyphs = [];
    private FontFamily? _cachedFont;
    private Brush? _cachedBrush;
    private CultureInfo? _cachedCulture;
    private double _cachedPixelsPerDip;
    private Typeface? _normalTypeface;
    private Typeface? _searchTypeface;

    public OcrCharacterLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Cells is not { Count: > 0 } cells) return;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var culture = Language.GetSpecificCulture();
        if (!Equals(_cachedFont, FontFamily) || !ReferenceEquals(_cachedBrush, TextBrush) ||
            !Equals(_cachedCulture, culture) || _cachedPixelsPerDip != pixelsPerDip)
        {
            _glyphs.Clear();
            _cachedFont = FontFamily;
            _cachedBrush = TextBrush;
            _cachedCulture = culture;
            _cachedPixelsPerDip = pixelsPerDip;
            _normalTypeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _searchTypeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        }

        foreach (var cell in cells)
        {
            if (!double.IsFinite(cell.Width) || !double.IsFinite(cell.Height) || cell.Width <= 0 || cell.Height <= 0) continue;
            Brush? fill = null;
            Brush? stroke = null;
            var thickness = CellBorderThickness;
            // Preserve the previous XAML trigger precedence, including search over locks.
            if (IsCharacterEditMode && ShowUnselectedBorders) (fill, stroke) = (UnselectedFill, UnselectedStroke);
            if (IsCharacterEditMode && IsRegionSelected) (fill, stroke, thickness) = (RowFill, RowStroke, RowBorderThickness);
            if (cell.IsSelected) (fill, stroke, thickness) = (SelectedFill, SelectedStroke, SelectedBorderThickness);
            if (cell.IsLocked) (fill, stroke, thickness) = (LockedFill, LockedStroke, LockedBorderThickness);
            if (cell.IsSelected && cell.IsLocked) (fill, stroke, thickness) = (SelectedLockedFill, SelectedLockedStroke, SelectedLockedBorderThickness);
            if (cell.IsSearchMatch) (fill, stroke, thickness) = (SearchFill, SearchStroke, SelectedBorderThickness);

            var bounds = new Rect(cell.Left, cell.Top, cell.Width, cell.Height);
            var left = Math.Min(thickness.Left, cell.Width / 2);
            var right = Math.Min(thickness.Right, cell.Width / 2);
            var top = Math.Min(thickness.Top, cell.Height / 2);
            var bottom = Math.Min(thickness.Bottom, cell.Height / 2);
            var inner = new Rect(bounds.Left + left, bounds.Top + top,
                Math.Max(0, bounds.Width - left - right), Math.Max(0, bounds.Height - top - bottom));
            // Draw disjoint border strips so translucent strokes are not doubled at corners.
            if (fill is not null) drawingContext.DrawRectangle(fill, null, inner);
            if (stroke is not null)
            {
                drawingContext.DrawRectangle(stroke, null, new Rect(bounds.Left, bounds.Top, bounds.Width, top));
                drawingContext.DrawRectangle(stroke, null, new Rect(bounds.Left, bounds.Bottom - bottom, bounds.Width, bottom));
                drawingContext.DrawRectangle(stroke, null, new Rect(bounds.Left, inner.Top, left, inner.Height));
                drawingContext.DrawRectangle(stroke, null, new Rect(bounds.Right - right, inner.Top, right, inner.Height));
            }

            var width = Math.Max(0, inner.Width - 2);
            var height = Math.Max(0, inner.Height - 2);
            if (width == 0 || height == 0 || cell.Text.Length == 0) continue;
            var key = (cell.Text, cell.IsSearchMatch);
            if (!_glyphs.TryGetValue(key, out var text))
            {
                if (_glyphs.Count >= MaximumCachedGlyphs) _glyphs.Clear();
                text = new FormattedText(cell.Text, culture, FlowDirection.LeftToRight,
                    cell.IsSearchMatch ? _searchTypeface! : _normalTypeface!, 100,
                    cell.IsSearchMatch ? SearchText : TextBrush, pixelsPerDip)
                { LineHeight = 100 };
                _glyphs.Add(key, text);
            }
            // Same per-cell stretch and 1-unit margin as the former Viewbox/TextBlock.
            var scaleX = width / Math.Max(1, text.WidthIncludingTrailingWhitespace);
            var scaleY = height / Math.Max(1, text.Height);
            drawingContext.PushTransform(new MatrixTransform(scaleX, 0, 0, scaleY, inner.Left + 1, inner.Top + 1));
            drawingContext.DrawText(text, new Point());
            drawingContext.Pop();
        }
    }
}
