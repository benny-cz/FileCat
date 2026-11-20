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
function Run-Tests([string]$bin,[string]$assembly,[string[]]$filters,[string]$xml,[string]$log,[int]$threads=2){
 Set-Location -LiteralPath $bin;$ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' $assembly @filters -noLogo -noColor -maxThreads $threads -xml $xml *> $log
 $script:nativeExit=$LASTEXITCODE;$ErrorActionPreference='Stop'
}
Wait-Idle verify
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try{
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Bundle changed'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload;if($LASTEXITCODE){throw 'Extraction failed'}
 [ordered]@{user=(& whoami.exe);session=(Get-Process -Id $PID).SessionId;runnerPid=$PID;osVersion=[Environment]::OSVersion.VersionString}|ConvertTo-Json|Set-Content (Join-Path $base 'environment.json')
 foreach($phase in 'smoke','locality','native-control','core','native','render','settings'){
  Wait-Idle $phase;('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload '@CORE@';$assembly='FileCat.Core.Tests.dll'
  $filters=switch($phase){
   'smoke'{@('-method','*SortedCapturePayload*','-method','*MaximumAcceptedCapturedValueFieldsFitTheExistingSortKeyLimit','-method','*PermutedNativeIdentitiesProduceOrderedCanonicalObjectsWithExactAliasesAndBoundedReads')}
   'locality'{@('-method','*SealedIdentityUniquenessIsDerivedRatherThanAcceptedFromTheSuppliedHeader','-method','*InternalTraversalUsesCapturedRowsWhilePublicChildrenRetainExactNameOrder','-method','*UniqueIdentityTraversalPreservesFilteredAndUnfilteredAccountingWhenNativeKeysReverseObjectIds')}
   'native-control'{@('-method','*NativeDeepSiblingEnumerationHasBoundedDescriptorChurn','-method','*NativeDirectoryReplacementDuringEnumerationCannotPublishEitherOccupant','-method','*NativeRelativeChildrenObserveLinksAndFifosWithoutFollowingOrReadingThem')}
   'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests','-class','*ZipListingRevisionTests')}
   'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')}
   'render'{@('-class','*AnalysisRenderingTests')}
   'settings'{@('-class','*SettingsDialogTests','-class','*SettingsPreservationTests')}
  }
  if($phase-eq'native'){$bin=Join-Path $payload 'native';$assembly='FileCat.Platform.Windows.Tests.dll'}
  if($phase-in@('render','settings')){$bin=Join-Path $payload 'render';$assembly=if($phase-eq'settings'){'FileCat.App.Tests.dll'}else{'FileCat.Rendering.Tests.dll'}}
  $xml=Join-Path $base ($phase+'.xml');$log=Join-Path $base ($phase+'.log')
  Run-Tests $bin $assembly $filters $xml $log
  [xml]$document=[IO.File]::ReadAllText($xml);$a=$document.assemblies.assembly
  if($nativeExit-ne0-or[int]$a.total-le0-or[int]$a.failed-ne0-or[int]$a.errors-ne0){('FINISHED exit=1 phase='+$phase+' nativeExit='+$nativeExit)|Set-Content $state;exit 1}
  if($phase-eq'smoke'-and([int]$a.total-ne6-or[int]$a.passed-ne6-or[int]$a.skipped-ne0)){throw 'Smoke discovery or result mismatch'}
  if($phase-eq'locality'-and([int]$a.total-ne7-or[int]$a.passed-ne7-or[int]$a.skipped-ne0)){throw 'Locality discovery or result mismatch'}
  if($phase-eq'native-control'-and([int]$a.total-ne4-or[int]$a.passed-ne0-or[int]$a.skipped-ne4)){throw 'Unix native control discovery/platform skips mismatch'}
 }
 foreach($count in 1000,1000000){foreach($shape in 'wide','deep'){
  $phase=[string]$count+'-'+$shape;Wait-Idle $phase;('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $case=Join-Path $base ('case-'+$phase);New-Item -ItemType Directory -Path $case|Out-Null
  $env:FILECAT_TEST_NATIVE_SCALE_COUNT=[string]$count;$env:FILECAT_TEST_NATIVE_SCALE_SHAPE=$shape;$env:FILECAT_TEST_NATIVE_SCALE_ROOT=$case
  $env:FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE='1';$env:FILECAT_TEST_NATIVE_SCALE_SOURCE=$null
  if($count-eq1000000-and$shape-eq'wide'){$env:FILECAT_TEST_NATIVE_SCALE_SOURCE='@SOURCE@'}
  Run-Tests (Join-Path $payload 'render') 'FileCat.Rendering.Tests.dll' @('-class','*AnalysisAcquisitionScaleTests') (Join-Path $case 'tests.xml') (Join-Path $case 'test.log') 1
  [xml]$xml=[IO.File]::ReadAllText((Join-Path $case 'tests.xml'));$a=$xml.assemblies.assembly
  $r=Get-Content -Raw -LiteralPath (Join-Path $case 'receipt.json')|ConvertFrom-Json;$paths=$count+1;if($shape-eq'deep'){$paths+=32}
  if($nativeExit-ne0-or[int]$a.total-ne1-or[int]$a.passed-ne1-or[int]$a.failed-ne0-or[int]$a.errors-ne0-or[int]$a.skipped-ne0-or!$r.completed-or!$r.cleanup-or$r.failure-or$r.paths-ne$paths-or$r.sourceCloseFailures-ne0-or$r.remainingSharedQuotaBytes-ne0-or!$r.privateMemoryBudgetVerified){('FINISHED exit=1 phase='+$phase+' nativeExit='+$nativeExit)|Set-Content $state;exit 1}
 }}
 'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
