$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try {
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $base 'bundle.tgz')).Hash -ne '2bb0f2a1946bb6cc87aa075f0deee17a98c96d4487739a70151b1ef46c00845d'){throw 'Candidate archive hash mismatch'}
 $patchInput=Join-Path $base 'patch-input'
 if(Test-Path -LiteralPath $patchInput){throw 'Fresh patch extraction required'}
 New-Item -ItemType Directory -Path $patchInput|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $patchInput
 if($LASTEXITCODE){throw 'Patch extraction failed'}
 $plan=Get-Content -Raw -LiteralPath (Join-Path $patchInput 'plan.json')|ConvertFrom-Json
 $baseline=[IO.Path]::GetFullPath($plan.baseline)
 if($baseline-ne'C:\FileCatDsa20261104R2\batch-r846\payload'){throw 'Unexpected cached baseline'}
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains(':')-or@($item.path-split'/'|Where-Object{$_-eq'..'}).Count){throw 'Invalid cached input path'}
  $cachedFile=Join-Path $baseline $item.path
  if((Get-FileHash -LiteralPath $cachedFile -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.baselineSha256){throw ('Cached input changed: '+$item.path)}
 }
 $payload=Join-Path $base 'payload'
 if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 foreach($item in $plan.files){
  $source=if($item.patch){Join-Path $patchInput ('patch/'+$item.path)}else{Join-Path $baseline $item.path}
  $target=Join-Path $payload $item.path
  New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null
  Copy-Item -LiteralPath $source -Destination $target
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.sha256){throw ('Copied input mismatch: '+$item.path)}
 }
 ('VERIFIED cachedFiles='+$plan.files.Count+' changedFiles='+@($plan.files|Where-Object{$_.patch}).Count)|Set-Content (Join-Path $base 'cache-proof.txt')
 foreach($check in 'smoke','preview'){
  if(Busy){('BUSY phase='+$check)|Set-Content $state;exit 75}
  ('RUNNING phase='+$check+' runnerPid='+$PID)|Set-Content $state
  $folder=if($check-eq'smoke'){'core'}else{'render'}
  $assembly=if($check-eq'smoke'){'FileCat.Core.Tests.dll'}else{'FileCat.Rendering.Tests.dll'}
  $bin=Join-Path $payload $folder
  $filters=if($check-eq'smoke'){@('-method','*SavedComparison*','-method','*FailedSecondInputsAndCanceledReadsReleaseEveryImportReservation','-method','*ComparisonPrivatePublicationIsOneCreateNewFileAndNeverReplacesOrPublishesHalfInputs')}
   else{@('-method','*SavedComparisonNativeInputsRoundtripEveryMetricAndKeepExactRawNamesAndSources','-method','*ComparisonSaveCaptureOwnsInputsAndAppliedQueryIndependentlyOfLaterEditsAndSourceClosure','-method','*ComparisonSavingRefusesPendingMetricAndExistingNamesWhileKeepingThePreviousFile','-method','*ComparisonSaveAndOpenDialogsUsePrivateAtomicBundlesAndRestoreAppliedViewInANewTab')}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($check+'-r849.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($check+'-r849.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($result){('FINISHED exit=1 phase='+$check)|Set-Content $state;exit 1}
 }
 $overall=0
 foreach($phase in 'core','native','render'){
  if(Busy){('BUSY phase='+$phase)|Set-Content $state;exit 75}
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload $phase
  $assembly=switch($phase){'core'{'FileCat.Core.Tests.dll'};'native'{'FileCat.Platform.Windows.Tests.dll'};'render'{'FileCat.Rendering.Tests.dll'}}
  $filters=switch($phase){'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')};'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')};'render'{@('-class','*AnalysisRenderingTests')}}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r849.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r849.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($result -ne 0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
} catch {($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
