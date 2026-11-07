$ErrorActionPreference='Stop'
$watch=[Diagnostics.Stopwatch]::StartNew()
$preparation=[IO.File]::ReadAllText((Join-Path $PWD '.tmp/prepare-comparison-save-r846.ps1')).Replace('r846','r849').Replace('r847','r850').Replace('r845','r848')
$temporary=Join-Path $PWD '.tmp/pack-full-comparison-save-r849.ps1'
[IO.File]::WriteAllText($temporary,$preparation,[Text.UTF8Encoding]::new($false))
& $temporary
if($LASTEXITCODE){throw 'Full platform packing failed'}
$old=Join-Path $PWD '.tmp/batch-r846-windows';$current=Join-Path $PWD '.tmp/batch-r849-windows'
$delta=Join-Path $PWD '.tmp/delta-windows-r849';if(Test-Path -LiteralPath $delta){throw 'Fresh delta stage required'}
New-Item -ItemType Directory -Path $delta|Out-Null
$changed=@();$files=@(Get-ChildItem -LiteralPath $current -File -Recurse|ForEach-Object{
 $relative=[IO.Path]::GetRelativePath($current,$_.FullName).Replace('\','/')
 $before=Join-Path $old $relative;if(!(Test-Path -LiteralPath $before)){throw ('Uncached input: '+$relative)}
 $oldHash=(Get-FileHash -LiteralPath $before -Algorithm SHA256).Hash.ToLowerInvariant();$newHash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 $patch=$oldHash-ne$newHash
 if($patch){$script:changed+=@($relative);$target=Join-Path $delta ('patch/'+$relative);New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null;Copy-Item -LiteralPath $_.FullName -Destination $target}
 [ordered]@{path=$relative;baselineSha256=$oldHash;sha256=$newHash;patch=$patch}
})
if($changed.Count-ne2-or@($changed|Where-Object{$_-notin 'core/FileCat.Core.Tests.dll','core/FileCat.Core.Tests.pdb'}).Count){throw ('Helper-only delta contained unexpected changes: '+($changed-join', '))}
[ordered]@{baseline='C:\FileCatDsa20261104R2\batch-r846\payload';files=$files}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $delta 'plan.json')
$archive=Join-Path $PWD '.tmp/dsa-batch-windows-r849.tgz';$originalHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
& tar.exe -czf $archive -C $delta .;if($LASTEXITCODE){throw 'Delta archive failed'}
$newHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$runnerPath=Join-Path $PWD '.tmp/batch-windows-r849.ps1';$runner=[IO.File]::ReadAllText($runnerPath)
$start=$runner.IndexOf(" `$payload=Join-Path `$base 'payload'");$finish=$runner.IndexOf(" foreach(`$check in 'smoke','preview')")
if($start-lt0-or$finish-le$start){throw 'Runner extraction boundary changed'}
$reuse=@'
 $patchInput=Join-Path $base 'patch-input'
 if(Test-Path -LiteralPath $patchInput){throw 'Fresh patch extraction required'}
 New-Item -ItemType Directory -Path $patchInput|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $patchInput
 if($LASTEXITCODE){throw 'Patch extraction failed'}
 $plan=Get-Content -Raw -LiteralPath (Join-Path $patchInput 'plan.json')|ConvertFrom-Json
 $baseline=[IO.Path]::GetFullPath($plan.baseline)
 if($baseline-ne'C:\FileCatDsa20261104R2\batch-r846\payload'){throw 'Unexpected cached baseline'}
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains(':')-or@($item.path-split'/'|Where-Object{$_-eq'..'}).Count){throw 'Invalid cached input path'}
  $cachedFile=Join-Path $baseline $item.path
  if((Get-FileHash -LiteralPath $cachedFile -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.baselineSha256){throw ('Cached input changed: '+$item.path)}
 }
 $payload=Join-Path $base 'payload'
 if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 foreach($item in $plan.files){
  $source=if($item.patch){Join-Path $patchInput ('patch/'+$item.path)}else{Join-Path $baseline $item.path}
  $target=Join-Path $payload $item.path
  New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null
  Copy-Item -LiteralPath $source -Destination $target
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.sha256){throw ('Copied input mismatch: '+$item.path)}
 }
 ('VERIFIED cachedFiles='+$plan.files.Count+' changedFiles='+@($plan.files|Where-Object{$_.patch}).Count)|Set-Content (Join-Path $base 'cache-proof.txt')
'@
$runner=$runner.Substring(0,$start)+$reuse+"`n"+$runner.Substring($finish)
$runner=$runner.Replace($originalHash,$newHash)
[IO.File]::WriteAllText($runnerPath,$runner,[Text.UTF8Encoding]::new($false))
$watch.Stop()
[ordered]@{revision='r849';baseline='r846';verifiedInputs=$files.Count;changed=$changed;archiveBytes=(Get-Item -LiteralPath $archive).Length;sha256=$newHash;elapsedSeconds=$watch.Elapsed.TotalSeconds}|ConvertTo-Json -Depth 4|Set-Content .tmp/delta-proof-r849.json
Write-Output ('Windows reused '+$files.Count+' verified inputs; only '+$changed.Count+' changed files; '+(Get-Item -LiteralPath $archive).Length+' archive bytes.')
