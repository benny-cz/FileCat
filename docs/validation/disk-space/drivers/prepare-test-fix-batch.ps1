param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$BaselineRevision,
 [Parameter(Mandatory=$true)][ValidateSet('core','render')][string]$Phase,
 [Parameter(Mandatory=$true)][string]$BuildLog,
 [Parameter(Mandatory=$true)][string]$SourceManifest
)
$ErrorActionPreference='Stop';$watch=[Diagnostics.Stopwatch]::StartNew()
$build=[IO.File]::ReadAllText((Join-Path $PWD $BuildLog))
foreach($required in 'Build succeeded.','0 Warning(s)','0 Error(s)'){if(!$build.Contains($required)){throw 'A completed clean build is required'}}
foreach($item in (Get-Content -Raw -LiteralPath $SourceManifest|ConvertFrom-Json)){
 if((Get-FileHash -LiteralPath $item.path -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.sha256){throw 'Source changed after freeze'}
}
$assemblies=@{core='FileCat.Core.Tests';native='FileCat.Platform.Windows.Tests';render='FileCat.Rendering.Tests'}
$allowed=@(($Phase+'/'+$assemblies[$Phase]+'.dll'),($Phase+'/'+$assemblies[$Phase]+'.pdb'))
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
  $current=Join-Path $bin $parts[1]
  $before=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $after=(Get-FileHash -LiteralPath $current -Algorithm SHA256).Hash.ToLowerInvariant()
  $patch=$before-ne$after
  if($patch){if($relative-notin$allowed){throw ('A test-only fix changed a product input: '+$relative)};$script:changed+=@($relative)}
  $target=Join-Path $stage $relative;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null
  Copy-Item -LiteralPath $current -Destination $target
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()-ne$after){throw 'Staged input mismatch'}
  [ordered]@{path=$relative;baselineSha256=$before;sha256=$after;patch=$patch}
 })
 if($changed.Count-ne2){throw 'Expected exactly a test DLL and PDB change'}
 $archive=Join-Path $PWD ('.tmp/dsa-batch-'+$platform+'-'+$Revision+'.tgz')
 if(Test-Path -LiteralPath $archive){throw 'A fresh archive is required'}
 $extension=if($platform-eq'windows'){'ps1'}else{'sh'}
 $template=if($platform-eq'windows'){'batch-windows-grouping-template.ps1'}else{'batch-unix-grouping-template.sh'}
 $runner=[IO.File]::ReadAllText((Join-Path $PWD ('.tmp/'+$template)))
 if($platform-eq'windows'){
  $delta=Join-Path $PWD ('.tmp/test-fix-delta-'+$Revision);New-Item -ItemType Directory -Path $delta|Out-Null
  foreach($relative in $changed){$target=Join-Path $delta ('patch/'+$relative);New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $stage $relative) -Destination $target}
  $baseline='C:\FileCatDsa20261104R2\batch-'+$BaselineRevision+'\payload'
  [ordered]@{baseline=$baseline;files=$files}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $delta 'plan.json')
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
  if($item.patch-and$item.path-notin @('@DLL@','@PDB@')){throw 'Product input patch refused'}
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
  $reuse=$reuse.Replace('@BASELINE@',$baseline).Replace('@DLL@',$allowed[0]).Replace('@PDB@',$allowed[1])
  $runner=$runner.Substring(0,$start)+$reuse+"`n"+$runner.Substring($finish)
 }else{& tar.exe -czf $archive -C $stage .}
 if($LASTEXITCODE){throw 'Test-fix archive failed'}
 $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/batch-'+$platform+'-'+$Revision+'.'+$extension)),$runner.Replace('@HASH@',$hash).Replace('@REV@',$Revision).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
 $proof+=@{platform=$platform;verifiedInputs=$files.Count;changed=$changed;guestReuse=$platform-eq'windows';archiveBytes=(Get-Item -LiteralPath $archive).Length;sha256=$hash}
}
$watch.Stop()
[ordered]@{revision=$Revision;baseline=$BaselineRevision;elapsedSeconds=$watch.Elapsed.TotalSeconds;platforms=$proof}|ConvertTo-Json -Depth 5|Set-Content ('.tmp/test-fix-proof-'+$Revision+'.json')
Write-Output ('Prepared one immutable batch per platform; Windows reuses verified product inputs. Seconds: '+[math]::Round($watch.Elapsed.TotalSeconds,2))
