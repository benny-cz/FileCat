$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try {
 if((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $base 'bundle.tgz')).Hash -ne 'bf64b75bbfa0e4e031e20cfb7bd806c90417e2a22871954622d516bffcce2132'){throw 'Candidate archive hash mismatch'}
 $patchInput=Join-Path $base 'patch-input'
 if(Test-Path -LiteralPath $patchInput){throw 'Fresh patch extraction required'}
 New-Item -ItemType Directory -Path $patchInput|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $patchInput
 if($LASTEXITCODE){throw 'Patch extraction failed'}
 $plan=Get-Content -Raw -LiteralPath (Join-Path $patchInput 'plan.json')|ConvertFrom-Json
 if($plan.baseline-ne'C:\FileCatDsa20261104R2\batch-r921\payload'){throw 'Unexpected cached baseline'}
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains(':')-or@($item.path-split'/'|Where-Object{$_-eq'..'}).Count){throw 'Invalid cached input path'}
  if($item.patch-and$item.path-notin @('render/FileCat.Rendering.Tests.dll','render/FileCat.Rendering.Tests.pdb')){throw 'Product input patch refused'}
  if((Get-FileHash -LiteralPath (Join-Path $plan.baseline $item.path) -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.baselineSha256){throw 'Cached input changed'}
 }
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 foreach($item in $plan.files){
  $source=if($item.patch){Join-Path $patchInput ('patch/'+$item.path)}else{Join-Path $plan.baseline $item.path}
  $target=Join-Path $payload $item.path;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null
  Copy-Item -LiteralPath $source -Destination $target
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.sha256){throw 'Copied input mismatch'}
 }
 ('VERIFIED cachedFiles='+$plan.files.Count+' changedFiles='+@($plan.files|Where-Object{$_.patch}).Count)|Set-Content (Join-Path $base 'cache-proof.txt')
 $overall=0
 foreach($phase in 'core','native','render'){
  if(Busy){('BUSY phase='+$phase)|Set-Content $state;exit 75}
  ('RUNNING phase='+$phase+' runnerPid='+$PID)|Set-Content $state
  $bin=Join-Path $payload $phase
  $assembly=switch($phase){'core'{'FileCat.Core.Tests.dll'};'native'{'FileCat.Platform.Windows.Tests.dll'};'render'{'FileCat.Rendering.Tests.dll'}}
  $filters=switch($phase){'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')};'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')};'render'{@('-class','*AnalysisRenderingTests')}}
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r924.xml')))
  Set-Location -LiteralPath $bin
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r924.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($result -ne 0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
} catch {($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
