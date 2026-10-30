[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory, [string]$DistributionDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new distribution-test output directory.' }
$null=New-Item -ItemType Directory -Path $OutputDirectory
$checks=[Collections.Generic.List[string]]::new()
function Check([string]$Name,[scriptblock]$Test) { & $Test; $checks.Add('PASS: '+$Name) }
function Reject([scriptblock]$Test) { $failed=$false; try { & $Test|Out-Null } catch { $failed=$true }; if (!$failed) { throw 'Expected invalid distribution input to be rejected.' } }
try {
    $map=Join-Path $PSScriptRoot 'GetMsixPackageVersion.ps1'
    Check 'Store package version reserves the fourth part and derives the global application revision.' {
        if ((& $map -NumericApplicationVersion '1.0.0.250') -cne '1.0.250.0' -or
            (& $map -NumericApplicationVersion '2.3.7.65534') -cne '2.3.65534.0') { throw 'Invalid Store version mapping.' }
        foreach($value in @('0.0.0.1','1.0.0.0','1.0.0.65535','1.0.0-dev.250')) { Reject { & $map -NumericApplicationVersion $value } }
    }
    $manifestPath=Join-Path $OutputDirectory 'AppxManifest.xml'
    & (Join-Path $PSScriptRoot 'NewDistributionManifest.ps1') -OutputPath $manifestPath -PackageName 'PdfCorrectorium.Test' -Publisher 'CN=Test & Publisher' -PublisherDisplayName 'Test & Publisher'
    [xml]$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
    Check 'Manifest preserves escaped publisher text and the derived package version.' {
        if ($manifest.Package.Identity.Publisher -cne 'CN=Test & Publisher' -or $manifest.Package.Identity.Version -cne (& $map)) { throw 'Manifest identity mismatch.' }
    }
    Check 'Manifest uses desktop medium integrity and only the explicit full-trust capability.' {
        $application=$manifest.SelectSingleNode("//*[local-name()='Application']")
        if ($application.GetAttribute('RuntimeBehavior','http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -cne 'packagedClassicApp' -or
            $application.GetAttribute('TrustLevel','http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -cne 'mediumIL') { throw 'Incorrect desktop execution boundary.' }
        $capabilities=@($manifest.SelectNodes("//*[local-name()='Capability']"))
        if($capabilities.Count-ne1-or$capabilities[0].Name-cne'runFullTrust'){throw 'Unexpected package capability.'}
    }
    Check 'File types are declared without installer scripts or default-PDF changes.' {
        $types=@($manifest.SelectNodes("//*[local-name()='FileType']")|ForEach-Object {$_.InnerText})
        if($types.Count-ne2-or'.pdf'-notin$types-or'.pdfocrproj'-notin$types){throw 'Missing file associations.'}
        if(@($manifest.SelectNodes("//*[contains(local-name(),'Script')]")).Count){throw 'Unexpected install script.'}
    }
    $feed=Join-Path $OutputDirectory 'PdfCorrectorium.appinstaller'
    & (Join-Path $PSScriptRoot 'NewAppInstallerFile.ps1') -OutputPath $feed -PackageName 'PdfCorrectorium.Test' -Publisher 'CN=Test & Publisher' -AppInstallerUri 'https://example.invalid/stable/PdfCorrectorium.appinstaller' -PackageUri 'https://example.invalid/releases/version/PdfCorrectorium.msix'
    [xml]$installer=Get-Content -LiteralPath $feed -Raw -Encoding UTF8
    Check 'App Installer checks independently in the background and never requires update before launch.' {
        $launch=$installer.SelectSingleNode("//*[local-name()='OnLaunch']")
        if($launch.HoursBetweenUpdateChecks-ne'24'-or$launch.ShowPrompt-ne'false'-or$launch.UpdateBlocksActivation-ne'false'-or
           !$installer.SelectSingleNode("//*[local-name()='AutomaticBackgroundTask']")-or
           $installer.SelectSingleNode("//*[local-name()='ForceUpdateFromAnyVersion']")){throw 'Incorrect update behavior.'}
        if($installer.AppInstaller.MainPackage.Version-cne(& $map)){throw 'Feed version differs from application-derived package version.'}
    }
    Check 'Unsafe App Installer feeds and reused manifest paths are rejected.' {
        foreach($uri in @('http://example.invalid/x.appinstaller','file:///C:/x.appinstaller','https://example.invalid/evil.exe','https://user:pass@example.invalid/x.appinstaller')){
            Reject { & (Join-Path $PSScriptRoot 'NewAppInstallerFile.ps1') -OutputPath (Join-Path $OutputDirectory 'invalid.appinstaller') -PackageName 'PdfCorrectorium.Test' -Publisher 'CN=Test' -AppInstallerUri $uri -PackageUri 'https://example.invalid/a.msix' }
        }
        Reject { & (Join-Path $PSScriptRoot 'NewDistributionManifest.ps1') -OutputPath $manifestPath -PackageName 'PdfCorrectorium.Test' -Publisher 'CN=Test' -PublisherDisplayName 'Test' }
    }
    Check 'Only direct manifests embed the signed automatic-update feed; the Windows 11 floor supports it.' {
        if($manifest.SelectSingleNode("//*[local-name()='AutoUpdate']")){throw 'A non-direct manifest must not embed a private updater.'}
        $directPath=Join-Path $OutputDirectory 'DirectAppxManifest.xml'
        & (Join-Path $PSScriptRoot 'NewDistributionManifest.ps1') -OutputPath $directPath -PackageName 'PdfCorrectorium.Test' -Publisher 'CN=Test & Publisher' -PublisherDisplayName 'Test' -EmbedAppInstaller
        [xml]$direct=Get-Content -LiteralPath $directPath -Raw -Encoding UTF8
        $embedded=$direct.SelectSingleNode("//*[local-name()='AutoUpdate']/*[local-name()='AppInstaller']")
        if(!$embedded-or$embedded.File-cne'PdfCorrectorium.appinstaller'-or$embedded.NamespaceURI-cne'http://schemas.microsoft.com/appx/manifest/uap/windows10/13'){throw 'Missing embedded feed declaration.'}
        if([version]$direct.Package.Dependencies.TargetDeviceFamily.MinVersion-lt[version]'10.0.21300.0'){throw 'Embedded feed needs a compatible OS floor.'}
        if($installer.AppInstaller.NamespaceURI-cne'http://schemas.microsoft.com/appx/appinstaller/2021'){throw 'Expected modern App Installer update settings.'}
    }
    if($DistributionDirectory){
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $info=Get-Content -LiteralPath (Join-Path $DistributionDirectory 'distribution-info.json') -Raw|ConvertFrom-Json
        $inventory=Get-Content -LiteralPath (Join-Path $DistributionDirectory 'msix-payload-inventory.json') -Raw|ConvertFrom-Json
        Check 'MSIX contains every certified payload file byte-for-byte, including its full private runtime.' {
            $zip=[IO.Compression.ZipFile]::OpenRead($info.msix.path)
            try {
                foreach($item in $inventory){
                    $entry=$zip.GetEntry($item.path)
                    if(!$entry-or$entry.Length-ne$item.bytes){throw ('Missing or resized MSIX member: '+$item.path)}
                    $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
                    try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
                    if($hash-cne$item.sha256){throw ('Changed MSIX member: '+$item.path)}
                }
                if(!$zip.GetEntry('coreclr.dll')-or!$zip.GetEntry('PresentationFramework.dll')-or!$zip.GetEntry('pdfium.dll')){throw 'Missing standalone runtime/native payload.'}
            }finally{$zip.Dispose()}
        }
        Check 'Development MSIX records its signature and is never declared ready for public release.' {
            if($info.readyForPublicRelease){throw 'Packaging alone must not certify a public release.'}
            if($info.channel-eq'Development'-and($info.signature-notin@('Unsigned','ValidTimestamped')-or$info.readyForDirectInstall-or$info.storeSubmissionPerformed)){throw 'Development package claimed production readiness.'}
        }
        Check 'ZIP has private .NET, a portable marker and no install-specific update routing.' {
            $zip=[IO.Compression.ZipFile]::OpenRead($info.zip.path)
            try {
                foreach($name in @('coreclr.dll','hostfxr.dll','PresentationFramework.dll','PdfCorrectorium.exe','portable.marker')){if(!$zip.GetEntry($name)){throw ('Missing portable member: '+$name)}}
                foreach($name in @('distribution.json','config/','logs/','cache/','workspaces/')){if($zip.GetEntry($name)){throw 'Portable ZIP contains packaged identity or generated state.'}}
            }finally{$zip.Dispose()}
            if((Get-FileHash -LiteralPath $info.zip.path).Hash-cne$info.zip.sha256-or(Get-FileHash -LiteralPath $info.msix.path).Hash-cne$info.msix.sha256){throw 'Distribution hash mismatch.'}
        }
    }
    $checks|Set-Content -LiteralPath (Join-Path $OutputDirectory 'checks.txt') -Encoding UTF8
    Write-Output ('Distribution checks passed: '+$checks.Count)
} catch {
    $checks|Set-Content -LiteralPath (Join-Path $OutputDirectory 'checks.txt') -Encoding UTF8
    $_|Out-String|Set-Content -LiteralPath (Join-Path $OutputDirectory 'failure.txt') -Encoding UTF8
    throw
}
