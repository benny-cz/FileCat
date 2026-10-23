param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Run only inside the interactive Windows validation VM. All mutation is confined
# to a fresh fixture below FileCatValidation; only this script's app is closed.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatUndoWindowSize {
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle);
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$prefix = $validation.TrimEnd('\') + '\'
if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    !(Test-Path -LiteralPath $app -PathType Leaf)) {
    throw 'Use a Git-built FileCat.exe and a report inside the isolated FileCatValidation directory.'
}

$root = Join-Path $validation ('UndoMixed-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'target'
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$count = 42
$expected = @{}
for ($i = 0; $i -lt $count; $i++) {
    $name = 'item-{0:D2}.txt' -f $i
    $content = "native inverse $i`n" + ('x' * (10 + $i))
    [IO.File]::WriteAllText((Join-Path $source $name), $content)
    $expected[$name] = $content
}
$lockedName = 'item-35.txt'
$changedName = 'item-36.txt'
$changedContent = "changed after original move`n"
$process = $null
$lock = $null
$result = [ordered]@{ Passed = $false; Fixture = $root; App = $app;
    AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    OutcomeLines = 0; FailurePath = $null; KeptPath = $null;
    FinalPath = $null; Dpi = 0; NarrowReview = $null; NarrowReport = $null;
    Error = $null }

function Snapshot($window) {
    for ($retry = 0; $retry -lt 4; $retry++) {
        try { return @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)) }
        catch [Runtime.InteropServices.COMException] {
            if ($retry -eq 3) { throw }
            Start-Sleep -Milliseconds 150
        }
    }
}
function Await([scriptblock]$condition, [string]$description, [int]$seconds = 30) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 150
    } while ($clock.Elapsed.TotalSeconds -lt $seconds)
    throw "Timed out waiting for $description."
}
function Button($window, [string]$name) {
    return @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -eq $name -and $_.Current.IsEnabled -and !$_.Current.IsOffscreen
    } | Sort-Object { $_.Current.BoundingRectangle.Y })[0]
}
function ClickButton($window, [string]$name) {
    $button = Await { Button $window $name } "enabled $name button"
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function TextContains($window, [string]$fragment) {
    return @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.Contains($fragment)
    }).Count -gt 0
}
function ExactFiles($folder, [int]$expectedCount) {
    return @(Get-ChildItem -LiteralPath $folder -File).Count -eq $expectedCount
}
function Foreground($process) {
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    Start-Sleep -Milliseconds 200
}

try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    Await {
        $process.Refresh()
        !$process.HasExited -and $process.MainWindowHandle -ne [IntPtr]::Zero
    } 'isolated main window' | Out-Null
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'Unexpected executable owns the FileCat window.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $row = Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq 'item-00.txt'
    })[0] } 'first source row'
    $row.SetFocus()
    Foreground $process
    (New-Object -ComObject WScript.Shell).SendKeys('^a')
    Await { TextContains $window 'Marked 42 of 42 files' } 'all 42 source items marked' | Out-Null
    ClickButton $window 'Move'
    Await { TextContains $window '42' -and (Button $window 'Move') } '42-item Move review' | Out-Null
    ClickButton $window 'Move'
    Await { (ExactFiles $target $count) -and (ExactFiles $source 0) } 'completed 42-item Move' 90 | Out-Null
    foreach ($name in $expected.Keys) {
        if ([IO.File]::ReadAllText((Join-Path $target $name)) -cne $expected[$name]) {
            throw "Original Move changed $name bytes."
        }
    }

    $lock = [IO.File]::Open((Join-Path $target $lockedName), [IO.FileMode]::Open,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    [IO.File]::WriteAllText((Join-Path $target $changedName), $changedContent)
    $handle = [IntPtr]$process.MainWindowHandle
    $result.Dpi = [FileCatUndoWindowSize]::GetDpiForWindow($handle)
    $scale = $result.Dpi / 96.0
    if (![FileCatUndoWindowSize]::MoveWindow($handle, 80, 80,
            [int][Math]::Round(720 * $scale), [int][Math]::Round(540 * $scale), $true)) {
        throw 'Could not resize the owned FileCat window for the narrow Undo review.'
    }
    Foreground $process
    (New-Object -ComObject WScript.Shell).SendKeys('^z')
    Await { TextContains $window '42 recorded inverse actions' -and
        (Button $window 'Run these inverse actions') } 'complete Undo review' | Out-Null
    if (!(TextContains $window (Join-Path $source 'item-41.txt'))) {
        throw 'Undo review omitted the full last affected path.'
    }
    $frame = $window.Current.BoundingRectangle
    $firstAction = @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.Contains((Join-Path $source 'item-41.txt')) -and
        !$_.Current.IsOffscreen
    })[0]
    $approval = Button $window 'Run these inverse actions'
    $cancel = Button $window 'Cancel'
    if (!$firstAction -or !$approval -or !$cancel) {
        throw 'The narrow Undo review hid the full first action or its decision controls.'
    }
    $actionBounds = $firstAction.Current.BoundingRectangle
    $approveBounds = $approval.Current.BoundingRectangle
    $cancelBounds = $cancel.Current.BoundingRectangle
    $result.NarrowReview = [ordered]@{
        Frame = "$frame"; Action = "$actionBounds";
        Approve = "$approveBounds"; Cancel = "$cancelBounds"
    }
    if ($frame.Width -gt (730 * $scale) -or
        $actionBounds.Height -le (30 * $scale) -or
        $actionBounds.Left -lt $frame.Left -or $actionBounds.Right -gt $frame.Right -or
        $approveBounds.Right -gt $frame.Right -or $approveBounds.Bottom -gt $frame.Bottom -or
        $cancelBounds.Left -lt $frame.Left -or $cancelBounds.Bottom -gt $frame.Bottom) {
        throw 'The narrow Undo review did not wrap its full path or keep the decisions inside the window.'
    }
    ClickButton $window 'Run these inverse actions'
    Await { (ExactFiles $source ($count - 2)) -and (ExactFiles $target 2) } 'mixed inverse result' 90 | Out-Null
    $lock.Dispose(); $lock = $null
    foreach ($name in $expected.Keys) {
        $atSource = Join-Path $source $name
        $atTarget = Join-Path $target $name
        if ($name -in @($lockedName, $changedName)) {
            if (Test-Path -LiteralPath $atSource) { throw "Unsafe restoration of $name." }
            $wanted = if ($name -eq $changedName) { $changedContent } else { $expected[$name] }
            if ([IO.File]::ReadAllText($atTarget) -cne $wanted) { throw "Kept $name bytes changed." }
        } elseif ([IO.File]::ReadAllText($atSource) -cne $expected[$name] -or
            (Test-Path -LiteralPath $atTarget)) { throw "Restored $name is wrong." }
    }

    ClickButton $window 'Undo report'
    $reportWindow = Await { @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ProcessId -eq $process.Id -and $_.Current.Name -like '*Undo report*'
        })[0] } 'retained Undo report window'
    $reportHandle = [IntPtr]$reportWindow.Current.NativeWindowHandle
    if (![FileCatUndoWindowSize]::MoveWindow($reportHandle, 100, 100,
            [int][Math]::Round(600 * $scale), [int][Math]::Round(460 * $scale), $true)) {
        throw 'Could not resize the owned Undo report window.'
    }
    Start-Sleep -Milliseconds 300
    $reportFrame = $reportWindow.Current.BoundingRectangle
    $copyButton = Await { Button $reportWindow 'Copy all' } 'narrow report Copy all'
    $copyBounds = $copyButton.Current.BoundingRectangle
    $result.NarrowReport = [ordered]@{ Frame = "$reportFrame"; CopyAll = "$copyBounds" }
    if ($reportFrame.Width -gt (610 * $scale) -or
        $copyBounds.Left -lt $reportFrame.Left -or $copyBounds.Right -gt $reportFrame.Right -or
        $copyBounds.Top -lt $reportFrame.Top -or $copyBounds.Bottom -gt $reportFrame.Bottom) {
        throw 'The narrow Undo report hid Copy all or did not accept the requested size.'
    }
    ClickButton $reportWindow 'Copy all'
    $copy = Await {
        $value = Get-Clipboard -Raw
        if ($value -like '*Undo:*' -and $value -like '*item-41.txt*') { $value }
    } 'complete clipboard report'
    $lines = @($copy -split "`r?`n" | Where-Object {
        $_ -match '^(Restored|Failed|Not undone|Kept|Not attempted):? '
    })
    if ($lines.Count -ne $count -or $copy -notmatch '1 failed' -or
        $copy -notmatch '1 kept' -or $copy -notmatch '40 restored') {
        throw "The retained report lacks 42 mixed outcomes: $($lines.Count) lines."
    }
    $failurePath = Join-Path $target $lockedName
    $keptPath = Join-Path $target $changedName
    $finalPath = Join-Path $source 'item-00.txt'
    foreach ($path in @($failurePath, $keptPath, $finalPath)) {
        if (!$copy.Contains($path)) { throw "The report omitted whole path $path." }
    }
    if (@($lines[30..($count - 1)] | Where-Object { !$_.Contains($root) }).Count -ne 0) {
        throw 'One of the report outcomes beyond row 30 lacks its complete fixture path.'
    }
    if ($lines[0] -notlike "*$failurePath*" -or $lines[1] -notlike "*$keptPath*") {
        throw 'The report does not put failed and kept outcomes ahead of successes.'
    }
    $result.OutcomeLines = $lines.Count
    $result.FailurePath = $failurePath
    $result.KeptPath = $keptPath
    $result.FinalPath = $finalPath
    $result.Passed = $true
}
catch { $result.Error = $_ | Out-String }
finally {
    if ($lock) { $lock.Dispose() }
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        $process.WaitForExit(5000) | Out-Null
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
