using System.ComponentModel;
using System.Windows;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.Services;

/// <summary>確認前の候補。PDFもプロジェクトも変更せず、画像は保持しません。</summary>
public sealed class DocumentBatchCandidate : INotifyPropertyChanged
{
    public required int PageNumber { get; init; }
    public required string Description { get; init; }
    public string Status { get; init; } = "確認して適用";
    public int? DestinationPage { get; init; }
    public string DestinationSummary => DestinationPage is { } page ? $"{page}ページ" : "—";
    public bool CanInclude { get; init; } = true;
    public PdfInternalLink? Link { get; init; }
    public IReadOnlyList<PdfRedaction> Redactions { get; init; } = [];
    public IReadOnlyList<Rect> PreviewAreas { get; init; } = [];
    public IReadOnlyList<System.Windows.Point> PreviewPath { get; init; } = [];
    public double PreviewWidth { get; init; }
    public double PreviewHeight { get; init; }
    private bool _isIncluded;
    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            value &= CanInclude;
            if (_isIncluded == value) return;
            _isIncluded = value;
            PropertyChanged?.Invoke(this, new(nameof(IsIncluded)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public enum DocumentBatchKind { AutomaticLinks, RepeatedRedaction, TextRedaction }
