[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new MSIX asset directory.' }
$null=New-Item -ItemType Directory -Path $OutputDirectory
Add-Type -AssemblyName System.Drawing
$source=Join-Path (Split-Path $PSScriptRoot) 'src/PdfCorrectorium.App/Resources/Branding/PdfCorrectorium-256.png'
$image=[Drawing.Image]::FromFile($source)
try {
    foreach($entry in @(@{name='StoreLogo.png';size=50},@{name='Square44x44Logo.png';size=44},@{name='Square150x150Logo.png';size=150})){
        $bitmap=[Drawing.Bitmap]::new($entry.size,$entry.size)
        try {
            $graphics=[Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic; $graphics.DrawImage($image,0,0,$entry.size,$entry.size) }
            finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $OutputDirectory $entry.name),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    }
} finally { $image.Dispose() }
