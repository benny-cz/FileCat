param([ValidateSet('Stage','Prepare','Observe')][string]$Mode,[int]$Pass=1,[string]$ExpectedHash)
$ErrorActionPreference='Stop'
$base='C:\FileCatDsa20261104R2\desktop-r71-profile';$app=Join-Path $base 'app';$state=Join-Path $base 'state.txt'
function Gate([int]$Owned=0){
 $others=@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue|Where-Object Id -ne $Owned)
 $runners=@(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.CommandLine -match '(?i)FileCat.*[.]dll'})
 if($others.Count -or $runners.Count){throw 'BUSY: postpone this machine'}
}
function ReadReport([string]$Path){try {Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json}catch{return $null}}
if($Mode -eq 'Stage'){
 Gate
 $archive='C:\FileCatDsa20261104R2\desktop-r71-windows.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne $ExpectedHash){throw 'Archive mismatch'}
 if(Test-Path -LiteralPath $app){throw 'Use a fresh native profile stage'}
 New-Item -ItemType Directory -Path $app|Out-Null
 & tar.exe -xzf $archive -C $app;if($LASTEXITCODE){throw 'Extraction failed'}
 'STAGED'|Set-Content $state;exit 0
}
if($Mode -eq 'Prepare'){
 Gate
 if($Pass -eq 3){
  $profile=@(Get-ChildItem -LiteralPath (Join-Path $app 'native-validation-shared-profile') -Recurse -Filter private-state.json -File)
  if($profile.Count -ne 1 -or $profile[0].LinkType -or $profile[0].FullName -notlike ($app+'\native-validation-shared-profile\*')){throw 'Unexpected generated profile'}
  $expected=Get-Content (Join-Path $base 'private-state-sha256.txt') -Raw
  if((Get-FileHash -LiteralPath $profile[0].FullName).Hash -ne $expected.Trim()){throw 'Profile bytes changed before corruption probe'}
  [IO.File]::WriteAllText($profile[0].FullName,'{"schema":"future-profile","test-canary":"preserve exact bytes"}',[Text.UTF8Encoding]::new($false))
 }
 'PREPARED'|Set-Content $state;exit 0
}
$record=[ordered]@{purpose='Native Windows profile restart and fallback subset; no native input qualification';pass=$Pass};$run=$null;$owned=0
try{
 $until=[DateTime]::UtcNow.AddSeconds(150)
 do{
  $candidates=@(Get-ChildItem -LiteralPath (Join-Path $app 'native-validation') -Directory -ErrorAction SilentlyContinue|ForEach-Object{
   $r=ReadReport (Join-Path $_.FullName 'report.json');if($r -and $r.state -ne 'closed'){[pscustomobject]@{Run=$_.FullName;Report=$r}}
  })
  if($candidates.Count -eq 1){
   $run=$candidates[0].Run;$report=$candidates[0].Report;$owned=[int]$report.processId;Gate $owned
   $process=Get-Process -Id $owned
   if($process.Path -ne (Join-Path $app 'FileCat.exe')){throw 'Owned image path mismatch'}
   if($report.profileProbeError){throw $report.profileProbeError}
   if($report.profileProbeFinished){break}
  }
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 if(-not $report.profileProbeFinished){throw 'Profile probe timed out'}
 if($report.lastObservation.Phase -ne 5 -or $report.profileProbeCaptureError){throw ('Native scan/capture failed '+$report.profileProbeCaptureError)}
 $profile=@(Get-ChildItem -LiteralPath (Join-Path $app 'native-validation-shared-profile') -Recurse -Filter private-state.json -File)
 if($profile.Count -ne 1){throw 'Unexpected private profile count'}
 $current=$report.lastObservation.hostProfile;$notice=$report.lastObservation.ContinuityNotice
 $hash=(Get-FileHash -LiteralPath $profile[0].FullName).Hash
 if($Pass -eq 1){
  if($current -notlike 'hp1:*' -or $notice){throw 'Native host context unavailable'}
  $current|Set-Content (Join-Path $base 'identity.txt');$hash|Set-Content (Join-Path $base 'private-state-sha256.txt')
 }elseif($Pass -eq 2){
  if($current -ne (Get-Content (Join-Path $base 'identity.txt') -Raw).Trim() -or $notice -or $hash -ne (Get-Content (Join-Path $base 'private-state-sha256.txt') -Raw).Trim()){throw 'Restart continuity mismatch'}
 }else{
  if($current -notlike 'ephemeral:*' -or -not $notice -or [IO.File]::ReadAllText($profile[0].FullName) -ne '{"schema":"future-profile","test-canary":"preserve exact bytes"}'){throw 'Corrupt-state fallback mismatch'}
 }
 Copy-Item -LiteralPath (Join-Path $run 'windows-native-profile-probe.png') -Destination (Join-Path $base ('native-profile-'+$Pass+'.png'))
 $record.processId=$owned;$record.finished=$true;$record.hasQualifiedHostProfile=$current.StartsWith('hp1:');$record.continuityNotice=$notice;$record.privateBytesPreserved=$true
 $record.fixtureRegularPaths=$report.fixtureRegularPaths;$record.appSha256=$report.appSha256;$record.harnessSha256=$report.harnessSha256
}catch{$record.error=$_|Out-String;throw}finally{
 if($run){[IO.File]::WriteAllText((Join-Path $run 'close.request'),'')}
 if($owned){
  $until=[DateTime]::UtcNow.AddSeconds(20)
  while((Get-Process -Id $owned -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 200}
  $record.ownedProcessExited=-not [bool](Get-Process -Id $owned -ErrorAction SilentlyContinue)
  $closed=ReadReport (Join-Path $run 'report.json');$record.state=$closed.state;$record.sourceUnchanged=$closed.sourceUnchanged;$record.deadlineClosed=$closed.deadlineClosed
 }
 $record|ConvertTo-Json -Depth 10|Set-Content (Join-Path $base ('result-'+$Pass+'.json'))
}
if(-not $record.ownedProcessExited -or $record.state -ne 'closed' -or -not $record.sourceUnchanged -or $record.deadlineClosed){throw 'Native cleanup/source verification failed'}
Gate
('PASS '+$Pass)|Set-Content $state
