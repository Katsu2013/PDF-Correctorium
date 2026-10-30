using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using AvalonDock.Layout;
using PdfCorrectorium.App.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifySharedReadingsAndDockAsync(MainWindow main, string output, Action<bool, string> check,
        Func<FrameworkElement, string, int, int, Task> picture)
    {
        var vm = (MainWindowViewModel)main.DataContext; var packages = new ProjectPackageService();
        var pdf = Path.Combine(output, "shared-readings-source.pdf"); WriteDocumentUiTestPdf(pdf);
        var geometry = new TextGeometry { LocalBounds = new(new(25, 280), new(220, 24)), RotationCenter = new(135, 292), IsGeometryLocked = true };
        var first = new OcrTextRegion { OriginalText = "今日の日本語", OriginalGeometry = geometry, EditedGeometry = geometry,
            WordReadings = [new() { SurfaceText = "今日", ReadingText = "こんにち" }] };
        var second = first with { Id = Guid.NewGuid(), OriginalText = "今日", WordReadings = [],
            OriginalGeometry = geometry with { LocalBounds = new(new(25, 200), new(80, 24)) }, EditedGeometry = geometry with { LocalBounds = new(new(25, 200), new(80, 24)) } };
        var next = first with { Id = Guid.NewGuid(), OriginalText = "今日、日本語。", WordReadings = [] };
        var page = new OcrPage { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [first, second], ReadingOrder = [first.Id, second.Id] };
        var project = new PdfCorrectoriumProject { SourcePdf = await packages.CreateSourceReferenceAsync(pdf),
            Pages = [page, new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400, TextRegions = [next], ReadingOrder = [next.Id] }] };
        PdfExportService.PopulateReadingFixture(pdf, project.Pages);
        project = project with { SourcePdf = await packages.CreateSourceReferenceAsync(pdf) };
        var input = Path.Combine(output, "shared-readings-input.pdfocrproj"); await packages.SaveAsync(input, project);
        await vm.LoadProjectForDiagnosticsAsync(input); await vm.EnsureDocumentMetadataAsync();
        vm.EditorModeIndex = 0; vm.IsOcrOverlayVisible = true; vm.ShowWordReadingRuby = true; vm.ShowOcrFrames = false;
        var row1 = vm.OverlayItems.First(row => row.Id == first.Id); var row2 = vm.OverlayItems.First(row => row.Id == second.Id);
        vm.SetOverlaySelection([row1], row1); row1.SelectCharacterRangeByTextOffset(0, 2);
        var before1 = row1.Capture(); var before2 = row2.Capture();
        var baseline = Path.Combine(output, "shared-readings-before.pdf"); await vm.ExportPdfForDiagnosticsAsync(baseline);
        vm.WordReadingScopeIndex = 1; vm.WordReadingSurface = "今日"; vm.WordReadingPronunciation = "きょう";
        vm.RegisterWordReadingCommand.Execute(null);
        vm.WordReadingSurface = "日本語"; vm.WordReadingPronunciation = "にほんご"; vm.RegisterWordReadingCommand.Execute(null);
        check(vm.CommonWordReadings.Count == 2 && row1.Capture() == before1 && row2.Capture() == before2,
            "Registering shared readings changes only project metadata, without copying readings into rows or changing geometry.");
        check(row1.WordReadingMatches.First().ReadingText == "こんにち" && row2.WordReadingMatches.Single().ReadingText == "きょう",
            "Visible ruby prefers a local exception and applies the shared reading to other rows.");
        await using (var worker = new PdfNativeWorkerClient(reuseInputs: true))
        {
            var unseen = await vm.ReadSpeechPageAsync(vm.CreateSpeechTicket(ReadAloudScope.RemainingPages), 2, worker, default);
            check(unseen.Segments.Single().Text == "きょう、にほんご。" && worker.InputStageCount == 0 && vm.SelectedPage?.PageNumber == 1,
                "An unvisited page inherits shared readings for speech without rendering it or staging a PDF.");
        }
        var cached = row2.WordReadingMatches;
        for (var index = 0; index < 10000; index++)
            if (!ReferenceEquals(cached, row2.WordReadingMatches)) throw new InvalidOperationException("Shared matches were regenerated.");
        check(true, "Shared preview matches remain row-cached across ten thousand reads.");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
        var layer = IconVisuals((DependencyObject)main.Content).OfType<OcrCharacterLayer>().First(item => ReferenceEquals(item.DataContext, row2));
        var hit = layer.RubyHitAreas.Single();
        check(layer.FindReadingAt(new(hit.Bounds.Left + hit.Bounds.Width / 2, hit.Bounds.Top + hit.Bounds.Height / 2)) == hit.Reading,
            "The actual drawn ruby rectangle supplies its matching word for double-click editing.");
        var popup = main.OpenRubyEditor(row2, hit.Reading, new(80, 100));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var form = (StackPanel)((Border)popup.Child).Child;
        var scope = form.Children.OfType<ComboBox>().Single(); scope.SelectedIndex = 0;
        var pronunciation = form.Children.OfType<TextBox>().Single(); pronunciation.SetCurrentValue(TextBox.TextProperty, "こんじつ");
        pronunciation.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        await picture((FrameworkElement)popup.Child, "ruby-edit-popup", 280, 220);
        var button = form.Children.OfType<Button>().Single();
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!popup.IsOpen && row2.WordReadingDefinitions.Single().ReadingText == "こんじつ" && vm.CommonWordReadings.First().ReadingText == "きょう" && row2.Capture() with { WordReadingsText = before2.WordReadingsText } == before2,
            "The popup's actual Apply action commits a row exception without moving text or changing the common dictionary.");
        vm.UndoCommand.Execute(null);
        check(row2.WordReadingMatches.Single().ReadingText == "きょう", "Undo of a row exception restores inherited ruby.");
        vm.WordReadingScopeIndex = 1; vm.SelectedWordReading = vm.CommonWordReadings.First(item => item.SurfaceText == "今日");
        vm.RemoveWordReadingCommand.Execute(null);
        check(row1.WordReadingMatches.First().ReadingText == "こんにち" && row2.WordReadingMatches.Count == 0,
            "Deleting a shared word preserves explicit row exceptions.");
        vm.UndoCommand.Execute(null);
        check(row2.WordReadingMatches.Single().ReadingText == "きょう", "Shared dictionary deletion supports Undo and invalidates preview caches.");
        vm.RedoCommand.Execute(null); vm.UndoCommand.Execute(null);
        var saved = Path.Combine(output, "shared-readings-saved.pdfocrproj"); await vm.SaveProjectForDiagnosticsAsync(saved, ProjectPdfStorageMode.Embedded);
        var reopened = await packages.OpenAsync(saved);
        check(reopened.WordReadings.Count == 2 && reopened.Pages[0].TextRegions[0].WordReadings.Single().ReadingText == "こんにち" && reopened.Pages[1].TextRegions.Single().WordReadings.Count == 0,
            "Save retains project-wide readings, local exceptions and uncopied unvisited rows.");
        using (var archive = ZipFile.OpenRead(saved))
        using (var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open()))
            check(manifest.RootElement.GetProperty("formatVersion").GetString() == "1.14" && manifest.RootElement.GetProperty("minimumApplicationVersion").GetString() == "1.0.0-dev.262",
                "The saved manifest protects shared readings from older savers.");
        var exported = Path.Combine(output, "shared-readings-after.pdf"); await vm.ExportPdfForDiagnosticsAsync(exported);
        static byte[] Pixels(BitmapSource image) { var stride = image.PixelWidth * 4; var bytes = new byte[stride * image.PixelHeight]; image.CopyPixels(bytes, stride, 0); return bytes; }
        var preview = new PdfPreviewService();
        var beforePixels = Pixels((await preview.RenderPageAsync(baseline, 1, 600)).Image);
        var afterPixels = Pixels((await preview.RenderPageAsync(exported, 1, 600)).Image);
        check(beforePixels.SequenceEqual(afterPixels),
            "Actual PDF exports remain pixel-identical after shared pronunciation edits.");
        await vm.LoadProjectForDiagnosticsAsync(saved); await vm.EnsureDocumentMetadataAsync();
        check(vm.CommonWordReadings.Count == 2 && vm.OverlayItems.First(row => row.Id == second.Id).WordReadingMatches.Single().ReadingText == "きょう",
            "Reopening applies the saved common dictionary to newly materialized row views.");
        vm.WordReadingScopeIndex = 0;
        await VerifyDockedSpeechStateAsync(output, saved, check, picture);
    }

    private async Task VerifyDockedSpeechStateAsync(string output, string project, Action<bool, string> check,
        Func<FrameworkElement, string, int, int, Task> picture)
    {
        async Task Settle() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(25); }
        async Task Until(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(20); while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("Docked speech did not settle."); await Task.Delay(15); } await Settle(); }
        static T Control<T>(FrameworkElement root, string name) where T : class => (T)root.FindName(name);
        var directory = Path.Combine(output, "docked-speech"); var fake = new DiagnosticSpeech();
        var window = new MainWindow(diagnosticDirectory: directory) { ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, ClosePromptOverride = () => MessageBoxResult.No,
            ReadAloudPlaybackOverride = fake };
        try
        {
            window.Show(); await Settle();
            check(window.DockedReadAloudPanel is null && window.PropertyPanes["read-aloud"].IsHidden,
                $"Normal startup keeps speech hidden and does not initialize voices (created={window.DockedReadAloudPanel is not null}, hidden={window.PropertyPanes["read-aloud"].IsHidden}).");
            var vm = (MainWindowViewModel)window.DataContext; await vm.LoadProjectForDiagnosticsAsync(project); await vm.EnsureDocumentMetadataAsync();
            Control<Button>(window, "ReadAloudToolbarButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.DockedReadAloudPanel is { } value && Control<Button>(value, "PlayButton").IsEnabled);
            var panel = window.DockedReadAloudPanel!; var pane = window.PropertyPanes["read-aloud"];
            Control<Button>(panel, "PlayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => fake.Texts.Count == 1);
            Control<Button>(panel, "PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => fake.Paused);
            var position = Control<TextBlock>(panel, "PositionText").Text;
            window.SetPaneAutoHide(pane, true); await Settle();
            check(pane.IsAutoHidden && ReferenceEquals(panel, window.DockedReadAloudPanel) && fake.Paused && fake.Texts.Count == 1,
                "Auto-hiding an active reader preserves paused playback and avoids resynthesis.");
            window.SetPaneAutoHide(pane, false); pane.Float(); await Settle();
            check(pane.IsFloating && fake.Paused && fake.Texts.Count == 1 && Control<TextBlock>(panel, "PositionText").Text == position,
                "Floating the reader retains its position, voice session and pause state.");
            pane.Dock(); await Settle();
            var current = (LayoutAnchorablePane)pane.Parent; current.Children.Remove(pane);
            var workspace = window.AvalonDockPreviewManager!.Layout.RootPanel.Children.OfType<LayoutPanel>()
                .Single(item => item.Descendents().OfType<LayoutDocument>().Any(document => document.ContentId == "editor"));
            workspace.Children.Insert(0, new LayoutAnchorablePane(pane) { DockWidth = new GridLength(280) });
            window.SetPaneAutoHide(pane, true); await Settle();
            var xml = window.CaptureNativeWorkspace();
            check(pane.IsAutoHidden && !(XElement.Parse(xml).Attribute("PdfCorrectoriumFixedLeft")?.Value ?? "").Split(',').Contains("read-aloud"),
                "Immediate auto-hide after a cross-side move clears the moved reader's former fixed exception.");
            check(window.AvalonDockPreviewManager.Layout.LeftSide.Descendents().OfType<LayoutAnchorable>().Any(item => item.ContentId == "read-aloud") && WorkspaceDockLayout.Normalize(xml) == xml,
                "The reader can be compactly auto-hidden on the left and serialized with all known panels.");
            check(window.RestoreNativeWorkspace(xml), "Native auto-hide workspace restores successfully."); await Settle();
            pane = window.PropertyPanes["read-aloud"];
            check(pane.IsAutoHidden && fake.Paused && fake.Texts.Count == 1 && ReferenceEquals(panel, window.DockedReadAloudPanel),
                "Restoring layout retains the exact playback controller without unloading its voice.");
            var properties = window.PropertyPanes["properties"];
            window.SetPaneAutoHide(properties, true); await Settle();
            vm.EditorMode = EditorInteractionMode.LinkEditing; vm.EditorMode = EditorInteractionMode.OcrEditing; await Settle();
            check(properties.IsAutoHidden && pane.IsAutoHidden, "Editing-mode switches do not unpin auto-hidden property or speech panels.");
            await picture((FrameworkElement)window.Content, "workspace-auto-hide", 1400, 850);
            await vm.FlushDisplaySettingsAsync();
            var savedSettings = vm.CurrentApplicationSettings;
            panel.StopPlayback(); await panel.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            var restart = new MainWindow(diagnosticDirectory: directory) { ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000,
                ClosePromptOverride = () => MessageBoxResult.No, ReadAloudPlaybackOverride = new DiagnosticSpeech() };
            try
            {
                restart.Show(); await Settle();
                check(restart.PropertyPanes["read-aloud"].IsAutoHidden && restart.PropertyPanes["properties"].IsAutoHidden &&
                    restart.AvalonDockPreviewManager!.Layout.LeftSide.Descendents().OfType<LayoutAnchorable>().Any(item => item.ContentId == "read-aloud"),
                    "A new app window restores left-side speech and property auto-hide preferences.");
            }
            finally { restart.Close(); await Settle(); }
            var legacy = XDocument.Parse(xml); legacy.Descendants().Where(element => (string?)element.Attribute("ContentId") == "read-aloud").Remove();
            check(window.RestoreNativeWorkspace(legacy.ToString()), "Legacy workspaces without a reader migrate without resetting other panels."); await Settle();
            check(window.PropertyPanes["read-aloud"].IsHidden && window.PropertyPanes["properties"].IsHidden == properties.IsHidden,
                "Legacy migration adds only a hidden reader and preserves existing property state.");
        }
        finally { window.Close(); await Settle(); }
    }
}
