param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateRange(100, 20000)][int]$FileCount = 5000,
    [ValidateSet('KeepCancelReport', 'StopExitRestart', 'ClearOriginalHistory')][string]$Action = 'KeepCancelReport'
)

# Interactive Windows VM only. The fixture, profile and report must stay below
# FileCatValidation. The runner closes only the FileCat instance it starts.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    !(Test-Path -LiteralPath $app -PathType Leaf)) {
    throw 'Use an isolated Git-built FileCat.exe and a guest-local report below FileCatValidation.'
}
$root = Join-Path $validation ('UndoClose-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'target'
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
for ($i = 0; $i -lt $FileCount; $i++) {
    [IO.File]::WriteAllText((Join-Path $source ('item-{0:D4}.txt' -f $i)), "owned inverse $i`n")
}
$process = $null
$result = [ordered]@{ Passed = $false; Fixture = $root; App = $app;
    AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    FileCount = $FileCount; Action = $Action; ClosePromptSeen = $false;
    Restarted = $false; Restored = 0; NotAttempted = 0; OutcomeLines = 0;
    ObservedText = @(); ObservedButtons = @(); Error = $null }

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
    (Await { Button $window $name } "enabled $name button").GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function TextContains($window, [string]$fragment) {
    return @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.Contains($fragment)
    }).Count -gt 0
}
function FolderCount([string]$folder) {
    return @(Get-ChildItem -LiteralPath $folder -File).Count
}
function Assert-SplitBytes {
    for ($i = 0; $i -lt $FileCount; $i++) {
        $name = 'item-{0:D4}.txt' -f $i
        $left = Join-Path $source $name
        $right = Join-Path $target $name
        $existsLeft = Test-Path -LiteralPath $left -PathType Leaf
        $existsRight = Test-Path -LiteralPath $right -PathType Leaf
        if ($existsLeft -eq $existsRight) { throw "Missing or duplicated $name after stopping Undo." }
        $content = [IO.File]::ReadAllText($(if ($existsLeft) { $left } else { $right }))
        if ($content -cne "owned inverse $i`n") { throw "Changed $name bytes after stopping Undo." }
    }
}
function Foreground($process) {
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    Start-Sleep -Milliseconds 150
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
        $_.Current.Name -eq 'item-0000.txt'
    })[0] } 'first source row' 90
    $row.SetFocus(); Foreground $process
    (New-Object -ComObject WScript.Shell).SendKeys('^a')
    $formattedCount = $FileCount.ToString('N0')
    Await { TextContains $window "Marked $formattedCount of $formattedCount files" } 'complete selection' 90 | Out-Null
    ClickButton $window 'Move'
    Await { TextContains $window $formattedCount -and (Button $window 'Move') } 'Move review' 90 | Out-Null
    ClickButton $window 'Move'
    Await { (FolderCount $target) -eq $FileCount -and (FolderCount $source) -eq 0 } 'original Move' 240 | Out-Null

    # The last destination file can appear before the job has committed its
    # final journal state. Wait for the actual Undo affordance, not file count.
    $undo = Await { Button $window 'Undo' } 'completed Move Undo action' 120
    $undo.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Await { TextContains $window "$formattedCount recorded inverse actions" } 'complete inverse review' 60 | Out-Null
    $approval = Await { Button $window 'Run these inverse actions' } 'inverse approval'
    $approval.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if ($Action -eq 'ClearOriginalHistory') {
        $clearLabel = 'Clear finished history' + [char]0x2026
        $result.DrawerOpenBefore = [bool](Button $window $clearLabel)
        if (!$result.DrawerOpenBefore) { ClickButton $window 'Details (Ctrl+J)' }
        $clear = Await { Button $window $clearLabel } 'Clear finished history during active inverse' 30
        if (!(TextContains $window 'Undo')) { throw 'Active inverse disappeared before history review.' }
        $clear.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Await { Button $window 'Keep history' } 'active-inverse history decision' 30 | Out-Null
        $result.ClearReviewActive = TextContains $window 'Undo attempt is being reviewed or run'
        if (!$result.ClearReviewActive) { throw 'History review did not disclose the admitted inverse.' }
        ClickButton $window 'Clear finished history'
        Await { !(Button $window 'Keep history') } 'approved history removal' 30 | Out-Null
        $result.ActiveInverseAfterClear = TextContains $window 'Undo'
        $result.OriginalRowRemoved = !(Button $window $clearLabel)
        Await { (FolderCount $source) -eq $FileCount -and
            (FolderCount $target) -eq 0 -and (Button $window 'Undo report') } 'exact inverse and retained report after original history removal' 240 | Out-Null
        Assert-SplitBytes
        $result.Restored = FolderCount $source
        $result.ReportAfter = [bool](Button $window 'Undo report')
        $result.Passed = $result.ClearReviewActive -and $result.ActiveInverseAfterClear -and
            $result.OriginalRowRemoved -and $result.ReportAfter -and
            $result.Restored -eq $FileCount -and (FolderCount $target) -eq 0
    }
    else {
    if (!$process.CloseMainWindow()) { throw 'The owned FileCat close request was refused.' }
    $keep = Await {
        if (TextContains $window 'Closing FileCat stops these operations') {
            Button $window 'Keep FileCat open'
        }
    } 'active-work close choice' 30
    $result.ClosePromptSeen = $true
    if ($process.HasExited) { throw 'FileCat exited while the inverse still owned active work.' }
    if ($Action -eq 'StopExitRestart') {
        $stop = Await { Button $window 'Stop them and exit' } 'owned stop-and-exit choice'
        $stop.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Await { $process.Refresh(); $process.HasExited } 'FileCat exit after the inverse stopped' 120 | Out-Null
        $leftCount = FolderCount $source
        $rightCount = FolderCount $target
        if ($leftCount -lt 1 -or $rightCount -lt 1 -or $leftCount + $rightCount -ne $FileCount) {
            throw "Stop did not leave a partial, accounted inverse: $leftCount restored, $rightCount unattempted."
        }
        Assert-SplitBytes
        $journalDir = Join-Path $data 'local\journal'
        $openUndo = @(Get-ChildItem -LiteralPath $journalDir -Filter 'job-*.fcj' -File | Where-Object {
            $body = Get-Content -LiteralPath $_.FullName -Raw
            $body -match '"undoOf"' -and $body -notmatch '"t":"end"'
        })
        if ($openUndo.Count -ne 0) { throw 'The stopped inverse left an open recovery journal.' }
        $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
            '--left', $source, '--right', $target) -PassThru
        Await {
            $process.Refresh()
            !$process.HasExited -and $process.MainWindowHandle -ne [IntPtr]::Zero
        } 'same-profile restart' 30 | Out-Null
        if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
            throw 'A different executable owned the restarted window.'
        }
        Assert-SplitBytes
        $result.Restored = $leftCount
        $result.NotAttempted = $rightCount
        $result.Restarted = $true
        $result.Passed = $true
    }
    else {
        $keep.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $cancel = Await { Button $window 'Cancel' } 'active inverse Cancel button' 15
        $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Await { (FolderCount $source) + (FolderCount $target) -eq $FileCount -and
            (Button $window 'Undo report') } 'canceled inverse and retained report' 120 | Out-Null
        Assert-SplitBytes

        ClickButton $window 'Undo report'
        $reportWindow = Await { @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                $_.Current.ProcessId -eq $process.Id -and $_.Current.Name -like '*Undo report*'
            })[0] } 'retained Undo report window'
        ClickButton $reportWindow 'Copy all'
        $copy = Await {
            $value = Get-Clipboard -Raw
            if ($value -like '*Undo:*' -and $value -like "*item-$('{0:D4}' -f ($FileCount - 1)).txt*") { $value }
        } 'complete copied Undo report' 60
        $lines = @($copy -split "`r?`n" | Where-Object {
            $_ -match '^(Restored|Failed|Not undone|Kept|Not attempted):? '
        })
        $restored = @($lines | Where-Object { $_ -like 'Restored *' }).Count
        $unattempted = @($lines | Where-Object { $_ -like 'Not attempted:*' }).Count
        if ($lines.Count -ne $FileCount -or $restored -lt 1 -or $unattempted -lt 1 -or
            $restored + $unattempted -ne $FileCount) {
            throw "The canceled report is incomplete: $($lines.Count) lines, $restored restored, $unattempted unattempted."
        }
        if (@($lines[30..($FileCount - 1)] | Where-Object { !$_.Contains($root) }).Count -ne 0) {
            throw 'A retained inverse outcome beyond row 30 lacks its complete fixture path.'
        }
        $result.Restored = $restored
        $result.NotAttempted = $unattempted
        $result.OutcomeLines = $lines.Count
        $result.Passed = $true
    }
    }
}
catch {
    $result.Passed = $false
    $result.Error = $_ | Out-String
    try {
        if ($window) {
            $result.ObservedText = @((Snapshot $window) | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
                $_.Current.Name.Length -gt 0
            } | Select-Object -Last 75 | ForEach-Object { $_.Current.Name })
            $result.ObservedButtons = @((Snapshot $window) | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
                $_.Current.Name.Length -gt 0
            } | Select-Object -Last 75 | ForEach-Object { $_.Current.Name })
        }
    } catch { }
}
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        $process.WaitForExit(5000) | Out-Null
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
