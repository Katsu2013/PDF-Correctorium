using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

/// <summary>定型領域の候補検索範囲と反映方法を選択する画面です。</summary>
public partial class RepeatedRegionPropagationOptionsWindow : Window
{
    private readonly int _pageCount;
    private readonly int _referencePageNumber;
    private readonly int[] _selectedPages;

    public RepeatedRegionPropagationOptionsWindow(
        int pageCount,
        int referencePageNumber,
        IReadOnlyCollection<int> selectedPages)
    {
        _pageCount = pageCount;
        _referencePageNumber = referencePageNumber;
        _selectedPages = selectedPages.Where(page => page >= 1 && page <= pageCount && page != referencePageNumber)
            .Distinct().OrderBy(page => page).ToArray();
        InitializeComponent();
        LocalizationService.Apply(this);
        SelectedPagesRadioButton.IsEnabled = _selectedPages.Length > 0;
    }

    /// <summary>確定した候補検索条件です。</summary>
    public RepeatedRegionPropagationOptions? Options { get; private set; }

    private void SearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetPages(out var pages)) return;
        Options = new RepeatedRegionPropagationOptions(
            pages,
            SimilaritySlider.Value,
            PreserveTargetTextCheckBox.IsChecked == true,
            DeleteMatchesRadioButton.IsChecked == true
                ? RepeatedRegionPropagationMode.DeleteMatches
                : RepeatedRegionPropagationMode.ReplaceStructure);
        DialogResult = true;
    }

    private bool TryGetPages(out IReadOnlyList<int> pages)
    {
        if (SelectedPagesRadioButton.IsChecked == true) pages = _selectedPages;
        else if (SpecifiedPagesRadioButton.IsChecked == true)
        {
            if (!PageRangeParser.TryParse(PageSpecificationTextBox.Text, _pageCount, out pages))
            {
                MessageBox.Show(
                    LocalizationService.IsEnglish
                        ? $"Enter page numbers from 1 to {_pageCount}. Example: 1,3,5-10"
                        : $"1～{_pageCount} の範囲でページ番号を入力してください。例: 1,3,5-10",
                    LocalizationService.Translate("ページ指定"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            pages = pages.Where(page => page != _referencePageNumber).ToArray();
        }
        else pages = Enumerable.Range(1, _pageCount).Where(page => page != _referencePageNumber).ToArray();

        if (pages.Count > 0) return true;
        MessageBox.Show(
            LocalizationService.IsEnglish
                ? "Select at least one target page other than the reference page."
                : "参照ページ以外の対象ページを選択してください。",
            LocalizationService.Translate("ページ指定"),
            MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void TargetMode_OnChanged(object sender, RoutedEventArgs e)
    {
        if (PageSpecificationTextBox is not null)
            PageSpecificationTextBox.IsEnabled = SpecifiedPagesRadioButton?.IsChecked == true;
    }

    private void SimilaritySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SimilarityValueTextBlock is not null) SimilarityValueTextBlock.Text = $"{e.NewValue:0}%";
    }
}
