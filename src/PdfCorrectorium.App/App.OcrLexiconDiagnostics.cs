using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyOcrLexiconWorkflowAsync(MainWindow owner, MainWindowViewModel vm, string input, string directory, Action<bool, string> check)
    {
        async Task Ready()
        {
            var timeout = Stopwatch.StartNew();
            while (!vm.IsOcrLexiconReady && timeout.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(50);
            check(vm.IsOcrLexiconReady, "Idle dictionary preparation completes without blocking editing.");
        }
        await vm.LoadProjectForDiagnosticsAsync(input); await Ready();
        var overlays = (System.Collections.IDictionary)typeof(MainWindowViewModel).GetField("_pageOverlays", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        check(overlays.Count == 1 && !vm.HasUnsavedChanges, "Background vocabulary uses stored text without eagerly loading page overlay models or making the project dirty.");
        var referenceCount = vm.OcrWordFrequencies.Single(w => w.Word == "Node.js").Count;
        var watch = Stopwatch.StartNew(); var query = await vm.QueryOcrLexiconAsync("Node.」s", default);
        check(query.SuggestedText == "Node.js" && !query.CanApplyAutomatically && watch.ElapsedMilliseconds < 500,
            "A prepared frequency index supplies reviewed line candidates without a document rescan.");
        var result = await vm.AnalyzeOcrSimilarWordsAsync(new(MinimumFrequencyRatio: 2));
        var candidate = result.Analysis.Candidates.First(c => c.Word == "Node.is" && !c.Sample.IsLocked);
        var corrected = vm.ApplySimilarWordCorrectionsAndRefresh(result, [candidate], registerKnown: true, registerRules: true);
        check(corrected.ChangedCount == 1 && vm.CurrentOcrLexicon.KnownTerms.Contains("Node.js") && vm.CurrentOcrLexicon.Rules.Single().Source == "Node.is" && !vm.CurrentOcrLexicon.Rules.Single().AllowAutomatic,
            "Reviewed correction and optional explicit registration are one operation, with auto permission off by default.");
        vm.UndoCommand.Execute(null);
        check(!vm.HasUnsavedChanges && vm.CurrentOcrLexicon.Rules.Count == 0 && vm.CurrentOcrLexicon.KnownTerms.Count == 0, "One Undo restores both OCR text and its dictionary registration.");
        vm.RedoCommand.Execute(null);
        check(vm.CurrentOcrLexicon.Rules.Count == 1, "Redo restores the exact registered rule identity.");
        await Ready();
        check(vm.OcrWordFrequencies.Single(w => w.Word == "Node.js").Count == referenceCount + 1, "Incremental counts include the reviewed correction exactly once.");
        var output = Path.Combine(directory, "shared-dictionary.pdfocrproj"); await vm.SaveProjectForDiagnosticsAsync(output);
        var saved = await new ProjectPackageService().OpenAsync(output);
        check(saved.OcrLexicon.Rules.Count == 1 && saved.OcrWordCountsCache is not null, "Normal save retains the authoritative dictionary and a completed matching frequency cache.");
        foreach (var language in new[] { LocalizationService.JapaneseLanguage, LocalizationService.EnglishLanguage })
        {
            LocalizationService.SetLanguage(language);
            var dialog = new OcrLexiconWindow(vm) { Owner = owner.IsLoaded ? owner : null, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            dialog.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            check(((TextBox)dialog.FindName("KnownTermsEditor")).Text == "Node.js", "Dictionary management shows saved document terms without translating them.");
            var footer = (StackPanel)dialog.FindName("DialogFooter");
            check(footer.Children.OfType<Button>().All(b => string.IsNullOrEmpty(PdfCorrectorium.App.Resources.ActionIcon.GetKey(b))), "Dictionary dialog footer buttons remain text-only.");
            SaveIconDiagnosticImage(dialog.Content as FrameworkElement ?? dialog, Path.Combine(directory, language == LocalizationService.EnglishLanguage ? "ocr-dictionary-en.png" : "ocr-dictionary-ja.png"));
            dialog.Close();
        }
        LocalizationService.SetLanguage(LocalizationService.JapaneseLanguage);
        await vm.LoadProjectForDiagnosticsAsync(output); await Ready();
        check(vm.CurrentOcrLexicon.KnownTerms.Contains("Node.js") && vm.CurrentOcrLexicon.Rules.Count == 1 && !vm.HasUnsavedChanges, "Opening a saved project restores knowledge without automatic text mutation.");
        await vm.AnalyzeOcrSimilarWordsAsync(new(MinimumFrequencyRatio: 2));
        var frequencyWindow = new OcrQualityAnalysisWindow(vm);
        try
        {
            var frequencyRows = ((DataGrid)frequencyWindow.FindName("WordResultsGrid")).Items.Cast<object>().ToArray();
            var explicitRows = frequencyRows.Where(row => ((OcrSimilarWordCandidate)row.GetType().GetProperty("Source")!.GetValue(row)!).Distance == 0).ToArray();
            check(explicitRows.Length > 0 && explicitRows.All(row => row.GetType().GetProperty("WordCount")!.GetValue(row) is null &&
                row.GetType().GetProperty("ReferenceCount")!.GetValue(row) is null), "Unmeasured phrase/rule frequencies are blank, never fabricated as zero.");
        }
        finally { frequencyWindow.Close(); }
        var restored = new OcrQualityAnalysisWindow(vm); check(((TextBox)restored.FindName("KnownWordsTextBox")).Text == "Node.js", "Normal similar-word analysis restores the same persisted dictionary."); restored.Close();
        // An idle text-only worker must not render a bitmap or create overlay view models.
        var project = await new ProjectPackageService().OpenAsync(input);
        var native = Path.Combine(directory, "native-vocabulary.pdfocrproj");
        await new ProjectPackageService().SaveAsync(native, project with { Pages = [] });
        await vm.LoadProjectForDiagnosticsAsync(native); await Ready();
        check(overlays.Count == 1, "Native PDF vocabulary extraction leaves unloaded pages unloaded.");
        await vm.LoadProjectForDiagnosticsAsync(input);
    }
}
