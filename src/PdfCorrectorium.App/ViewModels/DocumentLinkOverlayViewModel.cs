using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>OCR領域に属さない任意矩形リンクをプレビューへ表示する読み取り専用モデルです。</summary>
public sealed class DocumentLinkOverlayViewModel(PdfInternalLink link, System.Windows.Rect bounds)
{
    public Guid Id => link.Id;
    public PdfInternalLink Link => link;
    public double Left => bounds.Left;
    public double Top => bounds.Top;
    public double Width => bounds.Width;
    public double Height => bounds.Height;
    public string Description => string.IsNullOrWhiteSpace(link.Description)
        ? link.ExternalUri ?? "ページリンク"
        : link.Description;
    public bool IsExternal => !string.IsNullOrWhiteSpace(link.ExternalUri);
}
