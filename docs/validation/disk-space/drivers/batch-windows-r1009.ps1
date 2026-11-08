$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try {
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $base 'bundle.tgz')).Hash -ne '3dd2ca6c329402874bee9ff810e72cf14d84aff2dfe2061486a4c3c9455ef64c'){throw 'Candidate archive hash mismatch'}
 $control=Join-Path $base 'cache-input'
 New-Item -ItemType Directory -Path $control|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $control
 if($LASTEXITCODE){throw 'Cache plan extraction failed'}
 $plan=Get-Content -LiteralPath (Join-Path $control 'plan.json') -Raw|ConvertFrom-Json
 if($plan.baseline-cne'C:\FileCatDsa20261104R2\batch-r1006\payload'){throw 'Unexpected cached baseline'}
 $payload=$plan.baseline
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains('..')-or$item.path.Contains(':')){throw 'Unsafe cached path'}
  $file=Join-Path $payload $item.path
  if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()-cne$item.sha256){throw ('Cached input changed: '+$item.path)}
 }
 ('VERIFIED cachedFiles='+$plan.files.Count+' changedFiles=0')|Set-Content (Join-Path $base 'cache-proof.txt')
 $overall=0
 foreach($phase in 'smoke','preview','core','native','render'){
  if(Busy){('BUSY phase='+$phase)|Set-Content $state;exit 75}
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload (@{smoke='core';preview='render';core='core';native='native';render='render'}[$phase])
  $assembly=switch($phase){'smoke'{'FileCat.Core.Tests.dll'};'preview'{'FileCat.Rendering.Tests.dll'};'core'{'FileCat.Core.Tests.dll'};'native'{'FileCat.Platform.Windows.Tests.dll'};'render'{'FileCat.Rendering.Tests.dll'}}
  $filters=switch($phase){'smoke'{@('-method','*AnalysisHistoryTests.*','-method','*AnalysisCatalogTests.CatalogMemoryRemainsReservedUntilResultOwnersClose')};'preview'{@('-method','*AnalysisRenderingTests.NativeRolling*','-method','*AnalysisRenderingTests.NativeNamedOwnerCannotBecomeAutomaticHistoryAndNonHistoryFilesArePreserved','-method','*AnalysisRenderingTests.RollingHistoryPolicyReviewPreservesTheCurrentChoiceAndNamesOnlyItsPrivateScope','-method','*AnalysisRenderingTests.ActualDiagnosticsZipIncludesOnlyFiniteAnalysisPreferencesAndPreservesPrivateSentinels')};'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')};'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')};'render'{@('-class','*AnalysisRenderingTests')}}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r1009.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r1009.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($phase -in @('smoke','preview')){
   $xml=[xml][IO.File]::ReadAllText((Join-Path $base ($phase+'-r1009.xml')))
   $expected=if($phase-eq'smoke'){27}else{14}
   $assemblyResult=$xml.assemblies.assembly
   if([int]$assemblyResult.total-ne$expected-or[int]$assemblyResult.passed-ne$expected-or[int]$assemblyResult.failed-ne0-or[int]$assemblyResult.errors-ne0){$result=1}
   if($result-ne0){('FINISHED exit=1 phase='+$phase)|Set-Content $state;exit 1}
  }
  if($result -ne 0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
} catch {($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
