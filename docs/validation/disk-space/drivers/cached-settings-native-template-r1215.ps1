$ErrorActionPreference='Stop';$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use the ordinary R2 interactive desktop'}
function Idle {if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75}}
try{
 Idle;'RUNNING phase=verify'|Set-Content $state
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Bundle changed'}
 $driverRoot=Join-Path $base 'input';if(Test-Path -LiteralPath $driverRoot){throw 'Fresh driver directory required'}
 New-Item -ItemType Directory -Path $driverRoot|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $driverRoot;if($LASTEXITCODE){throw 'Extraction failed'}
 $plan=Get-Content -LiteralPath (Join-Path $driverRoot 'plan.json') -Raw|ConvertFrom-Json
 $bin=$plan.source
 function Verify {
  foreach($row in $plan.inputs){
   if([IO.Path]::IsPathRooted($row.path)-or$row.path.Split('/')-contains'..'){throw 'Invalid cached input path'}
   $path=Join-Path $bin $row.path;$item=Get-Item -LiteralPath $path
   if($item.PSIsContainer-or($item.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0){throw 'Cached input is not a regular file'}
   if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash-ne$row.sha256){throw ('Cached input changed: '+$row.path)}
  }
 }
 Verify
 [ordered]@{source=$bin;baseline='r1214';verifiedFiles=@($plan.inputs).Count;productionChanged=$false;planSha256=(Get-FileHash -LiteralPath (Join-Path $driverRoot 'plan.json') -Algorithm SHA256).Hash}|ConvertTo-Json|Set-Content (Join-Path $base 'cache-proof.json')
 $phases=if($plan.phases){@($plan.phases)}else{@('settings','1000-wide','1000-deep')}
 foreach($phase in $phases){
  Idle;('RUNNING phase='+$phase)|Set-Content $state
  if($phase-eq'settings'){
   $assembly='FileCat.App.Tests.dll';$output=Join-Path $base 'settings.xml';$log=Join-Path $base 'settings.log';$filters=@()
   foreach($name in $plan.classes){$filters+=@('-class',$name)}
  }else{
   $shape=$phase.Split('-')[1];$case=Join-Path $base ('case-'+$phase);New-Item -ItemType Directory -Path $case|Out-Null
   $env:FILECAT_TEST_NATIVE_SCALE_COUNT='1000';$env:FILECAT_TEST_NATIVE_SCALE_SHAPE=$shape;$env:FILECAT_TEST_NATIVE_SCALE_ROOT=$case
   Remove-Item Env:FILECAT_TEST_NATIVE_SCALE_SOURCE,Env:FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE -ErrorAction SilentlyContinue
   $assembly='FileCat.Rendering.Tests.dll';$output=Join-Path $case 'tests.xml';$log=Join-Path $case 'test.log';$filters=@('-class','*AnalysisAcquisitionScaleTests')
  }
  Set-Location -LiteralPath $bin;$ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' $assembly @filters -noLogo -noColor -maxThreads 2 -xml $output *> $log
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
  [xml]$document=[IO.File]::ReadAllText($output);$a=$document.assemblies.assembly
  if($result-ne0-or[int]$a.total-le0-or[int]$a.failed-ne0-or[int]$a.errors-ne0){throw ('The actual test phase failed: '+$phase)}
  if($phase-ne'settings'){
   $receipt=Get-Content -LiteralPath (Join-Path $case 'receipt.json') -Raw|ConvertFrom-Json
   if([int]$a.total-ne1-or[int]$a.passed-ne1-or!$receipt.completed-or!$receipt.cleanup-or$receipt.remainingSharedQuotaBytes-ne0-or$receipt.sourceCloseFailures-ne0){throw 'Native acquisition control failed'}
  }
 }
 Verify;'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FINISHED exit=1'|Set-Content $state;exit 1}
