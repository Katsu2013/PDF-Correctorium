[CmdletBinding()]
param([string]$PublishDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src/PdfCorrectorium.App/PdfCorrectorium.App.csproj'
Push-Location $repositoryRoot
try {
    $result = & dotnet msbuild $projectPath -nologo -target:ValidateRepositoryVersion -getProperty:Version,AssemblyVersion,FileVersion,InformationalVersion,TargetFramework
    if ($LASTEXITCODE -ne 0) { throw 'Build-version validation failed.' }
    $properties = (($result -join "`n") | ConvertFrom-Json).Properties
}
finally { Pop-Location }

$binaryChecks = @()
$requiredFrameworks = @()
$includedFrameworks = @()
$deploymentMode = $null
$dependencyLock = Get-Content -LiteralPath (Join-Path $repositoryRoot 'DEPENDENCIES.lock.json') -Raw | ConvertFrom-Json
$runtimePacks = @($dependencyLock.dependencies | Where-Object { $_.PSObject.Properties['runtimeFramework'] })
$runtimeVersions = @($runtimePacks.version | Select-Object -Unique)
if ($runtimePacks.Count -ne 2 -or $runtimeVersions.Count -ne 1) { throw 'Lock exactly the NETCore and WindowsDesktop runtime packs.' }
# Include the two standalone diagnostics, which are not part of the solution.
foreach ($directory in @('src', 'tests')) {
    foreach ($project in (Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $directory) -Filter '*.csproj' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })) {
        [xml]$xml = Get-Content -LiteralPath $project.FullName -Raw
        $framework = $xml.SelectSingleNode('/Project/PropertyGroup/TargetFramework')
        if (!$framework -or $framework.InnerText -notmatch '^net10\.0(?:-windows[0-9.]*)?$') {
            throw ('All application and diagnostic projects must target .NET 10: ' + $project.FullName)
        }
    }
}
if ($PublishDirectory) {
    $resolvedDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
    $runtime = & (Join-Path $PSScriptRoot 'AssertRuntimeConfiguration.ps1') `
        -Path (Join-Path $resolvedDirectory 'PdfCorrectorium.runtimeconfig.json') -TargetFramework $properties.TargetFramework `
        -DeploymentMode SelfContained -BundledRuntimeVersion $runtimeVersions[0]
    $requiredFrameworks = $runtime.RequiredFrameworks
    $includedFrameworks = $runtime.IncludedFrameworks
    $deploymentMode = $runtime.DeploymentMode
    foreach ($name in @('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll','PresentationNative_cor3.dll','msvcp140.dll','msvcp140_1.dll','vcruntime140.dll','vcruntime140_1.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $resolvedDirectory $name) -PathType Leaf)) { throw ('Self-contained runtime file is missing: ' + $name) }
    }
    $requiredFiles = @('PdfCorrectorium.exe', 'PdfCorrectorium.dll', 'PdfCorrectorium.Core.dll',
        'PdfCorrectorium.Infrastructure.dll', 'PdfCorrectorium.ProjectFormat.dll')
    foreach ($fileName in $requiredFiles) {
        $binary = Get-Item -LiteralPath (Join-Path $resolvedDirectory $fileName)
        $details = $binary.VersionInfo
        if ($details.FileVersion -ne $properties.FileVersion -or
            ($details.ProductVersion -split '\+')[0] -ne $properties.Version) {
            throw "Published version mismatch: $fileName ($($details.FileVersion), $($details.ProductVersion))"
        }
        $assemblyVersion = $null
        if ($binary.Extension -eq '.dll') {
            $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($binary.FullName).Version.ToString(4)
            if ($assemblyVersion -ne $properties.AssemblyVersion) { throw "Assembly version mismatch: $fileName" }
        }
        $binaryChecks += [pscustomobject]@{
            file = $fileName; fileVersion = $details.FileVersion; productVersion = $details.ProductVersion
            assemblyVersion = $assemblyVersion; sha256 = (Get-FileHash -LiteralPath $binary.FullName -Algorithm SHA256).Hash
        }
    }
    # The UTF-8 Win32 manifest is embedded into the apphost by the SDK.
    $exeText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $resolvedDirectory 'PdfCorrectorium.exe')))
    $identity = [regex]::Match($exeText, '<assemblyIdentity\s+[^>]*name="PdfCorrectorium\.app"[^>]*/>')
    if (!$identity.Success -or !$identity.Value.Contains('version="' + $properties.FileVersion + '"')) {
        throw 'Embedded Windows application-manifest version does not match FileVersion.'
    }
    $dependencyChecks = & (Join-Path $PSScriptRoot 'TestDependencyLock.ps1') -PublishDirectory $resolvedDirectory
    foreach ($native in $dependencyChecks.PublishedFiles) {
        $binaryChecks += [pscustomobject]@{
            file = $native.file; fileVersion = $null; productVersion = $native.version
            assemblyVersion = $null; sha256 = $native.sha256; dependency = $native.dependency
        }
    }
} else {
    $null = & (Join-Path $PSScriptRoot 'TestDependencyLock.ps1')
}

[pscustomobject]@{
    Version = $properties.Version
    NumericVersion = $properties.FileVersion
    InformationalVersion = $properties.InformationalVersion
    BuildLabel = 'v' + $properties.Version
    TargetFramework = $properties.TargetFramework
    RuntimeIdentifier = 'win-x64'
    BundledRuntimeVersion = $runtimeVersions[0]
    DeploymentMode = $deploymentMode
    RequiredFrameworks = $requiredFrameworks
    IncludedFrameworks = $includedFrameworks
    Binaries = $binaryChecks
}
