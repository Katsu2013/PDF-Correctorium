using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyRapidOcrReviewAsync(MainWindow owner, MainWindowViewModel vm, RapidOcrTicket ticket,
        string directory, Action<bool, string> check)
    {
        var before = ticket.Region.Capture(); var dirty = vm.HasUnsavedChanges; var preview = vm.PreviewImage;
        async Task Idle(Window dialog) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout(); }
        foreach (var (width, height) in new[] { (4096, 100), (100, 512) })
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                drawing.DrawRectangle(Brushes.Black, null, new Rect(4, 4, width - 8, height - 8));
            }
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
            var dialog = new RapidOcrReviewWindow(vm, ticket with { Result = ticket.Result with { Image = image } })
            { Owner = owner, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            try
            {
                dialog.Show(); await Idle(dialog);
                var rendered = (Image)dialog.FindName("LineImage"); var viewport = (ScrollViewer)dialog.FindName("ImageViewport");
                Button Button(string name) => (Button)dialog.FindName(name);
                void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                bool Fits() => rendered.Width <= viewport.ViewportWidth + .01 && rendered.Height <= viewport.ViewportHeight + .01;
                check(Fits() && rendered.Width / width <= 1, $"{width}x{height}: initial image fits both axes without upscaling.");
                var frame = (Border)dialog.FindName("ImageFrame");
                var body = (ScrollViewer)dialog.FindName("ReviewBody");
                var footer = (FrameworkElement)dialog.FindName("DialogFooter");
                var root = (FrameworkElement)dialog.Content;
                bool FooterFits() => footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize)).Bottom <= root.ActualHeight + .1
                    && Button("ApplyButton").ActualHeight >= 30;
                check(frame.ActualHeight >= 64 && frame.ActualHeight <= 180 && (width < height || frame.ActualHeight == 64),
                    "The image frame follows the crop aspect ratio within 64–180 DIP instead of stretching into spare height.");
                check(dialog.ActualHeight < (width > height ? 480 : 600) && FooterFits(),
                    "Content-sized review starts compact and retains a reachable 30-DIP footer action.");
                check(new[] { "ZoomOutButton", "ZoomInButton", "FitImageButton", "ActualSizeButton" }.All(name =>
                    PdfCorrectorium.App.Resources.ActionIcon.GetIconOnly(Button(name)) && Button(name).ActualWidth == 32 && Button(name).ActualHeight == 32 &&
                    Button(name).ContentTemplate == dialog.FindResource("ActionIconOnlyTemplate") &&
                    new System.Windows.Automation.Peers.ButtonAutomationPeer(Button(name)).GetName()
                        .Contains(LocalizationService.StripMnemonic((string)Button(name).Content), StringComparison.Ordinal) &&
                    Button(name).ToolTip?.ToString()?.Contains("Alt+", StringComparison.Ordinal) == true),
                    "All image actions show only established icons in 32-DIP targets, retaining accessible names and tooltip mnemonics.");
                SaveIconDiagnosticImage(root, Path.Combine(directory, $"review-compact-{width}x{height}-ja.png"));
                check(new[] { ("ZoomOutButton", "ZoomOut"), ("ZoomInButton", "ZoomIn"), ("FitImageButton", "FitPage"),
                    ("ActualSizeButton", "ActualSize"), ("RestoreCandidateButton", "Reset") }.All(item =>
                        PdfCorrectorium.App.Resources.ActionIcon.GetKey(Button(item.Item1)) == item.Item2),
                    "Zoom and restore actions reuse the established application icons.");
                var scope = PresentationSource.FromVisual(dialog);
                check(new[] { "O", "I", "F", "Z" }.All(key => AccessKeyManager.IsKeyRegistered(scope, key)),
                    "Icon-only image actions register their original access keys without hidden labels.");
                var fittedWidth = rendered.Width; AccessKeyManager.ProcessKey(scope, "I", false); await Idle(dialog);
                check(rendered.Width > fittedWidth && ReferenceEquals(rendered.Source, image), "Zoom uses the same crop instead of rendering PDF/OCR again.");
                AccessKeyManager.ProcessKey(scope, "Z", false); await Idle(dialog);
                check(rendered.Width == width && rendered.Height == height, "100% shows original crop pixels in logical screen units.");
                AccessKeyManager.ProcessKey(scope, "O", false); await Idle(dialog); check(rendered.Width < width, "Zoom out is available after actual-size viewing.");
                AccessKeyManager.ProcessKey(scope, "F", false); dialog.SizeToContent = SizeToContent.Manual;
                dialog.Width = 640; dialog.Height = 560; await Idle(dialog);
                check(Fits(), "Fit mode recomputes when the review window is resized.");
                var editor = (TextBox)dialog.FindName("CandidateEditor");
                foreach (var invalid in new[] { "", " ", "a\nb", "a\tb", new string('字', 513) })
                {
                    editor.Text = invalid;
                    check(!Button("ApplyButton").IsEnabled, "Empty, multiline/control or over-limit manual text cannot be applied.");
                }
                editor.Text = new string('字', 510) + "か\u3099😀";
                check(Button("ApplyButton").IsEnabled, "The manual limit counts Unicode text elements, not UTF-16 units.");
                dialog.Width = 600; dialog.Height = 340; await Idle(dialog);
                check(FooterFits() && body.ScrollableHeight > 0 && editor.ActualHeight <= 76.1,
                    "At minimum window size, long candidates scroll in a bounded editor/body and never displace the footer.");
                body.ScrollToBottom(); await Idle(dialog);
                SaveIconDiagnosticImage(root, Path.Combine(directory, $"review-compact-long-{width}x{height}-ja.png"));
                dialog.Width = 640; dialog.Height = 560;
                Click("RestoreCandidateButton");
                // Difference rendering is intentionally debounced; a compact
                // layout can finish its idle pass before the 120 ms timer fires.
                await Task.Delay(180); await Idle(dialog);
                check(editor.Text == ticket.Result.Recognition.Reading.Text, "Restore returns the exact OCR candidate without changing the line.");
                var pasted = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "a\nb"), false, DataFormats.UnicodeText);
                editor.RaiseEvent(pasted);
                check(pasted.CommandCancelled && editor.Text == ticket.Result.Recognition.Reading.Text,
                    "An invalid paste is rejected explicitly instead of silently losing a line or trailing characters.");
                var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog)!, Environment.TickCount, Key.Return)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                editor.RaiseEvent(enter);
                check(enter.Handled && dialog.IsVisible && ticket.Region.Capture() == before, "Enter in the editor cannot accidentally submit a partly edited candidate.");
                var candidateText = editor.Text;
                LocalizationService.SetLanguage(LocalizationService.EnglishLanguage); LocalizationService.Apply(dialog);
                await Idle(dialog);
                check(dialog.Title.Contains("Review", StringComparison.Ordinal) && editor.Text == candidateText &&
                    WordDifference.GetSuggested((TextBlock)dialog.FindName("CandidateText")) == candidateText &&
                    Button("FitImageButton").Content.ToString()!.Contains("Fit", StringComparison.Ordinal),
                    "English chrome does not translate manual text or the raw difference preview.");
                dialog.Width = 600; dialog.Height = 340; await Idle(dialog);
                check(FooterFits() && Fits(), "English captions wrap/scroll at minimum size without clipping the footer or fitted image.");
                body.ScrollToTop(); await Idle(dialog);
                SaveIconDiagnosticImage(root, Path.Combine(directory, $"review-compact-{width}x{height}-en.png"));
                dialog.Width = 640; dialog.Height = 560; await Idle(dialog);
                SaveIconDiagnosticImage((FrameworkElement)dialog.Content, Path.Combine(directory, $"review-fit-{width}x{height}-en.png"));
                LocalizationService.SetLanguage(LocalizationService.JapaneseLanguage); LocalizationService.Apply(dialog);
                await Idle(dialog);
                SaveIconDiagnosticImage((FrameworkElement)dialog.Content, Path.Combine(directory, $"review-fit-{width}x{height}-ja.png"));
            }
            finally { dialog.Close(); LocalizationService.SetLanguage(LocalizationService.JapaneseLanguage); }
        }
        check(ticket.Region.Capture() == before && vm.HasUnsavedChanges == dirty && ReferenceEquals(vm.PreviewImage, preview),
            "Viewing, resizing, typing and cancelling never edit the document or replace the page image.");
        check(await vm.PrepareRapidOcrReviewAsync(ticket, "\t", default) is null,
            "The preparation layer independently rejects invalid manual input.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); var cancelled = false;
            try { await vm.PrepareRapidOcrReviewAsync(ticket, "手動の訂正", cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && ticket.Region.Capture() == before, "Cancelled manual preparation leaves the snapshot untouched.");
        }
        const string corrected = "フレームワークを再認識します。";
        var prepared = await vm.PrepareRapidOcrReviewAsync(ticket, corrected, default) ?? throw new InvalidOperationException("No manual ticket.");
        check(prepared.Result.Recognition.Reading.Text == corrected && prepared.Adjustment.Snapshot.Text == corrected &&
            prepared.Result.Recognition.Reading.Scores.Length == 0 && !prepared.Completion.IsAutomatic && !vm.ApplyRapidOcrResult(prepared, true),
            "Manual corrections have matching image geometry but never fabricate model scores or qualify for automatic completion.");
        var manual = new RapidOcrReviewWindow(vm, ticket)
        { Owner = owner, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        Exception? failure = null;
        manual.Loaded += (_, _) => manual.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var editor = (TextBox)manual.FindName("CandidateEditor"); editor.Text = corrected;
                await Task.Delay(180); await Idle(manual);
                var candidate = (TextBlock)manual.FindName("CandidateText");
                check(WordDifference.GetSuggested(candidate) == corrected && candidate.Inlines.OfType<System.Windows.Documents.Run>()
                    .Any(run => run.Foreground is SolidColorBrush brush && brush.Color.R > brush.Color.G),
                    "The live preview shows the edited text and highlights differences in red.");
                SaveIconDiagnosticImage((FrameworkElement)manual.Content, Path.Combine(directory, "review-manual-correction.png"));
                ((Button)manual.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; manual.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        check(manual.ShowDialog() == true && failure is null, "The actual review dialog applies manually typed text after asynchronous fitting.");
        var applied = ticket.Region.Capture();
        check(applied.Text == corrected && applied.CharacterAdvancesText == prepared.Adjustment.Snapshot.CharacterAdvancesText &&
            applied.Height == before.Height && applied.RotationDegrees == before.RotationDegrees && applied.ReadingOrder == before.ReadingOrder,
            "Manual application uses the corrected string's image fit and preserves height, rotation and reading order.");
        vm.UndoCommand.Execute(null); check(ticket.Region.Capture() == before, "One Undo restores manual text and character geometry together.");
        vm.RedoCommand.Execute(null); check(ticket.Region.Capture() == applied, "One Redo restores the complete manual correction.");
        vm.UndoCommand.Execute(null);
        check(await vm.PrepareRapidOcrReviewAsync(ticket, corrected, default) is null && !vm.ApplyRapidOcrResult(prepared, false),
            "Undo invalidates both an old review ticket and a prepared manual correction.");
    }
}
