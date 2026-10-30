# Shared payload policy for publication, staging and the formal release gate.
# Dot-source this file. It never builds, signs, deletes or publishes anything.
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Test-PublishPayloadPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[\\:*?"<>|]' -or
        $Path.StartsWith('/') -or $Path -match '(^|/)(\.|\.\.)(/|$)' -or
        $Path -match '(^|/)[^/]*[. ](/|$)' -or $Path.Contains('//') -or
        $Path -match '[\x00-\x1f]') { throw ('Invalid payload member path: ' + $Path) }
    $root = ($Path -split '/')[0]
    if ($root -in @('cache','config','logs','workspaces','.git','.vs','.codex','.agents','distribution.json','AppxManifest.xml') -or
        [IO.Path]::GetExtension($Path) -in @('.pdf','.pdfocrproj','.pfx','.p12','.pem','.key','.dmp')) {
        throw ('User data or nonportable identity must not be included in a payload: ' + $Path)
    }
}

function Get-PublishPayloadFiles {
    param([Parameter(Mandatory=$true)][string]$Directory, [switch]$AllowGeneratedState)
    $root = (Get-Item -LiteralPath $Directory -Force).FullName
    if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Payload roots must not be reparse points.'
    }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($root)
    while ($pending.Count) {
        foreach ($entry in (Get-ChildItem -LiteralPath $pending.Pop() -Force)) {
            $relative = $entry.FullName.Substring($root.Length + 1).Replace('\','/')
            # Check a directory before descending; never follow a junction.
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Payload reparse point: ' + $relative) }
            if ($entry.PSIsContainer) {
                if ($relative -in @('cache','config','logs','workspaces')) {
                    if ($AllowGeneratedState) { continue }
                    throw ('Generated state must not be packaged: ' + $relative)
                }
                Test-PublishPayloadPath $relative
                $pending.Push($entry.FullName)
            } else {
                if ($relative -ieq 'build-info.json') { continue }
                Test-PublishPayloadPath $relative
                [pscustomobject]@{ path=$relative; bytes=$entry.Length; sha256=(Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash }
            }
        }
    }
}

function Assert-PublishPayload {
    param([Parameter(Mandatory=$true)][string]$Directory, [switch]$AllowGeneratedState)
    $rootEntry = Get-Item -LiteralPath $Directory -Force
    if (!$rootEntry.PSIsContainer -or ($rootEntry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A payload must be a real directory, not a file or reparse point.' }
    $root = $rootEntry.FullName
    $infoPath = Join-Path $root 'build-info.json'
    $infoFile = Get-Item -LiteralPath $infoPath -Force
    if (($infoFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $infoFile.Length -gt 4MB) {
        throw 'Invalid build-info file.'
    }
    $info = Get-Content -LiteralPath $infoPath -Raw -Encoding utf8 | ConvertFrom-Json
    if (!$info.PSObject.Properties['schemaVersion'] -or $info.schemaVersion -ne 2 -or
        !$info.PSObject.Properties['payloadFiles'] -or @($info.payloadFiles).Count -eq 0 -or @($info.payloadFiles).Count -gt 10000) {
        throw 'A full schema-2 payload inventory is required. Do not recertify an old used folder by generating an inventory from it.'
    }
    $expected = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($info.payloadFiles)) {
        if (!$file.PSObject.Properties['path'] -or !$file.PSObject.Properties['bytes'] -or !$file.PSObject.Properties['sha256']) { throw 'Incomplete payload inventory member.' }
        $relative = [string]$file.path
        Test-PublishPayloadPath $relative
        if ($relative -ieq 'build-info.json' -or $expected.ContainsKey($relative) -or
            $file.bytes -isnot [ValueType] -or [decimal]$file.bytes -lt 0 -or [decimal]$file.bytes -ne [math]::Truncate([decimal]$file.bytes) -or
            [string]$file.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw ('Invalid or duplicate inventory member: ' + $relative) }
        $expected.Add($relative, $file)
    }
    $actual = @(Get-PublishPayloadFiles -Directory $root -AllowGeneratedState:$AllowGeneratedState)
    foreach ($file in $actual) {
        if (!$expected.ContainsKey($file.path)) { throw ('Unrecorded file must not be packaged: ' + $file.path) }
        $recorded = $expected[$file.path]
        if ($recorded.path -cne $file.path -or [decimal]$recorded.bytes -ne $file.bytes -or $recorded.sha256 -ine $file.sha256) {
            throw ('Payload member differs from its certified inventory: ' + $file.path)
        }
        $null = $expected.Remove($file.path)
    }
    if ($expected.Count) { throw ('A certified payload member is missing: ' + ($expected.Keys -join ', ')) }
    return $info
}

function Copy-PublishPayloadToStage {
    param([Parameter(Mandatory=$true)][string]$SourceDirectory, [Parameter(Mandatory=$true)][string]$DestinationDirectory)
    $source = (Get-Item -LiteralPath $SourceDirectory -Force).FullName
    $destination = [IO.Path]::GetFullPath($DestinationDirectory)
    if ($destination -ieq $source -or $destination.StartsWith($source.TrimEnd('\')+'\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Use a separate new staging directory outside the source payload.'
    }
    if (Test-Path -LiteralPath $destination) { throw 'A release stage must not overwrite an existing directory.' }
    $info = Assert-PublishPayload -Directory $source -AllowGeneratedState
    # Parent directory links could redirect a write before the stage exists.
    $parent = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($destination))
    while ($parent) {
        if ($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Stage parents must not be reparse points.' }
        $parent = $parent.Parent
    }
    $null = New-Item -ItemType Directory -Path $destination
    foreach ($member in (@($info.payloadFiles.path) + @('build-info.json'))) {
        $target = Join-Path $destination $member
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        Copy-Item -LiteralPath (Join-Path $source $member) -Destination $target
    }
    $null = Assert-PublishPayload -Directory $destination
    return $destination
}

function Assert-PublishArchive {
    param([Parameter(Mandatory=$true)][string]$ArchivePath, [Parameter(Mandatory=$true)][string]$PayloadDirectory)
    $info = Assert-PublishPayload -Directory $PayloadDirectory
    $members = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $info.payloadFiles) { $members.Add([string]$file.path, $file) }
    $metadata = Get-Item -LiteralPath (Join-Path $PayloadDirectory 'build-info.json')
    $members.Add('build-info.json', [pscustomobject]@{path='build-info.json';bytes=$metadata.Length;sha256=(Get-FileHash -LiteralPath $metadata.FullName).Hash})
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $zip.Entries) {
            $path = $entry.FullName
            if ($path.EndsWith('/')) { Test-PublishPayloadPath $path.TrimEnd('/'); continue }
            if (!$members.ContainsKey($path)) { throw ('Unexpected or duplicate archive member: ' + $path) }
            $expected = $members[$path]
            if ($expected.path -cne $path -or $entry.Length -ne $expected.bytes) { throw ('Archive member size/path mismatch: ' + $path) }
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($hash -ine $expected.sha256) { throw ('Archive member hash mismatch: ' + $path) }
            $null = $members.Remove($path)
        }
        if ($members.Count) { throw 'The archive is missing certified members.' }
    } finally { $zip.Dispose() }
}

function New-PublishArchive {
    param([Parameter(Mandatory=$true)][string]$ArchivePath, [Parameter(Mandatory=$true)][string]$PayloadDirectory)
    $info = Assert-PublishPayload -Directory $PayloadDirectory
    $root = (Get-Item -LiteralPath $PayloadDirectory).FullName
    $target = [IO.Path]::GetFullPath($ArchivePath)
    if ($target.StartsWith($root.TrimEnd('\')+'\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $target)) {
        throw 'Create the archive at a new path outside the certified payload.'
    }
    $temporary = $target + '.partial-' + [Guid]::NewGuid().ToString('N')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    try {
        $file = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew)
        try {
            $archive = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create, $true)
            try {
                # Specify slash-separated names: .NET Framework's directory ZIP
                # helper otherwise writes backslashes on Windows PowerShell 5.1.
                foreach ($member in (@($info.payloadFiles.path) + @('build-info.json'))) {
                    $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $member), $member, [IO.Compression.CompressionLevel]::Optimal)
                }
            } finally { $archive.Dispose() }
        } finally { $file.Dispose() }
        Assert-PublishArchive -ArchivePath $temporary -PayloadDirectory $root
        Move-Item -LiteralPath $temporary -Destination $target
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}
