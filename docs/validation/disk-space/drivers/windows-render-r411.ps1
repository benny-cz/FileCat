$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'render-state-r411.txt'
function Gate { if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75} }
try {
 Gate
 $archive=Join-Path $root 'render-r411.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne '04af668c312708a5cdfdf2312b03beae80863cc9cf7ffce3b9372beea35a5c5f'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'render-r411';New-Item -ItemType Directory -Force $bin|Out-Null
 & tar.exe -xzf $archive -C $bin
 if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
 Gate
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Rendering.Tests.dll') -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'render-r411.xml') *> (Join-Path $root 'render-r411.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
 ('FINISHED exit='+$result)|Set-Content $state
 exit $result
} catch {($_|Out-String)|Set-Content (Join-Path $root 'render-failure-r411.txt');'FAILED'|Set-Content $state;exit 1}