using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;

namespace PdfCorrectorium.App;
public partial class App
{
    private async Task VerifyRapidOcrLexiconAsync(MainWindow owner, MainWindowViewModel vm, string directory, Action<bool, string> check)
    {
        var ticket = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No dictionary test ticket.");
        var raw = ticket.Result.Recognition.Reading.Text;
        // Use an explicit equal-grapheme phrase, irrespective of inference spelling in the remaining sentence.
        var elements = OcrLineReconciler.Elements(raw); elements[0] = elements[0] == "プ" ? "フ" : "プ";
        var target = string.Concat(elements);
        ticket.Region.Text = raw;
        var rule = new OcrCorrectionRule { Source = raw, Target = target, AllowAutomatic = true };
        check(vm.SetOcrLexicon(new() { Rules = [rule] }), "Explicit automatic permission is stored in the project dictionary.");
        check(!vm.IsRapidOcrTicketCurrent(ticket), "Changing dictionary permission invalidates an older OCR ticket.");
        ticket = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No refreshed dictionary ticket.");
        var query = await vm.QueryOcrLexiconAsync(raw, default);
        check(query.CanApplyAutomatically && query.SuggestedText == target, "The re-OCR query finds the exact permitted registered Japanese phrase.");
        var prepared = await vm.PrepareRapidOcrReviewAsync(ticket, target, default);
        check(prepared is not null && prepared.Result.Recognition.Reading.Scores.Length == 0 && !prepared.Completion.IsAutomatic,
            "Dictionary replacements never inherit fabricated model confidence or missing-character auto evidence.");
        var before = ticket.Region.Capture(); var image = ticket.Result.Image;
        check(await vm.ApplyAutomaticOcrRulesAsync(ticket, query, default) && ticket.Region.Text == target,
            "Approved explicit rules apply through image fitting with separate authorization.");
        vm.UndoCommand.Execute(null); check(ticket.Region.Capture() == before, "Automatic rule text and image fit share one Undo.");
        vm.UndoCommand.Execute(null); check(vm.CurrentOcrLexicon.Rules.Count == 0, "The separate explicit permission edit remains independently undoable.");
        vm.UndoCommand.Execute(null); before = ticket.Region.Capture();
        ticket = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No manual registration ticket.");
        var manual = new RapidOcrReviewWindow(vm, ticket) { Owner = owner, ShowActivated = false };
        Exception? error = null;
        manual.Loaded += (_, _) => manual.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                ((TextBox)manual.FindName("CandidateEditor")).Text = target;
                ((CheckBox)manual.FindName("RegisterKnownCheck")).IsChecked = true;
                ((CheckBox)manual.FindName("RegisterRuleCheck")).IsChecked = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                check(((CheckBox)manual.FindName("RegisterKnownCheck")).IsEnabled, "Reviewed registered phrases can be explicitly retained, rather than learned silently.");
                SaveIconDiagnosticImage((FrameworkElement)manual.Content, Path.Combine(directory, "review-dictionary-registration.png"));
                ((Button)manual.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception failure) { error = failure; manual.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        check(manual.ShowDialog() == true && error is null, "Actual re-OCR review applies an edited phrase with explicit registration.");
        check(vm.CurrentOcrLexicon.KnownTerms.Contains(target) && vm.CurrentOcrLexicon.Rules.Single().Source == raw &&
            !vm.CurrentOcrLexicon.Rules.Single().AllowAutomatic, "Review registration creates verified knowledge but never silently grants auto permission.");
        vm.UndoCommand.Execute(null);
        check(ticket.Region.Capture() == before && vm.CurrentOcrLexicon.Rules.Count == 0 && vm.CurrentOcrLexicon.KnownTerms.Count == 0,
            "One Undo restores reviewed text, geometry, and both registrations together.");
        check(ReferenceEquals(ticket.Result.Image, image) || ticket.Result.Image.PixelWidth == image.PixelWidth,
            "Dictionary refinement uses the existing crop and never invokes another OCR pass.");
        var reviewedDictionary = new OcrLexicon { Rules = [rule with { AllowAutomatic = false }] };
        vm.SetOcrLexicon(reviewedDictionary);
        ticket = await vm.RecognizeSelectedLineAsync(default) ?? throw new InvalidOperationException("No review-only ticket.");
        query = await vm.QueryOcrLexiconAsync(raw, default);
        check(!query.CanApplyAutomatically && !await vm.ApplyAutomaticOcrRulesAsync(ticket, query, default), "Review-only rules cannot enter the automatic application path.");
        var dialog = new RapidOcrReviewWindow(vm, ticket, query) { Owner = owner, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        dialog.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(((TextBox)dialog.FindName("CandidateEditor")).Text == target && ((ListBox)dialog.FindName("DictionaryCandidates")).Items.Count > 0,
            "Dictionary suggestions appear separately in the normal editable review dialog.");
        typeof(RapidOcrReviewWindow).GetMethod("RestoreCandidate_OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        check(((TextBox)dialog.FindName("CandidateEditor")).Text == raw, "Restore recognition returns the exact raw model result, not a dictionary-corrected string.");
        SaveIconDiagnosticImage((FrameworkElement)dialog.Content, Path.Combine(directory, "review-dictionary-candidates.png")); dialog.Close();
        vm.UndoCommand.Execute(null);
    }
}
