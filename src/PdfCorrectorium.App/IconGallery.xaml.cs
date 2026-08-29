using System.Windows.Controls;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

/// <summary>
/// Blend for Visual Studio で、アプリケーション内のベクターアイコンを
/// 一覧表示して確認するための開発用ギャラリーを表します。
/// </summary>
public partial class IconGallery : UserControl
{
    /// <summary>
    /// アイコンギャラリーを初期化します。
    /// </summary>
    public IconGallery()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
    }
}
