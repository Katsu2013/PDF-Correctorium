# Implementation status

## Current repository snapshot: v1.0.0-dev.122

The packaged build in this workspace identifies itself as `v1.0.0-dev.122`. This section is the current implementation summary; the numbered sections below are retained as historical milestone notes and are not a complete changelog from dev.105 through dev.122.

### Implemented since the original foundation milestone

- Separate edited-PDF export with an isolated worker process, validation, and safe output commit.
- OCR-region rotation, writing-direction and review-state editing, character-level advances and locks, line split/merge, alignment, and reading-order operations.
- Fit-width, fit-height, fit-page, and fit-selection preview modes.
- Asynchronous page thumbnails with project cache persistence.
- Page insertion, deletion, reordering, and 90-degree rotation through a non-destructive working PDF.
- Search/replace, repeated-region propagation, and OCR quality analysis across a document.
- Editable/importable/exportable bookmarks, document viewer settings, document properties, and image optimization.
- Japanese/English UI switching, expanded display/edit/save settings, autosave, versioned backups, and backup restoration.

### Verification performed on 2026-08-29

- Release solution build: passed with 0 warnings and 0 errors when SDK 10.0.400 was selected outside the repository's `global.json` scope.
- Contract tests: 12 passed, 0 failed.
- Built-in UI startup smoke test: failed because `App.RunSmokeTest` expects settings format version 10 while `ApplicationSettings.Normalize` emits version 11.
- Normal repository-root build command: cannot start on a machine without SDK 10.0.302 because `global.json` uses `latestPatch` roll-forward.

### Known implementation defects

- Empty-string OCR edits cannot be represented reliably: an empty `EditedText` is also used as the “not edited” sentinel, so deleting all text through search/replace can restore the original text during save/export.
- Synchronizing loaded overlays back to the project currently drops or normalizes `ParentRegionId`, `FitMode`, `OutputAttributes`, `FlowDirection`, and explicit-writing-mode metadata.
- Page insertion, deletion, reordering, and rotation are non-Undo operations even though FR-104 requires Undo.
- New project manifests currently write `minimumApplicationVersion` and `applicationVersion` as `0.1.0`; the design example specifies `1.0.0`.

### Remaining Version 1.0 gaps

- Google Vision integration, in-application OCR execution, and a replaceable OCR-provider contract.
- Ruby editing/association UI; comments, user tags, attribute-diff display, and a separate audit history.
- Project migration, repair, read-only/rescue modes, and versioned JSON Schemas.
- Plugin abstraction/package contract.
- Idle-triggered autosave after 30 seconds; the current implementation is interval based.
- Command palette, workspace presets/docking, comprehensive PDF input-characteristic warnings, and the full multi-engine output validation matrix.

## Historical milestone notes

The test counts and verification statements in this section record what was true at each milestone. They are not the current verification result; use the dev.122 section above for current status.

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
- Contract tests without external packages (12 currently passing)

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
