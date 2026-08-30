[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BuildRoot,
    [Parameter(Mandatory=$true)][string]$NumericVersion,
    [Parameter(Mandatory=$true)][string]$SourceFingerprint
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $BuildRoot)) { return }
foreach ($previousBuild in (Get-ChildItem -LiteralPath $BuildRoot -Directory)) {
    if ($previousBuild.Name -notmatch '^PdfCorrectorium-v(\d+\.\d+\.\d+)-dev\.(\d+)-win-x64-') { continue }
    $previousNumeric = [version]($Matches[1] + '.' + $Matches[2])
    if ($previousNumeric -gt [version]$NumericVersion) {
        throw "Revision rollback refused: $($previousBuild.Name). Advance DevelopmentRevision before publishing."
    }
    $previousInfoPath = Join-Path $previousBuild.FullName 'build-info.json'
    if ($previousNumeric -eq [version]$NumericVersion -and (Test-Path -LiteralPath $previousInfoPath)) {
        $previousInfo = Get-Content -LiteralPath $previousInfoPath -Raw | ConvertFrom-Json
        if ($previousInfo.sourceFingerprint -cne $SourceFingerprint) {
            throw 'Build inputs changed without a revision increment. Advance DevelopmentRevision in Directory.Build.props.'
        }
    }
}
