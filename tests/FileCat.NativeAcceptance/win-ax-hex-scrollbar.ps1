param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('Viewer', 'Editor')][string]$WindowKind = 'Viewer'
)

# Run on the interactive Windows test desktop. Exercise the native scrollbar
# thumb; setting ScrollPattern.Value would not reproduce routed pointer events.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatHexPointer {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Input {
        public uint Type;
        public MouseInput Mouse;
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr handle, IntPtr after,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(
        uint count, Input[] inputs, int size);
    private static void Button(uint flag) {
        var input = new Input { Type = 0, Mouse = new MouseInput { Flags = flag } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("Native pointer button input was rejected.");
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
        Button(2); System.Threading.Thread.Sleep(60);
        Button(4);
    }
    public static void DragXY(int x, int y, int targetX, int targetY) {
        if (!SetCursorPos(x, y)) throw new InvalidOperationException("Cannot position test pointer.");
        System.Threading.Thread.Sleep(80);
        Button(2);
        try {
            System.Threading.Thread.Sleep(120);
            for (int step = 1; step <= 16; step++) {
                if (!SetCursorPos(x + (targetX - x) * step / 16, y + (targetY - y) * step / 16))
                    throw new InvalidOperationException("Cannot move test pointer during drag.");
                System.Threading.Thread.Sleep(25);
            }
        }
        finally { Button(4); }
        System.Threading.Thread.Sleep(250);
    }
    public static void Drag(int x, int y, int targetY) { DragXY(x, y, x, targetY); }
    public static string CursorPosition() {
        Point point;
        if (!GetCursorPos(out point)) throw new InvalidOperationException("Cannot read test pointer position.");
        return point.X + "," + point.Y;
    }
    public static uint ForegroundProcessId() {
        uint id;
        GetWindowThreadProcessId(GetForegroundWindow(), out id);
        return id;
    }
}
'@

$root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $root + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -PathType Leaf)) {
    throw 'Use a Git-built FileCat.exe inside the isolated FileCatValidation directory.'
}
if (!$report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    !(Test-Path -LiteralPath (Split-Path $report) -PathType Container) -or
    (Test-Path -LiteralPath $report)) {
    throw 'Use a fresh report path in FileCatValidation.'
}
if (@(Get-CimInstance Win32_Process -Filter "Name = 'FileCat.exe'").Count) {
    throw 'Another FileCat process is running in the guest; do not disturb its window.'
}
$fixture = Join-Path $root ('HexPointerFixture\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$file = Join-Path $source 'hex-scrollbar.bin'
$bytes = New-Object byte[] 8192
for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [byte]($i % 256) }
[IO.File]::WriteAllBytes($file, $bytes)
$originalHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
$process = $null
$result = [ordered]@{
    passed = $false
    windowKind = $WindowKind
    fixture = $fixture
    appDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    originalSha256 = $originalHash
}

function All($window) {
    return @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function One($elements, [string]$label) {
    $found = @($elements)
    if ($found.Count -ne 1) { throw "Expected one $label, found $($found.Count)." }
    return $found[0]
}
function WaitForViewer([int]$processId, [string]$suffix) {
    $observed = @()
    for ($i = 0; $i -lt 80; $i++) {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        $matches = @($windows | Where-Object {
            $_.Current.ProcessId -eq $processId -and
            $_.Current.Name.StartsWith('hex-scrollbar.bin ', [StringComparison]::Ordinal) -and
            $_.Current.Name.EndsWith($suffix, [StringComparison]::Ordinal)
        })
        if ($matches.Count -eq 1) { return $matches[0] }
        if ($i % 10 -eq 0) {
            $observed = @($windows | Where-Object { $_.Current.ProcessId -eq $processId } |
                ForEach-Object { $_.Current.Name })
        }
        Start-Sleep -Milliseconds 200
    }
    throw "The owned $suffix window did not open. Process windows: $($observed -join ' | ')"
}
function Hex($viewer) {
    return One (All $viewer | Where-Object {
        $_.Current.ClassName -eq 'HexView' -and !$_.Current.IsOffscreen
    }) 'visible Hex document'
}
function Status($viewer) {
    $document = Hex $viewer
    $value = [System.Windows.Automation.ValuePattern]$document.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    return $value.Current.Value
}
function Offset([string]$status, [string]$pattern) {
    $match = [regex]::Match($status, $pattern)
    if (!$match.Success) { throw "Hex status lacks $pattern`: $status" }
    return [long]($match.Groups[1].Value -replace '[^0-9]', '')
}
function FocusOwnedWindow($window) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [FileCatHexPointer]::ShowWindow($handle, 9) | Out-Null
    [FileCatHexPointer]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 150
    if ([FileCatHexPointer]::GetForegroundWindow() -ne $handle) {
        throw "Native pointer target is not the foreground $($window.Current.Name) window."
    }
}
function Thumb($viewer) {
    $bar = One (All $viewer | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ScrollBar -and
        $_.Current.Name -eq 'Hex vertical position' -and !$_.Current.IsOffscreen
    }) 'Hex vertical scrollbar'
    $thumb = One ($bar.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Thumb -and
            !$_.Current.IsOffscreen
        }) 'Hex scrollbar thumb'
    $range = [System.Windows.Automation.RangeValuePattern]$bar.GetCurrentPattern(
        [System.Windows.Automation.RangeValuePattern]::Pattern)
    return @{ Bar = $bar.Current.BoundingRectangle; Thumb = $thumb.Current.BoundingRectangle;
        Value = $range.Current.Value }
}
function Hit([int]$x, [int]$y) {
    $element = [System.Windows.Automation.AutomationElement]::FromPoint(
        [System.Windows.Point]::new($x, $y))
    if (!$element) { return 'none' }
    return "$($element.Current.ControlType.ProgrammaticName) / $($element.Current.Name) / $($element.Current.ClassName)"
}

try {
    $process = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $source, '--right', $target)
    for ($i = 0; $i -lt 100 -and $process.MainWindowHandle -eq [IntPtr]::Zero -and !$process.HasExited; $i++) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero -or $process.HasExited) {
        throw 'The owned FileCat main window did not open.'
    }
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'Unexpected executable owns the FileCat window.'
    }
    [FileCatHexPointer]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    # Place only this disposable instance where its title bar is exposed before
    # using a physical click to activate it.
    $mainRect = [FileCatHexPointer+Rect]::new()
    if (![FileCatHexPointer]::GetWindowRect($process.MainWindowHandle, [ref]$mainRect) -or
        ![FileCatHexPointer]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero,
            20, 20, $mainRect.Right - $mainRect.Left, $mainRect.Bottom - $mainRect.Top, 0x0014)) {
        throw 'Cannot position the owned FileCat window for native pointer input.'
    }
    [FileCatHexPointer]::Click(105, 34)
    [FileCatHexPointer]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $row = $null
    for ($i = 0; $i -lt 50 -and !$row; $i++) {
        $row = @(All $main | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -eq 'hex-scrollbar.bin'
        }) | Select-Object -First 1
        if (!$row) { Start-Sleep -Milliseconds 200 }
    }
    if (!$row) { throw 'The owned source file did not appear in the main panel.' }
    $bounds = $row.Current.BoundingRectangle
    $result.mainBounds = "$($main.Current.BoundingRectangle)"
    $result.rowBounds = "$bounds"
    [FileCatHexPointer]::Click([int]($bounds.X + [Math]::Min(25, $bounds.Width / 2)),
        [int]($bounds.Y + $bounds.Height / 2))
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    [FileCatHexPointer]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [FileCatHexPointer]::Click([int]($bounds.X + [Math]::Min(25, $bounds.Width / 2)),
        [int]($bounds.Y + $bounds.Height / 2))
    Start-Sleep -Milliseconds 150
    $foreground = [FileCatHexPointer]::ForegroundProcessId()
    if ($foreground -ne $process.Id) {
        $foregroundProcess = Get-Process -Id $foreground -ErrorAction SilentlyContinue
        throw "The owned FileCat main window did not receive keyboard focus (foreground $foreground $($foregroundProcess.ProcessName) session $($foregroundProcess.SessionId), owned $($process.Id) session $($process.SessionId), runner session $((Get-Process -Id $PID).SessionId))."
    }
    (New-Object -ComObject WScript.Shell).SendKeys('{F3}')
    $viewer = WaitForViewer $process.Id 'FileCat Viewer'
    if ($WindowKind -eq 'Viewer') {
        $hexButton = One (All $viewer | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $_.Current.Name -eq 'Hex' -and !$_.Current.IsOffscreen
        }) 'Hex mode button'
        $hexBounds = $hexButton.Current.BoundingRectangle
        [FileCatHexPointer]::Click([int]($hexBounds.X + $hexBounds.Width / 2),
            [int]($hexBounds.Y + $hexBounds.Height / 2))
    } else {
        [FileCatHexPointer]::SetForegroundWindow([IntPtr]$viewer.Current.NativeWindowHandle) | Out-Null
        (New-Object -ComObject WScript.Shell).SendKeys('{F6}')
        $viewer = WaitForViewer $process.Id 'FileCat Hex Editor'
    }
    Start-Sleep -Milliseconds 300
    $initial = Status $viewer
    $initialCursor = Offset $initial '^Offset ([0-9][0-9.,\p{Zs}]*) of'
    if ($initial -notmatch 'No byte selection') { throw "Unexpected initial selection: $initial" }

    # Resize the real top-level window and verify that the row layout changes.
    $handle = [IntPtr]$viewer.Current.NativeWindowHandle
    [FileCatHexPointer]::ShowWindow($handle, 9) | Out-Null
    $windowDpi = [FileCatHexPointer]::GetDpiForWindow($handle)
    if ($windowDpi -lt 96 -or $windowDpi -gt 384) { throw "Invalid native Hex window DPI: $windowDpi" }
    $scale = $windowDpi / 96.0
    $result.windowDpi = $windowDpi
    $rect = [FileCatHexPointer+Rect]::new()
    if (![FileCatHexPointer]::GetWindowRect($handle, [ref]$rect)) { throw 'Cannot read viewer bounds.' }
    if (![FileCatHexPointer]::SetWindowPos($handle, [IntPtr]::Zero, $rect.Left, $rect.Top,
        640, [Math]::Max(420, $rect.Bottom - $rect.Top), 0x0014)) {
        throw 'Cannot narrow the native viewer.'
    }
    Start-Sleep -Milliseconds 350
    $narrowRect = [FileCatHexPointer+Rect]::new()
    if (![FileCatHexPointer]::GetWindowRect($handle, [ref]$narrowRect)) {
        throw 'Cannot read the narrowed Hex window bounds.'
    }
    $narrowWidth = $narrowRect.Right - $narrowRect.Left
    $result.narrowWindowWidth = $narrowWidth
    $narrow = Status $viewer
    $narrowRow = [regex]::Match($narrow, 'row [0-9A-F]+: ([0-9A-F ]+)\.').Groups[1].Value
    # The editor has a 620-DIP minimum width. At 150% a requested 640-pixel
    # window is clamped near 930 pixels, so 940 pixels is not a real resize.
    # Use the actual narrowed width and available desktop room instead.
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement.Current.BoundingRectangle
    $wideRequest = [int][Math]::Min($desktop.Right - $rect.Left - 20,
        [Math]::Max(940, $narrowWidth + [Math]::Ceiling(320 * $scale)))
    if ($wideRequest - $narrowWidth -lt [Math]::Ceiling(100 * $scale)) {
        throw "The test desktop cannot widen the Hex window enough: $narrowWidth to $wideRequest physical pixels."
    }
    $result.wideRequestedWidth = $wideRequest
    if (![FileCatHexPointer]::SetWindowPos($handle, [IntPtr]::Zero, $rect.Left, $rect.Top,
        $wideRequest, [Math]::Max(420, $rect.Bottom - $rect.Top), 0x0014)) {
        throw 'Cannot widen the native viewer.'
    }
    Start-Sleep -Milliseconds 350
    $wideRect = [FileCatHexPointer+Rect]::new()
    if (![FileCatHexPointer]::GetWindowRect($handle, [ref]$wideRect)) {
        throw 'Cannot read the widened Hex window bounds.'
    }
    $result.wideWindowWidth = $wideRect.Right - $wideRect.Left
    $wide = Status $viewer
    $wideRow = [regex]::Match($wide, 'row [0-9A-F]+: ([0-9A-F ]+)\.').Groups[1].Value
    $narrowBytes = @($narrowRow -split ' ' | Where-Object { $_ }).Count
    $wideBytes = @($wideRow -split ' ' | Where-Object { $_ }).Count
    if ($wideBytes -le $narrowBytes) {
        throw "Hex row did not widen with the native window: $narrowBytes to $wideBytes bytes."
    }

    $scroll = Thumb $viewer
    $startX = [int]($scroll.Thumb.X + $scroll.Thumb.Width / 2)
    $startY = [int]($scroll.Thumb.Y + $scroll.Thumb.Height / 2)
    $travel = [int][Math]::Min(150, [Math]::Max(40, $scroll.Bar.Height / 3))
    FocusOwnedWindow $viewer
    $result.firstThumbBounds = "$($scroll.Thumb)"
    $result.firstThumbHit = Hit $startX $startY
    [FileCatHexPointer]::Drag($startX, $startY, $startY + $travel)
    $result.firstCursorAfter = [FileCatHexPointer]::CursorPosition()
    $result.firstBarValue = (Thumb $viewer).Value
    $after = Status $viewer
    $viewport = Offset $after 'viewport begins at byte ([0-9][0-9.,\p{Zs}]*)'
    $result.firstViewportByte = $viewport
    if ($viewport -le 0 -or $after -notmatch 'No byte selection' -or
        (Offset $after '^Offset ([0-9][0-9.,\p{Zs}]*) of') -ne $initialCursor) {
        throw "Scrollbar drag selected or moved the cursor, or did not scroll: $after"
    }
    Start-Sleep -Milliseconds 1000
    $scroll = Thumb $viewer
    $result.reverseThumbBounds = "$($scroll.Thumb)"
    $result.reverseBarBounds = "$($scroll.Bar)"
    if ([FileCatHexPointer]::GetForegroundWindow() -ne $handle) {
        throw 'Hex viewer lost foreground before the reverse thumb drag.'
    }
    $result.reverseThumbHit = Hit ([int]($scroll.Thumb.X + $scroll.Thumb.Width / 2)) `
        ([int]($scroll.Thumb.Y + $scroll.Thumb.Height / 2))
    [FileCatHexPointer]::Drag([int]($scroll.Thumb.X + $scroll.Thumb.Width / 2),
        [int]($scroll.Thumb.Y + $scroll.Thumb.Height / 2),
        [int][Math]::Max($scroll.Bar.Y + 10,
            $scroll.Thumb.Y + $scroll.Thumb.Height / 2 - $travel))
    $result.reverseCursorAfter = [FileCatHexPointer]::CursorPosition()
    $result.reverseBarValue = (Thumb $viewer).Value
    $afterUp = $null
    $upViewport = $viewport
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $afterUp = Status $viewer
        $upViewport = Offset $afterUp 'viewport begins at byte ([0-9][0-9.,\p{Zs}]*)'
        if ($upViewport -lt $viewport) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($upViewport -ge $viewport -or $afterUp -notmatch 'No byte selection' -or
        (Offset $afterUp '^Offset ([0-9][0-9.,\p{Zs}]*) of') -ne $initialCursor) {
        throw "Reverse scrollbar drag changed selection/cursor or did not scroll up: $afterUp"
    }

    # A prior explicit selection must survive a second thumb drag.
    $hex = Hex $viewer
    $content = $hex.Current.BoundingRectangle
    # UIA and SetCursorPos use physical pixels; the original offsets are
    # layout units. Scale them with the actual viewer window DPI.
    $startX = [int]($content.X + 20 * $scale)
    $endX = [int]($content.X + 160 * $scale)
    $rowY = [int]($content.Y + 12 * $scale)
    $result.explicitContentDrag = "$startX,$rowY to $endX,$rowY"
    FocusOwnedWindow $viewer
    $result.contentHit = Hit $startX $rowY
    [FileCatHexPointer]::DragXY($startX, $rowY, $endX, $rowY)
    $selected = Status $viewer
    if ($selected -notmatch 'Selected bytes 0 through 3, 4 bytes') {
        throw "Explicit keyboard selection did not appear: $selected"
    }
    $scroll = Thumb $viewer
    FocusOwnedWindow $viewer
    [FileCatHexPointer]::Drag([int]($scroll.Thumb.X + $scroll.Thumb.Width / 2),
        [int]($scroll.Thumb.Y + $scroll.Thumb.Height / 2),
        [int]($scroll.Thumb.Y + $scroll.Thumb.Height / 2 + $travel))
    $afterSelected = Status $viewer
    if ($afterSelected -notmatch 'Selected bytes 0 through 3, 4 bytes') {
        throw "Scrollbar drag altered the existing explicit selection: $afterSelected"
    }
    $result.passed = $true
    $result.narrowBytesPerRow = $narrowBytes
    $result.wideBytesPerRow = $wideBytes
    $result.scrollbarViewportByte = $viewport
    $result.reverseScrollbarViewportByte = $upViewport
    $result.scrollbarPreservedNoSelection = $true
    $result.scrollbarPreservedExplicitSelection = $true
} catch {
    $result.error = $_.Exception.Message
    $result.stack = $_.ScriptStackTrace
} finally {
    if ($process -and !$process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $process.WaitForExit(5000) | Out-Null
    }
    $result.sourceIntact = (Test-Path -LiteralPath $file -PathType Leaf) -and
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $originalHash
    $result.targetEmpty = @(Get-ChildItem -LiteralPath $target -Force).Count -eq 0
    if (!$result.sourceIntact -or !$result.targetEmpty) { $result.passed = $false }
    $owned = [IO.Path]::GetFullPath((Join-Path $root 'HexPointerFixture')) + [IO.Path]::DirectorySeparatorChar
    $safeCleanup = [IO.Path]::GetFullPath($fixture).StartsWith($owned, [StringComparison]::OrdinalIgnoreCase) -and
        $result.sourceIntact -and $result.targetEmpty
    $result.safeCleanup = $safeCleanup
    if ($safeCleanup) {
        for ($attempt = 0; $attempt -lt 5 -and (Test-Path -LiteralPath $fixture); $attempt++) {
            try { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction Stop }
            catch { $result.cleanupError = $_.Exception.Message; Start-Sleep -Milliseconds 200 }
        }
    }
    $result.fixtureRemoved = !(Test-Path -LiteralPath $fixture)
    if (!$result.fixtureRemoved) { $result.passed = $false }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.passed) { throw "Native Hex scrollbar check failed: $($result.error)" }
