$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2'
$state=Join-Path $root 'state-all-r239.txt'
if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object {-not $_.CommandLine -or $_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){'BUSY' | Set-Content $state;exit 75}
('RUNNING runnerPid='+$PID) | Set-Content $state
try {
$specs=,@('core','d9529e9302c31073844953735228bf17080d7d294ab68ecbd4853220e446025b','FileCat.Core.Tests.dll')
foreach($spec in $specs){
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object {-not $_.CommandLine -or $_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){'BUSY'|Set-Content $state;exit 75}
 $phase=$spec[0];$archive=Join-Path $root ($phase+'-all-r239.tgz');$bin=Join-Path $root ($phase+'-all-r239')
 if((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ne $spec[1]){throw 'Archive hash mismatch'}
 New-Item -ItemType Directory -Force -Path $bin | Out-Null
 & tar.exe -xzf $archive -C $bin
 if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
 $filters=switch($phase){'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*PathAndStateTests')};'native'{@('-class','*AnalysisWindowsTests')};'render'{@('-class','*AnalysisRenderingTests')}}
 $arguments=@((Join-Path $bin $spec[2]))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $root ($phase+'-all-r239.xml')))
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $root ($phase+'-all-r239.log'))
 $result=$LASTEXITCODE
 $ErrorActionPreference='Stop'
 if($result -ne 0){('FINISHED phase='+$phase+' exit='+$result) | Set-Content $state;exit $result}
}
'FINISHED exit=0' | Set-Content $state
} catch {($_ | Out-String) | Set-Content (Join-Path $root 'failure-all-r239.txt');'FAILED' | Set-Content $state;exit 1}