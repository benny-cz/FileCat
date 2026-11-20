$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try {
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $base 'bundle.tgz')).Hash -ne '4628733dee8d306fb2ad063d767138c35c73c388fca48164fb5370b0286240a7'){throw 'Candidate archive hash mismatch'}
 $payload=Join-Path $base 'payload'
 if(Test-Path -LiteralPath $payload){throw 'A fresh candidate directory is required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
 if($LASTEXITCODE){throw 'Candidate extraction failed'}
 $overall=0
 foreach($phase in 'smoke','preview','core','native','render'){
  if(Busy){('BUSY phase='+$phase)|Set-Content $state;exit 75}
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload (@{smoke='core';preview='render';core='core';native='native';render='render'}[$phase])
  $assembly=switch($phase){'smoke'{'FileCat.Core.Tests.dll'};'preview'{'FileCat.Rendering.Tests.dll'};'core'{'FileCat.Core.Tests.dll'};'native'{'FileCat.Platform.Windows.Tests.dll'};'render'{'FileCat.Rendering.Tests.dll'}}
  $filters=switch($phase){'smoke'{@('-method','FileCat.Core.Tests.AnalysisHistoryTests.*')};'preview'{@('-method','FileCat.Rendering.Tests.AnalysisRenderingTests.ActualDiagnosticsZipIncludesOnlyFiniteAnalysisPreferencesAndPreservesPrivateSentinels')};'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')};'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')};'render'{@('-class','*AnalysisRenderingTests')}}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r982.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r982.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($phase -in @('smoke','preview')){
   $xml=[xml][IO.File]::ReadAllText((Join-Path $base ($phase+'-r982.xml')))
   $expected=if($phase-eq'smoke'){25}else{1}
   $assemblyResult=$xml.assemblies.assembly
   if([int]$assemblyResult.total-ne$expected-or[int]$assemblyResult.passed-ne$expected-or[int]$assemblyResult.failed-ne0-or[int]$assemblyResult.errors-ne0){$result=1}
   if($result-ne0){('FINISHED exit=1 phase='+$phase)|Set-Content $state;exit 1}
  }
  if($result -ne 0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
} catch {($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
