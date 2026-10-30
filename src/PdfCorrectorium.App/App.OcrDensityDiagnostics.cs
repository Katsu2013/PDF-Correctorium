using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyOcrDensityUiAsync(MainWindow owner, MainWindowViewModel vm, string source, string directory, Action<bool, string> check)
    {
        var inputs = new (string Text, double Width, double Height)[]
        {
            ("日本語文章", 100, 20), ("ABCDEFGH", 96, 20), ("日本ABC123", 112, 20),
            ("ＡＢ１２かな", 120, 20), ("ｱｲｳｴｵｶｷｸｹｺ", 100, 20),
            ("通でした。", 100.4, 21.5), ("んと理解しておきましょう。", 277.8, 19.6),
            ("日本", 100, 20), (new string('B', 18), 100, 20),
        };
        var pageId = Guid.NewGuid();
        var regions = inputs.Select((value, i) =>
        {
            var geometry = new TextGeometry { LocalBounds = new(new(10, 355 - i * 32), new(value.Width, value.Height)),
                RotationCenter = new(10 + value.Width / 2, 355 - i * 32 + value.Height / 2) };
            return new OcrTextRegion { PageId = pageId, OriginalText = value.Text,
                OriginalGeometry = geometry, EditedGeometry = geometry, HasExplicitWritingMode = true };
        }).ToArray();
        var packages = new ProjectPackageService();
        var projectPath = Path.Combine(directory, "mixed-density.pdfocrproj");
        await packages.SaveAsync(projectPath, new PdfCorrectoriumProject
        {
            Name = "文字種と枠の比率", SourcePdf = await packages.CreateSourceReferenceAsync(source, directory),
            Pages = [new() { Id = pageId, PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = regions },
                new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400 }],
        });
        await vm.LoadProjectForDiagnosticsAsync(projectPath);
        var results = await vm.AnalyzeOcrCharacterCountAnomaliesAsync(new());
        check(results.Count == 2 && results.All(result => result.RegionId == regions[7].Id || result.RegionId == regions[8].Id),
            "Real project/preview coordinates retain only the sparse CJK and crowded Latin lines, not the healthy screenshot proportions.");
        check(results.All(result => result.PeerCount == 7), "Only independent plausible-density rows establish the comparison baseline.");
        var analysis = new OcrQualityAnalysisWindow(vm) { Owner = owner, ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        analysis.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
        try
        {
            analysis.Show();
            var method = typeof(OcrQualityAnalysisWindow).GetMethod("AnalyzeCharacterCountsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)method.Invoke(analysis, null)!;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); analysis.UpdateLayout();
            var grid = (DataGrid)analysis.FindName("CountResultsGrid");
            check(grid.Items.Count == 2 && !vm.HasUnsavedChanges, "The density-based result UI is read-only and keeps the two genuine test anomalies.");
            check(grid.Columns.Count(column => column.Visibility == Visibility.Visible) == 5 &&
                  grid.Columns.Where(ListDetails.GetIsDetail).All(column => column.Visibility == Visibility.Collapsed),
                "The anomaly list initially shows only page, finding, text, count and estimate, not technical statistics.");
            check(grid.Columns.Any(column => column.SortMemberPath == "DensityRatio") && grid.Columns.Any(column => Equals(column.Header, "目安文字数")),
                "The UI distinguishes a row-specific estimate from the old raw-count median and exposes a sortable density ratio.");
            var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            var firstBeforeDetails = grid.Items[0]; grid.SelectedItem = firstBeforeDetails;
            var columnMenu = OpenListColumnMenu(grid);
            foreach (var item in columnMenu.Items.OfType<MenuItem>().Where(item => item.Tag is DataGridColumn && !item.IsChecked))
                await ClickListColumnMenuItemAsync(item);
            columnMenu.IsOpen = false;
            check(grid.Columns.All(column => column.Visibility == Visibility.Visible) && ReferenceEquals(grid.SelectedItem, firstBeforeDetails),
                $"Detail columns retain the selected candidate without a new analysis (visible={grid.Columns.Count(column => column.Visibility == Visibility.Visible)}/{grid.Columns.Count}, selection={ReferenceEquals(grid.SelectedItem, firstBeforeDetails)}, checks={string.Join(',', columnMenu.Items.OfType<MenuItem>().Where(item => item.Tag is DataGridColumn).Select(item => item.IsChecked))}).");
            view.SortDescriptions.Add(new("DensityRatio", ListSortDirection.Ascending));
            var first = grid.Items[0];
            ((CheckBox)analysis.FindName("ShowLockedCountCheckBox")).IsChecked = true;
            check(ReferenceEquals(grid.Items[0], first) && view.SortDescriptions.Count == 1, "Density sorting survives the existing lock filter without recalculation.");
            columnMenu = OpenListColumnMenu(grid, keyboard: true);
            await ClickListColumnMenuItemAsync(columnMenu.Items.OfType<MenuItem>().Last());
            columnMenu.IsOpen = false;
            check(grid.Columns.Count(column => column.Visibility == Visibility.Visible) == 5 && view.SortDescriptions.Count == 1 &&
                  ReferenceEquals(grid.Items[0], first) && !vm.HasUnsavedChanges,
                "Collapsing details preserves density ordering, row identity and clean state.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); analysis.UpdateLayout();
            SaveIconDiagnosticImage((FrameworkElement)analysis.Content, Path.Combine(directory, "density-candidates.png"));
            LocalizationService.SetLanguage(LocalizationService.EnglishLanguage); LocalizationService.Apply(analysis);
            check(grid.Columns.Any(column => Equals(column.Header, "Estimated characters")) &&
                  grid.Columns.Any(column => Equals(column.Header, "Density ratio")), "Density estimates and ratios have English headers.");
        }
        finally { analysis.Close(); LocalizationService.SetLanguage(LocalizationService.JapaneseLanguage); }
    }

    // Optional read-only regression on a user-supplied project. No source PDF,
    // rendering, OCR inference, editing, or textual export is needed here.
    private static async Task VerifyOcrDensityBookAsync(string path, string directory, Action<bool, string> check)
    {
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var book = await new ProjectPackageService().OpenAsync(path);
        var samples = book.Pages.SelectMany(page => page.TextRegions.Where(region => !region.IsDeleted &&
            !string.IsNullOrWhiteSpace(region.EffectiveText)).Select(region =>
        {
            var geometry = region.EditedGeometry;
            return new OcrQualitySample(page.PageNumber, region.Id, region.EffectiveText,
                geometry.LocalBounds.Size.Width, geometry.LocalBounds.Size.Height, region.WritingMode == WritingMode.Vertical,
                geometry.IsGeometryLocked, geometry.CharacterLocks.Any(value => value), geometry.CharacterAdvances);
        })).ToArray();
        var timer = Stopwatch.StartNew();
        var results = await Task.Run(() => new OcrQualityAnalyzer().FindCharacterCountAnomalies(samples, new()));
        timer.Stop();
        check(SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(before), "The provided project remains byte-for-byte unchanged after read-only density analysis.");
        check(results.All(result => double.IsFinite(result.CountRatio) && double.IsFinite(result.ExpectedCharacterCount)),
            "All findings in the provided project have finite, normalized density and estimates.");
        var knownScreenshotRows = samples.Where(row => row.PageNumber == 385 && row.Text.Trim() == "通でした。").ToArray();
        check(knownScreenshotRows.Length > 0 && knownScreenshotRows.All(row => results.All(result => result.RegionId != row.RegionId)),
            "The actual five-character Japanese line at page 385 is no longer misclassified as an English-sized eight-character line.");
        await File.WriteAllTextAsync(Path.Combine(directory, "book-density-summary.json"), JsonSerializer.Serialize(new
        {
            Pages = book.Pages.Count, Regions = samples.Length, Candidates = results.Count,
            VisibleCandidates = results.Count(result => !result.Sample.IsGeometryLocked),
            TooFew = results.Count(result => result.Kind == OcrCharacterCountAnomalyKind.TooFew),
            TooMany = results.Count(result => result.Kind == OcrCharacterCountAnomalyKind.TooMany),
            CalculationMilliseconds = timer.Elapsed.TotalMilliseconds,
            ScreenshotRowsVerified = knownScreenshotRows.Length,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
