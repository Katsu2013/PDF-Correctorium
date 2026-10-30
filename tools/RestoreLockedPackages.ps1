[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'local-packages'))
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
if ((Get-Item -LiteralPath $packageRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'The package directory must not be a filesystem link.'
}
$lock = Get-Content -LiteralPath (Join-Path $repositoryRoot 'DEPENDENCIES.lock.json') -Raw | ConvertFrom-Json
if ($lock.schemaVersion -ne 1) { throw 'Unsupported dependency lock.' }
foreach ($dependency in $lock.dependencies) {
    foreach ($file in $dependency.repositoryFiles) {
        $relative = [string]$file.path
        if (!$relative.StartsWith('local-packages/', [StringComparison]::Ordinal)) { continue }
        $id = ([string]$dependency.name).ToLowerInvariant()
        $version = ([string]$dependency.version).ToLowerInvariant()
        $name = "$id.$version.nupkg"
        if ($id -notmatch '^[a-z0-9][a-z0-9._-]*$' -or $version -notmatch '^[0-9][a-z0-9.+-]*$' -or
            $relative -cne "local-packages/$name" -or ([string]$file.sha256) -notmatch '^[a-fA-F0-9]{64}$') {
            throw "Invalid locked package metadata: $relative"
        }
        $destination = Join-Path $packageRoot $name
        if (Test-Path -LiteralPath $destination) {
            if ((Get-FileHash -LiteralPath $destination).Hash -ine [string]$file.sha256) {
                throw "Existing package hash mismatch; no file was overwritten: $destination"
            }
            continue
        }
        $temporary = Join-Path $packageRoot ($name + '.download-' + [Guid]::NewGuid().ToString('N'))
        try {
            $url = "https://api.nuget.org/v3-flatcontainer/$id/$version/$name"
            Write-Output "Downloading pinned build dependency: $name"
            Invoke-WebRequest -Uri $url -OutFile $temporary
            if ((Get-FileHash -LiteralPath $temporary).Hash -ine [string]$file.sha256) { throw "Downloaded package hash mismatch: $name" }
            Move-Item -LiteralPath $temporary -Destination $destination
        }
        finally {
            # Only our GUID-named partial download within the verified package directory.
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    }
}
Write-Output 'All locked NuGet archives are present and SHA-256 verified. Subsequent restore/build can run offline.'
