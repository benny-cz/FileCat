param([Parameter(Mandatory)][string]$ReportPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$validationRoot = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$build = Get-Content -LiteralPath (Join-Path $validationRoot 'vm-clone-build.json') -Raw | ConvertFrom-Json
$app = [IO.Path]::GetFullPath($build.exe)
$native = Join-Path $build.checkout 'tests\FileCat.NativeAcceptance\bin\Release\net10.0\FileCat.NativeAcceptance.exe'
$safeRoot = [IO.Path]::GetFullPath($validationRoot).TrimEnd('\') + '\'
if (!$build.passed -or !$app.StartsWith($safeRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $native) -or
    $build.sha256 -ne (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash) {
    throw 'The exact Git-built native validation app is unavailable.'
}

function Elements($window) {
    @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}

function Button($window, [string]$name) {
    @(Elements $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name.StartsWith($name, [StringComparison]::Ordinal)
    }) | Select-Object -First 1
}

function WaitButton($window, [string]$name) {
    for ($i = 0; $i -lt 80; $i++) {
        $found = Button $window $name
        if ($null -ne $found) { return $found }
        Start-Sleep -Milliseconds 150
    }
    $sample = @(Elements $window | ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -First 60)
    throw "The expected native button was unavailable: $name; visible names: $($sample -join ' | ')"
}

function InvokeButton($button) {
    $pattern = $null
    if (!$button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        throw "The button has no UI Automation Invoke pattern: $($button.Current.Name)"
    }
    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
}

$process = $null
$fixturePath = [IO.Path]::ChangeExtension($ReportPath, 'fixture.json')
try {
    $env:FILECAT_NATIVE_TEST_APP = $app
    & $native seed-remote-destination $fixturePath
    if ($LASTEXITCODE -ne 0) { throw 'The owned remote recovery fixture could not be seeded.' }
    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    $fixtureRoot = [IO.Path]::GetFullPath($fixture.root)
    $remoteRoot = [IO.Path]::GetFullPath((Join-Path $validationRoot 'RemoteRecovery')).TrimEnd('\') + '\'
    if (!$fixtureRoot.StartsWith($remoteRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $fixture.app -ne $app -or $fixture.remoteSource) { throw 'The fixture escaped the isolated validation root.' }
    $prefix = '.fc-' + [IO.Path]::GetFileNameWithoutExtension($fixture.journal).Substring(
        [IO.Path]::GetFileNameWithoutExtension($fixture.journal).Length - 8) + '-'
    $sourceHash = (Get-FileHash -LiteralPath $fixture.localFile -Algorithm SHA256).Hash

    # This is an interactive VM-only accessibility check: the window must be visible.
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $fixture.data,
        '--left', $fixture.source, '--right', $fixture.destination) -WindowStyle Normal -PassThru
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 150; $process.Refresh() }
    while ($process.MainWindowHandle -eq [IntPtr]::Zero -and !$process.HasExited -and $deadline.Elapsed.TotalSeconds -lt 20)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero -or
        [IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'The exact isolated FileCat window did not open.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $action = WaitButton $window 'Review local cleanup'
    $rowNames = @(Elements $window | ForEach-Object { $_.Current.Name })
    $rowHint = ($rowNames -join "`n").IndexOf($prefix, [StringComparison]::Ordinal) -ge 0
    InvokeButton $action
    $approve = WaitButton $window 'Keep remote upload for review'
    $dialogNames = @(Elements $window | ForEach-Object { $_.Current.Name })
    $dialogText = $dialogNames -join "`n"
    $dialogHint = $dialogText.IndexOf('Candidate temporary names begin: ' + $prefix, [StringComparison]::Ordinal) -ge 0
    $warning = $dialogText.IndexOf('server files are not inspected or deleted', [StringComparison]::Ordinal) -ge 0
    InvokeButton $approve

    $reviewed = Join-Path $fixture.data 'local\journal\reviewed'
    $saved = $null
    for ($i = 0; $i -lt 80 -and $null -eq $saved; $i++) {
        if (Test-Path -LiteralPath $reviewed) {
            $saved = Get-ChildItem -LiteralPath $reviewed -Filter 'review-*.json' | Where-Object {
                (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).JournalPath -eq $fixture.journal
            } | Select-Object -First 1
        }
        if ($null -eq $saved) { Start-Sleep -Milliseconds 150 }
    }
    if ($null -eq $saved) { throw 'The retained recovery report was not saved.' }
    $review = Get-Content -LiteralPath $saved.FullName -Raw | ConvertFrom-Json
    $reportHint = $review.Report.IndexOf($prefix, [StringComparison]::Ordinal) -ge 0
    $journalOpen = $review.ClosureConfirmed -eq $false -and
        (Test-Path -LiteralPath $fixture.journal) -and
        -not (Select-String -LiteralPath $fixture.journal -SimpleMatch '"t":"end"' -Quiet)
    $sourceIntact = $sourceHash -eq (Get-FileHash -LiteralPath $fixture.localFile -Algorithm SHA256).Hash
    $destinationEmpty = @(Get-ChildItem -LiteralPath $fixture.destination).Count -eq 0
    $passed = $rowHint -and $dialogHint -and $warning -and $reportHint -and
        $journalOpen -and $sourceIntact -and $destinationEmpty
    [pscustomobject]@{
        passed = $passed; head = $build.head; appSha256 = $build.sha256; fixture = $fixtureRoot
        prefix = $prefix; rowHint = $rowHint; dialogHint = $dialogHint; warning = $warning
        reportHint = $reportHint; journalOpen = $journalOpen; sourceIntact = $sourceIntact
        destinationEmpty = $destinationEmpty; sourceSha256 = $sourceHash
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    if (!$passed) { throw 'One or more native remote recovery review checks failed.' }
} catch {
    $_ | Out-String | Set-Content -LiteralPath ($ReportPath + '.error.txt') -Encoding UTF8
    throw
} finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:FILECAT_NATIVE_TEST_APP -ErrorAction SilentlyContinue
}
