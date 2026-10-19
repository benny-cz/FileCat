param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Create', 'Remove')]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'
$validationRoot = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$volumeRoot = Join-Path $validationRoot 'CrossVolume'
$runnerRoot = Join-Path $validationRoot 'Runner'
$reportFull = [IO.Path]::GetFullPath($ReportPath)
if (-not [string]::Equals([IO.Path]::GetDirectoryName($reportFull),
        [IO.Path]::GetFullPath($runnerRoot), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The volume report must stay in the guest-local validation Runner folder.'
}
$principal = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Creating or detaching the validation VHD requires an elevated guest admin session.'
}

function Invoke-ValidationDiskPart([string[]] $Commands, [string] $Name) {
    $instructionPath = Join-Path $volumeRoot "$Name.diskpart.txt"
    $logPath = Join-Path $volumeRoot "$Name.diskpart.log"
    $Commands | Set-Content -LiteralPath $instructionPath -Encoding Ascii
    & (Join-Path $env:SystemRoot 'System32\diskpart.exe') /s $instructionPath *> $logPath
    if ($LASTEXITCODE -ne 0) {
        throw "DiskPart failed ($LASTEXITCODE); see the guest-local log $logPath."
    }
    $log = Get-Content -LiteralPath $logPath -Raw
    if ($log -match 'DiskPart has encountered an error|Virtual Disk Service error') {
        throw "DiskPart reported an error; see the guest-local log $logPath."
    }
}

if ($Mode -eq 'Create') {
    if (Test-Path -LiteralPath $reportFull) {
        throw 'This report already exists; do not replace an existing validation volume.'
    }
    if (Get-Volume -DriveLetter R -ErrorAction SilentlyContinue) {
        throw 'R: is already assigned in the guest.'
    }
    if (Test-Path -LiteralPath 'R:\') {
        throw 'R: is already reachable in the guest.'
    }
    New-Item -ItemType Directory -Path $volumeRoot -Force | Out-Null
    $id = [guid]::NewGuid().ToString('N')
    $vhd = Join-Path $volumeRoot "$id.vhdx"
    Invoke-ValidationDiskPart @(
        "create vdisk file=`"$vhd`" maximum=512 type=expandable",
        "select vdisk file=`"$vhd`"",
        'attach vdisk',
        'create partition primary',
        'format fs=ntfs quick label=FileCatValidation',
        'assign letter=R'
    ) "$id-create"
    $volume = Get-Volume -DriveLetter R -ErrorAction Stop
    if ($volume.FileSystem -ne 'NTFS' -or
        $volume.FileSystemLabel -ne 'FileCatValidation' -or
        $volume.DriveType -ne 'Fixed') {
        throw 'The mounted validation volume did not have the expected NTFS identity.'
    }
    $testFolder = 'R:\FileCatValidation'
    New-Item -ItemType Directory -Path $testFolder -Force | Out-Null
    $acl = Get-Acl -LiteralPath $testFolder
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $rule = [Security.AccessControl.FileSystemAccessRule]::new(
        $identity, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $testFolder -AclObject $acl
    [pscustomobject]@{
        id = $id
        vhd = $vhd
        driveRoot = 'R:\'
        testFolder = $testFolder
        fileSystem = $volume.FileSystem
        label = $volume.FileSystemLabel
    } | ConvertTo-Json | Set-Content -LiteralPath $reportFull -Encoding UTF8
    return
}

if (-not (Test-Path -LiteralPath $reportFull)) {
    throw 'The existing validation volume report is required for removal.'
}
$report = Get-Content -LiteralPath $reportFull -Raw | ConvertFrom-Json
$vhdFull = [IO.Path]::GetFullPath([string] $report.vhd)
$id = [string] $report.id
if ($id -cnotmatch '^[0-9a-f]{32}$' -or
    -not [string]::Equals($vhdFull, (Join-Path $volumeRoot "$id.vhdx"),
        [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals([IO.Path]::GetDirectoryName($vhdFull),
        [IO.Path]::GetFullPath($volumeRoot), [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetExtension($vhdFull) -ne '.vhdx' -or
    [string] $report.driveRoot -ne 'R:\' -or
    [string] $report.testFolder -ne 'R:\FileCatValidation' -or
    [string] $report.label -ne 'FileCatValidation') {
    throw 'The report does not identify the expected guest-local validation VHD.'
}
if (-not (Test-Path -LiteralPath $vhdFull)) {
    throw 'The validation VHD no longer exists; no other file will be removed.'
}
Invoke-ValidationDiskPart @(
    "select vdisk file=`"$vhdFull`"",
    'detach vdisk'
) "$($report.id)-detach"
if (Get-Volume -DriveLetter R -ErrorAction SilentlyContinue) {
    throw 'R: is still mounted; the validation VHD will be retained.'
}
Remove-Item -LiteralPath $vhdFull
