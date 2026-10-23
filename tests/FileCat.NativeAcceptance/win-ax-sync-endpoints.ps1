param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [switch]$PauseReview
)

# Run in the logged-in Windows VM against an isolated Git build. The only
# filesystem changes are a private fixture beneath FileCatValidation.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatSyncInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    public static void Press(byte vk) {
        byte scan = (byte)MapVirtualKey(vk, 0);
        uint extended = vk == 0x28 ? 1u : 0u;
        keybd_event(vk, scan, extended, UIntPtr.Zero);
        keybd_event(vk, scan, extended | 2u, UIntPtr.Zero);
    }
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith(($validation + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation) {
    throw 'Use an isolated Git-built FileCat.exe and a report directly below FileCatValidation.'
}

function Wait-For([scriptblock]$Check, [string]$Description, [int]$Seconds = 20) {
    $until = [Diagnostics.Stopwatch]::StartNew()
    while ($until.Elapsed.TotalSeconds -lt $Seconds) {
        $value = & $Check
        if ($null -ne $value -and $false -ne $value) { return $value }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $Description."
}
function Descendants($root) {
    @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function One($root, $type, [string]$name) {
    $found = @(Descendants $root | Where-Object {
        $_.Current.ControlType -eq $type -and $_.Current.Name -eq $name
    })
    if ($found.Count -ne 1) { throw "Expected one native $name, found $($found.Count)." }
    $found[0]
}
function Invoke-Control($control) {
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Value($control) {
    $control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function WindowByName([int]$processId, [string]$name) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $matches = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        $nameCondition) | Where-Object {
            $_.Current.ProcessId -eq $processId -and $_.Current.Name -eq $name
        })
    if ($matches.Count -eq 1) { return $matches[0] }
    $null
}

$fixture = Join-Path $validation ('SyncEndpointsFixture\' + [guid]::NewGuid().ToString('N'))
$left = Join-Path $fixture 'source\active-client-work-with-a-long-location\Project'
$right = Join-Path $fixture 'target\archived-client-work-with-a-long-location\Project'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $left, $right, $data | Out-Null
$leftFile = Join-Path $left 'current.txt'
$rightFile = Join-Path $right 'old.txt'
[IO.File]::WriteAllBytes($leftFile, [Text.Encoding]::UTF8.GetBytes("new source content`n"))
[IO.File]::WriteAllBytes($rightFile, [Text.Encoding]::UTF8.GetBytes("old target content`n"))
$hashes = @{
    $leftFile = (Get-FileHash -LiteralPath $leftFile -Algorithm SHA256).Hash
    $rightFile = (Get-FileHash -LiteralPath $rightFile -Algorithm SHA256).Hash
}
$result = [ordered]@{
    Passed = $false
    App = $app
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    Fixture = $fixture
    Left = $left
    Right = $right
    Steps = [Collections.Generic.List[string]]::new()
}
$process = $null
$ready = [IO.Path]::ChangeExtension($report, '.ready')
$resume = [IO.Path]::ChangeExtension($report, '.resume')
Remove-Item -LiteralPath $ready, $resume -ErrorAction SilentlyContinue
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $left, '--right', $right) -PassThru
    $null = Wait-For { $process.Refresh(); if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.MainWindowHandle } } 'isolated FileCat main window' 25
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    [FileCatSyncInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $search = Wait-For { @(Descendants $main | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
        $_.Current.Name -eq 'Search commands' })[0] } 'command search'
    $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('compare directories')
    $search.SetFocus()
    [FileCatSyncInput]::Press(0x0D)
    $recursive = Wait-For { @(Descendants $main | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::CheckBox -and
        $_.Current.Name -eq 'Include subfolders: a preview of every difference below, marking nothing' })[0] } 'recursive compare choice'
    $recursive.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Control (One $main ([System.Windows.Automation.ControlType]::Button) 'Compare')
    $diff = Wait-For { @( [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.ProcessId -eq $process.Id -and $_.Current.Name -like 'Compare folders:*' })[0] } 'comparison window' 30
    $syncButton = Wait-For { @(Descendants $diff | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -like 'Synchronize*' -and $_.Current.IsEnabled })[0] } 'completed Synchronize action' 20
    Invoke-Control $syncButton
    $sync = Wait-For { WindowByName $process.Id 'Synchronize folders' } 'synchronization review'
    $result.Steps.Add('Compared distinct complete paths with identical Project basenames')

    function Endpoints {
        $edits = @(Descendants $sync | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit })
        $source = @($edits | Where-Object { $_.Current.Name -like 'Source folder: *' })
        $target = @($edits | Where-Object { $_.Current.Name -like 'Target folder: *' })
        if ($source.Count -ne 1 -or $target.Count -ne 1) { throw 'The review needs one Source and one Target field.' }
        [pscustomobject]@{ Source = $source[0]; Target = $target[0]; SourceText = Value $source[0]; TargetText = Value $target[0] }
    }
    $endpoints = Endpoints
    if ($endpoints.SourceText -cne $left -or $endpoints.TargetText -cne $right -or
        $endpoints.Source.Current.Name -cne "Source folder: $left" -or
        $endpoints.Target.Current.Name -cne "Target folder: $right") {
        throw 'The first direction lacks complete, distinct endpoint text or accessible names.'
    }
    $result.Steps.Add('Full selectable Source and Target fields expose distinct paths')
    $direction = @(Descendants $sync | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox -and
        $_.Current.Name -like 'Synchronization direction: from *' })[0]
    $mode = One $sync ([System.Windows.Automation.ControlType]::ComboBox) 'Mode'
    if (!$direction) { throw 'Direction selector is unavailable.' }
    [FileCatSyncInput]::SetForegroundWindow([IntPtr]$sync.Current.NativeWindowHandle) | Out-Null
    $direction.SetFocus()
    [FileCatSyncInput]::Press(0x28)
    $endpoints = Wait-For { $value = Endpoints; if ($value.SourceText -ceq $right -and $value.TargetText -ceq $left) { $value } } 'reversed full endpoint roles'
    $result.Steps.Add('Physical Down reversed both complete endpoint roles')
    $mode.SetFocus()
    [FileCatSyncInput]::Press(0x28)
    $warning = Wait-For { @(Descendants $sync | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.Contains('Mirror changes the target') -and $_.Current.Name.Contains($left) })[0] } 'Mirror warning with the reversed full target'
    $result.MirrorWarning = $warning.Current.Name
    $result.Steps.Add('Mirror warning names the reversed, full target path')

    $handle = [IntPtr]$sync.Current.NativeWindowHandle
    $result.Dpi = [FileCatSyncInput]::GetDpiForWindow($handle)
    $scale = $result.Dpi / 96.0
    $narrowWidth = [int][Math]::Round(640 * $scale)
    $narrowHeight = [int][Math]::Round(600 * $scale)
    $result.NarrowRequested = [ordered]@{ Logical = '640x600'; Physical = "${narrowWidth}x${narrowHeight}" }
    if (![FileCatSyncInput]::MoveWindow($handle, 120, 80, $narrowWidth, $narrowHeight, $true)) {
        throw 'Native narrow resize failed.'
    }
    Start-Sleep -Milliseconds 350
    $frame = $sync.Current.BoundingRectangle
    $sourceBounds = $endpoints.Source.Current.BoundingRectangle
    $targetBounds = $endpoints.Target.Current.BoundingRectangle
    $directionBounds = $direction.Current.BoundingRectangle
    $modeBounds = $mode.Current.BoundingRectangle
    $cancelBounds = (One $sync ([System.Windows.Automation.ControlType]::Button) 'Cancel').Current.BoundingRectangle
    $result.NarrowBounds = [ordered]@{
        Frame = "$frame"; Source = "$sourceBounds"; Target = "$targetBounds"
        Direction = "$directionBounds"; Mode = "$modeBounds"; Cancel = "$cancelBounds"
    }
    if ($frame.Width -gt ($narrowWidth + 10) -or $modeBounds.Top -le $directionBounds.Top -or
        $sourceBounds.Height -le (35 * $scale) -or $targetBounds.Height -le (35 * $scale)) {
        throw 'The native narrow review did not reflow controls and wrap both paths.'
    }
    foreach ($bounds in @($sourceBounds, $targetBounds, $directionBounds, $modeBounds, $cancelBounds)) {
        if ($bounds.Left -lt $frame.Left -or $bounds.Top -lt $frame.Top -or
            $bounds.Right -gt $frame.Right -or $bounds.Bottom -gt $frame.Bottom) {
            throw "A control escaped the narrow sync review: $bounds outside $frame."
        }
    }
    $result.Steps.Add('Narrow native review wraps both paths and keeps all choices inside')
    if ($PauseReview) {
        [IO.File]::WriteAllText($ready, 'Capture the review, then create the matching .resume file.')
        $null = Wait-For { Test-Path -LiteralPath $resume } 'native capture signal' 120
    }
    Invoke-Control (One $sync ([System.Windows.Automation.ControlType]::Button) 'Cancel')
    foreach ($path in $hashes.Keys) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $hashes[$path]) {
            throw "Cancel changed fixture bytes: $path"
        }
    }
    if ((@(Get-ChildItem -LiteralPath $left -Name) -join '|') -cne 'current.txt' -or
        (@(Get-ChildItem -LiteralPath $right -Name) -join '|') -cne 'old.txt') {
        throw 'Cancel changed the exact fixture trees.'
    }
    $result.Steps.Add('Cancel preserved both exact trees and file hashes')
    $result.Passed = $true
}
catch {
    $result.Error = $_.Exception.Message
    if ($diff) {
        try {
            $result.DifferenceControls = @(Descendants $diff | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -or
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text
            } | ForEach-Object { [pscustomobject]@{ Name = $_.Current.Name; Enabled = $_.Current.IsEnabled } })
        }
        catch { $result.DifferenceControlsError = $_.Exception.Message }
    }
}
finally {
    Remove-Item -LiteralPath $ready, $resume -ErrorAction SilentlyContinue
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
    }
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 8) + "`n")
}
if (!$result.Passed) { exit 1 }
