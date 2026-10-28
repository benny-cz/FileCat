param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# AC-S3-068 with NVDA (tooltip reporting on): one copy runs held to 1 MB/s while three more wait in the drive's queue
# (b, c, d, queued in that order from F5's Queue button). In Operations, d is moved earlier once through UI Automation
# and once by pointer, each move heard; d, now first in the queue, cannot move earlier and its hovered button says why.
# When the running copy ends the queue runs d, b, c, in that order, and every copy is whole.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatQueueInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    public static void ClickTitle(IntPtr h) {
        RECT r; GetWindowRect(h, out r);
        double scale = GetDpiForWindow(h) / 96.0;
        SetCursorPos(Math.Max(r.Left + (int)(40 * scale), r.Right - (int)(300 * scale)), r.Top + (int)(12 * scale));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@
$TreeScope = [System.Windows.Automation.TreeScope]
$AE = [System.Windows.Automation.AutomationElement]
function Descendants($element) { @($element.FindAll($TreeScope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) }
function Children($element) { @($element.FindAll($TreeScope::Children, [System.Windows.Automation.Condition]::TrueCondition)) }
function Await([scriptblock] $check, [string] $description, [int] $seconds = 20) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $check; if ($null -ne $value -and $value -ne $false) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $description"
}
function In($root, [string] $name, [string] $type = $null) {
    @(Descendants $root | Where-Object {
        $_.Current.Name -eq $name -and (-not $type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$type") })[0]
}
function Key([byte] $code, [bool] $extended = $false) {
    $flags = if ($extended) { 1 } else { 0 }
    [FileCatQueueInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [FileCatQueueInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) { [FileCatQueueInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    $reversed = @($codes); [array]::Reverse($reversed)
    foreach ($code in $reversed) { [FileCatQueueInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    Start-Sleep -Milliseconds 300
}
function ReleaseModifiers {
    $released = @()
    foreach ($k in 0xA4, 0xA5, 0x12, 0xA2, 0xA3, 0x11, 0xA0, 0xA1, 0x10, 0x5B, 0x5C) {
        if ([FileCatQueueInput]::GetAsyncKeyState($k) -band 0x8000) {
            [FileCatQueueInput]::keybd_event([byte]$k, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50; $released += $k
        }
    }
    $released
}
function Click($element) {
    $r = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $r.Width -lt 6 -or $r.Height -lt 6) { throw "Unreachable pointer target: $($element.Current.Name)" }
    [void][FileCatQueueInput]::SetCursorPos([int]($r.X + [Math]::Min(40, $r.Width / 2)), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 150
    [FileCatQueueInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [FileCatQueueInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 500
}
function TakeSpeech {
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    @($fresh | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim().Replace('\\', '\') })
}
function Hear([string] $text, [int] $seconds = 6) {
    $heard = @()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        $heard += @(TakeSpeech)
        if (@($heard | Where-Object { $_.Contains($text) }).Count) { return $heard }
        Start-Sleep -Milliseconds 200
    }
    throw "NVDA did not say '$text': $($heard -join ' / ')"
}
function Activate($window) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][FileCatQueueInput]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 400
    if ([FileCatQueueInput]::GetForegroundWindow() -ne $handle) { [FileCatQueueInput]::ClickTitle($handle); Start-Sleep -Milliseconds 600 }
    if ([FileCatQueueInput]::GetForegroundWindow() -ne $handle) { throw "$($window.Current.Name) is not foreground; refusing keys" }
}
function Focused { try { $AE::FocusedElement.Current.Name } catch { $null } }
function FocusRow([string] $name) {
    Key 0x24 $true  # Home
    for ($i = 0; $i -lt 8; $i++) {
        $at = Focused
        if ($at -eq $name -or $at -like "$name,*") { return }
        Key 0x28 $true
    }
    throw "The cursor never reached $name (at '$(Focused)')"
}
function Toggled($element) { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() }
function Invoke($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 500 }
# F5 on the item: its Copy dialog, optionally held to a speed, and started now or queued.
function SubmitCopy([string] $name, [string] $button, [string] $limit = $null) {
    Activate $main
    FocusRow $name
    Key 0x74
    $dialog = Await { In $main 'Copy dialog' } "the Copy dialog for $name"
    if ($limit) {
        # The options are folded under their "Options" header, a toggle.
        $options = Await { In $dialog 'Options' 'Button' } 'the Options header'
        if ((Toggled $options) -ne 'On') { $options.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 600 }
        $box = Await { @(Descendants $dialog | Where-Object { $_.Current.Name -like 'Speed limit*' -and
            $_.GetSupportedPatterns() -contains [System.Windows.Automation.ValuePattern]::Pattern })[0] } 'the speed limit'
        $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($limit)
        Start-Sleep -Milliseconds 400
    }
    $start = Await { $b = In $dialog $button 'Button'; if ($b -and $b.Current.IsEnabled) { $b } } "the dialog's $button button" 10
    Invoke $start
    Await { -not (In $main 'Copy dialog') } "the Copy dialog for $name to close" 10 | Out-Null
}
function Rows($history) {
    @(Children $history | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.ListItem' } |
        ForEach-Object { [pscustomobject]@{ Element = $_; Name = $_.Current.Name; Text = (@(Descendants $_ |
            Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' } | ForEach-Object { $_.Current.Name }) -join ' | ') } })
}
function Row($history, [string] $file) { @(Rows $history | Where-Object { "$($_.Name) $($_.Text)" -like "*$file*" })[0] }

$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation.TrimEnd('\') + '\'
if (-not $app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    -not $nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or (Get-Process -Name nvda -ErrorAction SilentlyContinue) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an isolated Git app, signed portable NVDA, a fresh validation report and no competing reader or FileCat'
}
$fixture = Join-Path $validation ('QueueFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'; $config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
$random = [Random]::new(68)
foreach ($file in @(@{ Name = 'big.bin'; Size = 64MB }, @{ Name = 'b.bin'; Size = 6MB }, @{ Name = 'c.bin'; Size = 6MB }, @{ Name = 'd.bin'; Size = 6MB })) {
    $bytes = [byte[]]::new($file.Size); $random.NextBytes($bytes)
    [IO.File]::WriteAllBytes((Join-Path $left $file.Name), $bytes)
}
$hashes = @{}; foreach ($f in Get-ChildItem -LiteralPath $left -File) { $hashes[$f.Name] = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash }
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
[presentation]
	reportTooltips = True
"@)
$script:nvdaLog = Join-Path $fixture 'nvda.log'
$script:speechIndex = 0
$script:result = [ordered]@{
    Passed = $false; Stage = 'starting'; Fixture = $fixture; Dpi = $null; Rows = @(); Moves = [ordered]@{}; Order = @()
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; Error = $null
}
$script:result.ReleasedBefore = @(ReleaseModifiers)
$reader = $script:owned = $null
try {
    $reader = Start-Process -FilePath $nvda -ArgumentList @(
        "--log-file=$script:nvdaLog", '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Await {
        if ($reader.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $script:nvdaLog) -and (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA initialization' 30 | Out-Null
    $script:owned = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    Await { $script:owned.Refresh(); if ($script:owned.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'owned FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($script:owned.MainModule.FileName) -ine $app) { throw 'Unexpected FileCat executable' }
    Start-Sleep 3
    $main = $AE::FromHandle($script:owned.MainWindowHandle)
    $script:result.Dpi = [FileCatQueueInput]::GetDpiForWindow($script:owned.MainWindowHandle)

    # One copy runs, held to 1 MB/s (about a minute); three wait in the queue.
    $script:result.Stage = 'jobs'
    SubmitCopy 'big.bin' 'Copy' '1'
    # The copy may write under a temporary name until it ends: anything in the destination means it started.
    Await { @(Get-ChildItem -LiteralPath $right -Force).Count -gt 0 } 'the held copy to start' 15 | Out-Null
    # Each queued copy is held to 4 MB/s too (about 1.5 s), so the order they run in can be seen from outside.
    foreach ($name in 'b.bin', 'c.bin', 'd.bin') { SubmitCopy $name 'Queue' '4' }

    # Operations: the history lists them; d is the last queued.
    $script:result.Stage = 'operations'
    Activate $main
    Chord @(0x11, 0x4A)  # Ctrl+J
    $history = Await { In $main 'Operations history' 'List' } 'the operations history'
    Start-Sleep -Milliseconds 800
    $script:result.Rows = @(Rows $history | ForEach-Object { "$($_.Name) || $($_.Text)" })
    foreach ($name in 'b.bin', 'c.bin', 'd.bin') { if (-not (Row $history $name)) { throw "No history row for $name" } }
    if (Test-Path -LiteralPath (Join-Path $right 'd.bin')) { throw 'A queued copy ran before the held one ended' }

    # d earlier through UI Automation: selected and its button invoked.
    $script:result.Stage = 'move by UI Automation'
    [void](TakeSpeech)
    (Row $history 'd.bin').Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $move = Await { $b = In $main 'Move earlier in queue' 'Button'; if ($b -and $b.Current.IsEnabled -and -not $b.Current.IsOffscreen) { $b } } 'an available Move earlier in queue' 8
    $script:result.Moves.ByAutomation = [ordered]@{ Help = $move.Current.HelpText }
    Invoke $move
    $script:result.Moves.ByAutomation.Heard = @(Hear 'earlier in the queue')
    # The selected row is heard as the operation and where it stands, not as a class name.
    if (-not @($script:result.Moves.ByAutomation.Heard | Where-Object { $_.Contains('Copy "d.bin" to') -and $_ -notmatch 'earlier in the queue' }).Count -or
        @($script:result.Moves.ByAutomation.Heard | Where-Object { $_ -match 'JobViewModel' }).Count) {
        throw "NVDA did not name d's row: $($script:result.Moves.ByAutomation.Heard -join ' / ')"
    }
    # d earlier again by pointer: its row clicked, then its button.
    $script:result.Stage = 'move by pointer'
    Click (Row $history 'd.bin').Element
    $move = Await { $b = In $main 'Move earlier in queue' 'Button'; if ($b -and $b.Current.IsEnabled -and -not $b.Current.IsOffscreen) { $b } } 'Move earlier in queue for d again' 8
    Click $move
    $script:result.Moves.ByPointer = [ordered]@{ Heard = @(Hear 'earlier in the queue') }
    # d is first in the queue now: its button is unavailable and, hovered, says why.
    $script:result.Stage = 'first in the queue'
    $first = Await { $b = In $main 'Move earlier in queue' 'Button'; if ($b -and -not $b.Current.IsEnabled) { $b } } 'the unavailable move for the first queued' 8
    $script:result.Moves.First = [ordered]@{ Help = $first.Current.HelpText }
    $r = $first.Current.BoundingRectangle
    [void][FileCatQueueInput]::SetCursorPos(5, 5); Start-Sleep -Milliseconds 300
    [void](TakeSpeech)
    [void][FileCatQueueInput]::SetCursorPos([int]($r.X + [Math]::Min(40, $r.Width / 2)), [int]($r.Y + $r.Height / 2))
    $script:result.Moves.First.Heard = @(Hear 'This is the first queued operation.')
    $script:result.RowsAfterMoves = @(Rows $history | ForEach-Object { "$($_.Name) || $($_.Text)" })

    # The queue runs in its new order once the held copy ends.
    $script:result.Stage = 'execution order'
    $seen = [Collections.Generic.List[string]]::new()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($seen.Count -lt 3 -and $watch.Elapsed.TotalSeconds -lt 150) {
        foreach ($name in 'b.bin', 'c.bin', 'd.bin') {
            $copy = Join-Path $right $name
            if (-not $seen.Contains($name) -and (Test-Path -LiteralPath $copy)) { $seen.Add($name) }
        }
        Start-Sleep -Milliseconds 20
    }
    $script:result.Order = @($seen)
    if (($seen -join ',') -ne 'd.bin,b.bin,c.bin') { throw "The queue ran $($seen -join ', ')" }
    # NVDA hears the copies end in that order too.
    $done = [Collections.Generic.List[string]]::new()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($done.Count -lt 3 -and $watch.Elapsed.TotalSeconds -lt 20) {
        foreach ($line in @(TakeSpeech)) {
            if ($line -match 'Copy "([bcd])\.bin" to .*: done') { $done.Add($Matches[1] + '.bin') }
        }
        Start-Sleep -Milliseconds 200
    }
    $script:result.CompletionHeard = @($done)
    if (($done -join ',') -ne 'd.bin,b.bin,c.bin') { throw "NVDA heard the queued copies end as $($done -join ', ')" }
    Await { @(Get-ChildItem -LiteralPath $right -File).Count -eq 4 -and
        @(Get-ChildItem -LiteralPath $right -File | Where-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -ne $hashes[$_.Name] }).Count -eq 0 } 'every copy to be whole' 30 | Out-Null
    $script:result.Passed = $true
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message }
finally {
    if ($script:owned -and -not $script:owned.HasExited) { $script:owned.Kill(); $script:owned.WaitForExit(5000) | Out-Null }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(5000) | Out-Null }
    $script:result.ReleasedAfter = @(ReleaseModifiers)
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
