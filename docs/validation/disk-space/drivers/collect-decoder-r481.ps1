param([ValidateSet('mac','linux','windows')][string]$Platform)
$ErrorActionPreference='Stop'
function Check { if($LASTEXITCODE){throw 'Evidence collection failed'} }
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
if($Platform-eq'windows'){
 $vr='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx'
 foreach($kind in 'core','native','render'){
  $revision=switch($kind){'core'{'r479'};'native'{'r479'};'render'{'r480'}}
  $guest=if($kind-eq'core'){'core-all'}else{$kind}
  foreach($ext in 'xml','log'){
   & $vr -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost $vm ('C:\FileCatDsa20261104R2\'+$guest+'-'+$revision+'.'+$ext) (Join-Path $PWD ('.tmp/windows-'+$kind+'-'+$revision+'.'+$ext));Check
  }
 }
 $raw=@(& $vr -T ws -vp a -gu Admin -gp a listProcessesInGuest $vm);$code=$LASTEXITCODE;Check
 [ordered]@{observedUtc=[DateTime]::UtcNow.ToString('o');exitCode=$code;filecatOrDotnet=@($raw|Where-Object{$_-match '^pid=.*cmd=.*(FileCat|dotnet)'})}|ConvertTo-Json|Set-Content .tmp/windows-post-decoder-r481.json
}else{
 $remote=if($Platform-eq'mac'){'/Users/benny/FileCatValidation/dsa-20261105-'}else{'/home/benny/FileCatValidation/dsa-20261105-'}
 foreach($kind in 'core','render'){
  $revision=switch($kind){'core'{'r479'};'render'{'r480'}}
  $folder=if($kind-eq'render'){'r480-render'}else{'r479-core'}
  foreach($ext in 'xml','log'){
   $source=$remote+$folder+'/'+$kind+'-'+$revision+'.'+$ext;$destination='.tmp/'+$tag+'-'+$kind+'-'+$revision+'.'+$ext
   if($Platform-eq'mac'){& scp.exe -O -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes ('benny@192.168.0.199:'+$source) $destination}
   else{& 'C:\Program Files\PuTTY\pscp.exe' -q -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' ('benny@192.168.58.129:'+$source) $destination};Check
  }
 }
 $script=@'
import datetime,json,subprocess
groups={}
for name,args in [('apps',['pgrep','-a','-x','FileCat([.].*)?']),('tests',['pgrep','-a','-f','^[^ ]*dotnet +.*FileCat'])]:
 r=subprocess.run(args,text=True,capture_output=True)
 if r.returncode not in (0,1):raise RuntimeError(r.stderr)
 groups[name]=r.stdout.splitlines()
print(json.dumps({'observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'exitCode':0,'processes':groups},indent=2))
'@
 $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))
 $command='python3 -c ''import base64;exec(base64.b64decode("'+$encoded+'"))'''
 if($Platform-eq'mac'){$result=& ssh.exe -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes benny@192.168.0.199 $command}
 else{$result=& 'C:\Program Files\PuTTY\plink.exe' -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' benny@192.168.58.129 $command};Check
 $result|Set-Content ('.tmp/'+$tag+'-post-decoder-r481.json')
}
