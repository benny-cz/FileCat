$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try {
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $base 'bundle.tgz')).Hash -ne 'efd434775e3e0b1a34e58ebf48d33234df6dfac9ea09c67923bf70732f2ab3d9'){throw 'Candidate archive hash mismatch'}
 $payload=Join-Path $base 'payload'
 if(Test-Path -LiteralPath $payload){throw 'A fresh candidate directory is required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
 if($LASTEXITCODE){throw 'Candidate extraction failed'}
 $overall=0
 foreach($phase in 'smoke','preview','core','native','render','appsettings'){
  if(Busy){('BUSY phase='+$phase)|Set-Content $state;exit 75}
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload (@{appsettings='render';smoke='core';preview='render';core='core';native='native';render='render'}[$phase])
  $assembly=switch($phase){'appsettings'{'FileCat.App.Tests.dll'};'smoke'{'FileCat.Core.Tests.dll'};'preview'{'FileCat.Rendering.Tests.dll'};'core'{'FileCat.Core.Tests.dll'};'native'{'FileCat.Platform.Windows.Tests.dll'};'render'{'FileCat.Rendering.Tests.dll'}}
  $filters=switch($phase){'appsettings'{@('-class','*SettingsDialogTests','-class','*SettingsPreservationTests')};'smoke'{@('-method','*AnalysisHumanReportTests.*','-method','*AnalysisMemoryReportTests.*','-method','*AnalysisGroupedReportTests.*')};'preview'{@('-method','*AnalysisRenderingTests.NativeCapturedDatasetCsv*','-method','*AnalysisRenderingTests.NativeCapturedReport*','-method','*AnalysisRenderingTests.CapturedReportReview*','-method','*AnalysisRenderingTests.CapturedReportFilename*','-method','*AnalysisRenderingTests.NativeMemoryCaptured*','-method','*AnalysisRenderingTests.MemoryFallbackCanExport*','-method','*AnalysisRenderingTests.NativeGroupedReport*','-method','*AnalysisRenderingTests.SupportedReportViewsReview*','-method','*AnalysisRenderingTests.NativeChartExport*','-method','*AnalysisRenderingTests.ChartExport*','-method','*AnalysisRenderingTests.NativeCsvBatch*','-method','*AnalysisRenderingTests.NativeCsvLineage*')};'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')};'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')};'render'{@('-class','*AnalysisRenderingTests')}}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r1096.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r1096.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($phase -in @('smoke','preview')){
   $xml=[xml][IO.File]::ReadAllText((Join-Path $base ($phase+'-r1096.xml')))
   $expected=if($phase-eq'smoke'){64}else{71}
   $assemblyResult=$xml.assemblies.assembly
   if([int]$assemblyResult.total-ne$expected-or[int]$assemblyResult.passed-ne$expected-or[int]$assemblyResult.failed-ne0-or[int]$assemblyResult.errors-ne0){$result=1}
   if($result-ne0){('FINISHED exit=1 phase='+$phase)|Set-Content $state;exit 1}
  }
  if($result -ne 0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
} catch {($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
