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
using PdfCorrectorium.App.Services;
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
                "RedactionMenuItem", "OcrEditToolbarButton", "RedactionToolbarButton", "EditorModeSelector",
                "RedactionInputModeComboBox",
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
                viewModel.ActivateOcrEditModeCommand, viewModel.ActivateRedactionModeCommand,
                viewModel.AddSelectedRedactionsCommand,
                viewModel.DeleteSelectedRedactionCommand, viewModel.ClearCurrentPageRedactionsCommand,
            ];
            await LayoutAsync();
            Check(!viewModel.HasDocument && !viewModel.CanUsePreview, "Startup has no document or usable preview.");
            Check(Control("ProjectStorageModeStatusItem").Visibility == Visibility.Collapsed,
                "Startup hides project PDF storage information from the status bar.");
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
            var readingOrderRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("reading order", 20, 20, 120, 30, true))
            {
                ReadingOrder = 1,
            };
            viewModel.OverlayItems.Add(readingOrderRegion);
            viewModel.EditorModeIndex = 1;
            var overlayCanvas = (ListBox)Control("OverlayCanvas");
            overlayCanvas.SelectedItems.Add(readingOrderRegion);
            await LayoutAsync();
            var readingOrderBadgeLayer = (ItemsControl)Control("ReadingOrderBadgeLayer");
            Check(readingOrderBadgeLayer.Visibility == Visibility.Visible && !readingOrderBadgeLayer.IsHitTestVisible,
                "Reading-order badges use a visible, non-interactive foreground layer in reading-order mode.");
            Check(Panel.GetZIndex(readingOrderBadgeLayer) > Panel.GetZIndex(Control("OverlayCanvas")),
                "Reading-order badges are stacked above OCR selection borders and resize handles.");
            static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                {
                    var child = VisualTreeHelper.GetChild(parent, index);
                    yield return child;
                    foreach (var descendant in Descendants(child)) yield return descendant;
                }
            }
            static bool RectClose(Rect first, Rect second) =>
                Math.Abs(first.Left - second.Left) < 0.01 && Math.Abs(first.Top - second.Top) < 0.01 &&
                Math.Abs(first.Width - second.Width) < 0.01 && Math.Abs(first.Height - second.Height) < 0.01;
            var selectedRegionContainer = overlayCanvas.ItemContainerGenerator.ContainerFromItem(readingOrderRegion) as ListBoxItem;
            Check(selectedRegionContainer is not null && Descendants(selectedRegionContainer).OfType<Thumb>()
                    .Any(thumb => Equals(thumb.Tag, "NW") && thumb.Visibility == Visibility.Visible),
                "The foreground-layer check includes a selected region with its upper-left resize handle visible.");
            var readingOrderBadgeContainer = readingOrderBadgeLayer.ItemContainerGenerator.ContainerFromItem(readingOrderRegion) as ContentPresenter;
            Check(readingOrderBadgeContainer is not null &&
                  readingOrderBadgeContainer.ContentTemplate.FindName("ReadingOrderBadge", readingOrderBadgeContainer) is Border
                  {
                      Background: SolidColorBrush { Color.A: 255 },
                      BorderBrush: SolidColorBrush { Color: var badgeBorderColor },
                      BorderThickness: { Left: > 0 }
                  } && badgeBorderColor == Colors.White,
                "Reading-order numbers have an opaque badge and contrasting outline above the selection frame.");
            Snapshot("reading-order-badges");
            viewModel.EditorModeIndex = 0;
            viewModel.OverlayItems.Clear();
            await LayoutAsync();
            Check(viewModel.IsOcrEditMode && !viewModel.IsRedactionMode && viewModel.CanEditGeometry,
                "OCR editing starts as the exclusive geometry-editing mode.");
            Check(viewModel.ActivateRedactionModeCommand.CanExecute(null),
                "A loaded document enables switching to redaction mode.");
            viewModel.ActivateRedactionModeCommand.Execute(null);
            await LayoutAsync();
            var modeGuardRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("mode guard", 10, 10, 60, 20, true));
            viewModel.OverlayItems.Add(modeGuardRegion);
            viewModel.SetOverlaySelection([modeGuardRegion], modeGuardRegion);
            Check(viewModel.IsRedactionMode && !viewModel.IsOcrEditMode,
                "Redaction mode is exclusive from OCR editing mode.");
            Check(!viewModel.CanEditGeometry && !viewModel.CanAddOcrRegion &&
                  !viewModel.DeleteOcrRegionsCommand.CanExecute(null),
                "Redaction mode disables OCR geometry editing and OCR-region creation/deletion.");
            Check(((ToggleButton)Control("RedactionToolbarButton")).IsChecked == true &&
                  ((ToggleButton)Control("OcrEditToolbarButton")).IsChecked != true &&
                  ((ComboBox)Control("EditorModeSelector")).SelectedIndex == (int)EditorInteractionMode.Redaction,
                "Toolbar buttons and the editor-mode selector show the same active redaction mode.");
            Check(Control("RedactionPanel").Visibility == Visibility.Visible &&
                  ((TextBlock)Control("PropertiesPaneTitle")).Text == LocalizationService.Translate("墨消しプロパティ"),
                "Redaction mode shows its dedicated property controls.");
            var redactionInputModes = (ComboBox)Control("RedactionInputModeComboBox");
            Check(redactionInputModes.Items.Count == 4 && redactionInputModes.SelectedIndex == 0,
                "Redaction mode offers rectangle, PDF text, polygon and freehand input with rectangle as the default.");
            redactionInputModes.SelectedIndex = (int)RedactionInputMode.TextSelection;
            await LayoutAsync();
            Check(((ListBox)Control("OverlayCanvas")).Cursor == Cursors.IBeam,
                "PDF text redaction uses an I-beam cursor instead of the area-selection crosshair.");
            redactionInputModes.SelectedIndex = (int)RedactionInputMode.Rectangle;
            await LayoutAsync();
            var sampleBitmap = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
            sampleBitmap.WritePixels(new Int32Rect(0, 0, 2, 2), new byte[]
            {
                0x33, 0x22, 0x11, 0xFF, 0x66, 0x55, 0x44, 0xFF,
                0x99, 0x88, 0x77, 0xFF, 0xCC, 0xBB, 0xAA, 0xFF,
            }, 8, 0);
            Check(PdfCorrectorium.App.MainWindow.TrySamplePreviewColor(sampleBitmap, new Point(75, 25), new Size(100, 100)) == "#445566" &&
                  PdfCorrectorium.App.MainWindow.TrySamplePreviewColor(sampleBitmap, new Point(100, 25), new Size(100, 100)) is null,
                "The eyedropper maps preview coordinates to the underlying page pixel and rejects clicks outside the page.");
            Check(PdfCorrectorium.App.MainWindow.CalculateUnselectedWheelOffset(100, -120, 3, 400, 1000) == 148 &&
                  PdfCorrectorium.App.MainWindow.CalculateUnselectedWheelOffset(20, 120, 3, 400, 1000) == 0 &&
                  PdfCorrectorium.App.MainWindow.CalculateUnselectedWheelOffset(900, -120, -1, 300, 1000) == 1000,
                "Unselected wheel scrolling follows system line/page settings and remains inside the workspace extent.");
            var redaction = viewModel.AddManualRedaction(new Rect(24, 30, 80, 35));
            await LayoutAsync();
            var redactionLayer = (ItemsControl)Control("RedactionOverlayLayer");
            Check(redaction is not null && viewModel.CurrentPageRedactionCount == 1 &&
                  redactionLayer.Items.Count == 1 && redactionLayer.IsHitTestVisible,
                "A manual redaction appears in the interactive foreground layer while redaction mode is active.");
            Check(viewModel.IsRedactionMode,
                "Redaction mode remains active after one mark so consecutive areas can be specified.");
            var redactionOverlay = viewModel.RedactionItems.Single();
            var redactionContainer = redactionLayer.ItemContainerGenerator.ContainerFromItem(redactionOverlay) as ContentPresenter;
            Check(redactionOverlay.IsSelected && redactionContainer is not null &&
                  Descendants(redactionContainer).OfType<Thumb>().Count(thumb => thumb.Visibility == Visibility.Visible) == 9,
                "A selected redaction exposes one move surface and eight resize handles.");
            var redactionPreviewBorder = redactionContainer is null
                ? null
                : redactionContainer.ContentTemplate.FindName("RedactionPreviewBorder", redactionContainer) as Border;
            var redactionMoveThumb = redactionContainer is null
                ? null
                : Descendants(redactionContainer).OfType<Thumb>().FirstOrDefault(thumb => Equals(thumb.Tag, "Move"));
            redactionMoveThumb?.ApplyTemplate();
            var redactionMoveSurface = redactionMoveThumb is not null && VisualTreeHelper.GetChildrenCount(redactionMoveThumb) > 0
                ? VisualTreeHelper.GetChild(redactionMoveThumb, 0) as Border
                : null;
            Check(redactionPreviewBorder is
                  {
                      Background: SolidColorBrush { Color: var initialRedactionColor },
                      Opacity: 0.62
                  } && initialRedactionColor == Colors.Black &&
                  redactionMoveSurface?.Background is SolidColorBrush { Color.A: 0 },
                "A black redaction uses its selected fill color, while the move surface remains visually transparent instead of painting white.");
            Check(viewModel.IsRedactionPreviewTranslucent && !viewModel.IsRedactionPreviewOpaque &&
                  ((RadioButton)Control("RedactionPreviewTranslucentRadioButton")).IsChecked == true,
                "Redaction previews start in translucent editor mode so the covered content can still be inspected.");
            viewModel.IsRedactionPreviewOpaque = true;
            await LayoutAsync();
            Check(viewModel.RedactionPreviewOpacity == 1d && redactionPreviewBorder?.Opacity == 1d &&
                  ((RadioButton)Control("RedactionPreviewOpaqueRadioButton")).IsChecked == true,
                "The editor preview can show the selected redaction color fully opaque without changing PDF output semantics.");
            viewModel.IsRedactionPreviewTranslucent = true;
            await LayoutAsync();
            var undoBeforeRedactionColor = viewModel.UndoCountForDiagnostics;
            viewModel.SetRedactionColor("#D32F2F");
            Check(viewModel.ProjectForDiagnostics!.Redactions.Single().ColorHex == "#D32F2F" &&
                  viewModel.RedactionItems.Single().ColorHex == "#D32F2F" &&
                  viewModel.UndoCountForDiagnostics == undoBeforeRedactionColor + 1,
                "Changing the color with an existing redaction selected updates that mark and creates one Undo entry.");
            viewModel.UndoCommand.Execute(null);
            Check(viewModel.ProjectForDiagnostics.Redactions.Single().ColorHex == "#000000" &&
                  viewModel.RedactionItems.Single().ColorHex == "#000000",
                "Undo restores the selected redaction's previous color.");
            Check(PdfCorrectorium.App.MainWindow.CalculateRedactionTransform(new Rect(20, 20, 80, 40), "Move", new Vector(-50, 500), 200, 150) ==
                  new Rect(0, 110, 80, 40),
                "Moving a redaction remains inside the page without changing its size.");
            Check(PdfCorrectorium.App.MainWindow.CalculateRedactionTransform(new Rect(20, 20, 80, 40), "NW", new Vector(15, 10), 200, 150) ==
                  new Rect(35, 30, 65, 30) &&
                  PdfCorrectorium.App.MainWindow.CalculateRedactionTransform(new Rect(20, 20, 80, 40), "SE", new Vector(-500, -500), 200, 150) ==
                  new Rect(20, 20, 8, 8),
                "Corner handles resize both axes and enforce the minimum redaction size.");
            Snapshot("redaction-mode");
            Check(Panel.GetZIndex(redactionLayer) > Panel.GetZIndex(Control("OverlayCanvas")) &&
                  Panel.GetZIndex(redactionLayer) < Panel.GetZIndex(Control("ReadingOrderBadgeLayer")),
                "Redaction previews are above OCR frames while reading-order badges remain foremost.");
            var originalRedactionBounds = redactionOverlay.Bounds;
            var originalPdfBounds = viewModel.ProjectForDiagnostics!.Redactions.Single().Bounds;
            var undoBeforeRedactionTransform = viewModel.UndoCountForDiagnostics;
            var adjustedRedactionBounds = new Rect(
                originalRedactionBounds.Left + 12,
                originalRedactionBounds.Top + 8,
                originalRedactionBounds.Width + 18,
                originalRedactionBounds.Height + 11);
            Check(viewModel.PreviewRedactionBounds(redactionOverlay.Id, adjustedRedactionBounds) &&
                  RectClose(viewModel.RedactionItems.Single().Bounds, adjustedRedactionBounds) &&
                  viewModel.ProjectForDiagnostics.Redactions.Single().Bounds == originalPdfBounds &&
                  viewModel.UndoCountForDiagnostics == undoBeforeRedactionTransform,
                "Dragging previews a redaction transform without rewriting the project or growing Undo history per pointer event.");
            Check(viewModel.CommitRedactionBounds(redactionOverlay.Id, originalRedactionBounds) &&
                  viewModel.ProjectForDiagnostics.Redactions.Single().Bounds != originalPdfBounds &&
                  viewModel.UndoCountForDiagnostics == undoBeforeRedactionTransform + 1,
                "Completing a redaction move or resize commits one project edit and one Undo entry.");
            viewModel.UndoCommand.Execute(null);
            Check(viewModel.CurrentPageRedactionCount == 1 && RectClose(viewModel.RedactionItems.Single().Bounds, originalRedactionBounds),
                "Undo restores the redaction's original position and size.");
            viewModel.UndoCommand.Execute(null);
            Check(viewModel.CurrentPageRedactionCount == 0, "A second Undo removes the newly added redaction.");
            viewModel.RedoCommand.Execute(null);
            Check(viewModel.CurrentPageRedactionCount == 1, "Redo restores a newly added redaction.");
            viewModel.RedoCommand.Execute(null);
            Check(RectClose(viewModel.RedactionItems.Single().Bounds, adjustedRedactionBounds),
                "A second Redo restores the adjusted redaction position and size.");
            viewModel.SelectedRedaction = viewModel.RedactionItems.Single();
            Check(viewModel.DeleteSelectedRedactionCommand.CanExecute(null),
                "Selecting an existing redaction enables its delete command.");
            Check(PdfCorrectorium.App.MainWindow.ShouldDeleteSelectedRedaction(Key.Delete, true, true, new Border()) &&
                  !PdfCorrectorium.App.MainWindow.ShouldDeleteSelectedRedaction(Key.Delete, true, true, new TextBox()) &&
                  !PdfCorrectorium.App.MainWindow.ShouldDeleteSelectedRedaction(Key.Back, true, true, new Border()),
                "Delete targets a selected redaction from the work area but preserves normal text-entry deletion.");
            viewModel.DeleteSelectedRedactionCommand.Execute(null);
            Check(viewModel.CurrentPageRedactionCount == 0 && viewModel.ProjectForDiagnostics.Redactions.Count == 0,
                "Delete removes the selected redaction preview and project entry.");
            viewModel.UndoCommand.Execute(null);
            Check(viewModel.CurrentPageRedactionCount == 1 && viewModel.ProjectForDiagnostics.Redactions.Count == 1,
                "Undo restores a deleted redaction.");
            viewModel.ActivateOcrEditModeCommand.Execute(null);
            await LayoutAsync();
            Check(viewModel.IsOcrEditMode && !viewModel.IsRedactionMode && viewModel.CanEditGeometry &&
                  ((ToggleButton)Control("OcrEditToolbarButton")).IsChecked == true &&
                   ((ToggleButton)Control("RedactionToolbarButton")).IsChecked != true,
                "Returning to OCR editing restores geometry editing and synchronized toolbar state.");
            Check(!redactionLayer.IsHitTestVisible,
                "Redaction handles stop intercepting the preview after returning to OCR editing.");
            Check(viewModel.ProjectStorageModeStatusText.Contains(viewModel.ProjectStorageModeText, StringComparison.Ordinal),
                "The project PDF storage status contains the current project mode name.");
            Check(viewModel.ProjectStorageModeText == LocalizationService.Translate("ポータブルモード"),
                "Embedded project storage uses the Portable mode display name.");
            Check(Control("ProjectStorageModeStatusItem").Visibility == Visibility.Visible &&
                  Equals(((StatusBarItem)Control("ProjectStorageModeStatusItem")).Content, viewModel.ProjectStorageModeStatusText),
                "The status bar identifies the current project PDF storage method.");
            var propertiesWindow = new DocumentPropertiesWindow(viewModel);
            Check(propertiesWindow.ProjectStorageModeText == viewModel.ProjectStorageModeText,
                "Document properties reports project PDF storage rather than application data storage.");
            Check(((ComboBox)propertiesWindow.FindName("PageModeComboBox")).Items
                    .OfType<ComboBoxItem>()
                    .Any(item => string.Equals(item.Tag?.ToString(), InitialPageMode.ContinuousFacingPages.ToString(), StringComparison.Ordinal)),
                "Document properties offers continuous facing pages as an exported-PDF initial view.");
            propertiesWindow.Close();
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
            await viewModel.SaveProjectForDiagnosticsAsync(projectPath, ProjectPdfStorageMode.Relative);
            await LayoutAsync();
            Check(viewModel.ProjectForDiagnostics?.PdfStorageMode == ProjectPdfStorageMode.Relative &&
                  Equals(((StatusBarItem)Control("ProjectStorageModeStatusItem")).Content, viewModel.ProjectStorageModeStatusText),
                "Changing the project PDF storage method updates the properties source and status bar.");
            Check(viewModel.ProjectStorageModeText == LocalizationService.Translate("通常モード"),
                "Relative project storage uses the Normal mode display name.");
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

    private static void WriteDocumentUiTestPdf(
        string path,
        int pageCount = 2,
        string? pageLayout = null,
        string? direction = null)
    {
        if (pageCount < 1) throw new ArgumentOutOfRangeException(nameof(pageCount));
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        var pageReferences = string.Join(' ', Enumerable.Range(3, pageCount).Select(number => $"{number} 0 R"));
        var catalogOptions = string.Concat(
            string.IsNullOrWhiteSpace(pageLayout) ? string.Empty : $" /PageLayout {pageLayout}",
            string.IsNullOrWhiteSpace(direction) ? string.Empty : $" /ViewerPreferences << /Direction {direction} >>");
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R{catalogOptions} >>",
            $"<< /Type /Pages /Kids [{pageReferences}] /Count {pageCount} >>",
        };
        objects.AddRange(Enumerable.Repeat(
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << >> >>",
            pageCount));
        for (var index = 0; index < objects.Count; index++)
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
