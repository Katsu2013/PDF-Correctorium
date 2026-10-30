# Third-party notices

## Google Material Symbols

- Purpose: selected operation icons in the shared vector icon system (no icon font bundled)
- License: Apache License 2.0
- Source: https://github.com/google/material-design-icons
- Pinned revision, source paths, normalization and modifications: `licenses/MaterialSymbols/NOTICE.md`
- Full license text: `licenses/MaterialSymbols/LICENSE`

PdfCorrectorium uses the following third-party components in the development build.

## .NET 10.0.14 private runtime (Windows x64)

- Purpose: run the application and its isolated PDF/OCR workers without a separately installed .NET Desktop Runtime
- Sources: Microsoft.NETCore.App.Runtime.win-x64 and Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.14, official NuGet packages
- License: the original license and third-party notices are included under `licenses/DotNet/NETCore` and `licenses/DotNet/WindowsDesktop`
- Runtime files and package hashes are pinned in `DEPENDENCIES.lock.json`. Runtime servicing is delivered by a newly verified application release, not by replacing individual files at startup.

The unmodified MSVC runtime DLLs already supplied in the official qpdf 12.4.2 MSVC64 archive are also copied beside ONNX/Skia/PDFium for private native loading. Their original tool-local copies and qpdf distribution notices are retained; both locations are hash-locked. No DLL is taken from the developer machine's System32 directory.

## Noto Sans JP

- Purpose: Japanese text for page numbers, headers, footers, and watermarks in exported PDFs
- License: SIL Open Font License 1.1
- Project: https://github.com/google/fonts/tree/main/ofl/notosansjp
- Source revision: `23e54b51ddffbc7713c583748e3bd86f62b1fa4a`

The font is bundled at `components/fonts/NotoSansJP.ttf` in the portable build.
The license text is included at `licenses/NotoSansJP/OFL.txt`.

## Dirkster.AvalonDock 4.74.1

- Purpose: WPF docking and floating panes in the main application window
- License: Microsoft Public License (Ms-PL)
- Project: https://github.com/Dirkster99/AvalonDock

The unmodified assembly provides docking and floating panes in the main window; the .NET 10 asset and its matching Japanese resources are selected for current builds.
Its full license text is included under `licenses/AvalonDock/LICENSE` in the repository and portable build.

## bblanchon.PDFium.Win32 158.0.8125

- Purpose: native PDF page parsing and rendering
- Package author: Benoit Blanchon
- Package license: Apache License 2.0
- Project: https://github.com/bblanchon/pdfium-binaries
- Upstream PDFium project: https://pdfium.googlesource.com/pdfium/

The distributed `pdfium.dll` is obtained from the package above. Its use and redistribution remain subject to the package and upstream third-party license notices. PdfCorrectorium does not modify the binary.

The NuGet package uses Apache-2.0. The same-version official binary archive's wrapper license and upstream/third-party notices are included under `licenses/PDFium` without modification.

## qpdf 12.4.2

- Purpose: inspect and edit PDF structure, links, bookmarks and metadata; validate redaction residuals; compact output
- License: Apache License 2.0
- Project: https://github.com/qpdf/qpdf
- Documentation: https://qpdf.readthedocs.io/

The portable Windows distribution uses the official 64-bit MSVC binary build.
The qpdf license and the notices for libraries shipped in its official binary
distribution are included under `licenses/qpdf`.

## RapidOCR selected-line recognition

- RapidOcrNet 4.2.0 (Apache-2.0), source commit `708cae2fcb88720e1d891a81b5ee3e8b2bcc139e`: https://github.com/BobLd/RapidOcrNet
- Japanese `japan_PP-OCRv4_rec_mobile.onnx`, RapidOCR v3.10.0 conversion of PaddleOCR weights (Apache-2.0): https://github.com/RapidAI/RapidOCR/blob/v3.10.0/python/rapidocr/default_models.yaml
- Matching Japanese dictionary from PaddleOCR release/2.7 (Apache-2.0): https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.7/ppocr/utils/dict/japan_dict.txt
- Microsoft.ML.OnnxRuntime / Managed 1.29.0 (MIT), SkiaSharp / Win32 native assets 3.119.1 (MIT with upstream notices), Clipper2 2.0.0 (Boost Software License 1.0), and .NET supporting packages (MIT).

Full licenses and upstream third-party notices are included under `licenses/RapidOCR/`.
The Japanese recognizer and dictionary live under `components/rapidocr/`; no detector,
classifier, default Latin models, Python runtime, or Linux/macOS OCR native files are shipped.
Execution does not download models or transmit documents. Original upstream authorship
and model ownership remain unchanged. Unused transitive package archives are retained
only for reproducible offline restoration, not copied into the portable application.

The exact repository and portable-distribution hashes for the pinned dependencies
are pinned in `DEPENDENCIES.lock.json` and checked by the publication tools.

## Windows speech synthesis

The application uses Windows.Media.SpeechSynthesis and Windows.Media.Playback
with Microsoft.Windows.SDK.NET.Ref 10.0.19041.57:
https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref/10.0.19041.57

The Windows SDK license is included in `licenses/WindowsSDK/Windows-SDK-License.rtf`
(original source: https://aka.ms/WinSDKLicenseURL). The C#/WinRT runtime source
license is included in `licenses/WindowsSDK/CsWinRT-LICENSE.txt` (Microsoft, MIT).
The projection/runtime assemblies are pinned in DEPENDENCIES.lock.json.

Only locally installed Windows voices are used. No voice model, cloud speech
service or microphone recognition feature is bundled by this integration.
The prior System.Speech package is no longer part of the application payload.
