using System.IO.Compression;
using System.Globalization;
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
    /// <summary>世代バックアップ全体の既定保持上限です。最新1世代は上限を超えても残します。</summary>
    public long BackupByteLimit { get; set; } = 4L * 1024 * 1024 * 1024;
    /// <summary>展開済み元PDFキャッシュのファイル数上限です。現在要求中のPDFは必ず残します。</summary>
    public int MaterializedSourceCacheFileLimit { get; set; } = 16;
    /// <summary>展開済み元PDFキャッシュの合計容量上限です。現在要求中のPDFは必ず残します。</summary>
    public long MaterializedSourceCacheByteLimit { get; set; } = 4L * 1024 * 1024 * 1024;
    /// <summary>旧形式プロジェクトからメモリへ復元するサムネイル数の上限です。</summary>
    public int LegacyThumbnailReadLimit { get; set; } = 64;
    /// <summary>信頼できないプロジェクトの展開量を制限する読込ポリシーです。</summary>
    public ProjectPackageLimits Limits { get; init; } = new();
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
    /// 旧呼び出し元との互換性を保ちながらプロジェクトを保存します。
    /// </summary>
    /// <param name="destinationPath"><c>.pdfocrproj</c>保存先。</param>
    /// <param name="project">保存する編集モデル。</param>
    /// <param name="embedSourcePdf">元PDFをパッケージ内へ内包する場合は<c>true</c>。</param>
    /// <param name="thumbnailCache">形式1.4以降では保存しない再生成可能キャッシュ。</param>
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
        // 保存処理ごとに一意な一時名を使い、並行保存や共有フォルダー上の固定名競合を避ける。
        var tempPath = fullPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            // 先に一時ZIPを完成させて検証する。既存ファイルを途中状態で開かせないため、
            // 検証前は目的のパスを変更しない。
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
                var storageMode = embedSourcePdf ? ProjectPdfStorageMode.Embedded : ProjectPdfStorageMode.Relative;
                var sourceForSave = project.SourcePdf with
                {
                    IsEmbedded = embedSourcePdf,
                    RelativePath = embedSourcePdf ? null : project.SourcePdf.RelativePath,
                    AbsolutePathHint = null,
                };
                var sourcePageCount = project.SourcePdf.PageCount ?? project.Pages.Select(page => page.PageNumber).DefaultIfEmpty(0).Max();
                var projectForSave = project with
                {
                    LastSavedAtUtc = now,
                    SourcePdf = sourceForSave,
                    PdfStorageMode = storageMode,
                    PageSequence = ProjectPageSequence.Normalize(project.PageSequence, project.Pages, sourcePageCount),
                };
                await WriteJsonAsync(archive, "manifest.json", manifest, cancellationToken);
                await WriteJsonAsync(archive, "project.json", projectForSave, cancellationToken);

                if (embedSourcePdf)
                {
                    var sourcePath = ResolveExternalSourcePath(project.SourcePdf, Path.GetDirectoryName(fullPath)!);
                    var entry = archive.CreateEntry("source/document.pdf", CompressionLevel.NoCompression);
                    await using var output = entry.Open();
                    await using var input = File.OpenRead(sourcePath);
                    await input.CopyToAsync(output, cancellationToken);
                }

                // OCRページの正本はproject.jsonだけです。ページ別JSONとサムネイルは
                // 再生成可能な重複データだったため、形式1.4から新規保存しません。
            }

            var validation = await ValidateAsync(tempPath, cancellationToken);
            if (!validation.IsValid)
                throw new InvalidDataException(string.Join(Environment.NewLine, validation.Issues.Select(x => $"{x.Code}: {x.Message}")));

            if (File.Exists(fullPath))
            {
                // 通常保存では直前の状態を世代バックアップへ残してから、検証済みZIPを置き換える。
                if (createBackups)
                {
                    // 固定名.bakと世代バックアップへ同じ旧版を二重コピーしない。
                    // 復旧用の新規コピーは世代バックアップへ一本化する。
                    CreateVersionedBackup(fullPath);
                }
                File.Move(tempPath, fullPath, overwrite: true);
                // 旧版が作成した固定名バックアップは、世代バックアップと現行ファイルを
                // 正常に確定した後だけ整理する。読込側は旧.bakを引き続き復旧候補にできる。
                if (createBackups) TryDelete(fullPath + ".bak");
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

            // 復元前の現行ファイルも別名で保持し、復元操作自体が失敗しても戻せるようにする。
            var recoveryCopy = fullPath + $".pre-recovery-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}";
            try
            {
                if (File.Exists(fullPath)) File.Copy(fullPath, recoveryCopy, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 破損ファイルの退避に失敗しても、バックアップからの復元は続行する。
            }
            TrimRecoveryCopies(fullPath, recoveryCopy);
            // 固定名を使わず、同時復元や共有フォルダー上の既存ファイルとの競合を避ける。
            var temporaryPath = fullPath + $".{Guid.NewGuid():N}.restore.tmp";
            try
            {
                File.Copy(candidate, temporaryPath, overwrite: false);
                var restoredValidation = await ValidateAsync(temporaryPath, cancellationToken);
                if (!restoredValidation.IsValid)
                {
                    try { File.Delete(temporaryPath); } catch { }
                    continue;
                }

                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 一時ファイルの残留を防ぐ。File.Moveが成功した場合はファイルが移動済みのため削除不要。
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                throw;
            }
            return candidate;
        }

        return null;
    }

    /// <summary>現在のプロジェクトを世代バックアップへ複製し、保持数を超えた古い世代を削除します。</summary>
    private void CreateVersionedBackup(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var backup = Path.Combine(directory, $"{stem}.backup-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}{ProjectExtension}");
        File.Copy(fullPath, backup, overwrite: false);

        TrimVersionedBackups(fullPath, backup);
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
        EnsureArchiveFileWithinLimits(projectPath);
        await using var file = File.OpenRead(projectPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        EnsureArchiveWithinLimits(archive);
        foreach (var entry in archive.Entries.Where(x =>
                     x.FullName.StartsWith("thumbnails/page-", StringComparison.OrdinalIgnoreCase) &&
                     x.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                 .Take(Math.Max(1, LegacyThumbnailReadLimit)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(entry.Name);
            if (!int.TryParse(fileName.AsSpan("page-".Length), out var pageNumber) || pageNumber <= 0)
                continue;
            // A cached thumbnail must stay small enough that a damaged project cannot exhaust memory.
            if (entry.Length <= 0 || entry.Length > Limits.MaximumThumbnailEntryBytes)
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
        EnsureArchiveFileWithinLimits(projectPath);
        await using var file = File.OpenRead(projectPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        EnsureArchiveWithinLimits(archive);
        var manifest = await ReadJsonAsync<ProjectManifest>(archive, "manifest.json", cancellationToken);
        if (!ProjectManifest.IsSupportedFormat(manifest.Format))
            throw new InvalidDataException($"Unsupported project format: {manifest.Format}");
        if (!ProjectManifest.IsSupportedVersion(manifest.FormatVersion))
            throw new InvalidDataException($"Unsupported project version: {manifest.FormatVersion}");
        var project = await ReadJsonAsync<PdfCorrectoriumProject>(archive, "project.json", cancellationToken);
        EnsureSourceReferenceIsSafe(project.SourcePdf);
        project = NormalizeStorageMode(project, manifest.FormatVersion);
        var sourcePageCount = project.SourcePdf.PageCount ?? project.Pages.Select(page => page.PageNumber).DefaultIfEmpty(0).Max();
        return project with
        {
            PageSequence = ProjectPageSequence.Normalize(project.PageSequence, project.Pages, sourcePageCount),
        };
    }

    /// <summary>外部参照PDFのサイズとSHA-256がプロジェクト記録と一致するか確認します。</summary>
    public async Task<bool> VerifySourceAsync(SourcePdfReference source, string projectDirectory, CancellationToken cancellationToken = default)
    {
        if (source.IsEmbedded) return true;
        EnsureSourceReferenceIsSafe(source);
        string sourcePath;
        try { sourcePath = ResolveExternalSourcePath(source, projectDirectory); }
        catch (FileNotFoundException) { return false; }
        var info = new FileInfo(sourcePath);
        if (info.Length != source.FileSize) return false;
        await using var stream = info.OpenRead();
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), source.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>明示指定されたPDFが、参照情報に記録されたサイズとSHA-256に一致するか確認します。</summary>
    /// <remarks>
    /// 相対パスの解決を行わないため、別プロセスへ元PDFパスを明示的に渡す出力処理で使用します。
    /// パス文字列ではなく内容を照合し、準備した編集情報を別のPDFへ誤適用しません。
    /// </remarks>
    public async Task<bool> VerifySourceFileAsync(
        SourcePdfReference source,
        string pdfPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        EnsureSourceReferenceIsSafe(source);
        return await FileMatchesSourceAsync(Path.GetFullPath(pdfPath), source, cancellationToken);
    }

    /// <summary>
    /// 対象プロジェクト専用の.assetsから、同じフォルダー内の現行・自動保存・バックアップの
    /// いずれからも参照されない内容ハッシュPDFだけを削除します。
    /// </summary>
    public async Task<int> CleanupUnreferencedAssetsAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        var assetsDirectory = Path.Combine(projectDirectory, Path.GetFileNameWithoutExtension(fullProjectPath) + ".assets");
        if (!Directory.Exists(assetsDirectory)) return 0;

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.TopDirectoryOnly)
                     .Where(IsProjectRecoveryCandidate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var project = await OpenAsync(candidate, cancellationToken);
                if (project.SourcePdf.IsEmbedded || string.IsNullOrWhiteSpace(project.SourcePdf.RelativePath)) continue;
                if (!IsSafeRelativeSourcePath(project.SourcePdf.RelativePath, allowParentSegments: true)) continue;
                referenced.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(candidate)!, project.SourcePdf.RelativePath)));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException or UnauthorizedAccessException)
            {
                // 壊れた復旧候補を根拠に削除してはいけないため、その候補だけ無視します。
            }
        }

        var removed = 0;
        foreach (var asset in Directory.EnumerateFiles(assetsDirectory, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            var stem = Path.GetFileNameWithoutExtension(asset);
            if (stem.Length != 64 || stem.Any(character => !Uri.IsHexDigit(character)) || referenced.Contains(Path.GetFullPath(asset)))
                continue;
            try
            {
                File.Delete(asset);
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try
        {
            if (!Directory.EnumerateFileSystemEntries(assetsDirectory).Any()) Directory.Delete(assetsDirectory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return removed;
    }

    private static bool IsProjectRecoveryCandidate(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(ProjectExtension + ".bak", StringComparison.OrdinalIgnoreCase) ||
               name.Contains(".pre-recovery-", StringComparison.OrdinalIgnoreCase);
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
        EnsureSourceReferenceIsSafe(source);
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, $"{source.Sha256.ToLowerInvariant()}.pdf");
        if (await FileMatchesSourceAsync(destinationPath, source, cancellationToken))
        {
            TouchCacheEntry(destinationPath);
            TrimMaterializedSourceCache(destinationDirectory, destinationPath);
            return destinationPath;
        }

        // 直接書き込まず、サイズとハッシュを検証した一時ファイルだけを公開名へ移動する。
        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            EnsureArchiveFileWithinLimits(projectPath);
            await using (var file = File.OpenRead(projectPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                EnsureArchiveWithinLimits(archive);
                var entry = archive.GetEntry("source/document.pdf")
                    ?? throw new InvalidDataException("The embedded source PDF is missing from the project.");
                if (source.FileSize < 0 || source.FileSize > Limits.MaximumEmbeddedPdfBytes || entry.Length != source.FileSize)
                    throw new InvalidDataException("The embedded source PDF size does not match the validated project reference.");
                await using var input = entry.Open();
                await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await CopyWithLimitAsync(input, output, Limits.MaximumEmbeddedPdfBytes, cancellationToken);
            }

            await using (var stream = File.OpenRead(temporaryPath))
            {
                var hash = await SHA256.HashDataAsync(stream, cancellationToken);
                if (!string.Equals(Convert.ToHexString(hash), source.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The embedded source PDF fingerprint does not match the project.");
            }
            File.Move(temporaryPath, destinationPath, true);
            TouchCacheEntry(destinationPath);
            TrimMaterializedSourceCache(destinationDirectory, destinationPath);
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
            // 外部から取得したZIPを読むため、JSONのデシリアライズより先に展開量と圧縮率を検査する。
            EnsureArchiveFileWithinLimits(projectPath);
            await using var file = File.OpenRead(projectPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            EnsureArchiveWithinLimits(archive);
            if (archive.GetEntry("manifest.json") is null) issues.Add(new("manifest.missing", "manifest.json is missing.", true));
            if (archive.GetEntry("project.json") is null) issues.Add(new("project.missing", "project.json is missing.", true));
            if (issues.Count == 0)
            {
                var manifest = await ReadJsonAsync<ProjectManifest>(archive, "manifest.json", cancellationToken);
                var project = await ReadJsonAsync<PdfCorrectoriumProject>(archive, "project.json", cancellationToken);
                var legacySourceEntry = archive.GetEntry("source/source-reference.json");
                if (manifest.FormatVersion is not ("1.4" or ProjectManifest.CurrentVersion) && legacySourceEntry is null)
                    issues.Add(new("sourceReference.missing", "The source reference is missing.", true));
                var source = legacySourceEntry is null
                    ? project.SourcePdf
                    : await ReadJsonAsync<SourcePdfReference>(archive, "source/source-reference.json", cancellationToken);
                if (manifest.ProjectId != project.ProjectId) issues.Add(new("projectId.mismatch", "Manifest and project IDs differ.", true));
                if (legacySourceEntry is not null && source != project.SourcePdf) issues.Add(new("sourceReference.mismatch", "The project and source reference entries differ.", true));
                try { EnsureSourceReferenceIsSafe(source); }
                catch (InvalidDataException ex) { issues.Add(new("sourceReference.invalid", ex.Message, true)); }
                var normalizedProject = NormalizeStorageMode(project, manifest.FormatVersion);
                if (manifest.FormatVersion is "1.2" or "1.3" or "1.4" or ProjectManifest.CurrentVersion)
                {
                    if (normalizedProject.PdfStorageMode == ProjectPdfStorageMode.Embedded && !source.IsEmbedded)
                        issues.Add(new("sourceStorage.mismatch", "The embedded storage mode does not match the source reference.", true));
                    if (normalizedProject.PdfStorageMode == ProjectPdfStorageMode.Relative && source.IsEmbedded)
                        issues.Add(new("sourceStorage.mismatch", "The relative storage mode does not match the source reference.", true));
                    if (normalizedProject.PdfStorageMode == ProjectPdfStorageMode.Relative && string.IsNullOrWhiteSpace(source.RelativePath))
                        issues.Add(new("sourcePath.missing", "A relative project must contain a relative PDF path.", true));
                    else if (normalizedProject.PdfStorageMode == ProjectPdfStorageMode.Relative &&
                             !IsSafeRelativeSourcePath(source.RelativePath!,
                                 allowParentSegments: manifest.FormatVersion is "1.3" or "1.4" or ProjectManifest.CurrentVersion))
                        issues.Add(new("sourcePath.unsafe", manifest.FormatVersion == "1.2"
                            ? "A version 1.2 relative PDF path must stay below the project directory."
                            : "A relative PDF path must be normalized and must not be rooted or drive-qualified.", true));
                    if (!string.IsNullOrWhiteSpace(source.AbsolutePathHint))
                        issues.Add(new("sourcePath.absolute", "Project formats 1.2 and later must not persist an absolute PDF path.", true));
                }
                if (source.IsEmbedded && archive.GetEntry("source/document.pdf") is null)
                    issues.Add(new("sourcePdf.missing", "The project declares an embedded PDF, but source/document.pdf is missing.", true));
                if (source.IsEmbedded && archive.GetEntry("source/document.pdf") is { } embedded && embedded.Length != source.FileSize)
                    issues.Add(new("sourcePdf.sizeMismatch", "The embedded PDF size does not match its source reference.", true));
                if (!ProjectManifest.IsSupportedFormat(manifest.Format)) issues.Add(new("format.unsupported", manifest.Format, true));
                if (!ProjectManifest.IsSupportedVersion(manifest.FormatVersion)) issues.Add(new("version.unsupported", manifest.FormatVersion, true));
                if (project.Pages.Select(x => x.PageNumber).Distinct().Count() != project.Pages.Count)
                    issues.Add(new("pages.duplicate", "Duplicate page numbers were found.", true));
                ValidatePageSequence(project, source, manifest.FormatVersion, issues);
                ValidateProjectExtensions(normalizedProject, issues);
            }
        }
        catch (InvalidDataException ex) { issues.Add(new("zip.invalid", ex.Message, true)); }
        catch (JsonException ex) { issues.Add(new("json.invalid", ex.Message, true)); }
        catch (OverflowException ex) { issues.Add(new("size.invalid", ex.Message, true)); }
        catch (IOException ex) { issues.Add(new("io.error", ex.Message, true)); }
        return new(issues);
    }

    private static async Task WriteJsonAsync<T>(ZipArchive archive, string name, T value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    /// <summary>許可されたサイズ内でZIP内JSONを読み込み、空または過大なエントリを拒否します。</summary>
    private async Task<T> ReadJsonAsync<T>(ZipArchive archive, string name, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing entry: {name}");
        if (entry.Length <= 0 || entry.Length > Limits.MaximumJsonEntryBytes)
            throw new InvalidDataException($"JSON entry exceeds the allowed size: {name}");
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Empty JSON entry: {name}");
    }

    /// <summary>ZIP爆弾、パストラバーサル、重複エントリ、過大な展開量を事前に検査します。</summary>
    private void EnsureArchiveWithinLimits(ZipArchive archive)
    {
        ValidateLimits();
        if (archive.Entries.Count > Limits.MaximumEntryCount)
            throw new InvalidDataException("The project package contains too many entries.");

        long totalBytes = 0;
        long thumbnailBytes = 0;
        var thumbnailCount = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var parts = name.Split('/');
            if (!string.Equals(name, entry.FullName, StringComparison.Ordinal) ||
                name.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(name) ||
                name.Contains(':', StringComparison.Ordinal) ||
                parts.Any(part => part is "" or "." or ".."))
                throw new InvalidDataException($"The project package contains an unsafe entry name: {entry.FullName}");
            if (!names.Add(name))
                throw new InvalidDataException($"The project package contains a duplicate entry: {name}");
            if (entry.Length < 0)
                throw new InvalidDataException($"The project package contains an invalid entry size: {name}");
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > Limits.MaximumTotalUncompressedBytes)
                throw new InvalidDataException("The expanded project package exceeds the allowed total size.");
            if (entry.Length > 1024 * 1024 &&
                (entry.CompressedLength <= 0 || entry.Length / (double)entry.CompressedLength > Limits.MaximumCompressionRatio))
                throw new InvalidDataException($"The project package entry has an unsafe compression ratio: {name}");

            if (name.StartsWith("thumbnails/page-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                thumbnailCount++;
                thumbnailBytes = checked(thumbnailBytes + entry.Length);
                if (thumbnailCount > Limits.MaximumThumbnailCount ||
                    entry.Length > Limits.MaximumThumbnailEntryBytes ||
                    thumbnailBytes > Limits.MaximumTotalThumbnailBytes)
                    throw new InvalidDataException("The project thumbnail cache exceeds the allowed limits.");
            }
            else if (name.Equals("source/document.pdf", StringComparison.OrdinalIgnoreCase) &&
                     entry.Length > Limits.MaximumEmbeddedPdfBytes)
            {
                throw new InvalidDataException("The embedded source PDF exceeds the allowed size.");
            }
        }
    }

    private void EnsureArchiveFileWithinLimits(string path)
    {
        ValidateLimits();
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > Limits.MaximumArchiveBytes)
            throw new InvalidDataException("The compressed project package exceeds the allowed file size.");
    }

    private void ValidateLimits()
    {
        if (Limits.MaximumArchiveBytes <= 0 || Limits.MaximumEntryCount <= 0 ||
            Limits.MaximumJsonEntryBytes <= 0 || Limits.MaximumThumbnailCount <= 0 ||
            Limits.MaximumThumbnailEntryBytes <= 0 || Limits.MaximumTotalThumbnailBytes <= 0 ||
            Limits.MaximumEmbeddedPdfBytes <= 0 || Limits.MaximumTotalUncompressedBytes <= 0 ||
            !double.IsFinite(Limits.MaximumCompressionRatio) || Limits.MaximumCompressionRatio <= 0 ||
            Limits.MaximumCommentCount <= 0 || Limits.MaximumTagCount <= 0 ||
            Limits.MaximumInternalLinkCount <= 0 || Limits.MaximumRedactionCount <= 0 || Limits.MaximumCommentCharacters <= 0 ||
            Limits.MaximumTagNameCharacters <= 0)
            throw new InvalidOperationException("Project package resource limits must be positive finite values.");
    }

    private static PdfCorrectoriumProject NormalizeStorageMode(PdfCorrectoriumProject project, string formatVersion)
    {
        var mode = project.PdfStorageMode == ProjectPdfStorageMode.Legacy
            ? project.SourcePdf.IsEmbedded ? ProjectPdfStorageMode.Embedded : ProjectPdfStorageMode.Relative
            : project.PdfStorageMode;
        var source = project.SourcePdf with { IsEmbedded = mode == ProjectPdfStorageMode.Embedded };
        // 1.0/1.1 projects may contain an absolute compatibility hint. It remains usable in memory,
        // but the next current-format save removes it from the package.
        return project with { PdfStorageMode = mode, SourcePdf = source };
    }

    private void ValidateProjectExtensions(PdfCorrectoriumProject project, List<ProjectValidationIssue> issues)
    {
        if (project.Comments.Count > Limits.MaximumCommentCount)
            issues.Add(new("comments.limit", "The project contains too many comments.", true));
        if (project.Tags.Count > Limits.MaximumTagCount)
            issues.Add(new("tags.limit", "The project contains too many tags.", true));
        if (project.InternalLinks.Count > Limits.MaximumInternalLinkCount)
            issues.Add(new("links.limit", "The project contains too many internal links.", true));
        if (project.Redactions.Count > Limits.MaximumRedactionCount)
            issues.Add(new("redactions.limit", "The project contains too many redaction regions.", true));

        var pages = project.Pages.ToDictionary(page => page.Id);
        foreach (var item in project.PageSequence.Select((page, index) => (Page: page, Number: index + 1)))
            pages.TryAdd(item.Page.PageId, new OcrPage { Id = item.Page.PageId, PageNumber = item.Number });
        var regions = project.Pages.SelectMany(page => page.TextRegions.Select(region => (page.Id, Region: region)))
            .ToDictionary(item => item.Region.Id);
        var tags = new HashSet<Guid>();
        var tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in project.Tags)
        {
            if (!tags.Add(tag.Id)) issues.Add(new("tags.duplicateId", "Duplicate tag IDs were found.", true));
            if (string.IsNullOrWhiteSpace(tag.Name) || tag.Name.Length > Limits.MaximumTagNameCharacters)
                issues.Add(new("tags.name", "A tag name is empty or too long.", true));
            else if (!tagNames.Add(tag.Name.Trim()))
                issues.Add(new("tags.duplicateName", "Duplicate tag names were found.", true));
            if (!IsColorHex(tag.ColorHex)) issues.Add(new("tags.color", "A tag color is invalid.", true));
        }

        var commentIds = new HashSet<Guid>();
        foreach (var comment in project.Comments)
        {
            if (!commentIds.Add(comment.Id)) issues.Add(new("comments.duplicateId", "Duplicate comment IDs were found.", true));
            if (comment.Body.Length > Limits.MaximumCommentCharacters)
                issues.Add(new("comments.body", "A comment is too long.", true));
            if (comment.TagIds.Distinct().Count() != comment.TagIds.Count || comment.TagIds.Any(tagId => !tags.Contains(tagId)))
                issues.Add(new("comments.tags", "A comment contains a duplicate or unknown tag reference.", true));
            if (!TargetExists(comment.Target, pages, regions))
                issues.Add(new("comments.target", "A comment target does not exist.", false));
        }

        var linkIds = new HashSet<Guid>();
        foreach (var link in project.InternalLinks)
        {
            if (!linkIds.Add(link.Id)) issues.Add(new("links.duplicateId", "Duplicate internal-link IDs were found.", true));
            if (!pages.ContainsKey(link.SourcePageId) || !pages.ContainsKey(link.DestinationPageId))
                issues.Add(new("links.page", "An internal link refers to a missing page.", false));
            if (link.SourceRegionId is { } regionId &&
                (!regions.TryGetValue(regionId, out var region) || region.Id != link.SourcePageId))
                issues.Add(new("links.region", "An internal link refers to a missing OCR region.", false));
            if (link.SourceRegionId is null && link.SourceBounds is not { IsValid: true })
                issues.Add(new("links.bounds", "An area link must contain a valid source rectangle.", true));
            if (link.DestinationPosition is { IsFinite: false })
                issues.Add(new("links.destination", "An internal-link destination contains invalid coordinates.", true));
            if (link.DestinationZoomPercent is { } zoom && (!double.IsFinite(zoom) || zoom is < 25 or > 400))
                issues.Add(new("links.zoom", "An internal-link destination zoom is outside 25–400 percent.", true));
        }

        var redactionIds = new HashSet<Guid>();
        foreach (var redaction in project.Redactions)
        {
            if (!redactionIds.Add(redaction.Id))
                issues.Add(new("redactions.duplicateId", "Duplicate redaction IDs were found.", true));
            if (!pages.TryGetValue(redaction.PageId, out var page))
            {
                issues.Add(new("redactions.page", "A redaction refers to a missing page.", true));
                continue;
            }
            if (!redaction.Bounds.IsValid || redaction.Bounds.Left < 0 || redaction.Bounds.Bottom < 0 ||
                (page.WidthPoints > 0 && redaction.Bounds.Right > page.WidthPoints + 0.01) ||
                (page.HeightPoints > 0 && redaction.Bounds.Top > page.HeightPoints + 0.01))
                issues.Add(new("redactions.bounds", "A redaction contains invalid or out-of-page bounds.", true));
            if (!IsColorHex(redaction.ColorHex))
                issues.Add(new("redactions.color", "A redaction color is invalid.", true));
            (Guid Id, OcrTextRegion Region)? source = null;
            if (redaction.SourceRegionId is { } sourceRegionId)
            {
                if (!regions.TryGetValue(sourceRegionId, out var sourceRegion) || sourceRegion.Id != redaction.PageId)
                    issues.Add(new("redactions.region", "A redaction refers to a missing OCR region.", false));
                else
                    source = sourceRegion;
            }
            var hasCharacterStart = redaction.SourceCharacterStart.HasValue;
            var hasCharacterLength = redaction.SourceCharacterLength.HasValue;
            if (hasCharacterStart != hasCharacterLength || redaction.SourceCharacterStart is < 0 ||
                redaction.SourceCharacterLength is <= 0)
                issues.Add(new("redactions.characters", "A redaction contains an invalid character range.", true));
            else if (hasCharacterStart && source is { } referencedRegion &&
                     redaction.SourceCharacterStart!.Value + redaction.SourceCharacterLength!.Value >
                     new StringInfo(referencedRegion.Region.EffectiveText).LengthInTextElements)
                issues.Add(new("redactions.characters", "A redaction character range exceeds its OCR region.", true));
        }
    }

    /// <summary>形式1.4以降の論理ページ対応が元PDFとOCRページに整合するか検査します。</summary>
    private static void ValidatePageSequence(
        PdfCorrectoriumProject project,
        SourcePdfReference source,
        string formatVersion,
        List<ProjectValidationIssue> issues)
    {
        if (formatVersion is not ("1.4" or ProjectManifest.CurrentVersion)) return;
        if (project.PageSequence.Count == 0)
        {
            if (source.PageCount is > 0 || project.Pages.Count > 0)
                issues.Add(new("pageSequence.missing", "The logical page sequence is missing.", true));
            return;
        }

        var sourcePageCount = source.PageCount;
        var pageIds = new HashSet<Guid>();
        foreach (var page in project.PageSequence)
        {
            if (page.PageId == Guid.Empty || !pageIds.Add(page.PageId))
                issues.Add(new("pageSequence.duplicateId", "The logical page sequence contains an empty or duplicate page ID.", true));
            if (page.SourcePageNumber <= 0 || sourcePageCount is { } count && page.SourcePageNumber > count)
                issues.Add(new("pageSequence.sourcePage", "A logical page refers to a source page outside the PDF.", true));
            if (ProjectPageSequence.NormalizeRotation(page.RotationDegrees) != page.RotationDegrees || page.RotationDegrees % 90 != 0)
                issues.Add(new("pageSequence.rotation", "A logical page rotation must be 0, 90, 180, or 270 degrees.", true));
        }

        foreach (var page in project.Pages)
        {
            if (page.PageNumber <= 0 || page.PageNumber > project.PageSequence.Count)
            {
                issues.Add(new("pages.outOfSequence", "An OCR page lies outside the logical page sequence.", true));
                continue;
            }
            if (page.Id != project.PageSequence[page.PageNumber - 1].PageId)
                issues.Add(new("pages.sequenceId", "An OCR page ID does not match its logical page entry.", true));
        }
    }

    /// <summary>世代数と合計容量の両方を満たすよう、古い世代バックアップから整理します。</summary>
    private void TrimVersionedBackups(string fullPath, string newestBackup)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var keepCount = Math.Clamp(BackupGenerationCount, 1, 20);
        var byteLimit = Math.Max(1, BackupByteLimit);
        var backups = Directory.EnumerateFiles(directory, $"{stem}.backup-*{ProjectExtension}")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        long retainedBytes = 0;
        for (var index = 0; index < backups.Length; index++)
        {
            var file = backups[index];
            var mustKeep = index == 0 || file.FullName.Equals(newestBackup, StringComparison.OrdinalIgnoreCase);
            var fits = index < keepCount && retainedBytes <= byteLimit - Math.Min(file.Length, byteLimit);
            if (mustKeep || fits)
            {
                retainedBytes = checked(retainedBytes + file.Length);
                continue;
            }
            TryDelete(file.FullName);
        }
    }

    /// <summary>復旧直前コピーは直近の1件だけを残します。</summary>
    private static void TrimRecoveryCopies(string fullPath, string newestCopy)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);
        foreach (var oldCopy in Directory.EnumerateFiles(directory, $"{fileName}.pre-recovery-*")
                     .Where(path => !path.Equals(newestCopy, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(File.GetLastWriteTimeUtc))
            TryDelete(oldCopy);
    }

    /// <summary>内容ハッシュ名の展開PDFをアクセス順で整理し、現在のPDFは必ず保持します。</summary>
    private void TrimMaterializedSourceCache(string directory, string protectedPath)
    {
        var fileLimit = Math.Max(1, MaterializedSourceCacheFileLimit);
        var byteLimit = Math.Max(1, MaterializedSourceCacheByteLimit);
        var files = Directory.EnumerateFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastAccessTimeUtc)
            .ThenByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        long retainedBytes = 0;
        var retainedCount = 0;
        foreach (var file in files)
        {
            var isProtected = file.FullName.Equals(protectedPath, StringComparison.OrdinalIgnoreCase);
            var fits = retainedCount < fileLimit && retainedBytes <= byteLimit - Math.Min(file.Length, byteLimit);
            if (isProtected || fits)
            {
                retainedCount++;
                retainedBytes = checked(retainedBytes + file.Length);
                continue;
            }
            TryDelete(file.FullName);
        }
    }

    private static void TouchCacheEntry(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool TargetExists(
        ProjectTargetReference target,
        IReadOnlyDictionary<Guid, OcrPage> pages,
        IReadOnlyDictionary<Guid, (Guid Id, OcrTextRegion Region)> regions) => target.Kind switch
        {
            ProjectTargetKind.Document => true,
            ProjectTargetKind.Page => target.PageId is { } pageId && pages.ContainsKey(pageId),
            ProjectTargetKind.OcrRegion => target.ObjectId is { } regionId && regions.ContainsKey(regionId),
            _ => true,
        };

    private static bool IsColorHex(string value) =>
        value.Length == 7 && value[0] == '#' && value.AsSpan(1).ToArray().All(Uri.IsHexDigit);

    private static bool IsSafeRelativeSourcePath(string value, bool allowParentSegments)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':')) return false;
        var segments = value.Replace('\\', '/').Split('/');
        return segments.Length > 0 && segments.All(segment =>
            !string.IsNullOrWhiteSpace(segment) &&
            segment != "." &&
            (allowParentSegments || segment != ".."));
    }

    /// <summary>ストリームをコピーし、読み取り累積量が上限を超えた時点で失敗します。</summary>
    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copied = checked(copied + read);
            if (copied > maximumBytes)
                throw new InvalidDataException("The expanded project entry exceeds the allowed size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void EnsureSourceReferenceIsSafe(SourcePdfReference source)
    {
        if (source.FileSize < 0)
            throw new InvalidDataException("The source PDF size is invalid.");
        if (source.Sha256.Length != 64 || source.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The source PDF SHA-256 value must contain exactly 64 hexadecimal characters.");
    }

    private static async Task<bool> FileMatchesSourceAsync(
        string path,
        SourcePdfReference source,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != source.FileSize) return false;
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), source.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>プロジェクト基準の相対パスを優先し、利用できない場合だけ旧形式の絶対パスを試します。</summary>
    private static string ResolveExternalSourcePath(SourcePdfReference source, string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(source.RelativePath))
        {
            if (!IsSafeRelativeSourcePath(source.RelativePath, allowParentSegments: true))
                throw new InvalidDataException("The source PDF path is not a normalized relative path.");
            var relative = Path.GetFullPath(Path.Combine(projectDirectory, source.RelativePath));
            if (File.Exists(relative)) return relative;
        }
        if (!string.IsNullOrWhiteSpace(source.AbsolutePathHint) && File.Exists(source.AbsolutePathHint)) return source.AbsolutePathHint;
        throw new FileNotFoundException("The source PDF could not be resolved.", source.FileName);
    }
}
