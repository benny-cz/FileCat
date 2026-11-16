$ErrorActionPreference='Stop'
$base=$PSScriptRoot
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content (Join-Path $base 'state.txt');exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content (Join-Path $base 'state.txt')
if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'd150e55c5d5d62edaa14273a2557c0d53bb3db0e8b5e5976bfaff6cf8b602f31'){throw 'Archive hash mismatch'}
$payload=Join-Path $base 'payload'
if(Test-Path -LiteralPath $payload){throw 'A fresh payload is required'}
New-Item -ItemType Directory -Path $payload|Out-Null
& tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
if($LASTEXITCODE){throw 'Extraction failed'}
if(Busy){'BUSY phase=core'|Set-Content (Join-Path $base 'state.txt');exit 75}
('RUNNING phase=core runnerPid='+$PID)|Set-Content (Join-Path $base 'state.txt')
Set-Location -LiteralPath (Join-Path $payload 'tests/FileCat.Core.Tests/bin/Debug/net10.0')
$ErrorActionPreference='Continue'
& 'C:\Program Files\dotnet\dotnet.exe' FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.ArchivePasswordCommandTests' -noLogo -noColor -maxThreads 2 -xml (Join-Path $base 'core-r1106.xml') *> (Join-Path $base 'core-r1106.log')
$code=$LASTEXITCODE
('FINISHED exit='+$code)|Set-Content (Join-Path $base 'state.txt')
exit $code