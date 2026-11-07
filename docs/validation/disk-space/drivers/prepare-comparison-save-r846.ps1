$ErrorActionPreference='Stop'
$paths=@(git diff --name-only -- src tests)+@(git ls-files --others --exclude-standard -- src tests)
$sources=@($paths|Sort-Object -Unique|ForEach-Object{[ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
$sources|ConvertTo-Json -Depth 4|Set-Content .tmp/source-candidate-r846.json
& docs/validation/disk-space/drivers/pack-batch-direct.ps1 -Revision r846 -RenderRevision r847 -BuildLog .tmp/build-r845.txt -SourceManifest .tmp/source-candidate-r846.json
if($LASTEXITCODE){throw 'Candidate packing failed'}
$path=Join-Path $PWD '.tmp/batch-windows-r846.ps1';$body=[IO.File]::ReadAllText($path)
$smoke=@'
 foreach($check in 'smoke','preview'){
  if(Busy){('BUSY phase='+$check)|Set-Content $state;exit 75}
  ('RUNNING phase='+$check+' runnerPid='+$PID)|Set-Content $state
  $folder=if($check-eq'smoke'){'core'}else{'render'}
  $assembly=if($check-eq'smoke'){'FileCat.Core.Tests.dll'}else{'FileCat.Rendering.Tests.dll'}
  $bin=Join-Path $payload $folder
  $filters=if($check-eq'smoke'){@('-method','*SavedComparison*','-method','*FailedSecondInputsAndCanceledReadsReleaseEveryImportReservation','-method','*ComparisonPrivatePublicationIsOneCreateNewFileAndNeverReplacesOrPublishesHalfInputs')}
   else{@('-method','*SavedComparisonNativeInputsRoundtripEveryMetricAndKeepExactRawNamesAndSources','-method','*ComparisonSaveCaptureOwnsInputsAndAppliedQueryIndependentlyOfLaterEditsAndSourceClosure','-method','*ComparisonSavingRefusesPendingMetricAndExistingNamesWhileKeepingThePreviousFile','-method','*ComparisonSaveAndOpenDialogsUsePrivateAtomicBundlesAndRestoreAppliedViewInANewTab')}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($check+'-r846.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($check+'-r846.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($result){('FINISHED exit=1 phase='+$check)|Set-Content $state;exit 1}
 }
'@
$body=$body.Replace(' $overall=0',$smoke+"`n"+' $overall=0')
[IO.File]::WriteAllText($path,$body.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
