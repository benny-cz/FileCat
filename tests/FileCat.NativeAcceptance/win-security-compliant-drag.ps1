param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SourceExe,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('Shift', 'Copy', 'Cancel')][string]$Mode,
    [Parameter(Mandatory)][string]$ExpectedDllSha256
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DragInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int x, int y, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, int flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
'@ -ReferencedAssemblies System.Drawing

$app = [IO.Path]::GetFullPath($AppPath)
$sourceExe = [IO.Path]::GetFullPath($SourceExe)
$report = [IO.Path]::GetFullPath($ReportPath)
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
if (!$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    !$sourceExe.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or
    (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash -ine $ExpectedDllSha256) {
    throw 'Use the owned staged app, source, fresh report, and exact DLL hash.'
}

function Wait-For([scriptblock]$Check, [string]$What, [int]$Seconds = 20) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        try { $answer = & $Check; if ($answer) { return $answer } }
        catch [System.Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 120
    }
    throw "Timed out waiting for $What"
}
function Move-Pointer([int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    for ($i = 1; $i -le 32; $i++) {
        [DragInput]::SetCursorPos([int]($x1 + ($x2 - $x1) * $i / 32), [int]($y1 + ($y2 - $y1) * $i / 32)) | Out-Null
        Start-Sleep -Milliseconds 22
    }
}
function Find-Descendant($Window, $Type, [string]$Name) {
    $all = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    @($all | Where-Object { $_.Current.ControlType -eq $Type -and $_.Current.Name -eq $Name } | Select-Object -First 1)
}

$fixture = Join-Path $validation ('CompliantDrag-' + [guid]::NewGuid().ToString('N'))
$sourceFolder = Join-Path $fixture 'source'
$targetFolder = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Path $sourceFolder, $targetFolder, $data -Force | Out-Null
$source = Join-Path $sourceFolder 'owned-drag.txt'
$target = Join-Path $targetFolder 'owned-drag.txt'
$sentinel = Join-Path $targetFolder 'unchanged-sentinel.txt'
$effectFile = Join-Path $fixture 'returned-effect.txt'
[IO.File]::WriteAllText($source, 'Compliant drag source owned bytes ' + $fixture)
[IO.File]::WriteAllText($sentinel, 'Target sentinel')
$before = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$sentinelBefore = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
$result = [ordered]@{ Passed = $false; Mode = $Mode; AppDllSha256 = $ExpectedDllSha256;
    Fixture = $fixture; SourceBeforeSha256 = $before; SentinelBeforeSha256 = $sentinelBefore; Stage = 'launch' }
$appProcess = $null
$sourceProcess = $null
$mouseDown = $false
$modifierDown = $false
$modifier = if ($Mode -eq 'Shift') { 0x10 } else { 0x11 }
try {
    $appProcess = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $sourceFolder, '--right', $targetFolder) -PassThru
    $sourceProcess = Start-Process -FilePath $sourceExe -ArgumentList @($source, $effectFile) -PassThru
    Wait-For { $appProcess.Refresh(); if ($appProcess.MainWindowHandle -ne [IntPtr]::Zero) { $appProcess.MainWindowHandle } } 'FileCat window' | Out-Null
    Wait-For { $sourceProcess.Refresh(); if ($sourceProcess.MainWindowHandle -ne [IntPtr]::Zero) { $sourceProcess.MainWindowHandle } } 'compliant source window' | Out-Null
    [DragInput]::MoveWindow($sourceProcess.MainWindowHandle, 20, 65, 750, 800, $true) | Out-Null
    [DragInput]::MoveWindow($appProcess.MainWindowHandle, 955, 65, 875, 910, $true) | Out-Null
    [DragInput]::SetForegroundWindow($appProcess.MainWindowHandle) | Out-Null
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
    $sourceWindow = [System.Windows.Automation.AutomationElement]::FromHandle($sourceProcess.MainWindowHandle)
    $row = Wait-For { Find-Descendant $window ([System.Windows.Automation.ControlType]::ListItem) 'unchanged-sentinel.txt' | Select-Object -First 1 } 'target sentinel row'
    $targetList = ([System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.SelectionContainer
    $targetBounds = $targetList.Current.BoundingRectangle
    $toX = [int]($targetBounds.Left + $targetBounds.Width / 2)
    $toY = [int]($targetBounds.Top + [Math]::Min(220, $targetBounds.Height - 60))
    $button = Wait-For { Find-Descendant $sourceWindow ([System.Windows.Automation.ControlType]::Pane) 'Drag owned file' | Select-Object -First 1 } 'drag source button'
    $bounds = $button.Current.BoundingRectangle
    $fromX = [int]($bounds.Left + $bounds.Width / 2)
    $fromY = [int]($bounds.Top + $bounds.Height / 2)
    $result.Geometry = @{ From = @($fromX, $fromY); To = @($toX, $toY) }
    [DragInput]::SetForegroundWindow($sourceProcess.MainWindowHandle) | Out-Null
    [DragInput]::SetCursorPos($fromX, $fromY) | Out-Null
    Start-Sleep -Milliseconds 150
    [DragInput]::keybd_event($modifier, 0, 0, [UIntPtr]::Zero)
    $modifierDown = $true
    [DragInput]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    $mouseDown = $true
    Move-Pointer $fromX $fromY ($fromX + 20) ($fromY + 10)
    Move-Pointer ($fromX + 20) ($fromY + 10) $toX $toY
    Start-Sleep -Milliseconds 650
    if ([DragInput]::GetAncestor([DragInput]::WindowFromPoint([Drawing.Point]::new($toX, $toY)), 2) -ne $appProcess.MainWindowHandle) {
        throw 'A different window covered the FileCat target.'
    }
    $result.Stage = 'drop'
    [DragInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    $mouseDown = $false
    Start-Sleep -Milliseconds 250
    [DragInput]::keybd_event($modifier, 0, 0x0002, [UIntPtr]::Zero)
    $modifierDown = $false
    Wait-For { if (Test-Path -LiteralPath $effectFile) { Get-Content -LiteralPath $effectFile -Raw } } 'returned drag effect' 12 | Out-Null
    $result.ReturnedEffect = (Get-Content -LiteralPath $effectFile -Raw).Trim()
    if ($Mode -eq 'Copy') {
        $copy = Wait-For { Find-Descendant $window ([System.Windows.Automation.ControlType]::Button) 'Copy' | Select-Object -First 1 } 'Copy review approval' 12
        ([System.Windows.Automation.InvokePattern]$copy.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        Wait-For { Test-Path -LiteralPath $target } 'completed Copy destination' 30 | Out-Null
    }
    elseif ($Mode -eq 'Cancel') {
        $cancel = Wait-For { Find-Descendant $window ([System.Windows.Automation.ControlType]::Button) 'Cancel' | Select-Object -First 1 } 'Copy review cancellation' 12
        ([System.Windows.Automation.InvokePattern]$cancel.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    }
    Start-Sleep -Milliseconds 400
    $result.SourceAfterSha256 = if (Test-Path -LiteralPath $source) { (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash } else { $null }
    $result.TargetAfterSha256 = if (Test-Path -LiteralPath $target) { (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash } else { $null }
    $result.SentinelAfterSha256 = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
    $journal = Join-Path $data 'local\journal'
    $result.JournalFileCount = if (Test-Path -LiteralPath $journal) { @(Get-ChildItem -LiteralPath $journal -File -Recurse).Count } else { 0 }
    if ($result.SourceAfterSha256 -ne $before -or $result.SentinelAfterSha256 -ne $sentinelBefore) { throw 'Source or sentinel bytes changed.' }
    if ($Mode -eq 'Shift' -and ($result.ReturnedEffect -ne 'None' -or $result.TargetAfterSha256 -or $result.JournalFileCount -ne 0)) {
        throw 'Shift-Move was not a refused, no-transfer result.'
    }
    if ($Mode -eq 'Copy' -and ($result.ReturnedEffect -ne 'Copy' -or $result.TargetAfterSha256 -ne $before)) {
        throw 'Copy did not return Copy and produce exact destination bytes.'
    }
    if ($Mode -eq 'Cancel' -and $result.TargetAfterSha256) {
        throw 'Canceled Copy produced destination bytes.'
    }
    $result.Passed = $true
    $result.Stage = 'complete'
} catch {
    $result.Error = $_.Exception.Message
    $result.ErrorType = $_.Exception.GetType().FullName
    if ($sourceWindow) {
        try {
            $result.SourceControls = @($sourceWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
                $_.Current.ControlType.ProgrammaticName + ': ' + $_.Current.Name
            } | Select-Object -First 30)
        } catch { }
    }
} finally {
    if ($mouseDown) { [DragInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) }
    if ($modifierDown) { [DragInput]::keybd_event($modifier, 0, 0x0002, [UIntPtr]::Zero) }
    if ($sourceProcess -and !$sourceProcess.HasExited) { $sourceProcess.Kill() }
    if ($appProcess -and !$appProcess.HasExited) { $appProcess.Kill() }
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 8))
}
