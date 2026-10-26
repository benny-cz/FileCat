param(
    [Parameter(Mandatory)][string]$ReportPath,
    [switch]$RetainDeletedClusters
)

# Run elevated only inside the logged-in Windows validation VM. The script owns
# a new VHD and refuses to touch a pre-existing G: or any other drive.
$ErrorActionPreference = 'Stop'
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$report = [IO.Path]::GetFullPath($ReportPath)
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notlike '*VMware*' -or
    !$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    !(Test-Path -LiteralPath $validation -PathType Container) -or
    (Test-Path -LiteralPath 'G:\')) {
    throw 'An elevated VMware validation guest with an unused G: and a FileCatValidation report path is required.'
}

$fixture = Join-Path $validation ('RecoveryVhd-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$vhd = Join-Path $fixture 'source.vhd'
$diskpartScript = Join-Path $fixture 'create.txt'
$result = [ordered]@{ Passed = $false; Fixture = $fixture; Vhd = $vhd; DiskNumber = $null;
    DiskUniqueId = $null; Volume = 'G:'; Label = $null; Source = 'G:\recover-me.bin';
    Expected = Join-Path $fixture 'source-expected.bin'; SourceSha256 = $null;
    Bytes = 81920; Deleted = $false; RetainDeletedClusters = [bool]$RetainDeletedClusters;
    OriginalNtfsDisableDeleteNotify = $null; NtfsDeleteNotifyRestored = !$RetainDeletedClusters;
    Error = $null }
$originalNtfsDeleteNotify = $null
$ntfsDeleteNotifyChanged = $false
try {
    if ($RetainDeletedClusters) {
        $query = @(& fsutil.exe behavior query DisableDeleteNotify)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read the original NTFS TRIM setting.' }
        $ntfsLines = @($query | Where-Object { $_ -match '^NTFS DisableDeleteNotify\s*=\s*[01]\b' })
        if ($ntfsLines.Count -ne 1) {
            throw 'The original NTFS TRIM setting is not an unambiguous 0 or 1.'
        }
        $setting = [regex]::Match($ntfsLines[0], '^NTFS DisableDeleteNotify\s*=\s*([01])\b')
        if (!$setting.Success) { throw 'The NTFS TRIM setting could not be parsed.' }
        $originalNtfsDeleteNotify = [int]$setting.Groups[1].Value
        $result.OriginalNtfsDisableDeleteNotify = $originalNtfsDeleteNotify
        if ($originalNtfsDeleteNotify -eq 0) {
            $ntfsDeleteNotifyChanged = $true
            & fsutil.exe behavior set DisableDeleteNotify NTFS 1 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Cannot disable NTFS TRIM for the isolated fixture.' }
        }
        $now = @(& fsutil.exe behavior query DisableDeleteNotify)
        if ($LASTEXITCODE -ne 0 -or
            !($now | Where-Object { $_ -match '^NTFS DisableDeleteNotify\s*=\s*1\b' })) {
            throw 'NTFS TRIM is still active; refusing to claim retained clusters.'
        }
    }
    $vhdType = if ($RetainDeletedClusters) { 'fixed' } else { 'expandable' }
    @(
        "create vdisk file=`"$vhd`" maximum=512 type=$vhdType",
        "select vdisk file=`"$vhd`"",
        'attach vdisk',
        'create partition primary',
        'format fs=ntfs quick label=FCTEST',
        'assign letter=G'
    ) | Set-Content -LiteralPath $diskpartScript -Encoding ASCII
    $diskpartOutput = & diskpart.exe /s $diskpartScript 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $diskpartOutput -match 'error|failed' -or
        !(Test-Path -LiteralPath 'G:\')) { throw "The owned VHD could not be mounted: $diskpartOutput" }
    $volume = Get-Volume -DriveLetter G
    $disk = Get-DiskImage -ImagePath $vhd | Get-Disk
    $partition = Get-Partition -DriveLetter G
    if ($volume.FileSystem -ne 'NTFS' -or $volume.FileSystemLabel -ne 'FCTEST' -or
        $partition.DiskNumber -ne $disk.Number -or !$disk.UniqueId) {
        throw 'G: does not resolve to the freshly created FCTEST VHD.'
    }
    $result.DiskNumber = $disk.Number
    $result.DiskUniqueId = $disk.UniqueId
    $result.Label = $volume.FileSystemLabel
    [IO.File]::WriteAllBytes('G:\keeper.bin', [byte[]]::new(1024 * 1024))
    for ($n = 0; $n -lt 100; $n++) {
        [IO.File]::WriteAllText(('G:\placeholder-{0:D3}.txt' -f $n), ('placeholder {0}' -f $n))
    }
    $bytes = [byte[]]::new($result.Bytes)
    [Random]::new(314159).NextBytes($bytes)
    [IO.File]::WriteAllBytes($result.Expected, $bytes)
    $sourceStream = [IO.FileStream]::new($result.Source, [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $sourceStream.Write($bytes, 0, $bytes.Length)
        $sourceStream.Flush($true)
    } finally { $sourceStream.Dispose() }
    Write-VolumeCache -Path $volume.Path
    $result.SourceSha256 = (Get-FileHash -LiteralPath $result.Source -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $result.Expected -Algorithm SHA256).Hash -ne $result.SourceSha256) {
        throw 'The source differs from its off-source reference before deletion.'
    }
    [IO.File]::Delete($result.Source)
    Write-VolumeCache -Path $volume.Path
    $result.Deleted = !(Test-Path -LiteralPath $result.Source)
    if (!$result.Deleted) { throw 'The fixture source survived deletion.' }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
} finally {
    if ($RetainDeletedClusters) {
        try {
            if ($ntfsDeleteNotifyChanged) {
                & fsutil.exe behavior set DisableDeleteNotify NTFS $originalNtfsDeleteNotify | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'Could not restore the original NTFS TRIM setting.' }
            }
            if ($null -ne $originalNtfsDeleteNotify) {
                $restored = @(& fsutil.exe behavior query DisableDeleteNotify)
                if ($LASTEXITCODE -ne 0 -or
                    !($restored | Where-Object {
                        $_ -match ('^NTFS DisableDeleteNotify\s*=\s*' + $originalNtfsDeleteNotify + '\b')
                    })) { throw 'The original NTFS TRIM setting did not verify after restoration.' }
                $result.NtfsDeleteNotifyRestored = $true
            }
        } catch {
            $result.Passed = $false
            $result.NtfsDeleteNotifyRestored = $false
            $result.Error = (($result.Error, $_.Exception.Message) | Where-Object { $_ }) -join '; '
        }
    }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw "The owned VHD fixture needs inspection: $($result.Error)" }
