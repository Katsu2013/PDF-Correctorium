using System.Diagnostics;
using System.IO;
using System.Text;

namespace PdfCorrectorium.App.Services;

/// <summary>外部ツールを、出力回収・期限・プロセスツリー終了を共通化して実行します。</summary>
internal static class ExternalProcessRunner
{
    internal sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    public static async Task<Result> RunAsync(
        string executablePath,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        int maximumOutputCharacters = 64 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maximumOutputCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumOutputCharacters));

        if (!WindowsSandboxProcess.IsAppContainer)
            return await RunSandboxedAsync(executablePath, arguments, timeout, cancellationToken, maximumOutputCharacters);

        // Already inside an AppContainer: descendants inherit the same capability-free token.
        // stdoutとstderrを待機前に読み始め、子プロセスの出力バッファ詰まりによるデッドロックを避ける。
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"外部処理を起動できませんでした: {Path.GetFileName(executablePath)}");
        try
        {
            using var job = WindowsProcessJob.Attach(process);
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            var standardOutput = ReadToEndWithLimitAsync(process.StandardOutput, maximumOutputCharacters, linkedSource.Token);
            var standardError = ReadToEndWithLimitAsync(process.StandardError, maximumOutputCharacters, linkedSource.Token);
            var outputFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            MonitorFailure(standardOutput, outputFailure);
            MonitorFailure(standardError, outputFailure);
            var exitTask = process.WaitForExitAsync(linkedSource.Token);
            var completed = await Task.WhenAny(exitTask, outputFailure.Task).ConfigureAwait(false);
            if (completed == outputFailure.Task) throw await outputFailure.Task.ConfigureAwait(false);
            await exitTask.ConfigureAwait(false);
            return new Result(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // タイムアウトも利用者キャンセルも、残った子プロセスが作業を続けないようツリーごと終了する。
            TryTerminate(process);
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException(
                $"外部処理 {Path.GetFileName(executablePath)} が制限時間 {timeout.TotalSeconds:N0} 秒以内に完了しませんでした。");
        }
        catch
        {
            TryTerminate(process);
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private static async Task<Result> RunSandboxedAsync(string executable, IEnumerable<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken, int maximumOutputCharacters)
    {
        using var sandbox = new WindowsSandboxProcess();
        var stagedExecutable = Path.GetFileName(executable).Equals("qpdf.exe", StringComparison.OrdinalIgnoreCase)
            ? sandbox.StageTool(executable) : sandbox.StageApplication(executable);
        var inputMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outputMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stagedArguments = new List<string>();
        foreach (var argument in arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var equals = argument.StartsWith("--", StringComparison.Ordinal) ? argument.IndexOf('=') : -1;
            var value = equals >= 0 ? argument[(equals + 1)..] : argument;
            if (!Path.IsPathFullyQualified(value)) { stagedArguments.Add(argument); continue; }
            var full = Path.GetFullPath(value);
            if (!inputMap.TryGetValue(full, out var staged) && !outputMap.TryGetValue(full, out staged))
            {
                if (File.Exists(full)) { staged = sandbox.StageInput(full); inputMap[full] = staged; }
                else { staged = Path.Combine(sandbox.WorkDirectory, Guid.NewGuid().ToString("N") + Path.GetExtension(full)); outputMap[full] = staged; }
            }
            stagedArguments.Add(equals >= 0 ? argument[..(equals + 1)] + staged : staged);
        }
        sandbox.Start(stagedExecutable, stagedArguments);
        sandbox.StandardInput.Close();
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken);
        var output = ReadToEndWithLimitAsync(sandbox.StandardOutput, maximumOutputCharacters, linked.Token);
        var error = ReadToEndWithLimitAsync(sandbox.StandardError, maximumOutputCharacters, linked.Token);
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        MonitorFailure(output, failure); MonitorFailure(error, failure);
        try
        {
            var exited = sandbox.Process.WaitForExitAsync(linked.Token);
            if (await Task.WhenAny(exited, failure.Task).ConfigureAwait(false) == failure.Task) throw await failure.Task;
            await exited.ConfigureAwait(false);
            var result = new Result(sandbox.Process.ExitCode, await output, await error);
            if (result.ExitCode is 0 or 3)
                foreach (var pair in outputMap)
                    if (File.Exists(pair.Value)) sandbox.CopyOutput(pair.Value, pair.Key, 16L * 1024 * 1024 * 1024);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"外部処理 {Path.GetFileName(executable)} が制限時間 {timeout.TotalSeconds:N0} 秒以内に完了しませんでした。");
        }
    }

    internal static async Task<string> ReadToEndWithLimitAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder(Math.Min(maximumCharacters, 8192));
        var buffer = new char[8192];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) return result.ToString();
            if (result.Length > maximumCharacters - read)
                throw new InvalidDataException($"外部処理の出力が上限 {maximumCharacters:N0} 文字を超えました。");
            result.Append(buffer, 0, read);
        }
    }

    private static void MonitorFailure(Task task, TaskCompletionSource<Exception> destination) =>
        _ = task.ContinueWith(
            completed => destination.TrySetResult(completed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    internal static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
