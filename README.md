# PDF Correctorium

PDF Correctorium is an Apache-2.0 licensed Windows PDF editor focused on correcting OCR text layers, geometry, reading order, vertical Japanese text, ruby, and review state without modifying the source PDF during editing.

The application targets .NET 8 for the Version 1.0 compatibility baseline and is built with the Visual Studio 2026 / .NET 10 toolchain.

## Current milestone

The current repository snapshot corresponds to the `v1.0.0-dev.122` development line. It includes:

- C# / .NET 8 / WPF solution structure
- Core OCR region model with vertical/horizontal writing and independent rotation
- Immutable original OCR values and editable overlay values
- Review states and output attributes
- Undo/redo for OCR text, geometry, character advances, reading order, review state, search/replace, and multi-region edits
- Safe ZIP-compatible `.pdfocrproj` save/open/validation
- Source PDF SHA-256 fingerprinting
- Portable and installed data-path resolution
- Structured diagnostic log foundation
- WPF application shell for opening PDFs and projects
- Local PDFium page rendering with a scrollable preview
- Page count, asynchronous thumbnail navigation, previous/next controls, page insertion/deletion/reordering, and 90-degree page rotation
- External and embedded project source-PDF preview support
- Semi-transparent OCR text overlays extracted from PDF text objects, including invisible render mode and zero-alpha text
- Automatic NDLOCR-Lite companion discovery for JSON, XML, TXT, and TEI files
- Coordinate-based overlay import from NDLOCR-Lite JSON and XML, with a manual import fallback
- Selectable OCR regions with text, position, size, rotation, writing-direction, review-state, split/merge, lock, and reading-order editing
- Mouse movement, eight-direction resize handles, rotation controls, alignment, and character-level advance editing
- 25-400% zoom, fit-width, fit-height, fit-page, fit-selection, toolbar controls, and Ctrl+mouse-wheel
- OCR search/replace, repeated-region propagation, and whole-document OCR quality analysis
- Editable PDF bookmarks and document viewer preferences
- Safe isolated export to a separately saved PDF, followed by validation and output commit
- Project autosave, versioned backups, backup restoration, and cached page thumbnails
- Japanese and English UI, switchable from `Settings > Display > Display language`
- Persistent UI-language selection for both portable and installed operation
- Compact preview workspace with configurable panels, thumbnail size, overlay appearance, edit handles, shortcuts, autosave, and backup retention
- Dependency-free contract test runner

The implemented application is broader than the original foundation milestone described in older design snapshots. Remaining Version 1.0 gaps and known defects are tracked in `IMPLEMENTATION_STATUS.md` and in the implementation-status sections of the design documentation.

## Display language

The application UI supports Japanese (`ja-JP`) and English (`en-US`). Change the language from the Display tab in the application settings window. The main window, menus, tooltips, property labels, page names, status choices, writing-direction choices, and principal dialogs update immediately; the selection is restored the next time the application starts.

Localization affects application chrome only. It never translates or rewrites text contained in the source PDF, imported OCR data, comments, bookmarks, or project content.

## Build

`global.json` currently pins .NET SDK `10.0.302` with `latestPatch` roll-forward. Install a compatible 10.0.3xx SDK before using the commands below. The source also compiles with SDK 10.0.400 when SDK selection is performed outside the pinned repository directory, but the normal repository-root command will not select that feature band.

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"
dotnet build PdfCorrectorium.sln
dotnet run --project tests/PdfCorrectorium.ContractTests
```

As of the 2026-08-29 documentation audit, the Release solution build succeeds with zero warnings and errors when a compatible SDK is selected, and all 12 contract tests pass. The built-in `--smoke-test` currently fails because its expected settings format version is stale; this is a known source defect rather than a successful quality gate.

## Documentation

The design baseline is under `outputs/PdfCorrectorium-Documentation`. The Markdown files are normative. `PDF-Correctorium-Design-Documentation.pdf` is a 2026-08-09 snapshot and can lag behind the Markdown until the PDF publication process is restored.

## License

Apache License 2.0. Third-party components are tracked in `THIRD-PARTY-NOTICES.md`; an SBOM will be added before a stable distribution.
