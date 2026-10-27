param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# Isolated native route to a Find form's resize grip: Alt+F7 opens Find, Advanced... opens the resizable criteria
# dialog, the grip must be reached by Shift+Tab/Tab and make NVDA speak its name and arrow-key help, and Right arrows must
# widen the dialog. Escape closes it.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatGripInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    // Input-driven activation at the title bar's right end, left of the caption buttons.
    public static void ClickTitle(IntPtr h) {
        RECT r; GetWindowRect(h, out r);
        double scale = GetDpiForWindow(h) / 96.0;
        SetCursorPos(Math.Max(r.Left + (int)(40 * scale), r.Right - (int)(300 * scale)), r.Top + (int)(12 * scale));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@
function Descendants($root) {
    @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Await([scriptblock] $check, [string] $description, [int] $seconds = 25) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $check; if ($null -ne $value -and $value -ne $false) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 125
    }
    throw "Timed out waiting for $description"
}
function Named($root, [string] $name) {
    @(Descendants $root | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1)[0]
}
function Click($element) {
    $r = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $r.Width -lt 8 -or $r.Height -lt 8) {
        throw "Unreachable pointer target: $($element.Current.Name)"
    }
    [void][FileCatGripInput]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 80
    [FileCatGripInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [FileCatGripInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
function Key([byte] $code, [bool] $extended = $false) {
    $flags = if ($extended) { 1 } else { 0 }
    [FileCatGripInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [FileCatGripInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) { [FileCatGripInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    [array]::Reverse($codes)
    foreach ($code in $codes) { [FileCatGripInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
}
function TakeSpeech {
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    return @($fresh | Where-Object { $_ -match 'Speaking \[' } |
        ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
}

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    -not $nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or
    (Get-Process -Name nvda -ErrorAction SilentlyContinue) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an isolated Git app, signed portable NVDA, a fresh validation report and no competing reader or FileCat'
}
$fixture = Join-Path $validation ('DialogGripFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
$left = Join-Path $fixture 'left'
$right = Join-Path $fixture 'right'
$config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
Set-Content -LiteralPath (Join-Path $left 'a.txt') 'grip fixture'
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$script:nvdaLog = Join-Path $fixture 'nvda.log'
$script:speechIndex = 0
$result = [ordered]@{
    Passed = $false; Stage = 'starting'; Fixture = $fixture
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; Error = $null
}
$reader = $owned = $null
try {
    $reader = Start-Process -FilePath $nvda -ArgumentList @(
        "--log-file=$script:nvdaLog", '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Await {
        if ($reader.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $script:nvdaLog) -and
            (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA initialization' 30 | Out-Null
    $owned = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    $main = Await {
        $owned.Refresh()
        if ($owned.MainWindowHandle -ne [IntPtr]::Zero) { [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle) }
    } 'owned FileCat window'
    if ([IO.Path]::GetFullPath($owned.MainModule.FileName) -ine $app) { throw 'Unexpected FileCat executable' }
    Start-Sleep 3
    [FileCatGripInput]::ClickTitle($owned.MainWindowHandle)
    Start-Sleep -Milliseconds 600
    if ([FileCatGripInput]::GetForegroundWindow() -ne $owned.MainWindowHandle) { throw 'The isolated FileCat window is not foreground; refusing keys' }
    $result.Stage = 'Find'
    Chord ([byte[]]@(0x12, 0x76)) # Alt+F7
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $find = Await {
        @($desktop.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.ProcessId -eq $owned.Id -and $_.Current.Name -like 'Find files*' } | Select-Object -First 1)[0]
    } 'the Find window'
    # ASCII only: Windows PowerShell reads this file without a byte-order mark.
    $advanced = Await {
        @(Descendants $find | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $_.Current.Name -like 'Advanced*' } | Select-Object -First 1)[0]
    } 'the Advanced button'
    Click $advanced
    $result.Stage = 'grip'
    $grip = Await { Named $find 'Resize dialog' } 'the dialog resize grip'
    $card = Await {
        @(Descendants $find | Where-Object { $_.Current.Name -like '* dialog' -and $_.Current.Name -ne 'Close dialog' -and
            $_.Current.Name -ne 'Resize dialog' } | Select-Object -First 1)[0]
    } 'the resizable dialog card'
    $result.Dialog = $card.Current.Name
    $result.GripHelpText = $grip.Current.HelpText
    # By keyboard: Shift+Tab from the dialog's first field wraps to the grip, its last stop; Tab onward if not.
    Start-Sleep -Milliseconds 800
    TakeSpeech | Out-Null
    Chord ([byte[]]@(0x10, 0x09))
    $presses = 0
    while ($presses -lt 40) {
        Start-Sleep -Milliseconds 700
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($focused -and $focused.Current.Name -eq 'Resize dialog' -and $focused.Current.ProcessId -eq $owned.Id) { break }
        Key 0x09
        $presses++
    }
    if ($presses -ge 40) { throw 'The resize grip is not reachable by Tab' }
    $result.TabPressesAfterShiftTab = $presses
    Start-Sleep -Milliseconds 800
    $speech = @(TakeSpeech)
    $result.FocusSpeech = @($speech | Select-Object -Last 2)
    $spoken = ($speech | Select-Object -Last 1) -join ' '
    if ($spoken -notmatch 'Resize dialog' -or $spoken -notmatch 'arrow keys') {
        throw 'NVDA did not speak the grip name and its arrow-key help'
    }
    $before = $card.Current.BoundingRectangle.Width
    foreach ($i in 1..3) { Key 0x27 $true } # Right
    Start-Sleep -Milliseconds 600
    $after = $card.Current.BoundingRectangle.Width
    $result.WidthBefore = $before
    $result.WidthAfter = $after
    $result.ArrowSpeech = @(TakeSpeech)
    if ($after -lt $before + 30) { throw "Right arrows did not widen the dialog ($before to $after)" }
    $result.Stage = 'close'
    Key 0x1B # Escape
    Await { -not (Named $find 'Resize dialog') } 'the dialog to close' | Out-Null
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
    $result.DebugSpeech = try { @(TakeSpeech) } catch { @() }
} finally {
    if ($owned -and -not $owned.HasExited) { $owned.Kill(); $owned.WaitForExit(10000) | Out-Null }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(10000) | Out-Null }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (-not $result.Passed) { throw $result.Error }
