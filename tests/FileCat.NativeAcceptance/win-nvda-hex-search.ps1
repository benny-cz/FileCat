param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$RunnerPath,
    [Parameter(Mandatory)][string]$NvdaPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Run in the interactive Windows VM session. The native Hex journey owns the
# fixture and checks offsets, keyboard navigation and unchanged source bytes.
# The viewer Stop journey then checks a real 2 GiB search cancellation; this
# wrapper requires NVDA to speak the match counts and the canceled outcome.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $root + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$runner = [IO.Path]::GetFullPath($RunnerPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$reportFile = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf)) {
    throw 'Use an isolated Git-built FileCat.exe below FileCatValidation.'
}
if (!$runner.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($runner) -ine 'FileCat.NativeAcceptance.exe' -or
    !(Test-Path -LiteralPath $runner -PathType Leaf)) {
    throw 'Use the matching isolated native acceptance runner.'
}
if (!$nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid') {
    throw 'Use the signed portable NVDA copy below FileCatValidation.'
}
if (!$reportFile.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $reportFile) -or
    !(Test-Path -LiteralPath (Split-Path $reportFile) -PathType Container)) {
    throw 'Use a fresh report file in FileCatValidation.'
}
if (Get-Process -Name nvda -ErrorAction SilentlyContinue) {
    throw 'Another NVDA instance is running; refusing to share its speech log.'
}
$otherApps = @(Get-CimInstance Win32_Process -Filter "Name = 'FileCat.exe'" -ErrorAction Stop)
if ($otherApps.Count) { throw 'Another FileCat instance is running; refusing to share NVDA speech.' }

$fixture = Join-Path $root ('NvdaHexSearch\' + [guid]::NewGuid().ToString('N'))
$config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $config | Out-Null
$seedReport = Join-Path $fixture 'selection-fixture.json'
$hexReport = Join-Path $fixture 'hex-search.json'
$viewerStopReport = Join-Path $fixture 'viewer-stop.json'
$stopReport = Join-Path $fixture 'selection-stop.json'
$log = Join-Path $fixture 'nvda.log'
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$result = [ordered]@{
    passed = $false
    fixture = $fixture
    appDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    nvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion
    nativeReport = $hexReport
    viewerStopReport = $viewerStopReport
    speechLog = $log
    expectedSpeech = @('1 distinct match visited', '2 distinct matches visited',
        '1 distinct match visited', 'Search canceled.')
}
$nvdaProcess = $null
$appProcess = $null
$seeded = $false
$nativeExit = $null
try {
    $nvdaProcess = Start-Process -FilePath $nvda -ArgumentList @("--log-file=$log", '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while (!((Test-Path -LiteralPath $log) -and
        (Select-String -LiteralPath $log -Pattern 'NVDA initialized' -SimpleMatch -Quiet))) {
        if ($deadline.Elapsed.TotalSeconds -gt 30 -or $nvdaProcess.HasExited) {
            throw 'NVDA did not initialize within 30 seconds.'
        }
        Start-Sleep -Milliseconds 300
    }
    & $runner seed-selection $seedReport | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Selection fixture failed: $LASTEXITCODE" }
    $seeded = $true
    $paths = Get-Content -LiteralPath $seedReport -Raw | ConvertFrom-Json
    $appProcess = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $paths.data, '--left', $paths.source, '--right', $paths.destination)
    $env:FILECAT_NATIVE_TEST_APP = $app
    Start-Sleep -Seconds 4
    & $runner hex-editor-search-native $hexReport | Out-Null
    $nativeExit = $LASTEXITCODE
    if (!(Test-Path -LiteralPath $hexReport -PathType Leaf)) {
        throw "The native Hex runner did not write its report (exit $nativeExit)."
    }
    $native = Get-Content -LiteralPath $hexReport -Raw | ConvertFrom-Json
    $result.nativePassed = $native.passed -and $nativeExit -eq 0
    $result.nativeFirstPointerFocused = $native.firstPointerFocused
    $result.nativeSourceIntact = $native.sourceIntact
    if (!$result.nativePassed) { throw "The native Hex journey failed (exit $nativeExit)." }

    & $runner search-stop-native $viewerStopReport | Out-Null
    $viewerStopExit = $LASTEXITCODE
    if (!(Test-Path -LiteralPath $viewerStopReport -PathType Leaf)) {
        throw "The native viewer Stop runner did not write its report (exit $viewerStopExit)."
    }
    $viewerStop = Get-Content -LiteralPath $viewerStopReport -Raw | ConvertFrom-Json
    $result.viewerStopPassed = $viewerStop.passed -and $viewerStopExit -eq 0 -and
        $viewerStop.canceled -and $viewerStop.stopHidden -and $viewerStop.sourceIntact -and
        $viewerStop.clipboardPreserved
    if (!$result.viewerStopPassed) { throw "The native viewer Stop journey failed (exit $viewerStopExit)." }

    # NVDA logs the actual phrases passed to its speech synthesizer. Search
    # only those records, in order, so UIA names or FileCat log messages alone
    # cannot satisfy this accessibility check.
    Start-Sleep -Seconds 2
    $speech = @(Get-Content -LiteralPath $log -Encoding UTF8 |
        Where-Object { $_.StartsWith('Speaking [') })
    $spokenHits = @()
    $cursor = 0
    foreach ($expected in $result.expectedSpeech) {
        $pattern = '(?<!\d)' + [regex]::Escape($expected) + '(?!\d)'
        $found = -1
        for ($i = $cursor; $i -lt $speech.Count; $i++) {
            if ([regex]::IsMatch($speech[$i].ToString(), $pattern)) { $found = $i; break }
        }
        if ($found -lt 0) { throw "NVDA did not speak '$expected' in order." }
        $spokenHits += [ordered]@{ expected = $expected; logIndex = $found;
            spoken = $speech[$found].ToString() }
        $cursor = $found + 1
    }
    $result.spokenMatches = $spokenHits
    $result.passed = $true
} catch {
    $result.error = $_.Exception.Message
} finally {
    if ($seeded) {
        & $runner selection-stop $stopReport | Out-Null
        $result.stopExit = $LASTEXITCODE
    }
    if ($appProcess -and !$appProcess.HasExited) {
        $appProcess.CloseMainWindow() | Out-Null
        if (!$appProcess.WaitForExit(10000)) { $appProcess.Kill() }
    }
    if ($nvdaProcess -and !$nvdaProcess.HasExited) {
        Start-Process -FilePath $nvda -ArgumentList @('-q') -Wait | Out-Null
        if (!$nvdaProcess.WaitForExit(15000)) { $nvdaProcess.Kill() }
    }
    if ($result.stopExit -ne 0) { $result.passed = $false }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportFile -Encoding UTF8
}
if (!$result.passed) {
    if ($result.error) { throw $result.error }
    throw 'NVDA Hex acceptance did not pass.'
}
