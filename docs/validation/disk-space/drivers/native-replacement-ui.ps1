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
$inputFolder=Join-Path $base 'input'
New-Item -ItemType Directory -Path $inputFolder|Out-Null
& tar.exe -xzf (Join-Path $base 'app.tgz') -C $inputFolder
if($LASTEXITCODE){throw 'Native extraction failed'}
$planFile=Join-Path $inputFolder 'plan.json'
if(Test-Path -LiteralPath $planFile){
 $plan=Get-Content -LiteralPath $planFile -Raw|ConvertFrom-Json
 if($plan.baseline-notmatch'^C:\\FileCatDsa20261104R2\\native-r[0-9]+\\app$'){throw 'Unexpected native cache root'}
 if((Get-Item -LiteralPath $plan.baseline).Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'Native cache is redirected'}
 $names=@{}
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains(':')-or@($item.path-split'/'|Where-Object{$_-eq'..'}).Count-or$names.ContainsKey($item.path)){throw 'Invalid native input path'}
  $names[$item.path]=$true
  $original=Join-Path $plan.baseline $item.path
  $node=Get-Item -LiteralPath $original
  if($node.PSIsContainer-or($node.Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Native cached input is redirected'}
  for($parent=$node.Directory;$parent.FullName-ne$plan.baseline;$parent=$parent.Parent){if(!$parent-or($parent.Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Native cached parent is redirected'}}
  if((Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant()-ne$item.baselineSha256){throw 'Native cached input changed'}
  if($item.patch-and$item.path-notin@('DiskSpaceHarness.dll','DiskSpaceHarness.pdb')){throw 'Native product patch refused'}
 }
 if(!$names.ContainsKey('FileCat.exe')-or!$names.ContainsKey('DiskSpaceHarness.dll')){throw 'Native input set incomplete'}
 foreach($item in $plan.files){
  $source=if($item.patch){Join-Path $inputFolder ('patch/'+$item.path)}else{Join-Path $plan.baseline $item.path}
  if((Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()-ne$item.sha256){throw 'Native copied input mismatch'}
  $target=Join-Path $app $item.path
  New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null
  Copy-Item -LiteralPath $source -Destination $target
  if((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant()-ne$item.sha256){throw 'Native staged input mismatch'}
 }
 @{baseline=$plan.baseline;verifiedInputs=$names.Count;patched=@($plan.files|Where-Object patch|ForEach-Object path)}|ConvertTo-Json|Set-Content (Join-Path $base 'cache-proof.json')
}else{Get-ChildItem -LiteralPath $inputFolder|Copy-Item -Destination $app -Recurse}
if((Observed).Count){'BUSY: postpone before native launch';exit 75}
$child=Start-Process -FilePath (Join-Path $app 'FileCat.exe') -ArgumentList '--replacement-ui-probe' -WorkingDirectory $app -WindowStyle Normal -PassThru -RedirectStandardOutput (Join-Path $base 'desktop.log') -RedirectStandardError (Join-Path $base 'desktop-error.log')
$null=$child.Handle
$child.Id|Set-Content (Join-Path $base 'owned.pid')
$result=[ordered]@{purpose='Native window control-event and filesystem subset; OS input/a11y/compositor/package coverage remains open';pid=$child.Id;bundleSha256=$ExpectedHash;passed=$false}
$result.launch=@{user=[Security.Principal.WindowsIdentity]::GetCurrent().Name;runnerSession=(Get-Process -Id $PID).SessionId;childSession=$child.SessionId;childName=$child.ProcessName}
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
  $others=@($ids|Where-Object{$_-ne$child.Id})
  if($child.HasExited-or$others.Count){$result.gateObservation=@(Get-Process -Id $ids -ErrorAction SilentlyContinue|Select-Object Id,ProcessName,SessionId);throw 'Owned child ended or another tester appeared'}
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
 if(Test-Path -LiteralPath (Join-Path $base 'cache-proof.json')){$names+='cache-proof.json'}
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
