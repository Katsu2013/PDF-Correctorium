# PDF Correctorium

PDF Correctorium is an Apache-2.0 licensed Windows PDF editor focused on correcting OCR text layers, geometry, reading order, vertical Japanese text, ruby, and review state without modifying the source PDF during editing.

The application targets .NET 8 for the Version 1.0 compatibility baseline and is built with the Visual Studio 2026 / .NET 10 toolchain.

## Current milestone

The current repository snapshot corresponds to the `v1.0.0-dev.124` development line. It includes:

- C# / .NET 8 / WPF solution structure
- Core OCR region model with vertical/horizontal writing and independent rotation
- Immutable original OCR values and editable overlay values
- Review states and output attributes
- Dedicated proofreading/review mode with status filters, cross-page target navigation, verify-and-next, and protection from direct geometry edits
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
- Editable PDF bookmarks, document metadata (title, author, subject, keywords, creator, and producer), document language, output PDF version, and viewer preferences
- Safe isolated export to a separately saved PDF, followed by validation and output commit
- Project autosave, versioned backups, backup restoration, and cached page thumbnails
- Japanese and English UI, switchable from `Settings > Display > Display language`
- Persistent UI-language selection for both portable and installed operation
- Compact preview workspace with configurable panels, thumbnail size, overlay appearance, edit handles, shortcuts, autosave, and backup retention
- Dependency-free contract test runner

The implemented application is broader than the original foundation milestone described in older design snapshots. Remaining Version 1.0 gaps and known defects are tracked in [Implementation status](IMPLEMENTATION_STATUS.md) and in the implementation-status sections of the design documentation. Features listed above are not a guarantee of complete round-trip preservation or of meeting every Version 1.0 requirement.

## Safety fixes and remaining limitations (dev.123)

The five issues reproduced in the dev.122 audit are now addressed: document switching prompts to save/discard/cancel and preserves the current document on failed loads; intentional empty text survives project save/reload and PDF export; parent/fit/output metadata is retained; review-mode width correction is guarded; bulk replacements receive needs-review status.

Autosave runs at the configured interval or after about 30 seconds without input (checked every 5 seconds). Never-saved projects receive a source-embedded recovery package under `workspaces/recovery/<project-id>.autosave.pdfocrproj`. Open that file explicitly to recover; automatic recovery discovery at startup is not implemented. Recovery writes do not mark the project saved.

**Project compatibility:** new saves use format 1.1; versions 1.0 and 1.1 can be read by dev.123. Older application builds cannot open new 1.1 packages. Keep a backup when older-build compatibility is needed. The manifest now records the build version.

Page-structure Undo, external/in-app OCR, ruby/comments/tags/diffs, hierarchical progress, repair/rescue UI, docking and other full Version 1.0 requirements remain unfinished. See [implementation status](IMPLEMENTATION_STATUS.md#remaining-version-10-gaps). This increment is not completion of every remaining feature.

## Display language

The application UI supports Japanese (`ja-JP`) and English (`en-US`). Change the language from the Display tab in the application settings window. The main window, menus, tooltips, property labels, page names, status choices, writing-direction choices, and principal dialogs update immediately; the selection is restored the next time the application starts.

Localization affects application chrome only. It never translates or rewrites text contained in the source PDF, imported OCR data, comments, bookmarks, or project content.

## Proofreading / review mode

Choose `校正・確認` in the toolbar mode selector. The right pane lists matching regions on the current page, in reading order. The default filter is `未確認・要再確認`; unreviewed-only, needs-review-only, and all-status filters are also available. Deleted regions are never review targets. Other OCR overlays remain visible for context.

`前の対象` / `次の対象` move through matching regions in page order, then reading order, loading additional pages only when necessary. They do not wrap at the document ends. `確認済みにして次へ` marks the single selected region verified and moves to the next target. A text correction marks the region modified; its editor remains open even if it no longer matches the filter. Choose all statuses to revisit verified, modified, excluded or deferred regions. Target search can be canceled, and changing the mode, filter, page or document cancels pending navigation.

Text, word readings, and review status remain editable. Ordinary direct movement, resize, rotation, alignment, character-width adjustment, region creation/deletion and split/merge commands are disabled in this mode; existing geometry-lock settings are not rewritten. The quality-analysis correction path is also guarded. Text corrections still use the normal character-cell reconciliation rules, including layout changes needed for inserting/removing text. Return to OCR editing for direct layout adjustments. Review states and corrected text support project save and Undo/Redo, including intentional empty text and preserved region metadata. Review filters and the selected mode are temporary UI state, not saved document metadata.

Selecting a review-list entry or using target navigation scrolls the preview to reveal the target. Ordinary selection by clicking an OCR region in the preview leaves the scroll position unchanged. The current-page target count is not a document-wide or hierarchical review-progress report; those aggregate reports, comments, tags and diffs remain unimplemented.

## Build

### Version policy

`Directory.Build.props` is the sole source of application version inputs. Development revision 124 produces product version `1.0.0-dev.124` and assembly/file version `1.0.0.124` across the solution. The title bar, About dialog, startup log and saved project manifest use the build version. [VERSIONING.md](VERSIONING.md) defines the mandatory revision-increment and verification rules; [AGENTS.md](AGENTS.md) applies them to future repository work.

Before delivering changed source/build tools, advance `DevelopmentRevision`. Portable publication rejects version mismatches, local revision rollback and changed inputs reusing a certified revision, checks the actual EXE/DLL metadata, and writes `build-info.json`. Same-source verification rebuilds may retain a revision but always use a new timestamped folder. The project data format remains 1.1; its minimum reader remains dev.123. Git metadata was unavailable in this working folder at the time of this change; local build records are not a substitute for Git history.

### Commands and prerequisites

Opening a `.pdf` or `.pdfocrproj` through a Windows file association is supported: the application consumes the single file argument and loads the first page, using the same loader as the File menu. Japanese names, spaces, relative paths, and uppercase extensions are supported. Each launch creates a new window; existing instances do not receive forwarded requests. Missing/corrupt files report an error without silently claiming a successful open.

The portable build includes `Icons/PdfDocument.ico`, `Icons/PdfCorrectoriumProject.ico`, and [FILE-ASSOCIATIONS.md](FILE-ASSOCIATIONS.md) with the quoted launch command and icon mapping. No Windows association or default application is changed automatically.

`global.json` specifies .NET SDK `10.0.302` with `latestFeature` roll-forward, allowing the normal repository-root commands to use installed .NET 10 SDK 10.0.400. The runtime target remains .NET 8.

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"
dotnet build PdfCorrectorium.sln
dotnet run --project tests/PdfCorrectorium.ContractTests
```

Create a portable build in a new timestamped folder without overwriting an earlier build:

```powershell
.\tools\BuildPortable.ps1
```

Builds are stored under `outputs/PdfCorrectorium-Builds` using the name
`PdfCorrectorium-<version>-win-x64-<yyyyMMdd-HHmmss>`. After a solution build has already restored dependencies, use `-NoRestore` when working offline.

The earlier dev.122 smoke-test settings mismatch is fixed in dev.123. Current verification is recorded in the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md); the old audit results remain historical evidence.

The status-bar zoom slider places 100% at its center marker: the left half maps linearly from 25% to 100%, and the right half from 100% to 400%. Clicking the track moves directly to that position. Arrow keys still change zoom by 1 percentage point, and PageUp/PageDown by 10; the dropdown and toolbar stay synchronized.

Status-bar zoom buttons are frameless, with background-only hover/press feedback and a keyboard-focus indicator. Toolbar spacing is compact: button padding is 2 DIP and margins are 1 DIP. The saved size preference and icon dimensions are preserved; the main button's outer size is reduced by 4 DIP (minimum 24 DIP), with tighter separators and toolbar padding.

The focused document-availability UI test runs without displaying a window. It checks disabled menus/commands before opening a PDF, dialog/shortcut guards, re-enabling after PDF/project loading, failed loads, page/zoom limits, the single-color status-bar slider track and centered two-scale mapping, and zoom-display synchronization after slider/toolbar operations, dropdown selections, and manual input. Supply a new output folder; the test writes its own two-page PDF fixture, screenshots, and `checks.txt`, and returns a nonzero exit code on failure:

```powershell
$uiTestOutput = Join-Path $PWD ("outputs/.verification/document-ui-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--document-ui-test", ('"' + $uiTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

On 2026-08-30 this focused test passed all 136 checks, including preservation of the zoom-text binding, conversion round-trips across 25–400%, midpoint/marker alignment, keyboard steps across the midpoint, flat status-button states, and compact toolbar sizing with labels on/off at size preferences 28, 36, and 64. It is separate from the legacy smoke test, whose settings mismatch was corrected in dev.123.

File-launch integration tests start 13 fresh application processes and verify the first-page preview, project data, embedded/external sources, error handling, unchanged input files, and packaged icon resolutions (67 checks):

```powershell
$launchTestOutput = Join-Path $PWD ("outputs/.verification/file-launch-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--file-launch-tests", ('"' + $launchTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

The dedicated review test creates its own two-page PDF/project fixture and renders the hidden WPF view. On 2026-08-30 it passed 69 checks covering filters, reading order, cross-page navigation, confirmation, text correction, Undo/Redo, save/reload, cancellation, geometry command/handler guards, and localized dropdown labels/selections:

```powershell
$reviewTestOutput = Join-Path $PWD ("outputs/.verification/review-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--review-mode-test", ('"' + $reviewTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

The child-test option `--startup-file-test <new-report-path>` suppresses only windows/modal error dialogs and writes the resulting document state; it invokes the same startup file-opening path as normal launches. These tests do not modify Windows file associations.

Before the dev.123 fixes, the 2026-08-30 audit re-ran the 136 document-UI, 67 file-launch, 69 review-mode and 13 contract checks: all 285 passed. Separate synthetic-data probes nevertheless reproduced the five now-addressed audit problems, and the legacy smoke test still failed. These counts describe the tested paths, not complete acceptance. See the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md) for coverage gaps and required regression cases.

The permanent `--persistence-test <new-output-directory>` additionally checks switching/cancel/failure preservation, empty OCR text through project and PDF output, loaded/unvisited region metadata, review restrictions, bulk status, idle autosave and embedded recovery. Run it like the other diagnostic modes, with a new output directory.

## Documentation

The [design documentation index](outputs/PdfCorrectorium-Documentation/README.md) links the normative Markdown and five updated diagrams, including the new review and document-properties views. Future repair functionality is explicitly labeled unimplemented. `PDF-Correctorium-Design-Documentation.pdf` remains the 2026-08-09 snapshot and has not been regenerated.

## License

Apache License 2.0. Third-party components are tracked in `THIRD-PARTY-NOTICES.md`; an SBOM will be added before a stable distribution.
