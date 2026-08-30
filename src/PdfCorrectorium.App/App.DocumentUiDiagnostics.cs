using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>文書未読込／読込済みの実画面と倍率スライダーを、ウィンドウを表示せず検証します。</summary>
    private async void RunDocumentUiTest(MainWindow window, string[] arguments, int optionIndex)
    {
        try
        {
            if (arguments.Length <= optionIndex + 1)
                throw new ArgumentException("--document-ui-test requires a new output directory.");
            var directory = Path.GetFullPath(arguments[optionIndex + 1]);
            if (Directory.Exists(directory))
                throw new IOException("Use a new output directory to avoid overwriting test results.");
            Directory.CreateDirectory(directory);
            var viewModel = (MainWindowViewModel)window.DataContext;
            var content = (FrameworkElement)window.Content;
            var checks = new List<string>();
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add(message);
            }
            FrameworkElement Control(string name) =>
                window.FindName(name) as FrameworkElement ?? throw new InvalidOperationException($"Missing control: {name}");
            Check(window.Title == PdfCorrectorium.Core.ApplicationBuildInfo.WindowTitle,
                "Window title contains the current development revision.");
            Check(PdfCorrectorium.Core.ApplicationBuildInfo.AboutText.Contains(
                typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion),
                "About text contains the executable product version.");
            Check(PdfCorrectorium.Core.ApplicationBuildInfo.AboutText.Contains(
                typeof(MainWindow).Assembly.GetName().Version!.ToString(4)),
                "About text contains all four numeric build components.");
            async Task LayoutAsync()
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                content.Measure(new Size(1400, 850));
                content.Arrange(new Rect(0, 0, 1400, 850));
                content.UpdateLayout();
            }
            void Snapshot(string name)
            {
                var bitmap = new RenderTargetBitmap(1400, 850, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, name + ".png"));
                encoder.Save(stream);
            }
            void SnapshotZoom(string name)
            {
                var zoomBar = (FrameworkElement)Control("StatusZoomSlider").Parent;
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                    drawing.DrawRectangle(new VisualBrush(zoomBar), null, new Rect(0, 0, zoomBar.ActualWidth, zoomBar.ActualHeight));
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(zoomBar.ActualWidth * 3), (int)Math.Ceiling(zoomBar.ActualHeight * 3), 288, 288, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, name + ".png"));
                encoder.Save(stream);
            }
            var documentControls = new[]
            {
                "DocumentPropertiesMenuItem", "FindOcrTextMenuItem", "ReplaceOcrTextMenuItem",
                "BookmarkMenu", "OcrMenu", "PageMenu", "ValidationMenu", "ToolbarPageNumberBox",
                "EditUnitSelector", "StatusZoomSlider", "StatusZoomComboBox",
                "StatusZoomOutButton", "StatusZoomInButton",
            };
            ICommand[] documentCommands =
            [
                viewModel.SaveProjectCommand, viewModel.SaveProjectAsCommand, viewModel.ExportPdfCommand,
                viewModel.ImportOcrDataCommand, viewModel.InsertPagesCommand, viewModel.DeletePagesCommand,
                viewModel.RotatePagesLeftCommand, viewModel.RotatePagesRightCommand,
                viewModel.OptimizeCurrentPageImageCommand, viewModel.OptimizeDocumentImagesCommand,
                viewModel.PreviousPageCommand, viewModel.NextPageCommand,
                viewModel.ZoomInCommand, viewModel.ZoomOutCommand, viewModel.ActualSizeCommand,
                viewModel.ToggleAddOcrRegionModeCommand, viewModel.AddBookmarkCommand,
                viewModel.AddChildBookmarkCommand, viewModel.DeleteBookmarkCommand,
                viewModel.ImportBookmarksCommand, viewModel.ExportBookmarksCommand,
            ];
            await LayoutAsync();
            Check(!viewModel.HasDocument && !viewModel.CanUsePreview, "Startup has no document or usable preview.");
            foreach (var name in documentControls)
                Check(!Control(name).IsEnabled, $"Startup disables {name}.");
            Check(documentCommands.All(command => !command.CanExecute(null)), "Startup disables all document commands.");
            foreach (var name in new[] { "ApplicationSettingsMenuItem", "ViewMenu", "HelpMenu" })
                Check(Control(name).IsEnabled, $"Startup keeps {name} available.");
            Check(viewModel.OpenPdfCommand.CanExecute(null) && viewModel.OpenProjectCommand.CanExecute(null), "Open commands remain available.");
            Check(!Control("RestoreProjectMenuItem").IsEnabled, "Backup restore is disabled before a project is saved.");
            var originalZoom = viewModel.ZoomPercent;
            viewModel.ZoomInCommand.Execute(null);
            viewModel.ActualSizeCommand.Execute(null);
            viewModel.ToggleAddOcrRegionModeCommand.Execute(null);
            Check(viewModel.ZoomPercent == originalZoom && !viewModel.IsAddOcrRegionMode, "Disabled commands cannot be executed directly.");
            foreach (var method in new[] { "DocumentPropertiesMenuItem_OnClick", "ValidateProjectMenuItem_OnClick", "RestoreProjectMenuItem_OnClick", "OcrQualityAnalysisMenuItem_OnClick" })
                typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [window, new RoutedEventArgs()]);
            var search = typeof(MainWindow).GetMethod("ShowOcrSearchReplaceWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            search.Invoke(window, [false]);
            search.Invoke(window, [true]);
            Check(window.OwnedWindows.Count == 0, "Dialog and search-shortcut entry points do not open empty-document windows.");
            Snapshot("no-document");

            try { await viewModel.LoadPdfForDiagnosticsAsync(Path.Combine(directory, "missing.pdf")); }
            catch (FileNotFoundException) { }
            Check(!viewModel.HasDocument && !viewModel.SaveProjectAsCommand.CanExecute(null), "A failed open does not enable document operations.");

            var pdfPath = Path.Combine(directory, "two-pages.pdf");
            WriteDocumentUiTestPdf(pdfPath);
            var saveAsNotifications = 0;
            viewModel.SaveProjectAsCommand.CanExecuteChanged += (_, _) => saveAsNotifications++;
            await viewModel.LoadPdfForDiagnosticsAsync(pdfPath);
            await LayoutAsync();
            Check(viewModel.HasDocument && viewModel.CanUsePreview && viewModel.PageItems.Count == 2, "PDF load produces a usable two-page document.");
            foreach (var name in documentControls)
                Check(Control(name).IsEnabled, $"PDF load enables {name}.");
            Check(saveAsNotifications > 0 && viewModel.SaveProjectAsCommand.CanExecute(null), "Save As notifies the UI when a document is loaded.");
            Check(!viewModel.PreviousPageCommand.CanExecute(null) && viewModel.NextPageCommand.CanExecute(null), "First page has correct navigation availability.");
            viewModel.SelectedPage = viewModel.PageItems[1];
            await viewModel.RenderPageForDiagnosticsAsync(2);
            Check(viewModel.PreviousPageCommand.CanExecute(null) && !viewModel.NextPageCommand.CanExecute(null), "Last page has correct navigation availability.");

            var slider = (Slider)Control("StatusZoomSlider");
            foreach (var zoom in new[] { 25d, 100d, 400d })
            {
                viewModel.ZoomPercent = zoom;
                await LayoutAsync();
                var track = (Track)slider.Template.FindName("PART_Track", slider);
                var background = (Border)slider.Template.FindName("SliderTrackBackground", slider);
                Check(track.DecreaseRepeatButton.Background is SolidColorBrush { Color.A: 0 } &&
                      track.IncreaseRepeatButton.Background is SolidColorBrush { Color.A: 0 } &&
                      background.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(221, 225, 230),
                    $"Zoom {zoom}% uses one neutral track color on both sides.");
                Check(viewModel.ZoomInCommand.CanExecute(null) == (zoom < 400) &&
                      viewModel.ZoomOutCommand.CanExecute(null) == (zoom > 25), $"Zoom {zoom}% observes its limits.");
            }
            viewModel.ActualSizeCommand.Execute(null);
            Check(viewModel.ZoomPercent == 100, "Actual-size command works after loading.");
            await LayoutAsync();
            await VerifyCompactChromeAsync(window, LayoutAsync, Check);
            Snapshot("loaded-document");
            await VerifyZoomSynchronizationAsync(window, LayoutAsync, Check);
            slider.SetCurrentValue(RangeBase.ValueProperty, EditorInteractionMath.ZoomPercentToSliderPosition(137));
            await LayoutAsync();
            Snapshot("zoom-synchronized");
            SnapshotZoom("zoom-detail-137");
            viewModel.ActualSizeCommand.Execute(null);
            await LayoutAsync();
            Snapshot("zoom-centered-100");
            SnapshotZoom("zoom-detail-100");

            var projectPath = Path.Combine(directory, "saved-project" + ProjectPackageService.ProjectExtension);
            await viewModel.SaveProjectForDiagnosticsAsync(projectPath);
            await LayoutAsync();
            Check(viewModel.CanRestoreProjectBackup && Control("RestoreProjectMenuItem").IsEnabled, "Saving a project enables backup restoration.");
            await viewModel.LoadProjectForDiagnosticsAsync(projectPath);
            Check(viewModel.HasDocument && viewModel.SaveProjectAsCommand.CanExecute(null), "Reopening a saved project enables document operations.");

            var package = new ProjectPackageService();
            var invalidProjectPath = Path.Combine(directory, "missing-source" + ProjectPackageService.ProjectExtension);
            var missingSource = await package.CreateSourceReferenceAsync(pdfPath);
            await package.SaveAsync(invalidProjectPath, new PdfCorrectoriumProject
            {
                Name = "Missing source test",
                SourcePdf = missingSource with { FileName = "absent.pdf", AbsolutePathHint = Path.Combine(directory, "absent.pdf"), RelativePath = "absent.pdf" },
            });
            try { await viewModel.LoadProjectForDiagnosticsAsync(invalidProjectPath); }
            catch (InvalidDataException) { }
            await LayoutAsync();
            Check(viewModel.HasDocument && Control("DocumentPropertiesMenuItem").IsEnabled &&
                  viewModel.SaveProjectCommand.CanExecute(null) && viewModel.ProjectPath == projectPath,
                "A failed replacement preserves the already loaded document and its available operations.");
            await viewModel.LoadPdfForDiagnosticsAsync(pdfPath);
            Check(viewModel.HasDocument && !viewModel.CanRestoreProjectBackup, "A subsequent valid PDF open restores operations and resets backup availability.");

            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks.Select(check => "PASS: " + check));
            _diagnostics?.Write("document-ui-test.passed", $"{checks.Count} checks passed. {directory}");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            _diagnostics?.Write("document-ui-test.failed", exception.ToString());
            Shutdown(1);
        }
    }

    private static void WriteDocumentUiTestPdf(string path)
    {
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << >> >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << >> >>",
        ];
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
}
