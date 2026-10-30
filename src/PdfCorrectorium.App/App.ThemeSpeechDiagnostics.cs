using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunThemeSpeechTestAsync(MainWindow main, string[] args, int option)
    {
        var output = Path.GetFullPath(args[option + 1]);
        var checks = new List<string>();
        ReadAloudWindow? reader = null;
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any(path => Path.GetFileName(path) != ".appstate"))
                throw new IOException("Use a new diagnostic directory.");
            Directory.CreateDirectory(output);
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add("PASS: " + message); }
            static T Control<T>(FrameworkElement window, string name) where T : class => (T)window.FindName(name);
            async Task Until(Func<bool> condition)
            {
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("UI condition timed out."); await Task.Delay(15); }
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            async Task Picture(FrameworkElement element, string name, int width = 1400, int height = 850)
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Window.GetWindow(element)?.Background ?? (Brush)Current.Resources["AppBackgroundBrush"], null, new Rect(0, 0, width, height));
                bitmap.Render(background);
                bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
            }
            var vm = (MainWindowViewModel)main.DataContext;
            main.ClosePromptOverride = () => MessageBoxResult.No;
            main.ShowActivated = false; main.ShowInTaskbar = false; main.Opacity = 0; main.Show();
            var paragraphProjectOption = Array.FindIndex(args, value => value.Equals("--paragraph-project", StringComparison.OrdinalIgnoreCase));
            if (paragraphProjectOption >= 0 && paragraphProjectOption + 2 < args.Length)
            {
                await VerifyUserParagraphSpeechAsync(main, output, args[paragraphProjectOption + 1], int.Parse(args[paragraphProjectOption + 2]), Check);
                File.WriteAllLines(Path.Combine(output, "checks.txt"), checks); main.Close(); Shutdown(0); return;
            }
            if (args.Contains("--pdf-readings-only", StringComparer.OrdinalIgnoreCase))
            {
                await VerifyPdfReadingExportAsync(output, Check);
                var projectOption = Array.FindIndex(args, value => value.Equals("--pdf-readings-project", StringComparison.OrdinalIgnoreCase));
                if (projectOption >= 0 && projectOption + 1 < args.Length)
                    await VerifyUserProjectReadingExportAsync(output, args[projectOption + 1], Check);
                File.WriteAllLines(Path.Combine(output, "checks.txt"), checks); main.Close(); Shutdown(0); return;
            }
            if (args.Contains("--chapter-group-only", StringComparer.OrdinalIgnoreCase))
            {
                await VerifyMixedParagraphAsync(main, output, Check, Picture);
                File.WriteAllLines(Path.Combine(output, "checks.txt"), checks); main.Close(); Shutdown(0); return;
            }
            if (args.Contains("--paragraph-frames-only", StringComparer.OrdinalIgnoreCase))
            {
                await VerifyParagraphFramesAsync(main, output, Check, Picture);
                File.WriteAllLines(Path.Combine(output, "checks.txt"), checks); main.Close(); Shutdown(0); return;
            }
            if (args.Contains("--shared-reading-only", StringComparer.OrdinalIgnoreCase))
            {
                await VerifySharedReadingsAndDockAsync(main, output, Check, Picture);
                File.WriteAllLines(Path.Combine(output, "checks.txt"), checks); main.Close(); Shutdown(0); return;
            }
            var launch = Control<Button>(main, "ReadAloudToolbarButton");
            Check(!launch.IsEnabled, "Read-aloud launcher is disabled before opening a document.");
            ApplicationThemeService.Apply(ApplicationThemeMode.Light);
            Check(!ApplicationThemeService.IsDark, "Explicit light does not inherit Windows dark mode.");
            foreach (var role in ApplicationThemeService.Palette)
                Check(Current.Resources[role.Key] is SolidColorBrush, "Theme role resolves: " + role.Key);
            ApplicationThemeService.Apply(ApplicationThemeMode.Dark);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            File.WriteAllText(Path.Combine(output, "theme-state.txt"), $"Main={main.Background}; Resource={Current.Resources["AppBackgroundBrush"]}; Mode={ApplicationThemeService.Mode}; HighContrast={SystemParameters.HighContrast}; Source={DependencyPropertyHelper.GetValueSource(main, Window.BackgroundProperty)}");
            Check(((SolidColorBrush)main.Background).Color == (Color)ColorConverter.ConvertFromString("#202124"), "An existing window follows the dark palette dynamically.");
            foreach (var key in new[] { "PrimaryTextBrush", "BodyTextBrush", "SecondaryTextBrush", "LinkTextBrush" })
                Check(Contrast(((SolidColorBrush)Current.Resources[key]).Color, ((SolidColorBrush)Current.Resources["InputBrush"]).Color) >= 4.5,
                    "Dark text contrast is readable: " + key);
            var count = ApplicationThemeService.PaletteApplicationCount;
            ApplicationThemeService.Apply(ApplicationThemeMode.Dark);
            Check(count == ApplicationThemeService.PaletteApplicationCount, "Unchanged theme does not rebuild palettes or controls.");
            ApplicationThemeService.SystemDarkOverride = () => false;
            ApplicationThemeService.Apply(ApplicationThemeMode.System);
            Check(!ApplicationThemeService.IsDark, "Automatic follows simulated Windows light mode without changing the OS.");
            ApplicationThemeService.SystemDarkOverride = () => true;
            ApplicationThemeService.QueueRefresh();
            await Until(() => ApplicationThemeService.IsDark);
            Check(ApplicationThemeService.IsDark, "OS notification switches an existing automatic-theme window to dark.");
            Check((Color)Current.Resources[SystemColors.ActiveCaptionTextColorKey] == ((SolidColorBrush)Current.Resources["PrimaryTextBrush"]).Color,
                "Legacy toolbar paths using caption colors follow the same palette.");
            foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
            {
                var bar = new ScrollBar { Orientation = orientation, Minimum = 0, Maximum = 100, Value = 50, ViewportSize = 10, LargeChange = 10 };
                bar.Style = (Style)Current.FindResource(typeof(ScrollBar));
                bar.Measure(orientation == Orientation.Vertical ? new(17, 240) : new Size(240, 17));
                bar.Arrange(orientation == Orientation.Vertical ? new(0, 0, 17, 240) : new Rect(0, 0, 240, 17)); bar.ApplyTemplate(); bar.UpdateLayout();
                Check(bar.Template.FindName("PART_Track", bar) is Track, "Themed scrollbar retains the native track: " + orientation);
                (orientation == Orientation.Vertical ? ScrollBar.LineDownCommand : ScrollBar.LineRightCommand).Execute(null, bar);
                Check(bar.Value > 50, "Themed scrollbar preserves native line commands: " + orientation);
                var before = bar.Value;
                var track = (Track)bar.Template.FindName("PART_Track", bar);
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(orientation == Orientation.Horizontal ? 10 : 0, orientation == Orientation.Vertical ? 10 : 0) { RoutedEvent = Thumb.DragDeltaEvent });
                Check(bar.Value > before, "Themed scrollbar preserves native thumb dragging: " + orientation);
            }
            ApplicationThemeService.SystemDarkOverride = null;

            await VerifyThemeChromeAsync(main, output, Check);

            var pdf = Path.Combine(output, "source.pdf"); WriteDocumentUiTestPdf(pdf);
            var packages = new ProjectPackageService();
            TextGeometry Geometry(double y) => new() { LocalBounds = new(new(20, y), new(200, 24)), RotationCenter = new(120, y + 12) };
            var one = new OcrTextRegion { OriginalText = "日本語 ABC", OriginalGeometry = Geometry(300), EditedGeometry = Geometry(300),
                WordReadings = [new() { SurfaceText = "日本語", ReadingText = "にほんご" }] };
            var hidden = new OcrTextRegion { OriginalText = "DO NOT SPEAK", OriginalGeometry = Geometry(220), EditedGeometry = Geometry(220), Output = new() { IncludeInSpeech = false } };
            var two = new OcrTextRegion { OriginalText = "第二ページ", OriginalGeometry = Geometry(280), EditedGeometry = Geometry(280) };
            var project = new PdfCorrectoriumProject { Name = "Theme speech fixture", SourcePdf = await packages.CreateSourceReferenceAsync(pdf),
                Pages = [new() { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [one, hidden], ReadingOrder = [one.Id, hidden.Id] },
                    new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400, TextRegions = [two], ReadingOrder = [two.Id] }] };
            var projectPath = Path.Combine(output, "speech.pdfocrproj"); await packages.SaveAsync(projectPath, project);
            await vm.LoadProjectForDiagnosticsAsync(projectPath); await vm.EnsureDocumentMetadataAsync();
            vm.EditorModeIndex = 0; vm.EditUnitIndex = (int)OcrEditUnit.Line;
            var line = vm.OverlayItems.First(region => region.Id == one.Id); vm.SetOverlaySelection([line], line);
            Check(launch.IsEnabled && ToolbarItem.GetKey(launch) == "main.readAloud", "Opening a document enables the customizable read-aloud launcher.");
            vm.SetToolbarItemVisible("main.readAloud", false);
            Check(launch.Visibility == Visibility.Collapsed && vm.CurrentApplicationSettings.HiddenToolbarItems.Contains("main.readAloud"),
                "Read-aloud toolbar visibility uses the shared application preference.");
            vm.SetToolbarItemVisible("main.readAloud", true);
            Check(launch.Visibility == Visibility.Visible, "The hidden read-aloud launcher can be restored without reopening the document.");
            launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var launched = main.DockedReadAloudPanel!;
            var speechPane = main.PropertyPanes["read-aloud"];
            try
            {
                await Until(() => Control<Button>(launched, "PlayButton").IsEnabled);
                Check(launched.IsVisible && launched.PlaybackTask is null, "The toolbar opens the real docked read-aloud panel without starting audio.");
                var rateControl = Control<Slider>(launched, "RateSlider");
                rateControl.Value = 1.5;
                Check(rateControl.TickFrequency == .5 && rateControl.SmallChange == .5 && vm.CurrentApplicationSettings.EffectiveSpeechRate == 1.5,
                    "The real speed slider accepts half steps and persists the fractional value.");
                Check(WindowsSpeechService.VoiceNamesMatch("Microsoft Haruka", "Microsoft Haruka Desktop"),
                    "The former Desktop voice preference maps to its installed Windows counterpart.");
                launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ReferenceEquals(main.DockedReadAloudPanel, launched), "Repeated toolbar clicks reuse the existing reader rather than opening duplicates.");
                await Until(() => launched.ActualHeight > 0);
                var speechContent = Control<StackPanel>(launched, "SpeechContent");
                var collapsedHeight = speechContent.ActualHeight;
                Check(speechContent.ActualHeight <= 240 && Control<Button>(launched, "PlayButton").ActualHeight >= 32,
                    "Docked reading controls remain compact with 32-unit media buttons.");
                Check(launched.FindName("DialogFooter") is null && Control<Button>(launched, "NextButton").ActualHeight >= 32 &&
                      Control<TextBlock>(launched, "SpokenText").Visibility == Visibility.Collapsed,
                    "Reader footer and empty text preview reserve no height while media actions retain their existing hit size.");
                async Task ReaderPicture(string name)
                {
                    var content = (FrameworkElement)speechContent;
                    await Picture(content, name, (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
                }
                ApplicationThemeService.Apply(ApplicationThemeMode.Light);
                await ReaderPicture("reader-compact-light");
                var options = Control<Expander>(launched, "SpeechOptionsExpander");
                options.IsExpanded = true;
                await Until(() => speechContent.ActualHeight > collapsedHeight + 35);
                Check(Control<ComboBox>(launched, "VoiceBox").IsVisible && Control<Slider>(launched, "RateSlider").IsVisible,
                    "Opening voice options grows the window and keeps voice/rate controls available.");
                await ReaderPicture("reader-options-light");
                ApplicationThemeService.Apply(ApplicationThemeMode.Dark);
                await ReaderPicture("reader-options-dark");
                options.IsExpanded = false;
                await Until(() => speechContent.ActualHeight <= collapsedHeight + 1);
                Check(speechContent.ActualHeight <= collapsedHeight + 1, "Closing voice options removes their reserved height.");
                speechPane.Hide();
                await Until(() => !launched.IsVisible);
                Check(ReferenceEquals(main.DockedReadAloudPanel, launched) && speechPane.IsHidden, "Hiding the docked reader preserves its controller without a footer button.");
            }
            finally { if (speechPane.IsVisible) speechPane.Hide(); }
            var dirty = vm.HasUnsavedChanges; var image = vm.PreviewImage;
            ApplicationThemeService.Apply(ApplicationThemeMode.Light);
            await Picture((FrameworkElement)main.Content, "main-light");
            ApplicationThemeService.Apply(ApplicationThemeMode.Dark);
            await Picture((FrameworkElement)main.Content, "main-dark");
            VerifyEnabledImportChrome(main, Check);
            var chrome = new List<string>();
            void DescribeChrome(DependencyObject element)
            {
                if (element is Control control && (control.Name.Contains("Overflow", StringComparison.OrdinalIgnoreCase) ||
                    control is ButtonBase && control.TemplatedParent?.GetType().Namespace?.StartsWith("AvalonDock.Controls", StringComparison.Ordinal) == true))
                    chrome.Add($"{control.GetType().Name} Name={control.Name} Parent={control.TemplatedParent?.GetType().Name} Foreground={control.Foreground} Background={control.Background}");
                for (var child = 0; child < VisualTreeHelper.GetChildrenCount(element); child++) DescribeChrome(VisualTreeHelper.GetChild(element, child));
            }
            DescribeChrome(main);
            File.WriteAllLines(Path.Combine(output, "chrome-controls.txt"), chrome);
            Check(ReferenceEquals(image, vm.PreviewImage) && vm.HasUnsavedChanges == dirty, "Theme switching never rerenders PDF pixels or dirties the document.");
            Check(((SolidColorBrush)Control<Border>(main, "PreviewPageHost").Background).Color == Colors.White, "PDF paper remains white in dark mode.");
            Check(vm.CurrentApplicationSettings.FormatVersion == 21, "Optional UI preferences do not bump project or settings compatibility.");
            var darkSettings = vm.CurrentApplicationSettings with { Theme = ApplicationThemeMode.Dark, SpeechVoiceName = "saved voice", SpeechRate = 2, FineSpeechRate = null };
            var settingsWindow = new ApplicationSettingsWindow(darkSettings, "Portable", vm.SettingsFilePath);
            Check(Control<ComboBox>(settingsWindow, "ThemeComboBox").SelectedValue?.ToString() == "Dark", "Settings exposes the saved theme using enum tags.");
            Check(settingsWindow.TryReadSettings(out var roundtrip) && roundtrip.Theme == ApplicationThemeMode.Dark && roundtrip.SpeechVoiceName == "saved voice", "Theme editing preserves voice preferences.");
            await Picture((FrameworkElement)settingsWindow.Content, "settings-dark", 620, 650);
            var transfer = Path.Combine(output, "settings-transfer.json"); await SettingsTransferService.ExportAsync(transfer, darkSettings);
            var imported = await SettingsTransferService.ImportAsync(transfer);
            Check(imported.Theme == ApplicationThemeMode.Dark && imported.SpeechVoiceName == "saved voice" && imported.SpeechRate == 2, "Theme/voice/rate survive settings export and import.");
            Check(new ApplicationSettings { Theme = (ApplicationThemeMode)999, SpeechRate = 999 }.Normalize() is { Theme: ApplicationThemeMode.System, SpeechRate: 5 }, "Unknown theme and speed normalize safely.");

            var ticket = vm.CreateSpeechTicket(ReadAloudScope.RemainingPages);
            await using (var worker = new PdfNativeWorkerClient(reuseInputs: true))
            {
                var page = await vm.ReadSpeechPageAsync(ticket, 1, worker, default);
                Check(page.Segments.Count == 1 && page.Segments[0].Text == "にほんご ABC", "Loaded-page speech retains saved exclusions and exact pronunciations.");
                Check(worker.InputStageCount == 0, "Visible OCR speech does not stage or render a PDF.");
                var native = await worker.ReadSpeechPageAsync(pdf, 1, default);
                Check(native.WidthPoints == 300 && native.HeightPoints == 400 && native.Regions.Count == 0, "Text-only isolated extraction returns page metrics for an image/empty page without OCR.");
                Check(worker.InputStageCount == 1, "Native text extraction stages only the requested source.");
            }
            var fake = new DiagnosticSpeech();
            await vm.ApplyApplicationSettingsAsync(vm.CurrentApplicationSettings with { Theme = ApplicationThemeMode.Dark, ShowOcrFrames = false });
            reader = new ReadAloudWindow(main, vm, fake);
            reader.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            await Until(() => Control<Button>(reader, "PlayButton").IsEnabled);
            Check(KeyboardAccess.GetKey(Control<Button>(reader, "PlayButton")) == "P" && KeyboardAccess.GetKey(Control<Button>(reader, "PauseButton")) == "A" && KeyboardAccess.GetKey(Control<Button>(reader, "StopButton")) == "T",
                "Icon-only media actions register real WPF access keys, not just automation labels.");
            Control<ComboBox>(reader, "ScopeBox").SelectedIndex = 0;
            Control<Button>(reader, "PlayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => fake.Texts.Count == 1);
            Check(fake.Texts.Single() == "にほんご ABC", "Selection scope speaks the current OCR selection only.");
            Check(fake.Rates[0] == 1.5, "Fractional slider speed reaches the playback engine without integer truncation.");
            Check(Control<TextBlock>(reader, "SpokenText").Visibility == Visibility.Visible && Control<TextBlock>(reader, "SpokenText").Text == fake.Texts[0],
                "Starting speech reveals the previously collapsed text preview.");
            Check(!vm.ShowOcrFrames && main.HasSpeechMarker && main.CurrentSpeechSegment?.RegionId == one.Id, "Spoken line has an independent real adorner even when ordinary OCR frames are hidden.");
            await Picture((FrameworkElement)main.Content, "speech-marker-dark");
            Control<Button>(reader, "PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => fake.Paused);
            Check(fake.Paused, "Pause reaches the speech service.");
            Control<Button>(reader, "PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !fake.Paused);
            Check(!fake.Paused, "Resume reaches the same speech session.");
            line.Left += 1;
            Check(vm.IsSpeechTicketCurrent(ticket) && main.CurrentSpeechSegment is not null, "Geometry-only edits keep speech and its live marker without rescanning words.");
            reader.StopPlayback("test stop");
            await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Check(main.CurrentSpeechSegment is null && Control<Button>(reader, "PlayButton").IsEnabled, "Stop cancels active text and restores controls and marker.");
            Control<ComboBox>(reader, "ScopeBox").SelectedIndex = 1;
            Control<Button>(reader, "PlayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => fake.Texts.Count == 2); fake.Advance();
            await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Check(vm.SelectedPage?.PageNumber == 1 && fake.Texts.Count == 2, "Current-page scope ends without reading the next page.");
            Control<ComboBox>(reader, "ScopeBox").SelectedIndex = 2;
            Control<Button>(reader, "PlayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => fake.Texts.Count == 3);
            Check(vm.SelectedPage?.PageNumber == 1, "Continuous reading begins without preparing all later pages.");
            fake.Advance(); await Until(() => fake.Texts.Count == 4);
            Check(vm.SelectedPage?.PageNumber == 2 && vm.OverlayItems.Any(region => region.Id == two.Id) && fake.Texts[^1] == "第二ページ", "Remaining-page scope waits for the displayed logical page and its OCR before speaking.");
            fake.Advance(); await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            await main.FollowSpeechPageAsync(1); vm.SetOverlaySelection([line], line);
            Control<ComboBox>(reader, "ScopeBox").SelectedIndex = 0;
            Control<Button>(reader, "PlayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => fake.Texts.Count == 5);
            line.Text = "修正文";
            await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Check(main.CurrentSpeechSegment is null, "Text editing cancels pending speech instead of reading stale corrections.");
            await Picture((FrameworkElement)reader.Content, "read-aloud-dark", 590, 250);
            vm.SetSpeechPreferences("diagnostic voice", 3); await vm.FlushDisplaySettingsAsync();
            Check(vm.CurrentApplicationSettings.SpeechRate == 3, "Speech preferences use shared settings persistence.");
            Check(ReadAloudWindow.Chunks(string.Concat(Enumerable.Repeat("か\u3099😀", 900))).All(chunk => !char.IsLowSurrogate(chunk[0]) && !char.IsHighSurrogate(chunk[^1])), "Long prompts split without breaking graphemes or surrogate pairs.");
            reader.Close(); reader = null;

            var paragraphFirst = one with { OriginalText = "これは同じ段落の", WordReadings = [] };
            var paragraphSecond = one with { Id = Guid.NewGuid(), OriginalText = "続きです。", WordReadings = [],
                OriginalGeometry = Geometry(270), EditedGeometry = Geometry(270) };
            var paragraphThird = one with { Id = Guid.NewGuid(), OriginalText = "次の段落です。", WordReadings = [],
                OriginalGeometry = Geometry(190), EditedGeometry = Geometry(190) };
            var paragraphProject = project with { Pages = [project.Pages[0] with
                { TextRegions = [paragraphFirst, paragraphSecond, paragraphThird], ReadingOrder = [paragraphFirst.Id, paragraphSecond.Id, paragraphThird.Id] }] };
            var paragraphPath = Path.Combine(output, "paragraphs.pdfocrproj"); await packages.SaveAsync(paragraphPath, paragraphProject);
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
            await vm.LoadProjectForDiagnosticsAsync(paragraphPath); await vm.EnsureDocumentMetadataAsync();
            var paragraphDirty = vm.HasUnsavedChanges;
            var paragraphFake = new DiagnosticSpeech(); reader = new ReadAloudWindow(main, vm, paragraphFake);
            reader.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            await Until(() => Control<Button>(reader, "PlayButton").IsEnabled);
            Check(Control<ComboBox>(reader, "VoiceBox").ToolTip is string tooltip && tooltip.Contains("自然音声", StringComparison.Ordinal), "Voice picker explains its standard-API scope without listing unusable Narrator voices.");
            Control<Button>(reader, "PlayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => paragraphFake.Texts.Count == 1);
            Check(paragraphFake.Texts[0] == "これは同じ段落の続きです。", "Actual read-aloud window submits one utterance for two wrapped Japanese OCR rows.");
            Check(main.CurrentSpeechSegment?.RegionId == paragraphFirst.Id, "A joined utterance initially marks its first OCR row.");
            var staleProgress = paragraphFake.CurrentProgress;
            paragraphFake.Progress(paragraphFirst.OriginalText.Length + 1);
            await Until(() => main.CurrentSpeechSegment?.RegionId == paragraphSecond.Id);
            Check(main.HasSpeechMarker && !vm.ShowOcrFrames, "Progress switches the independent marker to the second row without showing ordinary frames.");
            await Picture((FrameworkElement)main.Content, "speech-paragraph-second-row");
            paragraphFake.Advance(); await Until(() => paragraphFake.Texts.Count == 2);
            Check(paragraphFake.Texts[1] == paragraphThird.OriginalText && main.CurrentSpeechSegment?.RegionId == paragraphThird.Id, "A paragraph gap starts a new utterance and marker.");
            staleProgress?.Invoke(0); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(main.CurrentSpeechSegment?.RegionId == paragraphThird.Id, "A late progress callback from the preceding paragraph cannot move the marker backward.");
            paragraphFake.Advance(); await reader.PlaybackTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Check(main.CurrentSpeechSegment is null && vm.HasUnsavedChanges == paragraphDirty, "Paragraph inference clears its marker and does not change the document or dirty state.");
            reader.Close(); reader = null;

            await VerifyParagraphEditingAndNavigationAsync(main, output, Check, Picture);

            // An unsaved imported row has no authoritative saved region yet.
            // Its original ink position must survive a later layout correction.
            var nativePdf = Path.Combine(output, "native-invisible-text.pdf");
            WriteSpeechNativePdf(nativePdf);
            vm.DocumentSwitchPromptOverride = () => MessageBoxResult.No;
            await vm.LoadPdfForDiagnosticsAsync(nativePdf); await vm.EnsureDocumentMetadataAsync();
            var importedRow = vm.OverlayItems.First(region => region.Text.Contains("SECRET", StringComparison.Ordinal));
            var nativeProject = vm.ProjectForDiagnostics!;
            var mask = new PdfRedaction { PageId = nativeProject.PageSequence[0].PageId, Bounds = new(new(10, 290), new(160, 40)) };
            typeof(MainWindowViewModel).GetField("_project", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(vm, nativeProject with { Pages = [], Redactions = [mask] });
            var beforeMove = vm.CreateSpeechTicket(ReadAloudScope.CurrentPage);
            importedRow.Left += vm.PreviewPixelWidth * .75;
            Check(!vm.IsSpeechTicketCurrent(beforeMove), "A layout change with planned redactions cancels stale speech eligibility.");
            await using (var worker = new PdfNativeWorkerClient(reuseInputs: true))
            {
                var protectedPage = await vm.ReadSpeechPageAsync(vm.CreateSpeechTicket(ReadAloudScope.CurrentPage), 1, worker, default);
                Check(protectedPage.Segments.All(segment => !segment.Text.Contains("SECRET", StringComparison.Ordinal)), "An unsaved imported row moved away from its original mask is never spoken.");
            }

            var wave = Path.Combine(output, "windows-speech.wav");
            await using (var engine = new WindowsSpeechService(wave))
            {
                var voices = await engine.InitializeAsync();
                var voice = voices.FirstOrDefault(item => item.IsMicrosoft && item.Language.StartsWith("ja", StringComparison.Ordinal)) ??
                    throw new InvalidOperationException("A Microsoft Japanese voice is required for this machine diagnostic.");
                Check(voice.Name.Length > 0, "Actual Windows synthesis service enumerates the installed Microsoft Japanese voice.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var progressPositions = new System.Collections.Concurrent.ConcurrentQueue<int>();
                const string synthesisText = "PDFの読み上げを確認します。";
                await engine.SpeakAsync(synthesisText, voice.Name, 0, timeout.Token, position => progressPositions.Enqueue(position));
                Check(true, "Actual asynchronous Japanese synthesis completes without using the speakers.");
                Check(progressPositions.Any(position => position > 0) && progressPositions.All(position => position >= 0 && position < synthesisText.Length), "Actual timed Windows speech cues report source character positions during playback.");
                using var cancellation = new CancellationTokenSource();
                var speaking = engine.SpeakAsync(string.Concat(Enumerable.Repeat("読み上げの停止を確認します。", 500)), voice.Name, -5, cancellation.Token);
                await engine.PauseAsync(true); await engine.PauseAsync(false); cancellation.Cancel();
                var cancelled = false;
                try { await speaking.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Actual Windows synthesis cancellation completes promptly after pause/resume requests.");
            }
            var bytes = await File.ReadAllBytesAsync(wave);
            Check(bytes.Length > 1000 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && bytes.Skip(80).Any(value => value != 0), "Microsoft voice produces a real nonempty WAV stream.");
            await VerifyHalfStepSpeechAsync(output, Check);
            await VerifyMixedParagraphAsync(main, output, Check, Picture);
            await VerifyParagraphFramesAsync(main, output, Check, Picture);
            await VerifyWordReadingsAsync(main, output, Check, Picture);
            await VerifySharedReadingsAndDockAsync(main, output, Check, Picture);
            await VerifyPdfReadingExportAsync(output, Check);
            File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
            main.Close(); Shutdown(0);
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
            reader?.Close(); Shutdown(1);
        }
        finally { ApplicationThemeService.SystemDarkOverride = null; }
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value) { var number = value / 255d; return number <= .04045 ? number / 12.92 : Math.Pow((number + .055) / 1.055, 2.4); }
        static double Light(Color color) => .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        var a = Light(first); var b = Light(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static void WriteSpeechNativePdf(string path)
    {
        var stream = "BT /F1 20 Tf 20 300 Td 3 Tr (SECRET) Tj ET";
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream" };
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++) { offsets.Add(pdf.Length); pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n"); }
        var xref = pdf.Length; pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
    private sealed class DiagnosticSpeech : ISpeechPlayback
    {
        internal List<string> Texts { get; } = [];
        internal List<double> Rates { get; } = [];
        internal bool Paused { get; private set; }
        private TaskCompletionSource? _pending;
        public string DefaultVoice => "test";
        public Task<IReadOnlyList<WindowsSpeechVoice>> InitializeAsync() => Task.FromResult<IReadOnlyList<WindowsSpeechVoice>>([new("test", "ja-JP", true)]);
        private Action<int>? _progress;
        internal Action<int>? CurrentProgress => _progress;
        public async Task SpeakAsync(string text, string voice, double rate, CancellationToken token, Action<int>? progress = null)
        { _pending = new(TaskCreationOptions.RunContinuationsAsynchronously); _progress = progress; Texts.Add(text); Rates.Add(rate); await _pending.Task.WaitAsync(token); }
        internal void Progress(int position) => _progress?.Invoke(position);
        internal void Advance() => _pending?.TrySetResult();
        public Task PauseAsync(bool paused) { Paused = paused; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

