param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidateSet('mac','linux')][string]$Platform
)
$ErrorActionPreference='Stop'
& pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'preflight-indexed-r732.ps1') -Platform $Platform -Revision ($Revision+'check')
if($LASTEXITCODE-eq75){exit 75};if($LASTEXITCODE){throw 'Native preflight failed'}
$archive=Join-Path $PWD ('.tmp/desktop-'+$Revision+'-'+$Platform+'.tgz')
$hash=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
$driver=Join-Path $PSScriptRoot 'native-replacement-ui.py'
function Check([string]$message){if($LASTEXITCODE){throw $message}}
if($Platform-eq'mac'){
 $base='/Users/benny/FileCatValidation/dsa-native-'+$Revision
 $key='C:\Users\marek\.ssh\windows_claude_ed25519'
 & ssh.exe -i $key -o BatchMode=yes benny@192.168.0.199 ('umask 077; mkdir '+$base);Check 'Fresh Mac directory failed'
 & scp.exe -O -i $key -o BatchMode=yes $archive ('benny@192.168.0.199:'+$base+'/app.tgz');Check 'Mac archive copy failed'
 & scp.exe -O -i $key -o BatchMode=yes $driver ('benny@192.168.0.199:'+$base+'/run.py');Check 'Mac driver copy failed'
 & ssh.exe -i $key -o BatchMode=yes benny@192.168.0.199 ('python3 '+$base+'/run.py '+$base+' '+$hash);Check 'Mac native run failed'
}else{
 $base='/home/benny/FileCatValidation/dsa-native-'+$Revision
 $hostKey='SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE'
 $plink='C:\Program Files\PuTTY\plink.exe';$pscp='C:\Program Files\PuTTY\pscp.exe'
 & $plink -batch -pw a -hostkey $hostKey benny@192.168.58.129 ('umask 077; mkdir '+$base);Check 'Fresh Ubuntu directory failed'
 & $pscp -q -batch -pw a -hostkey $hostKey $archive ('benny@192.168.58.129:'+$base+'/app.tgz');Check 'Ubuntu archive copy failed'
 & $pscp -q -batch -pw a -hostkey $hostKey $driver ('benny@192.168.58.129:'+$base+'/run.py');Check 'Ubuntu driver copy failed'
 & $plink -batch -pw a -hostkey $hostKey benny@192.168.58.129 ('python3 '+$base+'/run.py '+$base+' '+$hash);Check 'Ubuntu native run failed'
}
