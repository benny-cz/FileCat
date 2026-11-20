$ErrorActionPreference='Stop';$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
function Idle([string]$phase){if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){('BUSY phase='+$phase)|Set-Content $state;exit 75}}
try{
 if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use the ordinary R2 interactive desktop'}
 Idle verify
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash.ToLowerInvariant()-ne'@HASH@'){throw 'Driver archive changed'}
 $driver=Join-Path $base 'driver';if(Test-Path -LiteralPath $driver){throw 'Fresh driver directory required'}
 New-Item -ItemType Directory -Path $driver|Out-Null;& tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $driver;if($LASTEXITCODE){throw 'Driver extraction failed'}
 $plan=Get-Content -Raw -LiteralPath (Join-Path $driver 'plan.json')|ConvertFrom-Json
 if($plan.baseline-cne'C:\FileCatDsa20261104R2\batch-r1250\payload\render'){throw 'Unexpected source build'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 foreach($row in $plan.inputs){
  if([IO.Path]::IsPathRooted($row.path)-or$row.path.Contains(':')-or@($row.path-split'/'|Where-Object{$_-eq'..'}).Count){throw 'Invalid cached input path'}
  if((Get-FileHash -LiteralPath (Join-Path $plan.baseline $row.path) -Algorithm SHA256).Hash.ToLowerInvariant()-ne$row.sha256){throw 'Cached input changed'}
 }
 foreach($row in $plan.inputs){$target=Join-Path $payload $row.path;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $plan.baseline $row.path) -Destination $target;if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()-ne$row.sha256){throw 'Copied input mismatch'}}
 [ordered]@{baseline=$plan.baseline;verifiedFiles=$plan.inputs.Count;changedProductFiles=0}|ConvertTo-Json|Set-Content (Join-Path $base 'cache-proof.json')
 $marker=Join-Path ([IO.Path]::GetDirectoryName($plan.sources.deep)) 'fixture.json'
 if((Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash.ToLowerInvariant()-ne$plan.deepMarkerSha256){throw 'The retained deep source marker changed'}
 foreach($shape in 'deep','wide'){
  Idle ('1000000-'+$shape)
  if($shape-eq'wide'){
   & (Join-Path $driver 'flatten.ps1') -DeepCase (Join-Path $base 'unknown') -Output (Join-Path $base 'flatten-negative.json')
   if($LASTEXITCODE-ne1-or!(Test-Path -LiteralPath (Join-Path $base 'flatten-negative.json.error.json'))){throw 'Fixture boundary negative control failed'}
   & (Join-Path $driver 'flatten.ps1') -DeepCase (Join-Path $base 'case-1000000-deep') -Output (Join-Path $base 'flatten.json')
   if($LASTEXITCODE){throw 'Guarded fixture flatten failed'}
  }
  $phase='1000000-'+$shape;('RUNNING phase='+$phase)|Set-Content $state;$case=Join-Path $base ('case-'+$phase);New-Item -ItemType Directory -Path $case|Out-Null
  $env:FILECAT_TEST_NATIVE_SCALE_COUNT='1000000';$env:FILECAT_TEST_NATIVE_SCALE_SHAPE=$shape;$env:FILECAT_TEST_NATIVE_SCALE_ROOT=$case;$env:FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE='1';$env:FILECAT_TEST_NATIVE_SCALE_SOURCE=$plan.sources.deep
  Set-Location -LiteralPath $payload;$ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' FileCat.Rendering.Tests.dll -class '*AnalysisAcquisitionScaleTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $case 'tests.xml') *> (Join-Path $case 'test.log')
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop';[xml]$xml=[IO.File]::ReadAllText((Join-Path $case 'tests.xml'));$a=$xml.assemblies.assembly;$receipt=Get-Content -Raw -LiteralPath (Join-Path $case 'receipt.json')|ConvertFrom-Json
  if($result-ne0-or[int]$a.total-ne1-or[int]$a.passed-ne1-or[int]$a.failed-ne0-or[int]$a.errors-ne0-or[int]$a.skipped-ne0-or!$receipt.completed-or!$receipt.cleanup-or$receipt.failure-or$receipt.phase-ne'closed'-or$receipt.paths-ne(1000000+$(if($shape-eq'deep'){33}else{1}))-or$receipt.sourceCloseFailures-ne0-or$receipt.remainingSharedQuotaBytes-ne0-or!$receipt.privateMemoryBudgetVerified-or!$receipt.reusedSource){throw 'Native million-file case failed'}
 }
 'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FINISHED exit=1'|Set-Content $state;exit 1}
