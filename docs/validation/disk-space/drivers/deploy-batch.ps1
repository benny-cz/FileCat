param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,[Parameter(Mandatory=$true)][ValidateSet('mac','linux','windows')][string]$Platform,[switch]$InteractiveWindows)
$ErrorActionPreference='Stop'
function Check([string]$what){if($LASTEXITCODE){throw $what}}
$archive=Join-Path $PWD ('.tmp/dsa-batch-'+$Platform+'-'+$Revision+'.tgz')
if($Platform-eq'windows'){
 $v='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx';$guest='C:\FileCatDsa20261104R2\batch-'+$Revision
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a createDirectoryInGuest $vm $guest;Check 'Windows candidate directory failed'
 # Refuse a predictable reserve failure before copying/extracting a large payload.
 $stage=Join-Path $PWD ('.tmp/batch-'+$Revision+'-windows')
 $extra=[long](Get-Item -LiteralPath $archive).Length
 if(Test-Path -LiteralPath (Join-Path $PWD ('.tmp/cache-input-'+$Revision))){$extra+=32MB}
 elseif(Test-Path -LiteralPath $stage){$extra+=[long](Get-ChildItem -LiteralPath $stage -File -Recurse|Measure-Object Length -Sum).Sum}
 else{throw 'Windows payload size is unknown; prepare a verified full stage or cache plan'}
 $space=@'
$ErrorActionPreference='Stop'
$drive=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
$reserve=[long][Math]::Min(2GB,[Math]::Max(256MB,[Math]::Ceiling($drive.Size/100.0)))
$required=$reserve+[long]@EXTRA@+256MB
[ordered]@{available=[long]$drive.FreeSpace;required=$required;reserve=$reserve;incomingBytes=[long]@EXTRA@}|ConvertTo-Json|Set-Content -LiteralPath '@GUEST@\space-preflight.json'
if($drive.FreeSpace-lt$required){exit 74}
'@
 $space=$space.Replace('@EXTRA@',[string]$extra).Replace('@GUEST@',$guest)
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($space))
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -EncodedCommand $encoded;Check 'Windows free-space preflight failed before transfer; inspect space-preflight.json'
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm $archive ($guest+'\bundle.tgz');Check 'Windows candidate copy failed'
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm (Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')) ($guest+'\run.ps1');Check 'Windows runner copy failed'
 if($InteractiveWindows){
  $hash=(Get-FileHash -LiteralPath (Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')) -Algorithm SHA256).Hash
  $launch=@'
$ErrorActionPreference='Stop'
$base='@BASE@';$taskName='FileCatDsaR2-@REV@';$script=Join-Path $base 'run.ps1'
if((Get-FileHash -LiteralPath $script -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Runner changed'}
if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue){throw 'A fresh test task is required'}
if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){'BUSY phase=verify'|Set-Content (Join-Path $base 'state.txt');exit 75}
$principal=New-ScheduledTaskPrincipal -UserId ($env:COMPUTERNAME+'\filecatdsa_r2') -LogonType Interactive -RunLevel Limited
$action=New-ScheduledTaskAction -Execute 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -Argument ('-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File '+$script)
$settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 30)
Register-ScheduledTask -TaskName $taskName -Principal $principal -Action $action -Settings $settings|Out-Null
$began=Get-Date;Start-ScheduledTask -TaskName $taskName
do{
 Start-Sleep -Milliseconds 500
 $state=Get-ScheduledTask -TaskName $taskName;$info=Get-ScheduledTaskInfo -TaskName $taskName
 if($info.LastRunTime-ge$began.AddSeconds(-2)-and$state.State-ne'Running'){
  $result=$info.LastTaskResult
  [ordered]@{name=$taskName;lastRunTime=$info.LastRunTime.ToString('o');terminalExit=$result;principal=$principal.UserId;logonType=[string]$principal.LogonType;runLevel=[string]$principal.RunLevel}|ConvertTo-Json|Set-Content (Join-Path $base 'task-outcome.json')
  Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
  exit $result
 }
}while((Get-Date)-lt$began.AddMinutes(31))
throw 'The actual scheduled test task is still live; inspect its handle without restarting'
'@
  $launch=$launch.Replace('@BASE@',$guest).Replace('@REV@',$Revision).Replace('@HASH@',$hash)
  $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($launch))
  & $v -T ws -vp a -gu Admin -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -EncodedCommand $encoded;Check 'Windows interactive candidate failed or busy'
 }else{
  & $v -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ($guest+'\run.ps1');Check 'Windows candidate failed or busy'
 }
}elseif($Platform-eq'mac'){
 $guest='/Users/benny/FileCatValidation/dsa-batch-20261107-'+$Revision;$key='C:\Users\marek\.ssh\windows_claude_ed25519'
 & ssh.exe -i $key -o BatchMode=yes benny@192.168.0.199 ('umask 077; mkdir -p '+$guest);Check 'Mac candidate directory failed'
 & scp.exe -O -i $key -o BatchMode=yes $archive ('benny@192.168.0.199:'+$guest+'/bundle.tgz');Check 'Mac candidate copy failed'
 & scp.exe -O -i $key -o BatchMode=yes ('.tmp/batch-mac-'+$Revision+'.sh') ('benny@192.168.0.199:'+$guest+'/run.sh');Check 'Mac runner copy failed'
 & ssh.exe -i $key -o BatchMode=yes benny@192.168.0.199 ('sh '+$guest+'/run.sh');Check 'Mac candidate failed or busy'
}else{
 $guest='/home/benny/FileCatValidation/dsa-batch-20261107-'+$Revision;$hostKey='SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE';$plink='C:\Program Files\PuTTY\plink.exe';$pscp='C:\Program Files\PuTTY\pscp.exe'
 & $plink -batch -pw a -hostkey $hostKey benny@192.168.58.129 ('umask 077; mkdir -p '+$guest);Check 'Ubuntu candidate directory failed'
 & $pscp -q -batch -pw a -hostkey $hostKey $archive ('benny@192.168.58.129:'+$guest+'/bundle.tgz');Check 'Ubuntu candidate copy failed'
 & $pscp -q -batch -pw a -hostkey $hostKey ('.tmp/batch-linux-'+$Revision+'.sh') ('benny@192.168.58.129:'+$guest+'/run.sh');Check 'Ubuntu runner copy failed'
 & $plink -batch -pw a -hostkey $hostKey benny@192.168.58.129 ('sh '+$guest+'/run.sh');Check 'Ubuntu candidate failed or busy'
}
