using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;
using State = PdfCorrectorium.App.Services.IsolatedPdfExportService.ExportWorkerState;
using Writer = PdfCorrectorium.App.Services.IsolatedPdfExportService.ExportWorkerStateWriter;

internal static class ExportStateRegressionChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const long StateBudget = 4 * 1024 * 1024;

    internal static async Task RunAsync(string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(root)) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
            lock (checks) { checks.Add("PASS: " + message); Console.WriteLine("PASS: " + message); }
        }
        try
        {
            using (var sandbox = new WindowsSandboxProcess())
            {
                var path = Path.Combine(sandbox.WorkDirectory, "state.json");
                var writer = new Writer(path);
                await writer.WriteAsync(new("starting"), CancellationToken.None);
                // Negative control reproduces the previous reader/writer sharing conflict.
                using (sandbox.OpenOutput(path, StateBudget))
                {
                    var blocked = false;
                    try { await writer.WriteAsync(new("exporting"), CancellationToken.None); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { blocked = true; }
                    Check(blocked, "A persistently locked old-style reader fails closed after the bounded retry window.");
                }
                Check((await ReadAsync(sandbox, path)).Phase == "starting", "A rejected replacement preserves the previous complete snapshot.");
                Check(!Directory.EnumerateFiles(sandbox.WorkDirectory, "*.tmp").Any(), "A rejected write cleans its unique temporary file.");

                await using (var retained = sandbox.OpenStateOutput(path, StateBudget))
                {
                    var legacyPath = Path.Combine(sandbox.WorkDirectory, "legacy-move.tmp");
                    await File.WriteAllTextAsync(legacyPath, "{}");
                    var legacyMoveBlocked = false;
                    try { File.Move(legacyPath, path, overwrite: true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { legacyMoveBlocked = true; }
                    finally { if (File.Exists(legacyPath)) File.Delete(legacyPath); }
                    Check(legacyMoveBlocked, "Legacy one-shot overwrite-move deterministically reproduces the read/update conflict even with delete sharing.");
                    var writeDenied = false;
                    try { using var mutation = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
                    catch (IOException) { writeDenied = true; }
                    Check(writeDenied, "The state reader still denies in-place writes.");
                    var pending = writer.WriteAsync(new("exporting", Current: 1, Total: 128), CancellationToken.None);
                    await Task.Delay(150);
                    Check(!pending.IsCompleted, "State publication waits for a transiently retained reader instead of failing immediately.");
                    Check((await JsonSerializer.DeserializeAsync<State>(retained, JsonOptions))?.Phase == "starting",
                        "The retained verified handle reads the old complete snapshot, not a mixed or truncated update.");
                    await retained.DisposeAsync();
                    await pending.WaitAsync(TimeSpan.FromSeconds(5));
                    Check((await ReadAsync(sandbox, path)).Current == 1, "State publication recovers when the transient reader closes.");
                }
                for (var i = 1; i <= 128; i++)
                    await writer.WriteAsync(new("exporting", Current: i, Total: 128, Message: new string('x', 4096)), CancellationToken.None);
                Check((await ReadAsync(sandbox, path)).Current == 128, "128 sequential updates publish complete state snapshots.");

                var reads = Task.Run(async () =>
                {
                    var completeReads = 0;
                    for (var i = 0; i < 256; i++)
                    {
                        // Publication can briefly make the path unavailable.
                        // Exercise the real monitor's skip-and-poll logic.
                        var state = await IsolatedPdfExportService.TryReadStateAsync(sandbox, path, CancellationToken.None);
                        if (state is not null && (state.Phase != "exporting" || state.Current < 0 || state.Current > 128))
                            throw new InvalidDataException("A concurrent reader observed an invalid snapshot.");
                        if (state is not null) completeReads++;
                        await Task.Delay(1);
                    }
                    if (completeReads == 0) throw new InvalidOperationException("Polling never recovered after updates.");
                });
                await Task.WhenAll(Enumerable.Range(0, 128).Select(i =>
                    writer.WriteAsync(new("exporting", Current: i, Total: 128), CancellationToken.None)).Append(reads));
                Check(true, "128 concurrent publications and 256 real-monitor polls preserve complete JSON and recover from transient busy snapshots.");
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel(); var stopped = false;
                    try { await writer.WriteAsync(new("cancelled"), cancelled.Token); }
                    catch (OperationCanceledException) { stopped = true; }
                    Check(stopped, "State publication observes cancellation.");
                }
                await writer.WriteAsync(new("completed"), CancellationToken.None);
                Check((await ReadAsync(sandbox, path)).Phase == "completed", "Publication remains usable after a failed or cancelled write.");
                Check(!Directory.EnumerateFiles(sandbox.WorkDirectory, "*.tmp").Any(), "Successful concurrent writes leave no temporary state files.");
                var outside = Path.Combine(root, "outside.json"); await File.WriteAllTextAsync(outside, "{}");
                var outsideRejected = false;
                try { using var invalid = sandbox.OpenStateOutput(outside, StateBudget); }
                catch (InvalidDataException) { outsideRejected = true; }
                Check(outsideRejected, "The replaceable-state reader retains verified operation-directory containment.");
                var oversizedRejected = false;
                try { using var invalid = sandbox.OpenStateOutput(path, 4); }
                catch (InvalidDataException) { oversizedRejected = true; }
                Check(oversizedRejected, "The replaceable-state reader retains the byte budget.");
                using (sandbox.OpenOutput(path, StateBudget))
                {
                    using var cancelledRetry = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                    var cancelledDuringRetry = false;
                    try { await writer.WriteAsync(new("cancelled"), cancelledRetry.Token); }
                    catch (OperationCanceledException) { cancelledDuringRetry = true; }
                    Check(cancelledDuringRetry, "Cancellation interrupts a sharing-conflict retry and cleans its temporary file.");
                    var blocked = false;
                    var deadline = Stopwatch.StartNew();
                    try { await writer.WriteAsync(new("forbidden"), CancellationToken.None); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { blocked = true; }
                    Check(blocked && deadline.Elapsed < TimeSpan.FromSeconds(8), "Completed-output reads remain strict and persistent conflicts fail within a bounded deadline.");
                }
            }

            var input = Path.Combine(root, "source.pdf");
            FollowupSecurityRegressionChecks.MakeTagged(input, false);
            var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(input));
            var packages = new ProjectPackageService();
            var source = (await packages.CreateSourceReferenceAsync(input, root)) with { PageCount = 1 };
            var pageId = Guid.NewGuid();
            var project = new PdfCorrectoriumProject
            {
                SourcePdf = source,
                Pages = [new OcrPage { Id = pageId, PageNumber = 1, WidthPoints = 100, HeightPoints = 100 }],
                Redactions = [new PdfRedaction { PageId = pageId, Bounds = new(new PdfPoint(0, 0), new PdfSize(100, 100)) }],
            };
            var projectPath = Path.Combine(root, "input.pdfocrproj");
            await packages.SaveAsync(projectPath, project, embedSourcePdf: false);
            using (var sandbox = new WindowsSandboxProcess())
            {
                var executable = sandbox.StageApplication(Path.Combine(AppContext.BaseDirectory, "PdfCorrectorium.exe"));
                var stagedSource = sandbox.StageInput(input, "input.pdf");
                var stagedProject = sandbox.StageInput(projectPath, "project.pdfocrproj");
                var output = Path.Combine(sandbox.WorkDirectory, "completed.pdf");
                var statePath = Path.Combine(sandbox.WorkDirectory, "export.state.json");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                sandbox.Start(executable, ["--isolated-pdf-export", stagedSource, stagedProject, output, statePath]);
                var stderr = ExternalProcessRunner.ReadToEndWithLimitAsync(sandbox.StandardError, 4 * 1024 * 1024, timeout.Token);
                var stdout = ExternalProcessRunner.ReadToEndWithLimitAsync(sandbox.StandardOutput, 4 * 1024 * 1024, timeout.Token);
                var exit = sandbox.Process.WaitForExitAsync(timeout.Token);
                FileStream? held = null;
                while (held is null && !exit.IsCompleted)
                {
                    try { if (File.Exists(statePath)) held = sandbox.OpenStateOutput(statePath, StateBudget); }
                    catch (IOException) { }
                    if (held is null) await Task.Delay(1, timeout.Token);
                }
                await using var retained = held ?? throw new InvalidOperationException("Worker exited before a state snapshot could be held.");
                var initial = await JsonSerializer.DeserializeAsync<State>(retained, JsonOptions, timeout.Token);
                Check(initial is not null && initial.Phase != "completed" && initial.Phase != "failed",
                    "A real AppContainer worker's state reader is held before export completes.");
                await Task.Delay(200, timeout.Token);
                Check(!exit.IsCompleted, "The real worker tolerates a deliberately retained progress reader while awaiting publication.");
                await retained.DisposeAsync();
                await exit;
                var errors = await stderr; var messages = await stdout;
                var final = await ReadAsync(sandbox, statePath);
                await File.WriteAllTextAsync(Path.Combine(root, "held-worker-state.json"), JsonSerializer.Serialize(final, JsonOptions));
                await File.WriteAllTextAsync(Path.Combine(root, "held-worker-stderr.txt"), errors);
                await File.WriteAllTextAsync(Path.Combine(root, "held-worker-stdout.txt"), messages);
                Check(sandbox.Process.ExitCode == 0, "The AppContainer worker completes after the retained reader closes. Exit=" + sandbox.Process.ExitCode + " " + final.Error + errors);
                Check(final.Phase == "completed" && final.Result?.AppliedRedactions == 1,
                    "The real worker publishes its final result after recovering from reader contention.");
                var copy = Path.Combine(root, "retained-reader-redacted.pdf"); sandbox.CopyOutput(output, copy, 1024 * 1024);
                Check(PdfPreviewService.RenderPageInProcess(copy, 1, 200, CancellationToken.None).SelectableCharacters.Count == 0,
                    "The held-reader export produces a readable PDF without the redacted text.");
            }
            // Exercise the actual parent polling loop concurrently, not just the transport helper.
            await Task.WhenAll(Enumerable.Range(1, 3).Select(async i =>
            {
                var operation = Path.Combine(root, "parallel-" + i); Directory.CreateDirectory(operation);
                var paths = new ApplicationPaths(StorageMode.Portable, operation, operation, Path.Combine(operation, "cache"), operation);
                var result = await new IsolatedPdfExportService(packages, paths).ExportAsync(input, Path.Combine(operation, "output.pdf"), project);
                Check(result.Result.AppliedRedactions == 1 && File.Exists(result.OutputPath), "Concurrent parent-monitored isolated export completes: " + i);
            }));
            var finalSourceHash = SHA256.HashData(await File.ReadAllBytesAsync(input));
            Check(sourceHash.SequenceEqual(finalSourceHash), "State-race and parallel export diagnostics leave the source PDF unchanged.");
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "failure.txt"), error.ToString());
            throw;
        }
        finally { await File.WriteAllLinesAsync(Path.Combine(root, "checks.txt"), checks); }
        Console.WriteLine($"Export-state regression checks passed: {checks.Count}. {root}");
    }

    private static async Task<State> ReadAsync(WindowsSandboxProcess sandbox, string path)
    {
        await using var stream = sandbox.OpenStateOutput(path, StateBudget);
        return await JsonSerializer.DeserializeAsync<State>(stream, JsonOptions)
            ?? throw new InvalidDataException("Missing worker state.");
    }
}
