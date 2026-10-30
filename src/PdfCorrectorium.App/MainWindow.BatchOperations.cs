using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private void EditingModeMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditDocument || sender is not FrameworkElement { Tag: string name } ||
            !Enum.TryParse<ViewModels.EditorInteractionMode>(name, out var mode)) return;
        CommitPendingEditorBindings();
        CancelPointerRectangle();
        ViewModel.EditorMode = mode;
    }
    private void AutoPageLinks_OnClick(object sender, RoutedEventArgs e) => ShowDocumentBatch(DocumentBatchKind.AutomaticLinks);
    private void RepeatRedaction_OnClick(object sender, RoutedEventArgs e) => ShowDocumentBatch(DocumentBatchKind.RepeatedRedaction);
    private void SearchRedactions_OnClick(object sender, RoutedEventArgs e) => ShowDocumentBatch(DocumentBatchKind.TextRedaction);
    private void ShowDocumentBatch(DocumentBatchKind kind)
    {
        if (!ViewModel.CanEditDocument) return;
        CommitPendingEditorBindings();
        var dialog = new DocumentBatchWindow(ViewModel, kind) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { ViewModel.ApplyBatchCandidates(dialog.ProjectId, dialog.Candidates); }
        catch (InvalidOperationException error) { ViewModel.StatusMessageForInteraction(error.Message); }
    }
    private void LinkAlign_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string operation }) ViewModel.AlignSelectedLinks(operation);
    }
    private void LinkLock_OnClick(object sender, RoutedEventArgs e) => ViewModel.SetSelectedLinksLocked(!ViewModel.AreSelectedLinksLocked);
}
