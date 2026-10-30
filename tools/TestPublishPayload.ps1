[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PublishPayload.ps1')
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new payload-test output directory.' }
$root = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$checks = [Collections.Generic.List[string]]::new()
function Check([string]$Name, [scriptblock]$Action) { & $Action; $checks.Add('PASS: '+$Name) }
function Reject([scriptblock]$Action, [string]$Reason) {
    $failure=$null
    try { & $Action | Out-Null } catch { $failure=$_.Exception.Message }
    if (!$failure -or $failure -notlike ('*'+$Reason+'*')) { throw ('Expected rejection containing '+$Reason+'; received '+$failure) }
}
function Fixture([string]$Name) {
    $dir = Join-Path $root $Name
    $null = New-Item -ItemType Directory -Path (Join-Path $dir 'components/model')
    [IO.File]::WriteAllText((Join-Path $dir 'product.dll'),'generated nonexecutable test fixture')
    [IO.File]::WriteAllText((Join-Path $dir 'components/model/characters.txt'),'generated dictionary')
    [IO.File]::WriteAllText((Join-Path $dir 'portable.marker'),'')
    $files=@(Get-PublishPayloadFiles -Directory $dir)
    @{schemaVersion=2;version='1.0.0-dev.1';sourceFingerprint=('A'*64);testFixtureOnly=$true;payloadFiles=$files}|ConvertTo-Json -Depth 6|
        Set-Content -LiteralPath (Join-Path $dir 'build-info.json') -Encoding utf8
    return $dir
}
function EditInventory([string]$Dir, [scriptblock]$Edit) {
    $path=Join-Path $Dir 'build-info.json'
    $info=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    & $Edit $info
    $info|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $path -Encoding utf8
}
try {
    Check 'A complete fresh payload is accepted.' { $null=Assert-PublishPayload -Directory (Fixture 'clean') }
    Check 'Changed bytes with the original size are rejected.' {
        $dir=Fixture 'tampered'; $path=Join-Path $dir 'components/model/characters.txt'; [IO.File]::WriteAllText($path, ('X'*(Get-Item -LiteralPath $path).Length))
        Reject {Assert-PublishPayload -Directory $dir} 'differs'
    }
    Check 'An unrecorded file is rejected, even with an innocent extension.' {
        $dir=Fixture 'extra'; [IO.File]::WriteAllText((Join-Path $dir 'private-notes.txt'),'generated private fixture')
        Reject {Assert-PublishPayload -Directory $dir} 'Unrecorded'
    }
    Check 'Missing certified members are rejected.' {
        $dir=Fixture 'missing'; Remove-Item -LiteralPath (Join-Path $dir 'portable.marker')
        Reject {Assert-PublishPayload -Directory $dir} 'missing'
    }
    Check 'Cached documents are refused by normal publication checks, including empty state folders.' {
        $dir=Fixture 'state'; $null=New-Item -ItemType Directory -Path (Join-Path $dir 'cache')
        Reject {Assert-PublishPayload -Directory $dir} 'Generated state'
        [IO.File]::WriteAllText((Join-Path $dir 'cache/reader.pdf'),'generated document fixture')
        Reject {Assert-PublishPayload -Directory $dir} 'Generated state'
    }
    Check 'A stage copies only recorded members and preserves the original cache.' {
        $dir=Fixture 'used'; $null=New-Item -ItemType Directory -Path (Join-Path $dir 'cache')
        $cached=Join-Path $dir 'cache/reader.pdf'; [IO.File]::WriteAllText($cached,'must remain in original fixture')
        $stage=Copy-PublishPayloadToStage -SourceDirectory $dir -DestinationDirectory (Join-Path $root 'clean-stage')
        if ((Test-Path -LiteralPath (Join-Path $stage 'cache')) -or !(Test-Path -LiteralPath $cached)) { throw 'Staging copied or removed source state.' }
        $null=Assert-PublishPayload -Directory $stage
        Reject {Copy-PublishPayloadToStage -SourceDirectory $dir -DestinationDirectory $stage} 'overwrite'
        Reject {Copy-PublishPayloadToStage -SourceDirectory $dir -DestinationDirectory (Join-Path $dir 'nested-stage')} 'outside'
    }
    Check 'Private documents cannot be certified under an unrelated directory.' {
        $dir=Fixture 'private-manifest'; EditInventory $dir {param($info) $info.payloadFiles[0].path='components/book.pdf'}
        Reject {Assert-PublishPayload -Directory $dir} 'User data'
    }
    Check 'Traversal, absolute, ADS and ambiguous Windows member names are rejected.' {
        foreach($path in @('../outside.txt','/outside.txt','C:/outside.txt','a\b.txt','a/./b.txt','a//b.txt','a/b.txt:secret','a/name./b.txt')) {
            Reject {Test-PublishPayloadPath $path} 'Invalid payload'
        }
    }
    Check 'Case-insensitive duplicate inventory members are rejected.' {
        $dir=Fixture 'duplicate'; EditInventory $dir {param($info) $copy=$info.payloadFiles[0]|Select-Object *; $copy.path=$copy.path.ToUpperInvariant(); $info.payloadFiles=@($info.payloadFiles)+@($copy)}
        Reject {Assert-PublishPayload -Directory $dir} 'duplicate'
    }
    Check 'Legacy partial inventories cannot be recertified as a clean release.' {
        $dir=Fixture 'legacy'; EditInventory $dir {param($info) $info.schemaVersion=1}
        Reject {Assert-PublishPayload -Directory $dir} 'schema-2'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Check 'Every member of the final ZIP matches the certified bytes.' {
        $dir=Fixture 'archive'; $zip=Join-Path $root 'clean.zip'
        New-PublishArchive -ArchivePath $zip -PayloadDirectory $dir
        Assert-PublishArchive -ArchivePath $zip -PayloadDirectory $dir
        $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Update)
        try { $entry=$archive.CreateEntry('cache/extra.pdf'); $writer=[IO.StreamWriter]::new($entry.Open()); try {$writer.Write('generated secret fixture')}finally{$writer.Dispose()} }
        finally {$archive.Dispose()}
        Reject {Assert-PublishArchive -ArchivePath $zip -PayloadDirectory $dir} 'Unexpected'
    }
    Check 'Duplicate ZIP member names cannot hide a second payload.' {
        $dir=Fixture 'archive-duplicate'; $zip=Join-Path $root 'duplicate.zip'
        New-PublishArchive -ArchivePath $zip -PayloadDirectory $dir
        $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Update)
        try {$null=$archive.CreateEntry('product.dll')}finally{$archive.Dispose()}
        Reject {Assert-PublishArchive -ArchivePath $zip -PayloadDirectory $dir} 'duplicate'
    }
    $checks|Set-Content -LiteralPath (Join-Path $root 'checks.txt') -Encoding utf8
    Write-Output ('Payload publication checks passed: '+$checks.Count)
} catch {
    $checks|Set-Content -LiteralPath (Join-Path $root 'checks.txt') -Encoding utf8
    $_|Out-String|Set-Content -LiteralPath (Join-Path $root 'failure.txt') -Encoding utf8
    throw
}
