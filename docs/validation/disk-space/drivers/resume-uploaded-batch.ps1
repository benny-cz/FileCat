param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidateSet('mac','linux','windows')][string]$Platform
)
$ErrorActionPreference='Stop'
# Use only after the original deployment handle has reached a terminal BUSY
# phase=verify result. The guest runner independently refuses existing payloads
# and checks for other instances before extraction and before every phase.
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
$state=Join-Path $PWD ('.tmp/'+$tag+'-batch-'+$Revision+'-state.txt')
if(!(Test-Path -LiteralPath $state)-or([IO.File]::ReadAllText($state).Trim())-cne'BUSY phase=verify'){
 throw 'Resume requires a collected terminal busy refusal before payload extraction'
}
$extension=if($Platform-eq'windows'){'ps1'}else{'sh'}
$runner=Join-Path $PWD ('.tmp/batch-'+$Platform+'-'+$Revision+'.'+$extension)
$bundle=Join-Path $PWD ('.tmp/dsa-batch-'+$Platform+'-'+$Revision+'.tgz')
$runnerHash=(Get-FileHash -LiteralPath $runner -Algorithm SHA256).Hash.ToLowerInvariant()
$bundleHash=(Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant()
if($Platform-eq'windows'){
 $guest='C:\FileCatDsa20261104R2\batch-'+$Revision
 $command=@'
$ErrorActionPreference='Stop'
$base='@BASE@'
if(Test-Path -LiteralPath (Join-Path $base 'payload')){throw 'Existing payload cannot be resumed by this driver'}
if((Get-FileHash -LiteralPath (Join-Path $base 'run.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()-cne'@RUNNER@'){throw 'Uploaded runner differs'}
if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash.ToLowerInvariant()-cne'@BUNDLE@'){throw 'Uploaded bundle differs'}
& (Join-Path $base 'run.ps1')
'@
 $command=$command.Replace('@BASE@',$guest).Replace('@RUNNER@',$runnerHash).Replace('@BUNDLE@',$bundleHash)
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
 & 'C:\Program Files\VMware\VMware Workstation\vmrun.exe' -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest 'V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx' 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand $encoded
}else{
 $guest=if($Platform-eq'mac'){'/Users/benny/FileCatValidation/dsa-batch-20261107-'+$Revision}else{'/home/benny/FileCatValidation/dsa-batch-20261107-'+$Revision}
 $command=@'
import hashlib,pathlib,subprocess
base=pathlib.Path('@BASE@')
if (base/'payload').exists():raise RuntimeError('Existing payload cannot be resumed by this driver')
for name,expected in [('run.sh','@RUNNER@'),('bundle.tgz','@BUNDLE@')]:
 h=hashlib.sha256()
 with (base/name).open('rb') as stream:
  for block in iter(lambda:stream.read(1048576),b''):h.update(block)
 if h.hexdigest()!=expected:raise RuntimeError('Uploaded input differs: '+name)
raise SystemExit(subprocess.run(['sh',str(base/'run.sh')]).returncode)
'@
 $command=$command.Replace('@BASE@',$guest).Replace('@RUNNER@',$runnerHash).Replace('@BUNDLE@',$bundleHash)
 $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($command))
 $remote='python3 -c ''import base64;exec(base64.b64decode("'+$encoded+'"))'''
 if($Platform-eq'mac'){& ssh.exe -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes benny@192.168.0.199 $remote}
 else{& 'C:\Program Files\PuTTY\plink.exe' -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' benny@192.168.58.129 $remote}
}
if($LASTEXITCODE){throw 'Uploaded cohort failed or postponed; retain this original handle through actual completion before collecting or retrying'}
