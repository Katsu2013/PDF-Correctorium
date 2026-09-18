# Implementation status

## Current repository snapshot: v1.0.0-dev.171

### Exact editor-to-PDF text-redaction geometry (2026-09-18)

- Text-selection rectangles are now painted from the exact PDF-coordinate band stored by the editor. Export no longer replaces the reviewed band with newly calculated glyph, baseline or font metrics, and it adds no hidden paint padding to text-selection bands. Position, width and height therefore remain the same between the translucent/opaque editor preview and the exported PDF.
- Security remains independent from presentation geometry: selected PDF character codes are still removed from their original `Tj`/`TJ` commands by centre-in-band matching, and post-export validation still rejects any extractable character left in a protected band. Images, paths and out-of-range text remain native.
- The user's seven page-10 bands were compared numerically after export. The stored blue-heading range `135.415–146.514 × 492.567–503.384` was emitted within SVG float quantization (`135.453–146.543`, with the same 10.817-point height); all other bands likewise retained their editor dimensions, including the `IDE` range and the orange title range.
- The permanent redaction diagnostic now rejects any export-time position, size or padding change to a stored text-selection band. The adjacent-glyph pointer-centre test from dev.170 remains in place.
- Final verification completed with a warning-free Release build, 28 contract tests, 17 source-versioning checks, 18 published-versioning checks, the source and packaged redaction diagnostics, and the packaged smoke diagnostic. The user's 205-page project was exported from both the source build and the portable build; both outputs retained seven native redaction paths at the same coordinates and passed qpdf syntax and stream validation. Evidence is stored under `outputs/.verification/dev171-final-20260918-121500`.

Product version is `1.0.0-dev.171` and numeric version is `1.0.0.171`. Project format remains 1.6. Git metadata remains unavailable to the publication script, so no commit or dirty-state claim is made.

## Previous repository snapshot: v1.0.0-dev.170

### Baseline- and pointer-aligned text redaction (2026-09-18)

- PDF output now derives a text-selection band’s vertical bounds only from characters in the same original text object and on the same baseline. A neighbouring Japanese run or a taller font can no longer enlarge a short Latin selection such as `IDE`; the reproduced page-10 range decreased from approximately 14.1 points to 6.1 points while retaining the selected glyphs’ measured horizontal bounds.
- Horizontal text dragging now uses the unexpanded pointer range and character centres along the writing direction. The perpendicular hit area can still be expanded for an almost-zero-height drag, but merely touching an adjacent glyph box no longer selects the glyph to its left.
- The permanent redaction diagnostic covers adjacent-glyph hit testing, preservation of measured glyph width, exclusion of a taller neighbouring text object and compact same-object baseline metrics. The user’s seven-range page-10 project completed through the structure-preserving path, removed 41 selected characters from seven original text objects, kept 56 out-of-range characters in their original commands, retained native page structure, rendered cleanly and passed qpdf syntax/stream validation.
- Release compilation and the isolated redaction diagnostic pass with no warnings or errors. Project format remains 1.6.

Product version is `1.0.0-dev.170` and numeric version is `1.0.0.170`. Git metadata remains unavailable to the publication script, so no commit or dirty-state claim is made.

## Previous repository snapshot: v1.0.0-dev.169

### Same-line text-redaction normalization and consolidation (2026-09-17)

- Separately selected characters now use the full source-PDF line as their vertical reference. Capitals, short glyphs and descenders therefore receive the same top, bottom and preview padding when selected one at a time; only the horizontal extent follows the selected characters.
- Overlapping text-selection redactions are consolidated regardless of colour, with the newest selection colour winning. Same-colour ranges that touch or are separated only by normal glyph spacing on the same line are joined into one continuous marker. Re-selecting the same character no longer leaves stacked redaction objects.
- Consolidation runs both while editing and immediately before export. Existing projects therefore receive the fix even when exported without first reopening redaction mode. Entering redaction mode also consolidates the current page as one Undo-able action.
- The user's saved page-10 project exported without rasterization. Six consolidated ranges removed 36 selected characters from six original text objects while retaining 39 out-of-range characters in their original commands; page 10 retained 15 fonts and 8 source image/mask entries, rendered cleanly and passed qpdf syntax/stream validation.
- Release compilation and the expanded isolated redaction diagnostic pass with no warnings or errors. The diagnostic covers equal line height for separately selected capital/descender glyphs, adjacent-range joining, duplicate suppression, newest-colour precedence and export-time cleanup of saved ranges.

Product version is `1.0.0-dev.169` and numeric version is `1.0.0.169`. Project format remains 1.6. Git metadata remains unavailable to the publication script, so no commit or dirty-state claim is made.

## Previous repository snapshot: v1.0.0-dev.168

### Exact glyph-bound text redaction and word-processor selection interaction (2026-09-17)

- Text-selection export no longer uses the padded preview band to decide which PDF characters are removed. A character is selected only when its center lies inside the saved text band, and the opaque output rectangle is tightened again from the selected characters' native PDF coordinates. This prevents overlapping glyph boxes, preview rounding and marker padding from deleting or covering the preceding/following character.
- Text marker horizontal preview padding is limited to 0.25–0.75 pixels and final PDF safety padding to 0.1 point. The user's saved three-band recovery project exported page 10 without rasterizing it: 29 selected characters were removed from 3 original text objects, 16 out-of-range characters stayed in the original commands, and the surrounding `「`, `CLI`, `ら`, `を`, `に` and `をそ` remained extractable and visible. qpdf reported no syntax or stream errors.
- A text drag now gets a line hit area even when its Y movement is zero or nearly zero; vertical text receives the corresponding narrow X hit area. The hit-area expansion is used only to find characters and never becomes an output rectangle. A four-pixel pointer movement is sufficient for text selection, while ordinary rectangle redaction retains its eight-by-eight minimum.
- PDF text selection uses the I-beam cursor. Automated redaction diagnostics cover zero-height horizontal drags and exact selected-versus-remaining visible character counts; document-UI diagnostics cover the effective I-beam cursor. Final packaged evidence is recorded under `outputs/.verification/dev168-final-20260917-01`.

Product version is `1.0.0-dev.168` and numeric version is `1.0.0.168`. Git metadata remains unavailable to the publication script, so no commit or dirty-state claim is made.

## Previous repository snapshot: v1.0.0-dev.167

### Guaranteed structure-preserving PDF text redaction and compact bands (2026-09-17)

- PDF text-selection redactions never fall back to a full-page bitmap. The exporter marks each original PDF text command, then removes only the selected encoded glyphs from its `Tj`/`TJ` operand and adds native opaque rectangle objects. Hexadecimal and escaped literal PDF strings are both supported. Fonts, text matrices, glyph advances, images, paths and unselected text commands remain native; an unsupported structure aborts before the destination is committed.
- The user's 205-page source and four saved text-selection redactions were exported through the isolated worker. Page 10 retained all 15 font resources and the same 8 source image/mask entries, added no full-page raster image, removed 29 selected characters from 7 original text objects, kept 19 out-of-range characters in their original commands, rendered with the original page colours, and passed searchable-text validation plus qpdf syntax/stream validation.
- Text selection is shown as compact, line-aligned marker bands while dragging. Bands use 3% vertical padding clamped to 0.5–1.2 preview pixels and a 0.5-point export safety margin, so tightly spaced neighbouring lines are not covered. Text bands cannot be moved or resized; creation clears the selection automatically, clicking a band reselects it for deletion, clicking blank page space clears it, and Escape clears only the selected band before leaving redaction mode.
- Release builds complete with no warnings or errors. Contract 28, document-UI 193, source versioning 17 and dependency-lock checks pass. The isolated redaction diagnostic covers compact-band conversion, immediate deselection, non-resizable text bands, hexadecimal and literal PDF text commands, structure-preserving partial text, native object counts, protected-range validation, shaped-range fallback and reopen. Final packaged evidence is recorded under `outputs/.verification/dev167-final-20260917-01`; the exact timestamped portable directory and source fingerprint are recorded by its `build-info.json` and reported at handoff.

Product version is `1.0.0-dev.167` and numeric version is `1.0.0.167`. Git metadata remains unavailable to the publication script, so no commit or dirty-state claim is made.

## Previous repository snapshot: v1.0.0-dev.165

### Structure-preserving PDF text redaction and native visible-text preview (2026-09-16)

- Text-selection redactions on ordinary PDF page text now remove the intersecting direct text objects and append opaque PDF path rectangles without flattening the page. Partially intersected text objects are split into surviving fragments and rebuilt with the source font, fill color, render mode and original character-union bounds; images, vector paths and unrelated text objects remain native.
- The structure-preserving path is deliberately limited to direct, unrotated fill/invisible text with no page annotations. Arbitrary rectangles, polygons, freehand ranges, form-contained text, rotated/sheared text and special stroke/clip rendering continue through the 300 DPI safety fallback. Post-export character-boundary validation and qpdf compaction remain mandatory for both paths.
- The PDF editor no longer turns ordinary visible page text into editable OCR overlay boxes. PDFium continues to render the complete page, while the OCR overlay now contains only genuinely invisible text. Visible and invisible characters remain independently available to PDF text-selection redaction.
- The isolated redaction diagnostic now exports a structure-preserving text-selection result before its existing shaped-redaction fallback result, verifies that native path/image counts are retained and that the exporter reports the structural path, then reopens and validates the fallback output as before.
- The fixed mixed-content fixture contains a native background image, vector path, visible text and invisible OCR. A partial selection replaced only the leading visible/invisible characters, retained the remaining visible `E TEXT`, and preserved direct page objects (5 text, 3 paths and 1 image in the packaged output). Both the structure-preserving and shaped-fallback PDFs rendered correctly, contained no extractable character inside protected ranges, contained no verbatim original full text after qpdf reconstruction and passed qpdf syntax/stream checks. Results: `outputs/.verification/dev165-persistence-20260916-01`, `outputs/.verification/dev165-redaction-partial-20260916-01` and `outputs/.verification/dev165-packaged-20260916-01`.
- The repository-root Release solution build completed with no warnings or errors. Contract 28, persistence 59, source versioning 17 and published versioning 18 checks passed; the packaged smoke diagnostic also exited 0. Versioning results: `outputs/.verification/dev165-versioning-final-20260916-01` and `outputs/.verification/dev165-versioning-published-20260916-01`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.165-win-x64-20260916-160106`. Product version `1.0.0-dev.165`, numeric version `1.0.0.165`, title/About, distribution label and principal binaries agree. SDK 10.0.400; source fingerprint `D4A8DA73ED4213BF52DDD7CEFC0A7447A80C62A70ADA8589BE0A4D7CE419FF8D`. Git metadata was unavailable to the publication script, so no commit or dirty-state claim is made.

## Previous repository snapshot: v1.0.0-dev.164

### Marker-style PDF text redaction and expanded safety coverage (2026-09-16)

- Source-PDF text selection now merges selected character boxes by visual line and creates one continuous, padded marker-style band per line. One drag remains one Undo operation, while the status message reports both the selected character count and generated band count.
- Text-selection marks receive a 2.5-point export safety margin in addition to their stored padded bounds. Raster painting, invisible searchable-text exclusion and post-export character-boundary validation all use the same margin. Existing projects containing the earlier character-sized text-selection marks also receive the larger margin when exported.
- The redaction diagnostic checks that synthetic characters on two lines become exactly two padded bands, that actual source-PDF characters produce fewer line bands than selected characters and remain one Undo operation, and that text-selection marks use the larger export safety margin before exercising the isolated PDF export and searchable-text validation path.
- The repository-root Release build completed with no warnings or errors. Contract 28, document-UI 193 and pre-publication versioning 17 checks passed. The isolated redaction output retained searchable OCR outside the protected range, rejected searchable characters inside it, stored the flattened page at 300 DPI, reopened and rendered, and passed qpdf syntax/stream validation. Source results: `outputs/.verification/dev164-final-20260916-134611977`.

### PDF text, polygon and freehand redaction input (2026-09-15)

- Redaction input now supports four explicit methods: rectangle, source-PDF text selection, polygon and freehand. PDF text selection uses both visible and invisible character boxes from the rendered source page and creates only the intersecting character marks as one undoable operation. OCR-region redaction remains available.
- Polygon input finishes on double-click or Enter. Freehand input closes the dragged trace and simplifies dense input to at most 256 points. Escape cancels only an unfinished outline before returning to the existing mode-cancel behavior.
- Project format 1.6 persists `shapeKind` and PDF-coordinate `pathPoints`. Validation rejects invalid kinds, non-finite or out-of-page points, paths outside the 3-to-4,096-point limit and bounds that disagree with the calculated outline. Formats 1.0 through 1.5 remain readable and legacy rectangle marks retain their prior meaning.
- A shared Core geometry service supplies compatible rectangle paths, bounds, point containment and rectangle intersection. Preview, searchable-OCR exclusion and post-export validation therefore use the same saved outline. Non-rectangular output uses a bounded scanline fill; legacy rectangles retain the fast rectangular path.
- The repository-root Release solution build completed with no warnings or errors. Contract 28, document-UI 193 and the expanded isolated redaction diagnostic passed. The diagnostic proved visible source-PDF text selection, single-step Undo, bounded freehand simplification, polygon package round-trip and polygon PDF output. Source result: `outputs/.verification/dev163-focused-20260915-220654242`.
- The design PDF was regenerated as 114 pages from the dev.163 Markdown and 12 existing SVG diagrams. Its metadata reports dev.163, qpdf found no syntax or stream errors, all 114 pages rendered at a common size with no blank page, and representative cover and final diagram pages rendered cleanly. Result: `outputs/.verification/design-pdf-dev163-certified-20260915-221248922`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.163-win-x64-20260915-220810`. Product version `1.0.0-dev.163`, numeric version `1.0.0.163`, title/About, distribution label and principal binaries agree. Source fingerprint `EC89AB06AEE2AE2A3AAEB2540517F828C8EFF2FC58E4BEFA8598D6667FAE2AD5`. Pre-publication versioning 17 and published versioning 18 checks passed. Packaged document-UI 193, shaped-redaction output and qpdf checks passed at `outputs/.verification/packaged-dev163-20260915-220835670`; published versioning result: `outputs/.verification/versioning-dev163-published-20260915-220835670`.

## Previous repository snapshot: v1.0.0-dev.161

### Character-precise redaction and large-document export acceleration (2026-09-15)

- When a redaction intersects only part of an invisible OCR text object, the exporter now removes the intersecting characters plus the existing one-point safety margin instead of discarding the entire line. Unaffected text objects remain unchanged; the left and right fragments of a partially intersecting object are rebuilt as invisible searchable text with the original font, color and union of the surviving character bounds.
- The post-export security check still examines every extracted character box and refuses to finalize any output that leaves searchable text in a protected area. Objects or glyphs that cannot be represented safely are omitted rather than retained speculatively. The permanent isolated diagnostic now proves that both sides of `LEFT0123456789RIGHT` survive while the covered centre does not.
- Full-page image analysis now classifies row, column and internal-grid occupancy in one pixel pass. The managed pixel buffer is processed in independent row bands while all PDFium document/object calls remain serialized. Each source bitmap is also created once and reused for crop/JPEG candidates, and the common highest-quality candidate is tried before the bounded fallback search.
- A production-equivalent project containing 450 pages, 14,275 modified OCR regions, 443 applicable image optimizations and one redaction completed under the 2 GiB worker limit in 153.6 seconds. The same 118,694,840-byte result previously took 164.4 seconds immediately before row-band parallelization (about 6.6% faster); it reopened, rendered and passed qpdf. Page 6 retained searchable text on both sides of the redaction and no searchable character inside it. Result: `outputs/.verification/dev161-actual-project-export-20260915-parallel`.
- Focused isolated redaction and image-optimization diagnostics passed, and all three outputs passed qpdf. Result: `outputs/.verification/dev161-focused-20260915-002740857`.
- The repository-root Release solution build completed from the existing locked restore with no warnings or errors. Contract 28, document-UI 190, keyboard 4,074, persistence 59, page-history 39, review 69, OCR-rendering 29, settings 101, recent-files 76, file-launch 80, pre-publication versioning 17 and dependency-lock 2 checks passed. Editor and smoke diagnostics exited 0. Results: `outputs/.verification/dev161-regression-20260915-002928569` and `outputs/.verification/versioning-dev161-20260915-003102115`.
- The design PDF was regenerated as 112 pages from the dev.161 Markdown and 12 SVG diagrams. qpdf, all-page rendering at a common size, extracted-text presence on every page and representative visual review passed. Result: `outputs/.verification/design-pdf-dev161-certified-20260915-072252452`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.161-win-x64-20260915-072011`. Product version `1.0.0-dev.161`, numeric version `1.0.0.161`, title/About, distribution label and principal binaries agree. SDK 10.0.400; source fingerprint `3D03721BF0F9A9D971573443552AC5384ECAF4B83E896E56DC79239A6242CD8F`. Published versioning 18 and dependency-lock checks passed. Packaged document-UI 190, keyboard 4,074, settings 101, isolated character-level redaction and smoke diagnostics passed; the generated redaction PDF also passed qpdf. Results: `outputs/.verification/dev161-packaged-20260915-072102415` and `outputs/.verification/versioning-dev161-published-20260915-072033483`.

## Previous repository snapshot: v1.0.0-dev.160

### Searchable OCR preservation and higher-quality redaction output (2026-09-14)

- Redacted pages no longer discard edited invisible OCR outside every marked range. The exporter preserves only invisible text objects (`Tr=3` or zero fill alpha) that stay outside every redaction plus its one-point safety margin; any text line touching a redaction is removed as a whole.
- Output validation now checks every extracted character boundary against the redaction ranges. Searchable text elsewhere on the page is therefore allowed, while any extractable character in a protected range still rejects the output.
- Redacted pages bypass ordinary page-image optimization so their source imagery is not JPEG-encoded twice. Flattening now uses 300 DPI and JPEG quality 99, bounded at 14,000 pixels on the long edge and approximately 96 megapixels for predictable worker memory use.
- The permanent isolated redaction diagnostic adds edited invisible OCR outside the marked area, enables page-image optimization to detect accidental double processing, and requires the OCR to survive, the optimized-image count to remain zero, the redaction color to be present and the flattened image to be at least 295 DPI.
- Focused source verification produced a 2,481 x 3,508 image at 300 DPI, preserved the edited `PDFPDF` OCR text outside the redaction, removed extractable text from the protected area, skipped the page-image optimization, reopened and rendered the PDF, and passed qpdf. Result: `outputs/.verification/dev160-redaction-certified-20260914-221423433`.

- Release compilation completed with no warnings or errors. Contract 28, document-UI 190, keyboard 4,074, persistence 59, page-history 39, review 69, OCR-rendering 29, settings 101, recent-files 76, file-launch 80, pre-publication versioning 17 and dependency-lock 2 checks passed. Editor and smoke diagnostics exited 0. Result: `outputs/.verification/dev160-regression-20260914-221606852`; versioning result: `outputs/.verification/versioning-20260914-221738914`.
- The design PDF was regenerated as 111 pages from the dev.160 Markdown and 12 SVG diagrams. qpdf, 111-page rendering, same-size validation, near-blank detection and representative visual review passed. Result: `outputs/.verification/design-pdf-dev160-final-20260914-222313927`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.160-win-x64-20260914-221819`. Product version `1.0.0-dev.160`, numeric version `1.0.0.160`, title/About, distribution label and principal binaries agree. SDK 10.0.400; source fingerprint `5097A55BD2D02D067AC49954D54F2F5F069E83733868AE9BE45404F63DD11EF3`. Published versioning 18 and dependency-lock checks passed. Packaged document-UI 190, keyboard 4,074, settings 101, redaction and smoke diagnostics passed; its redaction output retained `PDFPDF`, stored a 2,481 x 3,508 image at 300 DPI and passed qpdf. Result: `outputs/.verification/dev160-packaged-20260914-221902710`; published versioning result: `outputs/.verification/versioning-20260914-221836277`.

## Previous repository snapshot: v1.0.0-dev.159

### Bounded-memory large-PDF export and observable progress (2026-09-14)

- PDFium no longer retains all modified page and image resources until a large export finishes. The worker checkpoints after at most 96 pages or when process private memory reaches 768 MiB, compacts the checkpoint with qpdf, reopens it, verifies the page count and then continues. This keeps headroom below the existing 2 GiB per-process safety limit without weakening resource isolation.
- Checkpoint/cleanup work is reported as an explicit progress phase. Character-spacing fallback rows also advance progress instead of appearing stalled, while repeated fallback warnings are summarized by total count and up to eight samples rather than producing multi-megabyte UI/state/log payloads.
- The permanent project-export diagnostic now uses the same isolated worker and output-commit path as the interactive UI instead of calling the in-process exporter directly.
- The previously failing 450-page project with 14,275 modified regions, 444 image-optimized pages and one redaction completed under the production 2 GiB job limit in 158.1 seconds. The 118,059,146-byte output reopened as 450 pages, rendered and passed qpdf; the diagnostic log shrank from approximately 1.5 MiB to 1,446 bytes. Result: `outputs/.verification/dev159-actual-project-export-final-20260914-095105731`.

- Release compilation completed with no warnings or errors. Contract 28, document-UI 190, keyboard 4,074, persistence 59, page-history 39, review 69, OCR-rendering 29, settings 101, recent-files 76, file-launch 80, pre-publication versioning 17 and dependency-lock 2 checks passed. Editor, smoke, output-commit, isolated redaction and the production-equivalent 450-page project export exited 0. Results: `outputs/.verification/dev159-regression-20260914-152614645`, `outputs/.verification/dev159-versioning-20260914-152541062`, `outputs/.verification/dev159-redaction-20260914-152735509`, `outputs/.verification/dev159-actual-project-export-final-20260914-095105731`.
- The design PDF was regenerated as 110 pages from the dev.159 Markdown and 12 SVG diagrams. qpdf, 110-page rendering, same-size validation, near-blank detection and representative visual review passed.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.159-win-x64-20260914-152918`. Product version `1.0.0-dev.159`, numeric version `1.0.0.159`, title/About, distribution label and principal binaries agree. SDK 10.0.400; source fingerprint `3B4348A0A2961EB0FD20148FC928F697489A8E1CCE3D825D3B08E0FC45768D21`. Packaged document-UI 190, keyboard 4,074, settings 101, smoke, dependency lock and published versioning 18 checks passed. The packaged binary also completed the same 450-page production-equivalent export in 165.4 seconds and its output passed qpdf. Results: `outputs/.verification/dev159-packaged-20260914-153013409` and `outputs/.verification/dev159-packaged-versioning-20260914-152943314`.

## Previous repository snapshot: v1.0.0-dev.158

### Portable-project redaction export recovery and output finalization safety (2026-09-14)

- Fixed the interactive isolated-export path for portable projects. The temporary worker package now carries a validated relative transport reference to the exact prepared PDF instead of trying to save an embedded-source project with embedding disabled and no relative path. This resolves `sourcePath.missing: A relative project must contain a relative PDF path.` without duplicating a large source PDF.
- The worker independently verifies the explicit source file size and SHA-256 against the transport package before editing. A changed or substituted source is rejected before output begins.
- Progress and terminal worker states now pass through one serialized writer and unique temporary files. This removes the race in which a progress notification and completion notification could both replace `state.json.tmp`, causing `UnauthorizedAccessException` after a successfully generated PDF.
- PDF output replacement uses operation-owned staging and backup files. A successful replacement no longer leaves a fixed `.bak` containing the previous PDF, overwrites no user-managed backup, and is not reported as failed merely because an antivirus scanner briefly holds the already-committed cache copy.
- Export now rejects any result where fewer redaction marks were applied than requested. Cleanup failures can no longer hide the original generation or validation error.
- Release compilation completed with no warnings or errors. Contract 28, document-UI 190, keyboard 4,074, persistence 59, page-history 39, review 69, OCR-rendering 29, settings 101, recent-files 76, pre-publication versioning 17 and dependency-lock 2 checks passed. Editor, smoke, output-commit and isolated redaction diagnostics exited 0. Results: `outputs/.verification/dev158-regression-20260914-091604523`, `outputs/.verification/dev158-focused-20260914-091245711`, `outputs/.verification/dev158-versioning-final-20260914-092128462`.
- The original 450-page, 130,064,014-byte source together with its portable autosave project exported 14,275 modified regions, 444 optimized images and one redaction on one page to a 118,219,112-byte PDF. The redacted page changed from 1,101 extracted text characters to no content beyond the page delimiter, rendered successfully, and both the focused and actual outputs passed qpdf. Results: `outputs/.verification/dev158-actual-redaction-20260914-090352687`.
- The design PDF was regenerated as 109 pages from the dev.158 Markdown and 12 SVG diagrams. qpdf, full-page rendering and near-blank detection passed; results: `outputs/.verification/design-pdf-dev158-certified-20260914-092443624`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.158-win-x64-20260914-092151`. Product version `1.0.0-dev.158`, numeric version `1.0.0.158`, title/About, distribution label and principal binaries agree. SDK 10.0.400; source fingerprint `3A493628B6C926FAEC107FCB3176B1D0B511BCD80490BB3206DD0419E0E07B26`. Packaged document-UI 190, keyboard 4,074, settings 101, isolated redaction, smoke, dependency lock and published versioning 18 checks passed at `outputs/.verification/packaged-dev158-20260914-092223431` and `outputs/.verification/packaged-versioning-dev158-20260914-092301011`.

## Previous repository snapshot: v1.0.0-dev.157

### Redaction eyedropper, direct Delete and unselected wheel scrolling (2026-09-13)

- A redaction selected on the page or in the current-page list can now be removed with the Delete key regardless of which non-text control has focus. Text boxes and password fields retain normal character deletion, preventing a color-field edit from deleting the selected mark.
- The redaction property pane adds a one-shot eyedropper. It samples the underlying current-page bitmap rather than overlay visuals, converts preview coordinates to source pixels, and applies the RGB color to the selected mark or to the next mark when none is selected. Escape cancels only the eyedropper before the normal redaction-mode Escape behavior.
- In every editor mode, an ordinary mouse wheel over the work area scrolls the preview when neither OCR nor redaction content is selected. System line/page scroll settings are respected and offsets remain bounded. Ctrl+wheel continues to control zoom.
- Clicking empty page space before drawing a new redaction clears the previous redaction selection, making the unselected scrolling state explicit and avoiding accidental edits to the previous mark.
- Release compilation completed with no warnings or errors. Repository-root verification passed contract 28, document-UI 190, keyboard 4,074, persistence 59, page-history 39, review 69, OCR-rendering 29, settings 101, recent-files 76, pre-publication versioning 17 and dependency-lock 2 checks. Editor, direct redaction export/reopen and smoke diagnostics exited 0; qpdf found no syntax or stream errors in the redaction output and regenerated 107-page design PDF. Results: `outputs/.verification/dev157-final-20260913-231820293` and `outputs/.verification/design-pdf-dev157-final-20260913-232623421`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.157-win-x64-20260913-232218`. Product version `1.0.0-dev.157`, numeric version `1.0.0.157`, title/About, distribution label and principal binaries agree. SDK 10.0.400; source fingerprint `67208597005BD8E4D34DCFDBE3FDA4B8E238C9692A0ABA499BDD1A9F5A3BA27B`. Packaged document-UI 190, keyboard 4,074, settings 101, direct redaction, smoke, dependency lock and published versioning 18 checks passed at `outputs/.verification/packaged-dev157-20260913-232246756`.

## Previous repository snapshot: v1.0.0-dev.156

### Editable redaction appearance and removal (2026-09-13)

- Clicking an existing redaction on the page or in the current-page list selects it. Changing the color text or a preset now updates that selected mark, persists the new RGB value and records one Undo/Redo edit; with no selection, the same controls continue to set the color for the next mark.
- The redaction property pane now offers translucent and opaque editor-preview modes. This is deliberately session-only UI state: PDF export remains fully opaque in the mark's stored color and the project format remains 1.5.
- The move Thumb now has a transparent visual template, so it no longer covers a black redaction with the default white WPF Thumb surface. The preview border itself supplies the selected color.
- The Edit menu and property pane expose an explicit selected-redaction delete action. Page clicks establish selection before dragging, Delete remains keyboard-operable through the focused Thumb, and deletion is restorable through Undo.
- Release compilation completed with no warnings or errors. The focused WPF document diagnostic passes 187 checks and keyboard diagnostics pass 4,074 checks, including selected-color persistence/Undo, true black fill, opacity switching, selection-enabled deletion and deletion Undo.

Repository-root verification passed contract 28, document-UI 187, keyboard 4,074, persistence 59, page-history 39, review 69, OCR-rendering 29, settings 101, recent-files 76, pre-publication versioning 17 and dependency-lock 2 checks. Editor, direct redaction export/reopen and smoke diagnostics exited 0; qpdf found no syntax or stream errors in the regenerated 104-page design PDF. Results: `outputs/.verification/final-dev156-20260913-222734052`, focused UI results: `outputs/.verification/dev156-redaction-20260913-210016338`, final document render results: `outputs/.verification/design-pdf-dev156-final-20260913-223649622`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.156-win-x64-20260913-223316`. Product version `1.0.0-dev.156`, numeric version `1.0.0.156`, title/About, distribution label and principal binaries agree. SDK 10.0.400; source fingerprint `3182D89AEB6DE290BE58F6E8B9D0F72CAD06B3A202CA2B922EE357B68DAD9316`. Packaged document-UI 187, keyboard 4,074, settings 101, direct redaction, smoke, dependency lock and published versioning 18 checks passed at `outputs/.verification/packaged-dev156-20260913-223343836`, `outputs/.verification/packaged-settings-dev156-retry-20260913-223445004`, and `outputs/.verification/packaged-redaction-dev156-20260913-223503912`.

## Previous repository snapshot: v1.0.0-dev.155

### Atomic facing-page navigation (2026-09-13)

- Previous/next navigation in page-by-page facing mode now renders the destination page and its companion before changing the selected page. The currently complete spread therefore stays visible during preparation instead of temporarily collapsing to one page.
- The prepared main and companion previews are applied in the same dispatcher turn. Existing OCR interaction remains attached only to the selected main page, and the old companion is removed without leaving a stale page slot.
- Changing the document, page selection, layout, flow, cover placement or binding direction cancels obsolete preparation. Foreground and background PDF workers prepare the editable and passive pages independently without blocking the WPF dispatcher.
- Release compilation completed with no warnings or errors. Contract tests pass 28/28 and the settings/facing-view diagnostic passes 101 checks, including retention of the current spread during preparation and atomic replacement.

Source verification: `outputs/.verification/settings-dev155-final-20260913092016172`, `outputs/.verification/document-ui-dev155-20260913093112863`, and `outputs/.verification/versioning-dev155-20260913093112863`. Source smoke exited 0.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.155-win-x64-20260913-183150`. Product version `1.0.0-dev.155`, numeric version `1.0.0.155`, title/About, distribution label and principal binaries agree. SDK 10.0.400; source fingerprint `B71CCFEFF694E6BEF5D47381EDD1F91FF9F36341D29BD5BC0732D8A03C93EFAF`. Packaged settings/facing-view 101, smoke and published versioning 18 checks passed at `outputs/.verification/packaged-settings-dev155-20260913093213181` and `outputs/.verification/packaged-versioning-dev155-20260913093213181`.

## Previous repository snapshot: v1.0.0-dev.154

### Adjustable redaction geometry (2026-09-13)

- A redaction remains editable after creation. Clicking or dragging its filled area selects and moves it; eight edge/corner handles resize it within the current page, with an 8-pixel minimum preview size.
- Pointer movement changes only the lightweight overlay. The project model and Undo stack are updated once when the drag finishes, avoiding one history entry and one project rewrite per mouse event.
- Move/resize is recorded in PDF-point coordinates through the existing project-annotation history. Undo/Redo therefore restores exact redaction geometry, and canceling an interrupted drag restores the persisted bounds.
- The redaction layer accepts input only while redaction mode is active. Returning to OCR editing makes the layer non-interactive so it cannot block OCR selection or geometry editing.
- Release compilation completed with no warnings or errors. The WPF document diagnostic passes 179 checks, including eight resize handles, move/resize bounds, deferred persistence and one-entry Undo/Redo.

Repository-root verification passed contract 28, document-UI 179, keyboard 4,070, persistence 59, page-history 39, review 69, OCR-rendering 29, settings 99, recent-files 76, pre-publication versioning 17 and dependency-lock 2 checks. Editor and smoke diagnostics exited 0. Direct redaction project/export/reopen also exited 0 and qpdf found no syntax or stream errors. Results: `outputs/.verification/final-dev154-20260913-171428158`, `outputs/.verification/redaction-dev154-20260913-171612137`, and `outputs/.verification/versioning-dev154-20260913-171548811`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.154-win-x64-20260913-171719`. Product version `1.0.0-dev.154`, numeric version `1.0.0.154`, title/About, project-manifest application version, distribution label and managed/native dependency metadata agree. SDK 10.0.400; source fingerprint `E19C3397926BC528FCEEC92EBE1EE0FBD979A069B4717F2122E25C0D227D0854`. Packaged document-UI 179, keyboard 4,070, redaction, smoke and published versioning 18 checks passed at `outputs/.verification/packaged-dev154-20260913-171752733`.

## Previous repository snapshot: v1.0.0-dev.153

### Exclusive OCR-editing and redaction modes (2026-09-13)

- The editor mode is now a typed, mutually exclusive state with OCR editing, reading-order editing, review, and redaction values. Redaction is no longer an independent temporary flag layered on top of OCR editing.
- The toolbar exposes dedicated OCR-editing and redaction selectors, and the existing mode dropdown contains all four modes. Their selected state always follows the same ViewModel value.
- Entering redaction mode disables OCR-region creation, deletion, movement, resizing, rotation and character-width operations. The pointer becomes a redaction crosshair, consecutive drags can mark multiple ranges, and Escape returns to OCR editing.
- The right pane changes between OCR properties and redaction properties. Redaction mode shows only its instructions, color, preselected-text action and current-page redaction list; OCR selection/property editors are hidden.
- Release compilation completed with no warnings or errors. The WPF document diagnostic passes 171 checks, including exclusive mode state, command guards, synchronized toolbar/dropdown state, dedicated property-pane visibility and consecutive marking. Project reload preserves the active interaction mode, while the no-document state disables every document operation.

### Secure redaction and project format 1.5 (2026-09-13)

- Users can create redaction ranges from selected characters, selected OCR regions, or an arbitrary dragged rectangle. The right pane and Edit menu expose color input and presets, range creation, current-page range selection/removal, and current-page clearing.
- Redaction edits share the existing project annotation Undo/Redo history. Range geometry uses PDF point coordinates and follows stable page IDs rather than transient page numbers.
- PDF output treats a redacted page as a security boundary: it renders the completed page, paints the requested ranges with a one-point safety margin, removes the page's original text, image and annotation objects, and writes one replacement page image. Output validation rejects a redacted page if any extractable characters remain.
- This conservative method intentionally removes search/copy and existing annotations from the whole affected page. The editable project, source PDF, autosaves and backups are not sanitized and can retain confidential data; only the separately exported redacted PDF is intended for distribution.
- Project format 1.5 adds the persisted `Redactions` collection and requires application dev.152. Formats 1.0–1.4 remain readable. Current product/numeric versions are 1.0.0-dev.153 / 1.0.0.153; no data-format change was needed for the UI-only mode refinement.

Repository-root Release build succeeded with 0 warnings/errors. Contract 28, document-UI 171, keyboard 4,070, persistence 59, page-history 39, review 69, OCR-rendering 29, settings 99, recent-files 76, pre-publication versioning 17 and dependency-lock 2 checks passed. Editor and smoke diagnostics exited 0. Direct redaction save/Undo/Redo/export/reopen succeeded and qpdf found no syntax or stream errors in the result. Source results: `outputs/.verification/final-dev153-20260913-153426304`; redaction results: `outputs/.verification/redaction-dev153-20260913-153607604`; versioning results: `outputs/.verification/versioning-dev153-20260913-153532080`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.153-win-x64-20260913-153621`. Product version `1.0.0-dev.153`, numeric version `1.0.0.153`, title/About, project-manifest application version, distribution label and managed/native dependency metadata agree. SDK 10.0.400; source fingerprint `A5877F074F8214A5CF7630BDED652BB0DC3CEBEA0A97584F98866944CCEC0F0D`. Packaged versioning 18, document-UI 171, keyboard 4,070, redaction and smoke checks passed at `outputs/.verification/packaged-dev153-20260913-153653038`. The design PDF was regenerated from current Markdown and nine SVG figures as a dev.153 snapshot; every page was rendered for visual QA and qpdf found no syntax or stream errors.

## Previous repository snapshot: v1.0.0-dev.151

### Reliability, concurrency and verification hardening (2026-09-12)

- The non-interactive page-render diagnostic now awaits the isolated PDF worker asynchronously instead of synchronously blocking the WPF dispatcher. Its real PDF-to-PNG path completes and shuts down normally.
- Explicit current-page rendering uses the foreground PDF worker; thumbnails, adjacent/continuous-page previews and document-wide scan rendering use a separate background worker. Background look-ahead can no longer hold the interactive render queue.
- Startup diagnostics write one process-specific log with cross-reader sharing and never prevent startup when a log destination becomes unavailable. Concurrent application instances no longer race on a single daily file.
- Application settings saves are serialized across processes and merge only fields changed since each service loaded its baseline. A stale instance therefore preserves unrelated settings saved by another instance.
- Page working-file cleanup no longer treats a newly created session directory as abandoned before its owner creates the lock file. A grace period and open-only lock probing prevent concurrent launches from deleting one another's initialization state.
- Cancellation now reaches document search and quality-scan page loading. PDF native result codes that affect visibility, render mode, color and mark names are checked instead of silently accepting invalid data.
- The standard `dotnet test` solution command executes the dependency-free contract runner, preventing a zero-test success. Formatting policy now matches the repository's LF source files, and disposable cancellation/stream resources are released consistently.
- Product/numeric versions are 1.0.0-dev.151 / 1.0.0.151. Project format 1.4, minimum application version dev.143, and application-settings format 16 are unchanged because no persisted schema changed. The dev.150 portable candidate was rejected before commit or release when its concurrent-launch check exposed the page-session initialization race; changed source therefore advanced to dev.151.

Repository-root Release build succeeded with 0 warnings/errors. Standard `dotnet test` executed all 27 contract checks. Document-UI 151, OCR-rendering 29, recent-files 76, review 69, page-history 39, file-launch 80, persistence 59, settings/concurrency 99, keyboard 4,050, pre-publication versioning 17 and dependency-lock 2 checks passed; editor, render and source smoke diagnostics exited 0. Direct PDF export, project save/reopen/export and bookmark output passed, and qpdf found no syntax or stream errors in all three outputs. Two independent 32-process concurrent smoke runs completed without failure; the recorded run produced 32 unique startup logs. Results: `outputs/.verification/final-dev151-20260912-105239109`, versioning results: `outputs/.verification/versioning-dev151-20260912-105357878`.

The certified portable output is produced by `tools/BuildPortable.ps1` under the timestamped `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.151-win-x64-*` directory. `GetBuildVersion.ps1 -PublishDirectory` and published dependency/version checks must agree with product version `1.0.0-dev.151`, numeric version `1.0.0.151`, the distribution label and `build-info.json`; the exact path and fingerprint are taken from that immutable build record.

## Previous repository snapshot: v1.0.0-dev.149

### PDF initial view and project-specific editor view (2026-09-12)

- Opening a PDF now reads Catalog `/PageLayout` and `/ViewerPreferences /Direction`. The first editor layout uses the document's single/facing and page-by-page/continuous combination, cover placement, and binding direction; missing entries use the PDF defaults of Single Page and L2R.
- An intentional editor layout, flow, cover, or binding change is stored as the optional project `EditorViewState`, marks the project unsaved, and is restored on reopening. Merely applying a PDF's initial view or a saved project view does not create an override or mark the project modified.
- Document Properties can now select continuous facing pages, which writes `/TwoColumnLeft` or `/TwoColumnRight`; the existing four combinations continue to use bounded lazy rendering and do not create a working PDF.
- Product/numeric versions are 1.0.0-dev.149 / 1.0.0.149. Project format 1.4, minimum application version dev.143, and application-settings format 16 are unchanged because the project field is optional.

Repository-root Release build succeeded with 0 warnings/errors. 27 contract, 151 document-UI, 4,050 keyboard, 98 settings/layout, 59 persistence and 17 pre-publication version checks passed; source smoke exited 0. The new coverage reads missing and explicit PDF catalog view settings, saves an intentional project override, restores it without a dirty state, and exposes Continuous Facing Pages in Document Properties. Results: `outputs/.verification/final-dev149-20260912-042250874`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.149-win-x64-20260912-042510`. The packaged document-UI 151, settings 98 and persistence 59 checks passed, packaged smoke exited 0, and 18 published version/dependency checks passed at `outputs/.verification/packaged-dev149-20260912-042536200`. Product version `1.0.0-dev.149`, numeric version `1.0.0.149`, distribution label and binary metadata agree. SDK 10.0.400; source fingerprint `D8EE5E71F24D03C5FDC666ECC32F8B3A76CD64D6509DCBCFFAC7BC723EAEC08D`.

## Previous repository snapshot: v1.0.0-dev.148

### Independent page layout and continuous facing spreads (2026-09-11)

- Page layout (Single Page / Facing Pages) and flow (Page by Page / Continuous Scrolling) are now independent settings, so all four combinations are available from the View menu and toolbar. Settings format 16 migrates the former Continuous Pages value to Single Page plus Continuous Scrolling.
- A missing cover partner or unpaired final page reserves its position but draws no white page surface, border, or shadow. Page-by-page and continuous facing views use the same cover/binding calculator.
- Continuous facing spreads reuse the bounded virtualized layout and at most 12 viewport-neighbor images. Changing layout, cover, or binding cancels stale work and does not rebuild, copy, or mark the source PDF/project as modified. Only the current page remains the interactive OCR editing surface.
- Product/numeric versions are 1.0.0-dev.148 / 1.0.0.148. Project format 1.4 and minimum application version dev.143 are unchanged.

Repository-root Release build succeeded with 0 warnings/errors. 27 contract, 150 document-UI, 4,050 keyboard, 98 settings/layout and 17 pre-publication version checks passed; source smoke exited 0. The settings suite covers legacy migration, the four independent layout/flow combinations, undrawn empty sides, both bindings, bounded lazy rendering, and preserved editing/Undo state. Results: `outputs/.verification/final-dev148-20260911-195325189`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.148-win-x64-20260911-195430`. The packaged settings diagnostic repeated all 98 checks, packaged smoke exited 0, and 18 published version/dependency checks passed at `outputs/.verification/packaged-dev148-20260911-195506170`. Product version `1.0.0-dev.148`, numeric version `1.0.0.148`, distribution label and binary metadata agree. SDK 10.0.400; source fingerprint `DD247243453FD12A5FFE2B5A40B881D826CFB1C019312205BC15F1AE98B1C301`.

## Previous repository snapshot: v1.0.0-dev.147

### Facing-page cover and binding controls (2026-09-11)

- Facing Pages now independently supports showing page 1 as a separate cover and choosing left or right binding. Left binding places the earlier page on the left and a separate cover on the right; right binding mirrors the spread and cover. An unpaired first or final page receives a blank opposite side.
- The shared, side-effect-free `FacingPageLayoutCalculator` drives both WPF placement and companion-page lazy rendering. Changing the options cancels stale companion rendering and never rebuilds or copies the source PDF.
- Options are available in View > Page Display > Facing-page Settings and Settings > Display. They persist in application settings format 15, normalize unknown binding values to left binding, and remain independent from project format 1.4 and exported-PDF viewer preferences.
- Product/numeric versions are 1.0.0-dev.147 / 1.0.0.147.

Repository-root Release build succeeded with 0 warnings/errors. 27 contract, 150 document-UI, 3,922 keyboard, 94 settings/facing-view and 17 pre-publication version checks passed; source smoke exited 0. The settings diagnostic covers all four cover/binding combinations, companion navigation, an unpaired final page, persistence, unknown-value fallback, and unchanged edit/Undo state. Results: `outputs/.verification/final-dev147-20260911-142240685`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.147-win-x64-20260911-142615`. The packaged settings diagnostic repeated all 94 checks, packaged smoke exited 0, and 18 published version/dependency checks passed at `outputs/.verification/packaged-dev147-20260911-142636719`. Product version `1.0.0-dev.147`, numeric version `1.0.0.147`, distribution label and all five managed/application binary versions agree. SDK 10.0.400; source fingerprint `7D1ECC323711203D1D7DDF16C75C037F2ADF70B5950D6F8D04AEB54720ADFC23`. Git metadata is unavailable in this workspace, so Git tracking/commit/push were not performed.

## Older repository snapshot: v1.0.0-dev.146

Dev.146 introduced seamless full-document Continuous Pages using lightweight layout slots and a bounded 12-image viewport cache. Repository-root Release build succeeded with 0 warnings/errors. 27 contract, 150 document-UI, 3,868 keyboard, 86 settings/continuous-view and 17 pre-publication version checks passed; source smoke exited 0. Results: `outputs/.verification/final-dev146-20260911-125638498`.

Its certified portable output is `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.146-win-x64-20260911-125840`. The packaged settings diagnostic repeated all 86 checks, packaged smoke exited 0, and 18 published version/dependency checks passed at `outputs/.verification/packaged-dev146-20260911-125901341`. Source fingerprint: `42453ACA3E30501813D1854AF7D213C4F2E517C90B3979DE355F055EA381F019`.

## Older repository snapshot: v1.0.0-dev.145

Dev.145 introduced the Single Page, bounded neighboring-page Continuous Pages, and Facing Pages layouts. Its certified portable output was `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.145-win-x64-20260911-040357`; verification details remain in the test and operations records. Dev.146 supersedes its three-page Continuous Pages implementation.

## Older repository snapshot: v1.0.0-dev.143

### Logical page composition and bounded regenerated data (2026-09-10)

- Page deletion, reordering and 90-degree rotation now update a stable-ID logical page sequence instead of generating a complete working PDF for every edit and Undo/Redo state. Preview, OCR geometry, bookmarks, comments, tags and internal links follow the logical pages. External-page insertion and final export materialize the physical PDF once at the required operation boundary.
- Project format 1.4 makes `project.json` canonical and stops writing duplicate `source/source-reference.json`, per-page OCR JSON and regenerable thumbnails. Embedded PDFs are stored without futile ZIP recompression. Formats 1.0–1.3 remain readable; newly saved 1.4 projects require dev.143 or later.
- Normal-mode saves leave stable external PDFs in place. Only session-owned physical PDFs, such as an external-page insertion result, are persisted to `.assets`; unreferenced SHA-256-named assets are reclaimed only after scanning the current project, autosave, versioned backups and pre-recovery copies.
- Embedded-source materializations are limited by LRU order to 16 PDFs / 4 GiB. Versioned backups are limited by configured count and 4 GiB, pre-recovery copies to one, and duplicate fixed `.bak` creation has ended. In-memory thumbnails are limited to 64 entries and generated only within 32 pages of the current page; moving elsewhere refreshes that neighborhood.
- Character-spacing QDF updates use a single streaming marker index and range-copy replacement rather than reading the full QDF into managed memory. Search and quality analysis render only 96-pixel-wide scan previews. Page-history snapshots no longer retain regenerable thumbnail byte arrays.
- Product/numeric versions are 1.0.0-dev.143 / 1.0.0.143.

Final verification: repository-root Release build succeeded with 0 warnings/errors. 27 contract, 38 logical page-history, 55 persistence, 29 OCR-rendering, 17 pre-publication version and 18 published version/dependency checks passed; the editor diagnostic (including streaming QDF replacement), packaged 38-check page-history, packaged 55-check persistence and packaged smoke diagnostics all exited 0. Results: `outputs/.verification/logical-page-dev143-20260910-194529133`, `outputs/.verification/storage-dev143-20260910-194552508`, `outputs/.verification/rendering-dev143-20260910-194552508`, `outputs/.verification/versioning-dev143-20260910-194641576`, `outputs/.verification/packaged-logical-page-dev143-20260910-194833357`, `outputs/.verification/packaged-storage-dev143-20260910-194833357`, `outputs/.verification/versioning-published-dev143-20260910-194751694`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.143-win-x64-20260910-194723`. Product version `1.0.0-dev.143`, numeric version `1.0.0.143`, distribution label and all five managed/application binary versions agree. SDK 10.0.400; source fingerprint `70C7F7CD4D198A7185B74DE60E4D90E3FD3F3D7B931385120F646D1A5FD82788`. Git metadata is unavailable in this workspace, so Git tracking/commit/push were not performed.

## Previous repository snapshot: v1.0.0-dev.142

### Bounded page-edit working history (2026-09-10)

- Page insertion, deletion, reordering and rotation working PDFs are now governed by a dedicated retention policy: at most 12 session-owned PDFs and 1 GiB in total, including the current working PDF. The current file remains usable when it alone exceeds the byte budget; an additional revision is retained only when it fits.
- Retention keeps one contiguous newest prefix of the shared OCR/page Undo timeline. It never removes an entry from the middle, which would make earlier OCR state inconsistent with the page structure. Unreachable PDFs are reclaimed immediately after trimming.
- Shared before/after paths are counted once. Missing session-owned files form a safe history boundary rather than leaving a future failing Undo entry. The policy is separated from the ViewModel and accepts injected limits for deterministic diagnostics and future replacement by logical page-state materialization.
- The page-history diagnostic now covers default-limit integration plus independent file-count, byte-count, shared-resource, missing-resource and immediate-reclamation decisions. Product/numeric versions: 1.0.0-dev.142 / 1.0.0.142. Project format remains 1.3 and minimum reader remains dev.137 because no persisted schema changed.

Final verification: repository-root Release build succeeded with 0 warnings/errors. 24 contract, 37 page-history, 79 settings, 55 persistence and 18 published version/dependency checks passed, as did source and packaged smoke diagnostics. The packaged page-history diagnostic repeated all 37 checks against the delivered binaries. Results: `outputs/.verification/page-history-dev142-1789022594295`, `outputs/.verification/settings-dev142-1789022640417`, `outputs/.verification/persistence-dev142-1789022640417`, `outputs/.verification/versioning-dev142-20260910-154428512`, `outputs/.verification/packaged-page-history-dev142-1789022754168`, `outputs/.verification/versioning-published-dev142-20260910-154614273`.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.142-win-x64-20260910-154528`. Product version `1.0.0-dev.142`, numeric version `1.0.0.142`, distribution label and all five managed/application binary versions agree. SDK 10.0.400; source fingerprint `C7E04484B06409B8334EAF261F62B5DB98186CDC9289DB361BFDFE80E9C9F82F`. Git metadata remained unavailable, so Git tracking/commit/push were not performed.

## Previous repository snapshot: v1.0.0-dev.141

### OCR overlay rendering costs (2026-09-10)

- Replaced per-character Border/Viewbox/TextBlock visual trees with one `OcrCharacterLayer` per OCR region. Glyph layout caches are element-owned and bounded; selection, locks, search highlights, Unicode cells and zoom-aware border widths are preserved.
- Cache only the current text segmentation and immutable cell snapshot. Text, geometry, selection, lock, search and undo/redo changes invalidate display data. Repeated unchanged reads do not allocate new cell arrays.
- Instantiate the eight region resize handles only while their region is selected in line/paragraph mode outside review mode. Drag and undo still use the existing editing commands.
- Added `--ocr-rendering-test` for mixed-script image equivalence, cache invalidation, actual editor bindings/resize events, and a 20,000-character synthetic cost comparison. The existing editor diagnostic also needed fixture cleanup: character-adjustment sample lines were contaminating a later two-line reading-order assertion. The same failure was reproduced with isolated dev.140 binaries before correcting the fixture.
- Details, measurement limits and remaining rendering work: [OCR-RENDERING.md](OCR-RENDERING.md). Product/numeric versions: 1.0.0-dev.141 / 1.0.0.141. Project format remains 1.3, minimum reader dev.137.

Final verification: repository-root Release build succeeded with 0 warnings/errors. 24 contract, 29 rendering, 150 document-UI, 69 review, 55 persistence, 79 settings, 3792 keyboard and 18 version/dependency checks passed, as did the editor diagnostic. Packaged rendering (29), persistence (55) and smoke diagnostics exited 0. Document-UI assertions cover title/About version strings; contract checks cover assembly and saved-manifest application versions. Results: `outputs/.verification/final-dev141-20260910-084845378`, `outputs/.verification/versioning-20260910-085123260`, `outputs/.verification/packaged-dev141-20260910-085121375`.

Synthetic 250-region / 20,000-character rendering: 80,251 visual objects reduced to 251; initial construction/layout/drawing 4,333ms to 643ms; cumulative managed allocations 321,021,016 to 83,248,160 bytes. These are isolated component measurements, not end-to-end PDF speed or resident-memory figures. Repeated unchanged cell reads allocated 0 bytes; mixed-script baseline image difference was 0.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.141-win-x64-20260910-085048`. Product version `1.0.0-dev.141`, numeric version `1.0.0.141`, distribution label and all five managed/application binary versions agree. SDK 10.0.400; source fingerprint `C34BD4504CE131CA4676571E5392B1FF4653A735C840667D769C39E5E9F335CB`. This workspace is still not recognized as a Git repository; Git tracking/commit/push were not performed.

## Previous repository snapshot: v1.0.0-dev.140

### Reading-order badge foreground layer (2026-09-10)

- Reading-order number badges are rendered in a dedicated, non-interactive foreground layer above all OCR region bodies, selection borders and resize handles. Adjacent regions can no longer cover the sequence number.
- Badges use an opaque teal fill and a white outline so the number remains legible over the PDF and selection chrome at every zoom level.
- Product/numeric versions: 1.0.0-dev.140 / 1.0.0.140. Project format remains 1.3 and its minimum reader remains dev.137 because this is a presentation-only correction.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 24 contract, 3792 keyboard, 150 document-UI, 55 persistence, 69 review, 29 page-history, 76 recent-file, 79 settings, 80 file-launch and 18 version/dependency-management checks passed (4372 total). The document-UI suite includes a selected region with its upper-left resize handle visible plus foreground-layer, non-interactive, opaque-fill and contrasting-outline assertions. Packaged persistence (55 checks) and smoke diagnostics both exited 0.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.140-win-x64-20260910-013719`. Product version `1.0.0-dev.140`, numeric version `1.0.0.140`, distribution label and all five managed/application binary versions agree. The 15 native dependency records are present in `build-info.json`. SDK 10.0.400; source fingerprint `9C6E7EDCA2B288A0C1D601123452191FE580ED8E581682E592813215A75343D9`. Results are under `outputs/.verification/final-dev140-20260910-013514593`, `outputs/.verification/versioning-20260910-014100167` and `outputs/.verification/packaged-dev140-20260910-013838980`.

## Previous repository snapshot: v1.0.0-dev.139

### User-facing project storage terminology (2026-09-10)

- Project PDF storage is now presented consistently as `Portable mode` for a PDF embedded in the project and `Normal mode` for a source PDF referenced by relative path. Document Properties, the status bar and Save Project As use the same names.
- The Save Project As mnemonics are now Alt+P for Portable mode and Alt+N for Normal mode. Internal manifest values remain `Embedded` and `Relative`, so the data format and existing projects are unchanged.
- Product/numeric versions: 1.0.0-dev.139 / 1.0.0.139. Project format remains 1.3 and its minimum reader remains dev.137.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 24 contract, 3792 keyboard, 146 document-UI, 55 persistence, 69 review, 29 page-history, 76 recent-file, 79 settings, 80 file-launch and 18 version/dependency-management checks passed (4368 total). The settings suite encountered one transient file-lock failure during its first combined run and passed all 79 checks in a new isolated output folder. Packaged persistence (55 checks) and smoke diagnostics both exited 0.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.139-win-x64-20260910-010451`. Product version `1.0.0-dev.139`, numeric version `1.0.0.139`, distribution label and all five managed/application binary versions agree. The 15 native dependency records are present in `build-info.json`. SDK 10.0.400; source fingerprint `4103E0B097BF13B6589B89F4F73298CEA0D65723BE978204982EB918A3597E57`. Results are under `outputs/.verification/final-dev139-20260910-005935071`, `outputs/.verification/dev139-settings-retry-20260910-010211410`, `outputs/.verification/dev139-file-launch-20260910-010247802`, `outputs/.verification/versioning-20260910-010532886` and `outputs/.verification/packaged-dev139-20260910-010530306`.

## Previous repository snapshot: v1.0.0-dev.138

### Project storage labeling and status (2026-09-09)

- Application data placement and project PDF storage are now distinct UI concepts. Portable/installed application-data placement is exposed only in Settings > Manage as `Application data storage mode`.
- Document Properties labels the project value as `PDF storage` and reports the then-current Embedded / self-contained or Relative reference wording. The status bar reports the same project value, remains hidden without an open document, and updates immediately when Save As converts the project storage form.
- Product/numeric versions: 1.0.0-dev.138 / 1.0.0.138. Project format remains 1.3 and its minimum reader remains dev.137 because this revision changes presentation, not persisted data compatibility.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 24 contract, 3792 keyboard, 144 document-UI, 55 persistence, 69 review, 29 page-history, 76 recent-file, 79 settings, 80 file-launch and 18 version/dependency-management checks passed (4366 total). Packaged persistence (55 checks) and smoke diagnostics both exited 0.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.138-win-x64-20260909-171102`. Product version `1.0.0-dev.138`, numeric version `1.0.0.138`, distribution label and all five managed/application binary versions agree. The 15 native dependency records are present in `build-info.json`. SDK 10.0.400; source fingerprint `FE2EB9DC63837D72C8EDFABFAB343A0D494107DC2F7872622F2E5D52CC5845F8`. Results are under `outputs/.verification/final-dev138-20260909-170841984`, `outputs/.verification/versioning-dev138-20260909-171032713`, `outputs/.verification/versioning-20260909-171140033` and `outputs/.verification/packaged-dev138-20260909-171138730`.

## Previous repository snapshot: v1.0.0-dev.137

### Non-copying relative project references (2026-09-09)

- Saving an ordinary external PDF as a relative-reference project now records the PDF's path relative to the selected project location. It does not copy the PDF and does not create an adjacent `.assets` directory.
- An adjacent `<project-name>.assets` directory is created only when an embedded source is converted to relative storage or a page-structure edit has produced a transient working PDF that must be made durable.
- Save As recalculates an existing external reference from the new project location. Cross-drive relative references are rejected with guidance to choose embedded storage or a same-drive project location; the application does not silently duplicate the PDF.
- Project format 1.3 permits normalized parent-directory segments in an external relative reference while continuing to reject rooted, drive-qualified, empty and `.` segments. Formats 1.0–1.2 remain readable; the minimum reader for newly saved 1.3 packages is dev.137.
- Product/numeric versions: 1.0.0-dev.137 / 1.0.0.137.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 24 contract, 3792 keyboard, 139 document-UI, 55 persistence, 69 review, 29 page-history, 76 recent-file, 79 settings, 80 file-launch and 18 version/dependency-management checks passed (4361 total). Packaged persistence and smoke diagnostics both exited 0. The packaged test project records `..\source.pdf`, has no absolute path hint, resolves to the original PDF by fingerprint, and creates no `.assets` directory for normal relative saves.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.137-win-x64-20260909-140946`. Product version `1.0.0-dev.137`, numeric version `1.0.0.137`, distribution label and all five managed/application binary versions agree. The 15 native dependency records are present in `build-info.json`. SDK 10.0.400; source fingerprint `D07EE1412B59F93243982E8FC79DF53DC7EDBD8953B4997AAADC3B0B91C7FE93`. Results are under `outputs/.verification/final-dev137-20260909-140745220`, `outputs/.verification/versioning-20260909-140904954` and `outputs/.verification/packaged-dev137-20260909-141031745`.

## Previous repository snapshot: v1.0.0-dev.136

### Project storage selection on Save As (2026-09-09)

- Save Project As now opens a localized, keyboard-accessible storage-options window after the destination path is selected. Embedded and relative-reference storage can be selected independently from portable/installed operation.
- Newly opened PDFs still default to embedded storage in portable operation and relative storage in installed operation. Ordinary overwrite saves preserve the project mode; Save As can convert in either direction.
- Embedded output is verified to contain `source/document.pdf`. Relative output stores a SHA-256-named PDF in the adjacent `.assets` directory and does not duplicate the PDF in the project package. Former `.assets` directories are not deleted automatically during conversion.
- Product/numeric versions: 1.0.0-dev.136 / 1.0.0.136. Project format remains 1.2 and its minimum reader remains dev.133.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 23 contract, 3792 keyboard, 139 document-UI, 50 persistence, 69 review, 29 page-history, 76 recent-file, 79 settings, 80 file-launch and 18 version/dependency-management checks passed (4355 total). Packaged persistence and smoke diagnostics both exited 0. The storage UI selection, both defaults, and all four operation-mode/storage-mode combinations were exercised.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.136-win-x64-20260909-021904`. Product version `1.0.0-dev.136`, numeric version `1.0.0.136`, distribution label and all five managed/application binary versions agree. The 15 native dependency records are present in `build-info.json`. SDK 10.0.400; source fingerprint `909D65DA8F877525893CA3964965EA6344679A083C58422ACBA79B7D68BAC129`. Results are under `outputs/.verification/final-dev136-20260909-021720790`, `outputs/.verification/dev136-keyboard-20260909-021518138`, `outputs/.verification/versioning-20260909-021930393` and `outputs/.verification/packaged-dev136-20260909-021942070`.

## Previous repository snapshot: v1.0.0-dev.135

### Stable project-format compatibility metadata (2026-09-08)

- Project format 1.2 now records its actual minimum reader, `1.0.0-dev.133`, independently from the current saving build. `applicationVersion` continues to record the exact build that saved the package.
- The portable embedding correction from dev.134 remains in effect and is covered by package-content diagnostics.
- Product/numeric versions: 1.0.0-dev.135 / 1.0.0.135. Project format remains 1.2 and its minimum reader remains dev.133.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 23 contract, 3776 keyboard, 139 document-UI, 43 persistence, 69 review, 29 page-history, 76 recent-file, 79 settings, 80 file-launch and 18 version/dependency-management checks passed (4332 total). Packaged persistence and smoke diagnostics both exited 0. The packaged project contains `source/document.pdf`; its manifest records minimum reader dev.133 and saving build dev.135.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.135-win-x64-20260908-010928`. Product version `1.0.0-dev.135`, numeric version `1.0.0.135`, distribution label and all five managed/application binary versions agree. The 15 native dependency records are present in `build-info.json`. SDK 10.0.400; source fingerprint `8F1ADA2EB47C8B0D1AE4213356CAB8BA9FCE4FCE039419EF099D0762D57D928A`. Results are under `outputs/.verification/final-dev135-20260908-010825709`, `outputs/.verification/versioning-20260908-010957421` and `outputs/.verification/packaged-dev135-20260908-011007893`.

## Superseded intermediate snapshot: v1.0.0-dev.134

### Portable-project embedding correction (2026-09-08)

- Project storage is now determined by the application mode rather than a save-dialog default: portable operation always embeds the current working PDF as `source/document.pdf`; installed operation always stores a same-tree relative reference in the adjacent `<project-name>.assets` directory.
- Re-saving a formerly linked project in portable operation converts it to embedded storage. The obsolete storage-choice dialog was removed so a portable project cannot accidentally remain linked.
- Persistence diagnostics inspect the ZIP package itself for `source/document.pdf` and separately confirm that installed-mode projects remain relative and do not contain the embedded-PDF entry.
- Product/numeric versions: 1.0.0-dev.134 / 1.0.0.134. Project format remains 1.2 and its minimum reader remains dev.133; this behavior correction does not change the data schema.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 23 contract, 3776 keyboard, 139 document-UI, 43 persistence, 69 review, 29 page-history, 76 recent-file, 79 settings, 80 file-launch and 18 version/dependency-management checks passed (4332 total). Packaged persistence and smoke diagnostics both exited 0. The persistence checks directly opened the produced project ZIP and confirmed `source/document.pdf`; the installed-mode control package confirmed that entry was absent.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.134-win-x64-20260908-005647`. Product version `1.0.0-dev.134`, numeric version `1.0.0.134`, distribution label and all five managed/application binary versions agree. The 15 native dependency records are present in `build-info.json`. SDK 10.0.400; source fingerprint `06A0A25664AC55F15A7DB2E584527AC6487137CCC42EC46BF0A4EB064328F396`. Results are under `outputs/.verification/final-dev134-20260908-005438390`, `outputs/.verification/versioning-20260908-005718834` and `outputs/.verification/packaged-dev134-20260908-005730688`.

## Previous repository snapshot: v1.0.0-dev.133

### Project storage, input notices, comments/tags and page links (2026-09-07)

- Project format 1.2 explicitly supports self-contained PDF embedding and linked storage. Linked projects keep a SHA-256-named PDF in an adjacent `<project-name>.assets` directory and persist only a same-tree relative path; rooted, drive-qualified, dot-segment and escaping paths are rejected. Portable operation defaults to embedded storage and installed operation defaults to linked storage. Formats 1.0 and 1.1 remain readable.
- Added bounded, heuristic input-PDF notices for encryption/security state, non-embedded fonts, AcroForm/XFA, JavaScript, embedded files, optional content, signatures, launch actions and incremental updates. This is not cryptographic signature verification, conformance certification or a safety guarantee.
- Added document/page/OCR-region comments with importance, resolution state and tags. Comments, tags and app-authored links persist in projects and participate in Undo/Redo. Persistent audit history, tag-color editing and the remaining target kinds are future work.
- Added page-link authoring on selected OCR regions, optional destination zoom, Ctrl+click following, back/forward navigation and PDF GoTo link annotations on export. Existing PDF-link editing, automatic page-number recognition, arbitrary rectangles and external links remain out of scope.
- Product/numeric versions: 1.0.0-dev.133 / 1.0.0.133. Project format 1.2 and minimum reader dev.133. Git metadata remains unusable in this working directory; no Git history was recreated or modified.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 23 contract, 3786 keyboard, 139 document-UI, 38 persistence, 69 review, 29 page-history, 76 recent-file, 79 settings, 80 file-launch and 18 version/dependency-management checks passed (4337 total). The source and packaged bookmark/link diagnostics and packaged smoke test exited 0. Documentation verification covered 55 Markdown files, 318 local links and 6 SVG files without broken references or XML errors.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.133-win-x64-20260907-013108`. Product version `1.0.0-dev.133`, numeric version `1.0.0.133`, distribution label and all five managed/application binary versions agree. The 15 native dependency files match `DEPENDENCIES.lock.json`; all 20 records are present in `build-info.json`. SDK 10.0.400; source fingerprint `11ABC390DB95F75F76E89109A9F07C3EB983B7E39A8B2C077B286BB7B375F598`. Results are under `outputs/.verification/dev133-keyboard-20260906-145726896`, `outputs/.verification/final-dev133-20260907-012853412`, `outputs/.verification/bookmark-dev133-20260907-013012876`, `outputs/.verification/versioning-dev133-publish-20260907-013200387`, `outputs/.verification/packaged-smoke-dev133-20260907-013211379` and `outputs/.verification/packaged-link-dev133-20260907-013222234`.

## Previous repository snapshot: v1.0.0-dev.132

### Bookmark allocation fix and external-processing containment (2026-09-04)

- Updated the bundled qpdf from 12.3.2 to 12.4.1 and `bblanchon.PDFium.Win32` from 136.0.7060 to 154.0.8035. `DEPENDENCIES.lock.json` pins repository/publish SHA-256 values; version verification and `build-info.json` now cover native dependencies as well as the five managed/application binaries.
- All qpdf calls and the isolated PDF-export worker now have deadlines, bounded stdout/stderr and whole-process-tree termination. PDFium and export workers are attached to Windows Job Objects with per-process/job memory and active-process limits. Job Objects do not reduce the worker token or provide AppContainer isolation.
- PDFium worker protocol lines, JSON results, PNG previews and output directories are bounded/validated. A protocol or result error discards the worker before another request. NDLOCR companion and bookmark JSON/XML/TXT imports now limit file/count/page/line/title/depth resources; XML DTDs are prohibited.
- Project packages now reject an oversized compressed archive before ZIP parsing and additionally reject rooted, drive-qualified, empty-segment and dot-segment entry names. Contract coverage is 23 cases and `--page-history-test` adds worker containment, external output, NDLOCR and bookmark limit checks for 29 cases.
- Fixed an existing collision between newly allocated bookmark objects and a newly created PDF `/Info` object when the source PDF had no document-information dictionary. The permanent bookmark diagnostic now covers that case.
- Product/numeric versions: 1.0.0-dev.132 / 1.0.0.132. Project format 1.1 and minimum reader dev.123 are unchanged. No Git commit, push, tag or release is performed in this increment.

Final verification: the repository-root Release solution build succeeded with 0 warnings/errors. 76 recent-file, 79 settings, 3479 keyboard, 139 document-UI, 38 persistence, 69 review, 80 file-launch, 29 page-history, 23 contract and 18 version/dependency-management checks passed (4030 total). The final packaged bookmark diagnostic and smoke test both exited 0. The page-history diagnostic also confirmed Windows Job containment and left no app/qpdf process running.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.132-win-x64-20260904-194232`. Product version `1.0.0-dev.132`, numeric version `1.0.0.132`, distribution label and all five managed/application binary versions agree. The 15 native dependency files match `DEPENDENCIES.lock.json`; all 20 managed/application/native records are present in `build-info.json`. SDK 10.0.400; source fingerprint `4396D8E5432BED8EB7057FF53BCD79713A410752185273F0364B554501835F33`, which matches the post-build repository fingerprint. Results are under `outputs/.verification/final-dev132-20260904-193857546`, `outputs/.verification/bookmark-dev132-20260904-193837749`, `outputs/.verification/versioning-dev132-publish-20260904-194300504`, `outputs/.verification/packaged-dev132-20260904-194300504` and `outputs/.verification/packaged-dev132-final-20260904-194605469`. Both dev.131 folders (`...-192741` and `...-192928`) are superseded intermediate outputs and must not be distributed.

## Previous repository snapshot: v1.0.0-dev.130

### Native/PDF package hardening and bounded work files (2026-09-04)

- Project packages are rejected before deserialization or extraction when entry counts, expanded totals, compression ratios, JSON, thumbnail totals or embedded PDFs exceed configurable defaults. Unsafe/duplicate entry names, malformed source SHA-256 values and embedded-source size mismatches are invalid. A same-size cached embedded PDF is rehashed before reuse, and concurrent materialization uses unique temporary names.
- PDFium preview rendering, text extraction and document-property inspection now run through a reusable worker process instead of the WPF process. Managed failures are returned through a JSON protocol; native crashes, cancellation and deadlines discard the worker and the next request starts a clean process.
- Page-edit PDFs use a locked per-session store. Files are retained only while referenced by the current state or reachable Undo/Redo snapshots, and are removed when branches are invalidated, history is trimmed, documents change or the session closes. Abandoned locked-session directories are reclaimed on the next session.
- qpdf page operations use the shared external-process runner with captured output, a three-minute deadline, cancellation and process-tree termination. Partial output is reclaimed by the page-session store.
- Contract coverage adds package entry/JSON/embedded-PDF/fingerprint/cache-integrity limits. `--page-history-test` now also checks native-worker isolation, deadline termination and work-file reclamation.
- Product/numeric versions: 1.0.0-dev.130 / 1.0.0.130. Project format 1.1 and minimum reader dev.123 are unchanged. No Git commit, push, tag or release is performed in this increment.

Final verification: Release solution build succeeded with 0 warnings/errors. 76 recent-file, 79 settings, 3479 keyboard, 139 document-UI, 38 persistence, 69 review, 80 file-launch, 25 page-history, 21 contract and 18 version-management checks passed (4024 total). The packaged page-history diagnostic passed all 25 checks, the packaged smoke test exited 0, and no app/worker process, page working PDF or native-worker temporary file remained afterward.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.130-win-x64-20260904-131308`. Product version 1.0.0-dev.130, numeric version 1.0.0.130, title/About, saved-manifest application version and folder label agree. SDK 10.0.400; source fingerprint `F337CD741C45E5560D717CCAE7B1306186896DE7AE32AAB95106196CBFF21E83`. Source and binary hashes match `build-info.json`; previous build folders are unchanged. Results are under `outputs/.verification/final-dev130-20260904-130707418`, `outputs/.verification/packaged-dev130-page-history-20260904-131351333` and `outputs/.verification/versioning-20260904-131326107`.

## Previous repository snapshot: v1.0.0-dev.129

### Page-structure Undo/Redo (2026-09-01)

- Page insertion, deletion, reordering and 90-degree rotation now participate in the same ordered Undo/Redo history as OCR edits. Ctrl+Z/Ctrl+Y restores the corresponding PDF, project pages, bookmarks, current/multi-page selection and loaded OCR page cache; OCR edits made before a page operation remain reachable after undoing that operation.
- Each page operation retains before/after working-PDF snapshots without modifying the source PDF. Undo/Redo validates the target PDF before switching state, keeps the history entry when restoration fails, prevents concurrent history execution during page reconstruction/export, and clears a stale redo branch after any new OCR edit.
- The page-deletion confirmation now states that Undo is available. Working-PDF names were shortened so the bundled Windows qpdf can create them below its legacy 260-character path limit in the current portable workspace.
- Added `--page-history-test <new-output-directory>` with generated PDFs. It covers all four page operations, PDF/page/OCR geometry restoration, OCR-history continuity and instance identity, redo invalidation, page selection, long-path avoidance, and save/reload after redo.
- Product/numeric versions: 1.0.0-dev.129 / 1.0.0.129. Project format 1.1 and minimum reader dev.123 are unchanged. No Git commit, push, tag or release is performed in this increment.

Final verification: Release solution build succeeded with 0 warnings/errors. 76 recent-file, 79 settings, 3479 keyboard, 139 document-UI, 38 persistence, 69 review, 80 file-launch, 21 page-history, 16 contract and 18 version-management checks passed (4015 total). The packaged page-history diagnostic also passed all 21 checks, and packaged smoke test exited 0.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.129-win-x64-20260901-233021`. Product version 1.0.0-dev.129, numeric version 1.0.0.129, title/About, saved-manifest application version and folder label agree. SDK 10.0.400; source fingerprint `0A994537D19D6AB985617B269A501A2AADE2CB74E74053A91555A617C8522BEC`. Source and binary hashes match build-info.json; previous build folders are unchanged. Results are under `outputs/.verification/final-dev129-20260901-232932033`, `outputs/.verification/packaged-dev129-page-history-20260901-233040274` and `outputs/.verification/versioning-20260901-233436071`.

## Previous repository snapshot: v1.0.0-dev.128

### Recent files (2026-09-01)

- Added File > Recent Files for PDFs and projects, newest successful open first, including file-association/startup loading. Duplicate paths are promoted rather than repeated. Reopening uses the existing Save/Discard/Cancel and validated loading pipeline; missing/corrupt files do not replace current edits or change history order. Busy operations disable recent-file commands.
- Settings > Manage offers a display count (default 10, range 0–30) and confirmed Clear History. Clear is staged until Settings Save succeeds; Cancel leaves history intact. Zero hides and stops recording history without erasing it. Up to 30 paths are retained independently of the display count; reducing/increasing the count hides/restores older entries.
- History is local configuration (`recent-files.json`, history format 1), not project content or transferable settings. Settings format 13 adds `recentFileLimit`; older settings default to 10. Settings transfer includes the count but never file paths. Atomic history updates and a cross-process lock merge concurrent opens; a stale instance does not restore cleared entries. No PDF/project is deleted by Clear History.
- Shared UI styles, Japanese/English captions, semantic access keys (Recent R, Count C, History H) and Tab order are included. Added `--recent-files-test`; startup diagnostics now isolate their saved history from the delivered application. See [usage guide](RECENT-FILES.md).
- Product/numeric versions: 1.0.0-dev.128 / 1.0.0.128. Project format 1.1 and minimum reader dev.123 are unchanged. Git metadata is still unavailable; no Git initialization, commit, push, tag or release is performed.

Final verification: Release solution build succeeded with 0 warnings/errors. 76 recent-file, 79 settings, 3479 keyboard, 139 document-UI, 38 persistence, 69 review, 80 file-launch, 16 contract and 18 version-management checks passed (3994 total). Packaged smoke test exited 0. Japanese/English management screens were visually checked; the screen specification includes a new dev.128 screenshot while preserving dev.127's image. See the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md) for evidence and UI-test limitations.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.128-win-x64-20260901-020622`. Product version 1.0.0-dev.128, numeric version 1.0.0.128, title/About, saved-manifest application version and folder label agree. SDK 10.0.400; source fingerprint `1481C83A1AAAA5B2BA73D207DA743A8C6291599806650D61707D7A20660D84A5`. Source and binary hashes were checked against build-info.json; previous build folders are unchanged. This feature does not resolve previously identified project/PDF security concerns or other outstanding Version 1.0 scope.

## Previous repository snapshot: v1.0.0-dev.127

### Settings transfer and workspace presets (2026-09-01)

- Added Settings > Manage: export/import validated settings JSON, including all eight custom shortcuts and named layout presets. Import replaces the dialog draft only after confirmation; Save applies it, Cancel leaves the running application unchanged. Export includes pending dialog changes but does not apply them. PDF/project data and local storage paths are not exported.
- Named presets capture left/right panel widths and visibility, status-bar visibility and thumbnail visibility. Up to 20 presets with 1–64-character names; duplicate names use a confirmed update, deletion is confirmed, and Restore Defaults retains presets. Presets do not change unrelated draft settings, document edits, Undo or manual zoom. Auto-fit still responds to viewport changes. Splitter-adjusted widths are captured and width bindings restored when settings are applied.
- Reused shared UI styles and Japanese/English labels. The former Storage tab is now a collapsible section in Manage. Semantic access keys and Tab order cover the new controls; existing input shortcuts remain unchanged.
- Settings format is now 12 with an optional workspacePresets list; existing v11 local settings still load. Transfer envelope format is independently versioned at 1. Imports are bounded to 1 MiB, reject malformed/unsupported data, invalid/reserved/duplicate shortcuts and invalid/duplicate presets. Settings/export writes use unique temporary files and replacement after successful completion.
- Added permanent --settings-test coverage and extended keyboard diagnostics. See [usage and format guide](SETTINGS-WORKSPACES.md). Freely docking/floating panes, command palettes and shortcut-specific preset libraries remain unimplemented. The previously identified project/PDF security concerns are not fixed by this feature increment.
- Product/numeric versions: 1.0.0-dev.127 / 1.0.0.127. Project format 1.1 and minimum reader dev.123 are unchanged. Local Git metadata remains unusable; no reinitialization, commits, pushes, tags or releases were performed.

Final verification: Release solution build succeeded with 0 warnings/errors. 79 settings, 3407 keyboard, 16 contract, 18 version-management, 139 document-UI, 38 persistence, 69 review and 67 file-launch checks passed (3833 total). Packaged smoke test exited 0. The Japanese/English settings screens were visually checked. Native file-pickers and confirmation dialogs were not exercised by physical user input; see the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md) for coverage and limitations.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.127-win-x64-20260901-012101`. Actual binaries, title/About, project-manifest application version and distribution label agree. SDK 10.0.400; source fingerprint `E05EF31CFA030CB242EC7A3EDA45E3F5D4D7AA2BB86B978AEEB3AF80D2C77CBF` matches build-info.json. Previous build folders are unchanged.

## Previous repository snapshot: v1.0.0-dev.126

### Future feature requests recorded (2026-09-01; documentation only)

Recorded [FUT-001–004](outputs/PdfCorrectorium-Documentation/docs/13_Roadmap/13-02_FutureFeatureRequests.md): selectable-color redaction including background image content, text/region-click page navigation, bidirectional visible/invisible text conversion, and selectable editing-preview layouts. These are unimplemented requests, not additions to the current feature list. Link-following versus link-authoring scope, UI details, priorities and release scheduling remain undecided. Proposed acceptance checks are identified separately from the user's requests. No application/build code, version inputs, project format or published binaries changed; no rebuild or GitHub update was performed.

### Semantic access keys (2026-08-31)

- Replaced alphabetical allocation with explicit operation-based mnemonics: Open O, Save S, Save As A, Exit X, Undo U, Redo R, Previous P and Next N. Menu keys are local to their popup, not global Alt shortcuts. Settings Save is Alt+S; OK/Cancel remain unchanged.
- Reviewed main-window inputs, dialog actions/tabs and context menus in Japanese and English. Conflicts use letters from the operation name, such as aUthor, adVanced and eQualize. Related secondary controls remain reachable by Tab from their group; arbitrary numeric fallbacks were removed.
- Preserved Tab/F6 navigation and all eight customizable editor-shortcut tooltips. Added semantic mapping assertions, menu-scope checks and loaded-view checks across editor modes to the keyboard diagnostic.
- Product/numeric versions are `1.0.0-dev.126` / `1.0.0.126`; project format 1.1 and minimum reader dev.123 are unchanged. Local Git metadata remains unusable; no Git initialization, commits, pushes, tags or releases are performed.

Final verification: Release build succeeded with 0 warnings/errors. 3307 keyboard, 16 contract, 18 version-management, 139 document-UI, 38 persistence, 69 review and 67 file-launch checks passed (3654 total). Packaged smoke test exited 0. Actual binaries and the source fingerprint match the publication record.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.126-win-x64-20260831-190003`. Verification details and limitations are recorded in the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md).

## Previous repository snapshot: v1.0.0-dev.125

### Keyboard accessibility (2026-08-31)

- Added access-key labels and targets to main-window inputs and auxiliary dialogs, mnemonic captions to dialog actions/tabs and menu commands, and lifecycle-managed keys/hints for controls without captions. Existing user document strings and bound text are not rewritten.
- Added explicit local Tab ordering around bottom-docked action rows, excluded read-only display fields (not result grids), restored keyboard operation of custom expanders, and retained focus feedback in custom controls. F6/Shift+F6 navigates the main workspace regions. OK/Cancel retain their existing Enter/Esc behavior without new mnemonics.
- All eight configurable editor commands now expose live shortcut tooltips, including unassigned/legacy-conflict states. Character navigation/advance buttons have a separate toolbar row; existing estimation/equalize/restore context-menu tooltips use the same settings.
- Navigation/access keys and common editing shortcuts are reserved; shortcut recording does not trap Tab/Esc/Enter or Alt+alphanumeric. Focused dropdowns retain Alt+Up/Down. Existing conflicting assignments are reported, not rewritten, and do not override standard operations.
- Added `--keyboard-test` and [keyboard operation notes](KEYBOARD-ACCESSIBILITY.md). Current product/numeric versions are `1.0.0-dev.125` / `1.0.0.125`; project format 1.1 and minimum reader dev.123 are unchanged. Larger Version 1.0 gaps listed below remain open.
- Local Git metadata remains unusable. This development change does not create commits, tags, pushes or releases; previous GitHub history/releases are unchanged.

Final verification: Release build succeeded with 0 warnings/errors. 1094 keyboard, 16 contract, 18 version-management, 139 document-UI, 38 persistence, 69 review and 67 file-launch checks passed (1441 total); the packaged smoke test also exited 0. Published binaries, source fingerprint, title/About display and saved-manifest versions were verified.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.125-win-x64-20260831-145843`. Its `build-info.json` records product version `1.0.0-dev.125`, numeric version `1.0.0.125`, SDK 10.0.400 and source/binary hashes. Verification details and keyboard-test limitations are recorded in the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md).

## Previous repository snapshot: v1.0.0-dev.124

### Version-management correction (2026-08-30)

- Centralized the prefix and development revision in `Directory.Build.props`. All solution projects now use product version `1.0.0-dev.124` and assembly/file version `1.0.0.124`, including the generated Windows manifest. The title bar and About dialog show the development revision; About also shows the four-part numeric build. Startup logging and saved manifests use the same build-derived version.
- Added build-time consistency checks, source-fingerprint/revision-reuse checks, actual published binary verification and `build-info.json`. Portable folder names come from evaluated MSBuild properties, not documentation. Changed code/build tools require a revision increment; identical-source rebuilds keep the revision but always get a new timestamped folder.
- Added [mandatory version rules](VERSIONING.md) and repository [working instructions](AGENTS.md). Project format 1.1 and its dev.123 minimum reader are unchanged. Historical dev.123/122 verification records remain below.
- Git does not recognize the current working folder as a repository. No Git metadata has been recreated, committed, tagged or pushed. Versioned binary checks and local publication records do not establish Git history continuity.

Final verification: Release solution build succeeded with 0 warnings/errors; 16 contract, 18 version-management, 139 document-UI, 38 persistence, 69 review and 67 file-launch checks passed (347 total). Packaged smoke test also exited 0. The version-management checks include rejection of overridden version inputs, misleading labels, same-revision source changes and revision rollback. Actual apphost/assembly versions and the embedded Windows manifest were verified.

Certified portable output: `outputs/PdfCorrectorium-Builds/PdfCorrectorium-v1.0.0-dev.124-win-x64-20260830-183553`. Its `build-info.json` records source/binary hashes, SDK 10.0.400 and the matching product/numeric versions. Git fields are null because repository metadata was unavailable. The running pre-existing user instance was found to be the dev.122 build dated `20260830-143634`; it was not closed or replaced automatically.

### Remaining scope recorded at the dev.124 milestone

The five safety fixes from dev.123 remain implemented. Page-structure Undo was completed in dev.129; the larger Version 1.0 feature gaps listed below remain unfinished.

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

### Defects known at the dev.123 milestone

- At dev.123, page insertion, deletion, reordering and rotation did not support Undo and cleared earlier OCR Undo history. dev.129 resolves this FR-104 gap; the statement is retained as historical milestone evidence.
- The five dev.122 audit defects listed below were addressed in dev.123; their old descriptions are retained only as historical evidence.

### Remaining Version 1.0 gaps recorded at the dev.123 milestone

At dev.123, Google Vision and in-application OCR/provider contracts; ruby editing; comments, tags, diffs, hierarchical review aggregation and audit history; migration/repair/read-only/rescue UI and versioned schemas; plugin contracts; freely docking/floating panes and a command palette; export-strategy selection, complete input warnings and multi-engine validation remained unfinished. Comments and tags were later implemented in dev.133, while selectable-color redaction is implemented in dev.152. Recovery-package discovery at startup is still not implemented. At that milestone the design PDF was still a dated snapshot; the current PDF was regenerated for dev.152. This historical increment did not complete all unimplemented scope.

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

- At this dev.122 audit, page insertion, deletion, reordering, and rotation were non-Undo operations and adopting the working PDF cleared earlier overlay history. dev.129 resolves this historical defect.
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

1. Expand the new package and native-worker safeguards with hostile-PDF/package corpora, fuzzing, resource telemetry and recovery UX.
2. Define and implement the in-application OCR/provider boundary, including explicit privacy and credential handling.
3. Implement or explicitly defer ruby, comments, tags, diffs, hierarchical progress and audit history.
4. Add project migration, structured repair, read-only/rescue modes and published schemas.
5. Complete or defer the remaining workspace/docking and extensibility requirements.

## Known constraints

- Japanese/English UI switching is implemented, but complete localization coverage still requires release verification.
- NDLOCR-Lite JSON and conventional PAGE/LINE XML provide coordinate overlays. TXT and TEI are associated as companion metadata but do not yet provide overlay geometry.
- Edited output PDF generation, rotation controls, page thumbnails, and preview fit modes are implemented.
- Project format migration, structured repair, read-only/rescue modes, and JSON Schema publication remain unimplemented.
- The plugin contract is intentionally deferred until the primary PDF/OCR boundary is validated.
