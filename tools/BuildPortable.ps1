[CmdletBinding()]
param(
    [string]$BuildLabel,
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$version = & (Join-Path $PSScriptRoot 'GetBuildVersion.ps1')
if ($BuildLabel -and $BuildLabel -cne $version.BuildLabel) {
    throw "BuildLabel must match $($version.BuildLabel). Update Directory.Build.props; labels cannot override binary versions."
}
$BuildLabel = $version.BuildLabel
$statusText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'IMPLEMENTATION_STATUS.md') -Raw
if ($statusText -notmatch 'Current repository snapshot: (v[^\r\n]+)' -or $Matches[1].Trim() -cne $BuildLabel) {
    throw 'Update the current snapshot in IMPLEMENTATION_STATUS.md to match Directory.Build.props before publishing.'
}
$sourceFingerprint = & (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$buildName = "PdfCorrectorium-$BuildLabel-win-x64-$timestamp"
$buildRoot = Join-Path $repositoryRoot "outputs\PdfCorrectorium-Builds"
$destinationPath = Join-Path $buildRoot $buildName
$projectPath = Join-Path $repositoryRoot "src\PdfCorrectorium.App\PdfCorrectorium.App.csproj"
$nugetConfigPath = Join-Path $repositoryRoot "NuGet.Config"

New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
& (Join-Path $PSScriptRoot 'AssertBuildRevision.ps1') -BuildRoot $buildRoot `
    -NumericVersion $version.NumericVersion -SourceFingerprint $sourceFingerprint
if (Test-Path -LiteralPath $destinationPath)
{
    throw "Build destination already exists: $destinationPath"
}

$publishArguments = @(
    "publish",
    $projectPath,
    "-c", "Release",
    "--self-contained", "false",
    "--no-restore",
    "-o", $destinationPath
)

# Use the same SDK policy as an ordinary solution build.
Push-Location $repositoryRoot
try
{
    $sdkVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Unable to record the build SDK version.' }
    if (-not $NoRestore)
    {
        & dotnet restore $projectPath --configfile $nugetConfigPath
        if ($LASTEXITCODE -ne 0)
        {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }
    }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}

foreach ($fileName in @("LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md", "FILE-ASSOCIATIONS.md", "DEPENDENCIES.lock.json"))
{
    $sourcePath = Join-Path $repositoryRoot $fileName
    if (Test-Path -LiteralPath $sourcePath)
    {
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
    }
}

$verified = & (Join-Path $PSScriptRoot 'GetBuildVersion.ps1') -PublishDirectory $destinationPath
if ((& (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')) -cne $sourceFingerprint) {
    throw 'Build inputs changed while publishing. This output has not been certified; publish again from stable inputs.'
}
$gitCommit = $null
$gitDirty = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    try {
        $gitOutput = & git -C $repositoryRoot rev-parse --verify HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $gitCommit = ($gitOutput -join '').Trim()
            $gitStatus = & git -C $repositoryRoot status --porcelain 2>$null
            if ($LASTEXITCODE -eq 0) { $gitDirty = ![string]::IsNullOrWhiteSpace(($gitStatus -join "`n")) }
        }
    }
    catch { $gitCommit = $null; $gitDirty = $null }
}
$buildInfo = [ordered]@{
    schemaVersion = 1; version = $verified.Version; numericVersion = $verified.NumericVersion
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); buildFolder = $buildName
    sdkVersion = ($sdkVersion -join '').Trim(); sourceFingerprint = $sourceFingerprint
    gitCommit = $gitCommit; gitDirty = $gitDirty; binaries = $verified.Binaries
}
$buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destinationPath 'build-info.json') -Encoding utf8
$resolvedDestination = (Resolve-Path -LiteralPath $destinationPath).Path
Write-Output "Build completed: $resolvedDestination"
