using System.Windows;
namespace PdfCorrectorium.App;
public partial class MainWindow
{
    private void OcrLexicon_OnClick(object sender, RoutedEventArgs e)
    { CommitPendingEditorBindings(); new OcrLexiconWindow(ViewModel) { Owner = this }.ShowDialog(); }
}
