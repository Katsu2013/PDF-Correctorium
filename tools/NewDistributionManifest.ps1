[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$PackageName,
    [Parameter(Mandatory=$true)][string]$Publisher,
    [Parameter(Mandatory=$true)][string]$PublisherDisplayName,
    [string]$DisplayName = 'PDF Correctorium',
    [switch]$EmbedAppInstaller
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PackageName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$') { throw 'Invalid MSIX package name.' }
$null = [Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName) -or [string]::IsNullOrWhiteSpace($DisplayName)) { throw 'Publisher and application display names are required.' }
if (Test-Path -LiteralPath $OutputPath) { throw 'Do not overwrite a package manifest.' }
$template = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot) 'packaging/AppxManifest.xml.template') -Raw -Encoding UTF8
$automaticUpdate = if ($EmbedAppInstaller) { '<uap13:AutoUpdate><uap13:AppInstaller File="PdfCorrectorium.appinstaller" /></uap13:AutoUpdate>' } else { '' }
$template = $template.Replace('@@AUTOUPDATE@@', $automaticUpdate)
$values = @{
    NAME=$PackageName; PUBLISHER=$Publisher; VERSION=(& (Join-Path $PSScriptRoot 'GetMsixPackageVersion.ps1'))
    DISPLAYNAME=$DisplayName; PUBLISHERDISPLAY=$PublisherDisplayName
}
foreach ($key in $values.Keys) { $template = $template.Replace('@@'+$key+'@@', [Security.SecurityElement]::Escape([string]$values[$key])) }
if ($template -match '@@[A-Z]+@@') { throw 'Unresolved package-manifest placeholder.' }
$null = [xml]$template
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $template, [Text.UTF8Encoding]::new($false))
