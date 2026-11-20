$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'render-state-r292.txt'
function Gate { if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){'BUSY'|Set-Content $state;exit 75} }
try {
 Gate
 $archive=Join-Path $root 'render-r292.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne '44da9b2a21ac5916de711591e36f8dc1a679d60a35db57b88b2aecc16659c55d'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'render-r292';New-Item -ItemType Directory -Force $bin|Out-Null
 & tar.exe -xzf $archive -C $bin
 if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
 Gate
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Rendering.Tests.dll') -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'render-r292.xml') *> (Join-Path $root 'render-r292.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
 ('FINISHED exit='+$result)|Set-Content $state
 exit $result
} catch {($_|Out-String)|Set-Content (Join-Path $root 'render-failure-r292.txt');'FAILED'|Set-Content $state;exit 1}