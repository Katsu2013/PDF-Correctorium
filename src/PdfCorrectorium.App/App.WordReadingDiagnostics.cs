using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyWordReadingsAsync(MainWindow main, string output, Action<bool, string> check,
        Func<FrameworkElement, string, int, int, Task> picture)
    {
        var vm = (MainWindowViewModel)main.DataContext;
        var packages = new ProjectPackageService();
        var pdf = Path.Combine(output, "word-readings-source.pdf"); WriteDocumentUiTestPdf(pdf);
        var geometry = new TextGeometry { LocalBounds = new(new(25, 280), new(240, 24)), RotationCenter = new(145, 292), IsGeometryLocked = true };
        var row = new OcrTextRegion { OriginalText = "今日と日本語、今日。", OriginalGeometry = geometry, EditedGeometry = geometry };
        var vertical = row with { Id = Guid.NewGuid(), OriginalText = "日本語", WritingMode = WritingMode.Vertical,
            OriginalGeometry = geometry with { LocalBounds = new(new(230, 110), new(24, 100)) },
            EditedGeometry = geometry with { LocalBounds = new(new(230, 110), new(24, 100)) },
            WordReadings = [new() { SurfaceText = "日本語", ReadingText = "にほんご" }] };
        var page = new OcrPage { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [row, vertical], ReadingOrder = [row.Id, vertical.Id] };
        PdfExportService.PopulateReadingFixture(pdf, [page]);
        var input = Path.Combine(output, "word-readings-input.pdfocrproj");
        await packages.SaveAsync(input, new() { SourcePdf = await packages.CreateSourceReferenceAsync(pdf), Pages = [page] });
        vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
        await vm.LoadProjectForDiagnosticsAsync(input); await vm.EnsureDocumentMetadataAsync();
        vm.EditorModeIndex = 0; vm.EditUnitIndex = (int)OcrEditUnit.Character;
        vm.IsOcrOverlayVisible = true; vm.ShowOcrFrames = false; vm.ShowWordReadingRuby = true;
        var selected = vm.OverlayItems.First(item => item.Id == row.Id);
        var other = vm.OverlayItems.First(item => item.Id == vertical.Id);
        vm.SetOverlaySelection([selected], selected); selected.SelectCharacterRangeByTextOffset(0, 2);
        var before = selected.Capture();
        check(vm.WordReadingSurface == "今日" && vm.WordReadingPronunciation == "" && vm.CanEditWordReadings,
            "Contiguous character selection fills the surface and allows reading edits on a geometry-locked row.");
        var baseline = Path.Combine(output, "word-readings-baseline.pdf"); await vm.ExportPdfForDiagnosticsAsync(baseline);
        ((Expander)main.FindName("WordReadingExpander")).IsExpanded = true;
        var pronunciation = (TextBox)main.FindName("KeyboardField4");
        pronunciation.SetCurrentValue(TextBox.TextProperty, "きょう");
        pronunciation.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        check(vm.RegisterWordReadingCommand.CanExecute(null) && vm.WordReadingActionText == "登録", "The actual pronunciation input enables Register.");
        ((Button)main.FindName("RegisterWordReadingButton")).Command.Execute(null);
        check(selected.WordReadingDefinitions.Single().ReadingText == "きょう" && selected.WordReadingMatches.Count == 2 &&
              selected.Capture() with { WordReadingsText = before.WordReadingsText } == before && other.WordReadingDefinitions.Count == 1,
            "Register changes only this row's reading metadata, preserving text, bounds, locks and other rows.");
        check(vm.HasUnsavedChanges && !vm.RegisterWordReadingCommand.CanExecute(null), "Registration is undoable project data; unchanged entries cannot register twice.");
        var cached = selected.WordReadingMatches;
        for (var index = 0; index < 10000; index++)
            if (!ReferenceEquals(cached, selected.WordReadingMatches)) throw new InvalidOperationException("Reading matches were regenerated.");
        check(true, "Ten thousand unchanged preview queries reuse one row-local match snapshot.");
        vm.UndoCommand.Execute(null);
        check(selected.WordReadingDefinitions.Count == 0 && selected.WordReadingMatches.Count == 0, "Undo removes both speech reading and ruby matches.");
        vm.RedoCommand.Execute(null);
        check(selected.WordReadingMatches.Count == 2, "Redo restores all exact ruby occurrences.");
        cached = selected.WordReadingMatches; selected.Left += 1; selected.Left -= 1;
        check(vm.WordReadingPronunciation == "きょう" && ReferenceEquals(cached, selected.WordReadingMatches), "Geometry-only edits preserve reading drafts and cached matches.");
        vm.SelectedWordReading = selected.WordReadingDefinitions.Single(); vm.WordReadingPronunciation = "こんにち";
        check(vm.WordReadingActionText == "更新", "Selecting an existing entry offers Update.");
        vm.RegisterWordReadingCommand.Execute(null);
        check(selected.WordReadingDefinitions.Count == 1 && selected.WordReadingDefinitions[0].ReadingText == "こんにち", "Update replaces the selected surface without creating duplicate rules.");
        vm.RemoveWordReadingCommand.Execute(null);
        check(selected.WordReadingDefinitions.Count == 0, "Remove deletes a selected reading.");
        vm.UndoCommand.Execute(null);
        check(selected.WordReadingDefinitions.Single().ReadingText == "こんにち", "Removal supports Undo.");
        selected.SelectCharacter(0, false, false); selected.SelectCharacter(3, true, false);
        check(vm.WordReadingSurface == "", "Noncontiguous character selection does not invent a word.");
        selected.SelectCharacterRangeByTextOffset(0, 2);
        vm.WordReadingSurface = "不存在"; vm.WordReadingPronunciation = "test";
        check(vm.HasWordReadingInputMessage && !vm.RegisterWordReadingCommand.CanExecute(null), "A word absent from this row is rejected with visible feedback.");
        selected.SelectCharacterRangeByTextOffset(3, 3);
        vm.WordReadingPronunciation = "にほんご"; vm.RegisterWordReadingCommand.Execute(null);
        vm.SetOverlaySelection([selected, other], selected);
        check(!vm.CanEditWordReadings && !vm.RegisterWordReadingCommand.CanExecute(null), "Multi-row selection cannot accidentally register in only one row.");
        vm.SetOverlaySelection([selected], selected); selected.SelectCharacterRangeByTextOffset(0, 2);
        var dirty = vm.HasUnsavedChanges;
        vm.ShowWordReadingRuby = false;
        check(!vm.AreWordReadingRubiesVisible && vm.HasUnsavedChanges == dirty, "Ruby visibility is a display preference, not a project edit.");
        vm.ShowWordReadingRuby = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
        var layers = IconVisuals((DependencyObject)main.Content).OfType<OcrCharacterLayer>().Where(layer => layer.IsVisible).ToArray();
        check(layers.Any(layer => layer.LastDrawnRubyCount == 3) && layers.Any(layer => layer.IsVerticalWriting && layer.LastDrawnRubyCount == 1),
            "Actual horizontal and vertical editor layers draw ruby while OCR frames are off.");
        check(layers.All(layer => !layer.IsHitTestVisible && !layer.Focusable), "Ruby does not intercept text selection, drag or keyboard focus.");
        await picture((FrameworkElement)main.Content, "word-readings-editor", 1400, 850);
        ((Expander)main.FindName("WordReadingExpander")).BringIntoView();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await picture((FrameworkElement)main.Content, "word-readings-form", 1400, 850);
        var readingExpander = (Expander)main.FindName("WordReadingExpander"); readingExpander.IsExpanded = false;
        var body = IconVisuals((DependencyObject)main.Content).OfType<Border>().First(border => border.Name == "RegionBody" && ReferenceEquals(border.DataContext, selected));
        body.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "WordReading")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(readingExpander.IsExpanded && pronunciation.IsVisible, "The row's actual context-menu action reveals the pronunciation editor.");
        vm.ShowWordReadingRuby = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
        check(layers.All(layer => layer.LastDrawnRubyCount == 0), "The real UI toggle removes ruby immediately.");
        vm.ShowWordReadingRuby = true;
        var saved = Path.Combine(output, "word-readings-saved.pdfocrproj"); await vm.SaveProjectForDiagnosticsAsync(saved, ProjectPdfStorageMode.Embedded);
        var reopened = await packages.OpenAsync(saved);
        check(reopened.Pages[0].TextRegions[0].WordReadings.Count == 2 && reopened.Pages[0].TextRegions[0].EditedGeometry.IsGeometryLocked,
            "Actual project saving preserves readings and geometry locks using existing fields.");
        var exported = Path.Combine(output, "word-readings-export.pdf"); await vm.ExportPdfForDiagnosticsAsync(exported);
        var preview = new PdfPreviewService();
        static byte[] Pixels(BitmapSource image) { var stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8; var bytes = new byte[stride * image.PixelHeight]; image.CopyPixels(bytes, stride, 0); return bytes; }
        var originalPixels = Pixels((await preview.RenderPageAsync(baseline, 1, 600)).Image);
        var exportedPixels = Pixels((await preview.RenderPageAsync(exported, 1, 600)).Image);
        check(originalPixels.SequenceEqual(exportedPixels), "PDFium renders the actual exported PDF identically before and after reading metadata: no ruby is exported.");
        await using (var worker = new PdfNativeWorkerClient(reuseInputs: true))
        {
            var native = await worker.ReadSpeechPageAsync(exported, 1, default);
            check(native.Regions.All(region => !region.Text.Contains("こんにち", StringComparison.Ordinal) && !region.Text.Contains("にほんご", StringComparison.Ordinal)), "Exported PDF text contains no pronunciation glyphs.");
        }
        await vm.LoadProjectForDiagnosticsAsync(saved); await vm.EnsureDocumentMetadataAsync();
        check(vm.OverlayItems.First(item => item.Id == row.Id).WordReadingMatches.Count == 3, "Reopening restores screen ruby from existing project readings.");
        VerifyRubyDrawing(output, check);
    }

    private static void VerifyRubyDrawing(string output, Action<bool, string> check)
    {
        var region = new OverlayRegionViewModel(new("今日😀", 0, 0, 150, 30, true)) { WordReadingsText = "今日=きょう\n😀=えがお" };
        var layer = new OcrCharacterLayer { Cells = region.CharacterCells, Readings = region.WordReadingMatches, Width = 150, Height = 30,
            ShowFrames = false, TextBrush = Brushes.Red, PageLeft = 30, PageTop = 30, PageWidth = 240 };
        var canvas = new Canvas { Width = 240, Height = 180, Background = Brushes.White };
        Canvas.SetLeft(layer, 30); Canvas.SetTop(layer, 50); canvas.Children.Add(layer);
        byte[] Render(string? name = null)
        {
            canvas.Measure(new Size(240, 180)); canvas.Arrange(new Rect(0, 0, 240, 180)); canvas.UpdateLayout();
            var image = new RenderTargetBitmap(240, 180, 96, 96, PixelFormats.Pbgra32); image.Render(canvas);
            if (name is not null) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream); }
            var pixels = new byte[240 * 180 * 4]; image.CopyPixels(pixels, 240 * 4, 0); return pixels;
        }
        var on = Render("ruby-horizontal");
        check(layer.LastDrawnRubyCount == 2 && VisualTreeHelper.GetChildrenCount(layer) == 0, "Ruby uses drawing calls, with no per-word or per-character controls.");
        layer.ShowReadings = false; var off = Render();
        check(layer.LastDrawnRubyCount == 0 && !on.SequenceEqual(off), "Ruby toggle changes real rendered pixels independently of frame visibility.");
        layer.ShowReadings = true; layer.InverseZoom = .5; var zoomed = Render();
        check(layer.LastDrawnRubyCount == 2 && !on.SequenceEqual(zoomed), "Zoom invalidates ruby drawing without altering source geometry.");
        layer.RenderTransform = new RotateTransform(90, 75, 15); Render("ruby-rotation");
        check(layer.LastDrawnRubyCount == 2, "Ruby rotates together with its row without new controls.");
        layer.RenderTransform = Transform.Identity;
        region.IsVertical = true; region.Width = 30; region.Height = 110;
        layer.Cells = region.CharacterCells; layer.Width = 30; layer.Height = 110; layer.IsVerticalWriting = true;
        Render("ruby-vertical"); check(layer.LastDrawnRubyCount == 2, "Vertical ruby handles Japanese and surrogate-pair surfaces.");
        region.Text = "別の行"; layer.Cells = region.CharacterCells; layer.Readings = region.WordReadingMatches;
        Render(); check(layer.LastDrawnRubyCount == 0, "Changed row text invalidates stale pronunciation matches.");
    }
}
