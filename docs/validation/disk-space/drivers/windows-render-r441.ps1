$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'render-state-r441.txt'
function Gate { if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75} }
try {
 Gate
 $archive=Join-Path $root 'render-r441.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne '791533ef40e9cb2096b8968bb626ac3c2fc23145471a815626b075f923e2fb49'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'render-r441';New-Item -ItemType Directory -Force $bin|Out-Null
 & tar.exe -xzf $archive -C $bin
 if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
 Gate
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Rendering.Tests.dll') -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'render-r441.xml') *> (Join-Path $root 'render-r441.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
 ('FINISHED exit='+$result)|Set-Content $state
 exit $result
} catch {($_|Out-String)|Set-Content (Join-Path $root 'render-failure-r441.txt');'FAILED'|Set-Content $state;exit 1}