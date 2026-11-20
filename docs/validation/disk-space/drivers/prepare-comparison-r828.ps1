$ErrorActionPreference='Stop'
$paths=@(git diff --name-only -- src tests)+@(git ls-files --others --exclude-standard -- src tests)
$sources=@($paths|Sort-Object -Unique|ForEach-Object{[ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
$sources|ConvertTo-Json -Depth 4|Set-Content .tmp/source-candidate-r828.json
& docs/validation/disk-space/drivers/pack-batch-direct.ps1 -Revision r828 -RenderRevision r829 -BuildLog .tmp/build-r827.txt -SourceManifest .tmp/source-candidate-r828.json
if($LASTEXITCODE){throw 'Candidate packing failed'}
$path=Join-Path $PWD '.tmp/batch-windows-r828.ps1'
$body=[IO.File]::ReadAllText($path)
$smoke=@'
 if(Busy){'BUSY phase=smoke'|Set-Content $state;exit 75}
 ('RUNNING phase=smoke runnerPid='+$PID)|Set-Content $state
 $render=Join-Path $payload 'render'
 Set-Location -LiteralPath $render
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $render 'FileCat.Rendering.Tests.dll') -method '*ComparisonMetricWaitsActualReadersAndRecomputesAllCapturedMetrics' -method '*ComparisonChartSeparatesGrowthFromAreaAndKeepsRemovedOnlyInPrevious' -method '*ComparisonChartAndMetricControlsRenderAndSwitchAfterSourceClosure' -noLogo -noColor -maxThreads 2 -xml (Join-Path $base 'smoke-r828.xml') *> (Join-Path $base 'smoke-r828.log')
 $smokeResult=$LASTEXITCODE
 $ErrorActionPreference='Stop'
 if($smokeResult){'FINISHED exit=1 phase=smoke'|Set-Content $state;exit 1}
'@
$body=$body.Replace(' $overall=0', $smoke+"`n"+' $overall=0')
[IO.File]::WriteAllText($path,$body.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
