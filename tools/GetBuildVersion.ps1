[CmdletBinding()]
param([string]$PublishDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src/PdfCorrectorium.App/PdfCorrectorium.App.csproj'
Push-Location $repositoryRoot
try {
    $result = & dotnet msbuild $projectPath -nologo -target:ValidateRepositoryVersion -getProperty:Version,AssemblyVersion,FileVersion,InformationalVersion
    if ($LASTEXITCODE -ne 0) { throw 'Build-version validation failed.' }
    $properties = (($result -join "`n") | ConvertFrom-Json).Properties
}
finally { Pop-Location }

$binaryChecks = @()
if ($PublishDirectory) {
    $resolvedDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
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
}

[pscustomobject]@{
    Version = $properties.Version
    NumericVersion = $properties.FileVersion
    InformationalVersion = $properties.InformationalVersion
    BuildLabel = 'v' + $properties.Version
    Binaries = $binaryChecks
}
