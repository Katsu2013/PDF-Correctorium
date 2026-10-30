using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;

namespace PdfCorrectorium.App;

public partial class OcrQualityAnalysisWindow
{
    private readonly ObservableCollection<SimilarWordRow> _wordRows = [];
    private readonly HashSet<string> _ignoredWords = new(StringComparer.Ordinal);
    private OcrSimilarWordDocumentResult? _wordResult;
    internal Func<int, bool>? ConfirmWordCorrectionsOverride { get; set; }

    private void InitializeSimilarWords()
    {
        RestoreLexiconEditors();
        if (_viewModel.CachedSimilarWordResult is { } cached)
        {
            _wordResult = cached;
            foreach (var candidate in cached.Analysis.Candidates) _wordRows.Add(new(candidate));
            WordsStatusTextBlock.Text = LocalizationService.Translate("前回の候補を保持しています。再分析で最新の頻度を確認できます。");
        }
        WordResultsGrid.ItemsSource = _wordRows;
        WordResultsGrid.SelectionChanged += (_, _) => RefreshWordActions();
        _viewModel.PropertyChanged += OnWordDocumentPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnWordDocumentPropertyChanged;
        RefreshWordActions();
    }

    private void RestoreLexiconEditors()
    {
        KnownWordsTextBox.Text = string.Join(Environment.NewLine, _viewModel.CurrentOcrLexicon.KnownTerms);
        _ignoredWords.Clear(); _ignoredWords.UnionWith(_viewModel.CurrentOcrLexicon.IgnoredTerms);
    }
    private void Dictionary_OnClick(object sender, RoutedEventArgs e)
    { new OcrLexiconWindow(_viewModel) { Owner = this }.ShowDialog(); RestoreLexiconEditors(); RefreshWordActions(); }

    private void OnWordDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_isBusy && e.PropertyName == nameof(MainWindowViewModel.CurrentOcrLexicon)) RestoreLexiconEditors();
        RefreshWordActions();
    }

    private void SetSimilarWordsBusy(bool busy)
    {
        AnalyzeWordsButton.IsEnabled = !busy;
        KnownWordsTextBox.IsEnabled = !busy;
        WordReferenceCountTextBox.IsEnabled = !busy;
        WordFrequencyRatioTextBox.IsEnabled = !busy;
        WordSuspectCountTextBox.IsEnabled = !busy;
        if (!busy) WordsProgressBar.Visibility = Visibility.Collapsed;
        RefreshWordActions();
    }

    private void RefreshWordActions()
    {
        var hasSelection = !_isBusy && WordResultsGrid.SelectedItems.Count > 0;
        var current = _wordResult is not null && _viewModel.IsSimilarWordResultCurrent(_wordResult);
        if (_wordResult is not null && !current && !_isBusy)
            WordsStatusTextBlock.Text = LocalizationService.Translate("文書が変更されました。再解析してください。");
        NavigateWordButton.IsEnabled = hasSelection && current;
        IgnoreWordButton.IsEnabled = hasSelection && _viewModel.CanManageOcrLexicon;
        ResetIgnoredWordsButton.IsEnabled = !_isBusy && _ignoredWords.Count > 0 && _viewModel.CanManageOcrLexicon;
        CorrectWordsButton.IsEnabled = hasSelection && current && _viewModel.CanCorrectSimilarWords &&
            WordResultsGrid.SelectedItems.Cast<SimilarWordRow>().Any(r => !r.Source.Sample.IsLocked);
    }

    private async void AnalyzeWordsButton_OnClick(object sender, RoutedEventArgs e) => await AnalyzeWordsAsync();

    private async Task AnalyzeWordsAsync()
    {
        if (_isBusy) return;
        if (!int.TryParse(WordReferenceCountTextBox.Text, out var references) || references is < 2 or > 1000 ||
            !int.TryParse(WordFrequencyRatioTextBox.Text, out var ratio) || ratio is < 2 or > 1000 ||
            !int.TryParse(WordSuspectCountTextBox.Text, out var suspects) || suspects is < 1 or > 100)
        {
            WordsStatusTextBlock.Text = LocalizationService.Translate("基準件数・頻度比は2～1000、疑わしい語の最大件数は1～100で入力してください。");
            return;
        }
        var known = KnownWordsTextBox.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (known.Length > OcrSimilarWordAnalyzer.MaximumTerms)
        {
            WordsStatusTextBlock.Text = LocalizationService.Translate("登録語は2,000語以内にしてください。");
            return;
        }
        try
        {
            var dictionary = _viewModel.CurrentOcrLexicon with { KnownTerms = known.Distinct(StringComparer.Ordinal).ToArray() };
            if (!_viewModel.SetOcrLexicon(dictionary)) return;
        }
        catch (System.IO.InvalidDataException error) { WordsStatusTextBlock.Text = LocalizationService.Translate(error.Message); return; }
        _wordRows.Clear();
        _wordResult = null;
        _operationCancellation = new CancellationTokenSource();
        var operationId = ++_analysisOperationId;
        SetBusy(true, WordsProgressBar);
        WordsProgressBar.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<OperationProgressUpdate>(update =>
            {
                if (!IsCurrentAnalysis(operationId)) return;
                WordsProgressBar.Value = update.Percentage;
                WordsStatusTextBlock.Text = update.Message;
            });
            var result = await _viewModel.AnalyzeOcrSimilarWordsAsync(new(references, ratio, suspects, known, _ignoredWords.ToArray()),
                progress, _operationCancellation.Token, forceRebuild: true);
            if (!IsCurrentAnalysis(operationId)) return;
            _wordResult = result;
            foreach (var candidate in result.Analysis.Candidates) _wordRows.Add(new(candidate));
            WordsStatusTextBlock.Text = LocalizationService.IsEnglish
                ? $"{result.Analysis.TokenCount:N0} tokens / {result.Analysis.DistinctWordCount:N0} terms: {_wordRows.Count:N0} candidates. Ignored terms: {_ignoredWords.Count}. Ctrl/Shift selects multiple rows."
                : $"{result.Analysis.TokenCount:N0}語（異なり語{result.Analysis.DistinctWordCount:N0}種類）から{_wordRows.Count:N0}件の候補。無視{_ignoredWords.Count}語。Ctrl／Shiftで複数行を選択できます。";
        }
        catch (OperationCanceledException)
        {
            WordsStatusTextBlock.Text = LocalizationService.Translate("分析を中止しました。文書変更時は再解析してください。");
        }
        catch (Exception ex)
        {
            WordsStatusTextBlock.Text = LocalizationService.Translate("類似語を分析できませんでした。") + " " + LocalizationService.Translate(ex.Message);
        }
        finally { CompleteOperation(operationId); }
    }

    private async void WordResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => await NavigateWordAsync();
    private async void NavigateWordButton_OnClick(object sender, RoutedEventArgs e) => await NavigateWordAsync();

    private async Task NavigateWordAsync()
    {
        if (_isBusy || _wordResult is null || !_viewModel.IsSimilarWordResultCurrent(_wordResult) ||
            WordResultsGrid.SelectedItem is not SimilarWordRow row) return;
        try
        {
            await _viewModel.NavigateToOcrQualityCandidateAsync(row.PageNumber, row.Source.RegionId, row.Source.StartIndex, row.Source.Length);
            Owner?.Activate();
        }
        catch (Exception ex) { ShowError(LocalizationService.Translate("選択箇所へ移動できませんでした。"), ex); }
    }

    private void IgnoreWordButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !_viewModel.CanManageOcrLexicon) return;
        var words = WordResultsGrid.SelectedItems.Cast<SimilarWordRow>().Select(r => r.Word).ToHashSet(StringComparer.Ordinal);
        if (_ignoredWords.Union(words).Count() > OcrSimilarWordAnalyzer.MaximumTerms)
        {
            WordsStatusTextBlock.Text = LocalizationService.Translate("無視する語は2,000語以内にしてください。");
            return;
        }
        _ignoredWords.UnionWith(words);
        _viewModel.SetOcrLexicon(_viewModel.CurrentOcrLexicon with { IgnoredTerms = _ignoredWords.Order(StringComparer.Ordinal).ToArray() });
        foreach (var row in _wordRows.Where(r => words.Contains(r.Word)).ToArray()) _wordRows.Remove(row);
        WordsStatusTextBlock.Text = LocalizationService.IsEnglish ? $"Ignored {_ignoredWords.Count} terms in this project."
            : $"{_ignoredWords.Count}語をこの資料の無視語に登録しました。OCR文字列は変更していません。";
        if (_wordResult is not null) _wordResult = _wordResult with { TextGeneration = _viewModel.SimilarWordGeneration };
        RefreshWordActions();
    }

    private void ResetIgnoredWordsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !_viewModel.CanManageOcrLexicon) return;
        _ignoredWords.Clear();
        _viewModel.SetOcrLexicon(_viewModel.CurrentOcrLexicon with { IgnoredTerms = [] });
        WordsStatusTextBlock.Text = LocalizationService.Translate("無視を解除しました。再解析してください。");
        RefreshWordActions();
    }

    private void CorrectWordsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _wordResult is null) return;
        if (!_viewModel.IsSimilarWordResultCurrent(_wordResult))
        {
            WordsStatusTextBlock.Text = LocalizationService.Translate("文書が変更されました。再解析してください。");
            return;
        }
        var selected = WordResultsGrid.SelectedItems.Cast<SimilarWordRow>()
            .Select(r => r.Source).Where(c => !c.Sample.IsLocked).ToArray();
        if (!_viewModel.CanCorrectSimilarWords || selected.Length == 0) return;
        var message = string.Format(CultureInfo.CurrentCulture,
            LocalizationService.Translate("選択した{0}件のOCR文字列を候補語へ修正します。原画像は変更しません。誤検出でないことを確認しましたか？"), selected.Length);
        if (!(ConfirmWordCorrectionsOverride?.Invoke(selected.Length) ??
            (MessageBox.Show(this, message, "PDF Correctorium", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes))) return;
        var nextIndex = Math.Max(0, WordResultsGrid.SelectedIndex);
        OcrSimilarWordCorrectionResult correction;
        try { correction = _viewModel.ApplySimilarWordCorrectionsAndRefresh(_wordResult, selected, RegisterCorrectedWord.IsChecked == true, RegisterCorrectionRule.IsChecked == true); }
        catch (System.IO.InvalidDataException error) { WordsStatusTextBlock.Text = LocalizationService.Translate(error.Message); return; }
        _wordResult = correction.Remaining;
        _wordRows.Clear();
        foreach (var candidate in _wordResult.Analysis.Candidates.Where(candidate => !_ignoredWords.Contains(candidate.Word)))
            _wordRows.Add(new(candidate));
        if (_wordRows.Count > 0)
        {
            WordResultsGrid.SelectedIndex = Math.Min(nextIndex, _wordRows.Count - 1);
            WordResultsGrid.ScrollIntoView(WordResultsGrid.SelectedItem);
        }
        WordsStatusTextBlock.Text = LocalizationService.IsEnglish
            ? $"Corrected {correction.ChangedCount} occurrences; {_wordRows.Count} remain. Frequency counts are from the analysis. Layout edits can continue; text, lock or page changes require re-analysis."
            : $"{correction.ChangedCount}件修正、残り{_wordRows.Count}件。頻度は分析時の値です。配置を調整しても続けられます。文字・固定・ページの変更後は再分析が必要です。";
        RefreshWordActions();
    }

    private sealed class SimilarWordRow(OcrSimilarWordCandidate source)
    {
        public OcrSimilarWordCandidate Source { get; } = source;
        public int PageNumber => Source.PageNumber;
        public string Word => Source.Word;
        public string SuggestedWord => Source.SuggestedWord;
        // Explicit phrase/rule matches carry no token-frequency measurement. Zero would be fabricated evidence.
        private bool HasMeasuredCounts => !(Source.IsDictionaryReference && Source.Distance == 0 && Source.WordCount == 0 && Source.ReferenceCount == 0);
        public int? WordCount => HasMeasuredCounts ? Source.WordCount : null;
        public int? ReferenceCount => HasMeasuredCounts ? Source.ReferenceCount : null;
        public string Attention => string.Join(" / ", new[]
        {
            Source.Sample.IsLocked ? LocalizationService.Translate("固定済み・修正不可") : "",
            Source.HasAlternative ? LocalizationService.Translate("ほかの候補あり") : "",
        }.Where(value => value.Length > 0));
        public string Reason => string.Join(" / ", new[]
        {
            LocalizationService.Translate(Source.IsDictionaryReference ? "登録語に類似" : "頻出語に類似"),
            Source.UsesOcrConfusion ? LocalizationService.Translate("OCR混同") : "",
            Source.HasAlternative ? LocalizationService.Translate("ほかの候補あり") : "",
            Source.Sample.IsLocked ? LocalizationService.Translate("固定済み・修正不可") : "",
        }.Where(s => s.Length > 0));
        public string Context
        {
            get
            {
                var start = Math.Max(0, Source.StartIndex - 28);
                var end = Math.Min(Source.Sample.Text.Length, Source.StartIndex + Source.Length + 28);
                return (start > 0 ? "…" : "") + Source.Sample.Text[start..end].Replace('\n', ' ').Replace('\r', ' ') +
                    (end < Source.Sample.Text.Length ? "…" : "");
            }
        }
    }
}
