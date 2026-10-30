using System.IO;
using System.Text.Json;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class App
{
    private static async Task VerifyUserParagraphSpeechAsync(MainWindow main, string output, string projectPath, int number, Action<bool, string> check)
    {
        var vm = (MainWindowViewModel)main.DataContext;
        vm.DocumentSwitchPromptOverride = () => System.Windows.MessageBoxResult.No;
        await vm.LoadProjectForDiagnosticsAsync(projectPath);
        await vm.EnsureDocumentMetadataAsync();
        await using var worker = new PdfNativeWorkerClient(reuseInputs: true);
        var before = await vm.ReadSpeechPageAsync(vm.CreateSpeechTicket(ReadAloudScope.RemainingPages), number, worker, default);
        await vm.DisplaySpeechPageAsync(number, default);
        var after = await vm.ReadSpeechPageAsync(vm.CreateSpeechTicket(ReadAloudScope.RemainingPages), number, worker, default);
        static object Snapshot(MainWindowViewModel.SpeechPage page) => new
        {
            page.PageNumber,
            segments = page.Segments.Select(row => new { row.RegionId, row.Text, row.ParagraphGroupId, row.BreakBefore, row.IsPartialSelection, row.Bounds }),
            paragraphs = SpeechParagraphs.Build(page.Segments).Select(row => new { row.Text, ids = row.Spans.Select(span => span.Segment.RegionId) }),
        };
        File.WriteAllText(Path.Combine(output, "paragraph-speech-snapshots.json"), JsonSerializer.Serialize(new { before = Snapshot(before), after = Snapshot(after) }, new JsonSerializerOptions { WriteIndented = true }));
        check(SpeechParagraphs.Build(before.Segments).Any(row => row.Text == "Chapter 1"), "The supplied project's Chapter/1 is one paragraph before following its page.");
        check(SpeechParagraphs.Build(after.Segments).Any(row => row.Text == "Chapter 1"), "The supplied project's Chapter/1 is one paragraph after its page is displayed.");
    }
}
