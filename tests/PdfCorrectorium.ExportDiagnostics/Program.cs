using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.ProjectFormat;

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
