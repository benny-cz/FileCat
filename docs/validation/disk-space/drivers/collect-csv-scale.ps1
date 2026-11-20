param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,[Parameter(Mandatory=$true)][ValidateSet('windows','mac','linux')][string]$Platform)
$ErrorActionPreference='Stop';$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform};$archive=Join-Path $PWD ('.tmp/'+$tag+'-csv-scale-'+$Revision+'-results.tgz')
if(Test-Path -LiteralPath $archive){throw 'Collect each terminal cohort once'}
function Check([string]$message){if($LASTEXITCODE){throw $message}}
$original=Get-Content -LiteralPath ('.tmp/'+$tag+'-csv-scale-'+$Revision+'-original.json') -Raw|ConvertFrom-Json
if($original.state-cne'original-deployment-terminal'){throw 'Wait for the original deployment to actually return before collection'}
if($Platform-eq'windows'){
 $v='C:\Program Files\VMware\VMware Workstation\vmrun.exe';$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx';$guest='C:\FileCatDsa20261104R2\csv-scale-'+$Revision
 $command="Set-Location -LiteralPath '"+$guest+"'; `$files=@(Get-ChildItem -File|Where-Object{`$_.Name -match '^(state[.]json|result[.]json|[0-9]+-(wide|deep)[.](out|err))$'}|ForEach-Object{`$_.Name}); & tar.exe -czf results.tgz @files; exit `$LASTEXITCODE"
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -EncodedCommand $encoded;Check 'Windows scale archive failed'
 & $v -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost $vm ($guest+'\results.tgz') $archive;Check 'Windows scale collection failed'
}else{
 $guest=if($Platform-eq'mac'){'/Users/benny/FileCatValidation/csv-scale-'+$Revision}else{'/home/benny/FileCatValidation/csv-scale-'+$Revision}
 $command='cd '+$guest+'; tar -czf results.tgz state.json result.json *.out *.err'
 if($Platform-eq'mac'){
  & ssh.exe -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes benny@192.168.0.199 $command;Check 'Mac scale archive failed'
  & scp.exe -O -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes ('benny@192.168.0.199:'+$guest+'/results.tgz') $archive;Check 'Mac scale collection failed'
 }else{
  & 'C:\Program Files\PuTTY\plink.exe' -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' benny@192.168.58.129 $command;Check 'Ubuntu scale archive failed'
  & 'C:\Program Files\PuTTY\pscp.exe' -q -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' ('benny@192.168.58.129:'+$guest+'/results.tgz') $archive;Check 'Ubuntu scale collection failed'
 }
}
@'
import pathlib,sys,tarfile
source=pathlib.Path(sys.argv[1]);target=pathlib.Path(sys.argv[2]);assert not target.exists()
with tarfile.open(source) as archive:
 members=archive.getmembers();assert len(members)<=100 and sum(m.size for m in members)<=16*1024*1024
 for member in members:
  name=pathlib.PurePosixPath(member.name);assert not name.is_absolute() and '..' not in name.parts and ':' not in member.name and member.isfile()
 archive.extractall(target,filter='data')
'@|python - $archive ('.tmp/'+$tag+'-csv-scale-'+$Revision+'-results')
Check 'Scale receipt extraction failed'
& pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'preflight-indexed-r732.ps1') -Platform $Platform -Revision ($Revision+'scalepost')
if($LASTEXITCODE-notin0,75){throw 'Post-run inventory failed'}
Copy-Item -LiteralPath ('.tmp/'+$tag+'-pre-'+$Revision+'scalepost.json') -Destination ('.tmp/'+$tag+'-csv-scale-'+$Revision+'-post.json')
Write-Output ('Collected one '+$tag+' scale archive: '+(Get-Item -LiteralPath $archive).Length+' bytes')
