using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private async Task VerifyParagraphFramesAsync(MainWindow main, string output, Action<bool, string> check,
        Func<FrameworkElement, string, int, int, Task> picture)
    {
        var vm = (MainWindowViewModel)main.DataContext;
        var layer = (OcrParagraphLayer)main.FindName("ParagraphOutlineLayer");
        var canvas = (ListBox)main.FindName("OverlayCanvas");
        vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
        var service = new ProjectPackageService(); var pdf = Path.Combine(output, "paragraph-frames.pdf"); WriteDocumentUiTestPdf(pdf);
        OcrTextRegion Row(string text, double x, double y, double width, double height, Guid? group = null, bool vertical = false, double rotation = 0)
        {
            var geometry = new TextGeometry { LocalBounds = new(new(x, y), new(width, height)), RotationCenter = new(x + width / 2, y + height / 2),
                RotationDegrees = rotation, IsGeometryLocked = true };
            return new() { OriginalText = text, OriginalGeometry = geometry, EditedGeometry = geometry,
                WritingMode = vertical ? WritingMode.Vertical : WritingMode.Horizontal, HasExplicitWritingMode = true, ParagraphGroupId = group };
        }
        var verticalGroup = Guid.NewGuid(); var rotatedGroup = Guid.NewGuid();
        var rows = new[] { Row("これは同じ段落の", 20, 350, 160, 12), Row("続きの行です。", 20, 335, 160, 12),
            Row("縦の段落", 240, 260, 14, 60, verticalGroup, true), Row("続きです", 220, 260, 14, 60, verticalGroup, true),
            Row("回転した段落", 25, 180, 150, 12, rotatedGroup, rotation: 15), Row("回転した続き", 25, 158, 150, 12, rotatedGroup, rotation: 15),
            Row("", 20, 110, 120, 12), Row("削除済み", 20, 80, 100, 12) with { IsDeleted = true } };
        var page = new OcrPage { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = rows,
            ReadingOrder = rows.Select(row => row.Id).ToArray() };
        var second = Row("次のページの段落", 20, 300, 180, 12);
        var input = Path.Combine(output, "paragraph-frames-input.pdfocrproj");
        await service.SaveAsync(input, new() { SourcePdf = await service.CreateSourceReferenceAsync(pdf), Pages = [page,
            new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400, TextRegions = [second], ReadingOrder = [second.Id] }] });
        await vm.LoadProjectForDiagnosticsAsync(input); await vm.EnsureDocumentMetadataAsync();
        vm.DocumentViewMode = DocumentViewMode.SinglePage; vm.DocumentPageFlowMode = DocumentPageFlowMode.PageByPage;
        vm.EditorModeIndex = 0; vm.IsOcrOverlayVisible = vm.ShowOcrFrames = true; vm.EditUnitIndex = (int)OcrEditUnit.Paragraph; vm.ZoomPercent = 100;
        var content = (FrameworkElement)main.Content;
        async Task Draw()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            content.Measure(new(1400, 850)); content.Arrange(new(0, 0, 1400, 850)); main.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1400, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        await Draw();
        check(layer.IsVisible && !layer.IsHitTestVisible && !layer.Focusable && VisualTreeHelper.GetChildrenCount(layer) == 0,
            "Paragraph guides use one noninteractive drawing element with no child controls.");
        check(layer.LastDrawnFrameCount == 4 && layer.Frames.Count == 4,
            "Automatic, vertical manual, rotated manual and blank paragraphs each receive one outer frame; deleted rows do not.");
        foreach (var frame in layer.Frames)
        {
            var members = vm.OverlayItems.Where(row => frame.RegionIds.Contains(row.Id)).ToArray();
            var expected = Rect.Empty;
            foreach (var row in members)
            {
                var item = (ListBoxItem)canvas.ItemContainerGenerator.ContainerFromItem(row);
                var visual = IconVisuals(item).OfType<Grid>().First(grid => grid.RenderTransform is RotateTransform);
                expected.Union(visual.TransformToVisual(layer).TransformBounds(new Rect(visual.RenderSize)));
            }
            var displayed = frame.Bounds; displayed.Offset(layer.SurfaceOffset.X, layer.SurfaceOffset.Y);
            check(Math.Abs(displayed.X - expected.X) < .01 && Math.Abs(displayed.Y - expected.Y) < .01 &&
                Math.Abs(frame.Bounds.Width - expected.Width) < .01 && Math.Abs(frame.Bounds.Height - expected.Height) < .01 &&
                members.Length > 0 && vm.ResolveEditUnitSelection(members[0]).Select(row => row.Id).ToHashSet().SetEquals(frame.RegionIds),
                "Each drawn paragraph matches selection membership and encloses every rotated row corner. " +
                $"Displayed={displayed}; actual visual={expected}; members={members.Length}.");
        }
        var first = vm.OverlayItems.Single(row => row.Id == rows[0].Id); var next = vm.OverlayItems.Single(row => row.Id == rows[1].Id);
        var snapshot = vm.OverlayItems.Select(row => row.Capture()).ToArray(); var image = vm.PreviewImage; var dirty = vm.HasUnsavedChanges;
        var buildCount = layer.PartitionBuildCount; var frames = layer.Frames;
        static byte MaximumAlpha(FrameworkElement visual)
        {
            var width = (int)Math.Ceiling(visual.ActualWidth); var height = (int)Math.Ceiling(visual.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
            byte maximum = 0; for (var offset = 3; offset < pixels.Length; offset += 4) maximum = Math.Max(maximum, pixels[offset]); return maximum;
        }
        canvas.SelectedItems.Clear(); canvas.SelectedItems.Add(first); await Draw();
        check(layer.LastHighlightedFrameCount == 1 && ReferenceEquals(frames, layer.Frames) && buildCount == layer.PartitionBuildCount,
            "Selecting one member emphasizes its complete paragraph without recalculating partitions.");
        var selectedAlpha = MaximumAlpha(layer);
        check(selectedAlpha > 0, "The active drawing layer produces visible outline pixels, not only cached frame metadata.");
        var container = (ListBoxItem)canvas.ItemContainerGenerator.ContainerFromItem(first);
        var body = IconVisuals(container).OfType<Border>().First(border => border.Name == "RegionBody");
        body.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        await Draw();
        check(vm.SelectedOverlays.Select(row => row.Id).ToHashSet().SetEquals(new[] { first.Id, next.Id }),
            "Existing row clicks still select the full paragraph through the noninteractive outer frame.");
        foreach (var zoom in new[] { 57d, 100d, 200d })
        {
            vm.ZoomPercent = zoom; await Draw();
            check(layer.LastDrawnFrameCount == 4 && layer.PartitionBuildCount == buildCount && ReferenceEquals(frames, layer.Frames),
                "Zoom " + zoom + "% reuses paragraph bounds and maintains screen-sized strokes.");
        }
        vm.ZoomPercent = 40;
        ((ScrollViewer)main.FindName("PreviewScrollViewer")).ScrollToHome();
        foreach (var theme in new[] { ApplicationThemeMode.Light, ApplicationThemeMode.Dark })
        {
            ApplicationThemeService.Apply(theme); await Draw();
            await picture(content, "paragraph-frames-" + theme, 1400, 850);
            check(layer.LastDrawnFrameCount == 4 && layer.LastHighlightedFrameCount == 1 && layer.PartitionBuildCount == buildCount,
                theme + " palette retains all paragraph outlines and selection without regrouping.");
        }
        ApplicationThemeService.Apply(ApplicationThemeMode.Light);
        vm.ShowOcrFrames = false; await Draw();
        check(layer.FrameOpacity == 0 && layer.LastDrawnFrameCount == 0 && layer.PartitionBuildCount == buildCount,
            "The existing OCR-frame switch hides paragraph outlines without recomputing them.");
        check(MaximumAlpha(layer) == 0, "Disabling OCR frames removes every paragraph-outline pixel.");
        await picture(content, "paragraph-frames-hidden", 1400, 850);
        vm.ShowOcrFrames = true;
        var settings = vm.CurrentApplicationSettings;
        await vm.ApplyApplicationSettingsAsync(settings with { OcrFrameOpacity = .2 }); await Draw();
        check(layer.FrameOpacity == .2 && layer.LastDrawnFrameCount == 4 && layer.PartitionBuildCount == buildCount,
            "Existing frame opacity reaches the paragraph layer without affecting its partition cache.");
        check(MaximumAlpha(layer) < selectedAlpha, "Lower frame opacity reduces actual pixel alpha, including the selection outline.");
        await vm.ApplyApplicationSettingsAsync(settings); await Draw();
        check(ReferenceEquals(vm.PreviewImage, image) && vm.HasUnsavedChanges == dirty && vm.OverlayItems.Select(row => row.Capture()).SequenceEqual(snapshot),
            "Frame display, theme, zoom and selection preserve PDF pixels, OCR data, geometry locks and dirty state.");
        var beforeBounds = layer.Frames.Single(frame => frame.RegionIds.Contains(first.Id)).Bounds;
        first.Left += 12; await Draw();
        check(layer.PartitionBuildCount > buildCount && layer.Frames.Single(frame => frame.RegionIds.Contains(first.Id)).Bounds != beforeBounds,
            "Geometry changes update paragraph bounds even when model edits are batched.");
        vm.UndoCommand.Execute(null); await Draw();
        check(layer.Frames.Single(frame => frame.RegionIds.Contains(first.Id)).Bounds == beforeBounds, "Undo restores the previous outer bounds.");
        vm.SetOverlaySelection([first, next], first); vm.SeparateParagraphLinesCommand.Execute(null); await Draw();
        check(layer.LastDrawnFrameCount == 5, "Separating an automatic paragraph immediately draws two independent outlines.");
        vm.UndoCommand.Execute(null); await Draw(); check(layer.LastDrawnFrameCount == 4, "Undo restores one enclosing paragraph outline.");
        vm.EditUnitIndex = (int)OcrEditUnit.Line; await Draw();
        check(!layer.IsVisible && layer.Frames.Count == 0, "Line mode releases cached paragraph geometry and subscriptions.");
        vm.EditUnitIndex = (int)OcrEditUnit.Paragraph; await Draw();
        check(layer.IsVisible && layer.LastDrawnFrameCount == 4, "Returning to paragraph mode builds the current page once.");
        vm.EditorModeIndex = 4; await Draw();
        check(!layer.IsVisible, "Paragraph outlines are absent outside OCR editing.");
        vm.EditorModeIndex = 0; vm.IsOcrOverlayVisible = false; await Draw();
        check(!layer.IsVisible && layer.Frames.Count == 0, "Hiding all OCR also releases paragraph-only state.");
        vm.IsOcrOverlayVisible = true; await Draw();
        vm.SelectedPage = vm.PageItems.Single(item => item.PageNumber == 2);
        var end = DateTime.UtcNow.AddSeconds(20);
        while (!vm.OverlayItems.Any(row => row.Id == second.Id))
        { if (DateTime.UtcNow > end) throw new TimeoutException("Paragraph page navigation timed out."); await Task.Delay(15); }
        await Draw();
        check(layer.Frames.Count == 1 && layer.Frames[0].RegionIds.SequenceEqual(new[] { second.Id }),
            "Page navigation drops every previous-page outline and displays only the current page.");
        var finalBuilds = layer.PartitionBuildCount; first.Left += 1; await Draw();
        check(layer.PartitionBuildCount == finalBuilds, "Inactive-page row changes do not invalidate current-page paragraph geometry.");
    }
}
