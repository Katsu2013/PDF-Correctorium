using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core;
using System.Reflection;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Geometry validation accepts vertical rotated text", GeometryValidationAsync),
    ("OCR text edit preserves the original value", TextEditAsync),
    ("FR-400 empty edits and legacy text sentinels round-trip", EmptyTextCompatibilityAsync),
    ("FR-1000 package version gates reject unsupported formats", PackageVersionGateAsync),
    ("Build revision matches every application assembly and manifest", BuildVersionAsync),
    ("Project package round-trips and validates", ProjectRoundTripAsync),
    ("Project autosave restores a damaged package", ProjectAutoSaveRecoveryAsync),
    ("Project package preserves compressed page thumbnails", ProjectThumbnailCacheAsync),
    ("Legacy PdfOcrEditor project packages remain readable", LegacyProjectFormatAsync),
    ("Project validator rejects a missing manifest", MissingManifestAsync),
    ("Source fingerprint detects source changes", SourceFingerprintAsync),
    ("Portable marker selects portable storage", PortablePathsAsync),
    ("OCR quality analyzer finds character-count outliers", CharacterCountAnomalyAsync),
    ("OCR quality analyzer finds keyword-width outliers", KeywordWidthAnomalyAsync),
    ("PDF viewer settings map to Acrobat facing-page layouts", ViewerSettingsMappingAsync),
    ("PDF output versions map and reject unsafe downgrades", OutputVersionMappingAsync),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {test.Name}: {ex.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"Executed {tests.Length} contract tests; failures: {failures.Count}");
return failures.Count == 0 ? 0 : 1;

static Task GeometryValidationAsync()
{
    var geometry = CreateGeometry(-8.5);
    Equal(0, geometry.Validate().Count, "Valid geometry must not produce errors.");
    return Task.CompletedTask;
}

static Task BuildVersionAsync()
{
    var parts = ApplicationBuildInfo.Version.Split("-dev.");
    Equal(2, parts.Length, "Development version must include its revision.");
    var expectedNumeric = parts[0] + "." + parts[1];
    Equal(expectedNumeric, ApplicationBuildInfo.NumericVersion, "Numeric revision must match the product revision.");
    foreach (var assembly in new[] { typeof(ApplicationBuildInfo).Assembly, typeof(ProjectManifest).Assembly,
        typeof(ApplicationPaths).Assembly, Assembly.GetExecutingAssembly() })
    {
        Equal(expectedNumeric, assembly.GetName().Version!.ToString(4), $"Assembly version: {assembly.GetName().Name}");
        Equal(expectedNumeric, assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version, "File revision must match.");
        Equal(ApplicationBuildInfo.Version, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0], "Product revision must match.");
    }
    Equal(ApplicationBuildInfo.Version, new ProjectManifest().ApplicationVersion, "Saved application version must track the build.");
    Equal("1.1", ProjectManifest.CurrentVersion, "Application revisions do not automatically change the data format.");
    Equal("1.0.0-dev.123", new ProjectManifest().MinimumApplicationVersion, "The minimum reader stays at the first compatible build.");
    return Task.CompletedTask;
}

static Task TextEditAsync()
{
    var pageId = Guid.NewGuid();
    var geometry = CreateGeometry(0);
    var region = new OcrTextRegion
    {
        PageId = pageId,
        OriginalText = "第11章",
        OriginalGeometry = geometry,
        EditedGeometry = geometry,
        WritingMode = WritingMode.Vertical,
        FlowDirection = TextFlowDirection.TopToBottom,
    };
    var edited = region.EditText("第17章");
    Equal("第11章", edited.OriginalText, "The source OCR text must be immutable.");
    Equal("第17章", edited.EffectiveText, "The edited text must be effective.");
    Equal(ReviewStatus.Modified, edited.ReviewStatus, "Editing must update review state.");
    return Task.CompletedTask;
}

static Task EmptyTextCompatibilityAsync()
{
    var geometry = CreateGeometry(0);
    var region = new OcrTextRegion { OriginalText = "ABC", OriginalGeometry = geometry, EditedGeometry = geometry };
    Equal("ABC", region.EffectiveText, "Legacy empty edit means unchanged.");
    Equal("XYZ", (region with { EditedText = "XYZ" }).EffectiveText, "Legacy nonempty edits remain readable.");
    var edited = region.EditText("");
    True(edited.HasEditedText && edited.IsModified, "An explicit empty edit is a modification.");
    var restored = JsonSerializer.Deserialize<OcrTextRegion>(JsonSerializer.Serialize(edited))!;
    Equal("", restored.EffectiveText, "An empty edit must survive JSON round-trip.");
    Equal("ABC", restored.OriginalText, "Original text remains intact.");
    return Task.CompletedTask;
}

static async Task PackageVersionGateAsync()
{
    var directory = CreateTempDirectory();
    try
    {
        var pdf = Path.Combine(directory, "source.pdf");
        await File.WriteAllTextAsync(pdf, "%PDF-1.4\n%%EOF");
        var packages = new ProjectPackageService();
        var path = Path.Combine(directory, "future.pdfocrproj");
        await packages.SaveAsync(path, new PdfCorrectoriumProject { SourcePdf = await packages.CreateSourceReferenceAsync(pdf) });
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("manifest.json")!;
            JsonObject manifest;
            using (var input = entry.Open()) manifest = (JsonObject)(await JsonNode.ParseAsync(input))!;
            Equal("1.1", manifest["formatVersion"]!.GetValue<string>(), "New containers require an empty-edit aware reader.");
            Equal(ApplicationBuildInfo.Version, manifest["applicationVersion"]!.GetValue<string>(), "Manifest follows the build version.");
            manifest["formatVersion"] = "99.0";
            entry.Delete();
            using var output = zip.CreateEntry("manifest.json").Open();
            await JsonSerializer.SerializeAsync(output, manifest);
        }
        True(!(await packages.ValidateAsync(path)).IsValid, "Unsupported future formats fail validation.");
        try { await packages.OpenAsync(path); throw new Exception("Future project was accepted."); }
        catch (InvalidDataException) { }
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static Task ViewerSettingsMappingAsync()
{
    var rightToLeftWithCover = new ViewerSettings
    {
        BindingDirection = BindingDirection.RightToLeft,
        PageMode = InitialPageMode.FacingPages,
        ShowCoverSeparately = true,
    };
    Equal("/TwoPageLeft", PdfViewerSettingsMapping.GetPageLayoutName(rightToLeftWithCover),
        "A right-bound, left-opening document with a separate cover must place odd page 1 on the left in Acrobat.");
    Equal("/R2L", PdfViewerSettingsMapping.GetDirectionName(rightToLeftWithCover),
        "A right-bound, left-opening document must retain right-to-left reading direction.");

    var leftToRightWithCover = rightToLeftWithCover with
    {
        BindingDirection = BindingDirection.LeftToRight,
    };
    Equal("/TwoPageRight", PdfViewerSettingsMapping.GetPageLayoutName(leftToRightWithCover),
        "A left-bound, right-opening document with a separate cover must place odd page 1 on the right in Acrobat.");
    Equal("/L2R", PdfViewerSettingsMapping.GetDirectionName(leftToRightWithCover),
        "A left-bound, right-opening document must retain left-to-right reading direction.");

    var rightToLeftWithoutCover = rightToLeftWithCover with { ShowCoverSeparately = false };
    Equal("/TwoPageRight", PdfViewerSettingsMapping.GetPageLayoutName(rightToLeftWithoutCover),
        "Disabling the separate cover must invert the first facing-page slot for right-to-left documents.");

    var leftToRightWithoutCover = leftToRightWithCover with { ShowCoverSeparately = false };
    Equal("/TwoPageLeft", PdfViewerSettingsMapping.GetPageLayoutName(leftToRightWithoutCover),
        "Disabling the separate cover must invert the first facing-page slot for left-to-right documents.");

    return Task.CompletedTask;
}

static Task OutputVersionMappingAsync()
{
    Equal(null, PdfOutputVersionMapping.GetVersionString(PdfOutputVersion.Automatic),
        "Automatic output must not force a PDF version.");
    Equal("1.4", PdfOutputVersionMapping.GetVersionString(PdfOutputVersion.Pdf14),
        "PDF 1.4 must map to the qpdf version string.");
    Equal("2.0", PdfOutputVersionMapping.GetVersionString(PdfOutputVersion.Pdf20),
        "PDF 2.0 must map to the qpdf version string.");
    True(PdfOutputVersionMapping.IsLowerThanSource(PdfOutputVersion.Pdf14, "1.5"),
        "PDF 1.4 must be rejected for a PDF 1.5 source.");
    True(!PdfOutputVersionMapping.IsLowerThanSource(PdfOutputVersion.Pdf15, "1.5"),
        "The same PDF version must remain selectable.");
    return Task.CompletedTask;
}

static Task CharacterCountAnomalyAsync()
{
    var samples = new List<OcrQualitySample>();
    foreach (var text in new[] { "abcdefghij", "1234567890", "ABCDEFGHIJ", "あいうえおかきくけこ" })
        samples.Add(CreateQualitySample(text, 100, 20));
    var outlier = CreateQualitySample("ab", 100, 20);
    samples.Add(outlier);

    var results = new OcrQualityAnalyzer().FindCharacterCountAnomalies(
        samples,
        new OcrCharacterCountAnalysisOptions(SizeTolerancePercent: 5, MinimumPeerCount: 3, CountRatioThreshold: 1.5));

    True(results.Any(result => result.RegionId == outlier.RegionId && result.Kind == OcrCharacterCountAnomalyKind.TooFew),
        "A same-sized region with far fewer characters must be reported.");
    return Task.CompletedTask;
}

static Task KeywordWidthAnomalyAsync()
{
    var samples = new List<OcrQualitySample>
    {
        CreateQualitySample("Node.js", 70, 20, Enumerable.Repeat(10d, 7).ToArray()),
        CreateQualitySample("Node.js", 70, 20, Enumerable.Repeat(10d, 7).ToArray()),
        CreateQualitySample("Node.js", 70, 20, Enumerable.Repeat(10d, 7).ToArray()),
    };
    var outlier = CreateQualitySample("Node.js", 112, 20, Enumerable.Repeat(16d, 7).ToArray());
    samples.Add(outlier);

    var result = new OcrQualityAnalyzer().AnalyzeKeywordWidths(
        samples,
        new OcrKeywordWidthAnalysisOptions("Node.js", DeviationTolerancePercent: 20, MinimumReferenceCount: 3));

    Equal(4, result.OccurrenceCount, "All keyword occurrences must participate in the reference calculation.");
    True(result.Candidates.Any(candidate => candidate.RegionId == outlier.RegionId),
        "An occurrence with an abnormal normalized width must be reported.");

    var mixedDirections = new List<OcrQualitySample>
    {
        CreateQualitySample("縦書", 20, 80, [40d, 40d], isVertical: true),
        CreateQualitySample("縦書", 20, 80, [40d, 40d], isVertical: true),
        CreateQualitySample("横書", 80, 20, [40d, 40d]),
        CreateQualitySample("横書", 80, 20, [40d, 40d]),
    };
    var mixedResult = new OcrQualityAnalyzer().AnalyzeKeywordWidths(
        mixedDirections,
        new OcrKeywordWidthAnalysisOptions("書", DeviationTolerancePercent: 10, MinimumReferenceCount: 2));
    Equal(0, mixedResult.Candidates.Count,
        "Horizontal and vertical keyword widths must use independent references.");
    return Task.CompletedTask;
}

static OcrQualitySample CreateQualitySample(
    string text,
    double width,
    double height,
    IReadOnlyList<double>? advances = null,
    bool isVertical = false) =>
    new(
        1,
        Guid.NewGuid(),
        text,
        width,
        height,
        IsVertical: isVertical,
        IsGeometryLocked: false,
        HasLockedCharacters: false,
        advances ?? Enumerable.Repeat(width / Math.Max(1, text.Length), text.Length).ToArray());

static async Task ProjectRoundTripAsync()
{
    var directory = CreateTempDirectory();
    try
    {
        var pdf = Path.Combine(directory, "book.pdf");
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4\n%%EOF"u8.ToArray());
        var package = new ProjectPackageService();
        var source = await package.CreateSourceReferenceAsync(pdf, directory);
        var project = new PdfCorrectoriumProject
        {
            Name = "book",
            SourcePdf = source,
            OutputPdfVersion = PdfOutputVersion.Pdf15,
            DocumentLanguage = "ja-JP",
            DocumentMetadata = new PdfDocumentMetadata
            {
                Title = "校正対象文書",
                Author = "校正 太郎",
                Subject = "文書情報の保存テスト",
                Keywords = "PDF, 校正",
                Creator = "PDF Correctorium",
                Producer = "PDF Correctorium Exporter",
            },
            Pages =
            [
                new OcrPage
                {
                    PageNumber = 1,
                    WidthPoints = 595,
                    HeightPoints = 842,
                    ImageOptimization = new PageImageOptimization
                    {
                        KeepRegions =
                        [
                            new ImageOptimizationKeepRegion
                            {
                                LeftRatio = 0.1,
                                TopRatio = 0.2,
                                WidthRatio = 0.3,
                                HeightRatio = 0.4,
                            },
                        ],
                    },
                },
            ],
            BookmarksInitialized = true,
            BookmarksModified = true,
            Bookmarks =
            [
                new PdfBookmark
                {
                    Title = "第1章",
                    PageNumber = 1,
                    Children =
                    [
                        new PdfBookmark { Title = "第1節", PageNumber = 1 },
                    ],
                },
            ],
        };
        var path = Path.Combine(directory, "book.pdfocrproj");
        await package.SaveAsync(path, project);
        var validation = await package.ValidateAsync(path);
        True(validation.IsValid, "A newly saved project must validate.");
        var reopened = await package.OpenAsync(path);
        Equal(project.ProjectId, reopened.ProjectId, "Project ID must round-trip.");
        Equal(source.Sha256, reopened.SourcePdf.Sha256, "Source hash must round-trip.");
        Equal(PdfOutputVersion.Pdf15, reopened.OutputPdfVersion, "Output PDF version must round-trip.");
        Equal("ja-JP", reopened.DocumentLanguage, "Document language must round-trip.");
        Equal("校正対象文書", reopened.DocumentMetadata?.Title, "Document title must round-trip.");
        Equal("校正 太郎", reopened.DocumentMetadata?.Author, "Document author must round-trip.");
        Equal("文書情報の保存テスト", reopened.DocumentMetadata?.Subject, "Document subject must round-trip.");
        Equal("PDF, 校正", reopened.DocumentMetadata?.Keywords, "Document keywords must round-trip.");
        Equal("PDF Correctorium", reopened.DocumentMetadata?.Creator, "Document creator must round-trip.");
        Equal("PDF Correctorium Exporter", reopened.DocumentMetadata?.Producer, "Document producer must round-trip.");
        True(reopened.BookmarksInitialized, "Bookmark initialization state must round-trip.");
        True(reopened.BookmarksModified, "Bookmark modification state must round-trip.");
        Equal("第1章", reopened.Bookmarks.Single().Title, "Bookmark title must round-trip.");
        Equal("第1節", reopened.Bookmarks.Single().Children.Single().Title, "Bookmark hierarchy must round-trip.");
        var keepRegion = reopened.Pages.Single().ImageOptimization!.KeepRegions.Single();
        Equal(0.1, keepRegion.LeftRatio, "Image optimization keep-region X must round-trip.");
        Equal(0.4, keepRegion.HeightRatio, "Image optimization keep-region height must round-trip.");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task ProjectAutoSaveRecoveryAsync()
{
    var directory = CreateTempDirectory();
    try
    {
        var pdf = Path.Combine(directory, "recovery-source.pdf");
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4\n%%EOF"u8.ToArray());
        var package = new ProjectPackageService { BackupGenerationCount = 3 };
        var project = new PdfCorrectoriumProject
        {
            Name = "recovery-test",
            SourcePdf = await package.CreateSourceReferenceAsync(pdf, directory),
            Pages = [new OcrPage { PageNumber = 1, WidthPoints = 595, HeightPoints = 842 }],
        };
        var projectPath = Path.Combine(directory, "recovery-test.pdfocrproj");
        var autoSavePath = ProjectPackageService.GetAutoSavePath(projectPath);

        await package.SaveAsync(projectPath, project);
        await package.SaveAutoSaveAsync(autoSavePath, project with { Name = "autosaved-name" }, false,
            new Dictionary<int, byte[]>());
        await File.WriteAllTextAsync(projectPath, "damaged project package");

        var restoredFrom = await package.RestoreLatestValidBackupAsync(projectPath);
        Equal(autoSavePath, restoredFrom, "The newest valid autosave must be used for recovery.");
        var validation = await package.ValidateAsync(projectPath);
        True(validation.IsValid, "The restored project must pass package validation.");
        var restored = await package.OpenAsync(projectPath);
        Equal("autosaved-name", restored.Name, "Autosaved editing state must be restored.");
        True(Directory.EnumerateFiles(directory, "recovery-test.pdfocrproj.pre-recovery-*").Any(),
            "Recovery must preserve the damaged package for diagnostics.");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task MissingManifestAsync()
{
    var directory = CreateTempDirectory();
    try
    {
        var path = Path.Combine(directory, "broken.pdfocrproj");
        await using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            archive.CreateEntry("project.json");
        var validation = await new ProjectPackageService().ValidateAsync(path);
        True(!validation.IsValid, "A package without a manifest must fail validation.");
        True(validation.Issues.Any(x => x.Code == "manifest.missing"), "The diagnostic must identify the missing manifest.");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task ProjectThumbnailCacheAsync()
{
    var directory = CreateTempDirectory();
    try
    {
        var pdf = Path.Combine(directory, "thumbnail-source.pdf");
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4\n%%EOF"u8.ToArray());
        var package = new ProjectPackageService();
        var project = new PdfCorrectoriumProject
        {
            Name = "thumbnail-cache",
            SourcePdf = await package.CreateSourceReferenceAsync(pdf, directory),
            Pages = [new OcrPage { PageNumber = 1, WidthPoints = 595, HeightPoints = 842 }],
        };
        var expected = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var path = Path.Combine(directory, "thumbnail-cache.pdfocrproj");

        await package.SaveAsync(path, project, false, new Dictionary<int, byte[]> { [1] = expected });
        var reopened = await package.ReadThumbnailCacheAsync(path);

        True(reopened.TryGetValue(1, out var actual), "The saved thumbnail must be present in the package.");
        True(expected.SequenceEqual(actual!), "The thumbnail bytes must round-trip unchanged.");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task LegacyProjectFormatAsync()
{
    var directory = CreateTempDirectory();
    try
    {
        var pdf = Path.Combine(directory, "legacy.pdf");
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4\n%%EOF"u8.ToArray());

        var package = new ProjectPackageService();
        var source = await package.CreateSourceReferenceAsync(pdf, directory);
        var project = new PdfCorrectoriumProject
        {
            Name = "legacy",
            SourcePdf = source,
            Pages = [new OcrPage { PageNumber = 1, WidthPoints = 595, HeightPoints = 842 }],
        };
        var path = Path.Combine(directory, "legacy.pdfocrproj");
        await package.SaveAsync(path, project);

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")
                ?? throw new InvalidDataException("The generated project did not contain manifest.json.");
            JsonObject manifest;
            await using (var input = entry.Open())
                manifest = await JsonNode.ParseAsync(input) as JsonObject
                    ?? throw new InvalidDataException("The generated manifest could not be read.");

            entry.Delete();
            manifest["format"] = ProjectManifest.LegacyFormat;
            manifest["formatVersion"] = "1.0";
            var legacyEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var output = legacyEntry.Open();
            await JsonSerializer.SerializeAsync(output, manifest);
        }

        var validation = await package.ValidateAsync(path);
        True(validation.IsValid, "A project saved with the former application identifier must remain valid.");
        var reopened = await package.OpenAsync(path);
        Equal(project.ProjectId, reopened.ProjectId, "A legacy project must open without changing its identity.");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task SourceFingerprintAsync()
{
    var directory = CreateTempDirectory();
    try
    {
        var pdf = Path.Combine(directory, "source.pdf");
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4\n%%EOF"u8.ToArray());
        var package = new ProjectPackageService();
        var source = await package.CreateSourceReferenceAsync(pdf, directory);
        True(await package.VerifySourceAsync(source, directory), "The unchanged source must match.");
        await File.AppendAllTextAsync(pdf, "changed");
        True(!await package.VerifySourceAsync(source, directory), "A changed source must not match.");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task PortablePathsAsync()
{
    var directory = CreateTempDirectory();
    try
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "portable.marker"), string.Empty);
        var paths = ApplicationPathResolver.Resolve(directory);
        Equal(StorageMode.Portable, paths.Mode, "portable.marker must enable portable storage.");
        True(paths.ConfigurationDirectory.StartsWith(directory, StringComparison.OrdinalIgnoreCase), "Portable data must remain under the app directory.");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static TextGeometry CreateGeometry(double rotation) => new()
{
    LocalBounds = new(new(100, 200), new(220, 24)),
    RotationCenter = new(210, 212),
    RotationDegrees = rotation,
};

static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "PdfCorrectorium.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}");
}
