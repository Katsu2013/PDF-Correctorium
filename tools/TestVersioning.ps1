[CmdletBinding()]
param([string]$OutputDirectory, [string]$PublishDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot ('outputs/.verification/versioning-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new versioning-test output directory.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$checks = [Collections.Generic.List[string]]::new()
function Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $checks.Add('PASS: ' + $Name)
}
function MustFail([scriptblock]$Action, [string]$ExpectedMessage) {
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.Message }
    if (!$failure -or $failure -notlike ('*' + $ExpectedMessage + '*')) {
        throw "Expected rejection containing '$ExpectedMessage', received '$failure'."
    }
}
$assertScript = Join-Path $PSScriptRoot 'AssertBuildRevision.ps1'
try {
    $current = & (Join-Path $PSScriptRoot 'GetBuildVersion.ps1')
    Check 'Current MSBuild properties match the repository revision.' {
        if ($current.Version -notmatch '-dev\.([1-9][0-9]*)$' -or !$current.NumericVersion.EndsWith('.' + $Matches[1])) {
            throw 'Current numeric revision differs from product revision.'
        }
    }
    foreach ($invalidProperty in @('Version=9.0.0-dev.1', 'AssemblyVersion=9.0.0.1', 'FileVersion=9.0.0.1',
        'InformationalVersion=9.0.0-dev.1', 'DevelopmentRevision=0', 'DevelopmentRevision=65535', 'DevelopmentRevision=0124',
        'DevelopmentRevision=54321', 'ApplicationVersionPrefix=9.0.0')) {
        Check "MSBuild rejects $invalidProperty." {
            $savedPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                $errorOutput = & dotnet msbuild (Join-Path $repositoryRoot 'src/PdfCorrectorium.Core/PdfCorrectorium.Core.csproj') `
                    -nologo -target:ValidateRepositoryVersion "-property:$invalidProperty" 2>&1
                $code = $LASTEXITCODE
            }
            finally { $ErrorActionPreference = $savedPreference }
            if ($code -eq 0 -or ($errorOutput -join "`n") -notmatch 'Directory.Build.targets') {
                throw "Invalid version was not rejected by the version gate: $invalidProperty"
            }
        }
    }
    Check 'A misleading distribution label is rejected before publish.' {
        MustFail { & (Join-Path $PSScriptRoot 'BuildPortable.ps1') -BuildLabel 'v0.0.0-dev.1' -NoRestore } 'BuildLabel must match'
    }
    $fixtureRoot = Join-Path $OutputDirectory 'history'
    Check 'First publication with no local history is allowed.' {
        & $assertScript -BuildRoot $fixtureRoot -NumericVersion '1.0.0.100' -SourceFingerprint 'SOURCE_A'
    }
    $previous = Join-Path $fixtureRoot 'PdfCorrectorium-v1.0.0-dev.100-win-x64-20260101-000000'
    $null = New-Item -ItemType Directory -Path $previous
    @{sourceFingerprint='SOURCE_A'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $previous 'build-info.json') -Encoding utf8
    Check 'Same-source verification rebuild keeps the revision.' {
        & $assertScript -BuildRoot $fixtureRoot -NumericVersion '1.0.0.100' -SourceFingerprint 'SOURCE_A'
    }
    Check 'Changed source without a revision increment is rejected.' {
        MustFail { & $assertScript -BuildRoot $fixtureRoot -NumericVersion '1.0.0.100' -SourceFingerprint 'SOURCE_B' } 'without a revision increment'
    }
    Check 'Revision rollback is rejected.' {
        MustFail { & $assertScript -BuildRoot $fixtureRoot -NumericVersion '1.0.0.99' -SourceFingerprint 'SOURCE_A' } 'rollback refused'
    }
    Check 'Changed source with an advanced revision is allowed.' {
        & $assertScript -BuildRoot $fixtureRoot -NumericVersion '1.0.0.101' -SourceFingerprint 'SOURCE_B'
    }
    $fingerprintA = & (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')
    $fingerprintB = & (Join-Path $PSScriptRoot 'GetBuildSourceFingerprint.ps1')
    Check 'Unchanged build inputs have a stable SHA-256 fingerprint.' {
        if ($fingerprintA -notmatch '^[A-F0-9]{64}$' -or $fingerprintA -cne $fingerprintB) { throw 'Unstable input fingerprint.' }
    }
    $runtimeScript = Join-Path $PSScriptRoot 'AssertRuntimeConfiguration.ps1'
    $runtimeFixture = Join-Path $OutputDirectory 'runtimeconfig.json'
    $goodRuntime = '{"runtimeOptions":{"tfm":"net10.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}]}}'
    Check 'Runtime configuration accepts the intended desktop runtime.' {
        $goodRuntime | Set-Content -LiteralPath $runtimeFixture -Encoding utf8
        $actual = & $runtimeScript -Path $runtimeFixture -TargetFramework 'net10.0-windows7.0'
        if ($actual.RequiredFrameworks.Count -ne 2) { throw 'Missing runtime requirements.' }
    }
    foreach ($test in @('old-tfm','old-desktop','missing-desktop','duplicate-framework','major-roll-forward','invalid-json','missing-options')) {
        Check ('Runtime configuration rejects ' + $test + '.') {
            $config = $goodRuntime | ConvertFrom-Json
            switch ($test) {
                'old-tfm' { $config.runtimeOptions.tfm = 'net8.0' }
                'old-desktop' { $config.runtimeOptions.frameworks[1].version = '8.0.0' }
                'missing-desktop' { $config.runtimeOptions.frameworks = @($config.runtimeOptions.frameworks[0]) }
                'duplicate-framework' { $config.runtimeOptions.frameworks[1].name = 'Microsoft.NETCore.App' }
                'major-roll-forward' { $config.runtimeOptions | Add-Member -NotePropertyName rollForward -NotePropertyValue LatestMajor }
                'missing-options' { $config = [pscustomobject]@{ unrelated = $true } }
            }
            if ($test -eq 'invalid-json') { '{broken' | Set-Content -LiteralPath $runtimeFixture -Encoding utf8 }
            else { $config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $runtimeFixture -Encoding utf8 }
            MustFail { & $runtimeScript -Path $runtimeFixture -TargetFramework 'net10.0-windows7.0' } 'runtimeconfig'
        }
    }
    $selfContainedRuntime = '{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.14"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.14"}]}}'
    Check 'Self-contained runtime requirements are included, never shared.' {
        $selfContainedRuntime | Set-Content -LiteralPath $runtimeFixture -Encoding utf8
        $actual = & $runtimeScript -Path $runtimeFixture -TargetFramework 'net10.0-windows7.0' -DeploymentMode SelfContained -BundledRuntimeVersion '10.0.14'
        if ($actual.IncludedFrameworks.Count -ne 2 -or $actual.RequiredFrameworks.Count -ne 0) { throw 'Incorrect self-contained runtime model.' }
    }
    foreach ($test in @('shared-as-contained','contained-as-shared','mixed-mode','wrong-included-patch','missing-included-desktop')) {
        Check ('Runtime configuration rejects ' + $test + '.') {
            $config = $selfContainedRuntime | ConvertFrom-Json
            $mode = 'SelfContained'
            switch ($test) {
                'shared-as-contained' { $config = $goodRuntime | ConvertFrom-Json }
                'contained-as-shared' { $mode = 'FrameworkDependent' }
                'mixed-mode' { $config.runtimeOptions | Add-Member -NotePropertyName frameworks -NotePropertyValue @(($goodRuntime|ConvertFrom-Json).runtimeOptions.frameworks) }
                'wrong-included-patch' { $config.runtimeOptions.includedFrameworks[1].version='10.0.13' }
                'missing-included-desktop' { $config.runtimeOptions.includedFrameworks=@($config.runtimeOptions.includedFrameworks[0]) }
            }
            $config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $runtimeFixture -Encoding utf8
            MustFail { & $runtimeScript -Path $runtimeFixture -TargetFramework 'net10.0-windows7.0' -DeploymentMode $mode -BundledRuntimeVersion '10.0.14' } 'runtimeconfig'
        }
    }
    if ($PublishDirectory) {
        Check 'Published binaries and the embedded Windows manifest match the current revision.' {
            $verified = & (Join-Path $PSScriptRoot 'GetBuildVersion.ps1') -PublishDirectory $PublishDirectory
            $lock = Get-Content -LiteralPath (Join-Path $repositoryRoot 'DEPENDENCIES.lock.json') -Raw | ConvertFrom-Json
            $expected = 5 + @($lock.dependencies.publishFiles).Count
            if ($verified.Binaries.Count -ne $expected) { throw "Expected $expected verified managed and native binaries." }
        }
    }
    $checks | Set-Content -LiteralPath (Join-Path $OutputDirectory 'checks.txt') -Encoding utf8
    Write-Output "Versioning tests passed: $($checks.Count). $OutputDirectory"
}
catch {
    $checks | Set-Content -LiteralPath (Join-Path $OutputDirectory 'checks.txt') -Encoding utf8
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $OutputDirectory 'failure.txt') -Encoding utf8
    throw
}
