param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,[Parameter(Mandatory=$true)][ValidateSet('mac','linux','windows')][string]$Platform)
$ErrorActionPreference='Stop'
function Check {if($LASTEXITCODE){throw 'Candidate evidence copy failed'}}
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
if($Platform-eq'windows'){
 $v='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx';$guest='C:\FileCatDsa20261104R2\batch-'+$Revision
 foreach($file in @('state.txt')+@('core','native','render'|ForEach-Object{$_+'-'+$Revision+'.xml';$_+'-'+$Revision+'.log'})){
  & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost $vm ($guest+'\'+$file) (Join-Path $PWD ('.tmp/'+$tag+'-batch-'+$Revision+'-'+$file));Check
 }
 $raw=@(& $v -T ws -vp a -gu Admin -gp a listProcessesInGuest $vm);Check
 [ordered]@{observedUtc=[DateTime]::UtcNow.ToString('o');exitCode=0;filecatOrDotnet=@($raw|Where-Object{$_-match '^pid=.*cmd=.*(FileCat|dotnet)'})}|ConvertTo-Json|Set-Content ('.tmp/'+$tag+'-batch-'+$Revision+'-post.json')
}else{
 $guest=if($Platform-eq'mac'){'/Users/benny/FileCatValidation/dsa-batch-20261107-'+$Revision}else{'/home/benny/FileCatValidation/dsa-batch-20261107-'+$Revision}
 foreach($file in @('state.txt')+@('core','render'|ForEach-Object{$_+'-'+$Revision+'.xml';$_+'-'+$Revision+'.log'})){
  $dest='.tmp/'+$tag+'-batch-'+$Revision+'-'+$file
  if($Platform-eq'mac'){& scp.exe -O -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes ('benny@192.168.0.199:'+$guest+'/'+$file) $dest}
  else{& 'C:\Program Files\PuTTY\pscp.exe' -q -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' ('benny@192.168.58.129:'+$guest+'/'+$file) $dest};Check
 }
 $script=@'
import datetime,json,subprocess
rows=[]
for args in [['pgrep','-x','FileCat([.].*)?'],['pgrep','-f','^[^ ]*dotnet +.*FileCat']]:
 p=subprocess.run(args,text=True,capture_output=True)
 if p.returncode not in (0,1):raise RuntimeError(p.stderr)
 for pid in p.stdout.split():
  state=subprocess.run(['ps','-p',pid,'-o','pid=,ppid=,args='],text=True,capture_output=True)
  if state.returncode not in (0,1):raise RuntimeError(state.stderr)
  rows.extend(state.stdout.splitlines())
print(json.dumps({'observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'exitCode':0,'filecatOrDotnet':sorted(set(rows))},indent=2))
'@
 $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script));$command='python3 -c ''import base64;exec(base64.b64decode("'+$encoded+'"))'''
 if($Platform-eq'mac'){$post=& ssh.exe -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes benny@192.168.0.199 $command}
 else{$post=& 'C:\Program Files\PuTTY\plink.exe' -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' benny@192.168.58.129 $command};Check
 $post|Set-Content ('.tmp/'+$tag+'-batch-'+$Revision+'-post.json')
}
