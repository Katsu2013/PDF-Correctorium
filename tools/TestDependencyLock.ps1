[CmdletBinding()]
param([string]$PublishDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $repositoryRoot 'DEPENDENCIES.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($lock.schemaVersion -ne 1 -or !$lock.dependencies) { throw 'Unsupported or empty dependency lock.' }

$verified = [Collections.Generic.List[object]]::new()
foreach ($dependency in $lock.dependencies) {
    foreach ($file in $dependency.repositoryFiles) {
        $path = Join-Path $repositoryRoot ([string]$file.path)
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Locked dependency file is missing: $($file.path)" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -cne ([string]$file.sha256).ToLowerInvariant()) { throw "Locked dependency hash mismatch: $($file.path)" }
    }
    if ($PublishDirectory) {
        $publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
        foreach ($file in $dependency.publishFiles) {
            $path = Join-Path $publishRoot ([string]$file.path)
            if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Published dependency file is missing: $($file.path)" }
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -cne ([string]$file.sha256).ToLowerInvariant()) { throw "Published dependency hash mismatch: $($file.path)" }
            $verified.Add([pscustomobject]@{
                dependency = [string]$dependency.name
                version = [string]$dependency.version
                file = [string]$file.path
                sha256 = $hash.ToUpperInvariant()
            })
        }
    }
}
[pscustomobject]@{ DependencyCount = $lock.dependencies.Count; PublishedFiles = $verified.ToArray() }
