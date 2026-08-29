# qpdf runtime

PdfOcrEditor uses qpdf to remove PDF objects that became unreachable after an
image was cropped. This final compaction step is required for page-image
optimization to reduce the actual PDF file size while preserving document
metadata, bookmarks, viewer preferences, and other catalog information.

Expected runtime layout:

```text
PdfOcrEditor/
├─ PdfOcrEditor.exe
├─ qpdf.exe
├─ qpdf runtime DLLs
└─ licenses/
   └─ qpdf/
      ├─ LICENSE.txt
      └─ third-party notices
```

The source tree stages the official files under `tools/qpdf/bin` and
`tools/qpdf/licenses`. Publishing places the executables and DLLs beside
`PdfOcrEditor.exe`. The application also accepts a full executable path through
the `PDFOCR_QPDF_PATH` environment variable, or `qpdf.exe` available on `PATH`.

The portable distribution uses the official 64-bit MSVC build of qpdf. qpdf is
licensed under Apache License 2.0. Its license and notices must be included with
the portable package.
