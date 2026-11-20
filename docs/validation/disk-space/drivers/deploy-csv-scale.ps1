param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,[Parameter(Mandatory=$true)][ValidateSet('windows','mac','linux')][string]$Platform,[ValidatePattern('^[0-9,]+$')][string]$Counts='1000,1000000,10000000',[ValidatePattern('^(wide|deep)(,(wide|deep))?$')][string]$Shapes='wide,deep')
$ErrorActionPreference='Stop'
$manifest=Get-Content -LiteralPath ('.tmp/csv-scale-manifest-'+$Revision+'.json') -Raw|ConvertFrom-Json
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
$receipt=Join-Path $PWD ('.tmp/'+$tag+'-csv-scale-'+$Revision+'-original.json')
$claim=[IO.File]::Open($receipt,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read);$claim.Dispose()
function Record([string]$state,$code){[ordered]@{revision=$Revision;platform=$Platform;state=$state;exitCode=$code;observedUtc=[DateTime]::UtcNow.ToString('o');hostPid=$PID}|ConvertTo-Json|Set-Content -LiteralPath $receipt}
function Check([string]$message){if($LASTEXITCODE){throw $message}}
Record 'waiting-for-idle' $null
try{
 $deadline=[DateTime]::UtcNow.AddMinutes(30)
 while($true){
  & pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'preflight-indexed-r732.ps1') -Platform $Platform -Revision ($Revision+'scale')
  if($LASTEXITCODE-eq0){break};if($LASTEXITCODE-ne75){throw 'Guest preflight failed'}
  if([DateTime]::UtcNow-ge$deadline){Record 'postponed-no-deployment' 75;exit 75};Start-Sleep -Seconds 30
 }
 Record 'original-deployment-running' $null
 if($Platform-eq'windows'){
  $v='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx';$guest='C:\FileCatDsa20261104R2\csv-scale-'+$Revision
  & $v -T ws -vp a -gu filecatdsa_r2 -gp a createDirectoryInGuest $vm $guest;Check 'Windows scale directory failed'
  & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm $manifest.archive ($guest+'\app.tgz');Check 'Windows scale archive copy failed'
  & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm (Join-Path $PSScriptRoot 'run-csv-scale.ps1') ($guest+'\run.ps1');Check 'Windows scale driver copy failed'
  & $v -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ($guest+'\run.ps1') -ArchiveSha256 $manifest.sha256 -Counts $Counts -Shapes $Shapes
 }elseif($Platform-eq'mac'){
  $guest='/Users/benny/FileCatValidation/csv-scale-'+$Revision;$key='C:\Users\marek\.ssh\windows_claude_ed25519'
  & ssh.exe -i $key -o BatchMode=yes benny@192.168.0.199 ('umask 077; mkdir '+$guest);Check 'Mac scale fresh directory failed'
  & scp.exe -O -i $key -o BatchMode=yes $manifest.archive ('benny@192.168.0.199:'+$guest+'/app.tgz');Check 'Mac scale archive copy failed'
  & scp.exe -O -i $key -o BatchMode=yes (Join-Path $PSScriptRoot 'run-csv-scale.py') ('benny@192.168.0.199:'+$guest+'/run.py');Check 'Mac scale driver copy failed'
  & ssh.exe -i $key -o BatchMode=yes benny@192.168.0.199 ('python3 '+$guest+'/run.py '+$guest+' '+$manifest.sha256+' --counts '+$Counts+' --shapes '+$Shapes)
 }else{
  $guest='/home/benny/FileCatValidation/csv-scale-'+$Revision;$key='SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE';$plink='C:\Program Files\PuTTY\plink.exe';$pscp='C:\Program Files\PuTTY\pscp.exe'
  & $plink -batch -pw a -hostkey $key benny@192.168.58.129 ('umask 077; mkdir '+$guest);Check 'Ubuntu scale fresh directory failed'
  & $pscp -q -batch -pw a -hostkey $key $manifest.archive ('benny@192.168.58.129:'+$guest+'/app.tgz');Check 'Ubuntu scale archive copy failed'
  & $pscp -q -batch -pw a -hostkey $key (Join-Path $PSScriptRoot 'run-csv-scale.py') ('benny@192.168.58.129:'+$guest+'/run.py');Check 'Ubuntu scale driver copy failed'
  & $plink -batch -pw a -hostkey $key benny@192.168.58.129 ('python3 '+$guest+'/run.py '+$guest+' '+$manifest.sha256+' --counts '+$Counts+' --shapes '+$Shapes)
 }
 $result=$LASTEXITCODE;Record 'original-deployment-terminal' $result;exit $result
}catch{Record 'wrapper-failed-inspect-processes-before-retry' 1;throw}
