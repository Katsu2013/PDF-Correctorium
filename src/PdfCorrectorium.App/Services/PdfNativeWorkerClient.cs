using PdfCorrectorium.Core.IO;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace PdfCorrectorium.App.Services;

/// <summary>PDFiumを画面プロセスから隔離した、再利用可能な長寿命ワーカーへ委譲します。</summary>
internal sealed class PdfNativeWorkerClient : IDisposable, IAsyncDisposable
{
    private readonly bool _reuseInputs;
    private readonly bool _includeOcr;
    private string? _ocrSourceIdentity;
    private readonly Dictionary<string, string> _batchInputs = new(StringComparer.OrdinalIgnoreCase);
    internal int InputStageCount { get; private set; }
    internal PdfNativeWorkerClient(bool reuseInputs = false, bool includeOcr = false)
    { _reuseInputs = reuseInputs; _includeOcr = includeOcr; }
    public void Dispose() => Shutdown();
    public ValueTask DisposeAsync() => new(Task.Run(Shutdown));
    public const string WorkerOption = "--pdf-native-worker";
    private const int MaximumProtocolCharacters = 64 * 1024;
    private const long MaximumResultJsonBytes = 256L * 1024 * 1024;
    private const long MaximumPreviewBytes = 64L * 1024 * 1024;
    /// <summary>Current-page editing and explicit user operations use this worker.</summary>
    public static PdfNativeWorkerClient Shared { get; } = new();
    /// <summary>
    /// Thumbnails, continuous-page neighbors and document scans use a separate
    /// process so queued background rendering cannot delay an explicit page move.
    /// </summary>
    public static PdfNativeWorkerClient Background { get; } = new();
    /// <summary>Whole-document font/property inspection never queues ahead of page or thumbnail rendering.</summary>
    public static PdfNativeWorkerClient Inspection { get; } = new();
    internal static PdfNativeWorkerClient Ocr { get; } = new(reuseInputs: true, includeOcr: true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _standardError = new();
    private readonly HashSet<string> _operationDirectories = new(StringComparer.OrdinalIgnoreCase);
    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;
    private WindowsSandboxProcess? _sandbox;

    internal int? WorkerProcessId => _process is { HasExited: false } process ? process.Id : null;
    internal bool WorkerHasResourceJob => _sandbox is { MemoryLimitBytes: > 0 } && WorkerProcessId is not null;

    public async Task<PdfPreviewResult> RenderPageAsync(
        string pdfPath,
        int pageNumber,
        int targetWidth,
        CancellationToken cancellationToken)
    {
        var directory = RegisterOperationDirectory();
        try
        {
            await ExecuteAsync(new WorkerRequest(Guid.NewGuid().ToString("N"), "render", Path.GetFullPath(pdfPath),
                directory, pageNumber, targetWidth), TimeSpan.FromMinutes(2), cancellationToken);
            var metadata = await ReadJsonAsync<PreviewMetadata>(Path.Combine(directory, "result.json"), cancellationToken);
            var image = LoadFrozenBitmap(Path.Combine(directory, "preview.png"));
            return new PdfPreviewResult(image, metadata.PageCount, metadata.PageNumber, metadata.PageWidthPoints,
                metadata.PageHeightPoints, metadata.TextRegions, metadata.SelectableCharacters);
        }
        finally { ReleaseOperationDirectory(directory); }
    }

    internal async Task<BitmapSource> RenderImagePageAsync(string pdfPath, int pageNumber, int dpi, CancellationToken token)
    {
        if (!_reuseInputs) throw new InvalidOperationException("Image export requires an operation-scoped worker.");
        if (dpi is < 72 or > 300) throw new ArgumentOutOfRangeException(nameof(dpi));
        var directory = RegisterOperationDirectory();
        try
        {
            await ExecuteAsync(new WorkerRequest(Guid.NewGuid().ToString("N"), "render-image", Path.GetFullPath(pdfPath),
                directory, pageNumber, dpi), TimeSpan.FromMinutes(2), token);
            return LoadFrozenBitmap(Path.Combine(directory, "preview.png"), forImageExport: true);
        }
        finally { ReleaseOperationDirectory(directory); }
    }

    internal async Task<string[]> ReadOcrWordsAsync(string path, int page, CancellationToken token)
    {
        var directory = RegisterOperationDirectory();
        try
        {
            await ExecuteAsync(new WorkerRequest(Guid.NewGuid().ToString("N"), "ocr-words", Path.GetFullPath(path), directory, page), TimeSpan.FromMinutes(2), token);
            return await ReadJsonAsync<string[]>(Path.Combine(directory, "result.json"), token);
        }
        finally { ReleaseOperationDirectory(directory); }
    }

    internal async Task<PdfPreviewService.SpeechPage> ReadSpeechPageAsync(string path, int page, CancellationToken token)
    {
        var directory = RegisterOperationDirectory();
        try
        {
            await ExecuteAsync(new WorkerRequest(Guid.NewGuid().ToString("N"), "speech-text", Path.GetFullPath(path), directory, page), TimeSpan.FromMinutes(2), token);
            return await ReadJsonAsync<PdfPreviewService.SpeechPage>(Path.Combine(directory, "result.json"), token);
        }
        finally { ReleaseOperationDirectory(directory); }
    }

    public async Task<IReadOnlyList<PdfCharacterBox>> ReadCharacterBoxesAsync(
        string pdfPath,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var directory = RegisterOperationDirectory();
        try
        {
            await ExecuteAsync(new WorkerRequest(Guid.NewGuid().ToString("N"), "characters", Path.GetFullPath(pdfPath),
                directory, pageNumber), TimeSpan.FromMinutes(2), cancellationToken);
            return await ReadJsonAsync<PdfCharacterBox[]>(Path.Combine(directory, "result.json"), cancellationToken);
        }
        finally { ReleaseOperationDirectory(directory); }
    }

    public async Task<PdfDocumentPropertiesInfo> ReadPropertiesAsync(
        string pdfPath,
        CancellationToken cancellationToken)
    {
        var directory = RegisterOperationDirectory();
        try
        {
            await ExecuteAsync(new WorkerRequest(Guid.NewGuid().ToString("N"), "properties", Path.GetFullPath(pdfPath),
                directory), TimeSpan.FromMinutes(3), cancellationToken);
            return await ReadJsonAsync<PdfDocumentPropertiesInfo>(Path.Combine(directory, "result.json"), cancellationToken);
        }
        finally { ReleaseOperationDirectory(directory); }
    }

    internal async Task<RapidLineResult> RecognizeLineAsync(string pdfPath, int page, OcrLineCrop crop, CancellationToken token)
    {
        if (!_includeOcr) throw new InvalidOperationException("Recognition requires the OCR worker.");
        var directory = RegisterOperationDirectory();
        try
        {
            await ExecuteAsync(new(Guid.NewGuid().ToString("N"), "recognize-line", Path.GetFullPath(pdfPath), directory,
                page, Crop: crop), TimeSpan.FromSeconds(90), token);
            var recognition = await ReadJsonAsync<RapidLineRecognition>(Path.Combine(directory, "result.json"), token);
            return new(recognition, LoadFrozenBitmap(Path.Combine(directory, "preview.png")));
        }
        finally { ReleaseOperationDirectory(directory); }
    }

    private async Task ExecuteAsync(WorkerRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // PDFiumはネイティブ状態を共有するため、1つのワーカーに要求を直列送信する。
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_includeOcr)
            {
                var info = new FileInfo(request.PdfPath);
                var identity = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                // A persistent OCR session retains just one source PDF, never an ever-growing batch cache.
                if (_ocrSourceIdentity != identity) { StopWorker(); _ocrSourceIdentity = identity; }
            }
            EnsureWorker();
            var sandbox = _sandbox ?? throw new InvalidOperationException("PDF worker sandbox is missing.");
            var stagedDirectory = Path.Combine(sandbox.WorkDirectory, request.Id);
            Directory.CreateDirectory(stagedDirectory);
            string stagedPdf;
            if (_reuseInputs)
            {
                if (!_batchInputs.TryGetValue(request.PdfPath, out stagedPdf!))
                {
                    stagedPdf = sandbox.StageInput(request.PdfPath, $"batch-input-{_batchInputs.Count}.pdf");
                    _batchInputs.Add(request.PdfPath, stagedPdf);
                    InputStageCount++;
                }
            }
            else
            {
                stagedPdf = Path.Combine(stagedDirectory, "input.pdf");
                File.Copy(request.PdfPath, stagedPdf, false);
            }
            var stagedRequest = request with { PdfPath = stagedPdf, OutputDirectory = stagedDirectory };
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            try
            {
                await _input!.WriteLineAsync(JsonSerializer.Serialize(stagedRequest, JsonOptions).AsMemory(), linkedSource.Token)
                    .ConfigureAwait(false);
                await _input.FlushAsync(linkedSource.Token).ConfigureAwait(false);
                var line = await ReadLineWithLimitAsync(_output!, MaximumProtocolCharacters, linkedSource.Token)
                    .ConfigureAwait(false);
                if (line is null)
                    throw new InvalidOperationException(DescribeWorkerExit("PDF処理ワーカーが応答せず終了しました。"));
                var response = JsonSerializer.Deserialize<WorkerResponse>(line, JsonOptions)
                    ?? throw new InvalidDataException("PDF処理ワーカーから空の応答を受信しました。");
                if (!string.Equals(response.Id, request.Id, StringComparison.Ordinal))
                    throw new InvalidDataException("PDF処理ワーカーの応答順序が一致しません。");
                if (!response.Success)
                    throw new InvalidDataException(response.Error ?? "PDF処理ワーカーが失敗しました。");
                sandbox.CopyOutput(Path.Combine(stagedDirectory, "result.json"), Path.Combine(request.OutputDirectory, "result.json"), MaximumResultJsonBytes);
                if (request.Operation is "render" or "render-image" or "recognize-line")
                    sandbox.CopyOutput(Path.Combine(stagedDirectory, "preview.png"), Path.Combine(request.OutputDirectory, "preview.png"), MaximumPreviewBytes);
                TryDeleteDirectory(stagedDirectory);
            }
            catch (OperationCanceledException)
            {
                // 応答途中のワーカーを再利用すると次の要求と応答が混ざるため、キャンセル時は破棄する。
                StopWorker();
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TimeoutException($"PDF処理が制限時間 {timeout.TotalSeconds:N0} 秒以内に完了しませんでした。");
            }
            catch
            {
                // プロトコル破損や出力上限違反の後は応答境界を信頼できないため、必ず再起動する。
                StopWorker();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private void EnsureWorker()
    {
        if (_process is { HasExited: false }) return;
        // 前回の異常終了後も同じ標準入出力ストリームを再利用しないよう、常に新しいプロセスを作る。
        StopWorker();
        lock (_standardError) _standardError.Clear();
        var sandbox = new WindowsSandboxProcess();
        try
        {
            var executable = sandbox.StageApplication(ResolveExecutablePath(), includeOcr: _includeOcr);
            sandbox.Start(executable, [WorkerOption]);
            _sandbox = sandbox; _process = sandbox.Process;
            _input = sandbox.StandardInput; _output = sandbox.StandardOutput;
            _ = CaptureErrorAsync(sandbox.StandardError, sandbox.Process);
        }
        catch { sandbox.Dispose(); throw; }

    }

    public void Shutdown()
    {
        StopWorker(graceful: true);
        string[] pending;
        lock (_operationDirectories)
        {
            pending = _operationDirectories.ToArray();
            _operationDirectories.Clear();
        }
        foreach (var directory in pending) TryDeleteDirectory(directory);
    }

    private void StopWorker(bool graceful = false)
    {
        _batchInputs.Clear();
        var process = _process;
        var sandbox = _sandbox;
        _process = null;
        _sandbox = null;
        _input = null;
        _output = null;
        if (process is null)
        {
            sandbox?.Dispose();
            return;
        }
        try { process.StandardInput.Close(); } catch { }
        try
        {
            if (graceful && !process.HasExited) process.WaitForExit(1000);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (!process.HasExited) process.WaitForExit(5000);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        sandbox?.Dispose();
    }

    private async Task CaptureErrorAsync(StreamReader reader, Process process)
    {
        try
        {
            var buffer = new char[1024]; int count;
            while ((count = await reader.ReadAsync(buffer)) > 0)
            {
                lock (_standardError)
                {
                    if (_standardError.Length > 32768 - count)
                    { ExternalProcessRunner.TryTerminate(process); return; }
                    _standardError.Append(buffer, 0, count);
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private string DescribeWorkerExit(string message)
    {
        string error;
        lock (_standardError) error = _standardError.ToString().Trim();
        var exit = _process is { HasExited: true } process ? $" 終了コード: {process.ExitCode}。" : string.Empty;
        return string.IsNullOrWhiteSpace(error) ? message + exit : message + exit + Environment.NewLine + error;
    }

    /// <summary>ワーカープロセス側で標準入力の1行JSON要求を処理し、1行JSON応答を返します。</summary>
    /// <remarks>出力先を専用一時領域に限定し、画像・JSONの成果物サイズも制限します。</remarks>
    internal static async Task<int> RunServerAsync(CancellationToken cancellationToken = default)
    {
        using var recognizer = new RapidLineRecognizer();
        string? line;
        while ((line = await ReadLineWithLimitAsync(Console.In, MaximumProtocolCharacters, cancellationToken)) is not null)
        {
            WorkerRequest? request = null;
            WorkerResponse response;
            try
            {
                request = JsonSerializer.Deserialize<WorkerRequest>(line, JsonOptions)
                    ?? throw new InvalidDataException("PDF worker request is empty.");
                var outputDirectory = EnsureWorkerOutputDirectory(request.OutputDirectory);
                Directory.CreateDirectory(outputDirectory);
                switch (request.Operation)
                {
                    case "recognize-line":
                        {
                            var result = recognizer.Recognize(request.PdfPath, request.PageNumber,
                                request.Crop ?? throw new InvalidDataException("Missing OCR crop."), cancellationToken);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result.Image));
                            await using (var stream = new LengthLimitedWriteStream(new FileStream(
                                Path.Combine(outputDirectory, "preview.png"), FileMode.CreateNew), MaximumPreviewBytes)) encoder.Save(stream);
                            await WriteJsonAsync(Path.Combine(outputDirectory, "result.json"), result.Recognition, cancellationToken);
                            break;
                        }
                    case "render":
                    case "render-image":
                        {
                            var result = request.Operation == "render-image"
                                ? PdfPreviewService.RenderImagePageInProcess(request.PdfPath, request.PageNumber, request.TargetWidth, cancellationToken)
                                : PdfPreviewService.RenderPageInProcess(request.PdfPath, request.PageNumber, request.TargetWidth, cancellationToken);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(result.Image));
                            await using (var stream = new LengthLimitedWriteStream(
                                             new FileStream(Path.Combine(outputDirectory, "preview.png"), FileMode.CreateNew,
                                                 FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous),
                                             MaximumPreviewBytes))
                                encoder.Save(stream);
                            await WriteJsonAsync(Path.Combine(outputDirectory, "result.json"),
                                new PreviewMetadata(result.PageCount, result.PageNumber, result.PageWidthPoints,
                                    result.PageHeightPoints, result.TextRegions, result.SelectableCharacters), cancellationToken);
                            break;
                        }
                    case "ocr-words":
                        await WriteJsonAsync(Path.Combine(outputDirectory, "result.json"),
                            PdfPreviewService.ReadOcrWordsInProcess(request.PdfPath, request.PageNumber, cancellationToken), cancellationToken);
                        break;
                    case "speech-text":
                        await WriteJsonAsync(Path.Combine(outputDirectory, "result.json"),
                            PdfPreviewService.ReadSpeechPageInProcess(request.PdfPath, request.PageNumber, cancellationToken), cancellationToken);
                        break;
                    case "characters":
                        await WriteJsonAsync(Path.Combine(outputDirectory, "result.json"),
                            PdfPreviewService.ReadCharacterBoxesInProcess(request.PdfPath, request.PageNumber, cancellationToken),
                            cancellationToken);
                        break;
                    case "properties":
                        await WriteJsonAsync(Path.Combine(outputDirectory, "result.json"),
                            PdfDocumentPropertiesService.ReadInProcess(request.PdfPath, cancellationToken), cancellationToken);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown PDF worker operation: {request.Operation}");
                }
                response = new WorkerResponse(request.Id, true);
            }
            catch (Exception exception)
            {
                var detail = exception.ToString();
                response = new WorkerResponse(request?.Id ?? string.Empty, false,
                    detail.Length <= 32_768 ? detail : detail[..32_768]);
            }
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            await Console.Out.FlushAsync(cancellationToken);
        }
        return 0;
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        EnsureFileSize(path, MaximumResultJsonBytes, "JSON結果");
        await using var stream = File.OpenRead(path);
        await new JsonInputBudget { MaximumTokens = 16_000_000, MaximumObjects = 1_000_000,
            MaximumTotalStringCharacters = 64L * 1024 * 1024 }.ValidateAsync(stream, cancellationToken);
        stream.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("PDF処理ワーカーの結果が空です。");
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new LengthLimitedWriteStream(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true),
            MaximumResultJsonBytes);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static BitmapSource LoadFrozenBitmap(string path, bool forImageExport = false)
    {
        EnsureFileSize(path, MaximumPreviewBytes, "プレビュー画像");
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
        var maximumWidth = forImageExport ? 8192 : 2400;
        var maximumHeight = forImageExport ? 8192 : 3200;
        var maximumPixels = forImageExport ? 20_000_000 : 7_680_000;
        if (decoder is not PngBitmapDecoder || decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth < 1 || decoder.Frames[0].PixelWidth > maximumWidth ||
            decoder.Frames[0].PixelHeight < 1 || decoder.Frames[0].PixelHeight > maximumHeight ||
            (long)decoder.Frames[0].PixelWidth * decoder.Frames[0].PixelHeight > maximumPixels)
            throw new InvalidDataException("PDF worker preview dimensions exceed the display budget.");
        stream.Position = 0;
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string CreateOperationDirectory()
    {
        var root = GetWorkerRoot();
        Directory.CreateDirectory(root);
        CleanupOldOperationDirectories(root);
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetWorkerRoot() => WindowsSandboxProcess.IsAppContainer
        ? Path.GetFullPath(Environment.GetEnvironmentVariable("PDFCORRECTORIUM_WORKER_ROOT") ?? throw new InvalidOperationException("Worker directory is missing."))
        : Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PdfCorrectorium", "native-workers"));

    /// <summary>ワーカー成果物の出力先が専用一時領域の配下にあることを検証します。</summary>
    private static string EnsureWorkerOutputDirectory(string path)
    {
        var root = GetWorkerRoot();
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PDF処理ワーカーの出力先が許可された一時領域の外です。");
        return fullPath;
    }

    /// <summary>ワーカーが生成した成果物を読み込む前に、空ファイルと過大ファイルを拒否します。</summary>
    private static void EnsureFileSize(string path, long maximumBytes, string kind)
    {
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > maximumBytes)
            throw new InvalidDataException($"PDF処理ワーカーの{kind}が許可サイズを超えています。");
    }

    private static async Task<string?> ReadLineWithLimitAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(4096, maximumCharacters));
        var buffer = new char[1];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) return builder.Length == 0 ? null : builder.ToString();
            if (buffer[0] == '\n') return builder.ToString().TrimEnd('\r');
            if (builder.Length >= maximumCharacters)
                throw new InvalidDataException($"PDF処理ワーカーの通信行が上限 {maximumCharacters:N0} 文字を超えました。");
            builder.Append(buffer[0]);
        }
    }

    private string RegisterOperationDirectory()
    {
        var directory = CreateOperationDirectory();
        lock (_operationDirectories) _operationDirectories.Add(directory);
        return directory;
    }

    private void ReleaseOperationDirectory(string directory)
    {
        TryDeleteDirectory(directory);
        lock (_operationDirectories) _operationDirectories.Remove(directory);
    }

    private static string ResolveExecutablePath()
    {
        var adjacent = Path.Combine(AppContext.BaseDirectory, "PdfCorrectorium.exe");
        if (File.Exists(adjacent)) return adjacent;
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            return Environment.ProcessPath;
        throw new FileNotFoundException("PDF処理ワーカー用のPdfCorrectorium.exeが見つかりません。", adjacent);
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(50); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }
    }

    private static int _oldOperationCleanupPerformed;

    private static void CleanupOldOperationDirectories(string root)
    {
        if (Interlocked.Exchange(ref _oldOperationCleanupPerformed, 1) != 0) return;
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff) TryDeleteDirectory(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record WorkerRequest(
        string Id,
        string Operation,
        string PdfPath,
        string OutputDirectory,
        int PageNumber = 0,
        int TargetWidth = 0,
        OcrLineCrop? Crop = null);
    private sealed record WorkerResponse(string Id, bool Success, string? Error = null);
    private sealed record PreviewMetadata(
        int PageCount,
        int PageNumber,
        double PageWidthPoints,
        double PageHeightPoints,
        IReadOnlyList<PdfTextOverlayRegion> TextRegions,
        IReadOnlyList<PdfSelectableTextCharacter> SelectableCharacters);
}
