using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// 検証済みの一時PDFを利用者が指定した保存先へ安全に確定します。
/// </summary>
/// <remarks>
/// PDFの生成は長時間かかることがあるため、保存先の競合は生成前と確定直前の二度確認します。
/// 確定直前にAcrobat等が保存先を開いた場合も、完成済みPDFを別名へ退避して生成結果を失いません。
/// </remarks>
internal static class PdfOutputFileCommitter
{
    private const int CommitAttempts = 4;

    /// <summary>保存先のフォルダーへ書き込め、既存ファイルを置換できる状態かを確認します。</summary>
    internal static void ValidateDestination(string destinationPdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPdfPath);
        var destination = Path.GetFullPath(destinationPdfPath);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("PDFの出力先フォルダーを特定できません。");
        Directory.CreateDirectory(directory);

        var probePath = Path.Combine(directory, $".pdf-correctorium-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new IOException(
                $"出力先フォルダーへ書き込めません。保存先またはアクセス権を確認してください。\n{directory}",
                exception);
        }
        finally
        {
            TryDelete(probePath);
        }

        if (!File.Exists(destination)) return;
        if ((File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0)
            throw new IOException($"出力先PDFは読み取り専用です。読み取り専用を解除するか、別名で保存してください。\n{destination}");

        try
        {
            using var stream = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new IOException(
                $"出力先PDFはAcrobat等で開かれているか、ほかの処理が使用中です。PDFを閉じるか、別名を指定してください。\n{destination}",
                exception);
        }
    }

    /// <summary>
    /// 完成済みの一時PDFを保存先へ確定し、競合が解消しない場合は同じフォルダーへ別名で退避します。
    /// </summary>
    internal static PdfOutputCommitResult Commit(
        string completedPdfPath,
        string destinationPdfPath,
        bool preserveCompletedOutputOnConflict,
        CancellationToken cancellationToken)
    {
        var completed = Path.GetFullPath(completedPdfPath);
        var destination = Path.GetFullPath(destinationPdfPath);
        Exception? lastException = null;

        for (var attempt = 0; attempt < CommitAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                CommitOnce(completed, destination);
                return new PdfOutputCommitResult(destination);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                lastException = exception;
                if (attempt + 1 < CommitAttempts)
                    cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250 * (attempt + 1)));
            }
        }

        if (!preserveCompletedOutputOnConflict)
            throw CreateCommitException(destination, lastException!);

        var recoveredPath = CreateRecoveredPath(destination);
        try
        {
            File.Move(completed, recoveredPath);
            return new PdfOutputCommitResult(
                recoveredPath,
                $"指定した出力先は使用中または書き込み不可だったため、完成済みPDFを別名で保存しました。\n{recoveredPath}");
        }
        catch (Exception recoveryException) when (recoveryException is UnauthorizedAccessException or IOException)
        {
            throw new IOException(
                $"PDFの生成と検証は完了しましたが、指定先へ確定できませんでした。完成済みPDFは次の場所に保持されています。\n{completed}",
                new AggregateException(lastException!, recoveryException));
        }
    }

    private static void CommitOnce(string completedPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            // The completed PDF may be on another volume. Copy first, then remove the
            // working file, so finalization works across volumes and with long file names.
            File.Copy(completedPath, destinationPath, overwrite: false);
            File.Delete(completedPath);
            return;
        }

        if ((File.GetAttributes(destinationPath) & FileAttributes.ReadOnly) != 0)
            throw new UnauthorizedAccessException("出力先PDFは読み取り専用です。");

        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        var stagingPath = Path.Combine(destinationDirectory, $".pc-{Guid.NewGuid():N}.tmp");
        var backupPath = destinationPath + ".bak";
        try
        {
            // File.Replace requires files on the same volume, so stage a short sibling first.
            File.Copy(completedPath, stagingPath, overwrite: false);
            File.Replace(stagingPath, destinationPath, backupPath, ignoreMetadataErrors: true);
            File.Delete(completedPath);
        }
        finally
        {
            TryDelete(stagingPath);
        }
    }

    private static IOException CreateCommitException(string destinationPath, Exception innerException) =>
        new(
            $"出力先PDFを置き換えられません。Acrobat等で開いている場合は閉じ、読み取り専用属性とアクセス権を確認してください。\n{destinationPath}",
            innerException);

    private static string CreateRecoveredPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        var extension = Path.GetExtension(destinationPath);
        for (var sequence = 0; sequence < 100; sequence++)
        {
            var suffix = sequence == 0 ? string.Empty : $"-{sequence}";
            var candidate = Path.Combine(
                directory,
                $"PDF-Correctorium-completed-{DateTime.Now:yyyyMMdd-HHmmss}{suffix}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, $"PDF-Correctorium-completed-{Guid.NewGuid():N}{extension}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 書込み可否の確認用ファイルは、後続処理を妨げないよう削除失敗を無視します。
        }
    }
}

/// <summary>PDFの確定先と、指定先から変更された場合の警告を保持します。</summary>
internal sealed record PdfOutputCommitResult(string OutputPath, string? Warning = null);
