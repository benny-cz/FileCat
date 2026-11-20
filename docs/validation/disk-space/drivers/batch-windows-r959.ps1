$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try {
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $base 'bundle.tgz')).Hash -ne '63f1a8b0211741ea7c8afc42f13387c32f68b76d9332f287074aaca965d8f2a4'){throw 'Candidate archive hash mismatch'}
 $payload=Join-Path $base 'payload'
 if(Test-Path -LiteralPath $payload){throw 'A fresh candidate directory is required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
 if($LASTEXITCODE){throw 'Candidate extraction failed'}
 $overall=0
 foreach($phase in 'smoke','preview'){
  if(Busy){('BUSY phase='+$phase)|Set-Content $state;exit 75}
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload (@{smoke='core';preview='render';core='core';native='native';render='render'}[$phase])
  $assembly=switch($phase){'smoke'{'FileCat.Core.Tests.dll'};'preview'{'FileCat.Rendering.Tests.dll'};'core'{'FileCat.Core.Tests.dll'};'native'{'FileCat.Platform.Windows.Tests.dll'};'render'{'FileCat.Rendering.Tests.dll'}}
  $filters=switch($phase){'smoke'{@('-method','FileCat.Core.Tests.AnalysisMaintenanceTests.RecoveryMetadataCleanupUncertaintyDoesNotLoseTheConfirmedIncomingSave')};'preview'{@('-method','FileCat.Rendering.Tests.AnalysisRenderingTests.AbruptOwnedInventoryDeathPreservesNamedBytesAndAllowsFreshExplicitRecovery')};'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')};'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')};'render'{@('-class','*AnalysisRenderingTests')}}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r959.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r959.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($phase -in @('smoke','preview')){
   $xml=[xml][IO.File]::ReadAllText((Join-Path $base ($phase+'-r959.xml')))
   $expected=if($phase-eq'smoke'){1}else{25}
   $assemblyResult=$xml.assemblies.assembly
   if([int]$assemblyResult.total-ne$expected-or[int]$assemblyResult.passed-ne$expected-or[int]$assemblyResult.failed-ne0-or[int]$assemblyResult.errors-ne0){$result=1}
   if($result-ne0){('FINISHED exit=1 phase='+$phase)|Set-Content $state;exit 1}
  }
  if($result -ne 0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
} catch {($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
