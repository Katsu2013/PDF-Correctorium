namespace PdfCorrectorium.App.Services;

/// <summary>指定された対象ページの文字自動調整について、画面へ通知する現在の進捗を表します。</summary>
public sealed record BatchCharacterAdjustmentProgress(
    int CompletedPages,
    int TotalPages,
    int CurrentPageNumber,
    int AdjustedLineCount,
    int TargetLineCount,
    int ProcessedLineCountOnCurrentPage = 0,
    int TargetLineCountOnCurrentPage = 0)
{
    /// <summary>完了済みページ数と現在ページ内の処理済み行数から算出した 0～100 の進捗率です。</summary>
    public double Percentage => TotalPages <= 0
        ? 0
        : Math.Clamp(
            (CompletedPages + CurrentPageFraction) * 100d / TotalPages,
            0,
            100);

    private double CurrentPageFraction => TargetLineCountOnCurrentPage <= 0
        ? 0
        : Math.Clamp(
            ProcessedLineCountOnCurrentPage / (double)TargetLineCountOnCurrentPage,
            0,
            1);
}
