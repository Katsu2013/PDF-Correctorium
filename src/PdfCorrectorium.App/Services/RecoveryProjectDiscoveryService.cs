using System.IO;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App.Services;

public sealed record RecoveryProjectCandidate(
    string Path,
    string ProjectName,
    DateTime LastWriteTime,
    long FileSize);

/// <summary>通常保存前の文書に対して作成された復旧プロジェクトを安全に列挙します。</summary>
public static class RecoveryProjectDiscoveryService
{
    public static async Task<IReadOnlyList<RecoveryProjectCandidate>> FindValidAsync(
        string workspaceDirectory,
        ProjectPackageService packages,
        CancellationToken cancellationToken = default)
    {
        var recoveryDirectory = Path.Combine(Path.GetFullPath(workspaceDirectory), "recovery");
        if (!Directory.Exists(recoveryDirectory)) return [];
        var candidates = new List<RecoveryProjectCandidate>();
        foreach (var path in Directory.EnumerateFiles(
                     recoveryDirectory,
                     $"*.autosave{ProjectPackageService.ProjectExtension}",
                     SearchOption.TopDirectoryOnly)
                 .OrderByDescending(File.GetLastWriteTimeUtc)
                 .Take(100))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var validation = await packages.ValidateAsync(path, cancellationToken).ConfigureAwait(false);
                if (!validation.IsValid) continue;
                var project = await packages.OpenAsync(path, cancellationToken).ConfigureAwait(false);
                var file = new FileInfo(path);
                candidates.Add(new RecoveryProjectCandidate(
                    file.FullName,
                    string.IsNullOrWhiteSpace(project.Name) ? file.Name : project.Name,
                    file.LastWriteTime,
                    file.Length));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // 壊れた、使用中、またはアクセスできない復旧データは起動を妨げません。
            }
        }
        return candidates;
    }
}
