using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<PdfTextDecorationOverlayViewModel> DecorationOverlayItems { get; } = [];

    public IReadOnlyList<PdfTextDecoration> GetTextDecorations() => _project?.TextDecorations ?? [];

    public void ApplyTextDecorations(IReadOnlyList<PdfTextDecoration> decorations)
    {
        if (_project is null) return;
        _project = _project with { TextDecorations = decorations.ToArray() };
        RefreshPageDecorationItems();
        MarkNonUndoableChange();
        StatusMessage = "ページ番号・ヘッダー／フッター・透かしを更新しました。";
    }

    private void RefreshPageDecorationItems()
    {
        DecorationOverlayItems.Clear();
        if (_project is null || SelectedPage is null ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics)) return;
        var number = SelectedPage.PageNumber;
        var total = PageItems.Count;
        var scale = metrics.PixelWidth / Math.Max(1, metrics.WidthPoints);
        var fontPath = Path.Combine(AppContext.BaseDirectory, "components", "fonts", "NotoSansJP.ttf");
        var fontFamily = File.Exists(fontPath)
            ? new FontFamily(new Uri(Path.GetDirectoryName(fontPath)! + Path.DirectorySeparatorChar), "./#Noto Sans JP")
            : new FontFamily("Noto Sans JP");
        foreach (var item in _project.TextDecorations.Where(item => item.AppliesToPage(number, total)))
        {
            var text = item.TextForPage(number, total);
            if (string.IsNullOrWhiteSpace(text)) continue;
            Color color;
            try { color = (Color)ColorConverter.ConvertFromString(item.ColorHex); }
            catch (FormatException) { color = Colors.Black; }
            var brush = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(Math.Clamp(item.Opacity, 0, 1) * 255), color.R, color.G, color.B));
            brush.Freeze();
            var margin = item.MarginPoints * scale;
            var fontSize = item.FontSizePoints * scale;
            var position = item.PositionForPage(number);
            var top = position switch
            {
                PdfTextDecorationPosition.TopLeft or PdfTextDecorationPosition.TopCenter or PdfTextDecorationPosition.TopRight => margin,
                PdfTextDecorationPosition.BottomLeft or PdfTextDecorationPosition.BottomCenter or PdfTextDecorationPosition.BottomRight =>
                    metrics.PixelHeight - margin - fontSize * 1.35,
                _ => (metrics.PixelHeight - fontSize * 1.35) / 2,
            };
            var alignment = position switch
            {
                PdfTextDecorationPosition.TopLeft or PdfTextDecorationPosition.BottomLeft => TextAlignment.Left,
                PdfTextDecorationPosition.TopRight or PdfTextDecorationPosition.BottomRight => TextAlignment.Right,
                _ => TextAlignment.Center,
            };
            DecorationOverlayItems.Add(new PdfTextDecorationOverlayViewModel(
                text, margin, top, Math.Max(1, metrics.PixelWidth - margin * 2),
                fontSize, alignment, brush, fontFamily));
        }
    }
}
