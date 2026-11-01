[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$head=@(& git -C $repo rev-parse --verify HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or ($head -join '').Trim() -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'Publication requires an existing Git repository with a committed HEAD.'
}
$status=@(& git -C $repo status --porcelain=v1 --untracked-files=all 2>$null)
if ($LASTEXITCODE -ne 0 -or ![string]::IsNullOrWhiteSpace(($status -join "`n"))) {
    throw 'Publication requires a clean committed source. Commit reviewed changes before rebuilding for publication.'
}
Write-Output ($head -join '').Trim()
