$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$state=Join-Path $base 'state.txt'
if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use the ordinary R2 interactive desktop'}
function Busy { @(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
('RUNNING phase=verify runnerPid='+$PID)|Set-Content $state
try{
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'1668aad78a0330fec43feb58d0e3c53a95d0d3e5a5d0c51f5c80cc78f6068a8e'){throw 'Bundle changed'}
 $patchInput=Join-Path $base 'patch-input'
 if(Test-Path -LiteralPath $patchInput){throw 'Fresh patch extraction required'}
 New-Item -ItemType Directory -Path $patchInput|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $patchInput
 if($LASTEXITCODE){throw 'Patch extraction failed'}
 $plan=Get-Content -Raw -LiteralPath (Join-Path $patchInput 'plan.json')|ConvertFrom-Json
 if($plan.baseline-ne'C:\FileCatDsa20261104R2\batch-r1111\payload'){throw 'Unexpected cached baseline'}
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains(':')-or@($item.path-split'/'|Where-Object{$_-eq'..'}).Count){throw 'Invalid cached input path'}
  if($item.patch-and$item.path-notin @('render/FileCat.Rendering.Tests.dll','render/FileCat.Rendering.Tests.pdb')){throw 'Undeclared input patch refused'}
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
  $arguments=@((Join-Path $bin $assembly))+$filters+@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base ($phase+'-r1112.xml')))
  $ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'-r1112.log'))
  $result=$LASTEXITCODE
  $ErrorActionPreference='Stop'
  if($phase-eq'preview'){
   $a=([xml][IO.File]::ReadAllText((Join-Path $base 'preview-r1112.xml'))).assemblies.assembly
   if($result-ne0-or[int]$a.total-ne44-or[int]$a.passed-ne44-or[int]$a.failed-ne0-or[int]$a.errors-ne0){'FINISHED exit=1 phase=preview'|Set-Content $state;exit 1}
  }
  if($result-ne0){$overall=1}
 }
 ('FINISHED exit='+$overall)|Set-Content $state
 exit $overall
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
