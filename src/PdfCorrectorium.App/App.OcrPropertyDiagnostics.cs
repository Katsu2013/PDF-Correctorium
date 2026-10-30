using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.IO;
using PdfCorrectorium.App.Resources;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunOcrPropertyTestAsync(MainWindow window, string[] args, int option)
    {
        if (args.Length <= option + 1) throw new ArgumentException("A new OCR property diagnostic directory is required.");
        var directory = Path.GetFullPath(args[option + 1]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory)
            .Any(path => Path.GetFileName(path) != ".appstate")) throw new IOException("Use a new diagnostic directory.");
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
        }
        try
        {
            Directory.CreateDirectory(directory);
            window.ClosePromptOverride = () => MessageBoxResult.No;
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000; window.Show();
            var root = (FrameworkElement)window.Content;
            async Task Layout()
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                root.Measure(new Size(1400, 850)); root.Arrange(new Rect(0, 0, 1400, 850)); root.UpdateLayout();
            }
            void Snapshot(string name) => SaveIconDiagnosticImage(root, Path.Combine(directory, name + ".png"));
            var vm = (MainWindowViewModel)window.DataContext;
            vm.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            var blank = Path.Combine(directory, "ocr-property-fixture.pdf");
            await BlankPdfService.CreateAsync(blank, 1, 595, 842);
            await vm.OpenDocumentPathAsync(blank);
            await vm.EnsureDocumentMetadataAsync();
            await Layout();
            await RunOcrPropertyContextChecksAsync(window, vm, Check, Layout, Snapshot, directory);
            await VerifyPropertyPresentationAsync(window, vm, Check, Layout, Snapshot);
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
            window.Close(); Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
            File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString());
            _diagnostics?.Write("ocr-property-test.failed", error.ToString()); Shutdown(1);
        }
    }

    private async Task RunOcrPropertyContextChecksAsync(MainWindow window, MainWindowViewModel vm,
        Action<bool, string> check, Func<Task> layout, Action<string> snapshot, string directory)
    {
        LocalizationService.SetLanguage("ja-JP");
        vm.RefreshLocalization();
        vm.EditorMode = EditorInteractionMode.OcrEditing;
        vm.EditUnitIndex = (int)OcrEditUnit.Character;
        window.PropertyPanes["properties"].IsSelected = true;
        var details = (Expander)window.FindName("OcrDetailsExpander");
        var whole = (Button)window.FindName("OcrAdjustWholeButton");
        var suffix = (Button)window.FindName("OcrAdjustSuffixButton");
        var canvas = (ListBox)window.FindName("OverlayCanvas");
        check(ReferenceEquals(whole.Command, vm.EstimateCharacterAdvancesCommand) &&
            ReferenceEquals(suffix.Command, vm.EstimateCharacterSuffixAdvancesCommand) &&
            ActionIcon.GetKey(whole) == "EstimateCharacterAdvances" && ActionIcon.GetKey(suffix) == "EstimateCharacterSuffix",
            "Visible text adjustment actions reuse the existing commands and approved icons.");
        vm.SetOverlaySelection([]); await layout();
        check(vm.OcrContextTarget.Contains("対象未選択") && !whole.IsEnabled && !suffix.IsEnabled,
            "No selection has explicit context and cannot run image adjustment.");
        var first = vm.AddManualOcrRegion(new Rect(60, 240, 120, 28))!;
        first.Text = "校正AB";
        vm.EditUnitIndex = (int)OcrEditUnit.Character;
        canvas.SelectedItems.Clear(); canvas.SelectedItems.Add(first);
        vm.SetOverlaySelection([first], first);
        await layout(); first.SelectedCharacterIndex = 1;
        await layout();
        check(whole.IsEnabled && suffix.IsEnabled && vm.OcrWholeAdjustmentStatus.Contains("1行全体") &&
            vm.OcrSuffixAdjustmentStatus.Contains("2文字目〜末尾"),
            $"A selected character shows distinct entire-line and suffix scopes (whole={whole.IsEnabled}, suffix={suffix.IsEnabled}, primary={ReferenceEquals(vm.SelectedOverlay, first)}, selection={first.SelectedCharacterIndex}, text={first.Text}; {vm.OcrWholeAdjustmentStatus}; {vm.OcrSuffixAdjustmentStatus}).");
        details.IsExpanded = false; await layout();
        var dirty = vm.HasUnsavedChanges;
        var undo = vm.UndoCountForDiagnostics;
        check(window.FocusOcrDetail(Key.X) && details.IsExpanded &&
            ReferenceEquals(Keyboard.FocusedElement, window.FindName("KeyboardX")),
            "A numeric access-key target expands details and receives keyboard focus.");
        details.IsExpanded = false;
        using (var guide = new WindowDiagnosticLifetime(window.CreateOcrOperationGuide()))
        {
            var content = (ItemsControl)guide.Window.FindName("GuideItems");
            check(content.Items.Count >= 11 && content.Items.Cast<string>().All(text => text.Length > 0),
                "The guide retains the original OCR explanations, including selection, locks and numeric controls.");
            var footer = (StackPanel)guide.Window.FindName("DialogFooter");
            check(footer.Children.OfType<Button>().All(button => ActionIcon.GetKey(button).Length == 0),
                "Guide footer remains plain text, following the dialog icon policy.");
            guide.Window.Show(); await layout();
            SaveIconDiagnosticImage((FrameworkElement)guide.Window.Content,
                System.IO.Path.Combine(directory, "20-ocr-guide.png"));
        }
        check(vm.HasUnsavedChanges == dirty && vm.UndoCountForDiagnostics == undo,
            "Opening the guide and expanding details do not change project state or history.");
        first.SelectedCharacterIndex = first.TextElementCount - 1; await layout();
        check(!suffix.IsEnabled && vm.OcrSuffixAdjustmentStatus.Contains("最終文字"),
            "Last-character suffix failure has a specific, adjacent explanation.");
        first.SelectedCharacterIndex = 1;
        var second = vm.AddManualOcrRegion(new Rect(60, 280, 120, 28))!; second.Text = "別行CD";
        vm.EditUnitIndex = (int)OcrEditUnit.Character; first.SelectedCharacterIndex = 1;
        canvas.SelectedItems.Clear(); canvas.SelectedItems.Add(first); canvas.SelectedItems.Add(second);
        vm.SetOverlaySelection([first, second], first); second.IsGeometryLocked = true;
        await layout();
        check(whole.IsEnabled && vm.OcrWholeAdjustmentStatus.Contains("対象外：1行") &&
            vm.OcrContextState.Contains("1/2件"),
            "Partially locked multiple selection reports skipped lines, not a total lock.");
        first.IsGeometryLocked = true; await layout();
        check(!whole.IsEnabled && !suffix.IsEnabled && vm.OcrContextState.Contains("2/2件"),
            "All locked lines disable image adjustment without altering lock semantics.");
        check(((TextBlock)window.FindName("OcrWholeAdjustmentStatusText")).IsVisible &&
            !((TextBlock)window.FindName("OcrSuffixAdjustmentStatusText")).IsVisible,
            "All-locked selection displays its shared unavailable reason only once.");
        first.IsGeometryLocked = false; second.IsGeometryLocked = false;
        canvas.SelectedItems.Clear(); canvas.SelectedItems.Add(first);
        vm.SetOverlaySelection([first], first);
        vm.EditorMode = EditorInteractionMode.Review; await layout();
        check(!whole.IsEnabled && vm.OcrWholeAdjustmentStatus.Contains("OCR編集モード") &&
            vm.OcrContextState.Contains("文字訂正のみ"), "Proofreading distinguishes text correction from geometry adjustment.");
        vm.EditorMode = EditorInteractionMode.ReadingOrder; await layout();
        check(!((StackPanel)window.FindName("OcrImageAdjustmentPanel")).IsVisible,
            "Reading-order editing does not display unrelated image-adjustment controls.");
        vm.EditorMode = EditorInteractionMode.OcrEditing;
        vm.EditUnitIndex = (int)OcrEditUnit.Line; await layout();
        check(whole.IsVisible && !suffix.IsVisible, "Line editing offers entire-line adjustment without a misleading suffix action.");
        vm.EditUnitIndex = (int)OcrEditUnit.Character; first.SelectedCharacterIndex = 1;
        var preview = vm.PreviewImage; vm.PreviewImage = null;
        check(!whole.IsEnabled && vm.OcrWholeAdjustmentStatus.Contains("準備中"),
            "Missing preview is displayed as not ready, never as a successful empty estimate.");
        vm.PreviewImage = preview; await layout();
        ((ScrollViewer)window.FindName("KeyboardPropertiesPane")).ScrollToTop();
        await layout(); snapshot("21-ocr-basic-context");
        LocalizationService.SetLanguage("en-US"); vm.RefreshLocalization();
        foreach (var view in window.PropertyPaneViews) LocalizationService.Apply(view);
        await layout();
        check(vm.OcrContextTarget.StartsWith("PDF page") && whole.Content.ToString() == "Full Line" && suffix.Content.ToString() == "From Character" &&
            vm.OcrSuffixAdjustmentStatus.Contains("character 2 to end"), "Captions and derived scopes update together in English.");
        snapshot("22-ocr-basic-english");
        LocalizationService.SetLanguage("ja-JP"); vm.RefreshLocalization();
        foreach (var view in window.PropertyPaneViews) LocalizationService.Apply(view);
        details.IsExpanded = false;
        await VerifyCompactOcrPropertiesAsync(window, vm, check, layout, directory);
    }

    private sealed class WindowDiagnosticLifetime(Window window) : IDisposable
    {
        public Window Window { get; } = window;
        public void Dispose() => Window.Close();
    }
}
