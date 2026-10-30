using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class OcrOperationGuideWindow : Window
{
    public OcrOperationGuideWindow(IEnumerable<string> paragraphs)
    {
        InitializeComponent();
        GuideItems.ItemsSource = paragraphs.Select(LocalizationService.Translate).ToArray();
        LocalizationService.Apply(this);
    }
}
