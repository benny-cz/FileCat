param([ValidateSet('Stage','Observe')][string]$Mode,[string]$ExpectedHash)
$ErrorActionPreference='Stop'
$base='C:\FileCatDsa20261104R2\desktop-r101-acquisition';$app=Join-Path $base 'app';$state=Join-Path $base 'state.txt'
function Gate([int]$Owned=0){
 $others=@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue|Where-Object Id -ne $Owned)
 $runners=@(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.CommandLine -match '(?i)FileCat.*[.]dll'})
 if($others.Count -or $runners.Count){throw 'BUSY: postpone this machine'}
}
function ReadReport([string]$Path){try{Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json}catch{return $null}}
if($Mode -eq 'Stage'){
 Gate
 $archive='C:\FileCatDsa20261104R2\desktop-r101-windows.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne $ExpectedHash){throw 'Archive mismatch'}
 if(Test-Path -LiteralPath $app){throw 'Use a fresh stage'}
 New-Item -ItemType Directory -Path $app|Out-Null
 & tar.exe -xzf $archive -C $app;if($LASTEXITCODE){throw 'Extraction failed'}
 'STAGED'|Set-Content $state;exit 0
}
$record=[ordered]@{purpose='Native acquisition lifetime subset; no physical-input qualification'};$run=$null;$owned=0
try{
 $until=[DateTime]::UtcNow.AddSeconds(150)
 do{
  $candidates=@(Get-ChildItem -LiteralPath (Join-Path $app 'native-validation') -Directory -ErrorAction SilentlyContinue|ForEach-Object{
   $r=ReadReport (Join-Path $_.FullName 'report.json');if($r -and $r.state -ne 'closed'){[pscustomobject]@{Run=$_.FullName;Report=$r}}
  })
  if($candidates.Count -eq 1){
   $run=$candidates[0].Run;$report=$candidates[0].Report;$owned=[int]$report.processId;Gate $owned
   if((Get-Process -Id $owned).Path -ne (Join-Path $app 'FileCat.exe')){throw 'Owned image path mismatch'}
   if($report.profileProbeError){throw $report.profileProbeError}
   if($report.profileProbeFinished){break}
  }
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 if(-not $report.profileProbeFinished){throw 'Acquisition probe timed out'}
 $probe=$report.acquisitionProbe;$obs=$report.lastObservation
 foreach($key in @('repeatedStartPreservedIdentity','marksAndBasketPreserved','importedStayedInert','comparisonStayedInert')){if($probe.$key -ne $true){throw ('Acquisition failure '+$key)}}
 if($obs.Phase -ne 5 -or $obs.marked -ne 1 -or $obs.basket -ne 1 -or $obs.hostProfile -notlike 'hp1:*' -or $obs.Status -notlike '*New analysis*' -or $probe.sourcePaths -ne 518 -or $report.fixtureRegularPaths -ne 509){throw 'Native observation mismatch'}
 if($report.profileProbeCaptureError){throw ('Native capture failed '+$report.profileProbeCaptureError)}
 Copy-Item -LiteralPath (Join-Path $run 'windows-native-profile-probe.png') -Destination (Join-Path $base 'native-acquisition.png')
 $record.processId=$owned;$record.finished=$true;$record.acquisitionProbe=$probe;$record.fixtureRegularPaths=$report.fixtureRegularPaths
 $record.appSha256=$report.appSha256;$record.harnessSha256=$report.harnessSha256
}catch{$record.error=$_|Out-String;throw}finally{
 if($run){[IO.File]::WriteAllText((Join-Path $run 'close.request'),'')}
 if($owned){
  $until=[DateTime]::UtcNow.AddSeconds(20)
  while((Get-Process -Id $owned -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 200}
  $record.ownedProcessExited=-not [bool](Get-Process -Id $owned -ErrorAction SilentlyContinue)
  $closed=ReadReport (Join-Path $run 'report.json');$record.state=$closed.state;$record.sourceUnchanged=$closed.sourceUnchanged;$record.deadlineClosed=$closed.deadlineClosed
 }
 $record|ConvertTo-Json -Depth 10|Set-Content (Join-Path $base 'result.json')
}
if(-not $record.ownedProcessExited -or $record.state -ne 'closed' -or -not $record.sourceUnchanged -or $record.deadlineClosed){throw 'Native cleanup/source verification failed'}
Gate
$record.passed=$true;$record|ConvertTo-Json -Depth 10|Set-Content (Join-Path $base 'result.json')
'PASS'|Set-Content $state
