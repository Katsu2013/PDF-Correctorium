[CmdletBinding()]
param(
    [string]$BuildLabel,
    [switch]$NoRestore,
    [string]$SigningCertificateThumbprint,
    [string]$TimestampServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PublishPayload.ps1')

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
    "--self-contained", "true",
    "-r", $version.RuntimeIdentifier,
    ('-p:RuntimeFrameworkVersion=' + $version.BundledRuntimeVersion),
    "-p:DisableTransitiveFrameworkReferenceDownloads=true",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
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
        & dotnet restore $projectPath --configfile $nugetConfigPath -r $version.RuntimeIdentifier `
            -p:SelfContained=true ('-p:RuntimeFrameworkVersion=' + $version.BundledRuntimeVersion) `
            -p:DisableTransitiveFrameworkReferenceDownloads=true -p:PublishSingleFile=false -p:PublishTrimmed=false
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

# Signing happens before published hashes/build-info are recorded. No private key is exported.
$signing = $null
if ($SigningCertificateThumbprint) {
    if ($SigningCertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$' -or
        !$TimestampServer -or $TimestampServer -notmatch '^http://') {
        throw 'Signing requires a certificate thumbprint and an HTTP Authenticode timestamp endpoint supported by Windows PowerShell 5.1.'
    }
    $certificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $SigningCertificateThumbprint)
    if (!$certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or
        $certificate.NotBefore -gt (Get-Date) -or
        !($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })) {
        throw 'A currently valid code-signing certificate with a private key is required.'
    }
    $targets = @(Get-Item -LiteralPath (Join-Path $destinationPath 'PdfCorrectorium.exe'))
    $targets += Get-ChildItem -LiteralPath $destinationPath -Filter 'PdfCorrectorium*.dll' -File
    foreach ($target in $targets) {
        $signature = Set-AuthenticodeSignature -LiteralPath $target.FullName -Certificate $certificate `
            -HashAlgorithm SHA256 -TimestampServer $TimestampServer
        if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) {
            throw ('Signing or timestamp validation failed: ' + $target.Name)
        }
    }
    $signing = [ordered]@{ certificateThumbprint = $certificate.Thumbprint; timestampServer = $TimestampServer }
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
$payloadFiles = @(Get-PublishPayloadFiles -Directory $destinationPath | Sort-Object path)
$buildInfo = [ordered]@{
    schemaVersion = 2; version = $verified.Version; numericVersion = $verified.NumericVersion
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); buildFolder = $buildName
    sdkVersion = ($sdkVersion -join '').Trim(); sourceFingerprint = $sourceFingerprint
    targetFramework = $verified.TargetFramework; requiredFrameworks = $verified.RequiredFrameworks
    deploymentMode = $verified.DeploymentMode; runtimeIdentifier = $verified.RuntimeIdentifier
    bundledRuntimeVersion = $verified.BundledRuntimeVersion; includedFrameworks = $verified.IncludedFrameworks
    gitCommit = $gitCommit; gitDirty = $gitDirty; signing = $signing; binaries = $verified.Binaries
    payloadFiles = $payloadFiles
}
$buildInfo | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destinationPath 'build-info.json') -Encoding utf8
$null = Assert-PublishPayload -Directory $destinationPath
$resolvedDestination = (Resolve-Path -LiteralPath $destinationPath).Path
Write-Output "Build completed: $resolvedDestination"
