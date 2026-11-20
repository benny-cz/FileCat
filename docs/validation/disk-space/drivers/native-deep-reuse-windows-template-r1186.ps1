$ErrorActionPreference='Stop';$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use the ordinary R2 interactive desktop'}
function Idle {
 if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75}
}
try{
 Idle
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Bundle changed'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload;if($LASTEXITCODE){throw 'Extraction failed'}
 $prior='C:\FileCatDsa20261104R2\batch-r1185';$render=Join-Path $prior 'payload\render'
 foreach($row in (Get-Content -Raw -LiteralPath (Join-Path $payload 'inputs.json')|ConvertFrom-Json)){
  if((Get-FileHash -LiteralPath (Join-Path $render $row.path) -Algorithm SHA256).Hash-ne$row.sha256){throw 'An existing qualified input changed'}
 }
 [ordered]@{user=(& whoami.exe);session=(Get-Process -Id $PID).SessionId;runnerPid=$PID;productAndFixtureRebuilt=$false;reusedBatch=$prior}|ConvertTo-Json|Set-Content (Join-Path $base 'environment.json')
 # A malformed prior batch must fail before it can move any source directory.
 & (Join-Path $payload 'reshape.ps1') -PriorBatch 'C:\FileCatDsa20261104R2\unknown' -Output (Join-Path $base 'reshape-negative.json')
 if($LASTEXITCODE-ne1-or!(Test-Path -LiteralPath (Join-Path $base 'reshape-negative.json.error.json'))){throw 'Reshape boundary negative control failed'}
 'RUNNING phase=reshape'|Set-Content $state;Idle
 & (Join-Path $payload 'reshape.ps1') -PriorBatch $prior -Output (Join-Path $base 'reshape.json')
 if($LASTEXITCODE){throw 'Owned fixture reshape failed'}
 $wide=Get-Content -Raw -LiteralPath (Join-Path $prior 'case-1000000-wide\receipt.json')|ConvertFrom-Json
 foreach($count in 1000,1000000){
  Idle;$phase=[string]$count+'-deep';('RUNNING phase='+$phase)|Set-Content $state
  $case=Join-Path $base ('case-'+$phase);New-Item -ItemType Directory -Path $case|Out-Null
  $env:FILECAT_TEST_NATIVE_SCALE_COUNT=[string]$count;$env:FILECAT_TEST_NATIVE_SCALE_SHAPE='deep';$env:FILECAT_TEST_NATIVE_SCALE_ROOT=$case
  $env:FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE='1';$env:FILECAT_TEST_NATIVE_SCALE_SOURCE=$null
  if($count-eq1000000){$env:FILECAT_TEST_NATIVE_SCALE_SOURCE=$wide.retainedSource}
  Set-Location -LiteralPath $render;$ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' FileCat.Rendering.Tests.dll -class '*AnalysisAcquisitionScaleTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $case 'tests.xml') *> (Join-Path $case 'test.log')
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
  [xml]$xml=[IO.File]::ReadAllText((Join-Path $case 'tests.xml'));$a=$xml.assemblies.assembly
  $r=Get-Content -Raw -LiteralPath (Join-Path $case 'receipt.json')|ConvertFrom-Json
  if($result-ne0-or[int]$a.total-ne1-or[int]$a.passed-ne1-or[int]$a.failed-ne0-or[int]$a.errors-ne0-or[int]$a.skipped-ne0-or!$r.completed-or!$r.cleanup-or$r.failure-or$r.paths-ne($count+33)-or$r.sourceCloseFailures-ne0-or$r.remainingSharedQuotaBytes-ne0-or!$r.privateMemoryBudgetVerified){throw 'The actual deep qualification failed'}
 }
 'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FINISHED exit=1'|Set-Content $state;exit 1}
