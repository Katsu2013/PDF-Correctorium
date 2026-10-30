# qpdf runtime

PDF Correctorium bundles the official Windows x64 MSVC build of qpdf 12.4.2.
It reads and edits PDF structure, links, bookmarks and metadata, checks redaction
residuals, and compacts generated PDFs. The application explicitly requests JSON
schema version 2 and runs native processing in its existing restricted sandbox.

```text
PDF Correctorium/
├─ PdfCorrectorium.exe
├─ components/qpdf/       # qpdf.exe and all DLLs from the same MSVC64 release
└─ licenses/qpdf/         # Apache license, copyright and bundled notices
```

The source files live in tools/qpdf/bin and tools/qpdf/licenses. Update the whole
runtime set from a verified release, then update DEPENDENCIES.lock.json, notices,
and run the PDF output/security diagnostics. Do not replace only qpdf.exe.

Release builds use bundled executable paths only. PDFOCR_QPDF_PATH and PATH
fallbacks are available only in Debug builds; they do not override the bundled
Release runtime. Hashes identify exact files and do not establish freedom from
known vulnerabilities.
