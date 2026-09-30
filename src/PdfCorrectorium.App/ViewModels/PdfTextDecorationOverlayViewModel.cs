using System.Windows;
using System.Windows.Media;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>ページ画像に重ねる出力前の文字装飾プレビューです。</summary>
public sealed record PdfTextDecorationOverlayViewModel(
    string Text,
    double Left,
    double Top,
    double Width,
    double FontSize,
    TextAlignment Alignment,
    Brush Foreground,
    FontFamily FontFamily);
