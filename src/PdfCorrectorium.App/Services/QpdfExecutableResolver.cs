using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>Prefers the distribution's qpdf; environment overrides are development-only fallbacks.</summary>
internal static class QpdfExecutableResolver
{
    internal static string? Resolve(string? baseDirectory = null)
    {
        var root = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        var candidates = new[]
        {
            Path.Combine(root, "components", "qpdf", "qpdf.exe"),
            Path.Combine(root, "qpdf.exe"),
            Path.Combine(root, "tools", "qpdf", "bin", "qpdf.exe"),
            Path.Combine(root, "qpdf", "bin", "qpdf.exe"),
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;
#if DEBUG
        var configured = Environment.GetEnvironmentVariable("PDFOCR_QPDF_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);
        for (var parent = Directory.GetParent(root); parent is not null; parent = parent.Parent)
        {
            var candidate = Path.Combine(parent.FullName, "tools", "qpdf", "bin", "qpdf.exe");
            if (File.Exists(candidate)) return candidate;
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "qpdf.exe");
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (ArgumentException) { }
        }
#endif
        return null;
    }
}
