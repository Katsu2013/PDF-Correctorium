[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [Parameter(Mandatory=$true)][string]$VerificationSummary,
    [string]$DependencyReview,
    [string]$IndependentReview,
    [string]$OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PublishPayload.ps1')
$failures = [Collections.Generic.List[string]]::new()
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$version = & (Join-Path $PSScriptRoot 'GetBuildVersion.ps1') -PublishDirectory $PublishDirectory
$fingerprint = & (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')
$info = Get-Content -LiteralPath (Join-Path $PublishDirectory 'build-info.json') -Raw | ConvertFrom-Json
if ($info.sourceFingerprint -cne $fingerprint) { $failures.Add('Published inputs differ from the current source fingerprint.') }
try { $null = Assert-PublishPayload -Directory $PublishDirectory }
catch { $failures.Add('The complete clean payload was not verified: ' + $_.Exception.Message) }
if (!$info.PSObject.Properties['gitDirty'] -or $info.gitDirty -isnot [bool] -or $info.gitDirty -or
    !$info.PSObject.Properties['gitCommit'] -or [string]$info.gitCommit -notmatch '^[a-fA-F0-9]{40}$') {
    $failures.Add('The build must record a clean, committed Git source for formal publication.')
}
try {
    $head = & git -C $repositoryRoot rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or ($head -join '').Trim() -cne $info.gitCommit) { $failures.Add('The build Git commit does not match this checkout.') }
    $status = & git -C $repositoryRoot status --porcelain=v1 --untracked-files=all 2>$null
    if ($LASTEXITCODE -ne 0 -or ![string]::IsNullOrWhiteSpace(($status -join "`n"))) { $failures.Add('Commit and review all publication inputs before formal release.') }
} catch { $failures.Add('Git provenance could not be verified: ' + $_.Exception.Message) }
$targets = @(Get-Item -LiteralPath (Join-Path $PublishDirectory 'PdfCorrectorium.exe'))
$targets += Get-ChildItem -LiteralPath $PublishDirectory -Filter 'PdfCorrectorium*.dll' -File
foreach ($target in $targets) {
    $signature = Get-AuthenticodeSignature -LiteralPath $target.FullName
    if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) {
        $failures.Add('A valid timestamped publisher signature is missing: ' + $target.Name)
    }
}
function Read-BoundEvidence([string]$Path, [string]$Kind) {
    if (!$Path -or !(Test-Path -LiteralPath $Path -PathType Leaf)) { $failures.Add($Kind + ' evidence is missing.'); return $null }
    try {
        $record = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if ($record.sourceFingerprint -cne $fingerprint -or $record.applicationVersion -cne $version.Version) {
            $failures.Add($Kind + ' evidence is not bound to this exact source and version.')
        }
        $reviewed = [DateTimeOffset]::Parse($record.reviewedAtUtc)
        if ($reviewed -gt [DateTimeOffset]::UtcNow.AddMinutes(5) -or $reviewed -lt [DateTimeOffset]::UtcNow.AddDays(-30)) {
            $failures.Add($Kind + ' evidence is older than 30 days or dated in the future.')
        }
        return $record
    } catch { $failures.Add($Kind + ' evidence is invalid: ' + $_.Exception.Message); return $null }
}
function EvidenceValue($Record, [string]$Name) {
    if ($Record -and $Record.PSObject.Properties[$Name]) { return $Record.$Name }
    return $null
}
$tests = Read-BoundEvidence $VerificationSummary 'Verification'
if ($tests) {
    foreach ($name in @('releaseBuildPassed','contractsPassed','packagedSecurityPassed','packagedSandboxPassed',
        'packagedPersistencePassed','packagedPageHistoryPassed','versioningPassed','redactionResidualChecksPassed','malformedInputChecksPassed')) {
        $confirmed = EvidenceValue $tests $name
        if ($confirmed -isnot [bool] -or !$confirmed) { $failures.Add('Required verification is not confirmed: ' + $name) }
    }
}
$dependencies = Read-BoundEvidence $DependencyReview 'Dependency advisory review'
if ($dependencies) {
    if ((EvidenceValue $dependencies 'unresolvedCriticalOrHigh') -cne 0) { $failures.Add('Dependency critical/high findings remain unresolved or unreported.') }
    $lock = Get-Content -LiteralPath (Join-Path $repositoryRoot 'DEPENDENCIES.lock.json') -Raw | ConvertFrom-Json
    $expectedComponents = @($lock.dependencies | ForEach-Object { [pscustomobject]@{name=$_.name;version=$_.version} })
    $expectedComponents += [pscustomobject]@{name='.NET Desktop Runtime';version=$version.BundledRuntimeVersion}
    foreach ($component in $expectedComponents) {
        $matches = @((EvidenceValue $dependencies 'components') | Where-Object {
            (EvidenceValue $_ 'name') -ceq $component.name -and (EvidenceValue $_ 'version') -ceq $component.version -and
            (EvidenceValue $_ 'reviewed') -ceq $true -and (EvidenceValue $_ 'sourceUrl') -match '^https://'
        })
        if ($matches.Count -ne 1) { $failures.Add('Fresh primary-source advisory review for the exact locked version is missing: ' + $component.name + ' ' + $component.version) }
    }
}
$review = Read-BoundEvidence $IndependentReview 'Independent security review'
if ($review) {
    if ((EvidenceValue $review 'unresolvedCriticalOrHigh') -cne 0 -or (EvidenceValue $review 'approved') -cne $true -or !(EvidenceValue $review 'reviewer')) {
        $failures.Add('Independent security approval with no unresolved critical/high findings is required.')
    }
    if ((EvidenceValue $review 'privateReportingChannelVerified') -cne $true -or (EvidenceValue $review 'updateProcedureVerified') -cne $true) {
        $failures.Add('Private vulnerability reporting and security update procedures have not been verified.')
    }
}
$result = [ordered]@{ applicationVersion=$version.Version; sourceFingerprint=$fingerprint;
    checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); readyForRelease=($failures.Count -eq 0); blockers=@($failures) }
if ($OutputPath) {
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8
}
$result | ConvertTo-Json -Depth 6
if ($failures.Count -ne 0) { throw 'Formal release readiness has not been established. See the recorded blockers.' }
