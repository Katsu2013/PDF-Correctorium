using PdfCorrectorium.Core.Documents;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>OCR領域に属さない任意矩形リンクをプレビューへ表示する読み取り専用モデルです。</summary>
public sealed class DocumentLinkOverlayViewModel(PdfInternalLink link, System.Windows.Rect bounds, string destinationSummary = "", int sourcePageNumber = 0) : INotifyPropertyChanged
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
    public int SourcePageNumber => sourcePageNumber;
    public bool IsExternal => !string.IsNullOrWhiteSpace(link.ExternalUri);
    public bool IsEditable => link.UnsupportedAction is null;
    public bool CanDelete => IsEditable && !link.IsGeometryLocked;
    public string DestinationSummary => destinationSummary;
    public string ToolTip => $"{destinationSummary}\n{Description}{(link.IsGeometryLocked ? "\nロック中（移動・サイズ変更・削除不可）" : "")}";
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
