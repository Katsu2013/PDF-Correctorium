using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private sealed record StartupFileTestResult(
        bool Success, bool HasDocument, bool HasPreview, int Pages, int? SelectedPage,
        string SourcePdfPath, string ProjectPath, int Bookmarks, string? MetadataTitle,
        bool IsOpening, bool Busy, bool CanOpen, string[] Errors);

    /// <summary>通常と同じ起動引数処理を使用し、モーダル表示だけを診断結果への記録に置き換えます。</summary>
    private async Task RunStartupFileTestAsync(MainWindow window, string[] arguments, int optionIndex)
    {
        try
        {
            if (optionIndex != arguments.Length - 2)
                throw new ArgumentException("Use [file] --startup-file-test <new-report-path>.");
            var reportPath = Path.GetFullPath(arguments[optionIndex + 1]);
            if (File.Exists(reportPath)) throw new IOException("The report must not already exist.");
            var viewModel = (MainWindowViewModel)window.DataContext;
            var errors = new List<string>();
            viewModel.ErrorDialogOverride = (message, error) => errors.Add(message + " " + error.Message);
            var success = await OpenStartupFileAsync(window, arguments[..optionIndex]);
            var report = new StartupFileTestResult(success, viewModel.HasDocument, viewModel.HasPreview,
                viewModel.PageItems.Count, viewModel.SelectedPage?.PageNumber, viewModel.SourcePdfPath,
                viewModel.ProjectPath, viewModel.BookmarkItems.Count, viewModel.CurrentDocumentMetadata?.Title,
                viewModel.IsOpeningDocument, viewModel.IsBackgroundOperationVisible,
                viewModel.OpenPdfCommand.CanExecute(null) && viewModel.OpenProjectCommand.CanExecute(null), errors.ToArray());
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(0);
        }
        catch (Exception exception)
        {
            _diagnostics?.WriteException("startup-file-test.failed", exception);
            Shutdown(1);
        }
    }

    /// <summary>新規プロセスへ実際にファイル名を渡し、PDFとプロジェクトの初期ページ表示まで検証します。</summary>
    private async void RunFileLaunchTests(string[] arguments, int optionIndex)
    {
        try
        {
            if (arguments.Length != optionIndex + 2)
                throw new ArgumentException("--file-launch-tests requires a new output directory.");
            var directory = Path.GetFullPath(arguments[optionIndex + 1]);
            if (Directory.Exists(directory)) throw new IOException("Use a new output directory.");
            Directory.CreateDirectory(directory);
            var inputDirectory = Path.Combine(directory, "入力 ファイル");
            Directory.CreateDirectory(inputDirectory);
            var checks = new List<string>();
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add("PASS: " + message);
            }
            var pdfPath = Path.Combine(inputDirectory, "日本語 空白つき.PDF");
            WriteDocumentUiTestPdf(pdfPath);
            var package = new ProjectPackageService();
            var source = await package.CreateSourceReferenceAsync(pdfPath, inputDirectory);
            var project = new PdfCorrectoriumProject
            {
                Name = "起動テスト",
                SourcePdf = source,
                BookmarksInitialized = true,
                Bookmarks = [new PdfBookmark { Title = "保存済みしおり", PageNumber = 2 }],
                DocumentMetadata = new PdfDocumentMetadata { Title = "保存済みタイトル" },
            };
            var projectPath = Path.Combine(inputDirectory, "編集 プロジェクト.PDFOCRPROJ");
            await package.SaveAsync(projectPath, project);
            var embeddedSource = Path.Combine(inputDirectory, "内包用.pdf");
            WriteDocumentUiTestPdf(embeddedSource);
            var embeddedProjectPath = Path.Combine(inputDirectory, "内包 プロジェクト.pdfocrproj");
            await package.SaveAsync(embeddedProjectPath, project with { SourcePdf = await package.CreateSourceReferenceAsync(embeddedSource) }, embedSourcePdf: true);
            // Only a fixture created inside this fresh test directory is moved; no user document is touched.
            File.Move(embeddedSource, Path.Combine(inputDirectory, "内包用.original-not-available"));
            var missingSourceProject = Path.Combine(inputDirectory, "参照先なし.pdfocrproj");
            await package.SaveAsync(missingSourceProject, project with
            {
                SourcePdf = source with { FileName = "missing.pdf", RelativePath = "missing.pdf", AbsolutePathHint = Path.Combine(inputDirectory, "missing.pdf") },
            });
            var mismatchedProject = Path.Combine(inputDirectory, "指紋不一致.pdfocrproj");
            await package.SaveAsync(mismatchedProject, project with { SourcePdf = source with { Sha256 = new string('0', 64) } });
            var corruptPdf = Path.Combine(inputDirectory, "破損.pdf");
            var corruptProject = Path.Combine(inputDirectory, "破損.pdfocrproj");
            var unsupported = Path.Combine(inputDirectory, "対象外.txt");
            foreach (var file in new[] { corruptPdf, corruptProject, unsupported }) await File.WriteAllTextAsync(file, "Not a PDF or a project.");
            var originalHashes = new Dictionary<string, string>();
            foreach (var file in new[] { pdfPath, projectPath, embeddedProjectPath })
                originalHashes[file] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file)));

            var cases = new (string Name, string[] Files, bool Success, bool Document, bool Project)[]
            {
                ("no-file", [], true, false, false),
                ("pdf-absolute-unicode-spaces", [pdfPath], true, true, false),
                ("pdf-relative-upper-case", [Path.GetFileName(pdfPath)], true, true, false),
                ("project-absolute", [projectPath], true, true, true),
                ("project-relative-upper-case", [Path.GetFileName(projectPath)], true, true, true),
                ("project-embedded-without-original", [embeddedProjectPath], true, true, true),
                ("pdf-missing", [Path.Combine(inputDirectory, "missing.pdf")], false, false, false),
                ("pdf-corrupt", [corruptPdf], false, false, false),
                ("project-corrupt", [corruptProject], false, false, false),
                ("project-source-missing", [missingSourceProject], false, false, false),
                ("project-source-mismatch", [mismatchedProject], false, false, false),
                ("unsupported-type", [unsupported], false, false, false),
                ("multiple-files", [pdfPath, projectPath], false, false, false),
            };
            foreach (var item in cases)
            {
                var reportPath = Path.Combine(directory, item.Name + ".json");
                var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "PdfCorrectorium.exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = inputDirectory,
                };
                foreach (var file in item.Files) start.ArgumentList.Add(file);
                start.ArgumentList.Add("--startup-file-test");
                start.ArgumentList.Add(reportPath);
                using var child = Process.Start(start) ?? throw new IOException("Could not launch the test application.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                try { await child.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    child.Kill(entireProcessTree: true);
                    throw new TimeoutException($"File launch case {item.Name} did not finish.");
                }
                Check(child.ExitCode == 0 && File.Exists(reportPath), item.Name + ": child process completed and reported its actual document state.");
                var result = JsonSerializer.Deserialize<StartupFileTestResult>(await File.ReadAllTextAsync(reportPath))!;
                Check(result.Success == item.Success && result.HasDocument == item.Document && result.HasPreview == item.Document,
                    item.Name + ": the startup path reported the expected load result, including preview availability.");
                Check(!result.IsOpening && !result.Busy && result.CanOpen, item.Name + ": loading ended and Open commands remain usable.");
                Check(item.Success ? result.Errors.Length == 0 : result.Errors.Length > 0,
                    item.Name + ": errors are reported instead of silently leaving an empty window.");
                if (item.Document)
                {
                    Check(result.Pages == 2 && result.SelectedPage == 1 && File.Exists(result.SourcePdfPath), item.Name + ": two pages loaded and the first page selected.");
                    if (item.Project)
                        Check(result.Bookmarks == 1 && result.MetadataTitle == "保存済みタイトル" &&
                              result.ProjectPath.Equals(Path.GetFullPath(item.Files[0], inputDirectory), StringComparison.OrdinalIgnoreCase),
                            item.Name + ": project save path, saved bookmarks, and metadata were restored.");
                    else Check(result.SourcePdfPath.Equals(pdfPath, StringComparison.OrdinalIgnoreCase), item.Name + ": the requested PDF was opened.");
                }
            }
            foreach (var (file, hash) in originalHashes)
                Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))) == hash, Path.GetFileName(file) + ": launching did not modify the input file.");
            foreach (var name in new[] { "PdfDocument.ico", "PdfCorrectoriumProject.ico" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Icons", name);
                var decoder = new IconBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var sizes = decoder.Frames.Select(frame => frame.PixelWidth).ToHashSet();
                Check(new[] { 16, 24, 32, 48, 64, 128, 256 }.All(sizes.Contains), name + ": the packaged association icon contains every supported resolution.");
            }
            await File.WriteAllLinesAsync(Path.Combine(directory, "checks.txt"), checks);
            _diagnostics?.Write("file-launch-tests.passed", $"{checks.Count} checks passed. {directory}");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            _diagnostics?.WriteException("file-launch-tests.failed", exception);
            Shutdown(1);
        }
    }
}
