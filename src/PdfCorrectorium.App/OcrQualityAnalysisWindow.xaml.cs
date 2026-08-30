using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;

namespace PdfCorrectorium.App;

/// <summary>文書全体のOCR文字数と、同一キーワードの文字幅比率を分析する画面です。</summary>
public partial class OcrQualityAnalysisWindow : Window
{
    /// <summary>分析対象文書と候補への移動・補正操作を提供します。</summary>
    private readonly MainWindowViewModel _viewModel;
    /// <summary>文字数の外れ値候補を画面表示用に保持します。</summary>
    private readonly ObservableCollection<CharacterCountRow> _countRows = [];
    /// <summary>キーワード幅の外れ値候補を画面表示用に保持します。</summary>
    private readonly ObservableCollection<KeywordWidthRow> _keywordRows = [];
    /// <summary>ページ読込中の二重実行を防ぎます。</summary>
    private bool _isBusy;
    /// <summary>文書全体の分析を画面から中断するための通知元です。</summary>
    private CancellationTokenSource? _operationCancellation;
    /// <summary>完了済みの分析から遅れて届く進捗通知を無視するための識別番号です。</summary>
    private long _analysisOperationId;

    /// <summary>編集画面の状態を受け取り、品質分析画面を初期化します。</summary>
    public OcrQualityAnalysisWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        CountResultsGrid.ItemsSource = _countRows;
        KeywordResultsGrid.ItemsSource = _keywordRows;
        Closed += (_, _) =>
        {
            _analysisOperationId++;
            _operationCancellation?.Cancel();
        };
        LocalizationService.Apply(this);
    }

    private async void AnalyzeCountButton_OnClick(object sender, RoutedEventArgs e) => await AnalyzeCharacterCountsAsync();

    private async Task AnalyzeCharacterCountsAsync()
    {
        if (_isBusy) return;
        if (!TryReadDouble(SizeToleranceTextBox.Text, out var tolerance) ||
            !int.TryParse(MinimumPeerCountTextBox.Text, out var minimumPeers) ||
            !TryReadDouble(CountRatioTextBox.Text, out var ratio))
        {
            CountStatusTextBlock.Text = LocalizationService.Translate("分析条件を数値で入力してください。");
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        var operationId = ++_analysisOperationId;
        SetBusy(true, CountProgressBar);
        try
        {
            var progress = new Progress<string>(message =>
            {
                if (IsCurrentAnalysis(operationId)) CountStatusTextBlock.Text = message;
            });
            var detailedProgress = CreateProgressReporter(
                CountProgressBar,
                CountStatusTextBlock,
                () => IsCurrentAnalysis(operationId));
            var results = await _viewModel.AnalyzeOcrCharacterCountAnomaliesAsync(
                new OcrCharacterCountAnalysisOptions(tolerance, minimumPeers, ratio),
                progress, detailedProgress, _operationCancellation.Token);
            _countRows.Clear();
            foreach (var result in results) _countRows.Add(new CharacterCountRow(result));
            CountStatusTextBlock.Text = results.Count == 0
                ? LocalizationService.Translate("文字数の外れ値候補は見つかりませんでした。")
                : string.Format(CultureInfo.CurrentCulture, LocalizationService.Translate("{0}件の候補が見つかりました。"), results.Count);
        }
        catch (OperationCanceledException)
        {
            CountStatusTextBlock.Text = LocalizationService.Translate("分析をキャンセルしました。");
        }
        catch (Exception ex)
        {
            ShowError(LocalizationService.Translate("OCR文字数を分析できませんでした。"), ex);
        }
        finally { CompleteOperation(operationId); }
    }

    private async void AnalyzeKeywordButton_OnClick(object sender, RoutedEventArgs e) => await AnalyzeKeywordAsync();

    private async Task AnalyzeKeywordAsync()
    {
        if (_isBusy) return;
        if (string.IsNullOrWhiteSpace(KeywordTextBox.Text))
        {
            KeywordStatusTextBlock.Text = LocalizationService.Translate("キーワードを入力してください。");
            KeywordTextBox.Focus();
            return;
        }
        if (!TryReadDouble(KeywordToleranceTextBox.Text, out var tolerance) ||
            !int.TryParse(MinimumReferenceCountTextBox.Text, out var minimumReferences))
        {
            KeywordStatusTextBlock.Text = LocalizationService.Translate("分析条件を数値で入力してください。");
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        var operationId = ++_analysisOperationId;
        SetBusy(true, KeywordProgressBar);
        try
        {
            var progress = new Progress<string>(message =>
            {
                if (IsCurrentAnalysis(operationId)) KeywordStatusTextBlock.Text = message;
            });
            var detailedProgress = CreateProgressReporter(
                KeywordProgressBar,
                KeywordStatusTextBlock,
                () => IsCurrentAnalysis(operationId));
            var result = await _viewModel.AnalyzeOcrKeywordWidthsAsync(
                new OcrKeywordWidthAnalysisOptions(
                    KeywordTextBox.Text.Trim(),
                    MatchCaseCheckBox.IsChecked == true,
                    tolerance,
                    minimumReferences), progress, detailedProgress, _operationCancellation.Token);
            _keywordRows.Clear();
            foreach (var candidate in result.Candidates) _keywordRows.Add(new KeywordWidthRow(candidate));
            KeywordStatusTextBlock.Text = result.OccurrenceCount < minimumReferences
                ? string.Format(CultureInfo.CurrentCulture, LocalizationService.Translate("出現件数が{0}件のため、基準を決定できません。"), result.OccurrenceCount)
                : string.Format(CultureInfo.CurrentCulture,
                    LocalizationService.Translate("全{0}件から基準比率{1:0.00}を求め、{2}件を候補にしました。"),
                    result.OccurrenceCount, result.ReferenceRatio, result.Candidates.Count);
        }
        catch (OperationCanceledException)
        {
            KeywordStatusTextBlock.Text = LocalizationService.Translate("分析をキャンセルしました。");
        }
        catch (Exception ex)
        {
            ShowError(LocalizationService.Translate("キーワードの文字幅を分析できませんでした。"), ex);
        }
        finally { CompleteOperation(operationId); }
    }

    private async void CountResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => await NavigateCountAsync();
    private async void NavigateCountButton_OnClick(object sender, RoutedEventArgs e) => await NavigateCountAsync();

    private async Task NavigateCountAsync()
    {
        if (CountResultsGrid.SelectedItem is not CharacterCountRow row) return;
        await _viewModel.NavigateToOcrQualityCandidateAsync(row.Source.PageNumber, row.Source.RegionId);
        Owner?.Activate();
    }

    private async void KeywordResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => await NavigateKeywordAsync();
    private async void NavigateKeywordButton_OnClick(object sender, RoutedEventArgs e) => await NavigateKeywordAsync();

    private async Task NavigateKeywordAsync()
    {
        if (KeywordResultsGrid.SelectedItem is not KeywordWidthRow row) return;
        await _viewModel.NavigateToOcrQualityCandidateAsync(
            row.Source.PageNumber, row.Source.RegionId, row.Source.StartIndex, row.Source.Length);
        Owner?.Activate();
    }

    private async void CorrectSelectedKeywordButton_OnClick(object sender, RoutedEventArgs e)
    {
        var candidates = KeywordResultsGrid.SelectedItems.Cast<KeywordWidthRow>()
            .Select(row => row.Source).Where(candidate => !candidate.IsLocked).ToArray();
        await CorrectCandidatesAsync(candidates);
    }

    private async void CorrectAllKeywordButton_OnClick(object sender, RoutedEventArgs e) =>
        await CorrectCandidatesAsync(_keywordRows.Select(row => row.Source).Where(candidate => !candidate.IsLocked).ToArray());

    private async Task CorrectCandidatesAsync(IReadOnlyList<OcrKeywordWidthCandidate> candidates)
    {
        if (!_viewModel.CanEditGeometry) return;
        if (_isBusy || candidates.Count == 0)
        {
            KeywordStatusTextBlock.Text = LocalizationService.Translate("補正できる候補が選択されていません。");
            return;
        }
        var message = string.Format(CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0}件を同じキーワードの基準幅へ補正します。よろしいですか？"), candidates.Count);
        if (MessageBox.Show(this, message, "PDF Correctorium", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var changed = _viewModel.ApplyKeywordWidthCorrections(candidates);
        KeywordStatusTextBlock.Text = string.Format(CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0}件を補正しました。"), changed);
        if (changed > 0) await AnalyzeKeywordAsync();
    }

    private void SetBusy(bool value, System.Windows.Controls.ProgressBar activeProgressBar)
    {
        _isBusy = value;
        AnalyzeCountButton.IsEnabled = !value;
        AnalyzeKeywordButton.IsEnabled = !value;
        CountProgressBar.Visibility = value && ReferenceEquals(activeProgressBar, CountProgressBar)
            ? Visibility.Visible : Visibility.Collapsed;
        KeywordProgressBar.Visibility = value && ReferenceEquals(activeProgressBar, KeywordProgressBar)
            ? Visibility.Visible : Visibility.Collapsed;
        activeProgressBar.Value = 0;
        CancelAnalysisButton.IsEnabled = value;
        CancelAnalysisButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Mouse.OverrideCursor = value ? Cursors.Wait : null;
    }

    private static Progress<OperationProgressUpdate> CreateProgressReporter(
        System.Windows.Controls.ProgressBar progressBar,
        System.Windows.Controls.TextBlock statusTextBlock,
        Func<bool> isCurrentOperation) =>
        new(update =>
        {
            if (!isCurrentOperation()) return;
            progressBar.Value = update.Percentage;
            statusTextBlock.Text = LocalizationService.IsEnglish
                ? $"Loading page {update.Current:N0} of {update.Total:N0} for analysis..."
                : update.Message;
        });

    private void CompleteOperation(long operationId)
    {
        if (_analysisOperationId == operationId) _analysisOperationId++;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _isBusy = false;
        AnalyzeCountButton.IsEnabled = true;
        AnalyzeKeywordButton.IsEnabled = true;
        CountProgressBar.Visibility = Visibility.Collapsed;
        KeywordProgressBar.Visibility = Visibility.Collapsed;
        CancelAnalysisButton.Visibility = Visibility.Collapsed;
        Mouse.OverrideCursor = null;
    }

    /// <summary>指定した識別番号が現在実行中の分析を表すか判定します。</summary>
    private bool IsCurrentAnalysis(long operationId) =>
        _isBusy && _analysisOperationId == operationId;

    private void CancelAnalysisButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelAnalysisButton.IsEnabled = false;
        _operationCancellation?.Cancel();
    }

    private static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void ShowError(string message, Exception exception) =>
        MessageBox.Show(this, $"{message}\n\n{exception.Message}", "PDF Correctorium", MessageBoxButton.OK, MessageBoxImage.Error);

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>文字数外れ値の表示項目を提供します。</summary>
    private sealed class CharacterCountRow(OcrCharacterCountAnomaly source)
    {
        public OcrCharacterCountAnomaly Source { get; } = source;
        public int PageNumber => Source.PageNumber;
        public string KindDisplay => LocalizationService.Translate(Source.Kind == OcrCharacterCountAnomalyKind.TooFew ? "少なすぎる" : "多すぎる");
        public int CharacterCount => Source.CharacterCount;
        public double ExpectedCharacterCount => Source.ExpectedCharacterCount;
        public int PeerCount => Source.PeerCount;
        public double Width => Source.Width;
        public double Height => Source.Height;
        public string DirectionDisplay => LocalizationService.Translate(Source.IsVertical ? "縦書き" : "横書き");
        public string Text => Source.Text;
    }

    /// <summary>キーワード幅外れ値の表示項目を提供します。</summary>
    private sealed class KeywordWidthRow(OcrKeywordWidthCandidate source)
    {
        public OcrKeywordWidthCandidate Source { get; } = source;
        public int PageNumber => Source.PageNumber;
        public double CurrentSpan => Source.CurrentSpan;
        public double ReferenceSpan => Source.ReferenceSpan;
        public double DeviationPercent => Source.DeviationPercent;
        public string DirectionDisplay => LocalizationService.Translate(Source.Sample.IsVertical ? "縦書き" : "横書き");
        public string LockDisplay => LocalizationService.Translate(Source.IsLocked ? "固定済み" : "なし");
        public string Text => Source.Text;
    }
}
