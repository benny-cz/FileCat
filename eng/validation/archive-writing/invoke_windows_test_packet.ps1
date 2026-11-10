<#
Host-side driver for run_windows_test_packet.ps1 on the encrypted Windows 11 test VM through vmrun.
Copies the packet and runner, runs the wrapper as the guest administrator, then copies back the status and
sealed evidence into -OutDir and prints the guest summary. Exit code: 0 PASS, 1 FAIL, 3 POSTPONED.
#>
param(
    [Parameter(Mandatory)][string]$RunId,
    [Parameter(Mandatory)][string]$PacketPath,
    [Parameter(Mandatory)][string]$Filter,
    [Parameter(Mandatory)][string]$Projects,
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\..\.tmp'),
    [string]$Vmx = 'V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx',
    [string]$VmPassword = 'a', [string]$GuestUser = 'admin', [string]$GuestPassword = 'a',
    [int]$TimeoutMinutes = 240,
    [string]$Env = ''
)
$ErrorActionPreference = 'Stop'
$vmrun = 'C:\Program Files\VMware\VMware Workstation\vmrun.exe'
function Guest([string[]]$arguments) { & $vmrun -T ws -vp $VmPassword -gu $GuestUser -gp $GuestPassword @arguments }
$sha = (Get-FileHash -LiteralPath $PacketPath -Algorithm SHA256).Hash.ToLowerInvariant()
$packetName = [IO.Path]::GetFileName($PacketPath)
$guestPacket = "C:\FileCatValidation\aw-claude\packets\$packetName"
Guest @('CopyFileFromHostToGuest', $Vmx, $PacketPath, $guestPacket)
Guest @('CopyFileFromHostToGuest', $Vmx, (Join-Path $PSScriptRoot 'run_windows_test_packet.ps1'), 'C:\FileCatValidation\aw-claude\packets\run_windows_test_packet.ps1')
$launcher = Join-Path ([IO.Path]::GetTempPath()) "fc-run-$RunId.cmd"
$line = "@echo off`r`nC:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\FileCatValidation\aw-claude\packets\run_windows_test_packet.ps1 " +
    "-RunId $RunId -Packet `"$guestPacket`" -Sha256 $sha -Filter `"$Filter`" -Projects `"$Projects`" -TimeoutMinutes $TimeoutMinutes -Env `"$Env`" > C:\FileCatValidation\aw-claude\$RunId-wrapper.txt 2>&1`r`n"
[IO.File]::WriteAllText($launcher, $line)
Guest @('CopyFileFromHostToGuest', $Vmx, $launcher, "C:\FileCatValidation\aw-claude\packets\run-$RunId.cmd")
Remove-Item -LiteralPath $launcher
Guest @('runProgramInGuest', $Vmx, 'C:\Windows\System32\cmd.exe', "/c C:\FileCatValidation\aw-claude\packets\run-$RunId.cmd") | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$statusFile = Join-Path $OutDir "$RunId-win-status.txt"
Guest @('CopyFileFromGuestToHost', $Vmx, "C:\FileCatValidation\aw-claude\$RunId-status.txt", $statusFile)
$zip = Join-Path $OutDir "$RunId-win-evidence.zip"
Guest @('CopyFileFromGuestToHost', $Vmx, "C:\FileCatValidation\aw-claude\$RunId-evidence.zip", $zip) 2>$null
$status = (Get-Content -LiteralPath $statusFile -Raw).Trim()
"status: $status"
if (Test-Path -LiteralPath $zip) {
    "evidence: $zip sha256=$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($name in 'summary.json', 'account.json', 'error.txt', 'wrapper-error.txt') {
            $entry = $archive.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if ($entry) { "== $name"; $reader = New-Object IO.StreamReader($entry.Open()); try { $reader.ReadToEnd() } finally { $reader.Dispose() } }
        }
    } finally { $archive.Dispose() }
}
if ($status -like 'POSTPONED*') { exit 3 } elseif ($status -eq 'PASS') { exit 0 } else { exit 1 }
