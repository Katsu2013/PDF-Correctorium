using System.Runtime.CompilerServices;

// The export diagnostic executable verifies file-lock and recovery behavior that is intentionally
// kept internal to the desktop application rather than exposed as part of its public API.
[assembly: InternalsVisibleTo("PdfCorrectorium.ExportDiagnostics")]
