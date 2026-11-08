param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidateSet('mac','linux','windows')][string]$Platform
)
$ErrorActionPreference='Stop'
function Check([string]$message){if($LASTEXITCODE){throw $message}}
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
$archive=Join-Path $PWD ('.tmp/'+$tag+'-batch-'+$Revision+'-results.tgz')
if(Test-Path -LiteralPath $archive){throw 'A fresh result archive is required'}
if($Platform-eq'windows'){
 $vmrun='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx'
 $guest='C:\FileCatDsa20261104R2\batch-'+$Revision
 $command=@'
$ErrorActionPreference='Stop'
$base='@BASE@'
Set-Location -LiteralPath $base
$names=@(Get-ChildItem -LiteralPath $base -File|Where-Object{$_.Name -match '^(state[.]txt|failure[.]txt|cache-proof[.]txt|space-preflight[.]json|(smoke|preview|core|native|render)-@REV@[.](xml|log))$'}|ForEach-Object{$_.Name})
if(Test-Path -LiteralPath 'payload/render/comparison-growth-renders'){$names+='payload/render/comparison-growth-renders'}
else{
 foreach($control in @('cache-input/plan.json','patch-input/plan.json')){
  if(!(Test-Path -LiteralPath $control)){continue}
  $cached=(Get-Content -LiteralPath $control -Raw|ConvertFrom-Json).baseline
  if($cached-cnotmatch'^C:\\FileCatDsa20261104R2\\batch-r[0-9]+[a-z]?\\payload$'){throw 'Unexpected cached render location'}
  $renders=Join-Path $cached 'render/comparison-growth-renders'
  if(Test-Path -LiteralPath $renders){$names+=@('-C',$cached,'render/comparison-growth-renders')}
  break
 }
}
if(!$names.Contains('state.txt')){throw 'Runner state is missing'}
& tar.exe -czf (Join-Path $base 'results.tgz') @names
if($LASTEXITCODE){throw 'Result archive failed'}
'@
 $command=$command.Replace('@BASE@',$guest).Replace('@REV@',$Revision)
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
 & $vmrun -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -EncodedCommand $encoded;Check 'Windows result archive failed'
 & $vmrun -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost $vm ($guest+'\results.tgz') $archive;Check 'Windows result collection failed'
}else{
 $guest=if($Platform-eq'mac'){'/Users/benny/FileCatValidation/dsa-batch-20261107-'+$Revision}else{'/home/benny/FileCatValidation/dsa-batch-20261107-'+$Revision}
 $command='cd '+$guest+'; set -eu; set -- state.txt; for name in failure.txt cache-proof.txt smoke-'+$Revision+'.xml smoke-'+$Revision+'.log preview-'+$Revision+'.xml preview-'+$Revision+'.log core-'+$Revision+'.xml core-'+$Revision+'.log native-'+$Revision+'.xml native-'+$Revision+'.log render-'+$Revision+'.xml render-'+$Revision+'.log; do if [ -f "$name" ]; then set -- "$@" "$name"; fi; done; if [ -d payload/render/comparison-growth-renders ]; then set -- "$@" payload/render/comparison-growth-renders; fi; tar -czf results.tgz "$@"'
 if($Platform-eq'mac'){
  & ssh.exe -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes benny@192.168.0.199 $command;Check 'Mac result archive failed'
  & scp.exe -O -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes ('benny@192.168.0.199:'+$guest+'/results.tgz') $archive;Check 'Mac result collection failed'
 }else{
  & 'C:\Program Files\PuTTY\plink.exe' -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' benny@192.168.58.129 $command;Check 'Ubuntu result archive failed'
  & 'C:\Program Files\PuTTY\pscp.exe' -q -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' ('benny@192.168.58.129:'+$guest+'/results.tgz') $archive;Check 'Ubuntu result collection failed'
 }
}
$extract=@'
import pathlib,sys,tarfile
source=pathlib.Path(sys.argv[1]);target=pathlib.Path(sys.argv[2])
if target.exists():raise RuntimeError('Fresh extraction directory required')
with tarfile.open(source) as archive:
 members=archive.getmembers()
 if len(members)>1000 or sum(m.size for m in members)>256*1024*1024:raise RuntimeError('Result archive limit')
 for m in members:
  name=pathlib.PurePosixPath(m.name)
  if name.is_absolute() or '..' in name.parts or ':' in m.name or not(m.isfile() or m.isdir()):raise RuntimeError('Invalid result archive member')
 archive.extractall(target,filter='data')
'@
$stage=Join-Path $PWD ('.tmp/'+$tag+'-batch-'+$Revision+'-results')
$extract|python - $archive $stage;Check 'Result archive validation/extraction failed'
Get-ChildItem -LiteralPath $stage -File|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $PWD ('.tmp/'+$tag+'-batch-'+$Revision+'-'+$_.Name))}
& pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'preflight-indexed-r732.ps1') -Platform $Platform -Revision ($Revision+'post')
if($LASTEXITCODE -notin 0,75){throw 'Post-run process inventory failed'}
Copy-Item -LiteralPath ('.tmp/'+$tag+'-pre-'+$Revision+'post.json') -Destination ('.tmp/'+$tag+'-batch-'+$Revision+'-post.json')
Write-Output ('Collected one '+$tag+' archive: '+(Get-Item -LiteralPath $archive).Length+' bytes')
