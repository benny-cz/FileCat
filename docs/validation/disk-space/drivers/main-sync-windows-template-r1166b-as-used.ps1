$ErrorActionPreference='Stop'
$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use the ordinary R2 interactive desktop'}
function Busy { @(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count }
function Wait-Idle([string]$phase){
 $idle=0
 for($attempt=0;$attempt-le5;$attempt++){
  if(Busy){$idle=0}else{$idle++}
  if($idle-ge3){return}
  if($attempt-lt5){Start-Sleep -Seconds 1}
 }
 @(Get-CimInstance Win32_Process|Where-Object{$_.Name-like'FileCat*'-or$_.Name-eq'dotnet.exe'}|Select-Object ProcessId,ParentProcessId,Name,CommandLine)|ConvertTo-Json -Depth 3|Set-Content (Join-Path $base ('busy-@REV@-'+$phase+'.json'))
 ('BUSY phase='+$phase)|Set-Content $state;exit 75
}
Wait-Idle verify
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try{
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Bundle changed'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'A fresh payload is required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
 if($LASTEXITCODE){throw 'Extraction failed'}
 [ordered]@{user=(& whoami.exe);session=(Get-Process -Id $PID).SessionId;runnerPid=$PID;osVersion=[Environment]::OSVersion.VersionString}|ConvertTo-Json|Set-Content (Join-Path $base 'environment.json')
 $env:FILECAT_COLUMN_BASELINE_OUT=Join-Path $base 'column-baseline-@REV@.json'
 $env:FILECAT_OWNER_PERFORMANCE_OUT=Join-Path $base 'owner-performance-@REV@.json'
 $env:FILECAT_OWNER_ANALYSIS_OUT=Join-Path $base 'ownership-analysis-@REV@.json'
 $overall=0
 foreach($phase in 'smoke','preview','core','metacore','native','ownernative','render','ownerrender','appsettings','ownerapp'){
  Wait-Idle $phase
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload $(if($phase-in@('smoke','core','metacore')){'@CORE@'}elseif($phase-in@('native','ownernative')){'native'}else{'render'})
  $assembly=if($phase-in@('smoke','core','metacore')){'FileCat.Core.Tests.dll'}elseif($phase-in@('native','ownernative')){'FileCat.Platform.Windows.Tests.dll'}elseif($phase-in@('appsettings','ownerapp')){'FileCat.App.Tests.dll'}else{'FileCat.Rendering.Tests.dll'}
  $filters=switch($phase){
   'smoke'{@('-method','*IndexedComparisonLocate*')}
   'preview'{@('-method','*NativeIndexedComparisonMaintenance*')}
   'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests','-class','*ZipListingRevisionTests')}
   'metacore'{@('-class','*Metadata*Tests','-class','*OwnershipFieldTests','-class','*PrincipalNamesTests','-class','*UnixOwnershipTests','-class','*UnixPermissionTests')}
   'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')}
   'ownernative'{@('-class','*WindowsOwnershipTests','-class','*RegistryStandardUserTests')}
   'render'{@('-class','*AnalysisRenderingTests')}
   'ownerrender'{@('-class','*ColumnOwnershipBaselineTests','-class','*OwnerColumnPerformanceTests','-class','*OwnerColumnWidthTests','-class','*OwnershipAnalysisPerformanceTests')}
   'appsettings'{@('-class','*SettingsDialogTests','-class','*SettingsPreservationTests')}
   'ownerapp'{@('-class','*OwnerColumn*Tests','-class','*AnalysisCurrentDemandTests','-class','*ClippedCellInspectionTests','-class','*MetadataOutcomeUiTests')}
  }
  $threads=if($phase-in@('metacore','ownernative','ownerrender','ownerapp')){1}else{2}
  Set-Location -LiteralPath $bin
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads',[string]$threads,'-xml',(Join-Path $base ($phase+'-@REV@.xml')))
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-@REV@.log'))
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
  if($phase-in@('smoke','preview')){
   $a=([xml][IO.File]::ReadAllText((Join-Path $base ($phase+'-@REV@.xml')))).assemblies.assembly
   $count=if($phase-eq'smoke'){17}else{5}
   if($result-ne0-or[int]$a.total-ne$count-or[int]$a.passed-ne$count-or[int]$a.failed-ne0-or[int]$a.errors-ne0){('FINISHED exit=1 phase='+$phase)|Set-Content $state;exit 1}
  }
  if($result-ne0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state;exit $overall
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
