$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2'
$state=Join-Path $root 'state-all-r415.txt'
if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY' | Set-Content $state;exit 75}
('RUNNING runnerPid='+$PID) | Set-Content $state
try {
$specs=,@('core','7f7f3032a12c0f673c826daa93087a51547e2a035dfae2fb950e2f58e6daf7b7','FileCat.Core.Tests.dll')
foreach($spec in $specs){
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75}
 $phase=$spec[0];$archive=Join-Path $root ($phase+'-all-r415.tgz');$bin=Join-Path $root ($phase+'-all-r415')
 if((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ne $spec[1]){throw 'Archive hash mismatch'}
 New-Item -ItemType Directory -Force -Path $bin | Out-Null
 & tar.exe -xzf $archive -C $bin
 if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
 $filters=switch($phase){'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')};'native'{@('-class','*AnalysisWindowsTests')};'render'{@('-class','*AnalysisRenderingTests')}}
 $arguments=@((Join-Path $bin $spec[2]))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $root ($phase+'-all-r415.xml')))
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $root ($phase+'-all-r415.log'))
 $result=$LASTEXITCODE
 $ErrorActionPreference='Stop'
 if($result -ne 0){('FINISHED phase='+$phase+' exit='+$result) | Set-Content $state;exit $result}
}
'FINISHED exit=0' | Set-Content $state
} catch {($_ | Out-String) | Set-Content (Join-Path $root 'failure-all-r415.txt');'FAILED' | Set-Content $state;exit 1}