using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>Existing option-shaped paths take precedence over diagnostic command routing.</summary>
internal static class StartupArgumentRouting
{
    internal const string DiagnosticsEnvironmentVariable = "PDFCORRECTORIUM_ENABLE_DIAGNOSTICS";

    internal static bool IsDiagnosticRequest(IReadOnlyList<string> arguments) => arguments.Any(value =>
        value.StartsWith("--", StringComparison.Ordinal) &&
        (value.EndsWith("-test", StringComparison.OrdinalIgnoreCase) ||
         value.EndsWith("-tests", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("--diagnostic-", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("--sandbox-probe", StringComparison.OrdinalIgnoreCase)));

    internal static bool DiagnosticsEnabled(string? value) => string.Equals(value, "1", StringComparison.Ordinal);

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
