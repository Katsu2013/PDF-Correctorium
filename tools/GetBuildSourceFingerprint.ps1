# Hash build inputs, not mutable documentation, timestamps, caches or generated outputs.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$inputs = @()
foreach ($name in @('.gitattributes', '.gitignore', 'Directory.Build.props', 'Directory.Build.targets', 'global.json', 'NuGet.Config',
    'PdfCorrectorium.sln', 'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md', 'FILE-ASSOCIATIONS.md',
    'DEPENDENCIES.lock.json')) {
    $inputs += Get-Item -LiteralPath (Join-Path $repositoryRoot $name)
}
$inputs += Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -File -Recurse |
    Where-Object { $_.FullName.Substring($repositoryRoot.Length) -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -notlike '*_wpftmp.csproj' }
$inputs += Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tests') -File -Recurse |
    Where-Object { $_.FullName.Substring($repositoryRoot.Length) -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -notlike '*_wpftmp.csproj' }
if (Test-Path -LiteralPath (Join-Path $repositoryRoot '.github/workflows')) {
    $inputs += Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.github/workflows') -File
}
$inputs += Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in @('.ps1','.py') }
if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'packaging')) {
    $inputs += Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'packaging') -File -Recurse
}
if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'licenses')) {
    $inputs += Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'licenses') -File -Recurse
}
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'qpdf')) {
    $inputs += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'qpdf') -File -Recurse
}
if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'local-packages')) {
    $dependencyLock = Get-Content -LiteralPath (Join-Path $repositoryRoot 'DEPENDENCIES.lock.json') -Raw | ConvertFrom-Json
    foreach ($file in $dependencyLock.dependencies.repositoryFiles) {
        if (([string]$file.path).StartsWith('local-packages/', [StringComparison]::OrdinalIgnoreCase)) {
            $inputs += Get-Item -LiteralPath (Join-Path $repositoryRoot ([string]$file.path))
        }
    }
}
$byPath = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach ($inputFile in $inputs) {
    $relativePath = $inputFile.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/')
    $byPath[$relativePath] = $inputFile
}
$paths = [string[]]@($byPath.Keys)
[Array]::Sort($paths, [StringComparer]::Ordinal)
$lines = foreach ($relativePath in $paths) {
    $inputFile = $byPath[$relativePath]
    $hash = (Get-FileHash -LiteralPath $inputFile.FullName -Algorithm SHA256).Hash
    "$relativePath=$hash"
}
$algorithm = [Security.Cryptography.SHA256]::Create()
try {
    [BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-', '')
}
finally { $algorithm.Dispose() }
