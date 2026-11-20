param([ValidateSet('mac','linux')][string]$Platform)
$ErrorActionPreference='Stop'
$script=[IO.File]::ReadAllText((Join-Path $PWD '.tmp/inspect-decoder-processes-r481.py'))
$encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))
$command='python3 -c ''import base64;exec(base64.b64decode("'+$encoded+'"))'''
if($Platform-eq'mac'){$result=& ssh.exe -i 'C:\Users\marek\.ssh\windows_claude_ed25519' -o BatchMode=yes benny@192.168.0.199 $command;$tag='mac'}
else{$result=& 'C:\Program Files\PuTTY\plink.exe' -batch -pw a -hostkey 'SHA256:UeJys5LrKbfIVTAoZVRIjZh344WfGGWfFF5gTwC05oE' benny@192.168.58.129 $command;$tag='ubuntu'}
if($LASTEXITCODE){throw 'Process observation failed'}
$result|Set-Content ('.tmp/'+$tag+'-decoder-process-identities-r481.json')
$result
