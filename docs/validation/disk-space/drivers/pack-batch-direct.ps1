param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$RenderRevision,
 [Parameter(Mandatory=$true)][string]$BuildLog,
 [Parameter(Mandatory=$true)][string]$SourceManifest
)
$ErrorActionPreference='Stop'
$watch=[Diagnostics.Stopwatch]::StartNew()
$build=[IO.File]::ReadAllText((Join-Path $PWD $BuildLog))
foreach($required in 'Build succeeded.','0 Warning(s)','0 Error(s)'){if(!$build.Contains($required)){throw 'A completed clean build is required'}}
$sources=Get-Content -Raw -LiteralPath $SourceManifest|ConvertFrom-Json
foreach($entry in $sources){if((Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash.ToLowerInvariant()-ne$entry.sha256){throw ('Source changed after freeze: '+$entry.path)}}
$stages=@{}
foreach($spec in @(@('core','tests/FileCat.Core.Tests/bin/Debug/net10.0'),@('native','tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0'))){
 $stage=Join-Path $PWD ('.tmp/'+$spec[0]+'-'+$Revision)
 if(Test-Path -LiteralPath $stage){throw 'A fresh stage is required'}
 New-Item -ItemType Directory -Path $stage|Out-Null
 Get-ChildItem -LiteralPath (Join-Path $PWD $spec[1]) -File|Copy-Item -Destination $stage
 $stages[$spec[0]]=$stage
}
$renderBin=Join-Path $PWD 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'
$archives=@()
foreach($platform in 'mac','linux','windows'){
 $render=Join-Path $PWD ('.tmp/render-'+$RenderRevision+'-'+$platform)
 $batch=Join-Path $PWD ('.tmp/batch-'+$Revision+'-'+$platform)
 if((Test-Path -LiteralPath $render)-or(Test-Path -LiteralPath $batch)){throw 'A fresh platform stage is required'}
 New-Item -ItemType Directory -Path $render|Out-Null
 Get-ChildItem -LiteralPath $renderBin -File|Copy-Item -Destination $render
 New-Item -ItemType Directory -Path (Join-Path $render 'runtimes')|Out-Null
 $rids=switch($platform){'mac'{@('osx','osx-arm64')};'linux'{@('linux-x64')};'windows'{@('win','win-x64')}}
 foreach($rid in $rids){Copy-Item -LiteralPath (Join-Path $renderBin ('runtimes/'+$rid)) -Destination (Join-Path $render 'runtimes') -Recurse}
 New-Item -ItemType Directory -Path $batch|Out-Null
 Copy-Item -LiteralPath $stages.core -Destination (Join-Path $batch 'core') -Recurse
 Copy-Item -LiteralPath $render -Destination (Join-Path $batch 'render') -Recurse
 if($platform-eq'windows'){Copy-Item -LiteralPath $stages.native -Destination (Join-Path $batch 'native') -Recurse}
 $archive=Join-Path $PWD ('.tmp/dsa-batch-'+$platform+'-'+$Revision+'.tgz')
 & tar.exe -czf $archive -C $batch .
 if($LASTEXITCODE){throw 'Candidate archive failed'}
 $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
 $template=if($platform-eq'windows'){'batch-windows-grouping-template.ps1'}else{'batch-unix-grouping-template.sh'}
 $runner=[IO.File]::ReadAllText((Join-Path $PWD ('.tmp/'+$template))).Replace('@HASH@',$hash).Replace('@REV@',$Revision)
 $extension=if($platform-eq'windows'){'ps1'}else{'sh'}
 [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/batch-'+$platform+'-'+$Revision+'.'+$extension)),$runner.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
 $archives+=@{platform=$platform;path=$archive;sha256=$hash;bytes=(Get-Item -LiteralPath $archive).Length}
}
$watch.Stop()
[ordered]@{revision=$Revision;renderRevision=$RenderRevision;buildLog=$BuildLog;sourceManifest=$SourceManifest;archiveCount=3;standaloneArchives=0;elapsedSeconds=$watch.Elapsed.TotalSeconds;archives=$archives}|ConvertTo-Json -Depth 6|Set-Content ('.tmp/pack-timing-'+$Revision+'.json')
Write-Output ('Prepared exactly three deployable archives in '+[math]::Round($watch.Elapsed.TotalSeconds,2)+' seconds.')
