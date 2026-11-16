$ErrorActionPreference='Stop'
$stage=Join-Path $PWD '.tmp/main-sync-probe-r1106'
if(Test-Path -LiteralPath $stage){throw 'A fresh stage is required'}
New-Item -ItemType Directory -Path $stage|Out-Null
Copy-Item -LiteralPath 'FileCat.slnx' -Destination $stage
foreach($project in 'FileCat.Core.Tests','FileCat.WorkerProbe'){
 $target=Join-Path $stage ('tests/'+$project+'/bin/Debug/net10.0')
 New-Item -ItemType Directory -Path $target -Force|Out-Null
 Get-ChildItem -LiteralPath ('tests/'+$project+'/bin/Debug/net10.0') -File|Copy-Item -Destination $target
}
$coreStage=Join-Path $PWD '.tmp/batch-r1105-mac/core'
foreach($file in Get-ChildItem -LiteralPath $coreStage -File){
 $new=Join-Path $stage ('tests/FileCat.Core.Tests/bin/Debug/net10.0/'+$file.Name)
 if((Get-FileHash -LiteralPath $file.FullName).Hash-ne(Get-FileHash -LiteralPath $new).Hash){throw 'Core input differs from the original cohort'}
}
$portable=Join-Path $PWD '.tmp/main-sync-probe-r1106.tgz'
if(Test-Path -LiteralPath $portable){throw 'A fresh archive is required'}
& tar.exe -czf $portable -C $stage .
if($LASTEXITCODE){throw 'Probe archive failed'}
$hash=(Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash.ToLowerInvariant()
$unix=@'
set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() { pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; }
if busy; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
[ "$actual" = '@HASH@' ] || exit 74
umask 077
mkdir payload
tar -xzf bundle.tgz -C payload
if busy; then printf 'BUSY phase=core\n' > state.txt; exit 75; fi
printf 'RUNNING phase=core runnerPid=%s\n' "$$" > state.txt
cd payload/tests/FileCat.Core.Tests/bin/Debug/net10.0
set +e
"$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.ArchivePasswordCommandTests' -noLogo -noColor -maxThreads 2 -xml "$base/core-r1106.xml" > "$base/core-r1106.log" 2>&1
code=$?
set -e
printf 'FINISHED exit=%s\n' "$code" > "$base/state.txt"
tail -n 8 "$base/core-r1106.log"
exit "$code"
'@
$windows=@'
$ErrorActionPreference='Stop'
$base=$PSScriptRoot
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content (Join-Path $base 'state.txt');exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content (Join-Path $base 'state.txt')
if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Archive hash mismatch'}
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
'@
foreach($platform in 'windows','mac','linux'){
 $batch=Join-Path $PWD ('.tmp/batch-r1106-'+$platform)
 if(Test-Path -LiteralPath $batch){throw 'A fresh platform stage is required'}
 New-Item -ItemType Directory -Path $batch|Out-Null
 Get-ChildItem -LiteralPath $stage|Copy-Item -Destination $batch -Recurse
 Copy-Item -LiteralPath $portable -Destination ('.tmp/dsa-batch-'+$platform+'-r1106.tgz')
 $body=if($platform-eq'windows'){$windows}else{$unix}
 $ext=if($platform-eq'windows'){'ps1'}else{'sh'}
 [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/batch-'+$platform+'-r1106.'+$ext)),$body.Replace('@HASH@',$hash).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
}
[ordered]@{revision='r1106';original='r1105';coreInputsUnchanged=$true;workerProbeIncluded=$true;sha256=$hash;bytes=(Get-Item -LiteralPath $portable).Length;unpackedBytes=(Get-ChildItem -LiteralPath $stage -File -Recurse|Measure-Object Length -Sum).Sum}|ConvertTo-Json|Set-Content -LiteralPath .tmp/main-sync-probe-r1106.json
