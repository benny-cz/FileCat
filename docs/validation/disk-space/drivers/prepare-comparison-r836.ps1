$ErrorActionPreference='Stop'
$proof=@()
foreach($pair in @(@('.tmp/core-r828/FileCat.Core.dll','tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.dll'),@('.tmp/native-r828/FileCat.Platform.Windows.dll','tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0/FileCat.Platform.Windows.dll'))){
 $old=(Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash.ToLowerInvariant();$current=(Get-FileHash -LiteralPath $pair[1] -Algorithm SHA256).Hash.ToLowerInvariant()
 if($old-ne$current){throw ('A broad Core/native rerun is required: '+$pair[1])}
 $proof+=@{baseline=$pair[0];current=$pair[1];sha256=$current}
}
$proof|ConvertTo-Json -Depth 4|Set-Content .tmp/unchanged-check-r836.json
$paths=@(git diff --name-only -- src tests)+@(git ls-files --others --exclude-standard -- src tests)
$sources=@($paths|Sort-Object -Unique|ForEach-Object{[ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
$sources|ConvertTo-Json -Depth 4|Set-Content .tmp/source-candidate-r836.json
& docs/validation/disk-space/drivers/pack-batch-direct.ps1 -Revision r836 -RenderRevision r837 -BuildLog .tmp/build-r835.txt -SourceManifest .tmp/source-candidate-r836.json
if($LASTEXITCODE){throw 'Candidate packing failed'}
$body=[IO.File]::ReadAllText((Join-Path $PWD '.tmp/batch-windows-r828.ps1')).Replace('r828','r836')
$oldHash=(Get-FileHash -LiteralPath .tmp/dsa-batch-windows-r828.tgz -Algorithm SHA256).Hash.ToLowerInvariant();$newHash=(Get-FileHash -LiteralPath .tmp/dsa-batch-windows-r836.tgz -Algorithm SHA256).Hash.ToLowerInvariant()
$body=$body.Replace($oldHash,$newHash).Replace("foreach(`$phase in 'core','native','render')", "foreach(`$phase in 'core','render')")
$body=$body.Replace("@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests')", "@('-method','*ActualReadersKeepAdmissionUntilTheLastReleaseAndSignalCompletion','-method','*OrdinarySavedViewsRejectGrowthColorWithoutComparisonContext')")
$body=$body.Replace(' -noLogo -noColor -maxThreads 2 -xml', " -method '*MissingAutomaticRootsRequireExplicitMappingAndDefaultToCancel' -method '*ComparisonQueryPreservesLastValidRowsAndExposesUncertainMatches' -method '*ComparisonPaneFiltersGrowthInspectsBothSidesAndKeepsItsCloseDefault' -noLogo -noColor -maxThreads 2 -xml")
[IO.File]::WriteAllText((Join-Path $PWD '.tmp/batch-windows-r836.ps1'),$body,[Text.UTF8Encoding]::new($false))
foreach($platform in 'mac','linux'){
 $path=Join-Path $PWD ('.tmp/batch-'+$platform+'-r836.sh');$runner=[IO.File]::ReadAllText($path)
 $runner=$runner.Replace("-class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests'", "-method '*ActualReadersKeepAdmissionUntilTheLastReleaseAndSignalCompletion' -method '*OrdinarySavedViewsRejectGrowthColorWithoutComparisonContext'")
 [IO.File]::WriteAllText($path,$runner,[Text.UTF8Encoding]::new($false))
}
