# N-UI.C on Windows x64: the released FileCat build (C-only composition) with keyboard and NVDA at 640 x 420 DIP.
# Covers folder-scope radios, fixed help text, the absence of EFS controls and commands, the reset scope on each
# opening, the partial-result summary without percentages or ETA, and focus return. Run in the interactive session
# of the disposable VMware guest at medium integrity; the fixture lives under %LOCALAPPDATA%\FileCatValidation.
param(
    [Parameter(Mandatory)][string]$TestRoot,
    [Parameter(Mandatory)][string]$NvdaPath
)

$ErrorActionPreference = 'Stop'
$nativeInputSource = @'
using System;
using System.Runtime.InteropServices;
public static class StorageReviewInput {
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint key, uint kind);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static void Send(byte[] keys, bool extended) {
        uint flags = extended ? 1u : 0u;
        foreach (byte key in keys) keybd_event(key, (byte)MapVirtualKey(key, 0), flags, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--)
            keybd_event(keys[i], (byte)MapVirtualKey(keys[i], 0), flags | 2u, UIntPtr.Zero);
    }
    public static bool ResizeClient(IntPtr window, int width, int height) {
        Rect outer, client;
        if (!GetWindowRect(window, out outer) || !GetClientRect(window, out client)) return false;
        uint dpi = GetDpiForWindow(window);
        if (dpi == 0) return false;
        return SetWindowPos(window, IntPtr.Zero, 0, 0,
            (int)Math.Round(width * dpi / 96.0) + outer.Right - outer.Left - client.Right,
            (int)Math.Round(height * dpi / 96.0) + outer.Bottom - outer.Top - client.Bottom, 0x0016);
    }
    public static int[] ClientDip(IntPtr window) {
        Rect bounds;
        uint dpi = GetDpiForWindow(window);
        if (dpi == 0 || !GetClientRect(window, out bounds)) return Array.Empty<int>();
        return new[] { (int)Math.Round(bounds.Right * 96.0 / dpi), (int)Math.Round(bounds.Bottom * 96.0 / dpi) };
    }
    public static bool ClickUncoveredTitle(IntPtr window) {
        Rect bounds;
        if (!GetWindowRect(window, out bounds)) return false;
        var point = new Point { X = (bounds.Left + bounds.Right) / 2, Y = bounds.Top + 12 };
        if (GetAncestor(WindowFromPoint(point), 2) != window || !SetCursorPos(point.X, point.Y)) return false;
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        return true;
    }
    public static uint ForegroundProcess() {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return 0;
        uint pid;
        GetWindowThreadProcessId(foreground, out pid);
        return pid;
    }
    public static int InputDesktopError() {
        IntPtr desktop = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS.
        if (desktop == IntPtr.Zero) return Marshal.GetLastWin32Error();
        CloseDesktop(desktop);
        return 0;
    }
}
'@
$testRoot = [IO.Path]::GetFullPath($TestRoot)
$nvdaPath = [IO.Path]::GetFullPath($NvdaPath)
$session = (Get-Process -Id $PID).SessionId
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notmatch 'VMware') { throw 'Use the disposable VMware validation guest.' }
$validationRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation')) + '\'
if (!$session -or
    !$testRoot.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    !$nvdaPath.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    (Get-AuthenticodeSignature -LiteralPath $nvdaPath).Status -ne 'Valid') {
    throw 'Run the owned release fixture with signed NVDA in the interactive validation profile.'
}
$log = Join-Path $testRoot 'compression-release-result.json'
if (Test-Path -LiteralPath $log) { throw 'Use a fresh test root to preserve prior receipts.' }
$record = [ordered]@{ Passed = $false; Stage = 'process preflight'; Error = $null; Session = $session; Controls = @{}; Speech = @{}; Results = @{} }
$app = $reader = $null
$previousDpiContext = [IntPtr]::Zero
try {
    if (Get-Process dotnet,testhost,FileCat,FileCat.ShellHost,FileCat.App.Tests,FileCat.*QualificationHost,FileCat.*CrashHarness,nvda -ErrorAction SilentlyContinue) {
        throw 'A test process is already using this VM.'
    }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -TypeDefinition $nativeInputSource
    $previousDpiContext = [StorageReviewInput]::SetThreadDpiAwarenessContext([IntPtr](-4))
    if ($previousDpiContext -eq [IntPtr]::Zero) { throw 'Could not use physical pixels for the window measurements.' }
    $ui = [System.Windows.Automation.AutomationElement]
    $scope = [System.Windows.Automation.TreeScope]
    $all = [System.Windows.Automation.Condition]::TrueCondition
    function Await([scriptblock]$probe, [string]$description, [int]$seconds = 18) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while ($watch.Elapsed.TotalSeconds -lt $seconds) {
            try { $value = & $probe; if ($value) { return $value } }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
            catch [Runtime.InteropServices.COMException] { }
            Start-Sleep -Milliseconds 120
        }
        throw "Timed out waiting for $description."
    }
    function Require-Front {
        if ($main.Current.ProcessId -ne $app.Id -or [StorageReviewInput]::GetForegroundWindow() -ne [IntPtr]$main.Current.NativeWindowHandle) {
            throw 'The release instance does not own the foreground window.'
        }
    }
    function Key([byte[]]$keys, [bool]$extended = $false) {
        Require-Front
        [StorageReviewInput]::Send($keys, $extended)
        Start-Sleep -Milliseconds 300
    }
    function Find-Control([string]$name, [string]$kind) {
        $main.FindAll($scope::Descendants, $all) | Where-Object {
            $_.Current.Name -like $name -and (!$kind -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$kind")
        } | Select-Object -First 1
    }
    function Take-Speech {
        $lines = @(Get-Content -LiteralPath $speechPath -Encoding UTF8 -ErrorAction SilentlyContinue)
        $new = @($lines | Select-Object -Skip $script:speechIndex)
        $script:speechIndex = $lines.Count
        $new | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() }
    }
    # $seed carries speech already heard for the same moment, e.g. a radio button that took focus as the dialog opened.
    function Hear([string]$label, [string]$pattern, [string[]]$seed = @()) {
        $record.Speech[$label] = @($seed)
        Await {
            $record.Speech[$label] += @(Take-Speech)
            ($record.Speech[$label] -join ' ') -match $pattern
        } "NVDA speech for $label" | Out-Null
    }
    function Snapshot([string]$label) {
        $record.Controls[$label] = @($main.FindAll($scope::Descendants, $all) | ForEach-Object {
            $item = $_.Current
            if ($item.Name) { [pscustomobject]@{Name=$item.Name; Type=$item.ControlType.ProgrammaticName; Enabled=$item.IsEnabled; Offscreen=$item.IsOffscreen; Help=$item.HelpText} }
        })
    }
    function Tab-To([string]$name, [string]$kind) {
        for ($i = 0; $i -lt 40; $i++) {
            $focus = $ui::FocusedElement.Current
            if ($focus.Name -eq $name -and !$focus.IsOffscreen -and
                (!$kind -or $focus.ControlType.ProgrammaticName -eq "ControlType.$kind")) { return }
            Key @(0x09)
        }
        throw "Keyboard focus did not reach $name."
    }
    function Selected([string]$name) {
        $radio = Find-Control $name 'RadioButton'
        if (!$radio) { throw "Missing radio button: $name" }
        return $radio.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
    }
    function Show-Command([string]$phrase, [string]$title) {
        Key @(0x11, 0x10, 0x50)
        Await { $ui::FocusedElement.Current.Name -eq 'Search commands' } 'command search focus' | Out-Null
        Require-Front
        [System.Windows.Forms.SendKeys]::SendWait($phrase)
        Start-Sleep -Milliseconds 300
        [void](Take-Speech)
        Key @(0x0D)
        Await { Find-Control $title } $title | Out-Null
    }
    function Require-NoEfsControls([string]$label) {
        $efs = @($main.FindAll($scope::Descendants, $all) | Where-Object {
            $type = $_.Current.ControlType.ProgrammaticName
            $type -in 'ControlType.RadioButton','ControlType.CheckBox','ControlType.Button','ControlType.MenuItem' -and
                $_.Current.Name -match '(?i)\bEFS\b|encrypt|decrypt'
        } | ForEach-Object { $_.Current.Name })
        if ($efs.Count) { throw "$label exposes EFS controls: $($efs -join '; ')" }
    }
    function Review-Folder([string]$action, [string]$expectedResult) {
        $record.Stage = "open the $action review"
        Show-Command 'NTFS compression' 'NTFS compression*'
        Hear "$action review" 'NTFS compression'
        Require-NoEfsControls "The $action review"
        foreach ($text in 'Current state of selected items. Folder contents have not been inspected.*',
            '*Alternate data streams keep their own compression settings*') {
            if (!(Find-Control $text)) { throw "The $action review does not show: $text" }
        }
        if (!(Selected 'Selected items only') -or (Selected 'Selected folders and their existing contents')) {
            throw 'Each opening must start with the selected-items-only scope.'
        }
        $choose = Find-Control 'Choose action' 'Button'
        if (!$choose -or $choose.Current.IsEnabled) { throw 'The footer must stay disabled until an action is chosen.' }
        # The dialog may open with focus already on the first action, so its announcement can precede this point.
        $heard = @($record.Speech["$action review"]) + @(Take-Speech)
        Tab-To $action 'RadioButton'
        Hear "$action radio focus" ("'" + [regex]::Escape($action) + "', 'radio button'") $heard
        Key @(0x20)
        # Choosing the focused radio is announced as its new state; "not checked" does not match the quoted 'checked'.
        Hear "$action radio" "'checked'"
        Await { Find-Control 'Every selected item passed inspection for this action.' } "completed $action inspection" | Out-Null
        [void](Take-Speech)
        Tab-To 'Selected folders and their existing contents' 'RadioButton'
        Hear "$action folder scope focus" "'Selected folders and their existing contents', 'radio button'"
        Key @(0x20)
        Hear "$action folder scope" "'checked'"
        Await { Selected 'Selected folders and their existing contents' } 'the recursive scope selection' | Out-Null
        if (Selected 'Selected items only') { throw 'The two scope choices must be exclusive.' }
        $footer = Await { $b = Find-Control $action 'Button'; if ($b -and $b.Current.IsEnabled -and !$b.Current.IsOffscreen) { $b } } "the enabled $action footer"
        $record.Results["$action footer help"] = $footer.Current.HelpText
        Snapshot "$action review"
        [void](Take-Speech)
        Tab-To $action 'Button'
        Key @(0x0D)
        $record.Stage = "$action result"
        $result = Await { Find-Control "*$action*$expectedResult*" } "the $action result" 120
        if ($result.Current.IsOffscreen) { throw "$action has no visible result." }
        $record.Results[$action] = $result.Current.Name
        $operationText = @($main.FindAll($scope::Descendants, $all) | Where-Object { $_.Current.Name -match ([regex]::Escape($action)) } |
            ForEach-Object { $_.Current.Name + ' ' + $_.Current.HelpText })
        $record.Results["$action operation text"] = $operationText
        if (($operationText -join ' ') -match '\d\s?%|(?i)\bremaining\b|\bETA\b|time left|\bper second\b|/s\b') {
            throw "The $action operation shows a percentage, rate or ETA."
        }
        Snapshot "$action result"
    }

    $fixture = Join-Path $testRoot ('session-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $record.Fixture = Split-Path -Leaf $fixture
    $left = Join-Path $fixture 'left-release-review'
    $right = Join-Path $fixture 'right'
    $state = Join-Path $fixture 'state'
    $nvdaConfig = Join-Path $fixture 'nvda'
    $speechPath = Join-Path $fixture 'nvda.log'
    $script:speechIndex = 0
    New-Item -ItemType Directory -Path $left,$right,$state,$nvdaConfig -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $nvdaConfig 'nvda.ini'), "[general]`r`nshowWelcomeDialogAtStartup = False`r`n[update]`r`nautoCheck = False`r`nstartupNotification = False`r`nallowUsageStats = False`r`naskedAllowUsageStats = True`r`n")
    $folder = Join-Path $left 'folder-release'
    New-Item -ItemType Directory -Path $folder | Out-Null
    $inside = Join-Path $folder 'inside.txt'
    $locked = Join-Path $folder 'locked.txt'
    $outside = Join-Path $left 'zeta-unselected.txt'
    foreach ($path in $inside,$locked,$outside) { [IO.File]::WriteAllText($path, ('FileCat compression release fixture. ' * 512)) }
    Set-Content -LiteralPath $inside -Stream 'proof' -Value 'named stream' -NoNewline -Encoding ASCII
    [IO.File]::SetAttributes($locked, [IO.File]::GetAttributes($locked) -bor [IO.FileAttributes]::ReadOnly)
    $hashes = @{}
    foreach ($path in $inside,$locked,$outside) { $hashes[$path] = (Get-FileHash -LiteralPath $path).Hash }

    $record.Stage = 'launch'
    $exe = Join-Path $testRoot 'app\FileCat.exe'
    $record.FileCatDllSHA256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'FileCat.dll')).Hash
    $record.WindowsDllSHA256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'FileCat.Platform.Windows.dll')).Hash
    $record.NvdaVersion = (Get-Item -LiteralPath $nvdaPath).VersionInfo.ProductVersion
    $app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru -ArgumentList @(
        '--new-instance', '--reset-layout', '--data', "`"$state`"", '--left', "`"$left`"", '--right', "`"$right`"")
    $main = Await {
        $app.Refresh()
        if ($app.HasExited) { throw "The release instance exited with $($app.ExitCode)." }
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $ui::FromHandle($app.MainWindowHandle) }
    } 'the release window' 60
    $list = Await {
        $main.FindAll($scope::Descendants, $all) | Where-Object {
            $_.Current.ClassName -eq 'FileList' -and
                @($_.FindAll($scope::Children, $all) | Where-Object { $_.Current.Name -eq 'folder-release' }).Count -eq 1
        } | Select-Object -First 1
    } 'the populated source list'
    $handle = [IntPtr]$main.Current.NativeWindowHandle
    [void][StorageReviewInput]::SetForegroundWindow($handle)
    if (![StorageReviewInput]::ResizeClient($handle, 640, 420)) { throw 'Could not resize the owned window.' }
    $record.ClientDip = Await {
        $size = [StorageReviewInput]::ClientDip($handle)
        if ($size.Length -eq 2 -and $size[0] -ge 640 -and $size[0] -le 644 -and $size[1] -ge 420 -and $size[1] -le 424) { $size }
    } 'the 640 by 420 DIP client'
    $record.Dpi = [StorageReviewInput]::GetDpiForWindow($handle)

    $record.Stage = 'launch NVDA'
    $list.SetFocus()
    $reader = Start-Process -FilePath $nvdaPath -WindowStyle Hidden -PassThru -ArgumentList @(
        "--log-file=`"$speechPath`"", '--log-level=12', "--config-path=`"$nvdaConfig`"", '--disable-addons')
    Await {
        $reader.Refresh()
        if ($reader.HasExited) { throw "NVDA exited with $($reader.ExitCode) during startup." }
        Select-String -LiteralPath $speechPath -SimpleMatch 'NVDA initialized' -Quiet -ErrorAction SilentlyContinue
    } 'NVDA startup' 60 | Out-Null
    if ([StorageReviewInput]::GetForegroundWindow() -ne $handle) { [void][StorageReviewInput]::SetForegroundWindow($handle) }
    if ([StorageReviewInput]::GetForegroundWindow() -ne $handle) {
        if (![StorageReviewInput]::ClickUncoveredTitle($handle)) { throw 'A foreground window covers the owned FileCat fixture.' }
        Await { [StorageReviewInput]::GetForegroundWindow() -eq $handle } 'foreground FileCat after NVDA startup' | Out-Null
    }
    $list.SetFocus()
    Key @(0x2D, 0x09)
    Hear 'active FileCat window' 'FileCat.{0,180}window|Panel 1, source'
    Key @(0x24) $true
    for ($i = 0; $i -lt 4 -and $ui::FocusedElement.Current.Name -ne 'folder-release'; $i++) { Key @(0x28) $true }
    Hear 'focused folder' 'folder-release'

    $record.Stage = 'the release build advertises no EFS commands'
    Require-NoEfsControls 'The release main window'
    foreach ($phrase in 'EFS protection', 'Back up EFS key') {
        Key @(0x11, 0x10, 0x50)
        Await { $ui::FocusedElement.Current.Name -eq 'Search commands' } 'command search focus' | Out-Null
        Require-Front
        [System.Windows.Forms.SendKeys]::SendWait($phrase)
        Start-Sleep -Milliseconds 900
        $offered = @($main.FindAll($scope::Descendants, $all) | Where-Object {
            $_.Current.ControlType.ProgrammaticName -eq 'ControlType.ListItem' -and $_.Current.Name -like "$phrase*"
        })
        $record.Results["palette $phrase"] = $offered.Count
        if ($offered.Count) { throw "The release palette offers $phrase." }
        Key @(0x1B)
        Await { $ui::FocusedElement.Current.Name -ne 'Search commands' } 'command search dismissal' | Out-Null
    }
    $list.SetFocus()
    Hear 'focus after the palette checks' 'folder-release'

    Review-Folder 'Compress' '2 changed, 0 already satisfied, 1 skipped, 0 failed, 0 uncertain, 0 not processed'
    $compressed = @{}
    foreach ($path in $folder,$inside,$locked,$outside) { $compressed[$path] = [bool]([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::Compressed) }
    $record.Results['after compress'] = $compressed
    if (!$compressed[$folder] -or !$compressed[$inside] -or $compressed[$locked] -or $compressed[$outside]) {
        throw 'Compress must change the folder and its writable child only.'
    }
    # FileCat itself must return focus to the reviewed folder, at submission or when the review is dismissed.
    $sinceResult = @(Take-Speech)
    Key @(0x1B)
    Await { !(Find-Control 'NTFS compression*') } 'compression review dismissal' | Out-Null
    Await { $ui::FocusedElement.Current.Name -eq 'folder-release' } 'focus returning to the reviewed folder after Compress' | Out-Null
    Hear 'focus after compression' 'folder-release' $sinceResult

    Review-Folder 'Remove compression' '2 changed, 1 already satisfied, 0 skipped, 0 failed, 0 uncertain, 0 not processed'
    foreach ($path in $folder,$inside,$locked,$outside) {
        if ([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::Compressed) { throw "Removal left $path compressed." }
    }
    foreach ($path in $inside,$locked,$outside) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$path]) { throw "The payload of $path changed." }
    }
    if ((Get-Content -LiteralPath $inside -Stream 'proof' -Raw) -cne 'named stream') { throw 'The named stream changed.' }
    if (!([IO.File]::GetAttributes($locked) -band [IO.FileAttributes]::ReadOnly)) { throw 'The read-only attribute was cleared.' }
    $sinceResult = @(Take-Speech)
    Key @(0x1B)
    Await { !(Find-Control 'NTFS compression*') } 'removal review dismissal' | Out-Null
    Await { $ui::FocusedElement.Current.Name -eq 'folder-release' } 'focus returning to the reviewed folder after removal' | Out-Null
    Hear 'focus after removal' 'folder-release' $sinceResult
    $record.Stage = 'complete'
    $record.Passed = $true
}
catch {
    $record.Error = $_.Exception.Message
    if ($main) { Snapshot 'failure' }
    throw
}
finally {
    if ($app -and !$app.HasExited) { [void]$app.CloseMainWindow(); if (!$app.WaitForExit(4000)) { $app.Kill() } }
    if ($reader -and !$reader.HasExited) { $reader.Kill(); [void]$reader.WaitForExit(4000) }
    if ($previousDpiContext -ne [IntPtr]::Zero) { [void][StorageReviewInput]::SetThreadDpiAwarenessContext($previousDpiContext) }
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $log -Encoding UTF8
}
