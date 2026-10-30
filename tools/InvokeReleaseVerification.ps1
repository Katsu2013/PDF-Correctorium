[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run release verification with Windows PowerShell 5.1.' }
$repo=Split-Path -Parent $PSScriptRoot
$out=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Use a new verification directory.' }
$null=New-Item -ItemType Directory -Path $out
. (Join-Path $PSScriptRoot 'PublishPayload.ps1')
function Native([string[]]$Arguments,[string]$Log) {
    $saved=$ErrorActionPreference
    $ErrorActionPreference='Continue'
    try { & dotnet @Arguments *> (Join-Path $out $Log); $code=$LASTEXITCODE }
    finally { $ErrorActionPreference=$saved }
    if ($code -ne 0) { throw ('dotnet exited '+$code+'; see '+(Join-Path $out $Log)) }
}
function AppCheck([string]$Mode,[string]$Report='checks.txt',[string[]]$Extra=@()) {
    $result=Join-Path $out $Mode
    # --startup-file-test is a child-process JSON probe, not the file-launch
    # regression suite. The latter generates its own fixtures and checks.txt.
    $option=if ($Mode -eq 'file-launch') { '--file-launch-tests' } else { '--'+$Mode+'-test' }
    $arguments=@(('"'+$option+'"'), ('"'+$result+'"')) + $Extra
    $process=Start-Process -FilePath (Join-Path $diagnosticStage 'PdfCorrectorium.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(600000)) { $process.Kill(); throw ('Application diagnostic timed out: '+$Mode) }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw ('Application diagnostic failed: '+$Mode+'; see '+$result) }
    $reportPath=Join-Path $result $Report
    if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).Length -eq 0) { throw ('Application diagnostic did not produce checks: '+$Mode) }
}
Push-Location $repo
try {
    & (Join-Path $PSScriptRoot 'RestoreLockedPackages.ps1') *> (Join-Path $out 'dependency-restore.log')
    Native @('restore','PdfCorrectorium.sln','--configfile','NuGet.Config','-p:DisableTransitiveFrameworkReferenceDownloads=true') 'restore.log'
    Native @('build','PdfCorrectorium.sln','-c','Release','--no-restore') 'release-build.log'
    Native @('run','--project','tests/PdfCorrectorium.ContractTests','-c','Release','--no-build','--no-restore') 'contracts.log'
    & (Join-Path $PSScriptRoot 'TestPublishPayload.ps1') -OutputDirectory (Join-Path $out 'payload-policy') *> (Join-Path $out 'payload-policy.log')
    & (Join-Path $PSScriptRoot 'TestDistributions.ps1') -OutputDirectory (Join-Path $out 'distribution-policy') *> (Join-Path $out 'distribution-policy.log')
    $buildOutput=@(& (Join-Path $PSScriptRoot 'BuildPortable.ps1') | Tee-Object -FilePath (Join-Path $out 'portable-build.log'))
    $completion=@($buildOutput|Where-Object {$_ -is [string] -and $_.StartsWith('Build completed: ')})
    if ($completion.Count -ne 1) { throw 'BuildPortable did not identify exactly one new certified payload.' }
    $publish=$completion[0].Substring('Build completed: '.Length)
    $version=& (Join-Path $PSScriptRoot 'GetBuildVersion.ps1') -PublishDirectory $publish
    $info=Assert-PublishPayload -Directory $publish
    & (Join-Path $PSScriptRoot 'TestVersioning.ps1') -PublishDirectory $publish -OutputDirectory (Join-Path $out 'versioning') *> (Join-Path $out 'versioning.log')
    $releaseStage=Join-Path $out 'release-stage'
    & (Join-Path $PSScriptRoot 'NewReleaseStage.ps1') -PublishDirectory $publish -OutputDirectory $releaseStage *> (Join-Path $out 'release-stage.log')
    $diagnosticStage=Copy-PublishPayloadToStage -SourceDirectory $releaseStage -DestinationDirectory (Join-Path $out 'diagnostic-stage')
    $harness=Join-Path $repo 'tests/PdfCorrectorium.ExportDiagnostics/bin/Release/net10.0-windows10.0.19041.0/win-x64'
    $harnessDll=Join-Path $harness 'PdfCorrectorium.ExportDiagnostics.dll'
    if ([Reflection.AssemblyName]::GetAssemblyName($harnessDll).Version.ToString() -cne $version.NumericVersion) { throw 'The standalone diagnostic harness is stale.' }
    foreach($name in @('PdfCorrectorium.ExportDiagnostics.dll','PdfCorrectorium.ExportDiagnostics.deps.json','PdfCorrectorium.ExportDiagnostics.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $harness $name) -Destination $diagnosticStage
    }
    foreach($mode in @('security','sandbox','audit-review','export-state')) {
        # The harness is framework-dependent and uses the build machine's SDK
        # runtime. Its copied app assemblies exercise this exact payload; spawned
        # PdfCorrectorium.exe workers and built-in app checks use the private
        # self-contained runtime. A self-contained app runtimeconfig cannot be
        # combined with a framework-dependent harness deps file.
        Native @((Join-Path $diagnosticStage 'PdfCorrectorium.ExportDiagnostics.dll'),('--'+$mode+'-test'),(Join-Path $out $mode)) ($mode+'.log')
    }
    $applicationChecks=@('persistence','page-history','theme-speech','document-ui',
        'review-mode','similar-word','rapid-ocr','ocr-notices','pane-visibility','recent-files',
        'settings','updates','keyboard','ocr-rendering','link-editing','ocr-property',
        'document-data','avalondock','file-launch','character-estimation','document-organization')
    foreach($mode in $applicationChecks) {
        if ($mode -eq 'document-organization') { AppCheck $mode 'result.txt' }
        else { AppCheck $mode }
    }
    $null=Assert-PublishPayload -Directory $releaseStage
    $archive=Join-Path $out ('PdfCorrectorium-'+$version.BuildLabel+'-win-x64.zip')
    New-PublishArchive -ArchivePath $archive -PayloadDirectory $releaseStage
    $fingerprint=& (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')
    if ($fingerprint -cne $info.sourceFingerprint) { throw 'Verification inputs changed during the run.' }
    $summary=[ordered]@{
        applicationVersion=$version.Version;sourceFingerprint=$fingerprint;reviewedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
        releaseBuildPassed=$true;contractsPassed=$true;packagedSecurityPassed=$true;packagedSandboxPassed=$true
        packagedPersistencePassed=$true;packagedPageHistoryPassed=$true;versioningPassed=$true
        applicationDiagnosticsPassed=$applicationChecks
        redactionResidualChecksPassed=$true;malformedInputChecksPassed=$true
        publishDirectory=$publish;releaseStage=$releaseStage;gitCommit=$info.gitCommit;gitDirty=$info.gitDirty
        zip=[ordered]@{path=$archive;sha256=(Get-FileHash -LiteralPath $archive).Hash}
        readyForPublicRelease=$false
        note='Generated verification fixtures only. This does not sign, publish, approve an independent review or install/update MSIX.'
    }
    $summary|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $out 'verification-summary.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $releaseStage 'build-info.json') -Destination (Join-Path $out 'verified-build-info.json')
    Write-Output ('Release verification completed; formal public approval remains separate: '+$out)
} catch {
    $_|Out-String|Set-Content -LiteralPath (Join-Path $out 'failure.txt') -Encoding utf8
    throw
} finally { Pop-Location }
