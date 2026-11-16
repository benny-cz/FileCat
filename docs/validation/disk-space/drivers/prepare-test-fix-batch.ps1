param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$BaselineRevision,
 [Parameter(Mandatory=$true)][ValidateSet('core','render')][string]$Phase,
 [Parameter(Mandatory=$true)][string]$BuildLog,
 [Parameter(Mandatory=$true)][string]$SourceManifest,
 [ValidateSet('windows','mac','linux')][string[]]$ReusePlatforms=@('windows'),
 [ValidateSet('TestOnly','AppUi','OldProductControl')][string]$InputScope='TestOnly',
 [ValidateSet('core','render')][string[]]$AdditionalTestPhases=@(),
 [string]$WindowsTemplate='.tmp/batch-windows-grouping-template.ps1',
 [string]$UnixTemplate='.tmp/batch-unix-grouping-template.sh'
)
$ErrorActionPreference='Stop';$watch=[Diagnostics.Stopwatch]::StartNew()
$build=[IO.File]::ReadAllText((Join-Path $PWD $BuildLog))
if($build.Contains('Build FAILED.')){throw 'A failed build cannot qualify a cached delta'}
foreach($required in 'Build succeeded.','0 Warning(s)','0 Error(s)'){if(!$build.Contains($required)){throw 'A completed clean build is required'}}
foreach($item in (Get-Content -Raw -LiteralPath $SourceManifest|ConvertFrom-Json)){
 if((Get-FileHash -LiteralPath $item.path -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.sha256){throw 'Source changed after freeze'}
}
$assemblies=@{core='FileCat.Core.Tests';native='FileCat.Platform.Windows.Tests';render='FileCat.Rendering.Tests'}
$testPhases=@(@($Phase)+$AdditionalTestPhases|Sort-Object -Unique)
if($AdditionalTestPhases.Count-and$InputScope-ne'TestOnly'){throw 'Additional test assemblies are only permitted for TestOnly inputs'}
$allowed=@($testPhases|ForEach-Object{($_+'/'+$assemblies[$_]+'.dll');($_+'/'+$assemblies[$_]+'.pdb')})
$appFiles=@('render/FileCat.dll','render/FileCat.pdb')
if($InputScope-ne'TestOnly'-and$Phase-ne'render'){throw 'App UI changes and old-product controls require rendering inputs'}
if($InputScope-eq'AppUi'){$allowed+=$appFiles}
$proof=@()
foreach($platform in 'windows','mac','linux'){
 $old=Join-Path $PWD ('.tmp/batch-'+$BaselineRevision+'-'+$platform)
 $stage=Join-Path $PWD ('.tmp/batch-'+$Revision+'-'+$platform)
 if(Test-Path -LiteralPath $stage){throw 'A fresh batch stage is required'}
 New-Item -ItemType Directory -Path $stage|Out-Null
 $changed=@()
 $files=@(Get-ChildItem -LiteralPath $old -File -Recurse|ForEach-Object{
  $relative=[IO.Path]::GetRelativePath($old,$_.FullName).Replace('\','/')
  $parts=$relative.Split('/',2);$bin=Join-Path $PWD ('tests/'+$assemblies[$parts[0]]+'/bin/Debug/net10.0')
  if($relative-eq'FileCat.slnx'-or$relative.StartsWith('tests/',[StringComparison]::Ordinal)){$current=Join-Path $PWD $relative}
  elseif($relative.StartsWith('render/FileCat.App.Tests.',[StringComparison]::Ordinal)){$current=Join-Path $PWD ('tests/FileCat.App.Tests/bin/Debug/net10.0/'+$parts[1])}
  else{$current=Join-Path $bin $parts[1]}
  if($InputScope-eq'OldProductControl'-and$relative-notin$allowed){$current=$_.FullName}
  $before=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $after=(Get-FileHash -LiteralPath $current -Algorithm SHA256).Hash.ToLowerInvariant()
  $patch=$before-ne$after
  if($patch){if($relative-notin$allowed){throw ('An undeclared input changed: '+$relative)};$script:changed+=@($relative)}
  $target=Join-Path $stage $relative;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null
  Copy-Item -LiteralPath $current -Destination $target
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()-ne$after){throw 'Staged input mismatch'}
  [ordered]@{path=$relative;baselineSha256=$before;sha256=$after;patch=$patch}
 })
 if($InputScope-eq'AppUi'){
  if(@($appFiles|Where-Object{$_-notin$changed}).Count-or$changed.Count-notin2,4){throw 'Expected the App DLL/PDB and optionally one test DLL/PDB pair'}
 }elseif($changed.Count-ne$allowed.Count-or@($allowed|Where-Object{$_-notin$changed}).Count){throw 'Expected exactly the declared test DLL/PDB pairs'}
 $archive=Join-Path $PWD ('.tmp/dsa-batch-'+$platform+'-'+$Revision+'.tgz')
 if(Test-Path -LiteralPath $archive){throw 'A fresh archive is required'}
 $extension=if($platform-eq'windows'){'ps1'}else{'sh'}
 $template=if($platform-eq'windows'){$WindowsTemplate}else{$UnixTemplate}
 $core=if(Test-Path -LiteralPath (Join-Path $old 'tests/FileCat.Core.Tests/bin/Debug/net10.0')){'tests/FileCat.Core.Tests/bin/Debug/net10.0'}else{'core'}
 $runner=[IO.File]::ReadAllText((Join-Path $PWD $template)).Replace("`r`n","`n").Replace('@CORE@',$core)
 if($platform-eq'windows'-and$platform-in$ReusePlatforms){
  $delta=Join-Path $PWD ('.tmp/test-fix-delta-'+$Revision);New-Item -ItemType Directory -Path $delta|Out-Null
  foreach($relative in $changed){$target=Join-Path $delta ('patch/'+$relative);New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $stage $relative) -Destination $target}
  $baseline='C:\FileCatDsa20261104R2\batch-'+$BaselineRevision+'\payload'
  [ordered]@{baseline=$baseline;files=$files;scope=$InputScope}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $delta 'plan.json')
  & tar.exe -czf $archive -C $delta .
  $start=$runner.IndexOf(" `$payload=Join-Path `$base 'payload'");$finish=$runner.IndexOf(' $overall=0')
  if($start-lt0-or$finish-le$start){throw 'Runner extraction boundary changed'}
  $reuse=@'
 $patchInput=Join-Path $base 'patch-input'
 if(Test-Path -LiteralPath $patchInput){throw 'Fresh patch extraction required'}
 New-Item -ItemType Directory -Path $patchInput|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $patchInput
 if($LASTEXITCODE){throw 'Patch extraction failed'}
 $plan=Get-Content -Raw -LiteralPath (Join-Path $patchInput 'plan.json')|ConvertFrom-Json
 if($plan.baseline-ne'@BASELINE@'){throw 'Unexpected cached baseline'}
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains(':')-or@($item.path-split'/'|Where-Object{$_-eq'..'}).Count){throw 'Invalid cached input path'}
  if($item.patch-and$item.path-notin @(@ALLOWED@)){throw 'Undeclared input patch refused'}
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
'@
  $reuse=$reuse.Replace('@BASELINE@',$baseline).Replace('@ALLOWED@',(($allowed|ForEach-Object{"'"+$_+"'"})-join','))
  $runner=$runner.Substring(0,$start)+$reuse+"`n"+$runner.Substring($finish)
 }elseif($platform-in$ReusePlatforms){
  $delta=Join-Path $PWD ('.tmp/test-fix-delta-'+$platform+'-'+$Revision);New-Item -ItemType Directory -Path $delta|Out-Null
  foreach($relative in $changed){$target=Join-Path $delta ('patch/'+$relative);New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $stage $relative) -Destination $target}
  $guestRoot=if($platform-eq'mac'){'/Users/benny/FileCatValidation/'}else{'/home/benny/FileCatValidation/'}
  $baseline=$guestRoot+'dsa-batch-20261107-'+$BaselineRevision+'/payload'
  [ordered]@{baseline=$baseline;files=$files;allowed=$allowed;scope=$InputScope}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $delta 'plan.json')
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-batch-cache.py') -Destination (Join-Path $delta 'reuse.py')
  & tar.exe -czf $archive -C $delta .
  $extract="mkdir payload`ntar -xzf bundle.tgz -C payload"
  if(!$runner.Contains($extract)){throw 'Unix runner extraction boundary changed'}
  $reuse="mkdir patch-input`ntar -xzf bundle.tgz -C patch-input`npython3 patch-input/reuse.py '"+$baseline+"'"
  $runner=$runner.Replace($extract,$reuse)
 }else{& tar.exe -czf $archive -C $stage .}
 if($LASTEXITCODE){throw 'Test-fix archive failed'}
 $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/batch-'+$platform+'-'+$Revision+'.'+$extension)),$runner.Replace('@HASH@',$hash).Replace('@REV@',$Revision).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
 $proof+=@{platform=$platform;verifiedInputs=$files.Count;changed=$changed;guestReuse=$platform-in$ReusePlatforms;archiveBytes=(Get-Item -LiteralPath $archive).Length;sha256=$hash}
}
$watch.Stop()
[ordered]@{revision=$Revision;baseline=$BaselineRevision;scope=$InputScope;testPhases=$testPhases;productSourceBaseline=if($InputScope-eq'OldProductControl'){$BaselineRevision}else{$null};elapsedSeconds=$watch.Elapsed.TotalSeconds;platforms=$proof}|ConvertTo-Json -Depth 5|Set-Content ('.tmp/test-fix-proof-'+$Revision+'.json')
Write-Output ('Prepared one immutable batch per platform; verified guest reuse: '+($ReusePlatforms-join', ')+'. Seconds: '+[math]::Round($watch.Elapsed.TotalSeconds,2))
