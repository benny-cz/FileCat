param([ValidateSet('Stage','Observe')][string]$Mode,[string]$ExpectedHash)
$ErrorActionPreference='Stop'
$base='C:\FileCatDsa20261104R2\desktop-r442-memory';$app=Join-Path $base 'app';$state=Join-Path $base 'state.txt'
function Gate([int]$Owned=0){
 $others=@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue|Where-Object Id -ne $Owned)
 $runners=@(Get-Process -Name dotnet -ErrorAction SilentlyContinue)
 if($others.Count -or $runners.Count){throw 'BUSY: postpone this machine'}
}
function ReadReport([string]$Path){try{Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json}catch{return $null}}
if($Mode -eq 'Stage'){
 Gate
 $archive='C:\FileCatDsa20261104R2\desktop-r442-windows.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne $ExpectedHash){throw 'Archive mismatch'}
 if(Test-Path -LiteralPath $app){throw 'Use a fresh native memory stage'}
 New-Item -ItemType Directory -Path $app|Out-Null
 & tar.exe -xzf $archive -C $app;if($LASTEXITCODE){throw 'Extraction failed'}
 'STAGED'|Set-Content $state;exit 0
}
$record=[ordered]@{purpose='Actual Windows memory preparation/scope/retained view/query subset; no physical-input or compositor qualification';passed=$false}
$run=$null;$owned=0;$process=$null
try{
 $until=[DateTime]::UtcNow.AddSeconds(210)
 do{
  $candidates=@(Get-ChildItem -LiteralPath (Join-Path $app 'native-validation') -Directory -ErrorAction SilentlyContinue|ForEach-Object{
   $r=ReadReport (Join-Path $_.FullName 'report.json');if($r -and $r.state -ne 'closed'){[pscustomobject]@{Run=$_.FullName;Report=$r}}
  })
  if($candidates.Count -eq 1){
   $run=$candidates[0].Run;$report=$candidates[0].Report;$owned=[int]$report.processId;Gate $owned
   if($null -eq $process){$process=Get-Process -Id $owned;if($process.Path -ne (Join-Path $app 'FileCat.exe')){throw 'Owned image path mismatch'};$null=$process.Handle}
   if($process.HasExited){throw 'Owned native process exited before completion'}
   if($report.indexedProbeError){throw $report.indexedProbeError}
   if($report.indexedProbeFinished){break}
  }
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 if(-not $report.indexedProbeFinished){throw 'The same native memory workflow did not finish'}
 $probe=$report.indexedProbe
 if(-not $report.memoryFallbackProbeRequested -or $report.sessions -ne 1 -or $probe.kind -ne 'memory-lifecycle' -or -not $report.memoryLifecycleProbeRequested -or $probe.view -ne 'Memory'){throw 'Native memory view mismatch'}
 if($probe.matchingPaths -ne $report.fixtureRegularPaths -or $probe.queryMatches -ne 1 -or $probe.unavailableRoots -ne 1){throw 'Native memory source/query/scope counts differ'}
 if(-not $probe.publicStartLifecycle -or $probe.acquisitionOwnerClosed -or -not $probe.capturedRevisionRetained -or $probe.sourceCloseFailures -ne 0){throw 'Native memory ownership mismatch'}
 if(-not $probe.stateUnchanged -or -not $probe.neighborUnchanged -or $probe.scratchCreated -or $probe.privateNamesObserved){throw 'Native private state changed'}
 # Only the FileCat visual tree rendered by this owned process, never desktop/system capture.
 Copy-Item -LiteralPath (Join-Path $run 'native-memory-window-render.png') -Destination (Join-Path $base 'native-memory-window-render.png')
 $record.captureSha256=(Get-FileHash -LiteralPath (Join-Path $base 'native-memory-window-render.png')).Hash.ToLowerInvariant()
 $record.processId=$owned;$record.finished=$report;$record.passed=$true
}catch{$record.error=$_|Out-String;throw}finally{
 if($run){[IO.File]::WriteAllText((Join-Path $run 'close.request'),'close only this owned memory fixture')}
 if($process){$record.ownedProcessExited=$process.WaitForExit(30000);$process.Dispose()}
 if($run){
  $closed=ReadReport (Join-Path $run 'report.json');$record.closed=$closed
  if($closed.state -ne 'closed' -or $closed.indexedProbeError -or -not $closed.sourceUnchanged -or $closed.deadlineClosed -or -not $record.ownedProcessExited){$record.passed=$false}
  if($closed.indexedProbe){
   $p=$closed.indexedProbe
   $record.stateHashAfterClose=(Get-FileHash -LiteralPath $p.privateStateFile).Hash.ToLowerInvariant()
   $record.neighborHashAfterClose=(Get-FileHash -LiteralPath $p.neighborFile).Hash.ToLowerInvariant()
   $record.scratchCreatedAfterClose=Test-Path -LiteralPath $p.scratch
   if($record.stateHashAfterClose -ne $p.privateStateSha256 -or $record.neighborHashAfterClose -ne $p.neighborSha256 -or $record.scratchCreatedAfterClose){$record.passed=$false}
  }
 }
 $record|ConvertTo-Json -Depth 14|Set-Content (Join-Path $base 'native-memory-result.json')
}
if(-not $record.passed){throw 'Native memory cleanup/source verification failed'}
Gate
'PASS'|Set-Content $state
