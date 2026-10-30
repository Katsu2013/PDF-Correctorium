[CmdletBinding()]
param([string]$NumericApplicationVersion)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!$NumericApplicationVersion) { $NumericApplicationVersion = (& (Join-Path $PSScriptRoot 'GetBuildVersion.ps1')).NumericVersion }
if ($NumericApplicationVersion -notmatch '^([1-9][0-9]*)\.([0-9]+)\.([0-9]+)\.([1-9][0-9]*)$') { throw 'A four-part application version is required for MSIX mapping.' }
$value = [version]$NumericApplicationVersion
if ($value.Major -gt 65535 -or $value.Minor -gt 65535 -or $value.Build -gt 65535 -or $value.Revision -gt 65534) {
    throw 'Application version exceeds the MSIX/assembly component limits.'
}
# DevelopmentRevision is globally increasing even when the product prefix changes.
# Store reserves the fourth part; the original full version stays in build-info.
'{0}.{1}.{2}.0' -f $value.Major, $value.Minor, $value.Revision
