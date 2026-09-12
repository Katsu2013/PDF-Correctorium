using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class InputPdfIssuesWindow : Window
{
    public InputPdfIssuesWindow(IReadOnlyList<PdfInputIssue> issues)
    {
        InitializeComponent();
        DataContext = issues;
        LocalizationService.Apply(this);
    }
}
