param(
    [ValidateSet('Launch', 'Consent', 'Reader')][string]$Role = 'Launch',
    [string]$AppPath = 'C:\Program Files\FileCat\FileCat.exe',
    [string]$NvdaPath,
    [string]$ReportPath,
    [string]$Fixture = 'C:\FileCatValidation\nvda-audit',
    [string]$Control = 'C:\FileCatValidation\nvda-audit-control',
    [ValidateSet('Medium', 'High')][string]$ReaderLevel = 'Medium',
    # approve, cancel, unavailable; comma-separated also works through powershell -File.
    [string[]]$Journeys = @('approve', 'cancel', 'unavailable'),
    [string]$AuditSid = 'S-1-5-32-545',
    [string]$AuditMask = '0x00010000',
    [string]$RunFolder,
    [switch]$RecordOnly
)
# G-UX (Windows): NVDA on the authorized-inspection flow, the elevated helper's consent task dialogs and the audit
# editor of an INSTALLED FileCat (FileCat.PrivilegedHost.exe beside FileCat.exe) on an idle disposable VMware guest.
#
# One script, three roles. Start Launch from an elevated guest shell (for example a vmrun batch logon):
#   Launch   (elevated) builds the fixture: an Administrators-owned file the signed-in user's ordinary token can read,
#            with one audit entry (Everyone, ReadAttributes, success and failure) and a Medium integrity label. It
#            starts NVDA (private profile and log) at the chosen level, the Consent worker (elevated) and the Reader
#            (ordinary token) as one-shot interactive scheduled tasks, then stops them, compares the stored SACL and
#            removes every task it registered. Its report is -ReportPath; the Reader's is reader.json beside it.
#   Consent  answers only FileCat.PrivilegedHost's task dialogs, in queue order, after the Reader has heard each one
#            (a release marker) or 60 s. It records each dialog's UI Automation text and, when the Reader asks and only
#            while that dialog is the foreground window, presses NVDA+Tab, NVDA+B and then Tab four times (never Enter or
#            Space): keys sent from the Reader's ordinary token never reach NVDA while an elevated window has the
#            foreground. Where UAC prompts, approve it by hand: this worker never touches consent.exe or the secure
#            desktop.
#   Reader   drives FileCat by keyboard (UI Automation only finds elements and checks focus) and keeps every phrase
#            NVDA speaks: the inspector's "Inspect with authorization..." action, the waiting state, the helper's
#            dialogs (automatic speech, NVDA+Tab, NVDA+B, a Tab walk), the "Authorized permissions and ownership" panel with its
#            Auditing list, the "Edit audit rules" editor (entries, add check box, SID and mask boxes, events, review
#            lines, "Apply with authorization") up to "Verified changed", a cancelled approval, and the "Authorized
#            inspection unavailable" alert when the object's metadata changes during consent.
# A portable NVDA at the user's ordinary level cannot interact with an elevated window (NVDA user guide, portable
# copies): it spoke only the helper dialog's title and focused button, and no key reached it while that dialog had the
# foreground. -ReaderLevel High runs the same signed portable NVDA elevated as the stand-in for an installed NVDA's
# UIAccess.
# -RecordOnly keeps going past a missing phrase and only records it.
$ErrorActionPreference = 'Stop'
$Journeys = @($Journeys | ForEach-Object { "$_" -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($journey in $Journeys) { if ($journey -notin 'approve', 'cancel', 'unavailable') { throw "Unknown journey '$journey'." } }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GuxAuditInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint k, uint t);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int k);
    [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    static readonly int[] Held = { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x2D };
    static bool Extended(int k) { return k == 0x5B || k == 0x5C || k == 0xA3 || k == 0xA5 || k == 0x2D; }
    public static void Key(byte k, bool extended) {
        uint f = extended ? 1u : 0u; byte s = (byte)MapVirtualKey(k, 0);
        keybd_event(k, s, f, UIntPtr.Zero); keybd_event(k, s, f | 2u, UIntPtr.Zero);
    }
    public static void Chord(byte[] keys) {
        foreach (byte k in keys) keybd_event(k, (byte)MapVirtualKey(k, 0), 0, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], (byte)MapVirtualKey(keys[i], 0), 2, UIntPtr.Zero);
    }
    // NVDA's desktop layout: the extended Insert key is the NVDA modifier.
    public static void Nvda(byte k) {
        byte ins = (byte)MapVirtualKey(0x2D, 0);
        keybd_event(0x2D, ins, 1u, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        Key(k, false);
        System.Threading.Thread.Sleep(60);
        keybd_event(0x2D, ins, 3u, UIntPtr.Zero);
    }
    public static string Release() {
        string released = "";
        foreach (int k in Held)
            if ((GetAsyncKeyState(k) & 0x8000) != 0) {
                keybd_event((byte)k, (byte)MapVirtualKey((uint)k, 0), Extended(k) ? 3u : 2u, UIntPtr.Zero);
                released += string.Format("0x{0:X2} ", k);
            }
        return released.Trim();
    }
    public static uint WindowProcess(IntPtr h) { uint pid; GetWindowThreadProcessId(h, out pid); return pid; }
    public static uint ForegroundProcess() { return WindowProcess(GetForegroundWindow()); }
    // Clicks the window's own title bar; another window covering that point would get the click, so move first.
    public static bool ClickTitle(IntPtr handle) {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        for (int attempt = 0; attempt < 5; attempt++) {
            RECT r; GetWindowRect(handle, out r);
            POINT p = new POINT { X = (r.Left + r.Right) / 2, Y = r.Top + 12 };
            if (GetAncestor(WindowFromPoint(p), 2) == handle) {
                SetCursorPos(p.X, p.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                return true;
            }
            int w = r.Right - r.Left, hh = r.Bottom - r.Top;
            int[,] corners = { { 0, 0 }, { 0, sh - hh - 60 }, { sw - w, sh - hh - 60 }, { sw - w, 0 }, { 0, 0 } };
            MoveWindow(handle, Math.Max(0, corners[attempt, 0]), Math.Max(0, corners[attempt, 1]), w, hh, true);
            System.Threading.Thread.Sleep(300);
        }
        return false;
    }
}
'@
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$ellipsis = [string][char]0x2026
$helperName = 'FileCat.PrivilegedHost'
$fileName = 'F-audit.txt'

function Write-Json($value, [string]$path) { $value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8 }
function Integrity {
    $groups = (& whoami.exe /groups /fo csv) -join "`n"
    if ($groups -match 'S-1-16-12288') { 'high' } elseif ($groups -match 'S-1-16-8192') { 'medium' } else { 'other' }
}
function Is-VMware {
    $bios = Get-ItemProperty -LiteralPath 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS' -ErrorAction SilentlyContinue
    return "$($bios.SystemManufacturer) $($bios.SystemProductName)" -match 'VMware'
}
function Others {
    # Any FileCat, helper, .NET test or reader process means another tester (or a leftover) owns the guest.
    @(Get-Process -Name FileCat, $helperName, FileCat.ShellHost, nvda, nvda_slave, dotnet, testhost -ErrorAction SilentlyContinue |
        ForEach-Object { "$($_.ProcessName):$($_.Id)" })
}

# ---------------------------------------------------------------------------------------------------------------------
# Consent: an elevated worker in the signed-in session that answers the helper's task dialogs.
# ---------------------------------------------------------------------------------------------------------------------
function Run-Consent {
    $log = Join-Path $Control 'consent-worker.log'
    $outcomes = Join-Path $Control 'consent-outcomes.jsonl'
    $stop = Join-Path $Control 'stop'
    function Log([string]$m) { Add-Content -LiteralPath $log -Value ('{0:o} {1}' -f [DateTimeOffset]::Now, $m) -Encoding UTF8 }
    if ((Integrity) -ne 'high' -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0 -or !(Test-Path -LiteralPath (Join-Path $Control 'queue.json'))) {
        throw 'The consent worker runs elevated in the signed-in session with a queue.'
    }
    $queue = @(Get-Content -Raw -LiteralPath (Join-Path $Control 'queue.json') | ConvertFrom-Json | ForEach-Object { $_ })
    $target = Join-Path $Fixture $fileName
    Log "start pid=$PID session=$([Diagnostics.Process]::GetCurrentProcess().SessionId) queue=$($queue -join ',') markers=$RunFolder"
    function Describe($window) {
        $items = New-Object System.Collections.Generic.List[object]
        try {
            foreach ($e in @($window.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition))) {
                if ($items.Count -ge 120) { break }
                $c = $e.Current
                if ([string]::IsNullOrWhiteSpace($c.Name) -and [string]::IsNullOrWhiteSpace($c.AutomationId)) { continue }
                $items.Add([ordered]@{ Type = $c.ControlType.ProgrammaticName; Name = $c.Name; Id = $c.AutomationId; Class = $c.ClassName
                    Enabled = $c.IsEnabled; Focused = $c.HasKeyboardFocus; Help = $c.HelpText })
            }
        } catch { $items.Add([ordered]@{ Error = $_.Exception.Message }) }
        , $items.ToArray()
    }
    $seen = @{}
    $count = 0
    $deadline = [DateTime]::UtcNow.AddMinutes(25)
    try {
        while (!(Test-Path -LiteralPath $stop) -and [DateTime]::UtcNow -lt $deadline) {
            try {
                $helpers = @(Get-Process -Name $helperName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
                if ($helpers.Count) {
                    foreach ($window in @($AE::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition))) {
                        $handle = try { $window.Current.NativeWindowHandle } catch { $null }
                        if (!$handle -or $seen.ContainsKey($handle) -or $helpers -notcontains $window.Current.ProcessId -or $window.Current.ClassName -ne '#32770') { continue }
                        $seen[$handle] = $true
                        $count++
                        Start-Sleep -Milliseconds 500
                        $foreground = [GuxAuditInput]::GetForegroundWindow()
                        $record = [ordered]@{ Index = $count; Time = [DateTimeOffset]::Now.ToString('o'); Title = $window.Current.Name
                            Handle = $handle; HelperPid = $window.Current.ProcessId; Foreground = ($foreground -eq [IntPtr]$handle)
                            ForegroundProcess = [GuxAuditInput]::WindowProcess($foreground); Texts = (Describe $window)
                            Instruction = $(if ($count -le $queue.Count) { [string]$queue[$count - 1] } else { 'none' }) }
                        Log "window $count '$($record.Title)' foreground=$($record.Foreground) instruction=$($record.Instruction)"
                        $marker = if ($RunFolder) { Join-Path $RunFolder "release-$count" } else { $null }
                        $pressed = [ordered]@{}
                        $clock = [Diagnostics.Stopwatch]::StartNew()
                        # At most 60 s: the plan expires 90 s after FileCat creates it, and the dialog with it.
                        while ($clock.Elapsed.TotalSeconds -lt 60 -and $marker -and !(Test-Path -LiteralPath $marker)) {
                            if (![GuxAuditInput]::IsWindow([IntPtr]$handle)) { break }
                            # NVDA+Tab and NVDA+B for the Reader: while an elevated window has the foreground, keys from the
                            # Reader's ordinary token never reach NVDA (run 1 logged none), so this elevated worker presses
                            # them, and only while the helper's dialog is the foreground window.
                            foreach ($key in 'tab', 'b', 'walk') {
                                if ($pressed.Contains($key) -or !(Test-Path -LiteralPath (Join-Path $RunFolder "press-$count-$key"))) { continue }
                                if ([GuxAuditInput]::GetForegroundWindow() -eq [IntPtr]$handle) {
                                    [void][GuxAuditInput]::Release()
                                    if ($key -eq 'walk') {
                                        # Tab only (never Enter or Space): focus visits each command link and the steps
                                        # button, and NVDA speaks each as a keyboard user would hear it.
                                        $visited = @()
                                        for ($i = 0; $i -lt 4 -and [GuxAuditInput]::GetForegroundWindow() -eq [IntPtr]$handle; $i++) {
                                            [GuxAuditInput]::Key(0x09, $false)
                                            Start-Sleep -Milliseconds 1300
                                            $visited += try { $AE::FocusedElement.Current.Name } catch { '?' }
                                        }
                                        $pressed[$key] = "Tab x$($visited.Count): $($visited -join ' | ')"
                                    } else {
                                        [GuxAuditInput]::Nvda($(if ($key -eq 'tab') { 0x09 } else { 0x42 }))
                                        $pressed[$key] = [DateTimeOffset]::Now.ToString('o')
                                    }
                                    [void][GuxAuditInput]::Release()
                                } else { $pressed[$key] = 'not pressed: the dialog was not the foreground window' }
                                Set-Content -LiteralPath (Join-Path $RunFolder "pressed-$count-$key") -Value $pressed[$key] -Encoding ASCII
                            }
                            Start-Sleep -Milliseconds 100
                        }
                        $record.NvdaKeys = $pressed
                        $record.WaitedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 1)
                        $record.Released = [bool]($marker -and (Test-Path -LiteralPath $marker))
                        $record.ForegroundBeforeAnswer = [GuxAuditInput]::WindowProcess([GuxAuditInput]::GetForegroundWindow())
                        $record.TextsBeforeAnswer = Describe $window
                        switch ($record.Instruction) {
                            'approve' { [void][GuxAuditInput]::SendMessage([IntPtr]$handle, 0x0466, [IntPtr]100, [IntPtr]::Zero); $record.Action = 'TDM_CLICK_BUTTON 100' }
                            'cancel' { [void][GuxAuditInput]::SendMessage([IntPtr]$handle, 0x0466, [IntPtr]2, [IntPtr]::Zero); $record.Action = 'TDM_CLICK_BUTTON IDCANCEL' }
                            'touch-approve' {
                                # The reviewed object's metadata changes while consent is open: toggling Archive moves its change time.
                                $attributes = [IO.File]::GetAttributes($target)
                                [IO.File]::SetAttributes($target, ($attributes -bxor [IO.FileAttributes]::Archive))
                                $record.TouchedAttributes = "$attributes -> $([IO.File]::GetAttributes($target))"
                                Start-Sleep -Milliseconds 300
                                [void][GuxAuditInput]::SendMessage([IntPtr]$handle, 0x0466, [IntPtr]100, [IntPtr]::Zero)
                                $record.Action = 'changed attributes, then TDM_CLICK_BUTTON 100'
                            }
                            default { $record.Action = 'none; left open' }
                        }
                        $close = [Diagnostics.Stopwatch]::StartNew()
                        while ([GuxAuditInput]::IsWindow([IntPtr]$handle) -and $close.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 200 }
                        $record.Closed = ![GuxAuditInput]::IsWindow([IntPtr]$handle)
                        Add-Content -LiteralPath $outcomes -Value ($record | ConvertTo-Json -Depth 6 -Compress) -Encoding UTF8
                        Log "window $count action '$($record.Action)' closed=$($record.Closed)"
                    }
                }
                foreach ($key in @($seen.Keys)) { if (![GuxAuditInput]::IsWindow([IntPtr]$key)) { $seen.Remove($key) } }
            } catch { Log "loop error: $($_.Exception.Message)" }
            Start-Sleep -Milliseconds 250
        }
    } finally {
        $released = [GuxAuditInput]::Release()
        Log "stop; released keys: '$released'"
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Reader: the ordinary-token journey that NVDA hears.
# ---------------------------------------------------------------------------------------------------------------------
function Run-Reader {
    [void][GuxAuditInput]::SetThreadDpiAwarenessContext([IntPtr](-4))
    $run = [IO.Path]::GetFullPath($RunFolder)
    $validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation')).TrimEnd('\') + '\'
    $report = Join-Path $run 'reader.json'
    $log = Join-Path $run 'nvda.log'
    $app = [IO.Path]::GetFullPath($AppPath)
    $target = Join-Path $Fixture $fileName
    $script:result = [ordered]@{ Passed = $false; Stage = 'starting'; ReaderLevel = $ReaderLevel; RecordOnly = [bool]$RecordOnly
        Computer = $env:COMPUTERNAME; User = $env:USERNAME; Session = [Diagnostics.Process]::GetCurrentProcess().SessionId
        Integrity = (Integrity); App = $app; Target = $target; Journeys = $Journeys; ReleasedAtStart = ''
        Checks = New-Object System.Collections.Generic.List[object]; Helpers = New-Object System.Collections.Generic.List[object]
        Findings = New-Object System.Collections.Generic.List[string]; Error = $null }
    if (!(Is-VMware) -or $script:result.Session -eq 0 -or $script:result.Integrity -ne 'medium' -or
        !$run.StartsWith($validation, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $report) -or
        !(Test-Path -LiteralPath $app -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path (Split-Path $app) "$helperName.exe") -PathType Leaf) -or
        !(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-Process -Name FileCat, $helperName -ErrorAction SilentlyContinue)) {
        $script:result.Error = 'Run the Reader at the ordinary level in the signed-in VMware guest session, with installed FileCat idle and a fresh run folder.'
        if ($run.StartsWith($validation, [StringComparison]::OrdinalIgnoreCase) -and !(Test-Path -LiteralPath $report)) { Write-Json $script:result $report }
        throw $script:result.Error
    }
    $script:offset = 0L
    $script:app = $null
    $script:helperCount = 0
    function TakeSpeech {
        if (!(Test-Path -LiteralPath $log)) { return @() }
        $stream = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
        try {
            [void]$stream.Seek($script:offset, 'Begin')
            $buffer = New-Object byte[] ([int]($stream.Length - $script:offset))
            $read = $stream.Read($buffer, 0, $buffer.Length)
            if ($read -le 0) { return @() }
            $last = [Array]::LastIndexOf($buffer, [byte]10, $read - 1)
            if ($last -lt 0) { return @() }
            $script:offset += $last + 1
            $text = [Text.Encoding]::UTF8.GetString($buffer, 0, $last + 1)
            if ($text.Contains([string][char]0xFFFD)) { $text = [Text.Encoding]::GetEncoding(1252).GetString($buffer, 0, $last + 1) }
            return @($text -split "`r?`n" | Where-Object { $_.StartsWith('Speaking [') } | ForEach-Object { $_.Substring(9).Trim() })
        } finally { $stream.Dispose() }
    }
    function Plain([object[]]$heard) { ((@($heard) | ForEach-Object { "$_" }) -join ' ').Replace('\\', '\') }
    function Main { $AE::FromHandle([IntPtr]$script:app.MainWindowHandle) }
    function All($root) { @($root.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) }
    function Named([string]$name, $type = $null, [switch]$Prefix) {
        @(All (Main) | Where-Object {
            ($(if ($Prefix) { $_.Current.Name.StartsWith($name) } else { $_.Current.Name -eq $name })) -and
            (!$type -or $_.Current.ControlType -eq $type) -and !$_.Current.IsOffscreen })[0]
    }
    function Await([scriptblock]$check, [string]$label, [int]$seconds = 20) {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt $seconds) {
            try { $answer = & $check; if ($null -ne $answer -and $answer -ne $false) { return $answer } }
            catch [System.Windows.Automation.ElementNotAvailableException] {}
            catch [Runtime.InteropServices.COMException] {}
            catch [InvalidOperationException] {}
            Start-Sleep -Milliseconds 150
        }
        throw "Timed out: $label"
    }
    function FocusName { try { $AE::FocusedElement.Current.Name } catch { '' } }
    function Focus {
        try {
            $f = $AE::FocusedElement
            [ordered]@{ Name = $f.Current.Name; Type = $f.Current.ControlType.ProgrammaticName.Replace('ControlType.', '')
                Process = $f.Current.ProcessId; Enabled = $f.Current.IsEnabled; Help = $f.Current.HelpText
                ForegroundProcess = [GuxAuditInput]::ForegroundProcess() }
        } catch { [ordered]@{ Error = $_.Exception.Message; ForegroundProcess = [GuxAuditInput]::ForegroundProcess() } }
    }
    function Record([string]$step, [object[]]$heard, [string[]]$expected, [switch]$Soft) {
        $combined = Plain $heard
        $missing = @($expected | Where-Object { $_ -and !$combined.Contains($_) })
        $script:result.Checks.Add([ordered]@{ Step = $step; Expected = $expected; Missing = $missing; Spoken = @($heard); Focus = (Focus) })
        if ($missing.Count) {
            $script:result.Findings.Add("$step : NVDA did not speak $($missing -join ' | ')")
            if (!$Soft -and !$RecordOnly) { throw "NVDA did not speak $($missing -join ', ') at '$step': $combined" }
        }
    }
    function Collect([object[]]$heard, [string[]]$expected, [double]$seconds) {
        $heard = @($heard)
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt $seconds) {
            $heard += @(TakeSpeech)
            $combined = Plain $heard
            if ($expected -and @($expected | Where-Object { !$combined.Contains($_) }).Count -eq 0) {
                Start-Sleep -Milliseconds 500; $heard += @(TakeSpeech); break
            }
            Start-Sleep -Milliseconds 200
        }
        , $heard
    }
    function Hear([string]$step, [string[]]$expected, [double]$seconds = 8, [switch]$Soft) {
        $heard = Collect @() $expected $seconds
        Record $step $heard $expected -Soft:$Soft
        Plain $heard
    }
    function Listen([string]$step, [double]$seconds) {
        $heard = Collect @() @() $seconds
        Record $step $heard @()
        Plain $heard
    }
    function Foreground {
        $handle = [IntPtr]$script:app.MainWindowHandle
        [void][GuxAuditInput]::Release()
        for ($i = 0; $i -lt 6; $i++) {
            if ([GuxAuditInput]::GetForegroundWindow() -eq $handle) { return }
            [GuxAuditInput]::Key(0x10, $false)   # the foreground rule admits the process that produced the last input
            [void][GuxAuditInput]::ShowWindow($handle, 9)
            [void][GuxAuditInput]::SetForegroundWindow($handle)
            Start-Sleep -Milliseconds 250
        }
        for ($i = 0; $i -lt 3 -and [GuxAuditInput]::GetForegroundWindow() -ne $handle; $i++) {
            [void][GuxAuditInput]::ClickTitle($handle); Start-Sleep -Milliseconds 400
        }
        [void][GuxAuditInput]::Release()
        if ([GuxAuditInput]::GetForegroundWindow() -ne $handle) { throw 'The FileCat window is not foreground; refusing keys.' }
    }
    function Key([byte]$key, [bool]$extended = $false) {
        if ([GuxAuditInput]::ForegroundProcess() -ne $script:app.Id) { throw "Foreground is process $([GuxAuditInput]::ForegroundProcess()), not FileCat; refusing keys." }
        [GuxAuditInput]::Key($key, $extended); Start-Sleep -Milliseconds 300
    }
    function Chord([byte[]]$keys) {
        if ([GuxAuditInput]::ForegroundProcess() -ne $script:app.Id) { throw 'Foreground is not FileCat; refusing a chord.' }
        [GuxAuditInput]::Chord($keys); Start-Sleep -Milliseconds 450
    }
    function Type-Text([string]$text) {
        foreach ($ch in $text.ToCharArray()) {
            if ($ch -eq '-') { Key 0xBD; continue }
            $vk = [byte][char]::ToUpperInvariant($ch)
            if ([char]::IsUpper($ch)) { Chord @(0x10, $vk) } else { Key $vk }
        }
    }
    function Press([string]$step, [byte]$key, [string[]]$expected, [double]$seconds = 6, [switch]$Soft, [switch]$Extended) {
        [void](TakeSpeech)
        Key $key ([bool]$Extended)
        $heard = Collect @() $expected $seconds
        Record $step $heard $expected -Soft:$Soft
    }
    function Tab-To([string]$pattern, [string]$step, [string[]]$expected = @(), [int]$limit = 40) {
        # Tab until keyboard focus is on a control whose name matches; every stop's speech is kept, so the order is
        # evidence. The stop that reaches the control is checked against the expected phrases.
        [void](TakeSpeech)
        for ($i = 0; $i -lt $limit; $i++) {
            Key 0x09
            $heard = Collect @() @() 0.45
            if ((FocusName) -like $pattern) {
                $heard = Collect $heard $expected 3
                Record "$step (tab $i)" $heard $expected
                return $i
            }
            $script:result.Checks.Add([ordered]@{ Step = "$step tab $i"; Spoken = @($heard); Focus = (Focus) })
        }
        throw "Tab never reached '$pattern' at '$step'."
    }
    function Open-Inspector([string]$journey) {
        Foreground
        $list = @(All (Main) | Where-Object { $_.Current.ClassName -eq 'FileList' })[0]
        if ($list) { $list.SetFocus(); Start-Sleep -Milliseconds 300 }
        Foreground
        Key 0x24 $true                    # Home: the parent entry
        Press "$journey : fixture row" 0x28 @($fileName) 6 -Extended -Soft
        if ((FocusName) -ne $fileName) { throw "The fixture row is not focused (focus: $(FocusName))." }
        Chord @(0x11, 0x10, 0x50)        # Ctrl+Shift+P
        $search = Await { Named 'Search commands' $CT::Edit } 'command search'
        Type-Text 'permissions and ownership'
        Start-Sleep -Milliseconds 900
        [void](Await { Named 'Permissions and ownership' $CT::ListItem -Prefix } 'the palette entry')
        if (!$search.Current.HasKeyboardFocus) { throw 'The command search lost keyboard focus before Enter.' }
        Press "$journey : inspector opens" 0x0D @('Permissions and ownership dialog') 10
        [void](Tab-To ('Inspect with authorization' + $ellipsis) "$journey : inspector" @('Inspect with authorization', 'button'))
        # The waiting state: its card, its focused Cancel, and the sentence that says what happens next.
        Press "$journey : waiting state" 0x0D @('Inspect with authorization dialog', 'Cancel', 'button', 'Windows asks for administrator approval') 6
    }
    function Press-Nvda([int]$n, [string]$key, [int]$seconds = 8) {
        # The elevated consent worker presses keys while the helper's dialog is the foreground window.
        $done = Join-Path $run "pressed-$n-$key"
        Set-Content -LiteralPath (Join-Path $run "press-$n-$key") -Value $key -Encoding ASCII
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (!(Test-Path -LiteralPath $done) -and $clock.Elapsed.TotalSeconds -lt $seconds) { Start-Sleep -Milliseconds 100 }
        if (Test-Path -LiteralPath $done) { (Get-Content -Raw -LiteralPath $done).Trim() } else { 'no answer from the consent worker' }
    }
    function Hear-Helper([string]$kind) {
        # The elevated helper's task dialog: what NVDA says by itself, then NVDA+Tab, NVDA+B and a Tab walk, before the
        # worker answers. An ordinary-level NVDA never sees those keys: NVDA+Tab arrives in the dialog as a plain Tab.
        $script:helperCount++
        $n = $script:helperCount
        $entry = [ordered]@{ Index = $n; Kind = $kind }
        $process = Await { @(Get-Process -Name $helperName -ErrorAction SilentlyContinue | Where-Object {
            $_.MainWindowHandle -ne [IntPtr]::Zero -and $_.MainWindowTitle -like "*$kind security metadata*" })[0] } "the helper's $kind consent" 90
        $handle = $process.MainWindowHandle
        $entry.Title = $process.MainWindowTitle
        Start-Sleep -Milliseconds 400
        $entry.ForegroundIsHelper = ([GuxAuditInput]::ForegroundProcess() -eq [uint32]$process.Id)
        $entry.Automatic = Listen "helper $n ($kind): automatic speech" 6
        $entry.NvdaTabPressed = Press-Nvda $n 'tab'   # NVDA+Tab: report the focus
        $entry.NvdaTab = Listen "helper $n ($kind): NVDA+Tab" 4
        $entry.NvdaBPressed = Press-Nvda $n 'b'       # NVDA+B: read the foreground window
        $entry.NvdaB = Listen "helper $n ($kind): NVDA+B" 14
        $entry.TabWalk = Press-Nvda $n 'walk' 15      # Tab through the command links and the steps button
        $entry.TabWalkSpeech = Listen "helper $n ($kind): Tab walk" 2
        $walk = $script:result.Checks[$script:result.Checks.Count - 1]
        $walk.TabWalk = $entry.TabWalk
        # Everything the dialog shows: instruction, requester, the steps, the footnote, both command links and the steps
        # button. Only an NVDA that can read an elevated window hears it, so an ordinary-level NVDA only records it.
        $shows = if ($kind -eq 'inspect') {
            @('Inspect FileCat security metadata', 'Requester ', $fileName, 'Read owner, group, discretionary ACL (DACL), inheritance, system audit ACL (SACL) on this file.',
                'This approval covers the listed security metadata for one object.', 'Inspect security', 'Cancel', 'Hide the steps')
        } else {
            @('Apply a reviewed FileCat security change', 'Requester ', $fileName, 'Audit entry', 'Before no entry', "After Audit success and failure $AuditSid",
                'Windows audit policy determines whether events are recorded.', 'Apply security change', 'Cancel', 'Hide the steps')
        }
        Record "helper $n ($kind): dialog heard" @($entry.Automatic, $entry.NvdaTab, $entry.NvdaB, $entry.TabWalkSpeech) $shows -Soft:($ReaderLevel -ne 'High')
        Set-Content -LiteralPath (Join-Path $run "release-$n") -Value 'heard' -Encoding ASCII
        $closed = [Diagnostics.Stopwatch]::StartNew()
        while ([GuxAuditInput]::IsWindow($handle) -and $closed.Elapsed.TotalSeconds -lt 45) { Start-Sleep -Milliseconds 200 }
        $entry.ClosedAfterSeconds = [Math]::Round($closed.Elapsed.TotalSeconds, 1)
        $entry.Closed = ![GuxAuditInput]::IsWindow($handle)
        Start-Sleep -Milliseconds 400
        $entry.ForegroundAfterClose = [GuxAuditInput]::ForegroundProcess()
        $entry.FileCatForegroundAfterClose = ($entry.ForegroundAfterClose -eq [uint32]$script:app.Id)
        $script:result.Helpers.Add($entry)
        if (!$entry.FileCatForegroundAfterClose) {
            $script:result.Findings.Add("helper $n ($kind): after it closed the foreground was process $($entry.ForegroundAfterClose), not FileCat")
        }
    }

    $script:result.ReleasedAtStart = [GuxAuditInput]::Release()
    try {
        $script:result.Stage = 'reader attach'
        [void](Await { (Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch 'NVDA initialized' -Quiet) } 'NVDA initialization' 60)
        $script:result.Stage = 'launch'
        $data = Join-Path $run 'data'
        New-Item -ItemType Directory -Force -Path $data | Out-Null
        $env:FILECAT_QUALIFY_WINDOWS_PERMISSIONS_BROKER = '1'
        $script:app = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru -WindowStyle Normal -ArgumentList @(
            '--new-instance', '--data', ('"{0}"' -f $data), '--left', ('"{0}"' -f $Fixture), '--right', ('"{0}"' -f $data))
        [void](Await { $script:app.Refresh(); $script:app.MainWindowHandle -ne [IntPtr]::Zero } 'the FileCat window' 60)
        $script:result.AppPid = $script:app.Id
        $script:result.WindowTitle = $script:app.MainWindowTitle
        $width = [Math]::Min(1400, [GuxAuditInput]::GetSystemMetrics(0) - 80); $height = [Math]::Min(950, [GuxAuditInput]::GetSystemMetrics(1) - 100)
        [void][GuxAuditInput]::ShowWindow([IntPtr]$script:app.MainWindowHandle, 9)
        [void][GuxAuditInput]::MoveWindow([IntPtr]$script:app.MainWindowHandle, 40, 20, $width, $height, $true)
        Start-Sleep -Seconds 3
        Foreground
        Start-Sleep -Milliseconds 800
        [void](TakeSpeech)

        foreach ($journey in $Journeys) {
            $script:result.Stage = "$journey : inspector"
            Open-Inspector $journey
            switch ($journey) {
                'approve' {
                    $script:result.Stage = 'approve : helper inspection consent'
                    Hear-Helper 'inspect'
                    $script:result.Stage = 'approve : authorized panel'
                    [void](Await { Named 'Authorized permissions and ownership dialog' } 'the authorized panel' 60)
                    if ([GuxAuditInput]::ForegroundProcess() -ne $script:app.Id) { Foreground }
                    [void](Hear 'approve : authorized panel opens' @('Authorized permissions and ownership dialog') 8)
                    $script:result.Stage = 'approve : authorized panel Tab cycle'
                    $cycle = @()
                    for ($i = 0; $i -lt 40; $i++) {
                        [GuxAuditInput]::Key(0x09, $false)
                        $spoken = Collect @() @() 0.5
                        $cycle += $spoken
                        $script:result.Checks.Add([ordered]@{ Step = "approve : authorized tab $i"; Spoken = @($spoken); Focus = (Focus) })
                        if ($i -gt 4 -and (FocusName) -eq 'Close' -and (Plain $cycle).Contains('Edit audit rules')) { break }
                    }
                    $heardCycle = Plain $cycle
                    $script:result.AuthorizedCycleMissing = @(foreach ($part in $fileName, 'Auditing', 'S-1-1-0', 'Edit audit rules') {
                        if (!$heardCycle.Contains($part)) { $part } })
                    foreach ($part in $script:result.AuthorizedCycleMissing) { $script:result.Findings.Add("authorized panel Tab cycle never spoke '$part'") }
                    # Every Auditing row: the first by Tab, the next by Down.
                    [void](Tab-To '*S-1-1-0*0x00000080*' 'approve : Auditing list' @('Auditing', 'S-1-1-0'))
                    Press 'approve : Auditing second row' 0x28 @('Integrity label') 5 -Extended
                    $script:result.Stage = 'approve : audit editor'
                    [void](Tab-To ('Edit audit rules' + $ellipsis) 'approve : to the audit action' @('Edit audit rules', 'button'))
                    Press 'approve : audit editor opens' 0x0D @('Edit audit rules dialog', 'Ordered Windows audit entries', 'list', 'S-1-1-0') 10
                    Press 'approve : audit entry 2' 0x28 @('Integrity label') 5 -Extended
                    Press 'approve : audit entry 1' 0x26 @('S-1-1-0') 5 -Extended
                    [void](Tab-To 'Add an explicit audit entry' 'approve : to the add check box' @('Add an explicit audit entry', 'check box', 'not checked') 4)
                    Press 'approve : add toggled' 0x20 @('checked', 'Enter a hexadecimal mask with at least one supported file right.') 6 -Soft
                    $script:result.Stage = 'approve : SID'
                    [void](Tab-To 'Audit account SID*' 'approve : to the SID box' @('Audit account SID', 'edit') 6)
                    Type-Text $AuditSid
                    [void](Listen 'approve : SID typed' 2)
                    [void](Tab-To 'Audit rights mask*' 'approve : to the mask box' @('Audit rights mask (hexadecimal)', 'edit') 3)
                    Type-Text $AuditMask
                    [void](Listen 'approve : mask typed' 2.5)
                    [void](Tab-To 'Audit successful access' 'approve : to the success check box' @('Audit successful access', 'check box', 'checked') 3)
                    [void](Tab-To 'Audit failed access' 'approve : to the failure check box' @('Audit failed access', 'check box', 'not checked') 3)
                    Press 'approve : failure toggled' 0x20 @('checked') 5
                    $script:result.Stage = 'approve : review lines'
                    [void](Tab-To '*audit entry*' 'approve : to the review lines' @('Reviewed changes', 'list', 'Add audit entry', $AuditSid, 'Success and failure') 4)
                    Press 'approve : second review line' 0x28 @('The new entry is placed after the explicit audit entries') 5 -Extended
                    $script:result.ReviewRows = @(All (Main) | Where-Object { $_.Current.ControlType -eq $CT::ListItem -and
                        ($_.Current.Name -like '*audit entry*' -or $_.Current.Name -like 'The new entry*') } | ForEach-Object { $_.Current.Name })
                    [void](Tab-To 'Apply with authorization' 'approve : to Apply' @('Apply with authorization', 'button') 8)
                    $apply = Named 'Apply with authorization' $CT::Button
                    if (!$apply -or !$apply.Current.IsEnabled) { throw 'Apply with authorization is not available for the reviewed draft.' }
                    # The running action disables Apply; focus must stay in the dialog rather than fall to the window. Its
                    # status ("Checking the reviewed object", then "Waiting for administrator approval") is a live region:
                    # NVDA drops it once the helper's dialog has the foreground, so it is only recorded.
                    Press 'approve : apply started' 0x0D @('Edit audit rules dialog') 8
                    $started = Plain $script:result.Checks[$script:result.Checks.Count - 1].Spoken
                    if (!$started.Contains('Checking the reviewed object') -and !$started.Contains('Waiting for administrator approval')) {
                        $script:result.Findings.Add('approve : apply started : no status was spoken before the helper dialog')
                    }
                    $script:result.FocusWhileApplying = Focus
                    $script:result.Stage = 'approve : helper change consent'
                    Hear-Helper 'change'
                    if ([GuxAuditInput]::ForegroundProcess() -ne $script:app.Id) { Foreground }
                    $outcome = Await { $t = Named 'Verified changed' $CT::Text -Prefix; if ($t) { $t.Current.Name } else {
                        foreach ($needle in 'Failed before change', 'Reviewed state changed', 'Outcome uncertain', 'Already matched', 'Partially applied') {
                            $other = Named $needle $CT::Text -Prefix; if ($other) { return $other.Current.Name } } } } 'the apply outcome' 120
                    $script:result.ApplyOutcome = $outcome
                    [void](Hear 'approve : outcome spoken' @('Verified changed') 10)
                    if (!$outcome.StartsWith('Verified changed')) { throw "The audit change ended as '$outcome'." }
                    $script:result.FocusAfterOutcome = Focus
                    if ($script:result.FocusAfterOutcome.Name -ne 'Edit audit rules dialog') {
                        $script:result.Findings.Add("approve : after the outcome focus was '$($script:result.FocusAfterOutcome.Name)', not the editor dialog")
                        if (!$RecordOnly) { throw "After the outcome focus was '$($script:result.FocusAfterOutcome.Name)'." }
                    }
                    Press 'approve : editor closed by Escape' 0x1B @() 3
                    [void](Await { $null -eq (Named 'Ordered Windows audit entries') } 'the editor to close' 15)
                }
                'cancel' {
                    $script:result.Stage = 'cancel : helper inspection consent'
                    Hear-Helper 'inspect'
                    [void](Await { $null -eq (Named 'Windows asks for administrator approval to read this item' -Prefix) } 'the waiting state to close' 30)
                    Start-Sleep -Milliseconds 800
                    if ([GuxAuditInput]::ForegroundProcess() -ne $script:app.Id) { Foreground }
                    [void](Listen 'cancel : after the cancelled consent' 4)
                    $script:result.CancelShowed = @(foreach ($name in 'Authorized inspection unavailable dialog', 'Authorized permissions and ownership dialog') {
                        if (Named $name) { $name } })
                }
                'unavailable' {
                    $script:result.Stage = 'unavailable : helper inspection consent'
                    Hear-Helper 'inspect'
                    [void](Await { Named 'Authorized inspection unavailable dialog' } 'the unavailable alert' 60)
                    if ([GuxAuditInput]::ForegroundProcess() -ne $script:app.Id) { Foreground }
                    $message = @(All (Main) | Where-Object { $_.Current.ControlType -eq $CT::Text -and $_.Current.Name -like '*changed*' } | ForEach-Object { $_.Current.Name })
                    $script:result.UnavailableMessage = $message
                    $expected = @('Authorized inspection unavailable dialog', 'OK', 'button') + @($message | Select-Object -First 1)
                    [void](Hear 'unavailable : alert opens' $expected 8)
                    Press 'unavailable : alert closed by Enter' 0x0D @() 3
                    [void](Await { $null -eq (Named 'Authorized inspection unavailable dialog') } 'the alert to close' 15)
                }
            }
        }
        $script:result.Passed = $true
        $script:result.Stage = 'done'
    } catch {
        $script:result.Error = $_.Exception.Message
        try { $script:result.FailureFocus = Focus } catch {}
    } finally {
        [void][GuxAuditInput]::Release()
        if ($script:app -and !$script:app.HasExited) {
            [void]$script:app.CloseMainWindow()
            if (!$script:app.WaitForExit(8000)) { $script:app.Kill(); [void]$script:app.WaitForExit(5000) }
        }
        $script:result.ReleasedAtEnd = [GuxAuditInput]::Release()
        Write-Json $script:result $report
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Launch: elevated orchestration of fixture, NVDA, the consent worker and the Reader.
# ---------------------------------------------------------------------------------------------------------------------
function Native-Sacl([string]$path) {
    $acl = Get-Acl -LiteralPath $path -Audit
    $raw = [Security.AccessControl.RawSecurityDescriptor]::new($acl.GetSecurityDescriptorBinaryForm(), 0)
    $entries = @()
    if ($raw.SystemAcl) {
        foreach ($ace in $raw.SystemAcl) {
            $known = $ace -is [Security.AccessControl.KnownAce]
            $entries += [ordered]@{ Type = "$($ace.AceType)"; Flags = ('0x{0:X2}' -f [int]$ace.AceFlags)
                Sid = $(if ($known) { $ace.SecurityIdentifier.Value } else { $null })
                Mask = $(if ($known) { '0x{0:X8}' -f $ace.AccessMask } else { $null }) }
        }
    }
    [ordered]@{ Sddl = $acl.Sddl; Control = ('0x{0:X4}' -f [int]$raw.ControlFlags); Owner = $raw.Owner.Value; Sacl = $entries
        Label = ((& icacls.exe $path) -match 'Mandatory Label') -join ' '; ContentSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
        Attributes = "$([IO.File]::GetAttributes($path))" }
}

function Run-Launch {
    $report = [IO.Path]::GetFullPath($ReportPath)
    $run = Split-Path $report
    $validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation')).TrimEnd('\') + '\'
    $nvda = [IO.Path]::GetFullPath($NvdaPath)
    $app = [IO.Path]::GetFullPath($AppPath)
    $fixture = [IO.Path]::GetFullPath($Fixture).TrimEnd('\')
    $control = [IO.Path]::GetFullPath($Control).TrimEnd('\')
    $self = $PSCommandPath
    $user = [Security.Principal.WindowsIdentity]::GetCurrent()
    $explorer = @(Get-Process -Name explorer -IncludeUserName -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -ne 0 -and $_.UserName -eq $user.Name })
    $others = Others
    $result = [ordered]@{ Passed = $false; Stage = 'preflight'; Started = [DateTimeOffset]::Now.ToString('o'); Computer = $env:COMPUTERNAME
        User = $user.Name; ReaderLevel = $ReaderLevel; Journeys = $Journeys; RecordOnly = [bool]$RecordOnly; Others = $others
        Script = $self; ScriptSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $self).Hash; Error = $null }
    if (!(Is-VMware) -or (Integrity) -ne 'high' -or $explorer.Count -eq 0 -or $others.Count -or
        !$report.StartsWith($validation, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $run) -or
        !$nvda.StartsWith($validation, [StringComparison]::OrdinalIgnoreCase) -or (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or
        !(Test-Path -LiteralPath $app -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path (Split-Path $app) "$helperName.exe") -PathType Leaf) -or
        !$fixture.StartsWith('C:\FileCatValidation\', [StringComparison]::OrdinalIgnoreCase) -or
        !$control.StartsWith('C:\FileCatValidation\', [StringComparison]::OrdinalIgnoreCase) -or
        !$self.StartsWith('C:\FileCatValidation\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Launch elevated in an idle VMware guest with the user signed in, installed FileCat, a signed portable NVDA and a fresh run folder (others: $($others -join ', '))."
    }
    New-Item -ItemType Directory -Path $run | Out-Null
    $id = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $started = Get-Date
    $script:launchTasks = @()
    $shell = Join-Path $PSHOME 'powershell.exe'
    $nvdaLevel = if ($ReaderLevel -eq 'High') { 'Highest' } else { 'Limited' }
    function Start-Task([string]$name, [string]$execute, [string]$arguments, [string]$level) {
        $action = New-ScheduledTaskAction -Execute $execute -Argument $arguments
        $principal = New-ScheduledTaskPrincipal -UserId $user.Name -LogonType Interactive -RunLevel $level
        $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings -Force | Out-Null
        $script:launchTasks += $name
        Start-ScheduledTask -TaskName $name
    }
    try {
        $result.App = [ordered]@{ Path = $app; Version = (Get-Item -LiteralPath $app).VersionInfo.ProductVersion
            FileCatSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $app).Hash
            HelperSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path (Split-Path $app) "$helperName.exe")).Hash }
        $result.Nvda = [ordered]@{ Path = $nvda; Version = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion
            Signer = (Get-AuthenticodeSignature -LiteralPath $nvda).SignerCertificate.Subject }
        $result.Uac = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' |
            Select-Object EnableLUA, ConsentPromptBehaviorAdmin, PromptOnSecureDesktop

        $result.Stage = 'fixture'
        if (Test-Path -LiteralPath $fixture) {
            & takeown.exe /f $fixture /r /d y /a | Out-Null
            & icacls.exe $fixture /reset /t /c /q | Out-Null
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
        New-Item -ItemType Directory -Path $fixture | Out-Null
        $file = Join-Path $fixture $fileName
        'FileCat G-UX NVDA audit and consent witness.' | Set-Content -LiteralPath $file -Encoding ascii
        $admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
        $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
        $acl = [Security.AccessControl.FileSecurity]::new()
        $acl.SetOwner($admins)
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sid in $system, $admins) { $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'Allow')) }
        # The signed-in user's ordinary token (Administrators deny-only) can read and inspect it, not re-permission it.
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($user.User, 'ReadAndExecute', 'Allow'))
        $acl.SetAuditRuleProtection($true, $false)
        $acl.AddAuditRule([Security.AccessControl.FileSystemAuditRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-1-0'), 'ReadAttributes', 'Success,Failure'))
        Set-Acl -LiteralPath $file -AclObject $acl
        & icacls.exe $file /setintegritylevel M | Out-Null
        if ($LASTEXITCODE) { throw 'The Medium integrity label could not be set.' }
        $result.FixtureBefore = Native-Sacl $file

        $result.Stage = 'control'
        if (Test-Path -LiteralPath $control) { Remove-Item -LiteralPath $control -Recurse -Force }
        New-Item -ItemType Directory -Path $control | Out-Null
        $protected = [Security.AccessControl.DirectorySecurity]::new()
        $protected.SetAccessRuleProtection($true, $false)
        foreach ($sid in $system, $admins) {
            $protected.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        }
        [IO.Directory]::SetAccessControl($control, $protected)
        $queue = @(foreach ($journey in $Journeys) { switch ($journey) { 'approve' { 'approve'; 'approve' } 'cancel' { 'cancel' } 'unavailable' { 'touch-approve' } } })
        ConvertTo-Json -InputObject @($queue) | Set-Content -LiteralPath (Join-Path $control 'queue.json') -Encoding UTF8
        $result.Queue = $queue

        $result.Stage = 'nvda'
        $config = Join-Path $run 'nvda-config'
        New-Item -ItemType Directory -Path $config | Out-Null
        [IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), "[general]`r`nshowWelcomeDialogAtStartup = False`r`n[update]`r`nautoCheck = False`r`nstartupNotification = False`r`nallowUsageStats = False`r`naskedAllowUsageStats = True`r`n")
        $log = Join-Path $run 'nvda.log'
        Start-Task "FcGuxNvda-$id" $nvda ('--log-file="{0}" --log-level=12 --config-path="{1}" --disable-addons' -f $log, $config) $nvdaLevel
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (!((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch 'NVDA initialized' -Quiet))) {
            if ($clock.Elapsed.TotalSeconds -gt 60) { throw 'NVDA did not initialize within 60 seconds.' }
            Start-Sleep -Milliseconds 300
        }
        $result.NvdaProcesses = @(Get-Process -Name nvda -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Id) session $($_.SessionId) $($_.Path)" })

        $result.Stage = 'consent worker'
        Start-Task "FcGuxConsent-$id" $shell ('-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -Role Consent -Control "{1}" -Fixture "{2}" -RunFolder "{3}"' -f $self, $control, $fixture, $run) 'Highest'
        $clock.Restart()
        while (!(Test-Path -LiteralPath (Join-Path $control 'consent-worker.log'))) {
            if ($clock.Elapsed.TotalSeconds -gt 30) { throw 'The consent worker did not start.' }
            Start-Sleep -Milliseconds 300
        }

        $result.Stage = 'reader'
        $readerArgs = '-NoProfile -NonInteractive -WindowStyle Hidden -Sta -ExecutionPolicy Bypass -File "{0}" -Role Reader -AppPath "{1}" -Fixture "{2}" -RunFolder "{3}" -ReaderLevel {4} -Journeys {5} -AuditSid {6} -AuditMask {7}' -f
            $self, $app, $fixture, $run, $ReaderLevel, ($Journeys -join ','), $AuditSid, $AuditMask
        if ($RecordOnly) { $readerArgs += ' -RecordOnly' }
        Start-Task "FcGuxReader-$id" $shell $readerArgs 'Limited'
        Start-Sleep -Seconds 3
        $clock.Restart()
        while ((Get-ScheduledTask -TaskName "FcGuxReader-$id").State -eq 'Running') {
            if ($clock.Elapsed.TotalMinutes -gt 20) { Stop-ScheduledTask -TaskName "FcGuxReader-$id"; throw 'The Reader timed out.' }
            Start-Sleep -Seconds 1
        }
        $result.ReaderTaskResult = (Get-ScheduledTaskInfo -TaskName "FcGuxReader-$id").LastTaskResult
        $readerReport = Join-Path $run 'reader.json'
        $result.ReaderPassed = if (Test-Path -LiteralPath $readerReport) { [bool](Get-Content -Raw -LiteralPath $readerReport | ConvertFrom-Json).Passed } else { $false }
        $result.Stage = 'done'
    } catch {
        $result.Error = $_.Exception.Message
    } finally {
        try {
            Set-Content -LiteralPath (Join-Path $control 'stop') -Value 'stop' -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            if (Test-Path -LiteralPath (Join-Path $control 'consent-outcomes.jsonl')) {
                Copy-Item -LiteralPath (Join-Path $control 'consent-outcomes.jsonl') -Destination $run
                $result.Consent = @(Get-Content -LiteralPath (Join-Path $control 'consent-outcomes.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
            }
            if (Test-Path -LiteralPath (Join-Path $control 'consent-worker.log')) { Copy-Item -LiteralPath (Join-Path $control 'consent-worker.log') -Destination $run }
            # NVDA quits through its own -q at the level it runs; only this run's reader, app and helper processes are stopped.
            if (Get-Process -Name nvda -ErrorAction SilentlyContinue) {
                Start-Task "FcGuxNvdaQuit-$id" $nvda '-q' $nvdaLevel
                $clock = [Diagnostics.Stopwatch]::StartNew()
                while ((Get-Process -Name nvda -ErrorAction SilentlyContinue) -and $clock.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 300 }
            }
            $leftover = @(Get-Process -Name nvda, nvda_slave, FileCat, FileCat.ShellHost, $helperName -ErrorAction SilentlyContinue |
                Where-Object { $_.StartTime -ge $started -and ($_.Path -like ((Split-Path $app) + '\*') -or $_.Path -like ((Split-Path $nvda) + '\*')) })
            $result.Stopped = @($leftover | ForEach-Object { "$($_.ProcessName):$($_.Id)" })
            $leftover | Stop-Process -Force -ErrorAction SilentlyContinue
            if (Test-Path -LiteralPath (Join-Path $fixture $fileName)) { $result.FixtureAfter = Native-Sacl (Join-Path $fixture $fileName) }
            if ($result.FixtureBefore -and $result.FixtureAfter) {
                $result.AddedAuditEntries = @($result.FixtureAfter.Sacl | Where-Object { $_.Sid -eq $AuditSid })
                $result.OtherEntriesKept = (@($result.FixtureBefore.Sacl | ForEach-Object { "$($_.Type)|$($_.Flags)|$($_.Sid)|$($_.Mask)" }) -join ';') -eq
                    (@($result.FixtureAfter.Sacl | Where-Object { $_.Sid -ne $AuditSid } | ForEach-Object { "$($_.Type)|$($_.Flags)|$($_.Sid)|$($_.Mask)" }) -join ';')
            }
        } catch { $result.CleanupError = $_.Exception.Message }
        foreach ($name in @($script:launchTasks)) { Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue }
        $result.TasksLeft = @(Get-ScheduledTask -TaskName 'FcGux*' -ErrorAction SilentlyContinue | ForEach-Object { $_.TaskName })
        $result.Passed = [bool]($result.ReaderPassed -and $result.Stage -eq 'done' -and !$result.CleanupError -and $result.TasksLeft.Count -eq 0)
        $result.Finished = [DateTimeOffset]::Now.ToString('o')
        Write-Json $result $report
    }
}

switch ($Role) {
    'Launch' { Run-Launch }
    'Consent' { Run-Consent }
    'Reader' { Run-Reader }
}
