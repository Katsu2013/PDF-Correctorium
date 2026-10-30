using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private CancellationTokenSource? _rapidOcrCancellation;
    private async void RecognizeLine_OnClick(object sender, RoutedEventArgs e)
    {
        if (_rapidOcrCancellation is not null) return;
        CommitPendingEditorBindings();
        if (!ViewModel.CanRecognizeSelectedLine) return;
        using var cancellation = new CancellationTokenSource(); _rapidOcrCancellation = cancellation;
        try
        {
            var ticket = await ViewModel.RecognizeSelectedLineAsync(cancellation.Token);
            if (ticket is null || cancellation.IsCancellationRequested) return;
            var dictionary = await ViewModel.QueryOcrLexiconAsync(ticket.Result.Recognition.Reading.Text, cancellation.Token);
            if (!ViewModel.IsRapidOcrTicketCurrent(ticket)) return;
            if (dictionary.Matches.Count > 0)
            {
                // Rule authorization is distinct from model confidence and missing-character completion.
                if (dictionary.CanApplyAutomatically && await ViewModel.ApplyAutomaticOcrRulesAsync(ticket, dictionary, cancellation.Token)) return;
                new RapidOcrReviewWindow(ViewModel, ticket, dictionary) { Owner = this }.ShowDialog(); return;
            }
            if (ticket.Result.Recognition.Reading.Text == ticket.Before.Text)
            { ViewModel.ApplyRapidOcrResult(ticket, automatic: false); return; }
            if (ticket.Completion.IsAutomatic && ViewModel.ApplyRapidOcrResult(ticket, automatic: true)) return;
            new RapidOcrReviewWindow(ViewModel, ticket) { Owner = this }.ShowDialog();
        }
        catch (OperationCanceledException) { ViewModel.ReportRapidOcrStatus("選択行の再認識を中止しました。変更はありません。"); }
        catch (Exception error)
        {
            if (cancellation.IsCancellationRequested) return;
            await ViewModel.ReportRapidOcrFailureAsync(error);
        }
        finally { _rapidOcrCancellation = null; }
    }
    private void CancelRecognizeLine_OnClick(object sender, RoutedEventArgs e) => _rapidOcrCancellation?.Cancel();
}
