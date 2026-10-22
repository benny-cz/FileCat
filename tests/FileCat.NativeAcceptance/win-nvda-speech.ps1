param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][string]$NvdaPath,
    [switch]$RecordAll
)

# Hears FileCat through NVDA while native keys move file-list focus and open a dialog. NVDA runs from a portable
# copy below FileCatValidation with a test-owned configuration and log; every utterance it sends to speech is
# logged as "Speaking [...]". Only that log decides the result. Run in the interactive VM desktop session.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatNvdaInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    // Navigation keys are extended keys; without the flag Windows treats them as numeric keypad keys.
    public static void Chord(byte modifier, byte vk) {
        keybd_event(modifier, (byte)MapVirtualKey(modifier, 0), 0, UIntPtr.Zero);
        Press(vk, false);
        keybd_event(modifier, (byte)MapVirtualKey(modifier, 0), 2u, UIntPtr.Zero);
    }
    public static void Press(byte vk, bool extended) {
        byte scan = (byte)MapVirtualKey(vk, 0);
        uint flags = extended ? 1u : 0u;
        keybd_event(vk, scan, flags, UIntPtr.Zero);
        keybd_event(vk, scan, flags | 2u, UIntPtr.Zero);
    }
    public static uint ForegroundProcessId() {
        uint processId;
        GetWindowThreadProcessId(GetForegroundWindow(), out processId);
        return processId;
    }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    // Clicks the window's own title bar. Another window covering that point (such as this runner's console) would
    // receive the click instead, so the window is moved to an uncovered screen corner first when necessary.
    public static void ClickTitle(IntPtr handle) {
        int screenWidth = GetSystemMetrics(0), screenHeight = GetSystemMetrics(1);
        for (int attempt = 0; attempt < 5; attempt++) {
            RECT r;
            GetWindowRect(handle, out r);
            POINT p = new POINT { X = (r.Left + r.Right) / 2, Y = r.Top + 12 };
            if (GetAncestor(WindowFromPoint(p), 2) == handle) {
                SetCursorPos(p.X, p.Y);
                mouse_event(2, 0, 0, 0, 0);
                mouse_event(4, 0, 0, 0, 0);
                return;
            }
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            int[,] corners = { { 0, 0 }, { 0, screenHeight - height - 60 }, { screenWidth - width, screenHeight - height - 60 }, { screenWidth - width, 0 }, { 0, 0 } };
            MoveWindow(handle, Math.Max(0, corners[attempt, 0]), Math.Max(0, corners[attempt, 1]), width, height, true);
            System.Threading.Thread.Sleep(300);
        }
        throw new InvalidOperationException("Another window covers every title-bar position of the window to activate.");
    }
}
'@

$validationRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation')) + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
if (!$app.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !(Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Run a Git-built FileCat.exe from an isolated FileCatValidation folder.' }
if (!$nvda.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid') { throw 'Use the signed portable NVDA copy below FileCatValidation.' }
if (Get-Process -Name nvda -ErrorAction SilentlyContinue) { throw 'Another NVDA instance is running; stop it so this test owns the only speech log.' }

$fixture = Join-Path $validationRoot ('NvdaSpeechFixture\' + [guid]::NewGuid().ToString('N'))
$source, $target, $data, $config = 'source', 'target', 'data', 'nvda-config' | ForEach-Object { Join-Path $fixture $_ }
New-Item -ItemType Directory -Force -Path $source, $target, $data, $config | Out-Null
$expected = [ordered]@{ 'a.txt' = "alpha`n"; 'b.txt' = "beta`n"; 'c.txt' = "charlie`n" }
foreach ($name in $expected.Keys) { [IO.File]::WriteAllText((Join-Path $source $name), $expected[$name]) }
$sourceHash = @{}
foreach ($name in $expected.Keys) { $sourceHash[$name] = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash }
# No welcome dialog, update check or usage-statistics question may take focus from the tested window.
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$log = Join-Path $fixture 'nvda.log'
$report = [ordered]@{
    passed = $false; app = $app; appDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    nvda = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; fixture = $fixture; input = 'Windows virtual-key keyboard'
    sourceSha256 = $sourceHash; steps = @()
}
$script:offset = 0L
function TakeSpeech {
    if (!(Test-Path -LiteralPath $log)) { return @() }
    $stream = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($script:offset, 'Begin') | Out-Null
        $reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $last = $text.LastIndexOf("`n")
        if ($last -lt 0) { return @() }
        $complete = $text.Substring(0, $last + 1)
        $script:offset += [Text.Encoding]::UTF8.GetByteCount($complete)
        return @($complete -split "`r?`n" | Where-Object { $_.StartsWith('Speaking [') } | ForEach-Object { $_.Substring(9).Trim() })
    } finally { $stream.Dispose() }
}

$nvdaProcess = $null
$process = $null
$switchTarget = $null
try {
    $nvdaProcess = Start-Process -FilePath $nvda -ArgumentList @("--log-file=$log", '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while (!((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -Pattern 'NVDA initialized' -SimpleMatch -Quiet))) {
        if ($deadline.Elapsed.TotalSeconds -gt 30 -or $nvdaProcess.HasExited) { throw 'NVDA did not finish starting within 30 seconds.' }
        Start-Sleep -Milliseconds 300
    }
    # A test-owned window stands in for the window a user switches away from. Windows' foreground lock can keep a
    # program launched by this background runner from becoming foreground in NVDA's view, so the first step makes a
    # genuine switch from this window to FileCat.
    $switchTarget = Start-Process -FilePath powershell.exe -PassThru -WindowStyle Normal -ArgumentList @('-NoProfile', '-Command',
        "Add-Type -AssemblyName System.Windows.Forms; `$f = New-Object Windows.Forms.Form; `$f.Text = 'FileCat speech switch target'; `$f.StartPosition = 'Manual'; `$f.SetBounds(0, 0, 420, 200); [void]`$f.ShowDialog()")
    $deadline.Restart()
    do { Start-Sleep -Milliseconds 200; $switchTarget.Refresh() } while ($switchTarget.MainWindowHandle -eq [IntPtr]::Zero -and $deadline.Elapsed.TotalSeconds -lt 20)
    if ($switchTarget.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The test-owned switch window did not open.' }
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data, '--left', $source, '--right', $target) -PassThru
    $deadline.Restart()
    do { Start-Sleep -Milliseconds 200; $process.Refresh() } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and !$process.HasExited -and $deadline.Elapsed.TotalSeconds -lt 30)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The isolated FileCat window did not open.' }
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) { throw 'Unexpected executable owns the window.' }
    Start-Sleep -Seconds 2
    # Side by side, so a title-bar click reaches the intended window rather than one covering it.
    [FileCatNvdaInput]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    [FileCatNvdaInput]::MoveWindow($process.MainWindowHandle, 440, 0, 1300, 900, $true) | Out-Null
    Start-Sleep -Milliseconds 500

    function EnsureForeground([switch]$Click) {
        if ($Click) {
            # Input-driven activation, which Windows grants even while this runner's console holds the foreground.
            [FileCatNvdaInput]::ClickTitle($process.MainWindowHandle)
            Start-Sleep -Milliseconds 400
        }
        if ([FileCatNvdaInput]::ForegroundProcessId() -eq $process.Id) { return }
        [FileCatNvdaInput]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
        (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
        [FileCatNvdaInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 300
        if ([FileCatNvdaInput]::ForegroundProcessId() -ne $process.Id) {
            [FileCatNvdaInput]::ClickTitle($process.MainWindowHandle)
            Start-Sleep -Milliseconds 400
        }
        $foreground = [FileCatNvdaInput]::ForegroundProcessId()
        if ($foreground -ne $process.Id) { throw "The isolated FileCat process $($process.Id) is not foreground (actual $foreground); refusing to send keys." }
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($null -eq $focused -or $focused.Current.ProcessId -ne $process.Id) {
            throw "Keyboard focus is not in the isolated FileCat process $($process.Id); refusing to send keys."
        }
    }

    # Slash and A type "/" and "a" on the VM's US layout.
    $virtualKeys = @{ Down = @(0x28, $true); Up = @(0x26, $true); Home = @(0x24, $true); End = @(0x23, $true)
                      Tab = @(0x09, $false); F7 = @(0x76, $false); Escape = @(0x1B, $false); Enter = @(0x0D, $false)
                      Slash = @(0xBF, $false); A = @(0x41, $false) }
    function Step([string]$label, [string]$key, [string]$expect, [bool]$flush = $true, [scriptblock]$before = $null) {
        if ($before) { & $before }
        if ($flush) { TakeSpeech | Out-Null }
        EnsureForeground -Click:($null -ne $before)
        if ($key -eq 'CtrlC') { [FileCatNvdaInput]::Chord(0x11, 0x43) }
        elseif ($key -eq 'CtrlS') { [FileCatNvdaInput]::Chord(0x11, 0x53) }
        elseif ($key) {
            # A comma-separated key sequence, such as typed text followed by Enter.
            foreach ($name in $key.Split(',')) {
                [FileCatNvdaInput]::Press([byte]$virtualKeys[$name][0], [bool]$virtualKeys[$name][1])
                Start-Sleep -Milliseconds 80
            }
        }
        $heard = @()
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        while ($deadline.Elapsed.TotalSeconds -lt 6) {
            $heard += @(TakeSpeech)
            if (@($heard | Where-Object { $_.IndexOf($expect, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count) {
                Start-Sleep -Milliseconds 600
                $heard += @(TakeSpeech)
                break
            }
            Start-Sleep -Milliseconds 150
        }
        $passed = @($heard | Where-Object { $_.IndexOf($expect, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0
        $matching = @($heard | Where-Object { $_.IndexOf($expect, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count
        $report.steps += [ordered]@{ step = $label; expect = $expect; heard = $heard; matching = $matching; passed = $passed }
        if (!$passed -and !$RecordAll) { throw "${label}: NVDA said $($heard -join ' | ')" }
    }
    $switchAway = {
        # NVDA must have announced the other window, proving it saw a real foreground change before FileCat returns.
        TakeSpeech | Out-Null
        $announced = $false
        for ($attempt = 0; $attempt -lt 3 -and !$announced; $attempt++) {
            # Clicking a window that already is foreground raises no foreground event, so leave from FileCat first.
            if ($attempt -gt 0 -or [FileCatNvdaInput]::ForegroundProcessId() -eq $switchTarget.Id) {
                [FileCatNvdaInput]::ClickTitle($process.MainWindowHandle)
                Start-Sleep -Seconds 1
                TakeSpeech | Out-Null
            }
            [FileCatNvdaInput]::ClickTitle($switchTarget.MainWindowHandle)
            $wait = [Diagnostics.Stopwatch]::StartNew()
            while (!$announced -and $wait.Elapsed.TotalSeconds -lt 4) {
                $announced = @(TakeSpeech | Where-Object { $_ -like '*FileCat speech switch target*' }).Count -gt 0
                Start-Sleep -Milliseconds 150
            }
        }
        if (!$announced -or [FileCatNvdaInput]::ForegroundProcessId() -ne $switchTarget.Id) { throw 'NVDA did not observe the test-owned switch window taking the foreground.' }
    }
    Step 'switch to the new window' '' 'Go to parent location' $true $switchAway
    Step 'down to a.txt' 'Down' 'a.txt'
    Step 'down to b.txt' 'Down' 'b.txt'
    Step 'down to c.txt' 'Down' 'c.txt'
    Step 'up to b.txt' 'Up' 'b.txt'
    Step 'home to parent' 'Home' 'Go to parent location'
    Step 'end to c.txt' 'End' 'c.txt'
    Step 'tab to the target panel' 'Tab' 'Go to parent location'
    Step 'tab back to the source panel' 'Tab' 'c.txt'
    Step 'F7 opens Create folder' 'F7' 'folder'
    Step 'Escape returns to the list' 'Escape' 'c.txt'
    # The first notice after startup, then the same notice again, must each be announced.
    Step 'Ctrl+C announces the copy notice' 'CtrlC' 'Copied 1 item'
    Step 'a repeated copy is announced again' 'CtrlC' 'Copied 1 item'
    # A rejected value in a prompt is spoken as its message appears, and again when Enter is refused.
    Step 'Ctrl+S opens Quick filter' 'CtrlS' 'Quick filter dialog'
    Step 'typing an unclosed mask is announced' 'Slash' 'Unclosed regular expression in mask.'
    Step 'Enter on the rejected mask explains it again' 'A,Enter' 'Unclosed regular expression in mask.'
    Step 'Escape closes Quick filter' 'Escape' 'c.txt'
    # Returning to FileCat from another window announces its focused row again, once.
    Step 'switch away and back' '' 'c.txt' $true $switchAway
    $report.steps[-1].repeats = @($report.steps[-1].heard | Where-Object { $_ -like "*'c.txt'*" }).Count

    $names = @(Get-ChildItem -LiteralPath $source -Name | Sort-Object)
    if (($names -join ',') -ne 'a.txt,b.txt,c.txt' -or @(Get-ChildItem -LiteralPath $target -Force).Count) {
        throw 'The speech journey changed the owned fixture folders.'
    }
    foreach ($name in $expected.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash -ne $sourceHash[$name]) { throw "$name changed." }
    }
    $report.passed = @($report.steps | Where-Object { !$_.passed }).Count -eq 0
    if (!$report.passed) { throw 'NVDA did not announce every expected focus change.' }
} catch {
    $report.error = $_.Exception.Message
    throw
} finally {
    if ($switchTarget -and !$switchTarget.HasExited) { $switchTarget.Kill() }
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(10000)) { $process.Kill() }
    }
    if ($nvdaProcess -and !$nvdaProcess.HasExited) {
        Start-Process -FilePath $nvda -ArgumentList @('-q') -Wait | Out-Null
        if (!$nvdaProcess.WaitForExit(15000)) { $nvdaProcess.Kill() }
    }
    $report.speechLog = $log
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
