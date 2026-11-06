$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'render-state-r403.txt'
function Gate { if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75} }
try {
 Gate
 $archive=Join-Path $root 'render-r403.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne '77a88056dbe46b7d8e6b44208c0f0e770cef215e91f2a649f25959246ed51c76'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'render-r403';New-Item -ItemType Directory -Force $bin|Out-Null
 & tar.exe -xzf $archive -C $bin
 if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
 Gate
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Rendering.Tests.dll') -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'render-r403.xml') *> (Join-Path $root 'render-r403.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
 ('FINISHED exit='+$result)|Set-Content $state
 exit $result
} catch {($_|Out-String)|Set-Content (Join-Path $root 'render-failure-r403.txt');'FAILED'|Set-Content $state;exit 1}