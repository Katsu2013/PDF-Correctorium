using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// PDFium のネイティブ障害から編集画面を保護するため、PDF出力を別プロセスで実行します。
/// </summary>
internal sealed class IsolatedPdfExportService(
    ProjectPackageService packages,
    ApplicationPaths paths)
{
    private const string WorkerOption = "--isolated-pdf-export";
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(30);
    private const int MaximumWorkerOutputCharacters = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 現在の編集モデルを一時プロジェクトへ保存し、専用ワーカープロセスでPDFを生成します。
    /// </summary>
    internal async Task<IsolatedPdfExportResult> ExportAsync(
        string sourcePdfPath,
        string destinationPdfPath,
        PdfCorrectoriumProject project,
        CancellationToken cancellationToken = default) =>
        await ExportAsync(
            sourcePdfPath,
            destinationPdfPath,
            project,
            progress: null,
            cancellationToken);

    /// <summary>
    /// 現在の編集モデルを専用ワーカープロセスでPDF化し、ページ単位の進捗を通知します。
    /// </summary>
    internal async Task<IsolatedPdfExportResult> ExportAsync(
        string sourcePdfPath,
        string destinationPdfPath,
        PdfCorrectoriumProject project,
        IProgress<PdfExportProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPdfPath);
        ArgumentNullException.ThrowIfNull(project);

        Directory.CreateDirectory(paths.CacheDirectory);
        var operationId = Guid.NewGuid().ToString("N");
        var temporaryProjectPath = Path.Combine(paths.CacheDirectory, $"export-{operationId}.pdfocrproj");
        var statePath = Path.Combine(paths.CacheDirectory, $"export-{operationId}.state.json");
        var destinationFullPath = Path.GetFullPath(destinationPdfPath);
        var destinationDirectory = Path.GetDirectoryName(destinationFullPath)
            ?? throw new InvalidOperationException("PDFの出力先フォルダーを特定できません。");
        // The completed worker output also uses a short cache path. The parent process
        // commits it to the user-selected path only after generation and validation.
        var exportDirectory = Path.Combine(paths.CacheDirectory, "pdf-export", operationId);
        Directory.CreateDirectory(exportDirectory);
        var completedOutputPath = Path.Combine(exportDirectory, "completed.pdf");
        var standardError = string.Empty;
        var preserveCompletedOutput = false;
        long? peakWorkerMemoryBytes = null;
        long? workerMemoryLimitBytes = null;

        try
        {
            // The worker receives the actual source PDF as a separate, explicit argument. Its
            // transport package therefore needs a non-embedded reference that is valid relative
            // to the temporary package, even when the user's project is portable and has no
            // RelativePath. Re-hashing the prepared source also binds the worker payload to the
            // exact physical PDF that will be exported without copying that potentially large PDF.
            var transportSource = await packages.CreateSourceReferenceAsync(sourcePdfPath, cancellationToken: cancellationToken);
            transportSource = transportSource with
            {
                FileName = project.SourcePdf.FileName,
                RelativePath = Path.GetFileName(sourcePdfPath),
                AbsolutePathHint = null,
                PageCount = project.SourcePdf.PageCount,
                IsEmbedded = false,
            };
            var transportProject = project with
            {
                PdfStorageMode = ProjectPdfStorageMode.Relative,
                SourcePdf = transportSource,
            };
            await packages.SaveAsync(temporaryProjectPath, transportProject, embedSourcePdf: false, cancellationToken);
            var executablePath = ResolveExecutablePath();
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            startInfo.ArgumentList.Add(WorkerOption);
            startInfo.ArgumentList.Add(Path.GetFullPath(sourcePdfPath));
            startInfo.ArgumentList.Add(temporaryProjectPath);
            startInfo.ArgumentList.Add(completedOutputPath);
            startInfo.ArgumentList.Add(statePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("PDF出力プロセスを開始できませんでした。");
            try
            {
                using var job = WindowsProcessJob.Attach(process);
                workerMemoryLimitBytes = job.ProcessMemoryLimitBytes;
                using var timeoutSource = new CancellationTokenSource(ExportTimeout);
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
                var errorTask = ExternalProcessRunner.ReadToEndWithLimitAsync(
                    process.StandardError, MaximumWorkerOutputCharacters, linkedSource.Token);
                var outputTask = ExternalProcessRunner.ReadToEndWithLimitAsync(
                    process.StandardOutput, MaximumWorkerOutputCharacters, linkedSource.Token);
                var exitTask = process.WaitForExitAsync(linkedSource.Token);
                string? lastProgressKey = null;
                while (!exitTask.IsCompleted)
                {
                    if (errorTask.IsFaulted) await errorTask;
                    if (outputTask.IsFaulted) await outputTask;
                    var progressState = await TryReadStateAsync(statePath, linkedSource.Token);
                    if (progressState is not null)
                    {
                        var progressKey = $"{progressState.Phase}:{progressState.Current}:{progressState.Total}:{progressState.Message}";
                        if (!string.Equals(progressKey, lastProgressKey, StringComparison.Ordinal))
                        {
                            progress?.Report(new PdfExportProgress(
                                progressState.Phase,
                                progressState.Current,
                                progressState.Total,
                                progressState.Message ?? DescribePhase(progressState.Phase)));
                            lastProgressKey = progressKey;
                        }
                    }

                    await Task.WhenAny(exitTask, Task.Delay(250, linkedSource.Token));
                }

                await exitTask;
                peakWorkerMemoryBytes = job.TryGetPeakProcessMemoryBytes();
                standardError = await errorTask;
                _ = await outputTask;
            }
            catch (OperationCanceledException)
            {
                ExternalProcessRunner.TryTerminate(process);
                try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TimeoutException($"PDF出力が制限時間 {ExportTimeout.TotalMinutes:N0} 分以内に完了しませんでした。");
            }
            catch
            {
                ExternalProcessRunner.TryTerminate(process);
                try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                throw;
            }

            var state = await ReadStateAsync(statePath, cancellationToken);
            if (process.ExitCode == 0 && state?.Result is not null)
            {
                try
                {
                    var commit = await Task.Run(
                        () => PdfOutputFileCommitter.Commit(
                            completedOutputPath,
                            destinationFullPath,
                            preserveCompletedOutputOnConflict: true,
                            cancellationToken),
                        cancellationToken);
                    return new IsolatedPdfExportResult(state.Result, commit.OutputPath, commit.Warning);
                }
                catch
                {
                    preserveCompletedOutput = File.Exists(completedOutputPath);
                    throw;
                }
            }

            var phase = DescribePhase(state?.Phase);
            var detail = state?.Error;
            if (string.IsNullOrWhiteSpace(detail)) detail = standardError.Trim();
            if (string.IsNullOrWhiteSpace(detail) &&
                peakWorkerMemoryBytes is { } peak &&
                workerMemoryLimitBytes is { } limit &&
                peak >= limit * 0.95)
                detail =
                    $"PDF出力プロセスがメモリ安全上限付近で停止しました（最大 {FormatBytes(peak)}、上限 {FormatBytes(limit)}）。" +
                    "処理済みページを中間確定する新しい省メモリ経路でも完了できないPDFです。";
            if (string.IsNullOrWhiteSpace(detail))
                detail = $"ネイティブPDF処理が予期せず停止しました（終了コード {process.ExitCode}）。";
            throw new InvalidOperationException(
                $"PDF出力用の別プロセスが「{phase}」で停止しました。編集画面と未保存の変更は保護されています。\n{detail}");
        }
        finally
        {
            TryDelete(temporaryProjectPath);
            TryDelete(temporaryProjectPath + ".bak");
            TryDelete(temporaryProjectPath + ".tmp");
            TryDelete(statePath);
            TryDelete(statePath + ".tmp");
            TryDeleteStateTemporaryFiles(statePath);
            if (!preserveCompletedOutput) TryDelete(completedOutputPath);
            TryDeleteDirectory(exportDirectory);
        }
    }

    /// <summary>長時間のPDF生成を開始する前に、保存先の競合と書込み権限を確認します。</summary>
    internal static void ValidateDestination(string destinationPdfPath) =>
        PdfOutputFileCommitter.ValidateDestination(destinationPdfPath);

    /// <summary>コマンドラインから呼び出されたPDF出力ワーカーを実行します。</summary>
    internal static async Task<int> RunWorkerAsync(
        string sourcePdfPath,
        string projectPath,
        string destinationPdfPath,
        string statePath,
        CancellationToken cancellationToken = default)
    {
        var stateWriter = new ExportWorkerStateWriter(statePath);
        try
        {
            await stateWriter.WriteAsync(new ExportWorkerState("starting"), cancellationToken);
            var packageService = new ProjectPackageService();
            var project = await packageService.OpenAsync(projectPath, cancellationToken);
            if (!await packageService.VerifySourceFileAsync(project.SourcePdf, sourcePdfPath, cancellationToken))
                throw new InvalidDataException(
                    "PDF出力用の元PDFが、準備時に検証したファイルと一致しません。元PDFを開き直して再試行してください。");
            await stateWriter.WriteAsync(new ExportWorkerState("project-opened"), cancellationToken);
            await stateWriter.WriteAsync(new ExportWorkerState("exporting"), cancellationToken);
            var progress = new WorkerProgressReporter(stateWriter);
            var result = await new PdfExportService().ExportAsync(
                sourcePdfPath,
                destinationPdfPath,
                project,
                progress,
                cancellationToken);
            await stateWriter.WriteAsync(new ExportWorkerState("completed", result), cancellationToken);
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                await stateWriter.WriteAsync(
                    new ExportWorkerState("failed", Error: exception.ToString()),
                    CancellationToken.None);
            }
            catch
            {
                // 元の出力例外を優先します。状態ファイルを書けない場合も終了コードで親へ通知します。
            }
            return -1;
        }
    }

    private static string ResolveExecutablePath()
    {
        var packagedExecutable = Path.Combine(AppContext.BaseDirectory, "PdfCorrectorium.exe");
        if (File.Exists(packagedExecutable)) return packagedExecutable;
        return Environment.ProcessPath
            ?? throw new InvalidOperationException("PDF Correctorium の実行ファイルを特定できませんでした。");
    }

    private static async Task<ExportWorkerState?> ReadStateAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ExportWorkerState>(stream, JsonOptions, cancellationToken);
    }

    /// <summary>
    /// ワーカーが状態ファイルを置換している最中でも、親プロセスの監視を失敗させずに読み込みます。
    /// </summary>
    private static async Task<ExportWorkerState?> TryReadStateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadStateAsync(path, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DescribePhase(string? phase) => phase switch
    {
        "starting" => "起動準備",
        "project-opened" => "編集内容の読込後",
        "exporting" => "PDFの生成または検証中",
        "editing" => "変更ページの反映中",
        "checkpointing" => "中間結果の確定とメモリ解放中",
        "saving" => "一時PDFの保存中",
        "calibrating" => "文字位置の校正中",
        "spacing" => "文字送りの反映中",
        "compacting" => "PDFの圧縮中",
        "bookmarks" => "しおりの反映中",
        "validating" => "出力PDFの検証中",
        "committing" => "出力ファイルの確定中",
        "failed" => "PDF出力中",
        "completed" => "完了処理",
        _ => "起動前または初期化中",
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024L * 1024L => $"{bytes / (1024d * 1024d * 1024d):N2} GiB",
        >= 1024L * 1024L => $"{bytes / (1024d * 1024d):N1} MiB",
        _ => $"{bytes:N0} bytes",
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 一時ファイルの後始末に失敗しても、出力結果や本来の例外を上書きしません。
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch
        {
            // A preserved recovery PDF or a transient handle may keep this directory alive.
        }
    }

    private static void TryDeleteStateTemporaryFiles(string statePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(statePath));
            if (directory is null || !Directory.Exists(directory)) return;
            var pattern = Path.GetFileName(statePath) + ".*.tmp";
            foreach (var temporaryPath in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                TryDelete(temporaryPath);
        }
        catch
        {
            // 出力状態の一時ファイルは再利用しないため、回収失敗を本来の結果より優先しません。
        }
    }

    /// <summary>進捗と完了通知を直列化し、一意な一時ファイルから原子的に公開します。</summary>
    private sealed class ExportWorkerStateWriter(string statePath)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task WriteAsync(ExportWorkerState state, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            var fullPath = Path.GetFullPath(statePath);
            var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
                _gate.Release();
            }
        }
    }

    /// <summary>PDF出力サービスの進捗を、親プロセスが監視する状態ファイルへ直ちに反映します。</summary>
    private sealed class WorkerProgressReporter(ExportWorkerStateWriter stateWriter) : IProgress<PdfExportProgress>
    {
        public void Report(PdfExportProgress value)
        {
            try
            {
                stateWriter.WriteAsync(
                        new ExportWorkerState(
                            value.Phase,
                            Current: value.Current,
                            Total: value.Total,
                            Message: value.Message),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // 進捗通知の失敗はPDF生成自体を中断させません。
            }
        }
    }

    private sealed record ExportWorkerState(
        string Phase,
        PdfExportResult? Result = null,
        string? Error = null,
        int Current = 0,
        int Total = 0,
        string? Message = null);
}

/// <summary>別プロセスで生成したPDFの検証結果と、実際に確定したファイルパスを保持します。</summary>
internal sealed record IsolatedPdfExportResult(
    PdfExportResult Result,
    string OutputPath,
    string? Warning = null);
