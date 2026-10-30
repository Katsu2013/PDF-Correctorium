[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true)][string]$TargetFramework,
    [ValidateSet('Any','FrameworkDependent','SelfContained')][string]$DeploymentMode = 'Any',
    [string]$BundledRuntimeVersion
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$expectedTfm = ($TargetFramework -split '-')[0]
if ($expectedTfm -notmatch '^net([0-9]+)\.([0-9]+)$') { throw 'Unsupported runtimeconfig target framework.' }
$major = [int]$Matches[1]; $minor = [int]$Matches[2]
try { $document = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json }
catch { throw ('Invalid or missing runtimeconfig JSON: ' + $_.Exception.Message) }
$optionsProperty = $document.PSObject.Properties['runtimeOptions']
if (!$optionsProperty -or !$optionsProperty.Value) { throw 'runtimeconfig runtimeOptions is missing.' }
$options = $optionsProperty.Value
$tfmProperty = $options.PSObject.Properties['tfm']
if (!$tfmProperty -or $tfmProperty.Value -cne $expectedTfm) { throw 'runtimeconfig tfm does not match the application target.' }
$frameworksProperty = $options.PSObject.Properties['frameworks']
$includedProperty = $options.PSObject.Properties['includedFrameworks']
if ($frameworksProperty -and $includedProperty) { throw 'runtimeconfig cannot mix shared and included frameworks.' }
$actualMode = if ($includedProperty) { 'SelfContained' } else { 'FrameworkDependent' }
if ($DeploymentMode -ne 'Any' -and $DeploymentMode -cne $actualMode) { throw 'runtimeconfig deployment mode does not match the distribution.' }
$frameworks = @()
if ($frameworksProperty) { $frameworks = @($frameworksProperty.Value) }
if ($includedProperty) { $frameworks = @($includedProperty.Value) }
if ($frameworks.Count -ne 2) { throw 'runtimeconfig requires exactly NETCore and WindowsDesktop frameworks.' }
foreach ($name in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    $matchesForName = @($frameworks | Where-Object { $_.PSObject.Properties['name'] -and $_.name -ceq $name })
    if ($matchesForName.Count -ne 1) { throw 'runtimeconfig framework names must be present exactly once.' }
    $entry = $matchesForName[0]
    $versionProperty = $entry.PSObject.Properties['version']
    if (!$versionProperty -or [string]$versionProperty.Value -notmatch ('^' + $major + '\.' + $minor + '\.[0-9]+$')) {
        throw 'runtimeconfig framework version does not match the application target.'
    }
    if ($actualMode -eq 'SelfContained' -and $BundledRuntimeVersion -and $versionProperty.Value -cne $BundledRuntimeVersion) {
        throw 'runtimeconfig included runtime version differs from the locked runtime packs.'
    }
}
foreach ($entry in @($options) + $frameworks) {
    $roll = $entry.PSObject.Properties['rollForward']
    if ($roll -and $roll.Value -in @('Major', 'LatestMajor')) { throw 'runtimeconfig must not roll forward to an unverified major runtime.' }
}
[pscustomobject]@{
    TargetFramework = $expectedTfm; DeploymentMode = $actualMode
    RequiredFrameworks = @($frameworks | Where-Object { $actualMode -eq 'FrameworkDependent' } | Select-Object name,version)
    IncludedFrameworks = @($frameworks | Where-Object { $actualMode -eq 'SelfContained' } | Select-Object name,version)
}
