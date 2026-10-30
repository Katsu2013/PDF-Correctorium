namespace PdfCorrectorium.ProjectFormat;

/// <summary>未信頼の保存済み参照からのネットワーク／特殊パスへの自動アクセスを拒否します。</summary>
internal static class SourcePdfPathPolicy
{
    internal static void EnsureAutomaticAccessAllowed(string path)
    {
        // Classify UNC and device namespaces before any filesystem operation.
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new SourcePdfSelectionRequiredException();
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        if (OperatingSystem.IsWindows())
        {
            if (root.Length != 3 || root[1] != ':') throw new SourcePdfSelectionRequiredException();
            var driveType = new DriveInfo(root).DriveType;
            if (driveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom))
                throw new SourcePdfSelectionRequiredException();
        }
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                // Stop at the first reparse point; never inspect a child through it.
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new SourcePdfSelectionRequiredException();
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
    }
}

/// <summary>保存済み参照への自動アクセスではなく、利用者のPDF選択が必要です。</summary>
public sealed class SourcePdfSelectionRequiredException : IOException
{
    public SourcePdfSelectionRequiredException()
        : base("元PDFへの自動アクセスを停止しました。利用するPDFを明示的に選択してください。") { }
}
