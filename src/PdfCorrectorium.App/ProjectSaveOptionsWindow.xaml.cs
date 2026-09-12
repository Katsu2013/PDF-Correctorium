using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

/// <summary>プロジェクトを別名保存するときのPDF格納方式を選択します。</summary>
public partial class ProjectSaveOptionsWindow : Window
{
    public ProjectSaveOptionsWindow(ProjectPdfStorageMode initialMode)
    {
        InitializeComponent();
        var normalizedMode = initialMode == ProjectPdfStorageMode.Relative
            ? ProjectPdfStorageMode.Relative
            : ProjectPdfStorageMode.Embedded;
        EmbeddedModeRadioButton.IsChecked = normalizedMode == ProjectPdfStorageMode.Embedded;
        RelativeModeRadioButton.IsChecked = normalizedMode == ProjectPdfStorageMode.Relative;
        SelectedMode = normalizedMode;
        LocalizationService.Apply(this);
    }

    public ProjectPdfStorageMode SelectedMode { get; private set; }

    private void StorageMode_OnChecked(object sender, RoutedEventArgs e)
    {
        SelectedMode = ReferenceEquals(sender, RelativeModeRadioButton)
            ? ProjectPdfStorageMode.Relative
            : ProjectPdfStorageMode.Embedded;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedMode = RelativeModeRadioButton.IsChecked == true
            ? ProjectPdfStorageMode.Relative
            : ProjectPdfStorageMode.Embedded;
        DialogResult = true;
    }
}
