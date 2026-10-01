using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.Services;

/// <summary>PDFiumの表示変換を一度だけ採取し、回転・CropBoxを含む文字／リンク座標を変換します。</summary>
internal sealed class PdfPageCoordinateMapper
{
    private readonly Point _origin;
    private readonly Vector _xAxis;
    private readonly Vector _yAxis;
    private PdfPageCoordinateMapper(Point origin, Vector xAxis, Vector yAxis)
        => (_origin, _xAxis, _yAxis) = (origin, xAxis, yAxis);

    internal static PdfPageCoordinateMapper Create(IntPtr page, double pageWidth, double pageHeight,
        int pixelWidth, int pixelHeight)
    {
        const int precision = 64;
        Point Basis(double x, double y)
        {
            if (FPDF_PageToDevice(page, 0, 0, checked(pixelWidth * precision), checked(pixelHeight * precision),
                    0, x, y, out var deviceX, out var deviceY) == 0)
                throw new InvalidDataException("PDFの文字・リンク座標を変換できませんでした。");
            return new Point((double)deviceX / precision, (double)deviceY / precision);
        }
        var origin = Basis(0, 0);
        return new(origin, (Basis(pageWidth, 0) - origin) / pageWidth, (Basis(0, pageHeight) - origin) / pageHeight);
    }

    internal Point Map(double x, double y) => _origin + _xAxis * x + _yAxis * y;
    internal Rect MapBounds(PdfRectangle bounds)
    {
        var corners = new[] { Map(bounds.Left, bounds.Bottom), Map(bounds.Left, bounds.Top),
            Map(bounds.Right, bounds.Bottom), Map(bounds.Right, bounds.Top) };
        return new Rect(new Point(corners.Min(point => point.X), corners.Min(point => point.Y)),
            new Point(corners.Max(point => point.X), corners.Max(point => point.Y)));
    }

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY,
        int rotate, double pageX, double pageY, out int deviceX, out int deviceY);
}
