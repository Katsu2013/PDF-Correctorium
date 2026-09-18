using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.Core.Geometry;

/// <summary>矩形と自由形状の墨消しで共通使用する、純粋な幾何判定です。</summary>
public static class PdfRedactionGeometry
{
    /// <summary>通常の墨消しで、描画座標の丸め差を吸収する安全余白（PDFポイント）です。</summary>
    public const double DefaultSafetyPaddingPoints = 1d;

    /// <summary>
    /// PDF文字選択の墨消しで、グリフ境界外のアンチエイリアス、縁取り、影を含めて覆う安全余白です。
    /// 既存プロジェクトに保存済みの文字単位墨消しにも適用します。
    /// </summary>
    public const double TextSelectionSafetyPaddingPoints = 0.1d;

    public static double GetSafetyPadding(PdfRedaction redaction) =>
        redaction.ShapeKind == PdfRedactionShapeKind.TextSelection
            ? TextSelectionSafetyPaddingPoints
            : DefaultSafetyPaddingPoints;

    public static IReadOnlyList<PdfPoint> GetEffectivePath(PdfRedaction redaction) =>
        redaction.PathPoints.Count >= 3
            ? redaction.PathPoints
            :
            [
                new(redaction.Bounds.Left, redaction.Bounds.Bottom),
                new(redaction.Bounds.Right, redaction.Bounds.Bottom),
                new(redaction.Bounds.Right, redaction.Bounds.Top),
                new(redaction.Bounds.Left, redaction.Bounds.Top),
            ];

    public static PdfRectangle GetBounds(IReadOnlyList<PdfPoint> points)
    {
        if (points.Count == 0 || points.Any(point => !point.IsFinite)) return default;
        var left = points.Min(point => point.X);
        var right = points.Max(point => point.X);
        var bottom = points.Min(point => point.Y);
        var top = points.Max(point => point.Y);
        return new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(right - left, top - bottom));
    }

    /// <summary>文字境界等の矩形が、指定した安全余白込みで墨消し形状と交差するか判定します。</summary>
    public static bool IntersectsRectangle(
        PdfRedaction redaction,
        double left,
        double bottom,
        double right,
        double top,
        double padding = 0d)
    {
        if (right <= redaction.Bounds.Left - padding || left >= redaction.Bounds.Right + padding ||
            top <= redaction.Bounds.Bottom - padding || bottom >= redaction.Bounds.Top + padding)
            return false;
        if (redaction.PathPoints.Count < 3) return true;

        var expandedLeft = left - padding;
        var expandedRight = right + padding;
        var expandedBottom = bottom - padding;
        var expandedTop = top + padding;
        var path = redaction.PathPoints;
        if (path.Any(point => PointInRectangle(point, expandedLeft, expandedBottom, expandedRight, expandedTop)))
            return true;
        if (Contains(path, new PdfPoint(expandedLeft, expandedBottom)) ||
            Contains(path, new PdfPoint(expandedRight, expandedBottom)) ||
            Contains(path, new PdfPoint(expandedRight, expandedTop)) ||
            Contains(path, new PdfPoint(expandedLeft, expandedTop)))
            return true;

        for (var index = 0; index < path.Count; index++)
        {
            var first = path[index];
            var second = path[(index + 1) % path.Count];
            if (SegmentIntersectsRectangle(first, second, expandedLeft, expandedBottom, expandedRight, expandedTop))
                return true;
        }
        return false;
    }

    /// <summary>形状内、または輪郭から安全余白以内にある点か判定します。</summary>
    public static bool Contains(PdfRedaction redaction, PdfPoint point, double padding = 0d)
    {
        if (point.X < redaction.Bounds.Left - padding || point.X > redaction.Bounds.Right + padding ||
            point.Y < redaction.Bounds.Bottom - padding || point.Y > redaction.Bounds.Top + padding)
            return false;
        if (redaction.PathPoints.Count < 3) return true;
        if (Contains(redaction.PathPoints, point)) return true;
        if (padding <= 0) return false;
        for (var index = 0; index < redaction.PathPoints.Count; index++)
            if (DistanceToSegment(point, redaction.PathPoints[index], redaction.PathPoints[(index + 1) % redaction.PathPoints.Count]) <= padding)
                return true;
        return false;
    }

    private static bool Contains(IReadOnlyList<PdfPoint> path, PdfPoint point)
    {
        var inside = false;
        for (int current = 0, previous = path.Count - 1; current < path.Count; previous = current++)
        {
            var a = path[current];
            var b = path[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static bool PointInRectangle(PdfPoint point, double left, double bottom, double right, double top) =>
        point.X >= left && point.X <= right && point.Y >= bottom && point.Y <= top;

    private static bool SegmentIntersectsRectangle(
        PdfPoint a, PdfPoint b, double left, double bottom, double right, double top) =>
        SegmentsIntersect(a, b, new(left, bottom), new(right, bottom)) ||
        SegmentsIntersect(a, b, new(right, bottom), new(right, top)) ||
        SegmentsIntersect(a, b, new(right, top), new(left, top)) ||
        SegmentsIntersect(a, b, new(left, top), new(left, bottom));

    private static bool SegmentsIntersect(PdfPoint a, PdfPoint b, PdfPoint c, PdfPoint d)
    {
        static double Cross(PdfPoint p, PdfPoint q, PdfPoint r) =>
            (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        var abC = Cross(a, b, c);
        var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a);
        var cdB = Cross(c, d, b);
        return (abC == 0 && OnSegment(a, b, c)) || (abD == 0 && OnSegment(a, b, d)) ||
               (cdA == 0 && OnSegment(c, d, a)) || (cdB == 0 && OnSegment(c, d, b)) ||
               ((abC > 0) != (abD > 0) && (cdA > 0) != (cdB > 0));
    }

    private static bool OnSegment(PdfPoint a, PdfPoint b, PdfPoint point) =>
        point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X) &&
        point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y);

    private static double DistanceToSegment(PdfPoint point, PdfPoint a, PdfPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return Math.Sqrt(Math.Pow(point.X - a.X, 2) + Math.Pow(point.Y - a.Y, 2));
        var position = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / (dx * dx + dy * dy), 0d, 1d);
        var x = a.X + position * dx;
        var y = a.Y + position * dy;
        return Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2));
    }
}
