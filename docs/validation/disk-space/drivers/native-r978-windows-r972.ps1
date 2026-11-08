param([Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedHash)
$ErrorActionPreference='Stop'
$base=$PSScriptRoot
if($base-notmatch'^C:\\FileCatDsa20261104R2\\native-r[0-9]+$'){throw 'Unexpected native fixture root'}
function Observed { @(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue|Select-Object -ExpandProperty Id|Sort-Object) }
if((Observed).Count){'BUSY: postpone native desktop testing';exit 75}
if((Get-FileHash -LiteralPath (Join-Path $base 'app.tgz')).Hash.ToLowerInvariant()-ne$ExpectedHash){throw 'Native archive mismatch'}
$app=Join-Path $base 'app'
if(Test-Path -LiteralPath $app){throw 'Fresh native fixture required'}
New-Item -ItemType Directory -Path $app|Out-Null
& tar.exe -xzf (Join-Path $base 'app.tgz') -C $app
if($LASTEXITCODE){throw 'Native extraction failed'}
if((Observed).Count){'BUSY: postpone before native launch';exit 75}
$child=Start-Process -FilePath (Join-Path $app 'FileCat.exe') -ArgumentList '--replacement-ui-probe' -WorkingDirectory $app -WindowStyle Normal -PassThru -RedirectStandardOutput (Join-Path $base 'desktop.log') -RedirectStandardError (Join-Path $base 'desktop-error.log')
$child.Id|Set-Content (Join-Path $base 'owned.pid')
$result=[ordered]@{purpose='Native window control-event and filesystem subset; OS input/a11y/compositor/package coverage remains open';pid=$child.Id;bundleSha256=$ExpectedHash;passed=$false}
$run=$null
function ReadReport {
 for($i=0;$i-lt20;$i++){
  try{return Get-Content -LiteralPath (Join-Path $run 'report.json') -Raw|ConvertFrom-Json}catch{Start-Sleep -Milliseconds 25}
 }
 throw 'The owned native report remained unreadable'
}
try{
 $until=[DateTime]::UtcNow.AddMinutes(6)
 while([DateTime]::UtcNow-lt$until){
  $ids=@(Observed)
  if($ids.Count-ne1-or$ids[0]-ne$child.Id){throw 'Owned child ended or another tester appeared'}
  $runs=@(Get-ChildItem -LiteralPath (Join-Path $app 'native-validation') -Directory -ErrorAction SilentlyContinue|Where-Object{$_.Name-match'^dsa-[0-9a-f]{32}$'})
  if($runs.Count-eq1-and(Test-Path -LiteralPath (Join-Path $runs[0].FullName 'report.json'))){
   $run=$runs[0].FullName;$data=ReadReport;$probe=$data.replacementUiProbe
   if($probe-and$probe.finished){
    $result.finished=$data
    if(!$probe.passed){throw $probe.error}
    if(@($probe.cases).Count-ne4-or$probe.sessions-ne8){throw 'Native replacement case count mismatch'}
    $result.passed=$true;break
   }
  }
  Start-Sleep -Milliseconds 100
 }
 if(!$result.passed){throw 'Native replacement batch timed out'}
}catch{$result.error=($_|Out-String)}
finally{
 if($run-and!$child.HasExited){'Close only this task-owned native fixture'|Set-Content (Join-Path $run 'close.request')}
 if(!$child.WaitForExit(30000)){
  # Process object binds the child actually created by this script; never terminate by name.
  $child.Kill();if(!$child.WaitForExit(15000)){throw 'Exact owned child did not exit'}
  $result.forcedOwnedTermination=$true;$result.passed=$false
 }
 $result.exitCode=$child.ExitCode
 if($run){
  $data=ReadReport;$result.closed=$data
  if($data.state-ne'closed'-or$data.sourceUnchanged-ne$true-or$data.deadlineClosed-ne$false-or$data.indexedProbeError){$result.passed=$false}
 }
 $result.postProcesses=@(Observed)
 if($result.postProcesses.Count){$result.passed=$false}
 $result|ConvertTo-Json -Depth 30|Set-Content (Join-Path $base 'result.json')
 $names=@('result.json','desktop.log','desktop-error.log','owned.pid')
 if($run){
  Copy-Item -LiteralPath (Join-Path $run 'report.json') -Destination $base
  $names+='report.json'
  Get-ChildItem -LiteralPath $run -Filter 'replacement-*.png' -File|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $base;$names+=$_.Name}
 }
 Push-Location -LiteralPath $base
 try{& tar.exe -czf results.tgz @names;if($LASTEXITCODE){throw 'Native result archive failed'}}finally{Pop-Location}
}
@{passed=$result.passed;error=$result.error;exitCode=$result.exitCode;postProcesses=$result.postProcesses}|ConvertTo-Json -Depth 5
if($result.passed-and$result.exitCode-eq0){exit 0}else{exit 1}
