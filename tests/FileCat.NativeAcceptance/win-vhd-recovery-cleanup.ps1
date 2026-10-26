param([Parameter(Mandatory)][string]$ManifestPath)

# Detach and remove only a fixture made by win-vhd-recovery-fixture.ps1.
$ErrorActionPreference = 'Stop'
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$manifestPath = [IO.Path]::GetFullPath($ManifestPath)
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notlike '*VMware*' -or
    [IO.Path]::GetDirectoryName($manifestPath) -ine $validation -or
    !(Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'Cleanup requires an exact validation manifest in the isolated VMware guest.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$fixture = [IO.Path]::GetFullPath([string]$manifest.Fixture)
$vhd = [IO.Path]::GetFullPath([string]$manifest.Vhd)
$prefix = $validation.TrimEnd('\') + '\'
if (!$manifest.Passed -or !$fixture.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($fixture) -notmatch '^RecoveryVhd-[0-9a-f]{32}$' -or
    $vhd -ine (Join-Path $fixture 'source.vhd') -or
    !(Test-Path -LiteralPath $vhd -PathType Leaf) -or
    (Get-Process FileCat -ErrorAction SilentlyContinue)) {
    throw 'Cleanup refused: the manifest, owned path, VHD or FileCat process state changed.'
}
$image = Get-DiskImage -ImagePath $vhd
if (!$image.Attached) { throw 'The expected VHD is not attached; inspect before cleanup.' }
$disk = $image | Get-Disk
$partition = Get-Partition -DriveLetter G
$volume = Get-Volume -DriveLetter G
if ($disk.Number -ne [int]$manifest.DiskNumber -or
    $disk.UniqueId -ne [string]$manifest.DiskUniqueId -or
    $partition.DiskNumber -ne $disk.Number -or
    $volume.FileSystemLabel -ne 'FCTEST') {
    throw 'The mounted G: is not the exact manifest-bound VHD.'
}
Dismount-DiskImage -ImagePath $vhd
if ((Get-DiskImage -ImagePath $vhd).Attached -or (Test-Path -LiteralPath 'G:\')) {
    throw 'VHD detach did not complete; no file was removed.'
}
# The fully resolved target was checked above to be an exact child of FileCatValidation.
Remove-Item -LiteralPath $fixture -Recurse -Force
([ordered]@{ Passed = $true; Removed = $fixture; DiskNumber = $disk.Number }) |
    ConvertTo-Json | Set-Content -LiteralPath ($manifestPath + '.cleanup.json') -Encoding UTF8
