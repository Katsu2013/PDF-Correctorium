using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

/// <summary>
/// PDF内の透明テキストを文書全体または現在ページから検索し、個別置換と一括置換を行う画面です。
/// </summary>
public partial class OcrSearchReplaceWindow : Window
{
    /// <summary>検索と置換を実行するメイン画面の編集状態です。</summary>
    private readonly MainWindowViewModel _viewModel;
    /// <summary>検索結果一覧へ表示している一致箇所です。</summary>
    private readonly ObservableCollection<OcrTextSearchMatch> _matches = [];
    /// <summary>検索・ページ読み込み中の二重実行を防ぎます。</summary>
    private bool _isBusy;
    /// <summary>実行中の文書検索を利用者操作で中断するための通知元です。</summary>
    private CancellationTokenSource? _operationCancellation;
    /// <summary>
    /// 検索ごとに更新する識別番号です。完了済み検索から遅れて届いた進捗通知が、
    /// 最終結果や次の検索の表示を上書きすることを防ぎます。
    /// </summary>
    private long _searchOperationId;

    /// <summary>検索対象となる編集状態を受け取り、検索画面を初期化します。</summary>
    public OcrSearchReplaceWindow(MainWindowViewModel viewModel, bool focusReplacement = false)
    {
        InitializeComponent();
        _viewModel = viewModel;
        ResultsGrid.ItemsSource = _matches;
        Loaded += (_, _) =>
        {
            if (focusReplacement) ReplacementTextBox.Focus();
            else SearchTextBox.Focus();
        };
        Closed += (_, _) =>
        {
            _searchOperationId++;
            _operationCancellation?.Cancel();
            _viewModel.ClearOcrSearchHighlight();
        };
        LocalizationService.Apply(this);
    }

    /// <summary>既に開いている画面を再利用するとき、検索欄または置換欄へ入力位置を移します。</summary>
    public void ActivateSearch(bool focusReplacement)
    {
        Activate();
        if (focusReplacement) ReplacementTextBox.Focus();
        else SearchTextBox.Focus();
        if (!focusReplacement) SearchTextBox.SelectAll();
    }

    private async void SearchButton_OnClick(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void SearchTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        if (_isBusy) return;
        if (string.IsNullOrEmpty(SearchTextBox.Text))
        {
            StatusTextBlock.Text = "検索する文字列を入力してください。";
            SearchTextBox.Focus();
            return;
        }

        var navigateToFirst = false;
        var completionMessage = string.Empty;
        var operationId = ++_searchOperationId;
        _operationCancellation = new CancellationTokenSource();
        SetBusy(true, showProgress: true);
        try
        {
            var progress = new Progress<string>(message =>
            {
                if (IsCurrentSearch(operationId)) StatusTextBlock.Text = message;
            });
            var detailedProgress = new Progress<OperationProgressUpdate>(update =>
            {
                if (!IsCurrentSearch(operationId)) return;
                SearchProgressTextBlock.Text = LocalizationService.IsEnglish
                    ? $"Searching page {update.Current:N0} of {update.Total:N0}..."
                    : update.Message;
                SearchProgressBar.Value = update.Percentage;
            });
            var results = await _viewModel.SearchOcrTextAsync(
                CreateOptions(), progress, detailedProgress, _operationCancellation.Token);
            _matches.Clear();
            foreach (var result in results) _matches.Add(result);
            completionMessage = results.Count == 0 ? "一致する透明テキストはありません。" : $"{results.Count}件見つかりました。";
            if (results.Count == 0) _viewModel.ClearOcrSearchHighlight();
            if (_matches.Count > 0)
            {
                ResultsGrid.SelectedIndex = 0;
                navigateToFirst = true;
            }
        }
        catch (OperationCanceledException)
        {
            completionMessage = "検索をキャンセルしました。";
        }
        catch (Exception ex)
        {
            completionMessage = "透明テキストを検索できませんでした。";
            MessageBox.Show(this, $"透明テキストを検索できませんでした。\n\n{ex.Message}", "PDF Correctorium", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // Progress<T> はUIキュー経由で通知するため、完了直前の通知が後から届く場合があります。
            // 先に識別番号を無効化し、最終メッセージが進捗文言で上書きされないようにします。
            if (_searchOperationId == operationId) _searchOperationId++;
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false, showProgress: false);
        }
        StatusTextBlock.Text = completionMessage;
        if (navigateToFirst) await NavigateSelectedAsync();
    }

    /// <summary>指定した識別番号が現在実行中の検索を表すか判定します。</summary>
    private bool IsCurrentSearch(long operationId) =>
        _isBusy && _searchOperationId == operationId;

    private OcrTextSearchOptions CreateOptions() => new(
        SearchTextBox.Text,
        MatchCaseCheckBox.IsChecked == true,
        CurrentPageOnlyCheckBox.IsChecked == true,
        InvisibleOnlyCheckBox.IsChecked == true,
        WholeRegionMatchCheckBox.IsChecked == true,
        UseRegularExpressionCheckBox.IsChecked == true);

    private async void ResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => await NavigateSelectedAsync();

    private async void ResultsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isBusy && ResultsGrid.SelectedItem is OcrTextSearchMatch) await NavigateSelectedAsync();
    }

    private async void PreviousButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_matches.Count == 0) return;
        ResultsGrid.SelectedIndex = ResultsGrid.SelectedIndex <= 0 ? _matches.Count - 1 : ResultsGrid.SelectedIndex - 1;
        ResultsGrid.ScrollIntoView(ResultsGrid.SelectedItem);
        await NavigateSelectedAsync();
    }

    private async void NextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_matches.Count == 0) return;
        ResultsGrid.SelectedIndex = ResultsGrid.SelectedIndex >= _matches.Count - 1 ? 0 : ResultsGrid.SelectedIndex + 1;
        ResultsGrid.ScrollIntoView(ResultsGrid.SelectedItem);
        await NavigateSelectedAsync();
    }

    private async Task NavigateSelectedAsync()
    {
        if (_isBusy || ResultsGrid.SelectedItem is not OcrTextSearchMatch match) return;
        SetBusy(true, showProgress: false);
        try { await _viewModel.NavigateToOcrSearchMatchAsync(match); }
        finally { SetBusy(false, showProgress: false); }
    }

    private async void ReplaceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not OcrTextSearchMatch match) return;
        var index = ResultsGrid.SelectedIndex;
        try
        {
            if (!_viewModel.ReplaceOcrSearchMatch(match, CreateOptions(), ReplacementTextBox.Text))
            {
                StatusTextBlock.Text = "検索結果が更新されています。もう一度検索してください。";
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"透明テキストを置換できませんでした。\n\n{ex.Message}", "PDF Correctorium", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        await RunSearchAsync();
        if (_matches.Count > 0) ResultsGrid.SelectedIndex = Math.Min(index, _matches.Count - 1);
    }

    private async void ReplaceAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_matches.Count == 0)
        {
            await RunSearchAsync();
            if (_matches.Count == 0) return;
        }
        var answer = MessageBox.Show(
            this,
            $"{_matches.Count}件の透明テキストを置換します。\nこの操作は［元に戻す］で一括して戻せます。\n\n実行しますか？",
            "透明テキストをすべて置換",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        int count;
        try
        {
            count = _viewModel.ReplaceAllOcrSearchMatches(
                _matches.ToArray(),
                CreateOptions(),
                ReplacementTextBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"透明テキストを一括置換できませんでした。\n\n{ex.Message}", "PDF Correctorium", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        StatusTextBlock.Text = count == 0 ? "置換対象はありませんでした。" : $"{count}件を置換しました。";
        await RunSearchAsync();
    }

    private void SetBusy(bool busy, bool showProgress)
    {
        _isBusy = busy;
        SearchButton.IsEnabled = !busy;
        ReplaceAllButton.IsEnabled = !busy;
        SearchProgressPanel.Visibility = busy && showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (busy && showProgress)
        {
            CancelSearchButton.IsEnabled = true;
            SearchProgressBar.Value = 0;
            SearchProgressTextBlock.Text = LocalizationService.Translate("検索を準備しています...");
        }
        Cursor = busy ? Cursors.Wait : null;
    }

    private void CancelSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelSearchButton.IsEnabled = false;
        SearchProgressTextBlock.Text = LocalizationService.IsEnglish
            ? "Canceling search..."
            : "検索をキャンセルしています...";
        _operationCancellation?.Cancel();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
