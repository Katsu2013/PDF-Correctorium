using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyCompactOcrPropertiesAsync(MainWindow window, MainWindowViewModel vm,
        Action<bool, string> check, Func<Task> layout, string directory)
    {
        var fields = (UniformGrid)window.FindName("OcrReviewFieldsGrid");
        var buttons = (UniformGrid)window.FindName("OcrAdjustmentButtonsGrid");
        var review = (ComboBox)window.FindName("KeyboardField1");
        var direction = (ComboBox)window.FindName("KeyboardField2");
        var whole = (Button)window.FindName("OcrAdjustWholeButton");
        var suffix = (Button)window.FindName("OcrAdjustSuffixButton");
        var properties = (ScrollViewer)window.FindName("KeyboardPropertiesPane");
        var pane = (LayoutAnchorablePane)window.PropertyPanes["properties"].Parent;
        var originalWidth = pane.DockWidth;
        var originalLanguage = LocalizationService.CurrentLanguage;
        var oldDirty = vm.HasUnsavedChanges; var oldUndo = vm.UndoCountForDiagnostics;
        var oldPreview = vm.PreviewImage;
        var oldReview = vm.SelectedReviewStatus; var oldDirection = vm.SelectedWritingMode;
        Point At(FrameworkElement control, FrameworkElement parent) => control.TranslatePoint(new Point(), parent);
        bool SameRow(FrameworkElement first, FrameworkElement second, FrameworkElement parent) =>
            Math.Abs(At(first, parent).Y - At(second, parent).Y) < 1 && At(second, parent).X > At(first, parent).X;
        void SaveSelection(string name)
        {
            var section = (FrameworkElement)window.FindName("OcrSelectionProperties");
            var size = new Size(Math.Ceiling(section.ActualWidth), Math.Min(420, Math.Ceiling(section.ActualHeight)));
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(new Point(), size));
                drawing.PushClip(new RectangleGeometry(new Rect(new Point(), size)));
                drawing.DrawRectangle(new VisualBrush(section), null,
                    new Rect(0, 0, section.ActualWidth, section.ActualHeight));
                drawing.Pop();
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
        }
        try
        {
            check(AutomationProperties.GetAccessKey(review) == "Alt+S" && AutomationProperties.GetAccessKey(direction) == "Alt+D" &&
                PropertyLogicalElements(fields).OfType<Label>().Any(label => ReferenceEquals(label.Target, review)) &&
                PropertyLogicalElements(fields).OfType<Label>().Any(label => ReferenceEquals(label.Target, direction)),
                "Compact selectors retain both original named targets, labels and access keys.");
            check(BindingOperations.GetBinding(review, Selector.SelectedValueProperty)?.Path?.Path == "SelectedReviewStatus" &&
                BindingOperations.GetBinding(direction, Selector.SelectedValueProperty)?.Path?.Path == "SelectedWritingMode" &&
                TextSearch.GetTextPath(review) == "DisplayName" && TextSearch.GetTextPath(direction) == "DisplayName",
                "Status/direction keep two-way value bindings and native typed-option search despite the compact templates.");
            foreach (var language in new[] { "ja-JP", "en-US" })
            {
                LocalizationService.SetLanguage(language); vm.RefreshLocalization();
                foreach (var view in window.PropertyPaneViews) LocalizationService.Apply(view);
                foreach (var width in new[] { 320d, 240d, 440d })
                {
                    pane.DockWidth = new GridLength(width); await layout();
                    // Actual available content width, not the pane's requested outer width, controls wrapping.
                    var pair = fields.ActualWidth >= 272;
                    check(fields.Columns == (pair ? 2 : 1) && buttons.Columns == fields.Columns,
                        $"{language}/{width}: selectors and buttons use the same bounded responsive column count ({fields.ActualWidth:0.0} DIP).");
                    check(pair ? SameRow(review, direction, fields) && SameRow(whole, suffix, buttons)
                        : At(direction, fields).Y > At(review, fields).Y && At(suffix, buttons).Y > At(whole, buttons).Y,
                        $"{language}/{width}: normal panels are side by side; narrow panels stack without changing control instances.");
                    check(review.ActualWidth <= (fields.ActualWidth / fields.Columns) && direction.ActualWidth == review.ActualWidth &&
                        whole.ActualHeight <= 32 && suffix.ActualHeight <= 32 && whole.ActualHeight >= 30 && suffix.ActualHeight >= 30,
                        $"{language}/{width}: half-width selectors and compact buttons retain the standard font and 30px hit height.");
                    check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(whole)) &&
                        !string.IsNullOrWhiteSpace(AutomationProperties.GetName(suffix)) && whole.ToolTip is not null && suffix.ToolTip is not null &&
                        ((TextBlock)window.FindName("OcrWholeAdjustmentStatusText")).IsVisible &&
                        ((TextBlock)window.FindName("OcrSuffixAdjustmentStatusText")).IsVisible,
                        $"{language}/{width}: full action names, tooltips and actual adjustment scopes are not hidden by compact captions.");
                    foreach (var control in new[] { review, direction })
                    {
                        var label = (TextBlock)control.ItemTemplate.LoadContent();
                        // This detached template probe has no live DataContext tree.
                        // Supply the real option explicitly without editing the selected OCR status.
                        label.SetBinding(TextBlock.TextProperty, new Binding("DisplayName")
                        { Source = control == review ? (object?)vm.ReviewStatusOptions.MaxBy(option => option.DisplayName.Length) : vm.WritingModeOptions[0] });
                        label.Measure(new Size(Math.Max(1, control.ActualWidth - control.Padding.Left - control.Padding.Right - 2), double.PositiveInfinity));
                        check(!string.IsNullOrWhiteSpace(label.Text) && label.TextWrapping == TextWrapping.Wrap && label.DesiredSize.Width <= control.ActualWidth,
                            $"{language}/{width}: long option text can wrap instead of clipping in the compact selector.");
                    }
                    properties.ScrollToTop(); await layout(); SaveSelection($"compact-ocr-{language}-{width:0}");
                }
            }
            check(vm.HasUnsavedChanges == oldDirty && vm.UndoCountForDiagnostics == oldUndo && ReferenceEquals(vm.PreviewImage, oldPreview) &&
                vm.SelectedReviewStatus == oldReview && vm.SelectedWritingMode == oldDirection,
                "Resizing and translating compact properties does not edit OCR, change selection values, render a page or append Undo.");
        }
        finally
        {
            pane.DockWidth = originalWidth;
            LocalizationService.SetLanguage(originalLanguage); vm.RefreshLocalization();
            foreach (var view in window.PropertyPaneViews) LocalizationService.Apply(view);
            await layout();
        }
    }
}
