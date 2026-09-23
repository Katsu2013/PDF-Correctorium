using System.Windows;
using System.Windows.Controls;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class BlankPageWindow : Window
{
    private readonly double _currentWidth;
    private readonly double _currentHeight;
    private readonly int _currentPage;
    private readonly int _pageCount;

    public BlankPageWindow(double currentWidth, double currentHeight, int currentPage, int pageCount)
    {
        _currentWidth = currentWidth;
        _currentHeight = currentHeight;
        _currentPage = currentPage;
        _pageCount = pageCount;
        InitializeComponent();
        LocalizationService.Apply(this);
    }

    public int ResultCount { get; private set; }
    public double ResultWidthPoints { get; private set; }
    public double ResultHeightPoints { get; private set; }
    public int ResultInsertAfter { get; private set; }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountBox.Text, out var count) || count is < 1 or > 100)
        {
            MessageBox.Show(this, "ページ数は1～100で入力してください。", "空白ページを挿入", MessageBoxButton.OK, MessageBoxImage.Warning);
            CountBox.Focus();
            return;
        }
        var size = (SizeBox.SelectedItem as ComboBoxItem)?.Tag as string;
        (ResultWidthPoints, ResultHeightPoints) = size switch
        {
            "A4" => (595.276, 841.89),
            "A3" => (841.89, 1190.551),
            "Letter" => (612d, 792d),
            _ => (_currentWidth, _currentHeight),
        };
        var position = (PositionBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ResultInsertAfter = position switch
        {
            "Before" => Math.Max(0, _currentPage - 1),
            "Start" => 0,
            "End" => _pageCount,
            _ => _currentPage,
        };
        ResultCount = count;
        DialogResult = true;
    }
}
