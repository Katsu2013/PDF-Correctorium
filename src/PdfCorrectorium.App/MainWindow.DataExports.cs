using System.Windows;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private void DocumentDataExport_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditDocument || sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var kind)) return;
        CommitPendingEditorBindings();
        new DocumentDataExportWindow(ViewModel, (DocumentDataExportKind)kind) { Owner = this }.ShowDialog();
    }
}
