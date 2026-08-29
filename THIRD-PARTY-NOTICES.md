# Third-party notices

PdfCorrectorium uses the following third-party component in the development build.

## bblanchon.PDFium.Win32 136.0.7060

- Purpose: native PDF page parsing and rendering
- Package author: Benoit Blanchon
- Package license: Apache License 2.0
- Project: https://github.com/bblanchon/pdfium-binaries
- Upstream PDFium project: https://pdfium.googlesource.com/pdfium/

The distributed `pdfium.dll` is obtained from the package above. Its use and redistribution remain subject to the package and upstream third-party license notices. PdfCorrectorium does not modify the binary.

The Apache License 2.0 text is included in the repository `LICENSE` file.

## qpdf 12.3.2

- Purpose: remove unreachable PDF objects after page-image optimization and rebuild cross-reference data
- License: Apache License 2.0
- Project: https://github.com/qpdf/qpdf
- Documentation: https://qpdf.readthedocs.io/

The portable Windows distribution uses the official 64-bit MSVC binary build.
The qpdf license and the notices for libraries shipped in its official binary
distribution are included under `licenses/qpdf`.
