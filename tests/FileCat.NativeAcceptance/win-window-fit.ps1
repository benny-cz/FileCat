param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Only for the isolated, logged-in VMware validation guest: it changes the primary display's scaling through the
# DisplayConfig calls Settings uses and restores the original scaling afterwards. The viewer opens at 100% and is
# enlarged as a user would; the display then changes to 200% with it open. The viewer must stay inside the working
# area, and so must Find open across the change; the viewer's deliberate size must survive close, reopening
# must fit, and Find opened at 200% must fit too.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class FileCatWindowFit {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, out RECT rect, uint ini);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathInfo, ref uint modes, byte[] modeInfo, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(byte[] packet);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(byte[] packet);
    static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    public static List<KeyValuePair<IntPtr, string>> Windows(uint pid) {
        var found = new List<KeyValuePair<IntPtr, string>>();
        EnumWindows((h, l) => {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner == pid && IsWindowVisible(h)) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); found.Add(new KeyValuePair<IntPtr, string>(h, sb.ToString())); }
            return true; }, IntPtr.Zero);
        return found;
    }
    public static IntPtr ForegroundWindow() { return GetForegroundWindow(); }
    // The title bar's right end, left of the caption buttons: a secondary window opened over the middle cannot cover it.
    public static void ClickTitle(IntPtr hwnd) {
        RECT r; GetWindowRect(hwnd, out r);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        SetCursorPos(Math.Max(r.Left + (int)(40 * scale), r.Right - (int)(300 * scale)), r.Top + (int)(12 * scale));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
    public static void Key(byte vk, bool extended) {
        uint flags = extended ? 1u : 0u; byte scan = (byte)MapVirtualKey(vk, 0);
        keybd_event(vk, scan, flags, UIntPtr.Zero); keybd_event(vk, scan, flags | 2u, UIntPtr.Zero);
    }
    static byte[] Packet(int type, int size) {
        uint paths, modes;
        if (GetDisplayConfigBufferSizes(2, out paths, out modes) != 0) throw new Exception("GetDisplayConfigBufferSizes");
        var p = new byte[paths * 72]; var m = new byte[modes * 64];
        if (QueryDisplayConfig(2, ref paths, p, ref modes, m, IntPtr.Zero) != 0) throw new Exception("QueryDisplayConfig");
        var packet = new byte[size];
        BitConverter.GetBytes(type).CopyTo(packet, 0); BitConverter.GetBytes(size).CopyTo(packet, 4);
        Array.Copy(p, 0, packet, 8, 12);
        return packet;
    }
    // { recommended, current } percent of the primary display.
    public static int[] Scale() {
        var packet = Packet(-3, 32);
        if (DisplayConfigGetDeviceInfo(packet) != 0) throw new Exception("DisplayConfigGetDeviceInfo");
        int recommended = -BitConverter.ToInt32(packet, 20);
        return new[] { Steps[recommended], Steps[recommended + BitConverter.ToInt32(packet, 24)] };
    }
    public static void SetScale(int percent) {
        int recommended = Array.IndexOf(Steps, Scale()[0]);
        var packet = Packet(-4, 24);
        BitConverter.GetBytes(Array.IndexOf(Steps, percent) - recommended).CopyTo(packet, 20);
        if (DisplayConfigSetDeviceInfo(packet) != 0) throw new Exception("DisplayConfigSetDeviceInfo");
    }
}
"@
[void][FileCatWindowFit]::SetProcessDpiAwarenessContext([IntPtr]-4)

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$report = [IO.Path]::GetFullPath($ReportPath)
$app = [IO.Path]::GetFullPath($AppPath)
$computer = Get-CimInstance Win32_ComputerSystem
if ($computer.Manufacturer -notlike '*VMware*' -or [IO.Path]::GetDirectoryName($report) -ine $validation -or
    !$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or (Get-Process -Id $PID).SessionId -eq 0) {
    throw 'Display changes require the isolated interactive VMware validation guest and an owned app and report.'
}
if (Get-Process FileCat -ErrorAction SilentlyContinue) { throw 'Another FileCat is running; refusing to change the display under it.' }

function Frame($handle) {
    $r = New-Object FileCatWindowFit+RECT
    [void][FileCatWindowFit]::DwmGetWindowAttribute($handle, 9, [ref]$r, 16)
    [ordered]@{ Left = $r.Left; Top = $r.Top; Width = $r.Right - $r.Left; Height = $r.Bottom - $r.Top }
}
function WorkArea {
    $r = New-Object FileCatWindowFit+RECT
    [void][FileCatWindowFit]::SystemParametersInfo(0x30, 0, [ref]$r, 0)
    [ordered]@{ Left = $r.Left; Top = $r.Top; Width = $r.Right - $r.Left; Height = $r.Bottom - $r.Top }
}
function Inside($frame, $area) {
    # The visible frame within the working area, allowing one pixel of rounding.
    $frame.Left -ge $area.Left - 1 -and $frame.Top -ge $area.Top - 1 -and
        $frame.Left + $frame.Width -le $area.Left + $area.Width + 1 -and $frame.Top + $frame.Height -le $area.Top + $area.Height + 1
}
function Await([scriptblock]$condition, [string]$description, [int]$seconds = 15) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 150
    } while ($clock.Elapsed.TotalSeconds -lt $seconds)
    throw "Timed out waiting for $description."
}
function WindowNamed($process, [string]$pattern) {
    [FileCatWindowFit]::Windows([uint32]$process.Id) | Where-Object { $_.Value -like $pattern } | Select-Object -First 1
}
function Foreground($process, $handle) {
    [FileCatWindowFit]::ClickTitle($handle); Start-Sleep -Milliseconds 600
    # The window itself: another FileCat window over its title bar would take the keys.
    if ([FileCatWindowFit]::ForegroundWindow() -ne $handle) { throw 'The isolated FileCat main window is not foreground; refusing keys.' }
}
function OpenFind {
    [FileCatWindowFit]::keybd_event(0x12, [byte][FileCatWindowFit]::MapVirtualKey(0x12, 0), 0, [UIntPtr]::Zero)
    [FileCatWindowFit]::Key(0x76, $false)                                  # Alt+F7: Find
    [FileCatWindowFit]::keybd_event(0x12, [byte][FileCatWindowFit]::MapVirtualKey(0x12, 0), 2, [UIntPtr]::Zero)
}
function SavedViewerSize($data) {
    $text = (Get-ChildItem -LiteralPath $data -Recurse -Filter *.json | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    [ordered]@{ Width = if ($text -match '"ViewerWidth"\s*:\s*([0-9.]+)') { [double]$Matches[1] } else { $null }
                Height = if ($text -match '"ViewerHeight"\s*:\s*([0-9.]+)') { [double]$Matches[1] } else { $null } }
}

$result = [ordered]@{ Passed = $false; App = $app; AppDllSha256 = (Get-FileHash (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    OriginalScale = ([FileCatWindowFit]::Scale())[1]; Steps = [Collections.Generic.List[object]]::new(); Error = $null }
$process = $null
try {
    if ($result.OriginalScale -ne 100) { [FileCatWindowFit]::SetScale(100); Start-Sleep 3 }
    $fixture = Join-Path $validation ("WindowFitFixture\" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory "$fixture\source", "$fixture\target", "$fixture\data" -Force | Out-Null
    Set-Content -LiteralPath "$fixture\source\a.txt" ((1..200 | ForEach-Object { "line $_ of the display-change fixture" }) -join "`r`n")
    $result.Fixture = $fixture
    $result.SourceSha256 = (Get-FileHash "$fixture\source\a.txt" -Algorithm SHA256).Hash
    $process = Start-Process $app -ArgumentList '--new-instance', '--data', "`"$fixture\data`"", '--left', "`"$fixture\source`"",
        '--right', "`"$fixture\target`"" -PassThru
    $main = Await { $process.Refresh(); if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.MainWindowHandle } } 'the main window' 30
    Start-Sleep 5
    Foreground $process $main
    [FileCatWindowFit]::Key(0x28, $true); Start-Sleep -Milliseconds 400   # Down: a.txt
    [FileCatWindowFit]::Key(0x72, $false)                                  # F3
    $viewer = (Await { WindowNamed $process '*FileCat Viewer*' } 'the viewer').Key
    Start-Sleep 2
    $result.Steps.Add([ordered]@{ Step = 'viewer at 100%'; Frame = Frame $viewer; WorkArea = WorkArea; Dpi = [FileCatWindowFit]::GetDpiForWindow($viewer) })
    # Find open across the change as well, opened while the main window's title bar is still clear.
    Foreground $process $main
    OpenFind
    $findAcross = (Await { WindowNamed $process 'Find files*' } 'the Find window at 100%').Key
    Start-Sleep 2
    $result.Steps.Add([ordered]@{ Step = 'Find at 100%'; Frame = Frame $findAcross })
    [void][FileCatWindowFit]::MoveWindow($viewer, 120, 40, 1500, 1200, $true); Start-Sleep 2
    $result.Steps.Add([ordered]@{ Step = 'viewer enlarged at 100%'; Frame = Frame $viewer })
    [FileCatWindowFit]::SetScale(200); Start-Sleep 5
    $across = [ordered]@{ Step = 'Find after the display changed to 200%'; Frame = Frame $findAcross; WorkArea = WorkArea
        Dpi = [FileCatWindowFit]::GetDpiForWindow($findAcross) }
    $across.Inside = Inside $across.Frame $across.WorkArea
    $result.Steps.Add($across)
    [void][FileCatWindowFit]::PostMessage($findAcross, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    Await { -not (WindowNamed $process 'Find files*') } 'Find to close after the change' | Out-Null
    $open = [ordered]@{ Step = 'viewer after the display changed to 200%'; Frame = Frame $viewer; WorkArea = WorkArea
        Dpi = [FileCatWindowFit]::GetDpiForWindow($viewer) }
    $open.Inside = Inside $open.Frame $open.WorkArea
    $result.Steps.Add($open)
    $shot = New-Object System.Drawing.Bitmap $open.WorkArea.Width, $open.WorkArea.Height
    $g = [System.Drawing.Graphics]::FromImage($shot); $g.CopyFromScreen($open.WorkArea.Left, $open.WorkArea.Top, 0, 0, $shot.Size); $g.Dispose()
    $result.Capture = [IO.Path]::ChangeExtension($report, '.png')
    $shot.Save($result.Capture, [System.Drawing.Imaging.ImageFormat]::Png); $shot.Dispose()
    [void][FileCatWindowFit]::PostMessage($viewer, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    Await { -not (WindowNamed $process '*FileCat Viewer*') } 'the viewer to close' | Out-Null
    Start-Sleep 1
    $result.SavedViewerSize = SavedViewerSize "$fixture\data"
    Foreground $process $main
    [FileCatWindowFit]::Key(0x72, $false)
    $again = (Await { WindowNamed $process '*FileCat Viewer*' } 'the reopened viewer').Key
    Start-Sleep 2
    $reopened = [ordered]@{ Step = 'viewer reopened at 200%'; Frame = Frame $again; WorkArea = WorkArea }
    $reopened.Inside = Inside $reopened.Frame $reopened.WorkArea
    $result.Steps.Add($reopened)
    [void][FileCatWindowFit]::PostMessage($again, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    Await { -not (WindowNamed $process '*FileCat Viewer*') } 'the reopened viewer to close' | Out-Null
    Foreground $process $main
    OpenFind
    $find = (Await { WindowNamed $process 'Find files*' } 'the Find window').Key
    Start-Sleep 2
    $found = [ordered]@{ Step = 'Find opened at 200%'; Frame = Frame $find; WorkArea = WorkArea }
    $found.Inside = Inside $found.Frame $found.WorkArea
    $result.Steps.Add($found)
    [void][FileCatWindowFit]::PostMessage($find, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    Await { -not (WindowNamed $process 'Find files*') } 'Find to close' | Out-Null
    if ((Get-FileHash "$fixture\source\a.txt" -Algorithm SHA256).Hash -ne $result.SourceSha256 -or (Get-ChildItem "$fixture\target")) {
        throw 'The window journey changed the owned source or destination.'
    }
    # The enlarged frame was 1,500 by 1,200 pixels at 100%: its client size, a little less, is the size to keep.
    $result.Passed = $open.Inside -and $across.Inside -and $reopened.Inside -and $found.Inside -and
        $result.SavedViewerSize.Width -ge 1400 -and $result.SavedViewerSize.Height -ge 1100
}
catch { $result.Error = $_.Exception.Message }
finally {
    if ($process -and !$process.HasExited) {
        [void]$process.CloseMainWindow()
        if (!$process.WaitForExit(8000)) { Stop-Process -Id $process.Id -Force }
    }
    try { [FileCatWindowFit]::SetScale($result.OriginalScale); Start-Sleep 2; $result.RestoredScale = ([FileCatWindowFit]::Scale())[1] }
    catch { $result.RestoreError = $_.Exception.Message }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
