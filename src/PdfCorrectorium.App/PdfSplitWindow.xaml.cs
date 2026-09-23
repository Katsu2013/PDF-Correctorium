using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class PdfSplitWindow : Window
{
    private readonly int _pageCount;

    public PdfSplitWindow(int pageCount)
    {
        _pageCount = pageCount;
        InitializeComponent();
        LocalizationService.Apply(this);
    }

    public IReadOnlyList<IReadOnlyList<int>> ResultGroups { get; private set; } = [];

    private void Mode_OnChecked(object sender, RoutedEventArgs e)
    {
        if (PagesPerFileBox is null || BoundaryBox is null) return;
        PagesPerFileBox.IsEnabled = FixedSizeRadio.IsChecked == true;
        BoundaryBox.IsEnabled = BoundaryRadio.IsChecked == true;
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (EveryPageRadio.IsChecked == true)
            ResultGroups = PdfDocumentOrganizationService.BuildFixedSizeGroups(_pageCount, 1);
        else if (FixedSizeRadio.IsChecked == true)
        {
            if (!int.TryParse(PagesPerFileBox.Text, out var pagesPerFile) || pagesPerFile < 1 || pagesPerFile > _pageCount)
            {
                Warn($"1～{_pageCount}のページ数を入力してください。", PagesPerFileBox);
                return;
            }
            ResultGroups = PdfDocumentOrganizationService.BuildFixedSizeGroups(_pageCount, pagesPerFile);
        }
        else
        {
            if (!PageRangeParser.TryParse(BoundaryBox.Text, _pageCount, out var starts))
            {
                Warn($"各ファイルの開始ページを1～{_pageCount}で入力してください。", BoundaryBox);
                return;
            }
            ResultGroups = PdfDocumentOrganizationService.BuildBoundaryGroups(_pageCount, starts);
        }
        DialogResult = true;
    }

    private void Warn(string message, System.Windows.Controls.Control control)
    {
        MessageBox.Show(this, message, "PDFを分割", MessageBoxButton.OK, MessageBoxImage.Warning);
        control.Focus();
    }
}
