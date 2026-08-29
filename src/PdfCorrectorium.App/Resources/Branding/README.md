# PDF Correctorium branding icons

This directory contains the approved PDF Correctorium application and file-type icons.

## Files used by the application

- `PdfCorrectorium.ico`: multi-resolution Windows application icon (16, 24, 32, 48, 64, 128 and 256 px).
- `PdfCorrectorium-*.png`: PNG previews used by the icon gallery and documentation.
- `FileTypes/*.ico`: icons copied to the portable build's `Icons` directory for an installer or an explicit Windows file-association operation.
- `FileTypes/*-256.png`: maintenance previews displayed by `IconGallery.xaml`.
- `Variants/`: color, dark, light, monochrome and inverted brand variants.
- `Modes/`: project, backup, repair, validation, comparison and history icons.
- `Source/PdfCorrectoriumIconSet.png`: the user-approved source design sheet.

## File-type mapping

| Icon | Intended file type |
|---|---|
| `PdfDocument.ico` | `.pdf` |
| `PdfCorrectoriumProject.ico` | `.pdfocrproj` |
| `PdfCorrectoriumBackup.ico` | `.bak.pdfocrproj` |
| `PdfCorrectoriumAutosave.ico` | `.autosave.pdfocrproj` |
| `PdfCorrectoriumTemporary.ico` | `.tmp.pdfocrproj` |
| `PdfCorrectoriumRepair.ico` | `.repair.pdfocrproj` |
| `PdfCorrectoriumExport.ico` | exported `.pdf` |

Run `tools/GenerateBrandIcons.ps1` after replacing the approved source sheet. The script crops the same artwork deterministically, removes only the connected sheet background, and recreates the PNG and ICO assets without AI redrawing.
