# Explicit native qualification only. Run on an available disposable Windows guest, after building both projects.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProbeAssets,
    [Parameter(Mandatory = $true)][string]$VSTestConsole,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$taskCheckout = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
foreach ($taskPath in @($ProbeAssets, $VSTestConsole, $EvidenceDirectory)) {
    if (-not [IO.Path]::IsPathRooted($taskPath)) { throw 'Supply absolute asset, runner and evidence paths.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $ProbeAssets 'coreclr.dll') -PathType Leaf)) {
    throw 'Supply a dedicated self-contained FileCat.WorkerProbe bundle; this lane grants its package read access.'
}
if (-not (Test-Path -LiteralPath $VSTestConsole -PathType Leaf)) { throw 'The chosen SDK VSTest console does not exist.' }
$taskOtherInstances = @(Get-Process -Name 'FileCat', 'FileCat.App', 'FileCat.WorkerProbe' -ErrorAction SilentlyContinue)
if ($taskOtherInstances.Count -ne 0) { throw 'Postpone validation: another FileCat or worker instance is running.' }
$taskHarness = Join-Path $taskCheckout "tests\FileCat.CoreCrashHarness\bin\$Configuration\net10.0\FileCat.CoreCrashHarness.dll"
$taskTests = Join-Path $taskCheckout "tests\FileCat.Platform.Windows.Tests\bin\$Configuration\net10.0\FileCat.Platform.Windows.Tests.dll"
foreach ($taskBinary in @($taskHarness, $taskTests)) {
    if (-not (Test-Path -LiteralPath $taskBinary -PathType Leaf)) { throw "Build the native tests and CoreCrashHarness first: $taskBinary" }
}
$taskDotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$taskRun = 'containment-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
$taskPrefix = Join-Path $EvidenceDirectory $taskRun
$taskPreviousOptIn = $env:FILECAT_ARCHIVE_CONTAINMENT
$taskPreviousAssets = $env:FILECAT_ARCHIVE_PROBE_ASSETS
try {
    $env:FILECAT_ARCHIVE_CONTAINMENT = '1'
    $env:FILECAT_ARCHIVE_PROBE_ASSETS = $ProbeAssets
    $taskFilter = 'FullyQualifiedName~ArchiveAppContainerTests|FullyQualifiedName~RestrictedProcessLowTokenTests|FullyQualifiedName~RestrictedProcessFallbackTests|FullyQualifiedName~SandboxedWorkerEnvironmentTests'
    & $taskDotnet $taskHarness --archive-test-medium $taskPrefix $taskDotnet exec $VSTestConsole $taskTests "/TestCaseFilter:$taskFilter" "/Logger:trx;LogFileName=$taskRun.trx" "/ResultsDirectory:$EvidenceDirectory"
    $taskExit = $LASTEXITCODE
    $taskFacts = Get-Content -LiteralPath ($taskPrefix + '.facts.json') -Raw | ConvertFrom-Json
    if ($taskFacts.after -ne 8192) { throw 'The actual test parent did not establish medium integrity.' }
    Get-Content -LiteralPath ($taskPrefix + '.out.log')
    Write-Output "Evidence: $taskPrefix"
    if ($taskExit -ne 0) { throw "Native archive qualification failed (exit $taskExit). Inspect the facts, stdout, stderr and TRX." }
}
finally {
    $env:FILECAT_ARCHIVE_CONTAINMENT = $taskPreviousOptIn
    $env:FILECAT_ARCHIVE_PROBE_ASSETS = $taskPreviousAssets
}
