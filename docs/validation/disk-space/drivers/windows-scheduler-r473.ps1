$ErrorActionPreference='Stop';$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'scheduler-state-r473.txt'
function Gate { if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75} }
try {
 Gate
 $assembly=Join-Path $root 'core-all-r471\FileCat.Core.Tests.dll'
 if((Get-FileHash -LiteralPath $assembly).Hash -ne 'f8fa73019ff14ad8d5a8b2b63cb36e4bf82326b5154d9210a9bb4a9d04c00797'){throw 'Assembly mismatch'}
 Gate
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' $assembly -class '*DeviceIoSchedulerTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'scheduler-r473.xml') *> (Join-Path $root 'scheduler-r473.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result
} catch {($_|Out-String)|Set-Content (Join-Path $root 'scheduler-failure-r473.txt');'FAILED'|Set-Content $state;exit 1}
