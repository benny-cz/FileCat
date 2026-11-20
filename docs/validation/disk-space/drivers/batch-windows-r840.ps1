$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try {
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $base 'bundle.tgz')).Hash -ne 'e4c279263486a99743a9e43541411d7465988db6c373812694b07cbf6f7259d6'){throw 'Candidate archive hash mismatch'}
 $payload=Join-Path $base 'payload'
 if(Test-Path -LiteralPath $payload){throw 'A fresh candidate directory is required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
 if($LASTEXITCODE){throw 'Candidate extraction failed'}
 if(Busy){'BUSY phase=smoke'|Set-Content $state;exit 75}
 ('RUNNING phase=smoke runnerPid='+$PID)|Set-Content $state
 $render=Join-Path $payload 'render'
 Set-Location -LiteralPath $render
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $render 'FileCat.Rendering.Tests.dll') -method '*ComparisonMetricWaitsActualReadersAndRecomputesAllCapturedMetrics' -method '*ComparisonChartSeparatesGrowthFromAreaAndKeepsRemovedOnlyInPrevious' -method '*ComparisonChartAndMetricControlsRenderAndSwitchAfterSourceClosure' -method '*MissingAutomaticRootsRequireExplicitMappingAndDefaultToCancel' -method '*ComparisonQueryPreservesLastValidRowsAndExposesUncertainMatches' -method '*ComparisonPaneFiltersGrowthInspectsBothSidesAndKeepsItsCloseDefault' -noLogo -noColor -maxThreads 2 -xml (Join-Path $base 'smoke-r840.xml') *> (Join-Path $base 'smoke-r840.log')
 $smokeResult=$LASTEXITCODE
 $ErrorActionPreference='Stop'
 if($smokeResult){'FINISHED exit=1 phase=smoke'|Set-Content $state;exit 1}
 $overall=0
 foreach($phase in 'core','render'){
  if(Busy){('BUSY phase='+$phase)|Set-Content $state;exit 75}
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload $phase
  $assembly=switch($phase){'core'{'FileCat.Core.Tests.dll'};'native'{'FileCat.Platform.Windows.Tests.dll'};'render'{'FileCat.Rendering.Tests.dll'}}
  $filters=switch($phase){'core'{@('-method','*ActualReadersKeepAdmissionUntilTheLastReleaseAndSignalCompletion','-method','*OrdinarySavedViewsRejectGrowthColorWithoutComparisonContext')};'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')};'render'{@('-class','*AnalysisRenderingTests')}}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r840.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r840.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($result -ne 0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
} catch {($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
