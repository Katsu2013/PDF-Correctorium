# Hash build inputs, not mutable documentation, timestamps, caches or generated outputs.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$inputs = @()
foreach ($name in @('Directory.Build.props', 'Directory.Build.targets', 'global.json', 'NuGet.Config',
    'PdfCorrectorium.sln', 'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md', 'FILE-ASSOCIATIONS.md')) {
    $inputs += Get-Item -LiteralPath (Join-Path $repositoryRoot $name)
}
$inputs += Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -File -Recurse |
    Where-Object { $_.FullName.Substring($repositoryRoot.Length) -notmatch '[\\/](bin|obj)[\\/]' }
$inputs += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'qpdf')) {
    $inputs += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'qpdf') -File -Recurse
}
$lines = foreach ($inputFile in ($inputs | Sort-Object FullName -Unique)) {
    $relativePath = $inputFile.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $inputFile.FullName -Algorithm SHA256).Hash
    "$relativePath=$hash"
}
$algorithm = [Security.Cryptography.SHA256]::Create()
try {
    [BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-', '')
}
finally { $algorithm.Dispose() }
