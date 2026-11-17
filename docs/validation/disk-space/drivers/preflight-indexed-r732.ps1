param([ValidateSet('mac','linux','windows')][string]$Platform,[string]$Revision='r732')
$ErrorActionPreference='Stop'
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
if($Platform-eq'windows'){
 $inventoryAccount='filecatdsa_r2'
 $raw=@(& 'C:\Program Files\VMware\VMware Workstation\vmrun.exe' -T ws -vp a -gu $inventoryAccount -gp a listProcessesInGuest 'V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx')
 if($LASTEXITCODE){
  $inventoryAccount='Admin'
  $raw=@(& 'C:\Program Files\VMware\VMware Workstation\vmrun.exe' -T ws -vp a -gu $inventoryAccount -gp a listProcessesInGuest 'V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx')
 }
 if($LASTEXITCODE){throw 'Windows inventory failed'}
 $data=[ordered]@{observedUtc=[DateTime]::UtcNow.ToString('o');exitCode=0;inventoryAccount=$inventoryAccount;filecatOrDotnet=@($raw|Where-Object{$_-match '^pid=.*cmd=.*(FileCat|dotnet)'})}
 $json=$data|ConvertTo-Json -Depth 5
}else{
 $probe=@'
import datetime,json,subprocess
rows=[]
for args in [['pgrep','-x','FileCat([.].*)?'],['pgrep','-x','dotnet']]:
 p=subprocess.run(args,text=True,capture_output=True)
 if p.returncode not in (0,1):raise RuntimeError(p.stderr)
 for pid in p.stdout.split():
  state=subprocess.run(['ps','-p',pid,'-o','pid=,ppid=,args='],text=True,capture_output=True)
  if state.returncode not in (0,1):raise RuntimeError(state.stderr)
  rows.extend(state.stdout.splitlines())
print(json.dumps({'observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'exitCode':0,'filecatOrDotnet':sorted(set(rows))},indent=2))
'@
 $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($probe));$command='python3 -c ''import base64;exec(base64.b64decode("'+$encoded+'"))'''
 if($Platform-eq'mac'){$json=& ssh.exe -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes -o ConnectTimeout=12 benny@192.168.0.199 $command}
 else{$json=& 'C:\Program Files\PuTTY\plink.exe' -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' benny@192.168.58.129 $command}
 if($LASTEXITCODE){throw 'Unix inventory failed'}
 $data=($json -join "`n")|ConvertFrom-Json
}
$json|Set-Content ('.tmp/'+$tag+'-pre-'+$Revision+'.json')
Write-Output ($tag+' processes='+@($data.filecatOrDotnet).Count)
if(@($data.filecatOrDotnet).Count){exit 75}
