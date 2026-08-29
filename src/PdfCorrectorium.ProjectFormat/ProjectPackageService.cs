using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.ProjectFormat;

/// <summary>
/// ZIP互換の<c>.pdfocrproj</c>パッケージを作成、読込、検証します。
/// </summary>
/// <remarks>
/// 保存時は一時ファイルを完全に生成・検証してから目的ファイルへ置き換えます。
/// これにより、保存中の異常終了で既存プロジェクトを破損させないようにしています。
/// </remarks>
public sealed class ProjectPackageService
{
    /// <summary>プロジェクト保存ダイアログと関連付けに使用する標準拡張子です。</summary>
    public const string ProjectExtension = ".pdfocrproj";
    /// <summary>通常保存時に保持する世代バックアップ数です。</summary>
    public int BackupGenerationCount { get; set; } = 5;
    /// <summary>列挙値を可読な文字列で保存する共通JSON設定です。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>元PDFのパス、サイズおよびSHA-256からプロジェクト用参照を作成します。</summary>
    /// <param name="pdfPath">参照するPDFのパス。</param>
    /// <param name="projectDirectory">相対パスの基準。未指定時はファイル名だけを保存します。</param>
    public async Task<SourcePdfReference> CreateSourceReferenceAsync(string pdfPath, string? projectDirectory = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        var file = new FileInfo(pdfPath);
        if (!file.Exists) throw new FileNotFoundException("The source PDF was not found.", pdfPath);

        await using var stream = file.OpenRead();
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new SourcePdfReference
        {
            FileName = file.Name,
            RelativePath = projectDirectory is null ? file.Name : Path.GetRelativePath(projectDirectory, file.FullName),
            AbsolutePathHint = file.FullName,
            Sha256 = Convert.ToHexString(hash).ToLowerInvariant(),
            FileSize = file.Length,
        };
    }

    /// <summary>
    /// プロジェクトを一時ZIPへ書き込み、構造検証に合格した場合だけ保存先へ確定します。
    /// </summary>
    /// <param name="destinationPath"><c>.pdfocrproj</c>保存先。</param>
    /// <param name="project">保存する編集モデル。</param>
    /// <param name="embedSourcePdf">元PDFをパッケージ内へ内包する場合は<c>true</c>。</param>
    public async Task SaveAsync(string destinationPath, PdfCorrectoriumProject project, bool embedSourcePdf = false, CancellationToken cancellationToken = default)
        => await SaveCoreAsync(destinationPath, project, embedSourcePdf, thumbnailCache: null, createBackups: true, cancellationToken);

    /// <summary>
    /// ページサムネイルをプロジェクト内へキャッシュしながら保存します。
    /// </summary>
    /// <param name="destinationPath"><c>.pdfocrproj</c>保存先。</param>
    /// <param name="project">保存する編集モデル。</param>
    /// <param name="embedSourcePdf">元PDFをパッケージ内へ内包する場合は<c>true</c>。</param>
    /// <param name="thumbnailCache">ページ番号をキーとする圧縮JPEGデータ。</param>
    public async Task SaveAsync(
        string destinationPath,
        PdfCorrectoriumProject project,
        bool embedSourcePdf,
        IReadOnlyDictionary<int, byte[]> thumbnailCache,
        CancellationToken cancellationToken = default)
        => await SaveCoreAsync(destinationPath, project, embedSourcePdf, thumbnailCache, createBackups: true, cancellationToken);

    /// <summary>通常保存とは別の復旧用ファイルへ、世代バックアップを作らず保存します。</summary>
    public async Task SaveAutoSaveAsync(
        string destinationPath,
        PdfCorrectoriumProject project,
        bool embedSourcePdf,
        IReadOnlyDictionary<int, byte[]> thumbnailCache,
        CancellationToken cancellationToken = default) =>
        await SaveCoreAsync(destinationPath, project, embedSourcePdf, thumbnailCache, createBackups: false, cancellationToken);

    /// <summary>プロジェクトに対応する自動保存ファイルのパスを返します。</summary>
    public static string GetAutoSavePath(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var name = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, $"{name}.autosave{ProjectExtension}");
    }

    /// <summary>正常保存後に不要となった自動保存ファイルを削除します。</summary>
    public static void DeleteAutoSave(string projectPath)
    {
        var path = GetAutoSavePath(projectPath);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // 通常保存そのものは完了しているため、使用中の復旧ファイルを消せないだけで
            // 保存失敗にはしません。次回の保存または起動時に改めて整理できます。
        }
        catch (UnauthorizedAccessException)
        {
            // 読み取り専用媒体などでも、完成済みプロジェクトを保存失敗扱いにしません。
        }
    }

    private async Task SaveCoreAsync(
        string destinationPath,
        PdfCorrectoriumProject project,
        bool embedSourcePdf,
        IReadOnlyDictionary<int, byte[]>? thumbnailCache,
        bool createBackups,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(project);
        if (!destinationPath.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Project files must use {ProjectExtension}.", nameof(destinationPath));

        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tempPath = fullPath + ".tmp";
        var backupPath = fullPath + ".bak";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                var now = DateTimeOffset.UtcNow;
                var manifest = new ProjectManifest
                {
                    ProjectId = project.ProjectId,
                    CreatedAtUtc = project.CreatedAtUtc,
                    LastSavedAtUtc = now,
                };
                var sourceForSave = project.SourcePdf with { IsEmbedded = embedSourcePdf };
                var projectForSave = project with { LastSavedAtUtc = now, SourcePdf = sourceForSave };
                await WriteJsonAsync(archive, "manifest.json", manifest, cancellationToken);
                await WriteJsonAsync(archive, "project.json", projectForSave, cancellationToken);
                await WriteJsonAsync(archive, "source/source-reference.json", sourceForSave, cancellationToken);

                if (embedSourcePdf)
                {
                    var sourcePath = ResolveExternalSourcePath(project.SourcePdf, Path.GetDirectoryName(fullPath)!);
                    var entry = archive.CreateEntry("source/document.pdf", CompressionLevel.Optimal);
                    await using var output = entry.Open();
                    await using var input = File.OpenRead(sourcePath);
                    await input.CopyToAsync(output, cancellationToken);
                }

                foreach (var page in project.Pages.OrderBy(x => x.PageNumber))
                    await WriteJsonAsync(archive, $"ocr/pages/page-{page.PageNumber:000000}.json", page, cancellationToken);

                if (thumbnailCache is not null)
                {
                    foreach (var thumbnail in thumbnailCache
                                 .Where(x => x.Key > 0 && x.Value.Length > 0)
                                 .OrderBy(x => x.Key))
                    {
                        var entry = archive.CreateEntry($"thumbnails/page-{thumbnail.Key:000000}.jpg", CompressionLevel.NoCompression);
                        await using var output = entry.Open();
                        await output.WriteAsync(thumbnail.Value, cancellationToken);
                    }
                }
            }

            var validation = await ValidateAsync(tempPath, cancellationToken);
            if (!validation.IsValid)
                throw new InvalidDataException(string.Join(Environment.NewLine, validation.Issues.Select(x => $"{x.Code}: {x.Message}")));

            if (File.Exists(fullPath))
            {
                if (createBackups)
                {
                    File.Copy(fullPath, backupPath, overwrite: true);
                    CreateVersionedBackup(fullPath);
                }
                File.Move(tempPath, fullPath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// 自動保存または世代バックアップから、最初に検証へ合格したファイルを復元します。
    /// </summary>
    /// <returns>復元元のファイルパス。利用可能な候補がない場合は<c>null</c>。</returns>
    public async Task<string?> RestoreLatestValidBackupAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var candidates = new List<string> { GetAutoSavePath(fullPath), fullPath + ".bak" };
        candidates.AddRange(Directory.EnumerateFiles(directory, $"{stem}.backup-*{ProjectExtension}")
            .OrderByDescending(File.GetLastWriteTimeUtc));

        foreach (var candidate in candidates.Where(File.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = await ValidateAsync(candidate, cancellationToken);
            if (!validation.IsValid) continue;

            var recoveryCopy = fullPath + $".pre-recovery-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}";
            if (File.Exists(fullPath)) File.Copy(fullPath, recoveryCopy, overwrite: false);
            var temporaryPath = fullPath + ".restore.tmp";
            File.Copy(candidate, temporaryPath, overwrite: true);
            var restoredValidation = await ValidateAsync(temporaryPath, cancellationToken);
            if (!restoredValidation.IsValid)
            {
                File.Delete(temporaryPath);
                continue;
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
            return candidate;
        }

        return null;
    }

    private void CreateVersionedBackup(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var backup = Path.Combine(directory, $"{stem}.backup-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}{ProjectExtension}");
        File.Copy(fullPath, backup, overwrite: false);

        var keep = Math.Clamp(BackupGenerationCount, 1, 20);
        foreach (var oldBackup in Directory.EnumerateFiles(directory, $"{stem}.backup-*{ProjectExtension}")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(keep))
            File.Delete(oldBackup);
    }

    /// <summary>
    /// プロジェクトに保存された圧縮サムネイルを読み込みます。
    /// </summary>
    /// <remarks>壊れた個別キャッシュは無視し、PDF本体からの再生成へフォールバックできるようにします。</remarks>
    public async Task<IReadOnlyDictionary<int, byte[]>> ReadThumbnailCacheAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<int, byte[]>();
        await using var file = File.OpenRead(projectPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries.Where(x =>
                     x.FullName.StartsWith("thumbnails/page-", StringComparison.OrdinalIgnoreCase) &&
                     x.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(entry.Name);
            if (!int.TryParse(fileName.AsSpan("page-".Length), out var pageNumber) || pageNumber <= 0)
                continue;
            // A cached thumbnail must stay small enough that a damaged project cannot exhaust memory.
            if (entry.Length <= 0 || entry.Length > 4 * 1024 * 1024)
                continue;
            await using var input = entry.Open();
            using var buffer = new MemoryStream((int)entry.Length);
            await input.CopyToAsync(buffer, cancellationToken);
            result[pageNumber] = buffer.ToArray();
        }

        return result;
    }

    /// <summary>形式識別子とバージョンを確認してプロジェクトモデルを読み込みます。</summary>
    public async Task<PdfCorrectoriumProject> OpenAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(projectPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var manifest = await ReadJsonAsync<ProjectManifest>(archive, "manifest.json", cancellationToken);
        if (!ProjectManifest.IsSupportedFormat(manifest.Format))
            throw new InvalidDataException($"Unsupported project format: {manifest.Format}");
        if (manifest.FormatVersion != ProjectManifest.CurrentVersion)
            throw new InvalidDataException($"Unsupported project version: {manifest.FormatVersion}");
        return await ReadJsonAsync<PdfCorrectoriumProject>(archive, "project.json", cancellationToken);
    }

    /// <summary>外部参照PDFのサイズとSHA-256がプロジェクト記録と一致するか確認します。</summary>
    public async Task<bool> VerifySourceAsync(SourcePdfReference source, string projectDirectory, CancellationToken cancellationToken = default)
    {
        if (source.IsEmbedded) return true;
        string sourcePath;
        try { sourcePath = ResolveExternalSourcePath(source, projectDirectory); }
        catch (FileNotFoundException) { return false; }
        var info = new FileInfo(sourcePath);
        if (info.Length != source.FileSize) return false;
        await using var stream = info.OpenRead();
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), source.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>相対パスを優先し、見つからない場合は前回の絶対パスから元PDFを解決します。</summary>
    /// <exception cref="InvalidOperationException">元PDFがプロジェクトへ内包されている場合。</exception>
    public string ResolveSourcePath(SourcePdfReference source, string projectDirectory)
    {
        if (source.IsEmbedded)
            throw new InvalidOperationException("An embedded source PDF must be materialized before it can be opened.");
        return ResolveExternalSourcePath(source, projectDirectory);
    }

    /// <summary>
    /// 内包された元PDFをハッシュ名の作業ファイルとして展開し、内容を検証して返します。
    /// </summary>
    /// <remarks>同じハッシュの展開済みファイルが利用可能な場合は再利用します。</remarks>
    public async Task<string> MaterializeEmbeddedSourceAsync(
        string projectPath,
        SourcePdfReference source,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!source.IsEmbedded) return ResolveExternalSourcePath(source, Path.GetDirectoryName(projectPath)!);
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, $"{source.Sha256}.pdf");
        if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length == source.FileSize) return destinationPath;

        var temporaryPath = destinationPath + ".tmp";
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        try
        {
            await using (var file = File.OpenRead(projectPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry("source/document.pdf")
                    ?? throw new InvalidDataException("The embedded source PDF is missing from the project.");
                await using var input = entry.Open();
                await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, cancellationToken);
            }

            await using (var stream = File.OpenRead(temporaryPath))
            {
                var hash = await SHA256.HashDataAsync(stream, cancellationToken);
                if (!string.Equals(Convert.ToHexString(hash), source.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The embedded source PDF fingerprint does not match the project.");
            }
            File.Move(temporaryPath, destinationPath, true);
            return destinationPath;
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// ZIP構造、必須JSON、プロジェクトID、元PDF参照、ページ番号の整合性を検証します。
    /// </summary>
    public async Task<ProjectValidationResult> ValidateAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var issues = new List<ProjectValidationIssue>();
        try
        {
            await using var file = File.OpenRead(projectPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            if (archive.GetEntry("manifest.json") is null) issues.Add(new("manifest.missing", "manifest.json is missing.", true));
            if (archive.GetEntry("project.json") is null) issues.Add(new("project.missing", "project.json is missing.", true));
            if (archive.GetEntry("source/source-reference.json") is null) issues.Add(new("sourceReference.missing", "The source reference is missing.", true));
            if (issues.Count == 0)
            {
                var manifest = await ReadJsonAsync<ProjectManifest>(archive, "manifest.json", cancellationToken);
                var project = await ReadJsonAsync<PdfCorrectoriumProject>(archive, "project.json", cancellationToken);
                var source = await ReadJsonAsync<SourcePdfReference>(archive, "source/source-reference.json", cancellationToken);
                if (manifest.ProjectId != project.ProjectId) issues.Add(new("projectId.mismatch", "Manifest and project IDs differ.", true));
                if (source != project.SourcePdf) issues.Add(new("sourceReference.mismatch", "The project and source reference entries differ.", true));
                if (source.IsEmbedded && archive.GetEntry("source/document.pdf") is null)
                    issues.Add(new("sourcePdf.missing", "The project declares an embedded PDF, but source/document.pdf is missing.", true));
                if (!ProjectManifest.IsSupportedFormat(manifest.Format)) issues.Add(new("format.unsupported", manifest.Format, true));
                if (project.Pages.Select(x => x.PageNumber).Distinct().Count() != project.Pages.Count)
                    issues.Add(new("pages.duplicate", "Duplicate page numbers were found.", true));
            }
        }
        catch (InvalidDataException ex) { issues.Add(new("zip.invalid", ex.Message, true)); }
        catch (JsonException ex) { issues.Add(new("json.invalid", ex.Message, true)); }
        catch (IOException ex) { issues.Add(new("io.error", ex.Message, true)); }
        return new(issues);
    }

    private static async Task WriteJsonAsync<T>(ZipArchive archive, string name, T value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(ZipArchive archive, string name, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing entry: {name}");
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Empty JSON entry: {name}");
    }

    private static string ResolveExternalSourcePath(SourcePdfReference source, string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(source.RelativePath))
        {
            var relative = Path.GetFullPath(Path.Combine(projectDirectory, source.RelativePath));
            if (File.Exists(relative)) return relative;
        }
        if (!string.IsNullOrWhiteSpace(source.AbsolutePathHint) && File.Exists(source.AbsolutePathHint)) return source.AbsolutePathHint;
        throw new FileNotFoundException("The source PDF could not be resolved.", source.FileName);
    }
}
