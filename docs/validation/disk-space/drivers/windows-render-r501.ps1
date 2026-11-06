$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'render-state-r501.txt'
function Gate { if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75} }
try {
 Gate
 $archive=Join-Path $root 'render-r501.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne '8267191e1fc839a92464bb21f17679c976880d62b4d8e51779c600a45b15e13f'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'render-r501';New-Item -ItemType Directory -Force $bin|Out-Null
 & tar.exe -xzf $archive -C $bin
 if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
 Gate
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Rendering.Tests.dll') -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'render-r501.xml') *> (Join-Path $root 'render-r501.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
 ('FINISHED exit='+$result)|Set-Content $state
 exit $result
} catch {($_|Out-String)|Set-Content (Join-Path $root 'render-failure-r501.txt');'FAILED'|Set-Content $state;exit 1}