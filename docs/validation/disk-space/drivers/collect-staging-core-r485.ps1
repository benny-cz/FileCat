param([ValidateSet('mac','linux','windows')][string]$Platform)
$ErrorActionPreference='Stop'
function Check { if($LASTEXITCODE){throw 'Core evidence collection failed'} }
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
foreach($ext in 'xml','log'){
 $destination=Join-Path $PWD ('.tmp/'+$tag+'-core-r485.'+$ext)
 if($Platform-eq'windows'){
  & 'C:\Program Files\VMware\VMware Workstation\vmrun.exe' -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost 'V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx' ('C:\FileCatDsa20261104R2\core-all-r485.'+$ext) $destination
 }elseif($Platform-eq'mac'){
  & scp.exe -O -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes ('benny@192.168.0.199:/Users/benny/FileCatValidation/dsa-20261105-r485-core/core-r485.'+$ext) $destination
 }else{
  & 'C:\Program Files\PuTTY\pscp.exe' -q -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' ('benny@192.168.58.129:/home/benny/FileCatValidation/dsa-20261105-r485-core/core-r485.'+$ext) $destination
 };Check
}
