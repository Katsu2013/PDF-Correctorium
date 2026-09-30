# Third-party notices

PdfCorrectorium uses the following third-party components in the development build.

## Noto Sans JP

- Purpose: Japanese text for page numbers, headers, footers, and watermarks in exported PDFs
- License: SIL Open Font License 1.1
- Project: https://github.com/google/fonts/tree/main/ofl/notosansjp
- Source revision: `23e54b51ddffbc7713c583748e3bd86f62b1fa4a`

The font is bundled at `components/fonts/NotoSansJP.ttf` in the portable build.
The license text is included at `licenses/NotoSansJP/OFL.txt`.

## Dirkster.AvalonDock 4.74.1

- Purpose: experimental WPF docking comparison mode for the navigation and properties panes
- License: Microsoft Public License (Ms-PL)
- Project: https://github.com/Dirkster99/AvalonDock

The unmodified assembly is used only when the AvalonDock preview mode is selected.
Its full license text is included under `licenses/AvalonDock/LICENSE` in the repository and portable build.

## bblanchon.PDFium.Win32 154.0.8035

- Purpose: native PDF page parsing and rendering
- Package author: Benoit Blanchon
- Package license: Apache License 2.0
- Project: https://github.com/bblanchon/pdfium-binaries
- Upstream PDFium project: https://pdfium.googlesource.com/pdfium/

The distributed `pdfium.dll` is obtained from the package above. Its use and redistribution remain subject to the package and upstream third-party license notices. PdfCorrectorium does not modify the binary.

The Apache License 2.0 text is included in the repository `LICENSE` file.

## qpdf 12.4.1

- Purpose: remove unreachable PDF objects after page-image optimization and rebuild cross-reference data
- License: Apache License 2.0
- Project: https://github.com/qpdf/qpdf
- Documentation: https://qpdf.readthedocs.io/

The portable Windows distribution uses the official 64-bit MSVC binary build.
The qpdf license and the notices for libraries shipped in its official binary
distribution are included under `licenses/qpdf`.

The exact repository and portable-distribution hashes for both native dependencies
are pinned in `DEPENDENCIES.lock.json` and checked by the publication tools.
