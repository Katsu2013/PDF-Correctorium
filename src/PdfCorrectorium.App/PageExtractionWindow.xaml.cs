using System.Windows;
using System.Windows.Controls;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class PageExtractionWindow : Window
{
    private readonly int _pageCount;
    private readonly int _currentPage;
    private readonly IReadOnlyList<int> _selectedPages;

    public PageExtractionWindow(int pageCount, int currentPage, IReadOnlyList<int> selectedPages)
    {
        _pageCount = pageCount;
        _currentPage = Math.Clamp(currentPage, 1, pageCount);
        _selectedPages = selectedPages.Where(page => page >= 1 && page <= pageCount).Distinct().Order().ToArray();
        InitializeComponent();
        PresetBox.SelectedIndex = _selectedPages.Count > 1 ? 2 : 1;
        LocalizationService.Apply(this);
    }

    public IReadOnlyList<int> ResultPages { get; private set; } = [];

    private void PresetBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RangeBox is null || PresetBox.SelectedItem is not ComboBoxItem { Tag: string preset }) return;
        RangeBox.Text = preset switch
        {
            "All" => $"1-{_pageCount}",
            "Current" => _currentPage.ToString(),
            "Selected" => PageRangeParser.Format(_selectedPages.Count > 0 ? _selectedPages : [_currentPage]),
            "Odd" => string.Join(',', Enumerable.Range(1, _pageCount).Where(page => page % 2 == 1)),
            "Even" => string.Join(',', Enumerable.Range(1, _pageCount).Where(page => page % 2 == 0)),
            _ => RangeBox.Text,
        };
        RangeBox.IsReadOnly = preset != "Custom";
        if (preset == "Custom") RangeBox.Focus();
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!PageRangeParser.TryParse(RangeBox.Text, _pageCount, out var pages))
        {
            MessageBox.Show(this, $"ページ範囲を1～{_pageCount}で入力してください。", "ページを抽出",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            RangeBox.Focus();
            return;
        }
        ResultPages = pages;
        DialogResult = true;
    }
}
