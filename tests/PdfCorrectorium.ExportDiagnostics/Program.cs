using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.ProjectFormat;

if (args.Length == 2 && args[0] == "--audit-review-test")
{
    await AuditReviewRegressionChecks.RunAsync(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "--export-state-test")
{
    await ExportStateRegressionChecks.RunAsync(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "--followup-security-test")
{
    var root = Path.GetFullPath(args[1]); if (Directory.Exists(root)) throw new IOException("Use a new output directory."); Directory.CreateDirectory(root);
    var checks = new List<string>();
    await FollowupSecurityRegressionChecks.RunAsync(root, (passed, text) => { if (!passed) throw new InvalidOperationException(text); checks.Add("PASS: " + text); Console.WriteLine("PASS: " + text); });
    await File.WriteAllLinesAsync(Path.Combine(root, "checks.txt"), checks); return;
}
if (args.Length == 2 && args[0] == "--sandbox-test")
{
    var root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
    var outside = Path.Combine(root, "outside-secret.txt"); await File.WriteAllTextAsync(outside, "private audit fixture");
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
    try
    {
        using (var control = new System.Net.Sockets.TcpClient())
        {
            await control.ConnectAsync(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port).WaitAsync(TimeSpan.FromSeconds(5));
            using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        using var sandbox = new WindowsSandboxProcess();
        var executable = sandbox.StageApplication(Path.Combine(AppContext.BaseDirectory, "PdfCorrectorium.exe"));
        var inside = Path.Combine(sandbox.WorkDirectory, "inside.txt"); await File.WriteAllTextAsync(inside, "allowed");
        sandbox.Start(executable, ["--sandbox-probe", inside, outside, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port.ToString()]);
        var output = sandbox.StandardOutput.ReadToEndAsync(); var error = sandbox.StandardError.ReadToEndAsync();
        await sandbox.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
        var text = await output; await File.WriteAllTextAsync(Path.Combine(root, "sandbox.json"), text);
        Console.WriteLine(text); Console.WriteLine(await error);
        if (sandbox.Process.ExitCode != 0 || listener.Pending())
        {
            foreach (var log in Directory.EnumerateFiles(sandbox.WorkDirectory, "*.log", SearchOption.AllDirectories))
                File.Copy(log, Path.Combine(root, Path.GetFileName(log)), true);
            throw new InvalidOperationException("Sandbox proof failed: " + sandbox.Process.ExitCode);
        }
    }
    finally { listener.Stop(); }
    return;
}
if (args.Length == 2 && args[0] == "--security-test")
{
    await SecurityRegressionChecks.RunAsync(args[1]);
    return;
}

if (args.Length == 1 && string.Equals(args[0], "--output-commit-test", StringComparison.Ordinal))
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"pdf-correctorium-commit-{Guid.NewGuid():N}");
    Directory.CreateDirectory(testDirectory);
    var destinationPath = Path.Combine(testDirectory, "output.pdf");
    var completedPath = Path.Combine(testDirectory, "completed.pdf");
    await File.WriteAllTextAsync(destinationPath, "existing PDF placeholder");

    try
    {
        PdfOutputFileCommitter.ValidateDestination(destinationPath);
        await using (var destinationLock = new FileStream(
                         destinationPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.None))
        {
            try
            {
                PdfOutputFileCommitter.ValidateDestination(destinationPath);
                throw new InvalidOperationException("An occupied output PDF was not detected.");
            }
            catch (IOException)
            {
                // Expected: another application has the destination open exclusively.
            }

            await File.WriteAllTextAsync(completedPath, "completed PDF placeholder");
            var recoveryCommit = PdfOutputFileCommitter.Commit(
                completedPath,
                destinationPath,
                preserveCompletedOutputOnConflict: true,
                CancellationToken.None);
            if (!File.Exists(recoveryCommit.OutputPath) || string.IsNullOrWhiteSpace(recoveryCommit.Warning))
                throw new InvalidOperationException("The completed PDF was not preserved under a recovery name.");
        }

        await File.WriteAllTextAsync(completedPath, "replacement PDF placeholder");
        var commit = PdfOutputFileCommitter.Commit(
            completedPath,
            destinationPath,
            preserveCompletedOutputOnConflict: false,
            CancellationToken.None);
        if (!string.Equals(await File.ReadAllTextAsync(destinationPath), "replacement PDF placeholder", StringComparison.Ordinal) ||
            File.Exists(completedPath) ||
            Directory.EnumerateFiles(testDirectory, ".pc-*.bak").Any())
            throw new InvalidOperationException("Atomic replacement did not clean up its operation-owned files.");

        Console.WriteLine($"Output commit test passed. Replacement={commit.OutputPath}");
        return;
    }
    finally
    {
        try { Directory.Delete(testDirectory, recursive: true); } catch { }
    }
}

if (args.Length != 2)
    throw new ArgumentException("Expected a .pdfocrproj path and an output PDF path, or --output-commit-test.");

var projectPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var package = new ProjectPackageService();
var project = await package.OpenAsync(projectPath);
var projectDirectory = Path.GetDirectoryName(projectPath)!;
var operationDirectory = Path.Combine(Path.GetTempPath(), $"PdfCorrectorium-project-export-{Guid.NewGuid():N}");
Directory.CreateDirectory(operationDirectory);
try
{
    var sourcePath = project.SourcePdf.IsEmbedded
        ? await package.MaterializeEmbeddedSourceAsync(projectPath, project.SourcePdf, Path.Combine(operationDirectory, "cache"))
        : package.ResolveSourcePath(project.SourcePdf, projectDirectory);
    var sequence = ProjectPageSequence.Normalize(
        project.PageSequence,
        project.Pages,
        project.SourcePdf.PageCount ?? project.PageSequence.Count);
    var exportProject = project;
    if (!ProjectPageSequence.IsPhysicalIdentity(sequence, project.SourcePdf.PageCount ?? sequence.Count))
    {
        var materializedPath = Path.Combine(operationDirectory, "document.pdf");
        await new PdfPageManagementService().MaterializeAsync(sourcePath, sequence, materializedPath);
        sourcePath = materializedPath;
        exportProject = project with
        {
            SourcePdf = project.SourcePdf with { PageCount = sequence.Count },
            PageSequence = ProjectPageSequence.AsMaterialized(sequence),
        };
    }

    var result = await new PdfExportService().ExportAsync(sourcePath, outputPath, exportProject);
    Console.WriteLine($"Pages={result.ModifiedPages}; Regions={result.ModifiedRegions}; Output={outputPath}");
}
finally
{
    try { Directory.Delete(operationDirectory, recursive: true); } catch { }
}
