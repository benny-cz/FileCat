$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use the ordinary R2 interactive desktop'}
function Busy { @(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try{
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'c1f6ddfd45a964513f0b65cc9cbd6b7213bc785cb27f0ec3a87d60f730fef9df'){throw 'Bundle changed'}
 $payload=Join-Path $base 'payload'
 if(Test-Path -LiteralPath $payload){throw 'A fresh payload is required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
 if($LASTEXITCODE){throw 'Extraction failed'}
 [ordered]@{user=(& whoami.exe);session=(Get-Process -Id $PID).SessionId;runnerPid=$PID}|ConvertTo-Json|Set-Content (Join-Path $base 'environment.json')
 $overall=0
 foreach($phase in 'preview','core','native','render','appsettings'){
  if(Busy){('BUSY phase='+$phase)|Set-Content $state;exit 75}
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload $(if($phase-eq'core'){'tests/FileCat.Core.Tests/bin/Debug/net10.0'}elseif($phase-eq'native'){'native'}else{'render'})
  $assembly=switch($phase){'core'{'FileCat.Core.Tests.dll'};'native'{'FileCat.Platform.Windows.Tests.dll'};'appsettings'{'FileCat.App.Tests.dll'};default{'FileCat.Rendering.Tests.dll'}}
  $filters=switch($phase){
   'preview'{@('-method','*NativeExternalComparison*','-method','*NativeComparisonReport*','-method','*NativeComparisonReports*','-method','*ComparisonImportAndReportReviews*','-method','*NativeExternalInventoryInputEnforces*')}
   'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')}
   'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')}
   'render'{@('-class','*AnalysisRenderingTests')}
   'appsettings'{@('-class','*SettingsDialogTests','-class','*SettingsPreservationTests')}
  }
  Set-Location -LiteralPath $bin
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r1111.xml')))
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r1111.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($phase-eq'preview'){
   $a=([xml][IO.File]::ReadAllText((Join-Path $base 'preview-r1111.xml'))).assemblies.assembly
   if($result-ne0-or[int]$a.total-ne44-or[int]$a.passed-ne44-or[int]$a.failed-ne0-or[int]$a.errors-ne0){'FINISHED exit=1 phase=preview'|Set-Content $state;exit 1}
  }
  if($result-ne0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
