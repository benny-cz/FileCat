param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,[Parameter(Mandatory=$true)][ValidateSet('mac','linux','windows')][string]$Platform,[switch]$InteractiveWindows,
 [ValidateRange(5,240)][int]$WindowsTaskLimitMinutes=30,[switch]$ReuseUploadedWindows)
$ErrorActionPreference='Stop'
function Check([string]$what){if($LASTEXITCODE){throw $what}}
if ($ReuseUploadedWindows -and $Platform -ne 'windows') { throw 'Uploaded batch reuse is available for the Windows VM.' }
$archive=Join-Path $PWD ('.tmp/dsa-batch-'+$Platform+'-'+$Revision+'.tgz')
if($Platform-eq'windows'){
 $v='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx';$guest='C:\FileCatDsa20261104R2\batch-'+$Revision
 if($ReuseUploadedWindows){
  $previous=Join-Path $PWD ('.tmp/windows-batch-'+$Revision+'-state.txt')
  if(!(Test-Path -LiteralPath $previous)-or([IO.File]::ReadAllText($previous).Trim())-cne'BUSY phase=verify'){
   throw 'Reuse requires a retained terminal busy refusal before launch and extraction'
  }
  $bundleHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
  $runnerHash=(Get-FileHash -LiteralPath (Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')) -Algorithm SHA256).Hash
  $verify=@'
$ErrorActionPreference='Stop'
$base='@BASE@'
if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){exit 75}
if(([IO.File]::ReadAllText((Join-Path $base 'state.txt')).Trim())-cne'BUSY phase=verify'){throw 'Uploaded batch state changed'}
foreach($name in 'app','input','payload','owned.pid','driver'){if(Test-Path -LiteralPath (Join-Path $base $name)){throw 'The previous launch already created a payload'}}
if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@BUNDLE@'){throw 'Uploaded bundle changed'}
if((Get-FileHash -LiteralPath (Join-Path $base 'run.ps1') -Algorithm SHA256).Hash-ne'@RUNNER@'){throw 'Uploaded runner changed'}
'@
  $verify=$verify.Replace('@BASE@',$guest).Replace('@BUNDLE@',$bundleHash).Replace('@RUNNER@',$runnerHash)
  $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($verify))
  & $v -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -EncodedCommand $encoded
  Check 'Windows uploaded batch verification failed or the guest is busy'
 }else{
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a createDirectoryInGuest $vm $guest;Check 'Windows candidate directory failed'
 # Refuse a predictable reserve failure before copying/extracting a large payload.
 $stage=Join-Path $PWD ('.tmp/batch-'+$Revision+'-windows')
 $extra=[long](Get-Item -LiteralPath $archive).Length
 if(Test-Path -LiteralPath (Join-Path $PWD ('.tmp/cache-input-'+$Revision))){$extra+=32MB}
 elseif(Test-Path -LiteralPath $stage){$extra+=[long](Get-ChildItem -LiteralPath $stage -File -Recurse|Measure-Object Length -Sum).Sum}
 else{throw 'Windows payload size is unknown; prepare a verified full stage or cache plan'}
 $space=@'
$ErrorActionPreference='Stop'
if ('@INTERACTIVE@' -eq 'yes') {
 $expected=$env:COMPUTERNAME+'\filecatdsa_r2'
 $console=(Get-CimInstance Win32_ComputerSystem).UserName
 [ordered]@{expected=$expected;console=$console;available=($console -eq $expected)}|ConvertTo-Json|Set-Content -LiteralPath '@GUEST@\interactive-preflight.json'
 if ($console -ne $expected) {
  'FAILED phase=interactive-account'|Set-Content -LiteralPath '@GUEST@\state.txt'
  exit 76
 }
}
$drive=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
$reserve=[long][Math]::Min(2GB,[Math]::Max(256MB,[Math]::Ceiling($drive.Size/100.0)))
$required=$reserve+[long]@EXTRA@+256MB
[ordered]@{available=[long]$drive.FreeSpace;required=$required;reserve=$reserve;incomingBytes=[long]@EXTRA@}|ConvertTo-Json|Set-Content -LiteralPath '@GUEST@\space-preflight.json'
if($drive.FreeSpace-lt$required){exit 74}
'@
 $interactive=if($InteractiveWindows){'yes'}else{'no'}
 $space=$space.Replace('@EXTRA@',[string]$extra).Replace('@GUEST@',$guest).Replace('@INTERACTIVE@',$interactive)
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($space))
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -EncodedCommand $encoded;Check 'Windows placement preflight failed before transfer; inspect interactive-preflight.json or space-preflight.json'
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm $archive ($guest+'\bundle.tgz');Check 'Windows candidate copy failed'
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm (Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')) ($guest+'\run.ps1');Check 'Windows runner copy failed'
 }
 if($InteractiveWindows){
  $hash=(Get-FileHash -LiteralPath (Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')) -Algorithm SHA256).Hash
  $launch=@'
$ErrorActionPreference='Stop'
$base='@BASE@';$taskName='FileCatDsaR2-@REV@';$script=Join-Path $base 'run.ps1'
if((Get-FileHash -LiteralPath $script -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Runner changed'}
if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue){throw 'A fresh test task is required'}
if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){'BUSY phase=verify'|Set-Content (Join-Path $base 'state.txt');exit 75}
$expected=$env:COMPUTERNAME+'\filecatdsa_r2'
if ((Get-CimInstance Win32_ComputerSystem).UserName -ne $expected) {
 'FAILED phase=interactive-account'|Set-Content (Join-Path $base 'state.txt')
 exit 76
}
$principal=New-ScheduledTaskPrincipal -UserId $expected -LogonType Interactive -RunLevel Limited
$action=New-ScheduledTaskAction -Execute 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -Argument ('-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File '+$script)
$settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes @TASKLIMIT@)
Register-ScheduledTask -TaskName $taskName -Principal $principal -Action $action -Settings $settings|Out-Null
$began=Get-Date;Start-ScheduledTask -TaskName $taskName
do{
 Start-Sleep -Milliseconds 500
 $state=Get-ScheduledTask -TaskName $taskName;$info=Get-ScheduledTaskInfo -TaskName $taskName
 if ($state.State -eq 'Ready' -and $info.LastTaskResult -eq 0x41303 -and
     (Get-Date) -ge $began.AddSeconds(30) -and (Get-CimInstance Win32_ComputerSystem).UserName -ne $expected) {
  'FAILED phase=interactive-account'|Set-Content (Join-Path $base 'state.txt')
  Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
  exit 76
 }
 if($info.LastRunTime-ge$began.AddSeconds(-2)-and$state.State-ne'Running'){
  $result=$info.LastTaskResult
  [ordered]@{name=$taskName;lastRunTime=$info.LastRunTime.ToString('o');terminalExit=$result;principal=$principal.UserId;logonType=[string]$principal.LogonType;runLevel=[string]$principal.RunLevel;timeLimitMinutes=@TASKLIMIT@}|ConvertTo-Json|Set-Content (Join-Path $base 'task-outcome.json')
  Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
  exit $result
 }
}while((Get-Date)-lt$began.AddMinutes(@CONTROLLIMIT@))
throw 'The actual scheduled test task is still live; inspect its handle without restarting'
'@
  $launch=$launch.Replace('@BASE@',$guest).Replace('@REV@',$Revision).Replace('@HASH@',$hash).Replace('@TASKLIMIT@',[string]$WindowsTaskLimitMinutes).Replace('@CONTROLLIMIT@',[string]($WindowsTaskLimitMinutes+1))
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
