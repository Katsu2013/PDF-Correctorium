using System.Windows.Markup;
using System.Windows.Media;

namespace PdfCorrectorium.App.Resources;

/// <summary>
/// Creates shared, fill-only icon geometry with a genuinely transparent clearance
/// around a simple badge. Keep coordinates in the icon's fixed 24-by-24 canvas.
/// </summary>
[MarkupExtensionReturnType(typeof(Geometry))]
public sealed class BadgeIconGeometryExtension : MarkupExtension
{
    private const double GeometryTolerance = 0.01;

    public Geometry BaseGeometry { get; set; } = Geometry.Empty;
    public Geometry BadgeGeometry { get; set; } = Geometry.Empty;
    public bool IsBaseFilled { get; set; }
    public double BaseThickness { get; set; } = 1.8;
    public double BadgeThickness { get; set; } = 2;
    public double ClearanceThickness { get; set; } = 8;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(BaseGeometry);
        ArgumentNullException.ThrowIfNull(BadgeGeometry);
        ValidateThickness(BaseThickness, nameof(BaseThickness));
        ValidateThickness(BadgeThickness, nameof(BadgeThickness));
        ValidateThickness(ClearanceThickness, nameof(ClearanceThickness));
        if (ClearanceThickness < BadgeThickness)
        {
            throw new ArgumentOutOfRangeException(nameof(ClearanceThickness),
                "Badge clearance cannot be narrower than the badge itself.");
        }

        // Construct once when the resource dictionary is loaded, not on redraw.
        // Subtraction instead of a white stroke also works on selected/dark backgrounds.
        var baseInk = IsBaseFilled
            ? BaseGeometry
            : BaseGeometry.GetWidenedPathGeometry(CreatePen(BaseThickness), GeometryTolerance, ToleranceType.Absolute);
        var clearance = BadgeGeometry.GetWidenedPathGeometry(CreatePen(ClearanceThickness), GeometryTolerance, ToleranceType.Absolute);
        var badgeInk = BadgeGeometry.GetWidenedPathGeometry(CreatePen(BadgeThickness), GeometryTolerance, ToleranceType.Absolute);
        var separatedBase = Geometry.Combine(baseInk, clearance, GeometryCombineMode.Exclude, null,
            GeometryTolerance, ToleranceType.Absolute);
        var result = Geometry.Combine(separatedBase, badgeInk, GeometryCombineMode.Union, null,
            GeometryTolerance, ToleranceType.Absolute);
        result.Freeze();
        return result;
    }

    private static Pen CreatePen(double thickness) => new(Brushes.Black, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };

    private static void ValidateThickness(double thickness, string name)
    {
        if (!double.IsFinite(thickness) || thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Icon stroke widths must be finite and positive.");
        }
    }
}
