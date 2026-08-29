using System.ComponentModel;
using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

/// <summary>前処理付き一括自動調整の進捗と中止操作を表示します。</summary>
public partial class BatchCharacterAdjustmentProgressWindow : Window
{
    private bool _allowClose;
    private bool _cancellationRequested;

    public BatchCharacterAdjustmentProgressWindow(int totalPages)
    {
        InitializeComponent();
        PageProgressTextBlock.Text = LocalizationService.IsEnglish
            ? $"0 / {totalPages} pages"
            : $"0 / {totalPages} ページ";
        LocalizationService.Apply(this);
    }

    /// <summary>利用者が中止ボタンまたはウィンドウの閉じるボタンを押したときに発生します。</summary>
    public event EventHandler? CancellationRequested;

    /// <summary>最新の進捗値を画面へ反映します。</summary>
    public void Report(BatchCharacterAdjustmentProgress progress)
    {
        AdjustmentProgressBar.Value = progress.Percentage;
        StatusTextBlock.Text = LocalizationService.IsEnglish
            ? progress.TargetLineCountOnCurrentPage > 0
                ? $"Adjusting page {progress.CurrentPageNumber}: line {progress.ProcessedLineCountOnCurrentPage} of {progress.TargetLineCountOnCurrentPage}..."
                : $"Adjusting page {progress.CurrentPageNumber}..."
            : progress.TargetLineCountOnCurrentPage > 0
                ? $"{progress.CurrentPageNumber}ページを自動調整しています（{progress.ProcessedLineCountOnCurrentPage}/{progress.TargetLineCountOnCurrentPage}行）..."
                : $"{progress.CurrentPageNumber}ページを自動調整しています...";
        PageProgressTextBlock.Text = LocalizationService.IsEnglish
            ? $"{progress.CompletedPages} / {progress.TotalPages} pages"
            : $"{progress.CompletedPages} / {progress.TotalPages} ページ";
        PercentTextBlock.Text = $"{progress.Percentage:0}%";
        DetailTextBlock.Text = LocalizationService.IsEnglish
            ? $"Adjusted {progress.AdjustedLineCount} of {progress.TargetLineCount} lines"
            : $"対象 {progress.TargetLineCount} 行 / 調整済み {progress.AdjustedLineCount} 行";
    }

    /// <summary>処理終了後、閉じる操作を許可して進捗画面を閉じます。</summary>
    public void CompleteAndClose()
    {
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
        StatusTextBlock.Text = LocalizationService.IsEnglish
            ? "Canceling after the current operation..."
            : "現在の処理が終わり次第、中止します...";
        CancellationRequested?.Invoke(this, EventArgs.Empty);
    }
}
