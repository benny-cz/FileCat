param([ValidateSet('Stage','Observe')][string]$Mode,[string]$ExpectedHash)
$ErrorActionPreference='Stop'
$base='C:\FileCatDsa20261104R2\desktop-r397-indexed';$app=Join-Path $base 'app';$state=Join-Path $base 'state.txt'
function Gate([int]$Owned=0){
 $others=@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue|Where-Object Id -ne $Owned)
 $runners=@(Get-Process -Name dotnet -ErrorAction SilentlyContinue)
 if($others.Count -or $runners.Count){throw 'BUSY: postpone this machine'}
}
function ReadReport([string]$Path){try {Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json}catch{return $null}}
if($Mode -eq 'Stage'){
 Gate
 $archive='C:\FileCatDsa20261104R2\desktop-r397-windows.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne $ExpectedHash){throw 'Archive mismatch'}
 if(Test-Path -LiteralPath $app){throw 'Use a fresh native indexed stage'}
 New-Item -ItemType Directory -Path $app|Out-Null
 & tar.exe -xzf $archive -C $app;if($LASTEXITCODE){throw 'Extraction failed'}
 'STAGED'|Set-Content $state;exit 0
}
$record=[ordered]@{purpose='Native Windows indexed profile/quota/acquisition/window/namespace subset; no physical-input qualification';passed=$false};$run=$null;$owned=0;$process=$null
try{
 $until=[DateTime]::UtcNow.AddSeconds(180)
 do{
  $candidates=@(Get-ChildItem -LiteralPath (Join-Path $app 'native-validation') -Directory -ErrorAction SilentlyContinue|ForEach-Object{
   $r=ReadReport (Join-Path $_.FullName 'report.json');if($r -and $r.state -ne 'closed'){[pscustomobject]@{Run=$_.FullName;Report=$r}}
  })
  if($candidates.Count -eq 1){
   $run=$candidates[0].Run;$report=$candidates[0].Report;$owned=[int]$report.processId;Gate $owned
   if($null -eq $process){$process=Get-Process -Id $owned;if($process.Path -ne (Join-Path $app 'FileCat.exe')){throw 'Owned image path mismatch'};$null=$process.Handle}
   if($process.HasExited){throw 'Owned native process exited before completion'}
   if($report.indexedProbeError){throw $report.indexedProbeError}
   if($report.indexedProbeFinished -and (Test-Path -LiteralPath (Join-Path $run 'windows-native-indexed-probe.png'))){break}
  }
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 if(-not $report.indexedProbeFinished){throw 'Native indexed workflow timed out'}
 $probe=$report.indexedProbe
 if($probe.matchingPaths -ne $report.fixtureRegularPaths -or -not $probe.acquisitionOwnerClosed -or $probe.sourceCloseFailures -ne 0 -or $probe.order -ne 'Name' -or $probe.Descending -or $probe.view -ne 'Table' -or $report.sessions -ne 1){throw 'Native indexed observation mismatch'}
 if($probe.namespaceInspected -and ($probe.namespaceEvidence -notlike '*Not counted*' -or $probe.namespaceEvidence -notlike '*Link not followed*')){throw 'Namespace evidence mismatch'}
 Copy-Item -LiteralPath (Join-Path $run 'windows-native-indexed-probe.png') -Destination (Join-Path $base 'native-indexed.png')
 $record.processId=$owned;$record.finished=$report;$record.passed=$true
}catch{$record.error=$_|Out-String;throw}finally{
 if($run){[IO.File]::WriteAllText((Join-Path $run 'close.request'),'close only this owned indexed fixture')}
 if($process){$record.ownedProcessExited=$process.WaitForExit(30000);$process.Dispose()}
 if($run){
  $closed=ReadReport (Join-Path $run 'report.json');$record.closed=$closed
  if($closed.state -ne 'closed' -or -not $closed.sourceUnchanged -or $closed.deadlineClosed -or -not $record.ownedProcessExited){$record.passed=$false}
  if($closed.indexedProbe.scratch){$record.scratchRemaining=@(Get-ChildItem -LiteralPath $closed.indexedProbe.scratch -Force).Count;if($record.scratchRemaining){$record.passed=$false}}
 }
 $record|ConvertTo-Json -Depth 14|Set-Content (Join-Path $base 'native-indexed-result.json')
}
if(-not $record.passed){throw 'Native indexed cleanup/source verification failed'}
Gate
'PASS'|Set-Content $state
