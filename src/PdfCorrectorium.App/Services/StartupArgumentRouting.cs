using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>Existing option-shaped paths take precedence over diagnostic command routing.</summary>
internal static class StartupArgumentRouting
{
    internal static bool FilesTakePriority(IReadOnlyList<string> arguments,
        Func<string, bool>? fileExists = null, Func<string, bool>? directoryExists = null)
    {
        fileExists ??= File.Exists;
        directoryExists ??= Directory.Exists;
        // Probe only relative option-shaped arguments, never arbitrary document
        // references or UNC paths merely to decide whether diagnostics may run.
        return arguments.Any(value => value.StartsWith("--", StringComparison.Ordinal) &&
            (fileExists(value) || directoryExists(value)));
    }
}
