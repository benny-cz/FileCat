param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidateSet('mac','linux','windows')][string]$Platform
)
$ErrorActionPreference='Stop'
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
$archive=Join-Path $PWD ('.tmp/'+$tag+'-native-'+$Revision+'-results.tgz')
if(Test-Path -LiteralPath $archive){throw 'Fresh result archive required'}
function Check([string]$message){if($LASTEXITCODE){throw $message}}
if($Platform-eq'windows'){
 & 'C:\Program Files\VMware\VMware Workstation\vmrun.exe' -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost 'V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx' ('C:\FileCatDsa20261104R2\native-'+$Revision+'\results.tgz') $archive;Check 'Windows native collection failed'
}elseif($Platform-eq'mac'){
 & scp.exe -O -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes ('benny@192.168.0.199:/Users/benny/FileCatValidation/dsa-native-'+$Revision+'/results.tgz') $archive;Check 'Mac native collection failed'
}else{
 & 'C:\Program Files\PuTTY\pscp.exe' -q -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' ('benny@192.168.58.129:/home/benny/FileCatValidation/dsa-native-'+$Revision+'/results.tgz') $archive;Check 'Ubuntu native collection failed'
}
$extract=@'
import json,pathlib,sys,tarfile
source=pathlib.Path(sys.argv[1]);target=pathlib.Path(sys.argv[2])
assert not target.exists()
with tarfile.open(source) as archive:
 members=archive.getmembers()
 assert len(members)<50 and sum(m.size for m in members)<64*1024**2
 assert all(m.isfile() and len(pathlib.PurePosixPath(m.name).parts)==1 and ':' not in m.name and m.name not in ('.','..') for m in members)
 archive.extractall(target,filter='data')
data=json.loads((target/'result.json').read_text(encoding='utf-8-sig'))
print(json.dumps({k:data.get(k) for k in ('passed','error','exitCode','postProcesses')}))
'@
$extract|python - $archive ($archive-replace'-results.tgz$','-results');Check 'Native result validation failed'
