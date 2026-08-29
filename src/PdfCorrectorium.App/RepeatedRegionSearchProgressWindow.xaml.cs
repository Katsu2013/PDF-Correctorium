using System.ComponentModel;
using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

/// <summary>定型領域のページ横断検索について、進捗と中止操作を表示します。</summary>
public partial class RepeatedRegionSearchProgressWindow : Window
{
    private bool _allowClose;
    private bool _cancellationRequested;

    public RepeatedRegionSearchProgressWindow(int totalPages)
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        PageProgressTextBlock.Text = LocalizationService.IsEnglish
            ? $"0 / {totalPages} pages"
            : $"0 / {totalPages} ページ";
    }

    /// <summary>利用者が検索の中止を要求したときに発生します。</summary>
    public event EventHandler? CancellationRequested;

    /// <summary>最新の検索進捗を画面へ反映します。</summary>
    public void Report(RepeatedRegionSearchProgress progress)
    {
        SearchProgressBar.Value = progress.Percentage;
        StatusTextBlock.Text = progress.CurrentPageNumber <= 0
            ? LocalizationService.Translate("検索を準備しています...")
            : LocalizationService.IsEnglish
                ? $"Searching page {progress.CurrentPageNumber}..."
                : $"{progress.CurrentPageNumber}ページを検索しています...";
        PageProgressTextBlock.Text = LocalizationService.IsEnglish
            ? $"{progress.CompletedPages} / {progress.TotalPages} pages"
            : $"{progress.CompletedPages} / {progress.TotalPages} ページ";
        PercentTextBlock.Text = $"{progress.Percentage:0}%";
        DetailTextBlock.Text = LocalizationService.IsEnglish
            ? $"{progress.CandidateCount} candidate(s) found"
            : $"候補 {progress.CandidateCount} 件";
    }

    /// <summary>処理終了後、閉じる操作を許可して進捗画面を閉じます。</summary>
    public void CompleteAndClose()
    {
        if (_allowClose) return;
        _allowClose = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => RequestCancellation();

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        RequestCancellation();
    }

    private void RequestCancellation()
    {
        if (_cancellationRequested) return;
        _cancellationRequested = true;
        CancelButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationService.Translate("現在のページ検索が終わり次第、中止します...");
        CancellationRequested?.Invoke(this, EventArgs.Empty);
    }
}
