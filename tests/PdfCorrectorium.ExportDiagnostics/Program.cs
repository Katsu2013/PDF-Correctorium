using PdfCorrectorium.App.Services;
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
        await using var destinationLock = new FileStream(
            destinationPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

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
        var commit = PdfOutputFileCommitter.Commit(
            completedPath,
            destinationPath,
            preserveCompletedOutputOnConflict: true,
            CancellationToken.None);
        if (!File.Exists(commit.OutputPath) || string.IsNullOrWhiteSpace(commit.Warning))
            throw new InvalidOperationException("The completed PDF was not preserved under a recovery name.");

        Console.WriteLine($"Output commit test passed. Recovery={commit.OutputPath}");
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
var sourcePath = !string.IsNullOrWhiteSpace(project.SourcePdf.RelativePath)
    ? Path.GetFullPath(Path.Combine(projectDirectory, project.SourcePdf.RelativePath))
    : project.SourcePdf.AbsolutePathHint
      ?? throw new FileNotFoundException("The source PDF could not be resolved.");
var result = await new PdfExportService().ExportAsync(sourcePath, outputPath, project);
Console.WriteLine($"Pages={result.ModifiedPages}; Regions={result.ModifiedRegions}; Output={outputPath}");
