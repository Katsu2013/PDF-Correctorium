[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [ValidateSet('Development','Store','Direct')][string]$Channel = 'Development',
    [string]$PackageName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$AppInstallerUri,
    [string]$PackageUri,
    [string]$SigningCertificateThumbprint,
    [string]$TimestampServer,
    [string]$MakeAppxPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PublishPayload.ps1')
$Channel = @{development='Development';store='Store';direct='Direct'}[$Channel.ToLowerInvariant()]
if ($SigningCertificateThumbprint) { $SigningCertificateThumbprint=$SigningCertificateThumbprint.ToUpperInvariant() }
$repo = Split-Path -Parent $PSScriptRoot
$version = & (Join-Path $PSScriptRoot 'GetBuildVersion.ps1') -PublishDirectory $PublishDirectory
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
$buildInfo = Get-Content -LiteralPath (Join-Path $source 'build-info.json') -Raw | ConvertFrom-Json
$null = Assert-PublishPayload -Directory $source
$fingerprint = & (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')
if ($buildInfo.sourceFingerprint -cne $fingerprint -or $buildInfo.version -cne $version.Version -or $version.DeploymentMode -cne 'SelfContained') {
    throw 'Package only a current certified self-contained payload.'
}
foreach ($name in @('cache','config','logs','workspaces','distribution.json','AppxManifest.xml')) {
    if (Test-Path -LiteralPath (Join-Path $source $name)) { throw ('Do not package generated state or an already-wrapped payload: ' + $name) }
}
if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint -or
    @(Get-ChildItem -LiteralPath $source -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
    throw 'Package inputs must not contain reparse points.'
}
if ($Channel -eq 'Development') {
    if (!$PackageName) { $PackageName='PdfCorrectorium.Development' }
    if (!$Publisher) { $Publisher='CN=PDF Correctorium Development' }
    if (!$PublisherDisplayName) { $PublisherDisplayName='PDF Correctorium Development' }
} elseif (!$PackageName -or !$Publisher -or !$PublisherDisplayName -or
          $PackageName -match 'Development|REPLACE' -or $Publisher -match 'Development|REPLACE') {
    throw 'Store/Direct builds require the actual publisher and package identity, not development placeholders.'
}
if ($Channel -eq 'Direct' -and (!$AppInstallerUri -or !$PackageUri -or !$SigningCertificateThumbprint)) {
    throw 'Direct publication requires fixed HTTPS update URLs and a signing certificate. Use Development for unsigned review packages.'
}
if ($Channel -ne 'Direct' -and ($AppInstallerUri -or $PackageUri)) { throw 'Only Direct packages can declare a private App Installer feed.' }
$packageVersion = & (Join-Path $PSScriptRoot 'GetMsixPackageVersion.ps1') -NumericApplicationVersion $version.NumericVersion
if (!$MakeAppxPath) {
    $sdkRoots = @((Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'), (Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools'))
    $tools = @(); foreach ($root in $sdkRoots) { if (Test-Path -LiteralPath $root) { $tools += Get-ChildItem -LiteralPath $root -Filter makeappx.exe -File -Recurse | Where-Object { $_.Directory.Name -eq 'x64' } } }
    if ($tools.Count -eq 0) { throw 'Install the Windows SDK MSIX packaging tools, or supply MakeAppxPath.' }
    $MakeAppxPath = ($tools | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}
if (!$MakeAppxPath -or !(Test-Path -LiteralPath $MakeAppxPath)) { throw 'Install the Windows SDK MSIX packaging tools, or supply MakeAppxPath.' }
$toolSignature = Get-AuthenticodeSignature -LiteralPath $MakeAppxPath
if ($toolSignature.Status -ne 'Valid' -or $toolSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'MakeAppx must have a valid Microsoft signature.' }
$root = Join-Path $repo 'outputs/PdfCorrectorium-Distributions'
$null = New-Item -ItemType Directory -Path $root -Force
$inputs = [ordered]@{ sourceFingerprint=$fingerprint; applicationVersion=$version.Version; packageVersion=$packageVersion
    channel=$Channel; packageName=$PackageName; publisher=$Publisher; publisherDisplayName=$PublisherDisplayName
    appInstallerUri=$AppInstallerUri; packageUri=$PackageUri; signer=$SigningCertificateThumbprint }
$sha = [Security.Cryptography.SHA256]::Create()
try { $inputFingerprint = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($inputs|ConvertTo-Json -Compress)))).Replace('-','') } finally { $sha.Dispose() }
foreach ($old in (Get-ChildItem -LiteralPath $root -Filter distribution-info.json -File -Recurse)) {
    $record = Get-Content -LiteralPath $old.FullName -Raw | ConvertFrom-Json
    if ($record.packageName -ceq $PackageName -and $record.publisher -ceq $Publisher) {
        if ([version]$record.packageVersion -gt [version]$packageVersion) { throw 'MSIX package version rollback refused.' }
        if ($record.packageVersion -ceq $packageVersion -and $record.channel -ceq $Channel -and $record.inputFingerprint -cne $inputFingerprint) {
            throw 'Package inputs changed without increasing DevelopmentRevision.'
        }
    }
}
$destination = Join-Path $root ('PdfCorrectorium-'+$version.BuildLabel+'-win-x64-'+$Channel.ToLowerInvariant()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $destination -ErrorAction Stop
$stage = Copy-PublishPayloadToStage -SourceDirectory $source -DestinationDirectory (Join-Path $destination 'msix-payload')
$descriptor = [ordered]@{schemaVersion=1;channel=$Channel;appInstallerUri=$AppInstallerUri}
[IO.File]::WriteAllText((Join-Path $stage 'distribution.json'),($descriptor|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$displayName = if ($Channel -eq 'Development') { 'PDF Correctorium (Development)' } else { 'PDF Correctorium' }
if ($Channel -eq 'Direct') {
    & (Join-Path $PSScriptRoot 'NewAppInstallerFile.ps1') -OutputPath (Join-Path $stage 'PdfCorrectorium.appinstaller') `
        -PackageName $PackageName -Publisher $Publisher -AppInstallerUri $AppInstallerUri -PackageUri $PackageUri
}
& (Join-Path $PSScriptRoot 'NewDistributionManifest.ps1') -OutputPath (Join-Path $stage 'AppxManifest.xml') `
    -PackageName $PackageName -Publisher $Publisher -PublisherDisplayName $PublisherDisplayName -DisplayName $displayName -EmbedAppInstaller:($Channel -eq 'Direct')
& (Join-Path $PSScriptRoot 'NewMsixAssets.ps1') -OutputDirectory (Join-Path $stage 'Assets')
$msix = Join-Path $destination ('PdfCorrectorium-'+$version.BuildLabel+'-x64.msix')
& $MakeAppxPath pack /d $stage /p $msix /o *> (Join-Path $destination 'makeappx.log')
if ($LASTEXITCODE -ne 0) { throw ('MakeAppx validation/packaging failed; see ' + (Join-Path $destination 'makeappx.log')) }
$signatureState = 'Unsigned'
if ($SigningCertificateThumbprint) {
    if ($SigningCertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$' -or $TimestampServer -notmatch '^https?://') { throw 'Signing requires a certificate thumbprint and timestamp URL.' }
    $cert = Get-Item -LiteralPath ('Cert:\CurrentUser\My\'+$SigningCertificateThumbprint)
    if (!$cert.HasPrivateKey -or $cert.Subject -cne $Publisher -or $cert.NotAfter -lt (Get-Date) -or $cert.NotBefore -gt (Get-Date)) { throw 'Certificate private key, validity or MSIX publisher does not match.' }
    $signtool = Join-Path (Split-Path $MakeAppxPath) 'signtool.exe'
    $signToolSignature=Get-AuthenticodeSignature -LiteralPath $signtool
    if($signToolSignature.Status-ne'Valid'-or$signToolSignature.SignerCertificate.Subject-notmatch'O=Microsoft Corporation'){throw 'SignTool must have a valid Microsoft signature.'}
    & $signtool sign /fd SHA256 /sha1 $SigningCertificateThumbprint /tr $TimestampServer /td SHA256 $msix *> (Join-Path $destination 'signing.log')
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $msix
    if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) { throw 'MSIX needs a valid timestamped signature.' }
    $signatureState='ValidTimestamped'
}
if ($Channel -eq 'Direct') {
    Copy-Item -LiteralPath (Join-Path $stage 'PdfCorrectorium.appinstaller') -Destination (Join-Path $destination 'PdfCorrectorium.appinstaller')
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $destination ('PdfCorrectorium-'+$version.BuildLabel+'-win-x64.zip')
New-PublishArchive -ArchivePath $zip -PayloadDirectory $source
$inventory = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=$_.FullName.Substring($stage.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'msix-payload-inventory.json') -Encoding UTF8
$record = [ordered]@{
    schemaVersion=1; applicationVersion=$version.Version; numericApplicationVersion=$version.NumericVersion; sourceFingerprint=$fingerprint
    packageVersion=$packageVersion; packageName=$PackageName; publisher=$Publisher; channel=$Channel; inputFingerprint=$inputFingerprint
    createdAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); makeAppxVersion=(Get-Item -LiteralPath $MakeAppxPath).VersionInfo.FileVersion
    makeAppxSha256=(Get-FileHash -LiteralPath $MakeAppxPath).Hash; signature=$signatureState
    readyForPublicRelease=$false; readyForDirectInstall=($Channel -eq 'Direct' -and $signatureState -eq 'ValidTimestamped')
    storeIdentityProvided=($Channel -eq 'Store'); storeSubmissionPerformed=$false
    payloadBytes=($inventory|Measure-Object -Property bytes -Sum).Sum; payloadFiles=$inventory.Count
    msix=[ordered]@{path=$msix;bytes=(Get-Item -LiteralPath $msix).Length;sha256=(Get-FileHash -LiteralPath $msix).Hash}
    zip=[ordered]@{path=$zip;bytes=(Get-Item -LiteralPath $zip).Length;sha256=(Get-FileHash -LiteralPath $zip).Hash}
}
if ((& (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')) -cne $fingerprint) { throw 'Build inputs changed during packaging.' }
$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $destination 'distribution-info.json') -Encoding UTF8
Write-Output ('Distributions created: '+$destination)
if ($Channel -eq 'Development') { Write-Output 'Unsigned development MSIX: not installable on a normal PC until appropriately signed/trusted; no Store submission was made.' }
