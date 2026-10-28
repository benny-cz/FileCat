param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# AC-S3-064 on the isolated VMware guest: the main window is given normal bounds, maximized and restored through its
# window controls (UI Automation's WindowPattern, as the title-bar buttons do), closed maximized and reopened maximized
# with the same normal bounds behind it; moved to where a second monitor would be and closed there, it reopens
# centred on the remaining display; and with the display switched from 100% to 150% while it runs, and back, every
# reopening lies inside the work area with the menu, command search, both panels, the command line and the key bar
# inside the window. The display's original scale is restored at the end, whatever happens.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatPlacement {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(System.Drawing.Point p, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out RECT value, int size);
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathInfo, ref uint modes, byte[] modeInfo, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(byte[] packet);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(byte[] packet);
    public static RECT Frame(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, 16); return r; }  // DWMWA_EXTENDED_FRAME_BOUNDS
    public static RECT Work() {
        var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
        GetMonitorInfo(MonitorFromPoint(new System.Drawing.Point(0, 0), 1), ref info);
        return info.Work;
    }
    // The display's scale, as Settings sets it: steps relative to the recommended one.
    static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    static byte[] Source() {
        uint paths, modes;
        if (GetDisplayConfigBufferSizes(2, out paths, out modes) != 0) throw new Exception("GetDisplayConfigBufferSizes");
        var p = new byte[paths * 72]; var m = new byte[modes * 64];
        if (QueryDisplayConfig(2, ref paths, p, ref modes, m, IntPtr.Zero) != 0) throw new Exception("QueryDisplayConfig");
        var source = new byte[12]; Array.Copy(p, 0, source, 0, 12); return source;
    }
    static byte[] Header(int type, int size, byte[] source) {
        var packet = new byte[size];
        BitConverter.GetBytes(type).CopyTo(packet, 0); BitConverter.GetBytes(size).CopyTo(packet, 4);
        Array.Copy(source, 0, packet, 8, 12); return packet;
    }
    public static int Scale() {
        var packet = Header(-3, 32, Source());
        if (DisplayConfigGetDeviceInfo(packet) != 0) throw new Exception("DisplayConfigGetDeviceInfo");
        int recommended = -BitConverter.ToInt32(packet, 20);
        return Steps[recommended + BitConverter.ToInt32(packet, 24)];
    }
    public static void SetScale(int percent) {
        var get = Header(-3, 32, Source());
        if (DisplayConfigGetDeviceInfo(get) != 0) throw new Exception("DisplayConfigGetDeviceInfo");
        int recommended = -BitConverter.ToInt32(get, 20);
        var packet = Header(-4, 24, Source());
        BitConverter.GetBytes(Array.IndexOf(Steps, percent) - recommended).CopyTo(packet, 20);
        if (DisplayConfigSetDeviceInfo(packet) != 0) throw new Exception("DisplayConfigSetDeviceInfo");
    }
}
'@ -ReferencedAssemblies System.Drawing
# Every rectangle this runner reads (window frame, work area, UI Automation bounds) in physical pixels, before and
# after the scale changes: Windows scales them for a thread that is not per-monitor DPI aware.
if ([FileCatPlacement]::SetThreadDpiAwarenessContext([IntPtr](-4)) -eq [IntPtr]::Zero) { throw 'Per-monitor DPI awareness was refused' }
$TreeScope = [System.Windows.Automation.TreeScope]
$AE = [System.Windows.Automation.AutomationElement]
function Descendants($element) { @($element.FindAll($TreeScope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) }
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
function Rect($r) { [ordered]@{ X = $r.Left; Y = $r.Top; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top } }
function Inside($inner, $outer, [int] $slack = 2) {
    $inner.X -ge $outer.X - $slack -and $inner.Y -ge $outer.Y - $slack -and
    $inner.X + $inner.W -le $outer.X + $outer.W + $slack -and $inner.Y + $inner.H -le $outer.Y + $outer.H + $slack
}
function Near($a, $b, [int] $slack = 3) {
    [Math]::Abs($a.X - $b.X) -le $slack -and [Math]::Abs($a.Y - $b.Y) -le $slack -and
    [Math]::Abs($a.W - $b.W) -le $slack -and [Math]::Abs($a.H - $b.H) -le $slack
}
function Start-FileCat {
    $process = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    Await { $process.Refresh(); if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'owned FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) { throw 'Unexpected FileCat executable' }
    Start-Sleep -Milliseconds 2500
    $script:process = $process
    $script:main = $AE::FromHandle($process.MainWindowHandle)
}
function Handle { [IntPtr]$script:process.MainWindowHandle }
function WindowState([string] $state) {
    $script:main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState(
        [System.Windows.Automation.WindowVisualState]::$state)
    Start-Sleep -Milliseconds 1200
}
function Close-FileCat {
    $script:main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    if (-not $script:process.WaitForExit(15000)) { throw 'FileCat did not exit when its window closed' }
}
# The window's visible frame, its state, and whether its main controls lie inside it and on the display.
function Look([string] $label) {
    $frame = Rect ([FileCatPlacement]::Frame((Handle)))
    $work = Rect ([FileCatPlacement]::Work())
    $controls = @(Descendants $script:main | Where-Object {
        $name = $_.Current.Name; $type = $_.Current.ControlType.ProgrammaticName
        ($type -eq 'ControlType.MenuItem' -and $name -in 'File', 'Help') -or
        ($type -eq 'ControlType.Edit' -and $name -in 'Search commands', 'Command line') -or
        $_.Current.ClassName -eq 'FileList' -or
        ($type -eq 'ControlType.Button' -and $_.Current.HelpText -match '^F(1[0-2]|[1-9]):') })  # the key bar: "F5: Copy…"
    $outside = @($controls | Where-Object { $_.Current.IsOffscreen -or -not (Inside (Rect @{
        Left = [int]$_.Current.BoundingRectangle.Left; Top = [int]$_.Current.BoundingRectangle.Top
        Right = [int]$_.Current.BoundingRectangle.Right; Bottom = [int]$_.Current.BoundingRectangle.Bottom }) $frame) } |
        ForEach-Object { $_.Current.Name })
    $look = [ordered]@{
        Label = $label; Scale = [FileCatPlacement]::Scale(); Dpi = [FileCatPlacement]::GetDpiForWindow((Handle))
        Maximized = [FileCatPlacement]::IsZoomed((Handle)); Frame = $frame; Work = $work
        Controls = $controls.Count; KeyBarButtons = @($controls | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' }).Count
        Outside = $outside
    }
    $script:result.Looks += $look
    if (-not (Inside $frame $work 4)) { throw "$label`: the window $($frame | ConvertTo-Json -Compress) leaves the work area $($work | ConvertTo-Json -Compress)" }
    if ($look.Controls -lt 17 -or $look.KeyBarButtons -lt 12) { throw "$label`: only $($look.Controls) main controls ($($look.KeyBarButtons) keys) are exposed" }
    if ($outside.Count) { throw "$label`: controls outside the window: $($outside -join ', ')" }
    $look
}

$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation.TrimEnd('\') + '\'
$computer = Get-CimInstance Win32_ComputerSystem
if ($computer.Manufacturer -notlike '*VMware*' -or (Get-Process -Id $PID).SessionId -eq 0 -or
    -not $app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Display scale changes need the isolated interactive VMware guest, an isolated Git app, a fresh report and no FileCat'
}
$fixture = Join-Path $validation ('WindowRestoreFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'
New-Item -ItemType Directory -Force -Path $data, $left, $right | Out-Null
$originalScale = [FileCatPlacement]::Scale()
$script:result = [ordered]@{
    Passed = $false; Stage = 'starting'; Fixture = $fixture; OriginalScale = $originalScale; Looks = @(); Saved = @()
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash; Error = $null
}
$script:process = $null
function Saved([string] $label) {
    $workspace = Get-Content -Raw -LiteralPath (Join-Path $data 'workspace.json') | ConvertFrom-Json
    $script:result.Saved += [ordered]@{ Label = $label; Window = $workspace.Window }
    $workspace.Window
}
try {
    if ($originalScale -ne 100) { [FileCatPlacement]::SetScale(100); Start-Sleep 3 }

    # 1. Normal bounds, maximize and restore at 100%.
    $script:result.Stage = 'maximize and restore'
    Start-FileCat
    WindowState 'Normal'
    $work = Rect ([FileCatPlacement]::Work())
    $normal = [ordered]@{ X = $work.X + 150; Y = $work.Y + 100; W = [Math]::Min(1100, $work.W - 300); H = [Math]::Min(720, $work.H - 200) }
    [void][FileCatPlacement]::SetWindowPos((Handle), [IntPtr]::Zero, $normal.X, $normal.Y, $normal.W, $normal.H, 0x14)
    Start-Sleep -Milliseconds 1500
    $set = Look 'normal at 100%'
    WindowState 'Maximized'
    $max = Look 'maximized at 100%'
    if (-not $max.Maximized -or -not (Near $max.Frame $max.Work 2)) { throw 'Maximized does not fill the work area' }
    WindowState 'Normal'
    $restored = Look 'restored at 100%'
    if (-not (Near $restored.Frame $set.Frame)) { throw "Restore went to $($restored.Frame | ConvertTo-Json -Compress), not $($set.Frame | ConvertTo-Json -Compress)" }

    # 2. Closed maximized, reopened maximized with the same normal bounds behind it.
    $script:result.Stage = 'reopen maximized'
    WindowState 'Maximized'
    Close-FileCat
    $saved = Saved 'closed maximized'
    if (-not $saved.Maximized) { throw 'The maximized state was not saved' }
    Start-FileCat
    $reopened = Look 'reopened maximized'
    if (-not $reopened.Maximized) { throw 'The window did not reopen maximized' }
    WindowState 'Normal'
    $behind = Look 'restored after reopening'
    if (-not (Near $behind.Frame $set.Frame 4)) { throw "Behind the maximized window lay $($behind.Frame | ConvertTo-Json -Compress), not $($set.Frame | ConvertTo-Json -Compress)" }

    # 3. Moved to where a second monitor would be, closed there: it reopens centred on the remaining display.
    $script:result.Stage = 'removed monitor'
    [void][FileCatPlacement]::SetWindowPos((Handle), [IntPtr]::Zero, $work.X + $work.W + 300, $work.Y + 120, $normal.W, $normal.H, 0x14)
    Start-Sleep -Milliseconds 1500
    Close-FileCat
    $saved = Saved 'closed beyond the display'
    if ($saved.X -lt $work.X + $work.W) { throw "The beyond-the-display position was not saved: X = $($saved.X)" }
    Start-FileCat
    $recentred = Look 'reopened after the monitor went'
    $centre = $recentred.Frame.X + $recentred.Frame.W / 2
    if ([Math]::Abs($centre - ($work.X + $work.W / 2)) -gt 20) { throw "The window reopened at $($recentred.Frame | ConvertTo-Json -Compress), not centred" }

    # 4. The display goes to 150% while FileCat runs; maximize and restore there; reopen there.
    $script:result.Stage = '150%'
    [FileCatPlacement]::SetScale(150)
    Await { [FileCatPlacement]::GetDpiForWindow((Handle)) -eq 144 } 'the window to follow the display to 150%' 20 | Out-Null
    Start-Sleep -Milliseconds 1500
    $at150 = Look 'after the change to 150%'
    WindowState 'Maximized'
    $max150 = Look 'maximized at 150%'
    if (-not (Near $max150.Frame $max150.Work 2)) { throw 'Maximized at 150% does not fill the work area' }
    WindowState 'Normal'
    $normal150 = Look 'restored at 150%'
    if (-not (Near $normal150.Frame $at150.Frame 4)) { throw "Restore at 150% went to $($normal150.Frame | ConvertTo-Json -Compress), not $($at150.Frame | ConvertTo-Json -Compress)" }
    Close-FileCat
    [void](Saved 'closed at 150%')
    Start-FileCat
    $reopen150 = Look 'reopened at 150%'

    # 5. Back to 100% while it is closed: it reopens inside the work area.
    $script:result.Stage = 'back to 100%'
    Close-FileCat
    [void](Saved 'closed again at 150%')
    [FileCatPlacement]::SetScale(100)
    Start-Sleep 3
    Start-FileCat
    $reopen100 = Look 'reopened at 100% after 150%'
    Close-FileCat
    $script:result.Passed = $true
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message }
finally {
    if ($script:process -and -not $script:process.HasExited) { $script:process.Kill(); $script:process.WaitForExit(5000) | Out-Null }
    try { if ([FileCatPlacement]::Scale() -ne $originalScale) { [FileCatPlacement]::SetScale($originalScale) } } catch { $script:result.ScaleRestoreError = $_.Exception.Message }
    $script:result.FinalScale = try { [FileCatPlacement]::Scale() } catch { $null }
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
