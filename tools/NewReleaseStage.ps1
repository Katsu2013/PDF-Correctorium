[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PublishDirectory, [Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PublishPayload.ps1')
$version = & (Join-Path $PSScriptRoot 'GetBuildVersion.ps1') -PublishDirectory $PublishDirectory
$info = Assert-PublishPayload -Directory $PublishDirectory -AllowGeneratedState
$fingerprint = & (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')
if ($info.version -cne $version.Version -or $info.sourceFingerprint -cne $fingerprint) { throw 'Stage only a certified payload matching the current source and version.' }
$stage = Copy-PublishPayloadToStage -SourceDirectory $PublishDirectory -DestinationDirectory $OutputDirectory
$null = & (Join-Path $PSScriptRoot 'GetBuildVersion.ps1') -PublishDirectory $stage
Write-Output ('Release stage created without copying user state: ' + $stage)
