$ErrorActionPreference='Stop'
$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use the ordinary R2 interactive desktop'}
function Wait-Idle([string]$phase){
 $clear=0
 for($attempt=0;$attempt-le5;$attempt++){
  if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){$clear=0}else{$clear++}
  if($clear-ge3){return};if($attempt-lt5){Start-Sleep -Seconds 1}
 }
 @(Get-CimInstance Win32_Process|Where-Object{$_.Name-like'FileCat*'-or$_.Name-eq'dotnet.exe'}|Select-Object ProcessId,ParentProcessId,Name,CommandLine)|ConvertTo-Json -Depth 3|Set-Content (Join-Path $base ('busy-'+$phase+'.json'))
 ('BUSY phase='+$phase)|Set-Content $state;exit 75
}
Wait-Idle verify
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try{
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Bundle changed'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
 if($LASTEXITCODE){throw 'Extraction failed'}
 [ordered]@{user=(& whoami.exe);session=(Get-Process -Id $PID).SessionId;runnerPid=$PID;osVersion=[Environment]::OSVersion.VersionString}|ConvertTo-Json|Set-Content (Join-Path $base 'environment.json')
 $env:FILECAT_TEST_MAC_RUSAGE_CONTROL=Join-Path $payload 'native-counter-layout.json'
 foreach($count in 1000,100000,1000000){foreach($shape in 'wide','deep'){
  $phase=[string]$count+'-'+$shape;Wait-Idle $phase
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $case=Join-Path $base ('case-'+$phase);New-Item -ItemType Directory -Path $case|Out-Null
  $env:FILECAT_TEST_NATIVE_SCALE_COUNT=[string]$count;$env:FILECAT_TEST_NATIVE_SCALE_SHAPE=$shape;$env:FILECAT_TEST_NATIVE_SCALE_ROOT=$case
  Set-Location -LiteralPath (Join-Path $payload 'render')
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' FileCat.Rendering.Tests.dll -class '*AnalysisAcquisitionScaleTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $case 'tests.xml') *> (Join-Path $case 'test.log')
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
  if($result-ne0){('FINISHED exit=1 phase='+$phase+' nativeExit='+$result)|Set-Content $state;exit 1}
  [xml]$xml=[IO.File]::ReadAllText((Join-Path $case 'tests.xml'));$a=$xml.assemblies.assembly
  $receipt=Get-Content -Raw -LiteralPath (Join-Path $case 'receipt.json')|ConvertFrom-Json
  $paths=$count+1;if($shape-eq'deep'){$paths+=32}
  if([int]$a.total-ne1-or[int]$a.passed-ne1-or[int]$a.failed-ne0-or[int]$a.errors-ne0-or[int]$a.skipped-ne0-or!$receipt.completed-or!$receipt.cleanup-or$receipt.failure-or$receipt.paths-ne$paths-or$receipt.sourceCloseFailures-ne0-or$receipt.remainingSharedQuotaBytes-ne0-or!$receipt.privateMemoryBudgetVerified){('FINISHED exit=1 phase='+$phase+' validation=failed')|Set-Content $state;exit 1}
 }}
 'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
