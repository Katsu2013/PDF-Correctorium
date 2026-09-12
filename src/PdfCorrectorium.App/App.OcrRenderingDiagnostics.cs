using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>Checks cache invalidation, renderer equivalence, real edit chrome and dense-page costs.</summary>
    private async Task RunOcrRenderingTestAsync(MainWindow window, string[] arguments, int optionIndex)
    {
        string? directory = null;
        try
        {
            if (arguments.Length <= optionIndex + 1) throw new ArgumentException("A new diagnostics directory is required.");
            directory = Path.GetFullPath(arguments[optionIndex + 1]);
            if (Directory.Exists(directory)) throw new IOException("Use a new diagnostics directory.");
            Directory.CreateDirectory(directory);
            var checks = new List<string>();
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add(message);
            }
            static OverlayRegionViewModel Region(string text, double width = 600, double height = 40) =>
                new(new PdfTextOverlayRegion(text, 0, 0, width, height, true));

            var region = Region("Ae\u0301👩‍💻日本語");
            Check(region.TextElementCount == 6, "Unicode combining marks and ZWJ emoji remain complete text elements.");
            var cells = region.CharacterCells;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 10000; index++)
                if (!ReferenceEquals(cells, region.CharacterCells) || region.TextElementCount != 6)
                    throw new InvalidOperationException("Unchanged cell data was regenerated.");
            var cacheReadBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(cacheReadBytes < 1024, "10,000 unchanged cell/count reads allocate less than 1 KB.");
            Check(cells is not CharacterOverlayCell[], "Callers cannot mutate the cached cell array.");
            region.SelectedCharacterIndex = 2;
            Check(region.CharacterCells[2].IsSelected && !cells[2].IsSelected, "Selection invalidates cells without changing an earlier snapshot.");
            Check(region.FindCharacterIndexAt(250, 10) == 2, "Hit testing uses the same Unicode cell boundaries as rendering.");
            region.Width = 720;
            Check(Math.Abs(region.CharacterCells.Sum(cell => cell.Width) - 720) < 0.001, "Width edits rebuild geometry.");
            region.IsVertical = true;
            Check(region.CharacterCells[1].Top > 0 && region.CharacterCells.All(cell => cell.Left == 0), "Vertical mode rebuilds the cell axis.");
            var snapshot = region.Capture();
            region.Text = "短文";
            Check(region.CharacterCells.Count == 2, "Text replacement invalidates segmentation and cells.");
            region.Apply(snapshot);
            Check(region.TextElementCount == 6 && region.CharacterCells.Count == 6, "Snapshot restore rebuilds the previous Unicode cells.");
            region.SetSearchHighlightByTextOffset(3, 5);
            Check(region.CharacterCells[2].IsSearchMatch, "Search highlights invalidate the cell snapshot.");
            region.ClearSearchHighlight();
            Check(region.CharacterCells.All(cell => !cell.IsSearchMatch), "Clearing search invalidates its display state.");
            region.SelectedCharacterIndex = 2;
            region.SetSelectedCharacterLocks(true);
            Check(region.CharacterCells[2].IsLocked, "Lock changes reach the cached cells.");

            // A control-only baseline reproduces the former per-cell Border/Viewbox/TextBlock
            // without its binding/trigger overhead, making the cost comparison conservative.
            static Canvas LegacyLayer(OverlayRegionViewModel item)
            {
                var panel = new Canvas { Width = item.Width, Height = item.Height };
                foreach (var cell in item.CharacterCells)
                {
                    var border = new Border
                    {
                        Width = cell.Width,
                        Height = cell.Height,
                        BorderThickness = new Thickness(1),
                        Child = new Viewbox
                        {
                            Stretch = Stretch.Fill,
                            Margin = new Thickness(1),
                            Child = new TextBlock
                            {
                                Text = cell.Text,
                                FontFamily = new FontFamily("Segoe UI"),
                                FontSize = 100,
                                LineHeight = 100,
                                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = Brushes.DarkRed,
                            },
                        },
                    };
                    Canvas.SetLeft(border, cell.Left);
                    Canvas.SetTop(border, cell.Top);
                    panel.Children.Add(border);
                }
                return panel;
            }
            static OcrCharacterLayer DrawingLayer(OverlayRegionViewModel item) => new()
            {
                Width = item.Width,
                Height = item.Height,
                Cells = item.CharacterCells,
                FontFamily = new FontFamily("Segoe UI"),
                TextBrush = Brushes.DarkRed,
            };
            static int VisualCount(DependencyObject parent)
            {
                var count = 1;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    count += VisualCount(VisualTreeHelper.GetChild(parent, i));
                return count;
            }
            static RenderTargetBitmap Render(FrameworkElement element, int width, int height)
            {
                element.Measure(new Size(width, height));
                element.Arrange(new Rect(0, 0, width, height));
                element.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);
                return bitmap;
            }
            void SaveImage(BitmapSource bitmap, string name)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, name + ".png"));
                encoder.Save(stream);
            }
            var sample = Region("Node.js 日本語 e\u0301 👩‍💻", 720, 70);
            var legacyImage = Render(LegacyLayer(sample), 720, 70);
            var drawingImage = Render(DrawingLayer(sample), 720, 70);
            SaveImage(legacyImage, "legacy-text");
            SaveImage(drawingImage, "drawing-text");
            var legacyPixels = new byte[720 * 70 * 4];
            var drawingPixels = new byte[legacyPixels.Length];
            legacyImage.CopyPixels(legacyPixels, 720 * 4, 0);
            drawingImage.CopyPixels(drawingPixels, 720 * 4, 0);
            var differingPixels = 0;
            for (var index = 0; index < legacyPixels.Length; index += 4)
                if (Math.Abs(legacyPixels[index + 3] - drawingPixels[index + 3]) > 24) differingPixels++;
            var pixelDifferenceRatio = differingPixels / (720d * 70);
            Check(pixelDifferenceRatio < 0.05, "Mixed-script text matches former glyph layout (less than 5% materially different pixels).");
            var styled = DrawingLayer(sample);
            styled.Cells = sample.CharacterCells.Select((cell, index) => cell with
            {
                IsSelected = index is 1 or 3,
                IsLocked = index is 2 or 3,
                IsSearchMatch = index == 4,
            }).ToArray();
            styled.IsCharacterEditMode = true;
            styled.IsRegionSelected = true;
            var styledImage = Render(styled, 720, 70);
            SaveImage(styledImage, "selection-lock-search");
            var styledPixels = new byte[720 * 70 * 4];
            styledImage.CopyPixels(styledPixels, 720 * 4, 0);
            foreach (var (index, expectedColor) in new[] { (1, "#FFFF8A00"), (2, "#E0286DA8"), (3, "#FFD84315"), (4, "#FFFF0000") })
            {
                var cell = styled.Cells[index];
                var pixel = (int)(cell.Left + cell.Width / 2) * 4;
                var color = (Color)ColorConverter.ConvertFromString(expectedColor);
                var channels = new[] { color.B * color.A / 255, color.G * color.A / 255, color.R * color.A / 255, color.A };
                Check(channels.Select((channel, offset) => Math.Abs(channel - styledPixels[pixel + offset]) <= 1).All(value => value),
                    $"Cell {index} retains its selected/locked/search border color and precedence.");
            }
            Check(VisualTreeHelper.GetChildrenCount(styled) == 0 && !styled.IsHitTestVisible,
                "Character rendering has no child controls and does not intercept editing input.");

            object MeasurePage(bool legacy)
            {
                var pageRegions = Enumerable.Range(0, 250).Select(_ => Region(new string('漢', 80), 960, 32)).ToArray();
                foreach (var item in pageRegions) _ = item.CharacterCells;
                GC.Collect();
                var before = GC.GetTotalAllocatedBytes(true);
                var timer = Stopwatch.StartNew();
                var panel = new Canvas { Width = 1000, Height = 9000 };
                for (var i = 0; i < pageRegions.Length; i++)
                {
                    FrameworkElement layer = legacy ? LegacyLayer(pageRegions[i]) : DrawingLayer(pageRegions[i]);
                    Canvas.SetTop(layer, i * 36);
                    panel.Children.Add(layer);
                }
                _ = Render(panel, 1000, 900);
                timer.Stop();
                var bytes = GC.GetTotalAllocatedBytes(true) - before;
                var visuals = VisualCount(panel);
                if (!legacy) Check(visuals == 251, "20,000 characters use 250 region layers plus one canvas, independent of character count.");
                return new { regions = 250, characters = 20000, visuals, elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytes = bytes };
            }
            // Warm framework/font initialization before recording either implementation.
            _ = Render(LegacyLayer(Region("漢")), 600, 40);
            _ = Render(DrawingLayer(Region("漢")), 600, 40);
            var legacyCost = MeasurePage(true);
            var drawingCost = MeasurePage(false);

            var viewModel = (MainWindowViewModel)window.DataContext;
            window.ClosePromptOverride = () => MessageBoxResult.No;
            viewModel.ErrorDialogOverride = (message, exception) => throw new InvalidOperationException(message, exception);
            var pdfPath = Path.Combine(directory, "blank.pdf");
            WriteDocumentUiTestPdf(pdfPath);
            await viewModel.LoadPdfForDiagnosticsAsync(pdfPath);
            var first = Region("日本語とNode.js"); first.ReadingOrder = 1; first.Left = 20; first.Top = 20;
            var second = Region("次の行"); second.ReadingOrder = 2; second.Left = 20; second.Top = 80;
            viewModel.OverlayItems.Add(first);
            viewModel.OverlayItems.Add(second);
            var list = (ListBox)window.FindName("OverlayCanvas");
            list.SelectedItems.Add(first);
            var content = (FrameworkElement)window.Content;
            async Task LayoutAsync()
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                _ = Render(content, 1400, 850);
            }
            static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    yield return child;
                    foreach (var descendant in Descendants(child)) yield return descendant;
                }
            }
            await LayoutAsync();
            var firstContainer = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(first);
            var secondContainer = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(second);
            Check(Descendants(firstContainer).OfType<OcrCharacterLayer>().Single().Cells!.Count == first.TextElementCount,
                "The real editor uses the drawing layer and live cell binding.");
            int ResizeCount(DependencyObject item) => Descendants(item).OfType<Thumb>().Count(thumb => thumb.Tag is string);
            Check(ResizeCount(firstContainer) == 8 && ResizeCount(secondContainer) == 0,
                "Only the selected region instantiates its eight resize handles.");
            list.SelectedItems.Clear(); list.SelectedItems.Add(second);
            await LayoutAsync();
            Check(ResizeCount(firstContainer) == 0 && ResizeCount(secondContainer) == 8,
                "Changing selection releases previous resize handles and creates the new set.");
            viewModel.ZoomPercent = 100;
            var resize = Descendants(secondContainer).OfType<Thumb>().Single(thumb => Equals(thumb.Tag, "E"));
            var originalWidth = second.Width;
            resize.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            resize.RaiseEvent(new DragDeltaEventArgs(12, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            resize.RaiseEvent(new DragCompletedEventArgs(12, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(second.Width > originalWidth, "Lazily created handles still deliver resize drag events.");
            viewModel.UndoCommand.Execute(null);
            Check(Math.Abs(second.Width - originalWidth) < 0.001, "Resize from a lazily created handle remains undoable.");
            foreach (var zoom in new[] { 25d, 100d, 400d })
            {
                viewModel.ZoomPercent = zoom;
                viewModel.EditUnitIndex = (int)OcrEditUnit.Character;
                await LayoutAsync();
                var layer = Descendants(secondContainer).OfType<OcrCharacterLayer>().Single();
                Check(layer.IsCharacterEditMode && layer.CellBorderThickness == viewModel.CharacterCellBorderThickness && ResizeCount(secondContainer) == 0,
                    $"Character mode at {zoom}% keeps scale-aware borders without region resize handles.");
            }
            viewModel.EditUnitIndex = (int)OcrEditUnit.Line;
            viewModel.ZoomPercent = 100;
            viewModel.EditorModeIndex = 1;
            await LayoutAsync();
            Check(((ItemsControl)window.FindName("ReadingOrderBadgeLayer")).Visibility == Visibility.Visible,
                "Reading-order foreground badges remain available with the new renderer.");
            SaveImage(Render(content, 1400, 850), "editor-reading-order");
            viewModel.EditorModeIndex = 2;
            await LayoutAsync();
            Check(viewModel.IsReviewMode && ResizeCount(secondContainer) == 0, "Review mode creates no resize handles.");

            File.WriteAllText(Path.Combine(directory, "metrics.json"), JsonSerializer.Serialize(new
            {
                cacheReadBytes,
                pixelDifferenceRatio,
                legacy = legacyCost,
                drawing = drawingCost,
                note = "Synthetic initial layout/drawing only; not end-to-end PDF benchmarks. Timings are informational.",
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks.Select(check => "PASS: " + check));
            _diagnostics?.Write("ocr-rendering-test.passed", $"{checks.Count} checks passed. {directory}");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            if (Directory.Exists(directory)) File.WriteAllText(Path.Combine(directory, "failure.txt"), exception.ToString());
            _diagnostics?.Write("ocr-rendering-test.failed", exception.ToString());
            Shutdown(1);
        }
    }
}
