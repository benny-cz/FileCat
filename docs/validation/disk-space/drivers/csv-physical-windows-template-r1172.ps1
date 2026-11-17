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
Wait-Idle verify
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try{
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Bundle changed'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload
 if($LASTEXITCODE){throw 'Extraction failed'}
 [ordered]@{user=[string](& whoami.exe);session=[int](Get-Process -Id $PID).SessionId;runnerPid=$PID;osVersion=[Environment]::OSVersion.VersionString}|ConvertTo-Json|Set-Content (Join-Path $base 'environment.json')
 foreach($phase in 'counter','small-wide','small-deep','wide','deep','huge-wide','huge-deep'){
  Wait-Idle $phase;('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $arguments=@((Join-Path $payload 'FileCat.CoreCrashHarness.dll'))
  if($phase-eq'counter'){$arguments+='--analysis-memory-counter-control'}else{
   $count=1000000;$shape=$phase
   if($phase.StartsWith('small-')){$count=1000;$shape=$phase.Substring(6)}
   elseif($phase.StartsWith('huge-')){$count=10000000;$shape=$phase.Substring(5)}
   $parent=Join-Path $base ('case-'+$phase);if(Test-Path -LiteralPath $parent){throw 'Fresh case directory required'}
   New-Item -ItemType Directory -Path $parent|Out-Null
   $arguments+=@('--analysis-csv-scale',[string]$count,$shape,$parent)
  }
  $log=Join-Path $base ($phase+'.log');$ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> $log
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
  if($result-ne0){('FINISHED exit=1 phase='+$phase+' nativeExit='+$result)|Set-Content $state;exit 1}
  $last=@([IO.File]::ReadLines($log)|Select-Object -Last 1)[0]|ConvertFrom-Json
  if($last.passed-ne$true){throw ('Unverified case '+$phase)}
  if($phase-eq'counter'){
   if($last.allocationAttempted-ne$true-or$last.touchedBytes-ne67108864){throw 'Counter control missing'}
  }else{
   if($last.records-ne$count-or$last.shape-ne$shape-or$last.remainingQuotaBytes-ne0-or$last.remainingProfileQuotaBytes-ne0-or$last.ownedArtifactsRemoved-ne$true-or$last.foreignNeighborPreserved-ne$true-or$last.observedSourceWasNeverCreated-ne$true-or$last.privateMemoryBudgetVerified-ne$true){throw ('Scale invariants failed: '+$phase)}
   if($last.limitRefusal-eq$true){
    if($count-ne10000000-or$last.destinationWasNeverOpened-ne$true-or$last.retainedSourceUnchanged-ne$true-or$last.fullCsvExportQualified-ne$false){throw 'Unverified limit refusal'}
   }elseif(@($last.reports).Count-ne2-or@($last.reports|Where-Object{$_.verifiedEveryDeclaredRow-ne$true-or$_.lines-ne($count+2)}).Count){throw 'Complete CSV rows missing'}
  }
 }
 'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
