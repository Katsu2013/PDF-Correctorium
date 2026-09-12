using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class InternalLinkWindow : Window, INotifyPropertyChanged
{
    private string _destinationPageNumber;
    private string _zoomText;
    private string _description;
    private readonly int _pageCount;

    public InternalLinkWindow(int pageCount, PdfInternalLink? existing, int existingDestinationPage)
    {
        _pageCount = pageCount;
        _destinationPageNumber = Math.Clamp(existingDestinationPage, 1, pageCount).ToString(CultureInfo.CurrentCulture);
        _zoomText = existing?.DestinationZoomPercent?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
        _description = existing?.Description ?? string.Empty;
        InitializeComponent();
        DataContext = this;
        LocalizationService.Apply(this);
    }

    public string DestinationPageNumber { get => _destinationPageNumber; set => Set(ref _destinationPageNumber, value); }
    public string ZoomText { get => _zoomText; set => Set(ref _zoomText, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public int ResultDestinationPage { get; private set; }
    public double? ResultZoomPercent { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DestinationPageNumber, out var page) || page < 1 || page > _pageCount)
        {
            MessageBox.Show(this, $"移動先ページは1～{_pageCount}で入力してください。", "ページリンク", MessageBoxButton.OK, MessageBoxImage.Warning);
            PageNumberBox.Focus();
            return;
        }
        double? zoom = null;
        if (!string.IsNullOrWhiteSpace(ZoomText))
        {
            if ((!double.TryParse(ZoomText, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) &&
                 !double.TryParse(ZoomText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) || parsed is < 25 or > 400)
            {
                MessageBox.Show(this, "表示倍率は25～400%で入力するか、空欄にしてください。", "ページリンク", MessageBoxButton.OK, MessageBoxImage.Warning);
                ZoomBox.Focus();
                return;
            }
            zoom = parsed;
        }
        ResultDestinationPage = page;
        ResultZoomPercent = zoom;
        DialogResult = true;
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
