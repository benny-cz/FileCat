param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,[Parameter(Mandatory=$true)][ValidateSet('mac','linux','windows')][string]$Platform)
$ErrorActionPreference='Stop'
function Check([string]$what){if($LASTEXITCODE){throw $what}}
$archive=Join-Path $PWD ('.tmp/dsa-batch-'+$Platform+'-'+$Revision+'.tgz')
if($Platform-eq'windows'){
 $v='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx';$guest='C:\FileCatDsa20261104R2\batch-'+$Revision
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a createDirectoryInGuest $vm $guest;Check 'Windows candidate directory failed'
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm $archive ($guest+'\bundle.tgz');Check 'Windows candidate copy failed'
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromHostToGuest $vm (Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')) ($guest+'\run.ps1');Check 'Windows runner copy failed'
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ($guest+'\run.ps1');Check 'Windows candidate failed or busy'
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
