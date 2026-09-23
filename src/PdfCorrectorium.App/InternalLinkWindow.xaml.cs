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
    private string _externalUriText;
    private readonly int _pageCount;

    public InternalLinkWindow(int pageCount, PdfInternalLink? existing, int existingDestinationPage)
    {
        _pageCount = pageCount;
        _destinationPageNumber = Math.Clamp(existingDestinationPage, 1, pageCount).ToString(CultureInfo.CurrentCulture);
        _zoomText = existing?.DestinationZoomPercent?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
        _description = existing?.Description ?? string.Empty;
        _externalUriText = existing?.ExternalUri ?? "https://";
        InitializeComponent();
        DataContext = this;
        if (!string.IsNullOrWhiteSpace(existing?.ExternalUri)) ExternalTargetRadio.IsChecked = true;
        LocalizationService.Apply(this);
    }

    public string DestinationPageNumber { get => _destinationPageNumber; set => Set(ref _destinationPageNumber, value); }
    public string ZoomText { get => _zoomText; set => Set(ref _zoomText, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string ExternalUriText { get => _externalUriText; set => Set(ref _externalUriText, value); }
    public int ResultDestinationPage { get; private set; }
    public double? ResultZoomPercent { get; private set; }
    public string? ResultExternalUri { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (ExternalTargetRadio.IsChecked == true)
        {
            if (!Uri.TryCreate(ExternalUriText.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                MessageBox.Show(this, "Webアドレスは https:// または http:// で始まる完全なアドレスを入力してください。",
                    "ページリンク", MessageBoxButton.OK, MessageBoxImage.Warning);
                ExternalUriBox.Focus();
                return;
            }
            ResultExternalUri = uri.AbsoluteUri;
            ResultDestinationPage = 0;
            ResultZoomPercent = null;
            DialogResult = true;
            return;
        }
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
        ResultExternalUri = null;
        DialogResult = true;
    }

    private void TargetKind_OnChecked(object sender, RoutedEventArgs e)
    {
        if (InternalPagePanel is null || ZoomPanel is null || ExternalUriPanel is null) return;
        var external = ExternalTargetRadio.IsChecked == true;
        InternalPagePanel.IsEnabled = !external;
        ZoomPanel.IsEnabled = !external;
        ExternalUriPanel.IsEnabled = external;
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
