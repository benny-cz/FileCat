param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$BaselineRevision,
 [Parameter(Mandatory=$true)][string]$BuildLog,
 [Parameter(Mandatory=$true)][string]$SourceManifest,
 [switch]$OldProductNegativeControl
)
$ErrorActionPreference='Stop'
$scope=if($OldProductNegativeControl){'OldProductControl'}else{'AppUi'}
# Product UI deltas require appropriate affected broad regressions. They are not
# test-only qualification. The negative-control mode deliberately selects the
# baseline App DLL/PDB and current test DLL/PDB, and records that distinction.
& (Join-Path $PSScriptRoot 'prepare-test-fix-batch.ps1') -Revision $Revision -BaselineRevision $BaselineRevision -Phase render `
 -BuildLog $BuildLog -SourceManifest $SourceManifest -ReusePlatforms @('windows','mac','linux') -InputScope $scope
Copy-Item -LiteralPath ('.tmp/test-fix-proof-'+$Revision+'.json') -Destination ('.tmp/app-ui-delta-proof-'+$Revision+'.json')
