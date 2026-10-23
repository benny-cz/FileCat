param([Parameter(Mandatory = $true)][string]$DotnetPath)

# Run in the interactive Windows guest from a built Git checkout. The sole killed
# process is the harness started below; the fixture lives under FileCatValidation.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$checkout = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$validation = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$root = Join-Path $validation ('UndoCrash-' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $root 'data'
$items = Join-Path $root 'items'
$target = Join-Path $root 'target'
$marker = Join-Path $root 'inverse-held.json'
$journalDir = Join-Path $data 'local\journal'
$harness = Join-Path $checkout 'tests\FileCat.CoreCrashHarness\bin\Release\net10.0\FileCat.CoreCrashHarness.dll'
$app = Join-Path $checkout 'src\FileCat.App\bin\Release\net10.0\FileCat.exe'
$resultPath = Join-Path $validation 'win-undo-last.json'
$child = $null
$appProcess = $null
$result = [ordered]@{ passed = $false; root = $root; checkout = $checkout; appSha256 = $null; head = $null; appPid = $null; cancelKeptJournal = $false; approvedKeptJournal = $false; reviewedClosedJournal = $false; error = $null }

function Assert-Bytes([string]$name, [string]$expected) {
    $path = Join-Path $items $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or [IO.File]::ReadAllText($path) -cne $expected) {
        throw "Wrong contents at fixture item $name."
    }
}
function Assert-TemporaryCount([int]$expected) {
    $count = @(Get-ChildItem -LiteralPath $items -Filter '.~fcren-*' -File).Count
    if ($count -ne $expected) { throw "Expected $expected temporary names, found $count." }
}
function Open-UndoJournal {
    $journals = @(Get-ChildItem -LiteralPath $journalDir -Filter 'job-*.fcj' -File)
    return @($journals | Where-Object {
        $body = Get-Content -LiteralPath $_.FullName -Raw
        $body -match '"undoOf"' -and $body -notmatch '"t":"end"'
    })
}
function Elements {
    $appProcess.Refresh()
    if ($appProcess.HasExited -or $appProcess.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Git-built FileCat main window unavailable.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
    return @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
}
function Wait-Button([string]$name, [bool]$prefix = $false) {
    for ($i = 0; $i -lt 150; $i++) {
        $found = @(Elements | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $_.Current.IsEnabled -and
            ($(if ($prefix) { $_.Current.Name.StartsWith($name, [StringComparison]::Ordinal) } else { $_.Current.Name -ceq $name }))
        })
        if ($found.Count -eq 1) { return $found[0] }
        if ($found.Count -gt 1) { throw "Ambiguous button $name." }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for enabled button $name."
}
function Invoke-Button([string]$name, [bool]$prefix = $false) {
    $button = Wait-Button $name $prefix
    $pattern = $null
    if (-not $button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        throw "Button $name lacks Invoke pattern."
    }
    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
}
function Wait-Text([string]$fragment) {
    for ($i = 0; $i -lt 150; $i++) {
        if (@(Elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name.Contains($fragment) }).Count -gt 0) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for review text: $fragment."
}
function Assert-Review {
    Wait-Text 'Action: Finish renaming'
    $names = @(Elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name })
    foreach ($fragment in @('Reviewed interrupted renames to finish: 1', 'Recorded rename paths needing manual review: 2',
            'An item may be absent or inaccessible', 'No change will be made at these paths')) {
        if (@($names | Where-Object { $_.Contains($fragment) }).Count -eq 0) { throw "Review missing $fragment." }
    }
    foreach ($path in @((Join-Path $items 'b.txt'), (Join-Path $items 'a.txt'))) {
        if (@($names | Where-Object { $_.Contains($path) }).Count -eq 0) { throw "Review omitted whole path $path." }
    }
}

try {
    if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $harness -PathType Leaf) -or
        -not (Test-Path -LiteralPath $app -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $checkout '.git'))) { throw 'Git-built app, harness, SDK or checkout missing.' }
    New-Item -ItemType Directory -Path $root, $target -Force | Out-Null
    $result.appSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
    $git = 'C:\Program Files\Git\cmd\git.exe'
    if (Test-Path -LiteralPath $git) {
        $result.head = ((& $git -c "safe.directory=$checkout" -C $checkout rev-parse HEAD) | Select-Object -First 1).Trim()
    }
    $child = Start-Process -FilePath $DotnetPath -ArgumentList @($harness, $root, '--app-layout') -PassThru -WindowStyle Hidden
    for ($i = 0; $i -lt 150 -and -not (Test-Path -LiteralPath $marker) -and -not $child.HasExited; $i++) {
        Start-Sleep -Milliseconds 100
        $child.Refresh()
    }
    if (-not (Test-Path -LiteralPath $marker) -or $child.HasExited) { throw 'Dependent inverse did not reach the held rename.' }
    # On Windows, a renamed marker can become visible before its writer's
    # handle is released. Only read the completed marker after it is shareable.
    $markerDeadline = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try { $held = [IO.File]::ReadAllText($marker) | ConvertFrom-Json; break }
        catch [IO.IOException] {
            $child.Refresh()
            if ($markerDeadline.Elapsed -ge [TimeSpan]::FromSeconds(5) -or $child.HasExited) { throw }
            Start-Sleep -Milliseconds 25
        }
    }
    $prefix = [IO.Path]::GetFullPath($items) + [IO.Path]::DirectorySeparatorChar
    foreach ($path in @($held.source, $held.destination)) {
        if (-not [IO.Path]::GetFullPath($path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Crash marker escaped fixture.' }
    }
    Stop-Process -Id $child.Id -Force
    $child.WaitForExit(10000) | Out-Null
    if (-not $child.HasExited -or @(Open-UndoJournal).Count -ne 1) { throw 'Exact crash did not retain one open Undo journal.' }
    Assert-Bytes 'a.txt' 'C'; Assert-Bytes 'c.txt' 'B'; Assert-TemporaryCount 1
    $temp = @(Get-ChildItem -LiteralPath $items -Filter '.~fcren-*' -File)[0]
    if ([IO.File]::ReadAllText($temp.FullName) -cne 'A') { throw 'Held temporary bytes changed.' }

    $appProcess = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data, '--left', $items, '--right', $target) -PassThru
    $result.appPid = $appProcess.Id
    for ($i = 0; $i -lt 150 -and $appProcess.MainWindowHandle -eq [IntPtr]::Zero -and -not $appProcess.HasExited; $i++) {
        Start-Sleep -Milliseconds 100
        $appProcess.Refresh()
    }
    Invoke-Button 'Finish renaming' $true
    Assert-Review
    Invoke-Button 'Cancel'
    Assert-Bytes 'a.txt' 'C'; Assert-Bytes 'c.txt' 'B'; Assert-TemporaryCount 1
    if (@(Open-UndoJournal).Count -ne 1) { throw 'Cancel closed recovery tracking.' }
    $result.cancelKeptJournal = $true

    Invoke-Button 'Finish renaming' $true
    Assert-Review
    Invoke-Button 'Finish renaming'
    Wait-Text 'the new name'
    Wait-Text 'is taken, so it has its original name again'
    Invoke-Button 'OK'
    for ($i = 0; $i -lt 150 -and -not (Test-Path -LiteralPath (Join-Path $items 'b.txt')); $i++) { Start-Sleep -Milliseconds 100 }
    Assert-Bytes 'a.txt' 'C'; Assert-Bytes 'b.txt' 'A'; Assert-Bytes 'c.txt' 'B'; Assert-TemporaryCount 0
    Wait-Button 'Review retained recovery report for Undo:' $true | Out-Null
    if (@(Open-UndoJournal).Count -ne 1) { throw 'Unverified paths unexpectedly closed recovery tracking.' }
    $result.approvedKeptJournal = $true

    Invoke-Button 'Mark interrupted recovery reviewed for Undo:' $true
    Wait-Text 'File changes: none'
    Wait-Text 'Interrupted rename intents to inspect: 3'
    Invoke-Button 'Mark reviewed and stop reminders'
    for ($i = 0; $i -lt 150 -and @(Open-UndoJournal).Count -ne 0; $i++) { Start-Sleep -Milliseconds 100 }
    if (@(Open-UndoJournal).Count -ne 0) { throw 'Explicit reviewed decision did not close tracking.' }
    Assert-Bytes 'a.txt' 'C'; Assert-Bytes 'b.txt' 'A'; Assert-Bytes 'c.txt' 'B'; Assert-TemporaryCount 0
    $result.reviewedClosedJournal = $true
    $result.passed = $true
} catch {
    $result.error = $_.Exception.Message
} finally {
    if ($appProcess -and -not $appProcess.HasExited) {
        $appProcess.CloseMainWindow() | Out-Null
        $appProcess.WaitForExit(10000) | Out-Null
        if (-not $appProcess.HasExited) { Stop-Process -Id $appProcess.Id -Force }
    }
    if ($child -and -not $child.HasExited) { Stop-Process -Id $child.Id -Force }
    New-Item -ItemType Directory -Path $validation -Force | Out-Null
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
if (-not $result.passed) { exit 1 }
