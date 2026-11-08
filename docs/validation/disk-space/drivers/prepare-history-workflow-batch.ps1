param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][string]$BuildLog)
$ErrorActionPreference='Stop'
$render='tests/FileCat.Rendering.Tests/bin/Debug/net10.0';$app='tests/FileCat.App.Tests/bin/Debug/net10.0';$compared=0
foreach($file in (Get-ChildItem -LiteralPath $app -File -Filter '*.dll'|Where-Object Name -ne 'FileCat.App.Tests.dll')){
 $other=Join-Path $render $file.Name
 if(Test-Path -LiteralPath $other){
  if((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash-ne(Get-FileHash -LiteralPath $other -Algorithm SHA256).Hash){throw ('Shared product differs: '+$file.Name)}
  $compared++
 }
}
$names=@('FileCat.App.Tests.dll','FileCat.App.Tests.pdb','FileCat.App.Tests.deps.json','FileCat.App.Tests.runtimeconfig.json')
foreach($name in $names){Copy-Item -LiteralPath (Join-Path $app $name) -Destination (Join-Path $render $name)}
[ordered]@{sharedDllsVerified=$compared;appTestFiles=@($names|ForEach-Object{[ordered]@{name=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $render $_) -Algorithm SHA256).Hash.ToLowerInvariant()}})}|ConvertTo-Json -Depth 5|Set-Content ('.tmp/app-settings-inputs-'+$Revision+'.json')
$paths=@(git diff --name-only -- src tests)+@(git ls-files --others --exclude-standard -- src tests)
$manifest='.tmp/source-candidate-'+$Revision+'.json'
@($paths|Sort-Object -Unique|ForEach-Object{[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})|ConvertTo-Json -Depth 4|Set-Content $manifest
& (Join-Path $PSScriptRoot 'pack-batch-direct.ps1') -Revision $Revision -RenderRevision $Revision -BuildLog $BuildLog -SourceManifest $manifest
$core=@('*AnalysisHistoryTests.*','*AnalysisCatalogTests.CatalogMemoryRemainsReservedUntilResultOwnersClose')
$renderMethods=@('*AnalysisRenderingTests.NativeRolling*','*AnalysisRenderingTests.NativeNamedOwnerCannotBecomeAutomaticHistoryAndNonHistoryFilesArePreserved',
 '*AnalysisRenderingTests.RollingHistoryPolicyReviewPreservesTheCurrentChoiceAndNamesOnlyItsPrivateScope','*AnalysisRenderingTests.ActualDiagnosticsZipIncludesOnlyFiniteAnalysisPreferencesAndPreservesPrivateSentinels',
 '*AnalysisRenderingTests.NativeHistoryCompletion*','*AnalysisRenderingTests.NativeHistoryOff*','*AnalysisRenderingTests.StoppedAndUnconfirmedSourceCloseSessions*',
 '*AnalysisRenderingTests.SettingsHistoryPolicyRequires*','*AnalysisRenderingTests.HistorySettingsKeepExact*','*AnalysisRenderingTests.HistoryPolicyChangedDuringReview*',
 '*AnalysisRenderingTests.NativeHistoryView*','*AnalysisRenderingTests.NativeHistoryRemoval*','*AnalysisRenderingTests.NativeUnindexedHistoryRecovery*',
 '*AnalysisRenderingTests.NativeHistoryRecoveryRefuses*','*AnalysisRenderingTests.HistoryBrowserRemoval*','*AnalysisRenderingTests.NativeHistoryPinReplacement*',
 '*AnalysisRenderingTests.NativeHistoryRecoveryGraphAttestation*','*AnalysisRenderingTests.NativePendingHistory*')
& (Join-Path $PSScriptRoot 'add-windows-previews.ps1') -Revision $Revision -CoreMethods $core -CoreCases 27 -RenderMethods $renderMethods -RenderCases 38
foreach($platform in 'mac','linux'){& (Join-Path $PSScriptRoot 'add-unix-previews.ps1') -Platform $platform -Revision $Revision -CoreMethods $core -CoreCases 27 -RenderMethods $renderMethods -RenderCases 38}
& (Join-Path $PSScriptRoot 'add-app-settings-suite.ps1') -Revision $Revision
