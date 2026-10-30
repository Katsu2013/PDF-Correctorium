[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$PackageName,
    [Parameter(Mandatory=$true)][string]$Publisher,
    [Parameter(Mandatory=$true)][string]$AppInstallerUri,
    [Parameter(Mandatory=$true)][string]$PackageUri
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
foreach ($entry in @(@{value=$AppInstallerUri;extension='.appinstaller'}, @{value=$PackageUri;extension='.msix'})) {
    $uri = $null
    if (![uri]::TryCreate($entry.value,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -cne 'https' -or
        $uri.UserInfo -or $uri.Fragment -or !$uri.AbsolutePath.EndsWith($entry.extension,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'App Installer requires credential-free HTTPS URLs for .appinstaller and .msix files.'
    }
}
if ($PackageName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$') { throw 'Invalid MSIX package name.' }
$null = [Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
if (Test-Path -LiteralPath $OutputPath) { throw 'Do not overwrite an App Installer file.' }
$version = & (Join-Path $PSScriptRoot 'GetMsixPackageVersion.ps1')
$xml = [xml]'<?xml version="1.0" encoding="utf-8"?><AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2021"><MainPackage/><UpdateSettings><OnLaunch HoursBetweenUpdateChecks="24" ShowPrompt="false" UpdateBlocksActivation="false"/><AutomaticBackgroundTask/></UpdateSettings></AppInstaller>'
$xml.DocumentElement.SetAttribute('Uri',$AppInstallerUri)
$xml.DocumentElement.SetAttribute('Version',$version)
$main = $xml.DocumentElement.FirstChild
foreach ($entry in @{Name=$PackageName; Publisher=$Publisher; Version=$version; ProcessorArchitecture='x64'; Uri=$PackageUri}.GetEnumerator()) {
    $main.SetAttribute($entry.Key,[string]$entry.Value)
}
$settings = [Xml.XmlWriterSettings]::new(); $settings.Indent=$true; $settings.Encoding=[Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create([IO.Path]::GetFullPath($OutputPath),$settings)
try { $xml.Save($writer) } finally { $writer.Dispose() }
