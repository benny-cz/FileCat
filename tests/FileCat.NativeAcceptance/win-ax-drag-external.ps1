param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Runs only in the disposable Windows VM. The host captures a screen while
# HoverReady exists, then creates HoverCaptured to release the held mouse.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeDragInput {
    [StructLayout(LayoutKind.Sequential)] public struct CursorInfo {
        public int Size; public int Flags; public IntPtr Cursor; public System.Drawing.Point Position;
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int x, int y, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, int flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
'@ -ReferencedAssemblies System.Drawing

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report)) {
    throw 'Use a Git-built FileCat.exe and fresh report directly below FileCatValidation.'
}
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat processes before the isolated drag test.'
}
$git = 'C:\Program Files\Git\cmd\git.exe'
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$head = (& $git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'The test app does not belong to a readable Git checkout.'
}

function Wait-For([scriptblock]$Check, [string]$Description, [int]$Seconds = 25) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        try { $value = & $Check; if ($value) { return $value } }
        catch [System.Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 120
    }
    throw "Timed out waiting for $Description."
}
function Get-TopWindow([string]$Title) {
    $tops = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    @($tops | Where-Object { $_.Current.Name -like "*$Title*" -and
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window }) | Select-Object -First 1
}
function Close-OldTestExplorers {
    $tops = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($top in @($tops | Where-Object { $_.Current.Name -like 'DropTarget-*' -and
        $_.Current.ClassName -eq 'CabinetWClass' })) {
        ([System.Windows.Automation.WindowPattern]$top.GetCurrentPattern(
            [System.Windows.Automation.WindowPattern]::Pattern)).Close()
    }
}
function Get-Row($Window, [string]$Name) {
    $all = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    if ($all.Count -gt 1200) { throw "UI Automation exposed $($all.Count) nodes." }
    @($all | Where-Object { $_.Current.ControlType -eq
        [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq $Name }) |
        Select-Object -First 1
}
function Cursor-Receipt {
    $info = New-Object NativeDragInput+CursorInfo
    $info.Size = [Runtime.InteropServices.Marshal]::SizeOf($info)
    if (![NativeDragInput]::GetCursorInfo([ref]$info)) { throw 'GetCursorInfo failed.' }
    @{ Handle = $info.Cursor.ToInt64(); Flags = $info.Flags;
       X = $info.Position.X; Y = $info.Position.Y }
}
function Move-Pointer([int]$FromX, [int]$FromY, [int]$ToX, [int]$ToY) {
    for ($i = 1; $i -le 25; $i++) {
        $x = [int]($FromX + ($ToX - $FromX) * $i / 25)
        $y = [int]($FromY + ($ToY - $FromY) * $i / 25)
        [NativeDragInput]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 22
    }
}

$fixture = Join-Path $validation ('DragExternal\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture ('DropTarget-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$names = @('alpha-native-drag.txt', 'beta-native-drag.txt')
[IO.File]::WriteAllText((Join-Path $source $names[0]), 'FileCat external drag alpha ' + $fixture, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $source $names[1]), 'FileCat external drag beta ' + $fixture, [Text.Encoding]::UTF8)
$sourceHashes = @{}
foreach ($name in $names) { $sourceHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash }
$hoverReady = $report + '.HoverReady'
$hoverCaptured = $report + '.HoverCaptured'
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    Fixture = $fixture; SourceHashes = $sourceHashes; RequestedNames = $names
}
$process = $null
$mouseDown = $false
$controlDown = $false
try {
    Close-OldTestExplorers
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    Wait-For { $process.Refresh(); if ($process.HasExited) { throw 'Owned FileCat exited early.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.MainWindowHandle } } 'FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'The accessible process is not the exact Git-built test app.'
    }
    [NativeDragInput]::MoveWindow($process.MainWindowHandle, 30, 60, 875, 910, $true) | Out-Null
    [NativeDragInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $result.Stage = 'Explorer target'
    Start-Process -FilePath explorer.exe -ArgumentList @($target) | Out-Null
    $explorer = Wait-For { Get-TopWindow ([IO.Path]::GetFileName($target)) } 'test-owned Explorer target'
    $explorerHandle = [IntPtr]::new($explorer.Current.NativeWindowHandle)
    [NativeDragInput]::MoveWindow($explorerHandle, 955, 60, 895, 910, $true) | Out-Null
    Start-Sleep -Milliseconds 450
    [NativeDragInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $result.Stage = 'selected complete scope'
    $first = Wait-For { Get-Row $window $names[0] } 'first source row'
    $second = Wait-For { Get-Row $window $names[1] } 'second source row'
    foreach ($row in @($first, $second)) {
        $selection = [System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.AddToSelection()
    }
    $first = Get-Row $window $names[0]
    $second = Get-Row $window $names[1]
    $firstMarked = [System.Windows.Automation.SelectionItemPattern]$first.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $secondMarked = [System.Windows.Automation.SelectionItemPattern]$second.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (!$firstMarked.Current.IsSelected -or !$secondMarked.Current.IsSelected) { throw 'The two selected source rows did not remain marked.' }
    $bounds = $first.Current.BoundingRectangle
    $fromX = [int]($bounds.Left + [Math]::Min(105, $bounds.Width / 2))
    $fromY = [int]($bounds.Top + $bounds.Height / 2)
    $explorerBounds = $explorer.Current.BoundingRectangle
    $toX = [int]($explorerBounds.Left + $explorerBounds.Width / 2)
    $toY = [int]($explorerBounds.Top + [Math]::Min(470, $explorerBounds.Height - 100))
    $result.Geometry = @{ FirstRow = @($bounds.Left, $bounds.Top, $bounds.Width, $bounds.Height);
        Explorer = @($explorerBounds.Left, $explorerBounds.Top, $explorerBounds.Width, $explorerBounds.Height);
        From = @($fromX, $fromY); To = @($toX, $toY) }
    $result.Stage = 'native drag hover'
    [NativeDragInput]::SetCursorPos($fromX, $fromY) | Out-Null
    Start-Sleep -Milliseconds 130
    [NativeDragInput]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    $mouseDown = $true
    Move-Pointer $fromX $fromY ($fromX + 12) ($fromY + 10)
    Start-Sleep -Milliseconds 400
    # Explorer defaults to Move on the same volume. Ctrl requests a truthful
    # Copy, so source preservation is part of this acceptance route.
    [NativeDragInput]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    $controlDown = $true
    Move-Pointer ($fromX + 12) ($fromY + 10) $toX $toY
    Start-Sleep -Milliseconds 650
    $result.HoverCursor = Cursor-Receipt
    $hoverChild = [NativeDragInput]::WindowFromPoint([Drawing.Point]::new($toX, $toY))
    $hoverRoot = [NativeDragInput]::GetAncestor($hoverChild, 2)
    $result.HoverWindow = @{ Expected = $explorerHandle.ToInt64(); Actual = $hoverRoot.ToInt64() }
    if ($hoverRoot -ne $explorerHandle) { throw 'A different window covered the intended Explorer drop target.' }
    [IO.File]::WriteAllText($hoverReady, 'capture hover', [Text.Encoding]::ASCII)
    Wait-For { Test-Path -LiteralPath $hoverCaptured } 'host hover screenshot' 45 | Out-Null
    $result.Stage = 'external drop'
    [NativeDragInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    $mouseDown = $false
    [NativeDragInput]::keybd_event(0x11, 0, 0x0002, [UIntPtr]::Zero)
    $controlDown = $false
    Wait-For { if (@(Get-ChildItem -LiteralPath $target -File).Count -eq 2) { $true } } 'two Explorer copies' 30 | Out-Null
    $targetNames = @(Get-ChildItem -LiteralPath $target -File | ForEach-Object { $_.Name } | Sort-Object)
    $result.TargetNames = $targetNames
    $result.TargetHashes = @{}
    foreach ($name in $targetNames) { $result.TargetHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash }
    $result.SourceStillPresent = @{}
    foreach ($name in $names) { $result.SourceStillPresent[$name] = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash }
    if (@(Compare-Object ($names | Sort-Object) $targetNames).Count -ne 0) { throw 'Explorer received an incomplete or extra manifest.' }
    foreach ($name in $names) {
        if ($result.TargetHashes[$name] -ne $sourceHashes[$name] -or $result.SourceStillPresent[$name] -ne $sourceHashes[$name]) {
            throw "Content or source changed for $name."
        }
    }
    $result.Passed = $true
    $result.Stage = 'complete'
}
catch {
    $result.Error = $_.Exception.Message
}
finally {
    if ($mouseDown) { [NativeDragInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) }
    if ($controlDown) { [NativeDragInput]::keybd_event(0x11, 0, 0x0002, [UIntPtr]::Zero) }
    if ($process -and !$process.HasExited) { $process.CloseMainWindow() | Out-Null }
    if ($explorer) {
        try { ([System.Windows.Automation.WindowPattern]$explorer.GetCurrentPattern(
            [System.Windows.Automation.WindowPattern]::Pattern)).Close() }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 10), [Text.Encoding]::UTF8)
}
if (!$result.Passed) { exit 1 }
