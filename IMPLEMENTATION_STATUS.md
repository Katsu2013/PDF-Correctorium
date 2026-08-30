# Implementation status

## Current repository snapshot: v1.0.0-dev.124

### Version-management correction (2026-08-30)

- Centralized the prefix and development revision in `Directory.Build.props`. All solution projects now use product version `1.0.0-dev.124` and assembly/file version `1.0.0.124`, including the generated Windows manifest. The title bar and About dialog show the development revision; About also shows the four-part numeric build. Startup logging and saved manifests use the same build-derived version.
- Added build-time consistency checks, source-fingerprint/revision-reuse checks, actual published binary verification and `build-info.json`. Portable folder names come from evaluated MSBuild properties, not documentation. Changed code/build tools require a revision increment; identical-source rebuilds keep the revision but always get a new timestamped folder.
- Added [mandatory version rules](VERSIONING.md) and repository [working instructions](AGENTS.md). Project format 1.1 and its dev.123 minimum reader are unchanged. Historical dev.123/122 verification records remain below.
- Git does not recognize the current working folder as a repository. No Git metadata has been recreated, committed, tagged or pushed. Versioned binary checks and local publication records do not establish Git history continuity.

Final verification: Release solution build succeeded with 0 warnings/errors; 16 contract, 18 version-management, 139 document-UI, 38 persistence, 69 review and 67 file-launch checks passed (347 total). Packaged smoke test also exited 0. The version-management checks include rejection of overridden version inputs, misleading labels, same-revision source changes and revision rollback. Actual apphost/assembly versions and the embedded Windows manifest were verified.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.124-win-x64-20260830-183553`. Its `build-info.json` records source/binary hashes, SDK 10.0.400 and the matching product/numeric versions. Git fields are null because repository metadata was unavailable. The running pre-existing user instance was found to be the dev.122 build dated `20260830-143634`; it was not closed or replaced automatically.

### Current remaining scope

The five safety fixes from dev.123 remain implemented. Page-structure Undo and the larger Version 1.0 feature gaps listed below remain unfinished; this revision changes build identification and safeguards, not those features.

## Previous repository snapshot: v1.0.0-dev.123

### Implemented in this increment (2026-08-30)

- Updated all three existing SVG diagrams and added review-mode and document-properties diagrams. The repair/diagnostics diagram is explicitly a future design, not an implemented screen.
- Added Save / Discard / Cancel before document replacement, including pending editor input. Source validation and initial rendering happen before replacing the current document; failed replacements retain the previous preview, edits and Undo history.
- Added explicit `HasEditedText` so intentional empty OCR edits survive save/reload. Empty PDF text is removed before native measurement/transform, preventing the empty-text export crash reproduced during verification.
- Preserved existing parent IDs, fit modes, output attributes and explicit/original writing metadata when synchronizing loaded regions. Unvisited pages remain intact. Embedded projects retain their PDF when re-saved.
- Guarded quality-analysis width correction in both its UI and execution path while reviewing. Bulk replacements now receive `NeedsReview`, remain in the default review list, and retain Undo/Redo and persistence.
- Added idle-triggered autosave (30 seconds without input, checked every 5 seconds) alongside the configured interval. Never-saved projects receive a source-embedded recovery package under `workspaces/recovery/<project-id>.autosave.pdfocrproj`; open that file explicitly to recover. Autosave does not mark the document saved or repeatedly write an unchanged edit state.
- Project format 1.1 records explicit empty edits and build-derived application version. Readers accept 1.0 and 1.1, reject unsupported versions, and retain legacy naming compatibility. **dev.122 and earlier cannot open newly saved 1.1 projects**; keep backups if an older build must remain usable.
- Corrected the legacy smoke-test settings expectation and enabled .NET 10 SDK feature-band roll-forward so the normal repository-root build can use installed SDK 10.0.400.

### Verification

The permanent `--persistence-test <new-output-directory>` covers document replacement, canceled/failed saves, pending input, malformed/missing sources, empty edits, metadata on loaded/unvisited pages, review guards, bulk status, autosave, embedded-source persistence and actual text extraction after PDF export. See the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md) for the final run counts. Focused checks do not represent full Version 1.0 acceptance.

Final verification on 2026-08-30: repository-root Release build succeeded with 0 warnings/errors; 15 contract, 38 persistence, 69 review, 136 document-UI and 67 file-launch checks passed (325 total). Packaged `--smoke-test` also exited successfully. App diagnostics used `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.123-win-x64-20260830-180424`; all five updated SVGs were rendered and visually checked.

### Known implementation defects

- Page insertion, deletion, reordering and rotation still do not support Undo, and clear earlier OCR Undo history (FR-104 remains unmet).
- The five dev.122 audit defects listed below were addressed in dev.123; their old descriptions are retained only as historical evidence.

### Remaining Version 1.0 gaps

Google Vision and in-application OCR/provider contracts; ruby editing; comments, tags, diffs, hierarchical review aggregation and audit history; migration/repair/read-only/rescue UI and versioned schemas; plugin contracts; docking/presets/command palette and settings import-export; export-strategy selection, complete input warnings and multi-engine validation remain unfinished. Recovery-package discovery at startup is also not implemented. The old design PDF remains a dated snapshot. This increment is not completion of all unimplemented scope.

## Previous audited snapshot: v1.0.0-dev.122

Everything in this section describes dev.122 before the fixes above; it is not the current defect list or current verification result.

The build audited in this historical section identified itself as `v1.0.0-dev.122`. The numbered sections below are retained as historical milestone notes and are not a complete changelog from dev.105 through dev.122.

### Implemented since the original foundation milestone

- Separate edited-PDF export with an isolated worker process, validation, and safe output commit.
- OCR-region rotation, writing-direction and review-state editing, character-level advances and locks, line split/merge, alignment, and reading-order operations.
- Functional proofreading/review mode: current-page status-filtered target list; previous/next navigation across pages in reading order; single-region verify-and-next; empty/end-of-document feedback; and cancelable lazy page loading. Text and status edits support Undo/Redo and project persistence, subject to the known empty-text/attribute defects below. Ordinary direct geometry/structure controls are disabled while this mode is active, without rewriting saved lock flags; the quality-analysis correction path remains an exception. Other overlays remain visible for context; text insertion/deletion retains normal character-layout reconciliation. Explicit review-list/navigation selection reveals the target by scrolling, while ordinary preview clicks preserve scroll position.
- Selecting characters or changing editing modes without changing document content no longer marks a region modified. Shared dropdown templates honor display-member labels, and language changes preserve status, writing-direction, and review-filter selections.
- Fit-width, fit-height, fit-page, and fit-selection preview modes.
- Asynchronous page thumbnails with project cache persistence.
- Page insertion, deletion, reordering, and 90-degree rotation through a non-destructive working PDF.
- Search/replace, repeated-region propagation, and OCR quality analysis across a document.
- Editable/importable/exportable bookmarks; editable document metadata (title, author, subject, keywords, creator, and producer); editable document language; selectable output PDF version; document viewer settings; and image optimization.
- Japanese/English UI switching, expanded display/edit/save settings, autosave, versioned backups, and backup restoration.
- PDF-dependent menus, dialogs, navigation, and zoom controls are disabled until the source PDF is loaded; Open, settings, layout preferences, and Help remain available. The status-bar zoom slider uses a single neutral track color with a 100% center marker, a 25–100% left half, and a 100–400% right half. Arrow-key and PageUp/PageDown steps remain 1 and 10 percentage points.
- The zoom dropdown keeps its binding to the current zoom after text commit, selection, and cancellation; slider, toolbar, fit commands, and manual input remain synchronized.
- Status-bar zoom buttons are frameless in normal/disabled states, with background-only hover/press feedback and a keyboard-focus indicator. Toolbar button padding/margins, group separators, and outer sizes are compact while retaining the original icon dimensions and saved size preference.
- File-based startup for `.pdf` and `.pdfocrproj`, through the same loader as the File menu, including first-page rendering, project data restoration, and visible errors on failure. The portable build includes dedicated multi-resolution PDF/project icons and an association guide; Windows associations are not changed automatically.

### Focused UI verification performed on 2026-08-30

- Release solution build: 0 warnings and 0 errors; all 13 contract tests passed.
- `--document-ui-test <new-output-directory>`: 136 checks passed, covering startup/loaded menu states, dialog and search shortcut guards, failed opens, Save As notifications, page and zoom limits, project reload/backup availability, the single-color zoom track, the centered two-scale mapping and keyboard increments, zoom-dropdown synchronization, flat status-button states, and compact toolbar sizing with labels on/off at size preferences 28, 36, and 64.
- Rendered startup and loaded-document screenshots were visually checked. This focused test does not replace the legacy smoke test described below.
- `--file-launch-tests <new-output-directory>`: 67 checks passed across 13 fresh-process launches, covering Japanese/space-containing names, relative/uppercase paths, external and embedded PDF projects, corrupt/missing/unsupported input, source fingerprint mismatches, multiple-argument rejection, unchanged input files, and packaged ICO resolutions.
- `--review-mode-test <new-output-directory>`: 69 checks passed using a generated two-page PDF/project, covering every status filter, exclusion of deleted regions, reading-order sorting, preview/list selection, cross-page navigation in both directions, verification, text correction, Undo/Redo, project reload, cancellation/reentry prevention, empty documents, hidden geometry handles and disabled commands/handlers, mode exit, and Japanese/English labels with preserved selections. Rendered review screenshots were visually checked. The 136-check document-UI test, 67-check file-launch test, and 13 contract tests also passed after the review implementation.
- The new portable executable was also launched with only a PDF path and only a project path (no diagnostic options); both normal startup runs reached `startup.file-open.complete` with 2 pages and an available preview. Actual Windows association registration was not changed or tested.

### Verification performed on 2026-08-29

- Release solution build: passed with 0 warnings and 0 errors when SDK 10.0.400 was selected outside the repository's `global.json` scope.
- Contract tests: 13 passed, 0 failed.
- Built-in UI startup smoke test: failed because `App.RunSmokeTest` expects settings format version 10 while `ApplicationSettings.Normalize` emits version 11.
- Normal repository-root build command: cannot start on a machine without SDK 10.0.302 because `global.json` uses `latestPatch` roll-forward.

### Known defects recorded before dev.123

All items below were unfixed at the dev.122 audit. The first five were reproduced by independent synthetic-data probes in the 2026-08-30 audit and subsequently fixed in dev.123. Read the current snapshot above for remaining defects.

| Priority | Defect and observed impact |
|---|---|
| P1 | Opening another PDF/project in the same window lacks a save/discard/cancel check. After editing OCR text and opening another PDF, the dirty flag became false and the previous edits and Undo history were lost. The window-close prompt exists but is not used by the shared document-opening path. Save manually before switching. |
| P1 | Empty-string OCR edits cannot be represented reliably: empty `EditedText` also means “not edited.” Changing `ABCDEF` to empty, saving and reopening restored `ABCDEF`. The same effective-text fallback is used by export; empty-text round-trip/export acceptance is not met. |
| P1 | Synchronizing loaded overlays back to the project drops or normalizes region metadata. In the probe, `ParentRegionId` became null, `FitMode` changed from `Distribute` to `Automatic`, and disabled search/copy/speech/PDF output flags all became true. `FlowDirection` and explicit-writing-mode metadata are also normalized by the mapping. This affects existing project attributes even where the UI does not expose them. |
| P2 | OCR quality-analysis keyword-width correction bypasses the review-mode geometry guard. With review active and `CanEditGeometry=false`, the correction changed a test region's width from 720 to 1080. The analysis window has an execution confirmation; this is not an unprompted automatic change. Ordinary drag/resize controls are guarded, but review mode is not a universal geometry lock. |
| P2 | Bulk replace assigns `Modified`, whereas FR-700 requires `NeedsReview`. Replacing `ABC` with `XYZ` removed the region from the default unreviewed/needs-review list. Use all statuses to revisit it; the requirement remains needs-review and has not been relaxed to match the bug. |

- Page insertion, deletion, reordering, and rotation are non-Undo operations even though FR-104 requires Undo; adopting the changed working PDF also clears the earlier overlay Undo history.
- New project manifests write `minimumApplicationVersion` and `applicationVersion` as fixed `0.1.0` values instead of tracking the application version. The project-format example documents these actual values, not completion of version tracking.

### Audit and documentation reconciliation on 2026-08-30

- Re-ran the packaged dev.122 build dated `20260830-143634`: 136 document-UI, 67 file-launch and 69 review checks passed. All 13 contract tests also passed (285 checks in total).
- Separate probes of the current ViewModel/storage path reproduced the five defects above using generated PDFs/projects. Those probes are not permanent regression coverage in the passing suites. The legacy `--smoke-test` still exited with -1 because its expected settings version is 10, not 11.
- Updated the normative Markdown for review navigation/filtering, selection scrolling, properties and shared UI styles, zoom behavior, file launch/icons, timestamped builds, current test counts, and known limitations. Historical milestone counts below are intentionally unchanged.
- This reconciliation changes documentation only. It does not fix source code, register Windows associations, rebuild the application, or republish the 2026-08-09 design PDF. Passing focused checks do not establish the full Version 1.0 quality gate; see the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md).

### Remaining Version 1.0 gaps recorded in dev.122

- Google Vision integration, in-application OCR execution, and a replaceable OCR-provider contract.
- Ruby editing/association UI; comments, user tags, attribute-diff display, hierarchical page/paragraph/document review aggregation, and a separate audit history. The implemented review list/count is current-page based; cross-page navigation does not implement those aggregates.
- Project migration, repair, read-only/rescue modes, and versioned JSON Schemas.
- Plugin abstraction/package contract.
- Idle-triggered autosave after 30 seconds; the current implementation is interval based and skips projects without a save path, so never-saved projects are not automatically protected.
- Command palette, workspace presets/docking, settings/shortcut import-export, export-strategy selection, comprehensive PDF input-characteristic warnings, and the full multi-engine output validation matrix.

## Historical milestone notes

The test counts and verification statements in this section record what was true at each milestone. They are not the current verification result; use the current repository snapshot above for current status.

### v1.0.0-dev.104

- Added repeated-region propagation for page headers, page footers, running titles, page numbers, and other recurring OCR regions.
- Added target selection for selected pages, an explicit page range, or the whole document.
- Added similarity matching based on normalized page position, geometry, writing direction, and OCR text; changing page-number digit runs do not prevent a match.
- Added a review window that lists every detected page, match score, text, lock state, and an individual apply checkbox before any page is changed.
- Added two propagation modes: reproduce the edited split/geometry/character advances, or delete the matching regions.
- Added an option to preserve each target page's text while transferring layout edits, so changing page numbers and running titles remain intact.
- Excluded geometry-locked regions and character-locked regions from both automatic application and accidental overwrite.
- Made the multi-page result one Undo/Redo operation and renumbered reading order after replacement or deletion.
- Added progress reporting and cancellation for long whole-document searches, plus Japanese and English UI strings.
- Verified the Release build with zero warnings and zero errors; all 10 contract tests and the UI startup smoke test pass.

### v1.0.0-dev.103

- Added an application setting for character-edit box thickness (0.25–2.0 px, default 0.8 px).
- Made character-cell and character-mode selection borders retain a constant on-screen thickness independently of zoom.
- Preserved distinct emphasis for selected, row-selected, and locked character cells while deriving every thickness from one setting.
- Added Japanese and English labels and persisted the new setting with backward-compatible normalization.
- Verified the Release build with zero warnings and zero errors; all 10 contract tests and the UI startup smoke test pass.

### v1.0.0-dev.102

- Added whole-document OCR quality analysis for suspicious character-count outliers among similarly sized text regions.
- Added adjustable size tolerance, minimum peer count, and character-count ratio thresholds.
- Added a candidate list that navigates directly to the affected page and OCR region for visual review.
- Added keyword width analysis using the median normalized width of repeated occurrences.
- Separated horizontal and vertical keyword reference ratios so that different writing directions are never mixed.
- Added undoable correction for selected or all keyword-width candidates while preserving locked regions and locked characters.
- Added Japanese and English UI resources for the OCR quality analysis window.
- Added regression coverage for character-count anomalies, keyword-width deviations, mixed writing directions, and existing contracts.
- Verified the Release build with zero warnings and zero errors; all 10 contract tests pass.

### v1.0.0-dev.40

- Unified line and character-cell extents while retaining proportional character-width ratios.
- Preserved character-mode multi-selection when an already-selected line is clicked.
- Applied equalize, restore, and image-assisted character sizing to every selected OCR line as one undoable edit.
- Added horizontal, vertical, multi-line, Undo, actual-project, and PDF-output regression coverage.

## Completed foundation

- Solution and project boundaries
- Core geometry and OCR entities
- Non-destructive original/edited state separation
- Review status and output flags
- Undo/redo history primitive
- `.pdfocrproj` manifest, ZIP container, JSON serialization, validation, safe temporary save, backup
- Source PDF SHA-256 reference
- Portable/AppData path strategy
- Diagnostic file logging
- WPF shell and open/save project flow
- Explicit WPF startup pipeline, startup crash reporting, and headless UI smoke test
- Local PDF preview rendering through PDFium
- PDF page count, page navigator selection, previous/next navigation, and scrollable page display
- External and embedded project source-PDF resolution for preview
- UTF-8 PDF path support, including Japanese file names
- PDF text-object extraction and semi-transparent red OCR overlays
- Invisible text detection for text rendering mode 3 and zero/near-zero fill alpha
- Character-level extraction fallback when text-object traversal does not expose regions
- NDLOCR-Lite companion-file auto-discovery and manual import
- Official NDLOCR-Lite JSON coordinate import and PAGE/LINE XML coordinate import
- OCR overlay visibility toggle, source display, and per-page region count
- Selectable OCR regions and a selected-region property editor
- OCR text, X/Y position, width, and height editing
- Drag-to-move and lower-right resize handle
- Undo/redo for text and geometry changes
- 25-400% preview zoom, toolbar controls, Ctrl+mouse-wheel, and Ctrl+0 reset
- Edited OCR text and geometry round-trip through `.pdfocrproj`
- Japanese primary menu, toolbar, navigator, status, and property labels
- Reduced preview margins and automatic/manual fit-width display
- Eight-direction resize handles with page-boundary and minimum-size enforcement
- Image-assisted proportional character-width estimation with rotation/vertical-writing rectification, confidence reporting, and Undo/Redo
- Line-height-aware minimum character advances and selected-region-only character-cell visualization
- Per-cell foreground-ink validation, maximum character advances, and persisted estimator tuning controls
- NDLOCR vertical-writing metadata restoration for legacy projects and image-content endpoint fitting
- Contract tests without external packages (13 currently passing)

## Historical foundation verification baseline

The following figures are retained from the early foundation milestone and have been superseded by the current verification section at the top of this file.

- Toolchain: Visual Studio Community 2026 18.8 / MSBuild 18.8 / .NET SDK 10.0.302
- Target framework: .NET 8 (Version 1.0 compatibility baseline)
- Full solution build: passed with 0 warnings and 0 errors
- Contract tests: 6 passed, 0 failed
- PDF render integration test: 43-page PDF opened; first page rendered at 1200 x 1698 pixels
- Existing OCR PDF integration test: page 3 rendered with 523 extracted text regions
- NDLOCR-Lite official-schema fixture test: 2 page records loaded; 2 coordinate regions loaded on page 1
- Editor behavior test: zoom bounds plus text/geometry undo/redo passed
- Character-width estimation regressions: unequal synthetic glyph widths and dark bold glyphs on a light background detected while preserving the intended extent
- Real-project analysis regression: 7 vertical page-1 regions restored; `の手術` remains proportional; page-3 title/body minimum advance ratios remain plausible; page-6 prose has zero empty non-whitespace cells and bounded maximum advances; an oversized page-9 short line fitted to visible content with Undo restoration
- Interaction geometry test: fit-width calculation and all 8 resize directions passed
- Project persistence test: edited Japanese text and geometry saved and reopened successfully
- Verified scenarios: rotated vertical geometry, immutable source OCR text, project round-trip, missing manifest diagnostics, changed source fingerprint detection, Portable storage selection, WPF layout initialization, PDFium rendering, Japanese PDF paths

## Next implementation slice

1. Fix lossless OCR-region round-trip, including empty text and all persisted attributes, and add ViewModel-to-project regression tests.
2. Repair the UI startup smoke test and make it part of the repeatable verification command.
3. Decide whether page structure operations will become Undoable or whether FR-104 will be revised through the design process.
4. Align SDK selection, manifest application versions, release metadata, and documentation publication.
5. Implement or explicitly defer the remaining Version 1.0 gaps listed above.

## Known constraints

- Japanese/English UI switching is implemented, but complete localization coverage still requires release verification.
- NDLOCR-Lite JSON and conventional PAGE/LINE XML provide coordinate overlays. TXT and TEI are associated as companion metadata but do not yet provide overlay geometry.
- Edited output PDF generation, rotation controls, page thumbnails, and preview fit modes are implemented.
- Project format migration, structured repair, read-only/rescue modes, and JSON Schema publication remain unimplemented.
- The plugin contract is intentionally deferred until the primary PDF/OCR boundary is validated.
