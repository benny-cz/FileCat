param([ValidateSet('mac','linux','windows')][string]$Platform)
$ErrorActionPreference='Stop'
function Check { if($LASTEXITCODE){throw 'Receipt transfer failed'} }
if($Platform -eq 'windows'){
 $vr='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx'
 foreach($ext in 'xml','log'){
  & $vr -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost $vm ('C:\FileCatDsa20261104R2\render-r461.'+$ext) (Join-Path $PWD ('.tmp/windows-render-r461.'+$ext));Check
 }
 foreach($entry in @(@('native-inventory-result.json','windows-native-inventory-r462.json'),@('native-inventory-window-render.png','windows-native-inventory-r462.png'))){
  & $vr -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost $vm ('C:\FileCatDsa20261104R2\desktop-r462-inventory\'+$entry[0]) (Join-Path $PWD ('.tmp/'+$entry[1]));Check
 }
}else{
 $tag=if($Platform -eq 'linux'){'ubuntu'}else{'mac'}
 $homeRoot=if($Platform -eq 'linux'){'/home/benny'}else{'/Users/benny'}
 $native=$homeRoot+'/FileCatValidation/dsa-20261106-r462-inventory-'+$Platform
 $render=$homeRoot+'/FileCatValidation/dsa-20261105-r461-render'
 foreach($entry in @(@(($render+'/render-r461.xml'),($tag+'-render-r461.xml')),@(($render+'/render-r461.log'),($tag+'-render-r461.log')),@(($native+'/native-inventory-result.json'),($tag+'-native-inventory-r462.json')),@(($native+'/native-inventory-window-render.png'),($tag+'-native-inventory-r462.png')))){
  if($Platform -eq 'mac'){
   & scp.exe -O -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes ('benny@192.168.0.199:'+$entry[0]) ('.tmp/'+$entry[1]);Check
  }else{
   & 'C:\Program Files\PuTTY\pscp.exe' -q -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' ('benny@192.168.58.129:'+$entry[0]) ('.tmp/'+$entry[1]);Check
  }
 }
}
