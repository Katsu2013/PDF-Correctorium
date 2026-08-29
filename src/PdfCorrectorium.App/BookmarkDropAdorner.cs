using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

/// <summary>
/// しおりをドラッグしている間、挿入先または子階層化の候補をツリー項目上へ描画します。
/// </summary>
internal sealed class BookmarkDropAdorner : Adorner
{
    /// <summary>挿入位置を示す青い細線です。</summary>
    private static readonly Pen InsertionPen = new(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 2);
    /// <summary>子階層として追加するときの薄い背景色です。</summary>
    private static readonly Brush ChildFill = new SolidColorBrush(Color.FromArgb(34, 37, 99, 235));
    /// <summary>現在表示するドロップ位置です。</summary>
    private readonly BookmarkDropPosition _position;

    /// <summary>
    /// 指定したしおり見出しへドロップ位置表示を重ねます。
    /// </summary>
    /// <param name="adornedElement">表示対象となるしおり見出し。</param>
    /// <param name="position">前、子、後のいずれかの移動候補。</param>
    public BookmarkDropAdorner(UIElement adornedElement, BookmarkDropPosition position)
        : base(adornedElement)
    {
        _position = position;
        IsHitTestVisible = false;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = Math.Max(1, AdornedElement.RenderSize.Width);
        var height = Math.Max(1, AdornedElement.RenderSize.Height);

        if (_position == BookmarkDropPosition.AsChild)
        {
            drawingContext.DrawRoundedRectangle(ChildFill, InsertionPen, new Rect(1, 1, width - 2, height - 2), 2, 2);
            return;
        }

        var y = _position == BookmarkDropPosition.Before ? 1 : height - 1;
        drawingContext.DrawLine(InsertionPen, new Point(2, y), new Point(Math.Max(2, width - 2), y));
        drawingContext.DrawEllipse(InsertionPen.Brush, null, new Point(2, y), 2.5, 2.5);
    }
}
