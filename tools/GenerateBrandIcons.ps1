param(
    [Parameter(Mandatory = $false)]
    [string] $SourcePath,

    [Parameter(Mandatory = $false)]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $PSScriptRoot '..\src\PdfCorrectorium.App\Resources\Branding\Source\PdfCorrectoriumIconSet.png'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\src\PdfCorrectorium.App\Resources\Branding'
}

function Test-ConnectedBackgroundPixel {
    param([System.Drawing.Color] $Color)

    # The supplied design sheet has an almost-white background. Only pixels connected
    # to the crop boundary are removed, so white areas inside the document icon remain.
    return $Color.A -eq 0 -or ($Color.R -ge 232 -and $Color.G -ge 232 -and $Color.B -ge 232)
}

function Remove-ConnectedBackground {
    param([System.Drawing.Bitmap] $Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $visited = New-Object 'bool[]' ($width * $height)
    $queue = [System.Collections.Generic.Queue[System.Drawing.Point]]::new()

    for ($x = 0; $x -lt $width; $x++) {
        $queue.Enqueue([System.Drawing.Point]::new($x, 0))
        $queue.Enqueue([System.Drawing.Point]::new($x, $height - 1))
    }

    for ($y = 1; $y -lt ($height - 1); $y++) {
        $queue.Enqueue([System.Drawing.Point]::new(0, $y))
        $queue.Enqueue([System.Drawing.Point]::new($width - 1, $y))
    }

    while ($queue.Count -gt 0) {
        $point = $queue.Dequeue()
        $index = ($point.Y * $width) + $point.X
        if ($visited[$index]) {
            continue
        }

        $visited[$index] = $true
        $color = $Bitmap.GetPixel($point.X, $point.Y)
        if (-not (Test-ConnectedBackgroundPixel $color)) {
            continue
        }

        $Bitmap.SetPixel($point.X, $point.Y, [System.Drawing.Color]::Transparent)

        if ($point.X -gt 0) { $queue.Enqueue([System.Drawing.Point]::new($point.X - 1, $point.Y)) }
        if ($point.X + 1 -lt $width) { $queue.Enqueue([System.Drawing.Point]::new($point.X + 1, $point.Y)) }
        if ($point.Y -gt 0) { $queue.Enqueue([System.Drawing.Point]::new($point.X, $point.Y - 1)) }
        if ($point.Y + 1 -lt $height) { $queue.Enqueue([System.Drawing.Point]::new($point.X, $point.Y + 1)) }
    }
}

function Get-VisibleBounds {
    param([System.Drawing.Bitmap] $Bitmap)

    $left = $Bitmap.Width
    $top = $Bitmap.Height
    $right = -1
    $bottom = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -le 8) {
                continue
            }

            if ($x -lt $left) { $left = $x }
            if ($x -gt $right) { $right = $x }
            if ($y -lt $top) { $top = $y }
            if ($y -gt $bottom) { $bottom = $y }
        }
    }

    if ($right -lt $left -or $bottom -lt $top) {
        throw 'The selected icon crop did not contain any visible pixels.'
    }

    return [System.Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1)
}

function New-SquareIconMaster {
    param(
        [System.Drawing.Bitmap] $Source,
        [System.Drawing.Rectangle] $Crop,
        [int] $CanvasSize = 512
    )

    $cropped = $Source.Clone($Crop, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        Remove-ConnectedBackground $cropped
        $visibleBounds = Get-VisibleBounds $cropped
        $padding = [Math]::Max(6, [int]($CanvasSize * 0.035))
        $available = $CanvasSize - ($padding * 2)
        $scale = [Math]::Min($available / $visibleBounds.Width, $available / $visibleBounds.Height)
        $targetWidth = [Math]::Max(1, [int][Math]::Round($visibleBounds.Width * $scale))
        $targetHeight = [Math]::Max(1, [int][Math]::Round($visibleBounds.Height * $scale))
        $targetX = [int](($CanvasSize - $targetWidth) / 2)
        $targetY = [int](($CanvasSize - $targetHeight) / 2)

        $master = [System.Drawing.Bitmap]::new($CanvasSize, $CanvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($master)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage(
                $cropped,
                [System.Drawing.Rectangle]::new($targetX, $targetY, $targetWidth, $targetHeight),
                $visibleBounds,
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $graphics.Dispose()
        }

        return $master
    }
    finally {
        $cropped.Dispose()
    }
}

function New-ResizedBitmap {
    param([System.Drawing.Bitmap] $Master, [int] $Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.DrawImage($Master, 0, 0, $Size, $Size)
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap] $Bitmap)

    $stream = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

function Write-MultiResolutionIcon {
    param(
        [System.Drawing.Bitmap] $Master,
        [string] $Path,
        [int[]] $Sizes
    )

    $images = [System.Collections.Generic.List[object]]::new()
    foreach ($size in $Sizes) {
        $bitmap = New-ResizedBitmap $Master $size
        try {
            $images.Add([pscustomobject]@{ Size = $size; Bytes = Convert-BitmapToPngBytes $bitmap })
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $stream = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)

        $offset = 6 + (16 * $images.Count)
        foreach ($image in $images) {
            $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }

        foreach ($image in $images) {
            $writer.Write([byte[]]$image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Export-IconSet {
    param(
        [System.Drawing.Bitmap] $Source,
        [string] $Name,
        [System.Drawing.Rectangle] $Crop,
        [string] $Destination,
        [switch] $ExportApplicationPngSizes
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $master = New-SquareIconMaster $Source $Crop
    try {
        $icoPath = Join-Path $Destination ($Name + '.ico')
        Write-MultiResolutionIcon $master $icoPath @(16, 24, 32, 48, 64, 128, 256)

        $pngSizes = if ($ExportApplicationPngSizes) { @(16, 32, 48, 64, 128, 256) } else { @(256) }
        foreach ($size in $pngSizes) {
            $bitmap = New-ResizedBitmap $master $size
            try {
                $pngPath = Join-Path $Destination ('{0}-{1}.png' -f $Name, $size)
                $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $bitmap.Dispose()
            }
        }
    }
    finally {
        $master.Dispose()
    }
}

$resolvedSource = (Resolve-Path $SourcePath).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$fileTypeDirectory = Join-Path $OutputDirectory 'FileTypes'
New-Item -ItemType Directory -Path $fileTypeDirectory -Force | Out-Null
$variantDirectory = Join-Path $OutputDirectory 'Variants'
New-Item -ItemType Directory -Path $variantDirectory -Force | Out-Null
$modeDirectory = Join-Path $OutputDirectory 'Modes'
New-Item -ItemType Directory -Path $modeDirectory -Force | Out-Null

$source = [System.Drawing.Bitmap]::FromFile($resolvedSource)
try {
    # Coordinates are tied to the approved 1536 x 1024 design sheet stored with the project.
    Export-IconSet $source 'PdfCorrectorium' ([System.Drawing.Rectangle]::new(20, 16, 245, 263)) $OutputDirectory -ExportApplicationPngSizes

    $fileTypes = @(
        @{ Name = 'PdfDocument';              Crop = [System.Drawing.Rectangle]::new(17, 622, 103, 104) },
        @{ Name = 'PdfCorrectoriumProject';    Crop = [System.Drawing.Rectangle]::new(119, 622, 107, 104) },
        @{ Name = 'PdfCorrectoriumBackup';     Crop = [System.Drawing.Rectangle]::new(228, 622, 105, 104) },
        @{ Name = 'PdfCorrectoriumAutosave';   Crop = [System.Drawing.Rectangle]::new(334, 622, 109, 104) },
        @{ Name = 'PdfCorrectoriumTemporary';  Crop = [System.Drawing.Rectangle]::new(442, 622, 109, 104) },
        @{ Name = 'PdfCorrectoriumRepair';     Crop = [System.Drawing.Rectangle]::new(549, 622, 108, 104) },
        @{ Name = 'PdfCorrectoriumExport';     Crop = [System.Drawing.Rectangle]::new(657, 622, 106, 104) }
    )

    foreach ($fileType in $fileTypes) {
        Export-IconSet $source $fileType.Name $fileType.Crop $fileTypeDirectory
    }

    $variants = @(
        @{ Name = 'PdfCorrectoriumColor';      Crop = [System.Drawing.Rectangle]::new(23, 363, 123, 133) },
        @{ Name = 'PdfCorrectoriumDark';       Crop = [System.Drawing.Rectangle]::new(165, 363, 123, 133) },
        @{ Name = 'PdfCorrectoriumLight';      Crop = [System.Drawing.Rectangle]::new(318, 363, 123, 133) },
        @{ Name = 'PdfCorrectoriumMonochrome'; Crop = [System.Drawing.Rectangle]::new(469, 363, 123, 133) },
        @{ Name = 'PdfCorrectoriumInverted';   Crop = [System.Drawing.Rectangle]::new(622, 363, 123, 133) }
    )

    foreach ($variant in $variants) {
        Export-IconSet $source $variant.Name $variant.Crop $variantDirectory
    }

    $modes = @(
        @{ Name = 'Project';    Crop = [System.Drawing.Rectangle]::new(780, 365, 124, 131) },
        @{ Name = 'Backup';     Crop = [System.Drawing.Rectangle]::new(899, 365, 124, 131) },
        @{ Name = 'Repair';     Crop = [System.Drawing.Rectangle]::new(1021, 365, 124, 131) },
        @{ Name = 'Validation'; Crop = [System.Drawing.Rectangle]::new(1144, 365, 124, 131) },
        @{ Name = 'Compare';    Crop = [System.Drawing.Rectangle]::new(1268, 365, 124, 131) },
        @{ Name = 'History';    Crop = [System.Drawing.Rectangle]::new(1390, 365, 124, 131) }
    )

    foreach ($mode in $modes) {
        Export-IconSet $source $mode.Name $mode.Crop $modeDirectory
    }
}
finally {
    $source.Dispose()
}

Write-Host ('Generated PDF Correctorium branding icons in: {0}' -f (Resolve-Path $OutputDirectory).Path)
