param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+$')][string]$Revision)
$ErrorActionPreference='Stop'
& pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'preflight-indexed-r732.ps1') -Platform windows -Revision ($Revision+'check')
if($LASTEXITCODE-eq75){exit 75};if($LASTEXITCODE){throw 'Windows native preflight failed'}
$v='C:\Program Files\VMware\VMware Workstation\vmrun.exe'
$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx'
$base='C:\FileCatDsa20261104R2\native-'+$Revision
$archive=Join-Path $PWD ('.tmp/desktop-'+$Revision+'-windows.tgz')
$hash=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
function Check([string]$message){if($LASTEXITCODE){throw $message}}
& $v -T ws -vp a -gu filecatdsa_r2 -gp a createDirectoryInGuest $vm $base;Check 'Fresh Windows native directory failed'
& $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm $archive ($base+'\app.tgz');Check 'Windows native archive copy failed'
& $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm (Join-Path $PSScriptRoot 'native-replacement-ui.ps1') ($base+'\run.ps1');Check 'Windows native driver copy failed'
& $v -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm -interactive 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ($base+'\run.ps1') -ExpectedHash $hash
Check 'Windows native replacement run failed or busy'
