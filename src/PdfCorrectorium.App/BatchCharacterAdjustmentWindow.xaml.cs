using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

/// <summary>前処理を選択してから、文字送りの一括自動調整を開始する画面です。</summary>
public partial class BatchCharacterAdjustmentWindow : Window
{
    /// <summary>開いているPDFの総ページ数です。</summary>
    private readonly int _pageCount;

    /// <summary>ダイアログを開いた時点でプレビューしている1始まりのページ番号です。</summary>
    private readonly int _currentPageNumber;

    /// <summary>ページ一覧で選択されている1始まりのページ番号のスナップショットです。</summary>
    private readonly int[] _selectedPageNumbers;

    /// <summary>総ページ数、現在ページ、およびページ一覧の選択状態を使って画面を初期化します。</summary>
    public BatchCharacterAdjustmentWindow(
        int pageCount,
        int currentPageNumber,
        IReadOnlyCollection<int> selectedPageNumbers)
    {
        _pageCount = Math.Max(0, pageCount);
        _currentPageNumber = Math.Clamp(currentPageNumber, 1, Math.Max(1, _pageCount));
        _selectedPageNumbers = selectedPageNumbers
            .Where(pageNumber => pageNumber >= 1 && pageNumber <= _pageCount)
            .Distinct()
            .OrderBy(pageNumber => pageNumber)
            .ToArray();

        InitializeComponent();
        LocalizationService.Apply(this);

        SelectedPagesRadioButton.IsEnabled = _selectedPageNumbers.Length > 0;
        if (_selectedPageNumbers.Length > 1)
            SelectedPagesRadioButton.IsChecked = true;
        else
            CurrentPageRadioButton.IsChecked = true;
        UpdateTargetSummary();
    }

    /// <summary>利用者が選択した前処理オプションです。</summary>
    public BatchCharacterAdjustmentOptions Options { get; private set; } =
        new(true, true, true, true, true);

    /// <summary>利用者が選択した、実際に自動調整する1始まりのページ番号です。</summary>
    public IReadOnlyList<int> TargetPageNumbers { get; private set; } = [];

    private void ExecuteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryResolveTargetPages(out var targetPages, out var errorMessage))
        {
            MessageBox.Show(
                errorMessage,
                LocalizationService.IsEnglish ? "Page Selection" : "ページ指定",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            PageSpecificationTextBox.Focus();
            PageSpecificationTextBox.SelectAll();
            return;
        }

        TargetPageNumbers = targetPages;
        Options = new BatchCharacterAdjustmentOptions(
            ExpandLeadingCheckBox.IsChecked == true,
            ExpandTrailingCheckBox.IsChecked == true,
            NormalizeHeightCheckBox.IsChecked == true,
            ExpandNarrowEdgeCharactersCheckBox.IsChecked == true,
            AddLineEdgeSafetyMarginCheckBox.IsChecked == true);
        DialogResult = true;
    }

    /// <summary>対象範囲のラジオボタンが変わったとき、ページ指定欄と説明を更新します。</summary>
    private void TargetMode_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && PageSpecificationTextBox is null) return;
        PageSpecificationTextBox.IsEnabled = SpecifiedPagesRadioButton.IsChecked == true;
        UpdateTargetSummary();
    }

    /// <summary>ページ指定の入力中に、現在解釈できる対象ページを説明欄へ反映します。</summary>
    private void PageSpecificationTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (SpecifiedPagesRadioButton?.IsChecked == true)
            UpdateTargetSummary();
    }

    /// <summary>選択中の対象範囲を、重複のないページ番号配列へ変換します。</summary>
    private bool TryResolveTargetPages(out IReadOnlyList<int> pageNumbers, out string errorMessage)
    {
        if (CurrentPageRadioButton.IsChecked == true)
        {
            pageNumbers = [_currentPageNumber];
            errorMessage = string.Empty;
            return true;
        }

        if (SelectedPagesRadioButton.IsChecked == true)
        {
            pageNumbers = _selectedPageNumbers;
            errorMessage = string.Empty;
            return pageNumbers.Count > 0;
        }

        if (AllPagesRadioButton.IsChecked == true)
        {
            pageNumbers = Enumerable.Range(1, _pageCount).ToArray();
            errorMessage = string.Empty;
            return pageNumbers.Count > 0;
        }

        if (PageRangeParser.TryParse(PageSpecificationTextBox.Text, _pageCount, out var specifiedPages))
        {
            pageNumbers = specifiedPages;
            errorMessage = string.Empty;
            return true;
        }

        pageNumbers = [];
        errorMessage = LocalizationService.IsEnglish
            ? $"Enter valid page numbers from 1 to {_pageCount}, for example: 1,3,5-10."
            : $"1～{_pageCount} の範囲でページ番号を入力してください。例: 1,3,5-10";
        return false;
    }

    /// <summary>現在選択中の対象ページと件数を、利用者が確認できる短い説明へ整形します。</summary>
    private void UpdateTargetSummary()
    {
        if (TargetSummaryTextBlock is null) return;

        if (!TryResolveTargetPages(out var pages, out _))
        {
            TargetSummaryTextBlock.Text = LocalizationService.IsEnglish
                ? "Enter the pages to process, for example: 1,3,5-10."
                : "処理するページを入力してください。例: 1,3,5-10";
            ExecuteButton.IsEnabled = false;
            return;
        }

        ExecuteButton.IsEnabled = pages.Count > 0;
        var rangeText = PageRangeParser.Format(pages);
        TargetSummaryTextBlock.Text = LocalizationService.IsEnglish
            ? $"{pages.Count} page(s) will be processed: {rangeText}"
            : $"{pages.Count} ページを処理します: {rangeText}";
    }
}
