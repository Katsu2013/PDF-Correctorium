using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

/// <summary>PDF全体から検出した画像最適化候補を一覧表示し、適用方法を選択する画面です。</summary>
public partial class DocumentImageOptimizationWindow : Window
{
    /// <summary>PDF全体の解析結果です。</summary>
    private readonly PdfDocumentImageOptimizationAnalysis _analysis;

    /// <summary>一覧へ表示するページ単位の候補です。</summary>
    public ObservableCollection<DocumentImageOptimizationListItem> Items { get; }

    /// <summary>利用者が選んだ適用方法です。</summary>
    public DocumentImageOptimizationApplyMode ApplyMode { get; private set; }

    /// <summary>一覧で有効になっている解析結果を取得します。</summary>
    public IReadOnlyList<PdfImageOptimizationAnalysis> SelectedAnalyses => Items
        .Where(item => item.IsSelected)
        .Select(item => item.Analysis)
        .OrderBy(item => item.PageNumber)
        .ToArray();

    /// <summary>文書全体の解析結果を受け取り、候補を既定で全選択して表示します。</summary>
    public DocumentImageOptimizationWindow(PdfDocumentImageOptimizationAnalysis analysis)
    {
        InitializeComponent();
        _analysis = analysis;
        Items = new ObservableCollection<DocumentImageOptimizationListItem>(
            analysis.Candidates.Select(candidate => new DocumentImageOptimizationListItem(candidate)));
        foreach (var item in Items) item.PropertyChanged += Item_OnPropertyChanged;
        DataContext = this;

        DocumentSummaryText.Text =
            $"全{analysis.PageCount:N0}ページを解析し、最適化候補を{analysis.Candidates.Count:N0}ページ検出しました。" +
            (analysis.RemovableBlankImages > 0
                ? $" 空白だけの全面画像は{analysis.RemovableBlankImages:N0}個削除できます。"
                : string.Empty);
        UpdateSelectionSummary();
        LocalizationService.Apply(this);
    }

    /// <summary>候補のチェック状態が変化したときに合計見込みを更新します。</summary>
    private void Item_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentImageOptimizationListItem.IsSelected))
            UpdateSelectionSummary();
    }

    /// <summary>すべての候補を選択します。</summary>
    private void SelectAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items) item.IsSelected = true;
    }

    /// <summary>すべての候補の選択を解除します。</summary>
    private void ClearAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items) item.IsSelected = false;
    }

    /// <summary>選択ページを確認画面なしで一括登録します。</summary>
    private void BatchButton_OnClick(object sender, RoutedEventArgs e) => Complete(DocumentImageOptimizationApplyMode.Batch);

    /// <summary>選択ページを1ページずつプレビュー確認する方法を選びます。</summary>
    private void ReviewButton_OnClick(object sender, RoutedEventArgs e) => Complete(DocumentImageOptimizationApplyMode.Review);

    /// <summary>候補が1件以上選択されていれば画面を確定します。</summary>
    private void Complete(DocumentImageOptimizationApplyMode mode)
    {
        if (SelectedAnalyses.Count == 0)
        {
            MessageBox.Show("最適化するページを1件以上選択してください。", "PDF全体の画像最適化",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ApplyMode = mode;
        DialogResult = true;
    }

    /// <summary>選択候補だけを反映した画像容量およびPDF全体容量の概算を表示します。</summary>
    private void UpdateSelectionSummary()
    {
        var selected = Items.Where(item => item.IsSelected).Select(item => item.Analysis).ToArray();
        var savings = selected.Sum(item => Math.Max(0L, item.OriginalEncodedBytes - item.EstimatedEncodedBytes));
        var estimatedPdfBytes = Math.Max(0L, _analysis.SourcePdfBytes - savings);
        var reduction = _analysis.SourcePdfBytes <= 0 ? 0d : savings / (double)_analysis.SourcePdfBytes;
        SelectionSummaryText.Text =
            $"選択 {selected.Length:N0}ページ／画像削減見込み {FormatSize(savings)}／" +
            $"出力PDF概算 {FormatSize(_analysis.SourcePdfBytes)} → {FormatSize(estimatedPdfBytes)}（約{reduction:P1}削減）";
    }

    /// <summary>バイト数を画面向けの読みやすい単位へ変換します。</summary>
    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d * 1024d):N2} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024d * 1024d):N1} MB";
        if (bytes >= 1024L) return $"{bytes / 1024d:N1} KB";
        return $"{bytes:N0} B";
    }
}

/// <summary>PDF全体の画像最適化をまとめて適用するか、順に確認するかを表します。</summary>
public enum DocumentImageOptimizationApplyMode
{
    /// <summary>選択候補を確認画面なしでまとめて登録します。</summary>
    Batch,
    /// <summary>選択候補を1ページずつプレビュー確認して登録します。</summary>
    Review,
}

/// <summary>画像最適化候補一覧の1行を表します。</summary>
public sealed class DocumentImageOptimizationListItem : INotifyPropertyChanged
{
    /// <summary>現在の適用対象状態です。</summary>
    private bool _isSelected = true;

    /// <summary>元となるページ解析結果です。</summary>
    public PdfImageOptimizationAnalysis Analysis { get; }
    /// <summary>ページ番号です。</summary>
    public int PageNumber => Analysis.PageNumber;
    /// <summary>ページで実行する処理の要約です。</summary>
    public string ActionText => Analysis.RemovableBlankImages > 0 ? "空白画像を削除" : "余白・背景を最適化";
    /// <summary>現在の画像圧縮データ量です。</summary>
    public string OriginalSizeText => FormatSize(Analysis.OriginalEncodedBytes);
    /// <summary>処理後に見込まれる画像圧縮データ量です。</summary>
    public string EstimatedSizeText => FormatSize(Analysis.EstimatedEncodedBytes);
    /// <summary>画像圧縮データ量の削減率です。</summary>
    public string ReductionText => Analysis.OriginalEncodedBytes <= 0
        ? "-"
        : $"{1d - Analysis.EstimatedEncodedBytes / (double)Analysis.OriginalEncodedBytes:P0}";
    /// <summary>解析処理の説明です。</summary>
    public string Message => Analysis.Message;

    /// <summary>候補一覧の項目を作成します。</summary>
    public DocumentImageOptimizationListItem(PdfImageOptimizationAnalysis analysis) => Analysis = analysis;

    /// <summary>この候補を最適化対象へ含めるかを取得または設定します。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>バイト数を一覧向けの短い表示へ変換します。</summary>
    private static string FormatSize(long bytes) => bytes >= 1024L * 1024L
        ? $"{bytes / (1024d * 1024d):N1} MB"
        : $"{bytes / 1024d:N1} KB";
}
