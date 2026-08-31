using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

/// <summary>
/// アプリケーションの起動、依存サービスの構成、非対話テスト、および未処理例外を管理します。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Windows 11 でペン／タッチ機器が再接続されたときに、旧式の PenIMC (WISP) が
    /// 読み込めずアプリケーションが終了することを避けるための WPF 互換性スイッチ名です。
    /// </summary>
    private const string EnablePointerSupportSwitch = "Switch.System.Windows.Input.Stylus.EnablePointerSupport";

    /// <summary>通常ログを準備する前の起動失敗を記録する簡易診断器です。</summary>
    private StartupDiagnostics? _diagnostics;
    /// <summary>起動・終了だけを検証するスモークテストとして実行中かを示します。</summary>
    private bool _isSmokeTest;
    /// <summary>ダイアログを表示せず終了コードで結果を返すテスト実行かを示します。</summary>
    private bool _isNonInteractiveTest;

    /// <summary>
    /// WPF の入力管理が初期化される前に、Windows 8 以降の WM_POINTER ベースの
    /// ペン／タッチ入力を有効にします。PDF Correctorium の対応OSは Windows 11 のため、
    /// PenIMC.dll に依存する旧式の WISP 入力経路を使用する必要はありません。
    /// </summary>
    static App()
    {
        AppContext.SetSwitch(EnablePointerSupportSwitch, true);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _isSmokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        _isNonInteractiveTest = _isSmokeTest || e.Args.Contains("--render-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--ndl-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--editor-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--editor-project-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--pdf-export-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--project-export-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--project-analysis-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--image-optimize-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--bookmark-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--isolated-pdf-export", StringComparer.OrdinalIgnoreCase);
        _diagnostics = StartupDiagnostics.Create(AppContext.BaseDirectory);
        _isNonInteractiveTest |= e.Args.Contains("--document-ui-test", StringComparer.OrdinalIgnoreCase);
        _isNonInteractiveTest |= e.Args.Contains("--keyboard-test", StringComparer.OrdinalIgnoreCase);
        _isNonInteractiveTest |= e.Args.Contains("--settings-test", StringComparer.OrdinalIgnoreCase);
        _isNonInteractiveTest |= e.Args.Contains("--recent-files-test", StringComparer.OrdinalIgnoreCase);
        _isNonInteractiveTest |= e.Args.Contains("--review-mode-test", StringComparer.OrdinalIgnoreCase);
        _isNonInteractiveTest |= e.Args.Contains("--persistence-test", StringComparer.OrdinalIgnoreCase);
        _isNonInteractiveTest |= e.Args.Contains("--startup-file-test", StringComparer.OrdinalIgnoreCase) ||
            e.Args.Contains("--file-launch-tests", StringComparer.OrdinalIgnoreCase);
        _diagnostics.Write("startup.begin", $"Args: {string.Join(' ', e.Args)}");
        _diagnostics.Write("startup.version", $"Version: {PdfCorrectorium.Core.ApplicationBuildInfo.InformationalVersion}; build: {PdfCorrectorium.Core.ApplicationBuildInfo.NumericVersion}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            base.OnStartup(e);
            _diagnostics.Write("startup.application-ready");

            var isolatedExportIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--isolated-pdf-export", StringComparison.OrdinalIgnoreCase));
            if (isolatedExportIndex >= 0)
            {
                RunIsolatedPdfExport(e.Args, isolatedExportIndex);
                return;
            }

            var renderTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--render-test", StringComparison.OrdinalIgnoreCase));
            if (renderTestIndex >= 0)
            {
                RunRenderTest(e.Args, renderTestIndex);
                return;
            }

            var ndlTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--ndl-test", StringComparison.OrdinalIgnoreCase));
            if (ndlTestIndex >= 0)
            {
                RunNdlTest(e.Args, ndlTestIndex);
                return;
            }

            if (e.Args.Contains("--editor-test", StringComparer.OrdinalIgnoreCase))
            {
                RunEditorTest();
                return;
            }

            var editorProjectTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--editor-project-test", StringComparison.OrdinalIgnoreCase));
            if (editorProjectTestIndex >= 0)
            {
                RunEditorProjectTest(e.Args, editorProjectTestIndex);
                return;
            }

            var pdfExportTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--pdf-export-test", StringComparison.OrdinalIgnoreCase));
            if (pdfExportTestIndex >= 0)
            {
                RunPdfExportTest(e.Args, pdfExportTestIndex);
                return;
            }

            var projectExportTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--project-export-test", StringComparison.OrdinalIgnoreCase));
            if (projectExportTestIndex >= 0)
            {
                RunProjectExportTest(e.Args, projectExportTestIndex);
                return;
            }

            var projectAnalysisTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--project-analysis-test", StringComparison.OrdinalIgnoreCase));
            if (projectAnalysisTestIndex >= 0)
            {
                RunProjectAnalysisTest(e.Args, projectAnalysisTestIndex);
                return;
            }

            var imageOptimizeTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--image-optimize-test", StringComparison.OrdinalIgnoreCase));
            if (imageOptimizeTestIndex >= 0)
            {
                RunImageOptimizeTest(e.Args, imageOptimizeTestIndex);
                return;
            }

            var bookmarkTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--bookmark-test", StringComparison.OrdinalIgnoreCase));
            if (bookmarkTestIndex >= 0)
            {
                RunBookmarkTest(e.Args, bookmarkTestIndex);
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            _diagnostics.Write("startup.window-created");

            var fileLaunchTestsIndex = Array.FindIndex(e.Args, value => value.Equals("--file-launch-tests", StringComparison.OrdinalIgnoreCase));
            if (fileLaunchTestsIndex >= 0)
            {
                RunFileLaunchTests(e.Args, fileLaunchTestsIndex);
                return;
            }
            var startupFileTestIndex = Array.FindIndex(e.Args, value => value.Equals("--startup-file-test", StringComparison.OrdinalIgnoreCase));
            if (startupFileTestIndex >= 0)
            {
                await RunStartupFileTestAsync(window, e.Args, startupFileTestIndex);
                return;
            }

            var documentUiTestIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--document-ui-test", StringComparison.OrdinalIgnoreCase));
            var keyboardTestIndex = Array.FindIndex(e.Args, value => value.Equals("--keyboard-test", StringComparison.OrdinalIgnoreCase));
            var settingsTestIndex = Array.FindIndex(e.Args, value => value.Equals("--settings-test", StringComparison.OrdinalIgnoreCase));
            var recentFilesTestIndex = Array.FindIndex(e.Args, value => value.Equals("--recent-files-test", StringComparison.OrdinalIgnoreCase));
            if (recentFilesTestIndex >= 0)
            {
                await RunRecentFilesTestAsync(window, e.Args, recentFilesTestIndex);
                return;
            }
            if (settingsTestIndex >= 0)
            {
                await RunSettingsTestAsync(window, e.Args, settingsTestIndex);
                return;
            }
            if (keyboardTestIndex >= 0)
            {
                await RunKeyboardTestAsync(window, e.Args, keyboardTestIndex);
                return;
            }
            if (documentUiTestIndex >= 0)
            {
                RunDocumentUiTest(window, e.Args, documentUiTestIndex);
                return;
            }

            var reviewTestIndex = Array.FindIndex(e.Args, value => value.Equals("--review-mode-test", StringComparison.OrdinalIgnoreCase));
            if (reviewTestIndex >= 0)
            {
                await RunReviewModeTestAsync(window, e.Args, reviewTestIndex);
                return;
            }

            if (_isSmokeTest)
            {
                RunSmokeTest(window);
                return;
            }

            var persistenceIndex = Array.FindIndex(e.Args, value => value.Equals("--persistence-test", StringComparison.OrdinalIgnoreCase));
            if (persistenceIndex >= 0)
            {
                await RunPersistenceTestAsync(window, e.Args, persistenceIndex);
                return;
            }

            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _diagnostics.Write("startup.window-shown");
            await OpenStartupFileAsync(window, e.Args);
        }
        catch (Exception exception)
        {
            ReportFatal("The application failed during startup.", exception);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _diagnostics?.Write("shutdown", $"Exit code: {e.ApplicationExitCode}");
        base.OnExit(e);
    }

    /// <summary>
    /// 画面プロセスから分離されたPDF出力ワーカーを実行し、終了コードだけを親へ返します。
    /// </summary>
    private void RunIsolatedPdfExport(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 4)
        {
            _diagnostics?.Write("isolated-pdf-export.invalid-arguments", "Four paths are required after the option.");
            Shutdown(-1);
            return;
        }

        // WPF の Dispatcher スレッド上で非同期 I/O を同期的に待つと、
        // 継続処理が Dispatcher へ戻れずデッドロックする。出力ワーカーは
        // UI を必要としないため、最初からスレッドプール上で完結させる。
        var exitCode = Task.Run(() => IsolatedPdfExportService.RunWorkerAsync(
                arguments[optionIndex + 1],
                arguments[optionIndex + 2],
                arguments[optionIndex + 3],
                arguments[optionIndex + 4]))
            .GetAwaiter()
            .GetResult();
        _diagnostics?.Write("isolated-pdf-export.complete", $"Exit code: {exitCode}");
        Shutdown(exitCode);
    }

    private void RunSmokeTest(Window window)
    {
        if (!AppContext.TryGetSwitch(EnablePointerSupportSwitch, out var pointerSupportEnabled) ||
            !pointerSupportEnabled)
            throw new InvalidOperationException("Windows pointer input support was not enabled before WPF startup.");

        if (window.Content is not FrameworkElement content)
            throw new InvalidOperationException("The main window does not contain a root visual.");
        content.Measure(new Size(1400, 850));
        content.Arrange(new Rect(0, 0, 1400, 850));
        content.UpdateLayout();
        if (content.DesiredSize.Width <= 0 || content.DesiredSize.Height <= 0)
            throw new InvalidOperationException($"The main window layout is empty: {content.DesiredSize}.");
        if (window.FindName("OverlayCanvas") is not System.Windows.Controls.ListBox overlayList ||
            window.DataContext is not MainWindowViewModel viewModel)
            throw new InvalidOperationException("The OCR overlay selection surface was not initialized.");
        if (window.FindName("MainToolbarPanel") is not System.Windows.Controls.ToolBar ||
            window.FindName("PageCommandToolBar") is not System.Windows.Controls.ToolBar ||
            window.FindName("BookmarkCommandToolBar") is not System.Windows.Controls.ToolBar ||
            window.FindName("BookmarkTransferToolBar") is not System.Windows.Controls.ToolBar ||
            window.FindName("ReadingOrderToolBar") is not System.Windows.Controls.ToolBar ||
            window.FindName("LineCharacterSizeToolBar") is not System.Windows.Controls.ToolBar ||
            window.FindName("CharacterAdjustmentToolBar") is not System.Windows.Controls.ToolBar ||
            window.FindName("RotationPresetToolBar") is not System.Windows.Controls.ToolBar ||
            window.FindName("ToolbarPageSummary") is not System.Windows.Controls.TextBlock ||
            window.FindName("StatusZoomSlider") is not System.Windows.Controls.Slider ||
            window.FindName("StatusZoomComboBox") is not System.Windows.Controls.ComboBox { IsEditable: true } ||
            window.FindName("EditUnitSelector") is not System.Windows.Controls.ComboBox ||
            window.FindName("AlignmentIconPanel") is not System.Windows.Controls.Primitives.UniformGrid alignmentIcons ||
            alignmentIcons.Children.Count != 9)
            throw new InvalidOperationException("The icon toolbar, page navigator, status zoom control, or alignment icon panel was not initialized correctly.");
        var first = new OverlayRegionViewModel(new PdfTextOverlayRegion("first", 10, 10, 100, 20, true));
        var second = new OverlayRegionViewModel(new PdfTextOverlayRegion("second", 10, 40, 100, 20, true));
        viewModel.OverlayItems.Add(first);
        viewModel.OverlayItems.Add(second);
        overlayList.SelectedItems.Add(first);
        overlayList.SelectedItems.Add(second);
        if (overlayList.SelectedItems.Count != 2 || viewModel.SelectedOverlayCount != 2 || !viewModel.HasMultipleSelection)
            throw new InvalidOperationException("The OCR overlay list collapsed an extended selection to one item.");
        if (window.FindName("SelectedLineTextBox") is not System.Windows.Controls.TextBox selectedLineTextBox ||
            window.FindName("PreviewPageHost") is not System.Windows.Controls.Border { ContextMenu: not null })
            throw new InvalidOperationException("The live line editor or preview context menu was not initialized.");
        viewModel.SetOverlaySelection([first], first);
        selectedLineTextBox.Text = "first corrected";
        if (first.Text != "first corrected")
            throw new InvalidOperationException("The line editor did not update the preview model immediately.");
        overlayList.UnselectAll();
        viewModel.OverlayItems.Clear();
        var propertiesWindow = new DocumentPropertiesWindow(viewModel);
        if (propertiesWindow.Content is not FrameworkElement propertiesContent)
            throw new InvalidOperationException("The document properties dialog has no content.");
        propertiesContent.Measure(new Size(680, 520));
        propertiesContent.Arrange(new Rect(0, 0, 680, 520));
        if (propertiesContent.DesiredSize.Width <= 0 || propertiesContent.DesiredSize.Height <= 0)
            throw new InvalidOperationException("The document properties dialog layout is empty.");
        propertiesWindow.Close();
        var settingsWindow = new ApplicationSettingsWindow(
            viewModel.CurrentApplicationSettings,
            viewModel.StorageModeText,
            viewModel.SettingsFilePath);
        if (settingsWindow.Content is not FrameworkElement settingsContent)
            throw new InvalidOperationException("The application settings dialog has no content.");
        settingsContent.Measure(new Size(620, 650));
        settingsContent.Arrange(new Rect(0, 0, 620, 650));
        if (settingsContent.DesiredSize.Width <= 0 || settingsContent.DesiredSize.Height <= 0)
            throw new InvalidOperationException("The application settings dialog layout is empty.");
        var normalizedSettings = (new ApplicationSettings
        {
            CharacterHandleThickness = 100,
            CharacterHandleOpacity = 0,
            CharacterCellBorderThickness = 100,
            CharacterEstimationGlyphPrior = 5,
            UndoHistoryLimit = 1,
            ShowUnselectedCharacterCellBorders = false,
            ToolbarButtonSize = 100,
            PageListWidth = 100,
            PropertiesPanelWidth = 1000,
            PageThumbnailSize = 1000,
        }).Normalize();
        if (normalizedSettings.CharacterHandleThickness != 10 || normalizedSettings.CharacterHandleOpacity != 0.15 ||
            normalizedSettings.CharacterEstimationGlyphPrior != 1 ||
            normalizedSettings.UndoHistoryLimit != 10 || normalizedSettings.ShowUnselectedCharacterCellBorders ||
            normalizedSettings.ToolbarButtonSize != 64 || normalizedSettings.PageListWidth != 160 ||
            normalizedSettings.PropertiesPanelWidth != 600 || normalizedSettings.PageThumbnailSize != 220 ||
            normalizedSettings.CharacterCellBorderThickness != 2.0 ||
            normalizedSettings.FormatVersion != ApplicationSettings.CurrentFormatVersion)
            throw new InvalidOperationException("Application settings limits were not normalized.");
        if (!EditorShortcutService.Matches(Key.Right, ModifierKeys.Alt, normalizedSettings.NextCharacterShortcut) ||
            !EditorShortcutService.Matches(Key.A, ModifierKeys.Control | ModifierKeys.Shift, normalizedSettings.EstimateCharacterAdvancesShortcut) ||
            EditorShortcutService.TryNormalize("Shift+Q", out _))
            throw new InvalidOperationException("Default or invalid editor shortcuts were not normalized correctly.");

        var originalLanguage = LocalizationService.CurrentLanguage;
        try
        {
            var englishSettings = (normalizedSettings with
            {
                UiLanguage = LocalizationService.EnglishLanguage,
            }).Normalize();
            if (englishSettings.UiLanguage != LocalizationService.EnglishLanguage)
                throw new InvalidOperationException("The English UI language setting was not preserved.");

            LocalizationService.SetLanguage(englishSettings.UiLanguage);
            LocalizationService.Apply(settingsWindow);
            viewModel.RefreshLocalization();
            if (settingsWindow.Title != "Settings" ||
                LocalizationService.Translate("設定...") != "Settings..." ||
                LocalizationService.Translate("1 / 12 ページ") != "Page 1 of 12" ||
                new PdfPageItem(3).DisplayName != "Page 3" ||
                viewModel.ReviewStatusOptions.First().DisplayName != "Unreviewed" ||
                viewModel.WritingModeOptions.First().DisplayName != "Horizontal")
                throw new InvalidOperationException("The English UI translation did not update all representative controls and dynamic labels.");

            LocalizationService.SetLanguage(LocalizationService.JapaneseLanguage);
            LocalizationService.Apply(settingsWindow);
            viewModel.RefreshLocalization();
            if (settingsWindow.Title != "設定" || new PdfPageItem(3).DisplayName != "3 ページ")
                throw new InvalidOperationException("The UI could not be switched back to Japanese.");
        }
        finally
        {
            LocalizationService.SetLanguage(originalLanguage);
            LocalizationService.Apply(settingsWindow);
            viewModel.RefreshLocalization();
        }
        settingsWindow.Close();

        if (window is not MainWindow mainWindow)
            throw new InvalidOperationException("The smoke test did not create the main window type.");
        var closeTestRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("close test", 10, 10, 100, 20, true));
        viewModel.BeginOverlayEdit(closeTestRegion);
        closeTestRegion.Left += 1;
        viewModel.EndOverlayEdit("Unsaved-close diagnostic");
        if (!viewModel.HasUnsavedChanges)
            throw new InvalidOperationException("The unsaved-close diagnostic did not create a dirty project state.");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        mainWindow.ClosePromptOverride = () => MessageBoxResult.No;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Close();
        if (!closed)
            throw new InvalidOperationException("Discarding unsaved changes did not close the main window.");
        _diagnostics?.Write("smoke-test.pass", $"Desired size: {content.DesiredSize}; unsaved discard close passed");
        Shutdown(0);
    }

    private void RunRenderTest(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 2)
            throw new ArgumentException("--render-test requires an input PDF and an output PNG path.");
        var inputPath = Path.GetFullPath(arguments[optionIndex + 1]);
        var outputPath = Path.GetFullPath(arguments[optionIndex + 2]);
        var pageNumber = arguments.Length > optionIndex + 3 && int.TryParse(arguments[optionIndex + 3], out var requestedPage)
            ? requestedPage
            : 1;
        var result = new PdfPreviewService().RenderPageAsync(inputPath, pageNumber).GetAwaiter().GetResult();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result.Image));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
        _diagnostics?.Write("render-test.pass", $"Pages: {result.PageCount}; Size: {result.Image.PixelWidth}x{result.Image.PixelHeight}; Text regions: {result.TextRegions.Count}; Output: {outputPath}");
        Shutdown(0);
    }

    private void RunNdlTest(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 1)
            throw new ArgumentException("--ndl-test requires a PDF path.");
        var inputPath = Path.GetFullPath(arguments[optionIndex + 1]);
        var document = Task.Run(() => new NdlOcrCompanionService().TryImportAsync(inputPath)).GetAwaiter().GetResult()
            ?? throw new InvalidDataException("No NDLOCR companion files were detected.");
        var firstPageRegions = document.GetScaledRegions(1, 1200, 1600);
        _diagnostics?.Write(
            "ndl-test.pass",
            $"Source: {document.SourceKind}; Pages: {document.Pages.Count}; Page 1 regions: {firstPageRegions.Count}; Vertical regions: {firstPageRegions.Count(region => region.IsVertical)}; Companion files: {document.CompanionFiles.Count}");
        Shutdown(0);
    }

    private void RunEditorTest()
    {
        var paths = ApplicationPathResolver.Resolve(AppContext.BaseDirectory);
        ApplicationPathResolver.EnsureDirectories(paths);
        var viewModel = new MainWindowViewModel(
            new ProjectPackageService(),
            new PdfPreviewService(),
            new PdfExportService(),
            new NdlOcrCompanionService(),
            new DiagnosticLog(paths.LogDirectory),
            paths,
            () => { });
        viewModel.ZoomPercent = 175;
        if (viewModel.ZoomFactor != 1.75) throw new InvalidOperationException("Zoom factor was not updated.");
        viewModel.ZoomPercent = 999;
        if (viewModel.ZoomPercent != 400) throw new InvalidOperationException("Zoom maximum was not enforced.");

        var region = new OverlayRegionViewModel(new PdfTextOverlayRegion("original", 10, 20, 100, 30, true));
        viewModel.BeginOverlayEdit(region);
        region.Text = "edited";
        region.Width = 140;
        region.RotationDegrees = 12.5;
        viewModel.EndOverlayEdit("Editor diagnostic");
        if (!viewModel.UndoCommand.CanExecute(null)) throw new InvalidOperationException("Undo was not enabled after an edit.");
        viewModel.UndoCommand.Execute(null);
        if (region.Text != "original" || region.Width != 100 || region.RotationDegrees != 0) throw new InvalidOperationException("Undo did not restore the OCR region.");
        viewModel.RedoCommand.Execute(null);
        if (region.Text != "edited" || region.Width != 140 || region.RotationDegrees != 12.5) throw new InvalidOperationException("Redo did not reapply the OCR region.");
        var fitWidth = EditorInteractionMath.CalculateFitWidthPercent(810, 1200);
        if (Math.Abs(fitWidth - 66.6666667) > 0.001) throw new InvalidOperationException("Fit-width zoom calculation failed.");
        var fitHeight = EditorInteractionMath.CalculateFitHeightPercent(610, 1200);
        if (Math.Abs(fitHeight - 50) > 0.001) throw new InvalidOperationException("Fit-height zoom calculation failed.");
        var fitPage = EditorInteractionMath.CalculateFitPagePercent(810, 610, 1200, 1200);
        if (Math.Abs(fitPage - 50) > 0.001) throw new InvalidOperationException("Fit-page zoom calculation failed.");
        var centeredOffset = EditorInteractionMath.CalculateCenteredScrollOffset(600, 2, 800);
        if (Math.Abs(centeredOffset - 800) > 0.001) throw new InvalidOperationException("Selection-centered zoom calculation failed.");
        foreach (var direction in new[] { "NW", "N", "NE", "W", "E", "SW", "S", "SE" })
        {
            var resizeRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("resize", 100, 100, 200, 80, true));
            EditorInteractionMath.Resize(resizeRegion, direction, 12, 8, 1200, 1600);
            if (resizeRegion.Left < 0 || resizeRegion.Top < 0 || resizeRegion.Width < 4 || resizeRegion.Height < 4 ||
                resizeRegion.Left + resizeRegion.Width > 1200 || resizeRegion.Top + resizeRegion.Height > 1600)
                throw new InvalidOperationException($"Resize handle {direction} produced invalid geometry.");
        }
        var referenceRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("reference", 10, 10, 120, 20, true));
        var targetRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("target", 75, 40, 60, 20, true));
        referenceRegion.ReadingOrder = 1;
        targetRegion.ReadingOrder = 2;
        viewModel.OverlayItems.Add(referenceRegion);
        viewModel.OverlayItems.Add(targetRegion);
        viewModel.SetOverlaySelection([referenceRegion, targetRegion], referenceRegion);
        viewModel.EqualWidthCommand.Execute(null);
        if (targetRegion.Width != 120) throw new InvalidOperationException("Equal-width command did not use the primary region.");
        viewModel.UndoCommand.Execute(null);
        if (targetRegion.Width != 60) throw new InvalidOperationException("Grouped equal-width undo failed.");
        viewModel.SetOverlaySelection([referenceRegion, targetRegion], targetRegion);
        viewModel.SetAlignmentReferenceCommand.Execute(null);
        viewModel.EqualWidthCommand.Execute(null);
        if (referenceRegion.Width != 60 || !targetRegion.IsAlignmentReference || referenceRegion.IsAlignmentReference)
            throw new InvalidOperationException("Explicit equal-width reference selection failed.");
        viewModel.UndoCommand.Execute(null);
        targetRegion.Height = 35;
        viewModel.EqualHeightCommand.Execute(null);
        if (referenceRegion.Height != 35 || targetRegion.Height != 35) throw new InvalidOperationException("Equal-height command failed.");
        viewModel.UndoCommand.Execute(null);
        if (referenceRegion.Height != 20 || targetRegion.Height != 35) throw new InvalidOperationException("Grouped equal-height undo failed.");
        viewModel.AlignLeftCommand.Execute(null);
        if (referenceRegion.Left != targetRegion.Left) throw new InvalidOperationException("Multi-region left alignment failed.");
        viewModel.UndoCommand.Execute(null);
        if (targetRegion.Left != 75) throw new InvalidOperationException("Grouped alignment undo failed.");
        var referenceLeftBeforeNudge = referenceRegion.Left;
        var referenceTopBeforeNudge = referenceRegion.Top;
        var targetLeftBeforeNudge = targetRegion.Left;
        var targetTopBeforeNudge = targetRegion.Top;
        viewModel.NudgeSelection(2, -1);
        if (referenceRegion.Left != referenceLeftBeforeNudge + 2 || referenceRegion.Top != referenceTopBeforeNudge - 1 ||
            targetRegion.Left != targetLeftBeforeNudge + 2 || targetRegion.Top != targetTopBeforeNudge - 1)
            throw new InvalidOperationException("Keyboard nudge did not move the multi-selection as a group.");
        viewModel.UndoCommand.Execute(null);
        if (referenceRegion.Left != referenceLeftBeforeNudge || referenceRegion.Top != referenceTopBeforeNudge ||
            targetRegion.Left != targetLeftBeforeNudge || targetRegion.Top != targetTopBeforeNudge)
            throw new InvalidOperationException("Grouped keyboard-nudge undo failed.");
        viewModel.SelectedOverlay = targetRegion;
        viewModel.MoveReadingEarlierCommand.Execute(null);
        if (targetRegion.ReadingOrder != 1 || referenceRegion.ReadingOrder != 2)
            throw new InvalidOperationException("Reading-order move failed.");
        viewModel.UndoCommand.Execute(null);
        if (referenceRegion.ReadingOrder != 1 || targetRegion.ReadingOrder != 2)
            throw new InvalidOperationException("Reading-order undo failed.");
        targetRegion.WordReadingsText = "今日=きょう\n一日=ついたち";
        if (targetRegion.HasWordReadingErrors) throw new InvalidOperationException("Valid word readings were rejected.");
        targetRegion.WordReadingsText = "invalid";
        if (!targetRegion.HasWordReadingErrors) throw new InvalidOperationException("Invalid word-reading notation was accepted.");
        targetRegion.WordReadingsText = "今日=きょう\n一日=ついたち";
        viewModel.EditUnitIndex = (int)OcrEditUnit.Paragraph;
        var paragraph = viewModel.ResolveEditUnitSelection(referenceRegion);
        if (paragraph.Count != 2) throw new InvalidOperationException("Paragraph edit mode did not group adjacent lines.");
        viewModel.SetOverlaySelection(paragraph, referenceRegion);
        viewModel.SelectedParagraphText = "段落一行目\n段落二行目";
        if (referenceRegion.Text != "段落一行目" || targetRegion.Text != "段落二行目")
            throw new InvalidOperationException("Paragraph text edit did not update every grouped line.");
        viewModel.UndoCommand.Execute(null);
        viewModel.EditUnitIndex = (int)OcrEditUnit.Character;
        viewModel.SetOverlaySelection([targetRegion], targetRegion);
        viewModel.SelectCharacterAt(targetRegion, targetRegion.Width / 2, targetRegion.Height / 2);
        if (!viewModel.HasSelectedCharacter) throw new InvalidOperationException("Character edit mode did not select a text element.");
        if (targetRegion.CharacterCells.Count != targetRegion.TextElementCount ||
            targetRegion.CharacterCells.Count(cell => cell.IsSelected) != 1)
            throw new InvalidOperationException("Character edit mode did not produce one visual cell per text element.");
        targetRegion.SelectedCharacterIndex = 0;
        if (!viewModel.NextCharacterCommand.CanExecute(null))
            throw new InvalidOperationException("The next-character shortcut command was unavailable.");
        viewModel.NextCharacterCommand.Execute(null);
        if (targetRegion.SelectedCharacterIndex != 1)
            throw new InvalidOperationException("The next-character shortcut command did not move the selector.");
        viewModel.PreviousCharacterCommand.Execute(null);
        if (targetRegion.SelectedCharacterIndex != 0)
            throw new InvalidOperationException("The previous-character shortcut command did not move the selector.");
        var advanceBeforeShortcut = targetRegion.SelectedCharacterAdvance;
        viewModel.IncreaseCharacterAdvanceCommand.Execute(null);
        if (Math.Abs(targetRegion.SelectedCharacterAdvance - (advanceBeforeShortcut + 1)) > 0.001)
            throw new InvalidOperationException("The character-width shortcut command did not increase the selected advance.");
        viewModel.UndoCommand.Execute(null);
        if (Math.Abs(targetRegion.SelectedCharacterAdvance - advanceBeforeShortcut) > 0.001)
            throw new InvalidOperationException("Undo did not restore a keyboard character-width adjustment.");
        var selectedCharacterIndex = targetRegion.SelectedCharacterIndex;
        var originalCharacterAdvance = viewModel.SelectedCharacterAdvance;
        var originalLineWidth = targetRegion.Width;
        var originalCells = targetRegion.CharacterCells.ToArray();
        var nextCharacterLeft = selectedCharacterIndex + 1 < originalCells.Length
            ? originalCells[selectedCharacterIndex + 1].Left
            : -1;
        if (!targetRegion.HasHorizontalCharacterSelection || targetRegion.HasVerticalCharacterSelection ||
            Math.Abs(targetRegion.CharacterSelectionRight - (targetRegion.CharacterSelectionLeft + originalCharacterAdvance)) > 0.001)
            throw new InvalidOperationException("Horizontal character boundary geometry is invalid.");
        viewModel.BeginOverlayEdit(targetRegion);
        viewModel.SelectedCharacterAdvance = originalCharacterAdvance + 5;
        viewModel.EndOverlayEdit("Per-character advance diagnostic");
        if (Math.Abs(targetRegion.CharacterCells[selectedCharacterIndex].Width - (originalCharacterAdvance + 5)) > 0.001 ||
            Math.Abs(targetRegion.Width - (originalLineWidth + 5)) > 0.001)
            throw new InvalidOperationException("Per-character advance editing did not resize the character and line extent.");
        if (nextCharacterLeft >= 0 && Math.Abs(targetRegion.CharacterCells[selectedCharacterIndex + 1].Left - (nextCharacterLeft + 5)) > 0.001)
            throw new InvalidOperationException("Per-character advance editing did not move the following character boundary.");
        for (var index = 0; index < originalCells.Length; index++)
            if (index != selectedCharacterIndex && Math.Abs(targetRegion.CharacterCells[index].Width - originalCells[index].Width) > 0.001)
                throw new InvalidOperationException("Per-character advance editing changed an unrelated character width.");
        viewModel.UndoCommand.Execute(null);
        if (Math.Abs(viewModel.SelectedCharacterAdvance - originalCharacterAdvance) > 0.001 ||
            Math.Abs(targetRegion.Width - originalLineWidth) > 0.001)
            throw new InvalidOperationException($"Per-character advance undo did not restore the original geometry. Advance {viewModel.SelectedCharacterAdvance:0.###}/{originalCharacterAdvance:0.###}; width {targetRegion.Width:0.###}/{originalLineWidth:0.###}.");
        var beforeCharacterEdit = targetRegion.Text;
        viewModel.SelectedCharacterText = "字";
        if (targetRegion.Text == beforeCharacterEdit || !targetRegion.Text.Contains('字'))
            throw new InvalidOperationException("Character edit mode did not replace the selected text element.");
        viewModel.EditUnitIndex = (int)OcrEditUnit.Line;
        if (targetRegion.HasCharacterSelection || targetRegion.CharacterCells.Any(cell => cell.IsSelected))
            throw new InvalidOperationException("Character selection remained active after leaving character edit mode.");
        var verticalRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("縦書", 10, 10, 20, 80, true, IsVertical: true));
        verticalRegion.SelectedCharacterIndex = 0;
        if (!verticalRegion.HasVerticalCharacterSelection || verticalRegion.HasHorizontalCharacterSelection ||
            Math.Abs(verticalRegion.CharacterSelectionBottom - verticalRegion.CharacterCells[0].Height) > 0.001)
            throw new InvalidOperationException("Vertical character boundary geometry is invalid.");
        viewModel.SetOverlaySelection([verticalRegion], verticalRegion);
        var originalVerticalWidth = verticalRegion.Width;
        var originalVerticalHeight = verticalRegion.Height;
        viewModel.SelectedWritingMode = WritingMode.Horizontal;
        if (verticalRegion.IsVertical ||
            Math.Abs(verticalRegion.Width - originalVerticalHeight) > 0.001 ||
            Math.Abs(verticalRegion.Height - originalVerticalWidth) > 0.001 ||
            Math.Abs(verticalRegion.CharacterCells.Sum(cell => cell.Width) - verticalRegion.Width) > 0.001)
            throw new InvalidOperationException("Vertical-to-horizontal writing-mode conversion did not transpose the line geometry.");
        viewModel.UndoCommand.Execute(null);
        if (!verticalRegion.IsVertical ||
            Math.Abs(verticalRegion.Width - originalVerticalWidth) > 0.001 ||
            Math.Abs(verticalRegion.Height - originalVerticalHeight) > 0.001)
            throw new InvalidOperationException("Writing-mode conversion undo did not restore the vertical geometry.");
        viewModel.RedoCommand.Execute(null);
        if (verticalRegion.IsVertical)
            throw new InvalidOperationException("Writing-mode conversion redo did not reapply the horizontal direction.");
        viewModel.UndoCommand.Execute(null);
        var proportionalRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABC", 0, 0, 60, 20, true, CharacterAdvances: [10, 20, 30]));
        if (!proportionalRegion.CharacterCells.Select(cell => cell.Text).SequenceEqual(["A", "B", "C"]) ||
            !proportionalRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 20d, 30d]))
            throw new InvalidOperationException("Character overlay cells did not preserve one fitted text element per proportional cell.");
        var splitCharacterRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABC", 0, 0, 60, 20, true, CharacterAdvances: [10, 30, 20]));
        splitCharacterRegion.SelectedCharacterIndex = 1;
        viewModel.OverlayItems.Add(splitCharacterRegion);
        viewModel.SetOverlaySelection([splitCharacterRegion], splitCharacterRegion);
        viewModel.BeginOverlayEdit(splitCharacterRegion);
        viewModel.SelectedCharacterText = "XY";
        viewModel.EndOverlayEdit("Split character diagnostic");
        if (splitCharacterRegion.Text != "AXYC" ||
            !splitCharacterRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 15d, 15d, 20d]) ||
            !splitCharacterRegion.SelectedCharacterIndices.SequenceEqual([1, 2]) ||
            Math.Abs(splitCharacterRegion.Width - 60) > 0.001)
            throw new InvalidOperationException("Splitting one OCR character cell into multiple characters changed unrelated geometry.");
        viewModel.UndoCommand.Execute(null);
        if (splitCharacterRegion.Text != "ABC" ||
            !splitCharacterRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 30d, 20d]))
            throw new InvalidOperationException("Undo did not restore a split OCR character cell.");
        splitCharacterRegion.SelectedCharacterIndex = 1;
        splitCharacterRegion.ReplaceSelectedCharacter("XY");
        splitCharacterRegion.SelectedCharacterIndex = 1;
        splitCharacterRegion.SetSelectedCharacterLocks(true);
        splitCharacterRegion.ReplaceSelectedCharacter("12");
        if (!splitCharacterRegion.CharacterCells.Skip(1).Take(2).All(cell => cell.IsLocked) ||
            !splitCharacterRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 7.5d, 7.5d, 15d, 20d]))
            throw new InvalidOperationException("Split characters did not inherit the original cell lock and extent.");
        viewModel.OverlayItems.Remove(splitCharacterRegion);
        var deletionRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABCDE", 40, 30, 150, 20, true, CharacterAdvances: [10, 20, 30, 40, 50]));
        deletionRegion.Text = "ABE";
        if (!deletionRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 20d, 50d]) ||
            Math.Abs(deletionRegion.Width - 80) > 0.001 ||
            Math.Abs(deletionRegion.Left - 40) > 0.001)
            throw new InvalidOperationException("Deleting text resized cells before or after the edited range.");
        deletionRegion.SelectedCharacterIndex = 1;
        deletionRegion.ReplaceSelectedCharacter(string.Empty);
        if (!deletionRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 50d]) ||
            Math.Abs(deletionRegion.Width - 60) > 0.001 ||
            Math.Abs(deletionRegion.Left - 40) > 0.001)
            throw new InvalidOperationException("Single-cell deletion resized an unaffected character advance.");
        var inkBoundsRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABC", 0, 0, 100, 20, true, CharacterAdvances: [10, 20, 30]));
        var inkBoundsWidths = inkBoundsRegion.CharacterCells.Select(cell => cell.Width).ToArray();
        if (Math.Abs(inkBoundsWidths.Sum() - inkBoundsRegion.Width) > 0.001 ||
            Math.Abs(inkBoundsRegion.CharacterCells[^1].Left + inkBoundsRegion.CharacterCells[^1].Width - inkBoundsRegion.Width) > 0.001 ||
            Math.Abs(inkBoundsWidths[1] / inkBoundsWidths[0] - 2) > 0.001 ||
            inkBoundsRegion.IsModified)
            throw new InvalidOperationException(
                $"Imported glyph bounds were not normalized to the complete horizontal line extent. " +
                $"Widths=[{string.Join(',', inkBoundsWidths.Select(value => value.ToString("0.######", CultureInfo.InvariantCulture)))}], " +
                $"extent={inkBoundsRegion.Width:0.######}, modified={inkBoundsRegion.IsModified}.");
        var verticalInkBoundsRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "縦書き", 0, 0, 20, 120, true, IsVertical: true, CharacterAdvances: [10, 20, 30]));
        var verticalCells = verticalInkBoundsRegion.CharacterCells;
        if (Math.Abs(verticalCells.Sum(cell => cell.Height) - verticalInkBoundsRegion.Height) > 0.001 ||
            Math.Abs(verticalCells[^1].Top + verticalCells[^1].Height - verticalInkBoundsRegion.Height) > 0.001 ||
            verticalInkBoundsRegion.IsModified)
            throw new InvalidOperationException("Imported glyph bounds were not normalized to the complete vertical line extent.");
        viewModel.OverlayItems.Add(proportionalRegion);
        viewModel.EditUnitIndex = (int)OcrEditUnit.Character;
        viewModel.SetOverlaySelection([proportionalRegion], proportionalRegion);
        viewModel.SelectCharacterAt(proportionalRegion, 15, 10);
        if (proportionalRegion.SelectedCharacterIndex != 1)
            throw new InvalidOperationException("Proportional character hit testing used equal-width positions.");
        proportionalRegion.SelectCharacter(2, toggle: true, extendRange: false);
        if (proportionalRegion.SelectedCharacterCount != 2 ||
            !proportionalRegion.SelectedCharacterIndices.SequenceEqual([1, 2]))
            throw new InvalidOperationException("Ctrl-style multi-character selection failed.");
        viewModel.BeginOverlayEdit(proportionalRegion);
        proportionalRegion.AdjustSelectedCharacterAdvances(5);
        viewModel.EndOverlayEdit("Multi-character advance diagnostic");
        if (!proportionalRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 25d, 35d]) ||
            Math.Abs(proportionalRegion.Width - 70) > 0.001)
            throw new InvalidOperationException("Multi-character drag adjustment changed the wrong advances.");
        viewModel.UndoCommand.Execute(null);
        if (!proportionalRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 20d, 30d]))
            throw new InvalidOperationException("Multi-character advance undo failed.");
        proportionalRegion.SelectedCharacterIndex = 0;
        proportionalRegion.SelectCharacter(2, toggle: false, extendRange: true);
        if (proportionalRegion.SelectedCharacterCount != 3)
            throw new InvalidOperationException("Shift-style character range selection failed.");
        viewModel.BeginOverlayEdit(proportionalRegion);
        proportionalRegion.SelectedCharacterAdvance = 12;
        viewModel.EndOverlayEdit("Multi-character equal advance diagnostic");
        if (proportionalRegion.CharacterCells.Any(cell => Math.Abs(cell.Width - 12) > 0.001))
            throw new InvalidOperationException("Numeric multi-character sizing did not set a common advance.");
        viewModel.UndoCommand.Execute(null);
        viewModel.EqualizeCharacterAdvancesCommand.Execute(null);
        if (proportionalRegion.CharacterCells.Any(cell => Math.Abs(cell.Width - 20) > 0.001) || Math.Abs(proportionalRegion.Width - 60) > 0.001)
            throw new InvalidOperationException("Equal-width reset did not preserve the line extent.");
        viewModel.UndoCommand.Execute(null);
        if (!proportionalRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 20d, 30d]))
            throw new InvalidOperationException("Equal-width reset undo did not restore proportional advances.");
        proportionalRegion.SelectedCharacterIndex = 1;
        proportionalRegion.SelectedCharacterAdvance = 25;
        viewModel.RestoreOriginalCharacterAdvancesCommand.Execute(null);
        if (!proportionalRegion.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 20d, 30d]))
            throw new InvalidOperationException("Original character-width restoration failed.");
        var estimationPixels = Enumerable.Repeat((byte)255, 60 * 20 * 4).ToArray();
        for (var pixel = 3; pixel < estimationPixels.Length; pixel += 4) estimationPixels[pixel] = 255;
        foreach (var (left, right) in new[] { (2, 9), (15, 37), (43, 57) })
        for (var y = 3; y < 17; y++)
        for (var x = left; x <= right; x++)
        {
            var offset = (y * 60 + x) * 4;
            estimationPixels[offset] = 0;
            estimationPixels[offset + 1] = 0;
            estimationPixels[offset + 2] = 0;
        }
        var estimationImage = BitmapSource.Create(60, 20, 96, 96, PixelFormats.Bgra32, null, estimationPixels, 60 * 4);
        estimationImage.Freeze();
        var multiLinePixels = Enumerable.Repeat((byte)255, 60 * 50 * 4).ToArray();
        foreach (var (top, bottom, left, right) in new[]
                 {
                     (3, 17, 2, 9), (3, 17, 15, 37), (3, 17, 43, 57),
                     (28, 42, 2, 14), (28, 42, 20, 27), (28, 42, 34, 57),
                 })
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var offset = (y * 60 + x) * 4;
            multiLinePixels[offset] = 0;
            multiLinePixels[offset + 1] = 0;
            multiLinePixels[offset + 2] = 0;
        }
        var multiLineImage = BitmapSource.Create(60, 50, 96, 96, PixelFormats.Bgra32, null, multiLinePixels, 60 * 4);
        multiLineImage.Freeze();
        var firstSelectedLine = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABC", 0, 0, 60, 20, true, CharacterAdvances: [20, 20, 20])) { ReadingOrder = 10 };
        var secondSelectedLine = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "DEF", 0, 25, 60, 20, true, CharacterAdvances: [20, 20, 20])) { ReadingOrder = 11 };
        viewModel.OverlayItems.Add(firstSelectedLine);
        viewModel.OverlayItems.Add(secondSelectedLine);
        viewModel.PreviewImage = multiLineImage;
        viewModel.EditUnitIndex = (int)OcrEditUnit.Line;
        viewModel.SetOverlaySelection([firstSelectedLine, secondSelectedLine], firstSelectedLine);
        if (!viewModel.IncreaseLineCharacterSizeCommand.CanExecute(null))
            throw new InvalidOperationException("Line-mode bulk character-size command was not enabled.");
        viewModel.IncreaseLineCharacterSizeCommand.Execute(null);
        if (firstSelectedLine.CharacterCells.Any(cell => Math.Abs(cell.Width - 21) > 0.001) ||
            secondSelectedLine.CharacterCells.Any(cell => Math.Abs(cell.Width - 21) > 0.001))
            throw new InvalidOperationException("Line-mode bulk character-size command did not update every selected line.");
        viewModel.UndoCommand.Execute(null);
        if (firstSelectedLine.CharacterCells.Any(cell => Math.Abs(cell.Width - 20) > 0.001) ||
            secondSelectedLine.CharacterCells.Any(cell => Math.Abs(cell.Width - 20) > 0.001))
            throw new InvalidOperationException("Line-mode bulk character-size undo did not restore every selected line.");
        if (!viewModel.EstimateCharacterAdvancesCommand.CanExecute(null))
            throw new InvalidOperationException("Multi-line character-width estimation command was not enabled.");
        viewModel.EstimateCharacterAdvancesCommand.Execute(null);
        if (firstSelectedLine.CharacterCells.All(cell => Math.Abs(cell.Width - 20) < 0.001) ||
            secondSelectedLine.CharacterCells.All(cell => Math.Abs(cell.Width - 20) < 0.001))
            throw new InvalidOperationException("Multi-line character-width estimation did not update every selected line.");
        viewModel.UndoCommand.Execute(null);
        if (firstSelectedLine.CharacterCells.Any(cell => Math.Abs(cell.Width - 20) > 0.001) ||
            secondSelectedLine.CharacterCells.Any(cell => Math.Abs(cell.Width - 20) > 0.001))
            throw new InvalidOperationException("One Undo did not restore every line changed by multi-line estimation.");
        firstSelectedLine.SetCharacterAdvances([10, 20, 30]);
        secondSelectedLine.SetCharacterAdvances([30, 20, 10]);
        viewModel.SetOverlaySelection([firstSelectedLine, secondSelectedLine], secondSelectedLine);
        viewModel.EqualizeCharacterAdvancesCommand.Execute(null);
        if (firstSelectedLine.CharacterCells.Any(cell => Math.Abs(cell.Width - 20) > 0.001) ||
            secondSelectedLine.CharacterCells.Any(cell => Math.Abs(cell.Width - 20) > 0.001))
            throw new InvalidOperationException("Multi-line equal-width reset only updated the primary line.");
        viewModel.UndoCommand.Execute(null);
        if (!firstSelectedLine.CharacterCells.Select(cell => cell.Width).SequenceEqual([10d, 20d, 30d]) ||
            !secondSelectedLine.CharacterCells.Select(cell => cell.Width).SequenceEqual([30d, 20d, 10d]))
            throw new InvalidOperationException("Multi-line equal-width reset undo did not restore all selected lines.");
        var estimated = CharacterAdvanceEstimator.Estimate(estimationImage, proportionalRegion);
        if (estimated.Advances.Count != 3 || estimated.Advances[1] <= estimated.Advances[0] ||
            estimated.Advances[1] <= estimated.Advances[2] || Math.Abs(estimated.Advances.Sum() - 60) > 0.001)
            throw new InvalidOperationException("Image-based proportional character-width estimation failed.");
        proportionalRegion.SetCharacterAdvances(estimated.Advances);
        if (proportionalRegion.CharacterCells[1].Width <= proportionalRegion.CharacterCells[0].Width)
            throw new InvalidOperationException("Estimated character widths were not applied to the overlay.");
        var dashPixels = Enumerable.Repeat((byte)255, 80 * 20 * 4).ToArray();
        for (var pixel = 3; pixel < dashPixels.Length; pixel += 4) dashPixels[pixel] = 255;
        foreach (var (top, bottom, left, right) in new[]
                 {
                     (3, 16, 2, 14),   // A-like body
                     (9, 10, 23, 29),  // ASCII hyphen: low ink and narrow advance
                     (9, 10, 39, 57),  // Japanese prolonged mark: low ink and full-em advance
                     (3, 16, 65, 77),  // B-like body
                 })
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var offset = (y * 80 + x) * 4;
            dashPixels[offset] = 0;
            dashPixels[offset + 1] = 0;
            dashPixels[offset + 2] = 0;
        }
        var dashImage = BitmapSource.Create(80, 20, 96, 96, PixelFormats.Bgra32, null, dashPixels, 80 * 4);
        dashImage.Freeze();
        var dashRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("A-ーB", 0, 0, 80, 20, true));
        var dashEstimate = CharacterAdvanceEstimator.Estimate(dashImage, dashRegion);
        if (dashEstimate.Advances.Count != 4 ||
            dashEstimate.Advances[1] is < 6 or > 20 ||
            dashEstimate.Advances[2] < dashEstimate.Advances[1] * 1.45 ||
            Math.Abs(dashEstimate.Advances.Sum() - 80) > 0.001)
            throw new InvalidOperationException("Dash-aware character-width estimation collapsed or expanded a thin dash cell.");
        var suffixRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABC", 0, 0, 60, 20, true, CharacterAdvances: [10, 20, 30]));
        suffixRegion.SelectedCharacterIndex = 1;
        var suffixEstimationRegion = suffixRegion.CreateCharacterSuffixEstimationRegion(1);
        if (suffixEstimationRegion.Text != "BC" ||
            Math.Abs(suffixEstimationRegion.Left - 10) > 0.001 ||
            Math.Abs(suffixEstimationRegion.Width - 50) > 0.001)
            throw new InvalidOperationException("The selected-character suffix geometry was not anchored at the selected boundary.");
        var suffixEstimate = CharacterAdvanceEstimator.Estimate(estimationImage, suffixEstimationRegion);
        suffixRegion.ApplyCharacterSuffixAdvanceEstimation(1, suffixEstimate);
        var suffixCells = suffixRegion.CharacterCells.ToArray();
        if (Math.Abs(suffixCells[0].Width - 10) > 0.001 ||
            Math.Abs(suffixCells[1].Left - 10) > 0.001 ||
            suffixCells[1].Width <= suffixCells[2].Width)
            throw new InvalidOperationException("Suffix-only automatic adjustment changed the fixed prefix or lost proportional suffix widths.");
        var lockedPrefixSuffixRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABCDE", 0, 0, 50, 20, true, CharacterAdvances: [10, 10, 10, 10, 10]));
        lockedPrefixSuffixRegion.SelectedCharacterIndex = 0;
        lockedPrefixSuffixRegion.SetSelectedCharacterLocks(true);
        lockedPrefixSuffixRegion.SelectedCharacterIndex = 2;
        var lockedPrefixChanged = lockedPrefixSuffixRegion.ApplyCharacterSuffixAdvanceEstimation(
            2,
            new CharacterAdvanceEstimationResult(
                [5, 10, 25], 0, 40, 0.9, "suffix lock diagnostic", [0.3, 0.3, 0.3]));
        var lockedPrefixCells = lockedPrefixSuffixRegion.CharacterCells.ToArray();
        if (!lockedPrefixChanged ||
            Math.Abs(lockedPrefixCells[0].Width - 10) > 0.001 ||
            Math.Abs(lockedPrefixCells[1].Width - 10) > 0.001 ||
            Math.Abs(lockedPrefixCells[2].Left - 20) > 0.001 ||
            Math.Abs(lockedPrefixSuffixRegion.Width - 60) > 0.001)
            throw new InvalidOperationException("A locked prefix incorrectly constrained the estimated trailing extent.");
        for (var index = 2; index < lockedPrefixSuffixRegion.TextElementCount; index++)
        {
            lockedPrefixSuffixRegion.SelectedCharacterIndex = index;
            lockedPrefixSuffixRegion.SetSelectedCharacterLocks(true);
        }
        lockedPrefixSuffixRegion.SelectedCharacterIndex = 2;
        if (lockedPrefixSuffixRegion.ApplyCharacterSuffixAdvanceEstimation(
                2,
                new CharacterAdvanceEstimationResult(
                    [8, 12, 30], 0, 50, 0.9, "locked suffix diagnostic", [0.3, 0.3, 0.3])))
            throw new InvalidOperationException("A fully locked suffix was reported as changed.");
        var lockedRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABCDE", 0, 0, 60, 20, true, CharacterAdvances: [10, 10, 20, 10, 10]));
        lockedRegion.SelectedCharacterIndex = 2;
        lockedRegion.SetSelectedCharacterLocks(true);
        var lockedSnapshot = lockedRegion.Capture();
        lockedRegion.ApplyCharacterAdvanceEstimation(new CharacterAdvanceEstimationResult(
            [5, 15, 20, 15, 5], 0, 60, 0.9, "lock diagnostic", [0.3, 0.3, 0.3, 0.3, 0.3]));
        var lockedCells = lockedRegion.CharacterCells.ToArray();
        if (!lockedCells[2].IsLocked || Math.Abs(lockedCells[2].Left - 20) > 0.001 ||
            Math.Abs(lockedCells[2].Width - 20) > 0.001 ||
            Math.Abs(lockedCells[0].Width - 5) > 0.001 || Math.Abs(lockedCells[1].Width - 15) > 0.001 ||
            Math.Abs(lockedCells[3].Width - 15) > 0.001 || Math.Abs(lockedCells[4].Width - 5) > 0.001)
            throw new InvalidOperationException("Automatic adjustment did not preserve the locked character boundary.");
        // 行幅の変更後に再度自動調整しても、固定文字の開始位置と送り幅は完全に維持される必要があります。
        // 以前は行幅との再整合処理が全送り幅を正規化し、固定済みの文字までわずかに変化していました。
        lockedRegion.Width = 70;
        lockedRegion.ApplyCharacterAdvanceEstimation(new CharacterAdvanceEstimationResult(
            [15, 5, 20, 10, 20], 0, 70, 0.9, "repeated lock diagnostic", [0.3, 0.3, 0.3, 0.3, 0.3]));
        var repeatedlyAdjustedLockedCells = lockedRegion.CharacterCells.ToArray();
        if (!repeatedlyAdjustedLockedCells[2].IsLocked ||
            Math.Abs(repeatedlyAdjustedLockedCells[2].Left - 20) > 0.001 ||
            Math.Abs(repeatedlyAdjustedLockedCells[2].Width - 20) > 0.001)
            throw new InvalidOperationException("Repeated automatic adjustment changed a locked character boundary.");
        var restoredLockedRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABCDE", 0, 0, 60, 20, true, CharacterAdvances: [12, 12, 12, 12, 12]));
        restoredLockedRegion.Apply(lockedSnapshot);
        if (!restoredLockedRegion.CharacterCells[2].IsLocked || restoredLockedRegion.CharacterCells.Count(cell => cell.IsLocked) != 1)
            throw new InvalidOperationException("Character lock state was not restored from an edit snapshot.");
        lockedRegion.IsGeometryLocked = true;
        if (lockedRegion.CanAutomaticallyAdjust)
            throw new InvalidOperationException("A geometry-locked region still allowed automatic adjustment.");
        var textEditLockRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABCDE", 0, 0, 60, 20, true, CharacterAdvances: [5, 10, 15, 20, 10]));
        textEditLockRegion.SelectedCharacterIndex = 1;
        textEditLockRegion.SetSelectedCharacterLocks(true);
        textEditLockRegion.SelectedCharacterIndex = 2;
        textEditLockRegion.SetSelectedCharacterLocks(true);
        textEditLockRegion.SelectedCharacterIndex = 4;
        textEditLockRegion.SetSelectedCharacterLocks(true);
        textEditLockRegion.Text = "ABXDE";
        var replacementCells = textEditLockRegion.CharacterCells.ToArray();
        if (Math.Abs(replacementCells[0].Width - 5) > 0.001 || Math.Abs(replacementCells[1].Width - 10) > 0.001 ||
            Math.Abs(replacementCells[2].Width - 15) > 0.001 || !replacementCells[1].IsLocked ||
            !replacementCells[2].IsLocked || !replacementCells[4].IsLocked)
            throw new InvalidOperationException("A same-length text correction reset preceding or locked character advances.");
        var insertionLockRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABCDE", 0, 0, 60, 20, true, CharacterAdvances: [5, 10, 15, 20, 10]));
        insertionLockRegion.SelectedCharacterIndex = 1;
        insertionLockRegion.SetSelectedCharacterLocks(true);
        insertionLockRegion.SelectedCharacterIndex = 4;
        insertionLockRegion.SetSelectedCharacterLocks(true);
        insertionLockRegion.Text = "ABXYDE";
        var insertionCells = insertionLockRegion.CharacterCells.ToArray();
        if (Math.Abs(insertionCells[0].Width - 5) > 0.001 || Math.Abs(insertionCells[1].Width - 10) > 0.001 ||
            Math.Abs(insertionCells[^1].Width - 10) > 0.001 || !insertionCells[1].IsLocked ||
            !insertionCells[^1].IsLocked || Math.Abs(insertionCells.Sum(cell => cell.Width) - 60) > 0.001)
            throw new InvalidOperationException("Inserted text did not preserve the prefix and locked suffix advances.");
        var verticalSuffixPixels = Enumerable.Repeat((byte)255, 20 * 60 * 4).ToArray();
        foreach (var (top, bottom) in new[] { (2, 9), (15, 37), (43, 57) })
        for (var y = top; y <= bottom; y++)
        for (var x = 3; x < 17; x++)
        {
            var offset = (y * 20 + x) * 4;
            verticalSuffixPixels[offset] = 0;
            verticalSuffixPixels[offset + 1] = 0;
            verticalSuffixPixels[offset + 2] = 0;
        }
        var verticalSuffixImage = BitmapSource.Create(20, 60, 96, 96, PixelFormats.Bgra32, null, verticalSuffixPixels, 20 * 4);
        verticalSuffixImage.Freeze();
        var verticalSuffixRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion(
            "ABC", 0, 0, 20, 60, true, IsVertical: true, CharacterAdvances: [10, 20, 30]));
        verticalSuffixRegion.SelectedCharacterIndex = 1;
        var verticalSuffixEstimationRegion = verticalSuffixRegion.CreateCharacterSuffixEstimationRegion(1);
        if (Math.Abs(verticalSuffixEstimationRegion.Top - 10) > 0.001 ||
            Math.Abs(verticalSuffixEstimationRegion.Height - 50) > 0.001)
            throw new InvalidOperationException("Vertical suffix geometry was not anchored below the fixed prefix.");
        var verticalSuffixEstimate = CharacterAdvanceEstimator.Estimate(verticalSuffixImage, verticalSuffixEstimationRegion);
        verticalSuffixRegion.ApplyCharacterSuffixAdvanceEstimation(1, verticalSuffixEstimate);
        var verticalSuffixCells = verticalSuffixRegion.CharacterCells.ToArray();
        if (Math.Abs(verticalSuffixCells[0].Height - 10) > 0.001 ||
            Math.Abs(verticalSuffixCells[1].Top - 10) > 0.001 ||
            verticalSuffixCells[1].Height <= verticalSuffixCells[2].Height)
            throw new InvalidOperationException("Vertical suffix-only adjustment changed the fixed prefix or lost proportional suffix heights.");
        var extendedPixels = Enumerable.Repeat((byte)255, 120 * 20 * 4).ToArray();
        foreach (var (left, right) in new[] { (2, 9), (15, 37), (43, 57) })
        for (var y = 3; y < 17; y++)
        for (var x = left; x <= right; x++)
        {
            var offset = (y * 120 + x) * 4;
            extendedPixels[offset] = 0;
            extendedPixels[offset + 1] = 0;
            extendedPixels[offset + 2] = 0;
        }
        var extendedImage = BitmapSource.Create(120, 20, 96, 96, PixelFormats.Bgra32, null, extendedPixels, 120 * 4);
        extendedImage.Freeze();
        var extendedRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("ABC", 0, 0, 120, 20, true));
        var trimmedEstimate = CharacterAdvanceEstimator.Estimate(extendedImage, extendedRegion);
        if (trimmedEstimate.Extent >= 90 || trimmedEstimate.LeadingOffset > 5)
            throw new InvalidOperationException("Image-based estimation did not discard a clearly empty trailing extent.");
        extendedRegion.ApplyCharacterAdvanceEstimation(trimmedEstimate);
        if (extendedRegion.Width >= 90 || Math.Abs(extendedRegion.CharacterCells.Sum(cell => cell.Width) - extendedRegion.Width) > 0.001)
            throw new InvalidOperationException("Estimated content extent was not applied to the OCR region.");
        if (!WritingDirectionDetector.IsLikelyVertical("縦書きの文章", 20, 120) ||
            WritingDirectionDetector.IsLikelyVertical("横書きの文章", 120, 20))
            throw new InvalidOperationException("Writing-direction inference failed.");
        var numericTextBox = new TextBox { Text = "10" };
        if (!global::PdfCorrectorium.App.MainWindow.AdjustNumericTextBox(numericTextBox, 1) ||
            !double.TryParse(numericTextBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var adjustedNumericValue) ||
            Math.Abs(adjustedNumericValue - 11) > 0.001)
            throw new InvalidOperationException("Numeric mouse-wheel adjustment failed.");
        var verticalPixels = Enumerable.Repeat((byte)255, 20 * 120 * 4).ToArray();
        foreach (var (top, bottom) in new[] { (2, 9), (15, 37), (43, 57) })
        for (var y = top; y <= bottom; y++)
        for (var x = 3; x < 17; x++)
        {
            var offset = (y * 20 + x) * 4;
            verticalPixels[offset] = 0;
            verticalPixels[offset + 1] = 0;
            verticalPixels[offset + 2] = 0;
        }
        var verticalImage = BitmapSource.Create(20, 120, 96, 96, PixelFormats.Bgra32, null, verticalPixels, 20 * 4);
        verticalImage.Freeze();
        var verticalEstimateRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("縦書字", 0, 0, 20, 120, true, IsVertical: true));
        var verticalEstimate = CharacterAdvanceEstimator.Estimate(verticalImage, verticalEstimateRegion);
        verticalEstimateRegion.ApplyCharacterAdvanceEstimation(verticalEstimate);
        if (verticalEstimateRegion.Height >= 90 || verticalEstimateRegion.CharacterCells.Any(cell => Math.Abs(cell.Width - 20) > 0.001) ||
            verticalEstimateRegion.CharacterCells[1].Height <= verticalEstimateRegion.CharacterCells[0].Height)
            throw new InvalidOperationException("Vertical image-based character segmentation used the wrong axis.");
        var boldPixels = Enumerable.Repeat((byte)238, 120 * 40 * 4).ToArray();
        for (var pixel = 3; pixel < boldPixels.Length; pixel += 4) boldPixels[pixel] = 255;
        foreach (var (left, right) in new[] { (3, 32), (38, 74), (80, 116) })
        for (var y = 3; y < 37; y++)
        for (var x = left; x <= right; x++)
        {
            var offset = (y * 120 + x) * 4;
            boldPixels[offset] = 20;
            boldPixels[offset + 1] = 20;
            boldPixels[offset + 2] = 20;
        }
        var boldImage = BitmapSource.Create(120, 40, 96, 96, PixelFormats.Bgra32, null, boldPixels, 120 * 4);
        boldImage.Freeze();
        var boldRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("の手術", 0, 0, 120, 40, true));
        var boldEstimate = CharacterAdvanceEstimator.Estimate(boldImage, boldRegion);
        var boldAdvances = boldEstimate.Advances.ToArray();
        if (boldAdvances.Length != 3 || boldAdvances.Any(advance => advance < 28 || advance > 50) ||
            Math.Abs(boldAdvances.Sum() - boldEstimate.Extent) > 0.001)
            throw new InvalidOperationException("A bold glyph color was incorrectly treated as the image background.");
        var splitStrokePixels = Enumerable.Repeat((byte)255, 120 * 30 * 4).ToArray();
        foreach (var (left, right) in new[]
                 {
                     (2, 8), (18, 25),        // 「い」: two deliberately separated strokes
                     (32, 37), (42, 47), (52, 57), // 「け」: three separated strokes
                     (62, 70), (76, 87),      // 「な」
                     (92, 98), (108, 115),    // 「い」
                 })
        for (var y = 4; y < 26; y++)
        for (var x = left; x <= right; x++)
        {
            var offset = (y * 120 + x) * 4;
            splitStrokePixels[offset] = 0;
            splitStrokePixels[offset + 1] = 0;
            splitStrokePixels[offset + 2] = 0;
        }
        var splitStrokeImage = BitmapSource.Create(120, 30, 96, 96, PixelFormats.Bgra32, null, splitStrokePixels, 120 * 4);
        splitStrokeImage.Freeze();
        var splitStrokeRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("いけない", 0, 0, 120, 30, true));
        var splitStrokeEstimate = CharacterAdvanceEstimator.Estimate(splitStrokeImage, splitStrokeRegion);
        var splitStrokeAdvances = splitStrokeEstimate.Advances.ToArray();
        if (splitStrokeAdvances.Length != 4 ||
            splitStrokeAdvances.Any(advance => advance < 22 || advance > 38) ||
            Math.Abs(splitStrokeAdvances.Sum() - splitStrokeEstimate.Extent) > 0.001)
            throw new InvalidOperationException(
                $"Separated kana strokes were mistaken for character boundaries: {string.Join(", ", splitStrokeAdvances.Select(value => value.ToString("0.0")))}.");
        var punctuationPixels = Enumerable.Repeat((byte)255, 120 * 30 * 4).ToArray();
        foreach (var (left, right, top, bottom) in new[]
                 {
                     (2, 7, 4, 25), (17, 27, 7, 11), (17, 27, 18, 22), // 「に」
                     (32, 36, 20, 25),                                  // 「、」: ink at the left of a full-em cell
                     (62, 87, 4, 25),                                   // 「次」
                     (92, 101, 5, 24), (108, 116, 10, 20),              // 「へ」
                 })
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var offset = (y * 120 + x) * 4;
            punctuationPixels[offset] = 0;
            punctuationPixels[offset + 1] = 0;
            punctuationPixels[offset + 2] = 0;
        }
        var punctuationImage = BitmapSource.Create(120, 30, 96, 96, PixelFormats.Bgra32, null, punctuationPixels, 120 * 4);
        punctuationImage.Freeze();
        var punctuationRegion = new OverlayRegionViewModel(new PdfTextOverlayRegion("に、次へ", 0, 0, 120, 30, true));
        var punctuationEstimate = CharacterAdvanceEstimator.Estimate(punctuationImage, punctuationRegion);
        var punctuationAdvances = punctuationEstimate.Advances.ToArray();
        if (punctuationAdvances.Length != 4 ||
            punctuationAdvances.Any(advance => advance < 22 || advance > 38) ||
            Math.Abs(punctuationAdvances[1] - punctuationAdvances[2]) > 10 ||
            Math.Abs(punctuationAdvances.Sum() - punctuationEstimate.Extent) > 0.001)
            throw new InvalidOperationException(
                $"A sparse full-width punctuation mark lost its advance: {string.Join(", ", punctuationAdvances.Select(value => value.ToString("0.0")))}.");
        var bracketPixels = Enumerable.Repeat((byte)255, 120 * 30 * 4).ToArray();
        foreach (var (left, right, top, bottom) in new[]
                 {
                     (2, 5, 4, 25), (2, 14, 4, 7),       // 「: sparse ink at the leading edge
                     (34, 56, 4, 25),                     // 型
                     (84, 87, 4, 25), (75, 87, 22, 25),  // 」: sparse ink at the trailing edge
                     (94, 116, 4, 25),                    // 値
                 })
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var offset = (y * 120 + x) * 4;
            bracketPixels[offset] = 0;
            bracketPixels[offset + 1] = 0;
            bracketPixels[offset + 2] = 0;
        }
        var bracketImage = BitmapSource.Create(120, 30, 96, 96, PixelFormats.Bgra32, null, bracketPixels, 120 * 4);
        bracketImage.Freeze();
        var bracketRegion = new OverlayRegionViewModel(
            new PdfTextOverlayRegion("\u300c\u578b\u300d\u5024", 0, 0, 120, 30, true));
        var bracketEstimate = CharacterAdvanceEstimator.Estimate(bracketImage, bracketRegion);
        var bracketAdvances = bracketEstimate.Advances.ToArray();
        if (bracketAdvances.Length != 4 ||
            bracketAdvances.Any(advance => advance < 22 || advance > 38) ||
            Math.Abs(bracketAdvances.Sum() - bracketEstimate.Extent) > 0.001)
            throw new InvalidOperationException(
                $"A Japanese bracket captured an adjacent glyph: {string.Join(", ", bracketAdvances.Select(value => value.ToString("0.0")))}.");
        viewModel.EditUnitIndex = (int)OcrEditUnit.Line;
        viewModel.SetOverlaySelection([referenceRegion, targetRegion], referenceRegion);
        viewModel.SelectedReviewStatus = ReviewStatus.Verified;
        if (referenceRegion.ReviewStatus != ReviewStatus.Verified || targetRegion.ReviewStatus != ReviewStatus.Verified)
            throw new InvalidOperationException("Review status was not applied to the complete selection.");
        viewModel.UndoCommand.Execute(null);
        if (referenceRegion.ReviewStatus == ReviewStatus.Verified || targetRegion.ReviewStatus == ReviewStatus.Verified)
            throw new InvalidOperationException("Review-status undo did not restore the previous values.");
        viewModel.SetOverlaySelection([referenceRegion], referenceRegion);
        viewModel.BeginOverlayEdit(referenceRegion);
        referenceRegion.Left += 1;
        viewModel.EndOverlayEdit("Review-status diagnostic");
        if (referenceRegion.ReviewStatus != ReviewStatus.Modified)
            throw new InvalidOperationException("Editing an OCR region did not mark it as modified.");
        viewModel.SetOverlaySelection([referenceRegion], referenceRegion);
        viewModel.DeleteOcrRegionsCommand.Execute(null);
        if (!referenceRegion.IsDeleted) throw new InvalidOperationException("OCR-region deletion did not create a deletion marker.");
        if (targetRegion.ReadingOrder != 1)
            throw new InvalidOperationException("OCR-region deletion did not close the reading-order gap.");
        viewModel.UndoCommand.Execute(null);
        if (referenceRegion.IsDeleted) throw new InvalidOperationException("OCR-region deletion undo did not restore the region.");
        if (referenceRegion.ReadingOrder != 1 || targetRegion.ReadingOrder != 2)
            throw new InvalidOperationException("OCR-region deletion undo did not restore the previous reading order.");
        viewModel.RedoCommand.Execute(null);
        if (!referenceRegion.IsDeleted) throw new InvalidOperationException("OCR-region deletion redo did not hide the region again.");
        if (targetRegion.ReadingOrder != 1)
            throw new InvalidOperationException("OCR-region deletion redo did not restore the compact reading order.");
        viewModel.UndoCommand.Execute(null);
        if (referenceRegion.ReadingOrder != 1 || targetRegion.ReadingOrder != 2)
            throw new InvalidOperationException("OCR-region deletion final undo did not restore the previous reading order.");
        var manualSnapshot = new OverlayRegionSnapshot("", 25, 35, 180, 36, 0, 3, "", "", ReviewStatus.Modified);
        var manualRegion = new OverlayRegionViewModel(
            Guid.NewGuid(),
            "",
            manualSnapshot with { IsDeleted = true },
            manualSnapshot,
            true,
            false,
            "manual",
            null,
            true,
            false);
        if (!manualRegion.IsAdded || !manualRegion.IsModified)
            throw new InvalidOperationException("A manually added OCR region was not marked for PDF output.");
        var duplicateDeletionGeometry = new TextGeometry
        {
            LocalBounds = new PdfRectangle(new PdfPoint(79.2, 221.28), new PdfSize(440.64, 60.72)),
            RotationCenter = new PdfPoint(299.52, 251.64),
        };
        var firstDeletion = new OcrTextRegion
        {
            PageId = Guid.NewGuid(),
            OriginalText = "構築実践ガイド",
            OriginalGeometry = duplicateDeletionGeometry,
            EditedGeometry = duplicateDeletionGeometry,
            IsDeleted = true,
        };
        var duplicateDeletionGeometryOffset = duplicateDeletionGeometry with
        {
            LocalBounds = new PdfRectangle(new PdfPoint(78.96, 221.28), new PdfSize(440.88, 60.72)),
        };
        var secondDeletion = firstDeletion with
        {
            Id = Guid.NewGuid(),
            OriginalGeometry = duplicateDeletionGeometryOffset,
            EditedGeometry = duplicateDeletionGeometryOffset,
        };
        var separateDeletionGeometry = duplicateDeletionGeometry with
        {
            LocalBounds = new PdfRectangle(new PdfPoint(79.2, 321.28), new PdfSize(440.64, 60.72)),
        };
        var separateDeletion = firstDeletion with
        {
            Id = Guid.NewGuid(),
            OriginalGeometry = separateDeletionGeometry,
            EditedGeometry = separateDeletionGeometry,
        };
        if (!PdfExportService.IsDuplicateDeletionRequestForDiagnostics(secondDeletion, firstDeletion) ||
            PdfExportService.IsDuplicateDeletionRequestForDiagnostics(separateDeletion, firstDeletion))
            throw new InvalidOperationException("Duplicate PDF text deletion requests were not distinguished by strict source overlap.");
        var lineObjectGeometry = new TextGeometry
        {
            LocalBounds = new PdfRectangle(new PdfPoint(10, 20), new PdfSize(120, 20)),
            RotationCenter = new PdfPoint(70, 30),
            CharacterAdvances = [20, 35, 15, 25],
        };
        var horizontalLineObject = new OcrTextRegion
        {
            PageId = Guid.NewGuid(),
            OriginalText = "文、字。",
            EditedText = "文、字。",
            OriginalGeometry = lineObjectGeometry,
            EditedGeometry = lineObjectGeometry,
            WritingMode = WritingMode.Horizontal,
            OriginalWritingMode = WritingMode.Horizontal,
        };
        if (!PdfExportService.PreservesLineTextObjectForDiagnostics(horizontalLineObject))
            throw new InvalidOperationException("A horizontal OCR line would be fragmented into per-character PDF objects.");
        var verticalLineObject = horizontalLineObject with
        {
            WritingMode = WritingMode.Vertical,
            OriginalWritingMode = WritingMode.Vertical,
        };
        if (!PdfExportService.PreservesLineTextObjectForDiagnostics(verticalLineObject))
            throw new InvalidOperationException("A native vertical OCR line would be fragmented into per-character PDF objects.");
        var convertedVerticalLineObject = verticalLineObject with
        {
            OriginalWritingMode = WritingMode.Horizontal,
        };
        if (PdfExportService.PreservesLineTextObjectForDiagnostics(convertedVerticalLineObject))
            throw new InvalidOperationException("A writing-mode conversion incorrectly reused incompatible source font metrics.");
        if (!PdfExportService.SupportsTextSpacingOperandsForDiagnostics())
            throw new InvalidOperationException("PDF text string operands could not be converted to line-level spacing commands.");
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "PdfCorrectorium-settings-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsPaths = new ApplicationPaths(StorageMode.Portable, settingsDirectory, settingsDirectory, settingsDirectory, settingsDirectory);
            var settingsService = new ApplicationSettingsService(settingsPaths);
            settingsService.SaveAsync(new ApplicationSettings
            {
                CharacterHandleThickness = 3,
                CharacterHandleOpacity = 0.4,
                CharacterCellBorderThickness = 1.6,
                OcrOverlayColor = "#123456",
                UndoHistoryLimit = 75,
                CharacterEstimationMinimumAspectRatio = 0.25,
                CharacterEstimationMaximumAspectRatio = 1.40,
                CharacterEstimationUniformity = 0.60,
                CharacterEstimationInkCoverage = 0.20,
                CharacterEstimationGlyphPrior = 0.75,
                ShowUnselectedCharacterCellBorders = false,
                ShowPageThumbnails = true,
                ShowToolbarText = true,
                ToolbarButtonSize = 48,
                ShowPropertyHelpText = true,
                ShowPageListPanel = false,
                ShowPropertiesPanel = true,
                ShowStatusBar = false,
                PageListWidth = 280,
                PropertiesPanelWidth = 410,
                PreviousCharacterShortcut = "Ctrl+Alt+Left",
                NextCharacterShortcut = "Ctrl+Alt+Right",
                DecreaseCharacterAdvanceShortcut = "Ctrl+Alt+Down",
                IncreaseCharacterAdvanceShortcut = "Ctrl+Alt+Up",
                EstimateCharacterAdvancesShortcut = "F8",
                EstimateCharacterSuffixAdvancesShortcut = "F9",
                EqualizeCharacterAdvancesShortcut = "Ctrl+Alt+E",
                RestoreOriginalCharacterAdvancesShortcut = "Ctrl+Alt+R",
            }).GetAwaiter().GetResult();
            var loadedSettings = settingsService.Load();
            if (loadedSettings.CharacterHandleThickness != 3 || loadedSettings.CharacterHandleOpacity != 0.4 ||
                loadedSettings.CharacterCellBorderThickness != 1.6 ||
                loadedSettings.OcrOverlayColor != "#123456" || loadedSettings.UndoHistoryLimit != 75 ||
                loadedSettings.CharacterEstimationMinimumAspectRatio != 0.25 ||
                loadedSettings.CharacterEstimationMaximumAspectRatio != 1.40 ||
                loadedSettings.CharacterEstimationUniformity != 0.60 ||
                loadedSettings.CharacterEstimationInkCoverage != 0.20 ||
                loadedSettings.CharacterEstimationGlyphPrior != 0.75 ||
                loadedSettings.ShowUnselectedCharacterCellBorders ||
                !loadedSettings.ShowPageThumbnails ||
                !loadedSettings.ShowToolbarText ||
                loadedSettings.ToolbarButtonSize != 48 ||
                !loadedSettings.ShowPropertyHelpText ||
                loadedSettings.ShowPageListPanel ||
                !loadedSettings.ShowPropertiesPanel ||
                loadedSettings.ShowStatusBar ||
                loadedSettings.PageListWidth != 280 ||
                loadedSettings.PropertiesPanelWidth != 410 ||
                loadedSettings.PreviousCharacterShortcut != "Ctrl+Alt+Left" ||
                loadedSettings.NextCharacterShortcut != "Ctrl+Alt+Right" ||
                loadedSettings.DecreaseCharacterAdvanceShortcut != "Ctrl+Alt+Down" ||
                loadedSettings.IncreaseCharacterAdvanceShortcut != "Ctrl+Alt+Up" ||
                loadedSettings.EstimateCharacterAdvancesShortcut != "F8" ||
                loadedSettings.EstimateCharacterSuffixAdvancesShortcut != "F9" ||
                loadedSettings.EqualizeCharacterAdvancesShortcut != "Ctrl+Alt+E" ||
                loadedSettings.RestoreOriginalCharacterAdvancesShortcut != "Ctrl+Alt+R")
                throw new InvalidOperationException("Application settings did not persist and reload.");
        }
        finally
        {
            if (Directory.Exists(settingsDirectory)) Directory.Delete(settingsDirectory, recursive: true);
        }
        var caseInsensitiveMatches = MainWindowViewModel.FindTextOccurrences(
            "Alpha alpha ALPHA",
            "alpha",
            StringComparison.OrdinalIgnoreCase);
        if (!caseInsensitiveMatches.SequenceEqual([0, 6, 12]))
            throw new InvalidOperationException("Case-insensitive OCR text search failed.");
        var caseSensitiveMatches = MainWindowViewModel.FindTextOccurrences(
            "Alpha alpha ALPHA",
            "alpha",
            StringComparison.Ordinal);
        if (!caseSensitiveMatches.SequenceEqual([6]))
            throw new InvalidOperationException("Case-sensitive OCR text search failed.");
        _diagnostics?.Write("editor-test.pass", "Zoom; geometry; multi-selection; reading order; pronunciations; proportional hit testing; equalize/restore; review status; application settings; edit-unit behavior; OCR text search passed");
        Shutdown(0);
    }

    private void RunPdfExportTest(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 2)
            throw new ArgumentException("--pdf-export-test requires an input PDF and an output PDF path.");
        var inputPath = Path.GetFullPath(arguments[optionIndex + 1]);
        var outputPath = Path.GetFullPath(arguments[optionIndex + 2]);
        var paths = ApplicationPathResolver.Resolve(AppContext.BaseDirectory);
        ApplicationPathResolver.EnsureDirectories(paths);
        var previewService = new PdfPreviewService();
        var viewModel = new MainWindowViewModel(
            new ProjectPackageService(),
            previewService,
            new PdfExportService(),
            new NdlOcrCompanionService(),
            new DiagnosticLog(paths.LogDirectory),
            paths,
            () => { });
        var result = Task.Run(async () =>
        {
            await viewModel.LoadPdfForDiagnosticsAsync(inputPath);
            var region = viewModel.OverlayItems.FirstOrDefault()
                ?? throw new InvalidDataException("The test PDF did not produce an editable text overlay.");
            region.Left += 3;
            region.RotationDegrees = 4;
            if (region.TextElementCount > 1)
            {
                region.IsVertical = !region.IsVertical;
                var textElementIndexes = System.Globalization.StringInfo.ParseCombiningCharacters(region.Text);
                var firstElementEnd = textElementIndexes.Length > 1 ? textElementIndexes[1] : region.Text.Length;
                region.Text = region.Text[..firstElementEnd] + " " + region.Text[firstElementEnd..];
                region.SelectedCharacterIndex = 1;
                region.SelectedCharacterAdvance *= 1.25;
            }
            var addedRegion = viewModel.AddManualOcrRegion(new Rect(
                region.Left,
                Math.Min(viewModel.PreviewPixelHeight - 120, region.Top + region.Height + 8),
                120,
                40))
                ?? throw new InvalidDataException("The PDF export test could not add a transparent-text region.");
            addedRegion.Text = region.OriginalText;
            addedRegion.IsVertical = true;
            addedRegion.RotationDegrees = 11;
            var deletionRegion = viewModel.OverlayItems.FirstOrDefault(item =>
                !ReferenceEquals(item, region) &&
                !ReferenceEquals(item, addedRegion) &&
                !item.IsDeleted);
            if (deletionRegion is not null)
            {
                viewModel.SetOverlaySelection([deletionRegion], deletionRegion);
                viewModel.DeleteOcrRegionsCommand.Execute(null);
            }
            var export = await viewModel.ExportPdfForDiagnosticsAsync(outputPath);
            var reopened = await previewService.RenderPageAsync(outputPath, 1, viewModel.PreviewPixelWidth);
            if (reopened.PageCount <= 0 || reopened.Image.PixelWidth <= 0)
                throw new InvalidDataException("The exported PDF could not be rendered.");
            var rotatedCharacters = reopened.TextRegions
                .Where(item => Math.Abs(item.RotationDegrees - 4) < 0.75)
                .ToArray();
            if (rotatedCharacters.Length < 2)
                throw new InvalidDataException("The exported PDF rotation did not round-trip through PDF coordinates.");
            var horizontalSpan = rotatedCharacters.Max(item => item.Left + item.Width / 2d) -
                                 rotatedCharacters.Min(item => item.Left + item.Width / 2d);
            var verticalSpan = rotatedCharacters.Max(item => item.Top + item.Height / 2d) -
                               rotatedCharacters.Min(item => item.Top + item.Height / 2d);
            if (region.IsVertical ? verticalSpan <= horizontalSpan : horizontalSpan <= verticalSpan)
                throw new InvalidDataException(
                    $"The exported PDF writing direction did not round-trip through character positions. " +
                    $"Expected vertical={region.IsVertical}; horizontal span={horizontalSpan:0.##}; vertical span={verticalSpan:0.##}.");
            var addedCharacters = reopened.TextRegions
                .Where(item => Math.Abs(item.RotationDegrees - 11) < 0.75)
                .ToArray();
            if (addedCharacters.Length < 2)
                throw new InvalidDataException("The added vertical invisible text did not round-trip through PDF output.");
            var addedHorizontalSpan = addedCharacters.Max(item => item.Left + item.Width / 2d) -
                                      addedCharacters.Min(item => item.Left + item.Width / 2d);
            var addedVerticalSpan = addedCharacters.Max(item => item.Top + item.Height / 2d) -
                                    addedCharacters.Min(item => item.Top + item.Height / 2d);
            if (addedVerticalSpan <= addedHorizontalSpan)
                throw new InvalidDataException("The added vertical invisible text was not arranged from top to bottom.");
            if (deletionRegion is not null && reopened.TextRegions.Any(item =>
                    item.Text == deletionRegion.Text &&
                    Math.Abs(item.Left - deletionRegion.Left) < 4 &&
                    Math.Abs(item.Top - deletionRegion.Top) < 4))
                throw new InvalidDataException("The deleted text object remained in the exported PDF.");
            var expectedChanges = deletionRegion is null ? 2 : 3;
            if (export.ModifiedRegions < expectedChanges)
                throw new InvalidDataException("Added and deleted transparent-text regions were not included in PDF output.");
            return export;
        }).GetAwaiter().GetResult();
        _diagnostics?.Write("pdf-export-test.pass", $"Pages: {result.ModifiedPages}; regions: {result.ModifiedRegions}; output: {outputPath}");
        Shutdown(0);
    }

    private void RunEditorProjectTest(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 2)
            throw new ArgumentException("--editor-project-test requires an input PDF and an output project path.");
        var pdfPath = Path.GetFullPath(arguments[optionIndex + 1]);
        var projectPath = Path.GetFullPath(arguments[optionIndex + 2]);
        var paths = ApplicationPathResolver.Resolve(AppContext.BaseDirectory);
        ApplicationPathResolver.EnsureDirectories(paths);
        var packageService = new ProjectPackageService();
        var viewModel = new MainWindowViewModel(
            packageService,
            new PdfPreviewService(),
            new PdfExportService(),
            new NdlOcrCompanionService(),
            new DiagnosticLog(paths.LogDirectory),
            paths,
            () => { });
        var reopened = Task.Run(async () =>
        {
            await viewModel.LoadPdfForDiagnosticsAsync(pdfPath);
            if (viewModel.HasUnsavedChanges)
                throw new InvalidOperationException("A newly opened PDF was incorrectly marked as modified.");
            var region = viewModel.OverlayItems.FirstOrDefault()
                ?? throw new InvalidDataException("The test PDF did not produce an OCR overlay.");
            region.Text = "保存テスト";
            region.Left += 8;
            region.Width += 12;
            region.RotationDegrees = 7.5;
            var savedWritingMode = region.IsVertical ? WritingMode.Horizontal : WritingMode.Vertical;
            region.IsVertical = savedWritingMode == WritingMode.Vertical;
            region.WordReadingsText = "今日=きょう\n一日=ついたち";
            region.SelectedCharacterIndex = 0;
            region.SelectedCharacterAdvance += 3;
            region.ReviewStatus = ReviewStatus.NeedsReview;
            var addedRegion = viewModel.AddManualOcrRegion(new Rect(
                region.Left,
                Math.Min(viewModel.PreviewPixelHeight - Math.Max(24, region.Height), region.Top + region.Height + 8),
                Math.Max(80, region.Width),
                Math.Max(24, region.Height)))
                ?? throw new InvalidDataException("A manual OCR region could not be added.");
            addedRegion.Text = region.OriginalText;
            var deletionRegion = viewModel.OverlayItems.FirstOrDefault(item =>
                !ReferenceEquals(item, region) &&
                !ReferenceEquals(item, addedRegion) &&
                !item.IsDeleted);
            if (deletionRegion is not null)
            {
                viewModel.SetOverlaySelection([deletionRegion], deletionRegion);
                viewModel.DeleteOcrRegionsCommand.Execute(null);
            }
            if (!viewModel.HasUnsavedChanges)
                throw new InvalidOperationException("OCR edits did not mark the project as having unsaved changes.");
            await viewModel.SaveProjectForDiagnosticsAsync(projectPath);
            if (viewModel.HasUnsavedChanges)
                throw new InvalidOperationException("Saving did not clear the unsaved-change state.");
            var saved = await packageService.OpenAsync(projectPath);
            var savedRegion = saved.Pages.SelectMany(page => page.TextRegions).FirstOrDefault(item => item.Id == region.Id)
                ?? throw new InvalidDataException("The edited OCR region was not saved.");
            var savedAddedRegion = saved.Pages.SelectMany(page => page.TextRegions).FirstOrDefault(item => item.Id == addedRegion.Id)
                ?? throw new InvalidDataException("The manually added OCR region was not saved.");
            if (!savedAddedRegion.IsAdded || savedAddedRegion.IsDeleted || savedAddedRegion.EffectiveText != region.OriginalText)
                throw new InvalidDataException("The manually added OCR region did not round-trip through the project file.");
            if (deletionRegion is not null)
            {
                var savedDeletedRegion = saved.Pages.SelectMany(page => page.TextRegions).FirstOrDefault(item => item.Id == deletionRegion.Id)
                    ?? throw new InvalidDataException("The deleted OCR region marker was not saved.");
                if (!savedDeletedRegion.IsDeleted)
                    throw new InvalidDataException("The OCR-region deletion marker did not round-trip through the project file.");
            }
            if (savedRegion.ReviewStatus != ReviewStatus.NeedsReview)
                throw new InvalidDataException("The review status did not round-trip through the project file.");
            if (savedRegion.EffectiveText != "保存テスト" || !savedRegion.IsModified || savedRegion.EditedGeometry.RotationDegrees != 7.5 ||
                savedRegion.WritingMode != savedWritingMode || !savedRegion.HasExplicitWritingMode ||
                savedRegion.OriginalWritingMode == savedRegion.WritingMode ||
                savedRegion.EditedGeometry.CharacterAdvances.Count != region.TextElementCount ||
                savedRegion.EditedGeometry.CharacterAdvances.DistinctBy(value => Math.Round(value, 3)).Count() < 2 ||
                savedRegion.WordReadings.Count != 2 || savedRegion.WordReadings[0].ReadingText != "きょう")
                throw new InvalidDataException("The saved OCR edit did not round-trip.");
            region.RotationDegrees = 8.5;
            if (!viewModel.HasUnsavedChanges)
                throw new InvalidOperationException("An edit after saving did not restore the unsaved-change state.");
            await viewModel.SaveCurrentProjectForDiagnosticsAsync();
            if (viewModel.HasUnsavedChanges)
                throw new InvalidOperationException("Overwrite save did not clear the unsaved-change state.");
            var overwritten = await packageService.OpenAsync(projectPath);
            var overwrittenRegion = overwritten.Pages.SelectMany(page => page.TextRegions).FirstOrDefault(item => item.Id == region.Id)
                ?? throw new InvalidDataException("The overwritten project lost the edited OCR region.");
            if (overwrittenRegion.EditedGeometry.RotationDegrees != 8.5)
                throw new InvalidDataException("Overwrite save did not update the existing project path.");
            return overwritten;
        }).GetAwaiter().GetResult();
        _diagnostics?.Write("editor-project-test.pass", $"Saved, overwritten, and reopened {reopened.Pages.Count} OCR page(s) with edited text, geometry, and rotation");
        Shutdown(0);
    }

    private void RunProjectExportTest(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 2)
            throw new ArgumentException("--project-export-test requires a project path and an output PDF path.");
        var projectPath = Path.GetFullPath(arguments[optionIndex + 1]);
        var outputPath = Path.GetFullPath(arguments[optionIndex + 2]);
        var paths = ApplicationPathResolver.Resolve(AppContext.BaseDirectory);
        ApplicationPathResolver.EnsureDirectories(paths);
        var packages = new ProjectPackageService();
        var result = Task.Run(async () =>
        {
            var project = await packages.OpenAsync(projectPath);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            if (!await packages.VerifySourceAsync(project.SourcePdf, projectDirectory))
                throw new InvalidDataException("The project source PDF is missing or does not match its fingerprint.");
            var sourcePath = project.SourcePdf.IsEmbedded
                ? await packages.MaterializeEmbeddedSourceAsync(projectPath, project.SourcePdf, paths.CacheDirectory)
                : packages.ResolveSourcePath(project.SourcePdf, projectDirectory);
            var export = await new PdfExportService().ExportAsync(sourcePath, outputPath, project);
            var reopened = await new PdfPreviewService().RenderPageAsync(outputPath, 1, 640);
            if (reopened.PageCount != project.SourcePdf.PageCount || reopened.Image.PixelWidth <= 0)
                throw new InvalidDataException("The project export could not be reopened and rendered.");
            return export;
        }).GetAwaiter().GetResult();
        _diagnostics?.Write(
            "project-export-test.pass",
            $"Pages: {result.ModifiedPages}; regions: {result.ModifiedRegions}; warnings: {string.Join(" | ", result.Warnings)}; output: {outputPath}");
        Shutdown(0);
    }

    private void RunBookmarkTest(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 2)
            throw new ArgumentException("--bookmark-test requires an input PDF and an output PDF path.");
        var inputPath = Path.GetFullPath(arguments[optionIndex + 1]);
        var outputPath = Path.GetFullPath(arguments[optionIndex + 2]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var expected = new[]
        {
            new PdfBookmark
            {
                Title = "第1章",
                PageNumber = 1,
                Children =
                [
                    new PdfBookmark { Title = "第1節", PageNumber = 1 },
                ],
            },
            new PdfBookmark { Title = "付録", PageNumber = 1, IsExpanded = false },
        };
        var expectedMetadata = new PdfDocumentMetadata
        {
            Title = "PDF校正文書",
            Author = "校正 太郎",
            Subject = "文書情報出力テスト",
            Keywords = "PDF, 校正, テスト",
            Creator = "PDF Correctorium",
            Producer = "PDF Correctorium Exporter",
        };
        var service = new PdfBookmarkService();
        var exchangePath = Path.ChangeExtension(outputPath, ".pdfbookmarks.json");
        var (actual, actualMetadata) = Task.Run(async () =>
        {
            await service.ExportAsync(exchangePath, expected);
            var imported = await service.ImportAsync(exchangePath);
            if (imported.Count != 2 ||
                imported[0].Children.Count != 1 ||
                imported[0].Children[0].Title != "第1節")
                throw new InvalidDataException("Bookmark JSON import/export did not preserve the hierarchy.");
            foreach (var extension in new[] { ".txt", ".xml" })
            {
                var formatPath = Path.ChangeExtension(outputPath, extension);
                await service.ExportAsync(formatPath, expected);
                var formatImported = await service.ImportAsync(formatPath);
                if (formatImported.Count != 2 ||
                    formatImported[0].Title != "第1章" ||
                    formatImported[0].Children.Count != 1 ||
                    formatImported[0].Children[0].Title != "第1節" ||
                    formatImported[1].PageNumber != 1)
                    throw new InvalidDataException($"Bookmark {extension} import/export did not preserve the hierarchy.");
            }
            var file = new FileInfo(inputPath);
            var project = new PdfCorrectoriumProject
            {
                Name = Path.GetFileNameWithoutExtension(inputPath),
                SourcePdf = new SourcePdfReference
                {
                    FileName = file.Name,
                    AbsolutePathHint = inputPath,
                    Sha256 = string.Empty,
                    FileSize = file.Length,
                },
                Bookmarks = imported,
                BookmarksInitialized = true,
                BookmarksModified = true,
                DocumentMetadata = expectedMetadata,
                OutputPdfVersion = PdfOutputVersion.Pdf15,
                DocumentLanguage = "ja-JP",
            };
            await new PdfExportService().ExportAsync(inputPath, outputPath, project);
            var pdf14OutputPath = Path.Combine(
                Path.GetDirectoryName(outputPath)!,
                Path.GetFileNameWithoutExtension(outputPath) + ".pdf14.pdf");
            await new PdfExportService().ExportAsync(
                inputPath,
                pdf14OutputPath,
                project with { OutputPdfVersion = PdfOutputVersion.Pdf14 });
            var pdf14Properties = await PdfDocumentPropertiesService.ReadAsync(
                pdf14OutputPath,
                CancellationToken.None);
            if (pdf14Properties.PdfVersionText != "1.4")
                throw new InvalidDataException(
                    $"Selected PDF output version 1.4 was not preserved. Actual: {pdf14Properties.PdfVersionText}");
            var languageRemovedOutputPath = Path.Combine(
                Path.GetDirectoryName(outputPath)!,
                Path.GetFileNameWithoutExtension(outputPath) + ".language-removed.pdf");
            await new PdfExportService().ExportAsync(
                outputPath,
                languageRemovedOutputPath,
                project with { DocumentLanguage = string.Empty });
            var languageRemovedProperties = await PdfDocumentPropertiesService.ReadAsync(
                languageRemovedOutputPath,
                CancellationToken.None);
            if (!string.IsNullOrEmpty(languageRemovedProperties.LanguageText))
                throw new InvalidDataException(
                    $"Clearing the document language did not remove /Lang. Actual: {languageRemovedProperties.LanguageText}");
            return (
                await service.ReadFromPdfAsync(outputPath),
                await PdfDocumentPropertiesService.ReadAsync(outputPath, CancellationToken.None));
        }).GetAwaiter().GetResult();
        if (actual.Count != 2 ||
            actual[0].Title != "第1章" ||
            actual[0].PageNumber != 1 ||
            actual[0].Children.Count != 1 ||
            actual[0].Children[0].Title != "第1節" ||
            actual[1].Title != "付録")
            throw new InvalidDataException("PDF bookmark round-trip did not preserve hierarchy, title, and destination page.");
        if (actualMetadata.Title != expectedMetadata.Title ||
            actualMetadata.Author != expectedMetadata.Author ||
            actualMetadata.Subject != expectedMetadata.Subject ||
            actualMetadata.Keywords != expectedMetadata.Keywords ||
            actualMetadata.Creator != expectedMetadata.Creator ||
            actualMetadata.Producer != expectedMetadata.Producer)
            throw new InvalidDataException("PDF document metadata round-trip did not preserve the edited values.");
        if (actualMetadata.PdfVersionText != "1.5")
            throw new InvalidDataException(
                $"Selected PDF output version 1.5 was not preserved. Actual: {actualMetadata.PdfVersionText}");
        if (actualMetadata.LanguageText != "ja-JP")
            throw new InvalidDataException(
                $"Selected document language ja-JP was not preserved. Actual: {actualMetadata.LanguageText}");
        _diagnostics?.Write(
            "bookmark-test.pass",
            $"Top-level: {actual.Count}; total: {actual.Sum(item => 1 + item.Children.Count)}; title: {actualMetadata.Title}; output: {outputPath}");
        Shutdown(0);
    }

    private void RunImageOptimizeTest(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 3)
            throw new ArgumentException("--image-optimize-test requires an input PDF, output PDF, and page number.");
        var inputPath = Path.GetFullPath(arguments[optionIndex + 1]);
        var outputPath = Path.GetFullPath(arguments[optionIndex + 2]);
        if (!int.TryParse(arguments[optionIndex + 3], out var pageNumber) || pageNumber < 1)
            throw new ArgumentException("The image optimization page number is invalid.");

        var result = Task.Run(async () =>
        {
            var preview = new PdfPreviewService();
            var exporter = new PdfExportService();
            var options = new PageImageOptimization();
            var analysis = await exporter.AnalyzePageImageOptimizationAsync(inputPath, pageNumber, options);
            var documentAnalysis = await exporter.AnalyzeDocumentImageOptimizationAsync(inputPath, options);
            _diagnostics?.Write(
                "image-optimize-test.analysis",
                $"Page: {pageNumber}; can optimize: {analysis.CanOptimize}; retained: {analysis.RetainedRegionCount}; " +
                $"blank images: {analysis.RemovableBlankImages}; document candidates: {documentAnalysis.Candidates.Count}; " +
                $"removed: {analysis.Regions.Count}; area reduction: {analysis.EstimatedAreaReduction:P1}; " +
                $"JPEG quality: {analysis.EstimatedJpegQuality}; regions: " +
                string.Join(" | ", analysis.Regions.Select(region =>
                    $"{region.Description} ({region.LeftRatio:0.###},{region.TopRatio:0.###}," +
                    $"{region.WidthRatio:0.###},{region.HeightRatio:0.###})")));
            if (!analysis.CanOptimize)
                throw new InvalidDataException($"Page {pageNumber} did not contain an eligible full-page image: {analysis.Message}");
            if (!documentAnalysis.Candidates.Any(candidate => candidate.PageNumber == pageNumber))
                throw new InvalidDataException($"Document analysis did not include page {pageNumber} as an optimization candidate.");
            if (documentAnalysis.EstimatedPdfBytes > documentAnalysis.SourcePdfBytes)
                throw new InvalidDataException("Document analysis estimated a larger PDF after optimization.");
            if (analysis.RemovableBlankImages == 0 && analysis.EstimatedJpegQuality is < 80 or > 94)
                throw new InvalidDataException(
                    $"Page {pageNumber} selected an unsafe JPEG quality: {analysis.EstimatedJpegQuality}.");
            if (analysis.RemovableBlankImages > 0 && analysis.EstimatedEncodedBytes != 0)
                throw new InvalidDataException("A removable blank image still has an estimated encoded size.");
            var before = await preview.RenderPageAsync(inputPath, pageNumber, 1200);
            var eligiblePageNumbers = new List<int>();
            var beforePages = new Dictionary<int, PdfPreviewResult>();
            var projectPages = new List<OcrPage>();
            foreach (var candidatePage in new[] { pageNumber, 4, 33, 151 }.Distinct())
            {
                if (candidatePage < 1 || candidatePage > before.PageCount) continue;
                var candidateAnalysis = candidatePage == pageNumber
                    ? analysis
                    : await exporter.AnalyzePageImageOptimizationAsync(inputPath, candidatePage, options);
                if (!candidateAnalysis.CanOptimize) continue;
                var candidatePreview = candidatePage == pageNumber
                    ? before
                    : await preview.RenderPageAsync(inputPath, candidatePage, 1200);
                eligiblePageNumbers.Add(candidatePage);
                beforePages[candidatePage] = candidatePreview;
                projectPages.Add(new OcrPage
                {
                    PageNumber = candidatePage,
                    WidthPoints = candidatePreview.PageWidthPoints,
                    HeightPoints = candidatePreview.PageHeightPoints,
                    ImageOptimization = options,
                });
            }
            int? skippedOptimizationPage = null;
            for (var candidatePage = 1; candidatePage <= Math.Min(before.PageCount, 12); candidatePage++)
            {
                if (eligiblePageNumbers.Contains(candidatePage)) continue;
                var candidateAnalysis = await exporter.AnalyzePageImageOptimizationAsync(
                    inputPath,
                    candidatePage,
                    options);
                if (candidateAnalysis.CanOptimize) continue;
                skippedOptimizationPage = candidatePage;
                break;
            }
            if (skippedOptimizationPage is int skippedPage)
            {
                projectPages.Add(new OcrPage
                {
                    PageNumber = skippedPage,
                    ImageOptimization = options,
                });
            }
            var project = new PdfCorrectoriumProject
            {
                Name = "image-optimization-diagnostic",
                SourcePdf = new SourcePdfReference
                {
                    FileName = Path.GetFileName(inputPath),
                    AbsolutePathHint = inputPath,
                    Sha256 = string.Empty,
                    FileSize = new FileInfo(inputPath).Length,
                    PageCount = before.PageCount,
                },
                Pages = projectPages,
            };
            var export = await exporter.ExportAsync(inputPath, outputPath, project);
            if (export.OptimizedImages < eligiblePageNumbers.Count)
                throw new InvalidDataException(
                    $"The export optimized only {export.OptimizedImages} of {eligiblePageNumbers.Count} eligible images.");
            if (skippedOptimizationPage is int expectedSkippedPage &&
                !export.Warnings.Any(warning => warning.StartsWith(
                    $"{expectedSkippedPage}ページ:",
                    StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"The export did not report the skipped image optimization on page {expectedSkippedPage}.");
            }
            var baselinePath = outputPath + ".baseline.pdf";
            long baselineBytes;
            try
            {
                var baselineProject = project with { Pages = [] };
                await exporter.ExportAsync(inputPath, baselinePath, baselineProject);
                baselineBytes = new FileInfo(baselinePath).Length;
            }
            finally
            {
                if (File.Exists(baselinePath)) File.Delete(baselinePath);
            }
            var visualDifferences = new List<double>();
            foreach (var eligiblePageNumber in eligiblePageNumbers)
            {
                var after = await preview.RenderPageAsync(outputPath, eligiblePageNumber, 1200);
                var visualDifference = CalculateBitmapDifference(
                    beforePages[eligiblePageNumber].Image,
                    after.Image);
                if (visualDifference > 1.5d)
                {
                    throw new InvalidDataException(
                        $"Optimized page {eligiblePageNumber} changed visually " +
                        $"(mean channel difference {visualDifference:0.###}).");
                }
                visualDifferences.Add(visualDifference);
            }
            var maximumVisualDifference = visualDifferences.Count == 0
                ? 0d
                : visualDifferences.Max();
            var inputBytes = new FileInfo(inputPath).Length;
            var outputBytes = new FileInfo(outputPath).Length;
            if (outputBytes >= baselineBytes)
                throw new InvalidDataException(
                    $"Image optimization did not reduce the normal export size ({baselineBytes} -> {outputBytes}).");
            return (
                analysis,
                export,
                maximumVisualDifference,
                inputBytes,
                outputBytes,
                baselineBytes,
                eligiblePageNumbers);
        }).GetAwaiter().GetResult();

        _diagnostics?.Write(
            "image-optimize-test.pass",
            $"Pages: {string.Join(",", result.eligiblePageNumbers)}; images: {result.export.OptimizedImages}; " +
            $"area reduction: {result.analysis.EstimatedAreaReduction:P1}; " +
            $"JPEG quality: {result.analysis.EstimatedJpegQuality}; " +
            $"image bytes: {result.analysis.OriginalEncodedBytes} -> {result.analysis.EstimatedEncodedBytes}; " +
            $"maximum visual difference: {result.maximumVisualDifference:0.###}; " +
            $"bytes: input {result.inputBytes}; normal export {result.baselineBytes}; optimized {result.outputBytes}; output: {outputPath}");
        Shutdown(0);
    }

    private static double CalculateBitmapDifference(BitmapSource before, BitmapSource after)
    {
        if (before.PixelWidth != after.PixelWidth || before.PixelHeight != after.PixelHeight)
            return double.PositiveInfinity;
        var stride = before.PixelWidth * 4;
        var beforePixels = new byte[stride * before.PixelHeight];
        var afterPixels = new byte[stride * after.PixelHeight];
        new FormatConvertedBitmap(before, PixelFormats.Bgra32, null, 0).CopyPixels(beforePixels, stride, 0);
        new FormatConvertedBitmap(after, PixelFormats.Bgra32, null, 0).CopyPixels(afterPixels, stride, 0);
        long difference = 0;
        for (var index = 0; index < beforePixels.Length; index++)
            difference += Math.Abs(beforePixels[index] - afterPixels[index]);
        return difference / (double)beforePixels.Length;
    }

    private void RunProjectAnalysisTest(string[] arguments, int optionIndex)
    {
        if (arguments.Length <= optionIndex + 1)
            throw new ArgumentException("--project-analysis-test requires a project path.");
        var projectPath = Path.GetFullPath(arguments[optionIndex + 1]);
        var paths = ApplicationPathResolver.Resolve(AppContext.BaseDirectory);
        ApplicationPathResolver.EnsureDirectories(paths);
        var viewModel = new MainWindowViewModel(
            new ProjectPackageService(),
            new PdfPreviewService(),
            new PdfExportService(),
            new NdlOcrCompanionService(),
            new DiagnosticLog(paths.LogDirectory),
            paths,
            () => { });
        var summary = Task.Run(async () =>
        {
            await viewModel.LoadProjectForDiagnosticsAsync(projectPath);
            var verticalCount = viewModel.OverlayItems.Count(region => region.IsVertical);
            if (verticalCount == 0)
                throw new InvalidDataException($"NDLOCR vertical-writing metadata was not restored into the saved project. Source={viewModel.OcrDataSourceText}; regions={viewModel.OverlayItems.Count}.");
            var boldLine = viewModel.OverlayItems.FirstOrDefault(region => region.OriginalText == "の手術")
                ?? throw new InvalidDataException("The bold-glyph regression region was not found on page 1.");
            var boldPreviousWidth = boldLine.Width;
            var boldEstimate = CharacterAdvanceEstimator.Estimate(
                viewModel.PreviewImage as BitmapSource ?? throw new InvalidDataException("The page-1 preview image was not available."),
                boldLine);
            if (Math.Abs(boldEstimate.Extent - boldPreviousWidth) > 0.01)
                throw new InvalidDataException($"The fitted bold-glyph extent changed unexpectedly: {boldPreviousWidth:0.0} -> {boldEstimate.Extent:0.0}.");
            viewModel.EditUnitIndex = (int)OcrEditUnit.Character;
            viewModel.SetOverlaySelection([boldLine], boldLine);
            if (!viewModel.EstimateCharacterAdvancesCommand.CanExecute(null))
                throw new InvalidOperationException("Image-assisted character estimation was not available for the bold-glyph regression region.");
            viewModel.EstimateCharacterAdvancesCommand.Execute(null);
            var boldAdvances = boldLine.CharacterCells.Select(cell => boldLine.IsVertical ? cell.Height : cell.Width).ToArray();
            var boldExtent = boldAdvances.Sum();
            if (boldAdvances.Length != 3 || boldExtent <= 0 ||
                boldAdvances.Any(advance => advance < boldExtent * 0.20 || advance > boldExtent * 0.45) ||
                boldAdvances[0] >= boldAdvances[1] || boldAdvances[0] >= boldAdvances[2] ||
                Math.Abs(boldLine.Width - boldPreviousWidth) > 0.01)
                throw new InvalidDataException($"The bold-glyph boundaries were implausible: {string.Join(", ", boldAdvances.Select(value => value.ToString("0.0")))}.");
            viewModel.UndoCommand.Execute(null);
            await viewModel.RenderPageForDiagnosticsAsync(3);
            var thinCellRegions = new[]
            {
                viewModel.OverlayItems.FirstOrDefault(region => region.OriginalText.StartsWith("ブン投げてしまえ", StringComparison.Ordinal)),
                viewModel.OverlayItems.FirstOrDefault(region => region.OriginalText.StartsWith("そのロジックを組む", StringComparison.Ordinal)),
            };
            if (thinCellRegions.Any(region => region is null))
                throw new InvalidDataException("The page-3 thin-cell regression regions were not found.");
            var page3MinimumRatios = new List<double>();
            foreach (var region in thinCellRegions.Cast<OverlayRegionViewModel>())
            {
                var estimate = CharacterAdvanceEstimator.Estimate(
                    viewModel.PreviewImage as BitmapSource ?? throw new InvalidDataException("The page-3 preview image was not available."),
                    region);
                var crossExtent = region.IsVertical ? region.Width : region.Height;
                var minimumRatio = estimate.Advances.Min() / crossExtent;
                page3MinimumRatios.Add(minimumRatio);
                if (minimumRatio < 0.18)
                    throw new InvalidDataException($"Image estimation produced an implausibly thin character cell for '{region.OriginalText}': {minimumRatio:P1} of line height.");
            }
            await viewModel.RenderPageForDiagnosticsAsync(6);
            var page6Regions = new[]
            {
                viewModel.OverlayItems.FirstOrDefault(region => region.OriginalText.StartsWith("「生成AIの登場によって", StringComparison.Ordinal)),
                viewModel.OverlayItems.FirstOrDefault(region => region.OriginalText.StartsWith("る時代は終わる", StringComparison.Ordinal)),
            };
            if (page6Regions.Any(region => region is null))
                throw new InvalidDataException("The page-6 empty-cell regression regions were not found.");
            var page6EmptyCells = 0;
            var page6MaximumRatios = new List<double>();
            foreach (var region in page6Regions.Cast<OverlayRegionViewModel>())
            {
                var estimate = CharacterAdvanceEstimator.Estimate(
                    viewModel.PreviewImage as BitmapSource ?? throw new InvalidDataException("The page-6 preview image was not available."),
                    region);
                var crossExtent = region.IsVertical ? region.Width : region.Height;
                page6EmptyCells += region.CharacterCells
                    .Select((cell, index) => (cell, index))
                    .Count(item => !item.cell.Text.All(char.IsWhiteSpace) && estimate.InkCoverages[item.index] < 0.066);
                page6MaximumRatios.Add(estimate.Advances.Max() / crossExtent);
            }
            if (page6EmptyCells > 0 || page6MaximumRatios.Any(ratio => ratio > 1.75))
                throw new InvalidDataException($"Page-6 estimation assigned non-whitespace text to empty or excessively wide cells: empty={page6EmptyCells}, maximum={string.Join(", ", page6MaximumRatios.Select(value => value.ToString("P0")))}.");
            await viewModel.RenderPageForDiagnosticsAsync(7);
            var punctuationLine = viewModel.OverlayItems.FirstOrDefault(region =>
                                      region.OriginalText.StartsWith("かれない」という実感は、", StringComparison.Ordinal))
                                  ?? throw new InvalidDataException("The page-7 full-width punctuation regression region was not found.");
            var punctuationLineEstimate = CharacterAdvanceEstimator.Estimate(
                viewModel.PreviewImage as BitmapSource ?? throw new InvalidDataException("The page-7 preview image was not available."),
                punctuationLine);
            var punctuationIndex = punctuationLine.Text.IndexOf('、');
            if (punctuationIndex < 0 || punctuationIndex + 1 >= punctuationLineEstimate.Advances.Count)
                throw new InvalidDataException("The page-7 punctuation index could not be mapped to character advances.");
            var punctuationAverage = punctuationLineEstimate.Extent / punctuationLineEstimate.Advances.Count;
            var punctuationRatio = punctuationLineEstimate.Advances[punctuationIndex] / punctuationAverage;
            var followingRatio = punctuationLineEstimate.Advances[punctuationIndex + 1] / punctuationAverage;
            if (punctuationRatio is < 0.70 or > 1.30 || followingRatio > 1.45)
                throw new InvalidDataException(
                    $"The full-width comma lost its right-side advance: comma={punctuationRatio:P0}, following={followingRatio:P0}.");
            var disconnectedKanaLine = viewModel.OverlayItems.FirstOrDefault(region =>
                                           region.Text.Contains('に') &&
                                           region.Text.Count(character => character >= '\u3000') >= region.Text.Length * 0.70)
                                       ?? throw new InvalidDataException("A page-7 Japanese line containing 「に」 was not found.");
            var disconnectedKanaEstimate = CharacterAdvanceEstimator.Estimate(
                viewModel.PreviewImage as BitmapSource ?? throw new InvalidDataException("The page-7 preview image was not available."),
                disconnectedKanaLine);
            var disconnectedKanaIndex = disconnectedKanaLine.Text.IndexOf('に');
            var disconnectedKanaCrossExtent = disconnectedKanaLine.IsVertical ? disconnectedKanaLine.Width : disconnectedKanaLine.Height;
            var disconnectedKanaRatio = disconnectedKanaEstimate.Advances[disconnectedKanaIndex] / disconnectedKanaCrossExtent;
            if (disconnectedKanaRatio is < 0.55 or > 1.55)
                throw new InvalidDataException($"The kana 「に」 was assigned an implausible advance: {disconnectedKanaRatio:P0} of line height.");
            await viewModel.RenderPageForDiagnosticsAsync(9);
            var shortLine = viewModel.OverlayItems.FirstOrDefault(region => region.OriginalText == "も構いません。")
                ?? throw new InvalidDataException("The short-line regression region was not found on page 9.");
            var previousWidth = shortLine.Width;
            viewModel.EditUnitIndex = (int)OcrEditUnit.Character;
            viewModel.SetOverlaySelection([shortLine], shortLine);
            if (!viewModel.EstimateCharacterAdvancesCommand.CanExecute(null))
                throw new InvalidOperationException("Image-assisted character estimation was not available.");
            viewModel.EstimateCharacterAdvancesCommand.Execute(null);
            // Older projects may still contain a wide empty tail, while a
            // project saved by a newer build may already have the corrected
            // extent. Require a reduction only for the former.
            if (previousWidth > 250 && shortLine.Width >= previousWidth * 0.65)
                throw new InvalidDataException($"The empty trailing extent was not removed ({previousWidth:0.0} -> {shortLine.Width:0.0}).");
            if (previousWidth <= 250 && shortLine.Width > previousWidth * 1.15)
                throw new InvalidDataException($"An already-corrected short line was unexpectedly enlarged ({previousWidth:0.0} -> {shortLine.Width:0.0}).");
            var estimatedWidth = shortLine.Width;
            viewModel.UndoCommand.Execute(null);
            if (Math.Abs(shortLine.Width - previousWidth) > 0.01)
                throw new InvalidDataException("Undo did not restore the pre-estimation line extent.");
            await viewModel.RenderPageForDiagnosticsAsync(10);
            var bracketRatios = new List<double>();
            foreach (var region in viewModel.OverlayItems.Where(region =>
                         region.Text.Contains('\u300c') || region.Text.Contains('\u300d')))
            {
                var estimate = CharacterAdvanceEstimator.Estimate(
                    viewModel.PreviewImage as BitmapSource ??
                    throw new InvalidDataException("The page-10 preview image was not available."),
                    region);
                var elementIndexes = StringInfo.ParseCombiningCharacters(region.Text);
                var crossExtent = region.IsVertical ? region.Width : region.Height;
                for (var index = 0; index < elementIndexes.Length; index++)
                {
                    var elementEnd = index + 1 < elementIndexes.Length ? elementIndexes[index + 1] : region.Text.Length;
                    var element = region.Text[elementIndexes[index]..elementEnd];
                    if (element is not "\u300c" and not "\u300d") continue;
                    bracketRatios.Add(estimate.Advances[index] / crossExtent);
                }
            }
            if (bracketRatios.Count == 0)
                throw new InvalidDataException("No Japanese corner brackets were found on page 10.");
            if (bracketRatios.Any(ratio => ratio is < 0.45 or > 1.40))
                throw new InvalidDataException(
                    $"Page-10 Japanese brackets were assigned implausible advances: {string.Join(", ", bracketRatios.Select(value => value.ToString("P0")))}.");
            _diagnostics?.Write(
                "project-analysis.brackets",
                $"Page 10 corner-bracket advance/line-height ratios: {string.Join(", ", bracketRatios.Select(value => value.ToString("P0")))}");
            return (verticalCount, boldPreviousWidth, boldAdvances, page3MinimumRatios, page6EmptyCells, page6MaximumRatios,
                punctuationRatio, followingRatio, disconnectedKanaRatio, previousWidth, estimatedWidth);
        }).GetAwaiter().GetResult();
        _diagnostics?.Write(
            "project-analysis-test.pass",
            $"Page 1 vertical regions: {summary.verticalCount}; bold line extent {summary.boldPreviousWidth:0.0}, advances: {string.Join(", ", summary.boldAdvances.Select(value => value.ToString("0.0")))}; page 3 minimum advance/height: {string.Join(", ", summary.page3MinimumRatios.Select(value => value.ToString("P0")))}; page 6 empty cells: {summary.page6EmptyCells}, maximum advance/height: {string.Join(", ", summary.page6MaximumRatios.Select(value => value.ToString("P0")))}; page 7 comma/next: {summary.punctuationRatio:P0}/{summary.followingRatio:P0}, に/height: {summary.disconnectedKanaRatio:P0}; page 9 short line: {summary.previousWidth:0.0} -> {summary.estimatedWidth:0.0}; Undo passed");
        Shutdown(0);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        // Windows がスリープ復帰や機器の省電力復帰後にタブレット情報を更新する際、
        // WPF の旧式入力経路が PenIMC.dll を読み込めないことがあります。PDF編集状態とは
        // 無関係で、マウス／キーボード操作は継続できるため、この既知の入力例外だけは
        // 診断ログへ残して処理を続行します。その他の DLL 読み込み失敗は従来どおり致命的です。
        if (IsRecoverablePenImcException(e.Exception))
        {
            _diagnostics?.WriteException("input.penimc-unavailable", e.Exception);
            return;
        }

        ReportFatal("An unexpected UI error occurred.", e.Exception);
    }

    /// <summary>
    /// 例外が WPF の旧式ペン／タッチ入力（PenIMC/WISP）の再初期化だけで発生した
    /// 回復可能な DLL 読み込み失敗かを判定します。
    /// </summary>
    /// <param name="exception">Dispatcher が捕捉した例外。</param>
    /// <returns>PDF処理やアプリ固有DLLとは無関係な PenIMC の例外であれば <see langword="true"/>。</returns>
    private static bool IsRecoverablePenImcException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not DllNotFoundException)
                continue;

            var stackTrace = current.StackTrace;
            if (stackTrace?.Contains("MS.Win32.Penimc", StringComparison.Ordinal) == true ||
                stackTrace?.Contains("System.Windows.Input.PenThreadWorker", StringComparison.Ordinal) == true ||
                stackTrace?.Contains("System.Windows.Input.StylusWisp", StringComparison.Ordinal) == true)
                return true;
        }

        return false;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _diagnostics?.WriteException("domain.unhandled", exception);
        else
            _diagnostics?.Write("domain.unhandled", e.ExceptionObject?.ToString() ?? "Unknown error");
    }

    private void ReportFatal(string message, Exception exception)
    {
        var logPath = _diagnostics?.WriteException("fatal", exception) ?? "The startup log could not be created.";
        if (_isNonInteractiveTest)
        {
            Shutdown(-1);
            return;
        }
        try
        {
            MessageBox.Show(
                $"{message}\n\n{exception.Message}\n\nLog: {logPath}",
                "PDF Correctorium",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Shutdown(-1);
        }
    }
}
