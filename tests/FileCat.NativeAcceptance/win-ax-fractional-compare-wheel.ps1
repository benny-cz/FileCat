param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Native Win32 precision-wheel messages in an owned binary Compare window.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatCompareWheelInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr handle, IntPtr after,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, int data, UIntPtr extra);
    public static void Wheel(int delta) { mouse_event(0x0800, 0, 0, delta, UIntPtr.Zero); }
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !$report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    (Test-Path -LiteralPath $report)) {
    throw 'Use an isolated Git-built FileCat.exe and fresh validation report.'
}
$id = [guid]::NewGuid().ToString('N')
$fixture = Join-Path $validation ('FractionalCompareFixture\' + $id)
$left = Join-Path $fixture 'left'
$right = Join-Path $fixture 'right'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $left, $right, $data | Out-Null
$name = 'wheel-compare.bin'
$leftFile = Join-Path $left $name
$rightFile = Join-Path $right $name
$leftBytes = New-Object byte[] (16 * 300)
$rightBytes = New-Object byte[] (16 * 300)
$rightBytes[512] = 0x2A
[IO.File]::WriteAllBytes($leftFile, $leftBytes)
[IO.File]::WriteAllBytes($rightFile, $rightBytes)
$leftHash = (Get-FileHash -LiteralPath $leftFile -Algorithm SHA256).Hash
$rightHash = (Get-FileHash -LiteralPath $rightFile -Algorithm SHA256).Hash
$result = [ordered]@{
    Passed = $false
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    LeftSha256 = $leftHash
    RightSha256 = $rightHash
    States = @()
    Error = $null
}
$process = $null

function Descendants($owner) {
    @($owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Wait-For([scriptblock]$condition, [string]$description, [int]$seconds = 25) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $condition; if ($null -ne $value -and $value -ne $false) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 125
    }
    throw "Timed out waiting for $description"
}
function Row($bar) { [int][Math]::Round($bar.Current.Value) }
function Send-Sequence($bar, [int]$delta, [int]$times, [string]$name) {
    for ($i = 0; $i -lt $times; $i++) {
        [FileCatCompareWheelInput]::Wheel($delta)
        Start-Sleep -Milliseconds 35
    }
    Start-Sleep -Milliseconds 250
    $top = Row $bar
    $script:result.States += [ordered]@{ Name = $name; Delta = $delta; Events = $times; TopRow = $top }
    return $top
}

try {
    $process = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    $main = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'owned FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'An unexpected executable owns the FileCat window.'
    }
    $sourceList = Wait-For { @(Descendants $main | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
        $_.Current.Name -like 'Panel 1,*'
    }) | Select-Object -First 1 } 'left source list'
    $row = Wait-For { @(Descendants $sourceList | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq $name
    }) | Select-Object -First 1 } 'left binary file'
    [FileCatCompareWheelInput]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    [FileCatCompareWheelInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $row.SetFocus()
    [Windows.Forms.SendKeys]::SendWait('^i')
    $compare = Wait-For {
        @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                $_.Current.ProcessId -eq $process.Id -and
                $_.Current.Name.StartsWith('Compare: wheel-compare.bin')
            }) | Select-Object -First 1
    } 'owned binary Compare window' 30
    $handle = [IntPtr]$compare.Current.NativeWindowHandle
    [FileCatCompareWheelInput]::ShowWindow($handle, 9) | Out-Null
    [FileCatCompareWheelInput]::SetWindowPos($handle, [IntPtr]::Zero,
        60, 60, 1000, 700, 0x0040) | Out-Null
    [FileCatCompareWheelInput]::SetForegroundWindow($handle) | Out-Null
    $hex = Wait-For { @(Descendants $compare | Where-Object {
        $_.Current.ClassName -eq 'HexCompareView' -and !$_.Current.IsOffscreen
    }) | Select-Object -First 1 } 'binary comparison document' 30
    $bar = Wait-For { @(Descendants $hex | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ScrollBar -and
        !$_.Current.IsOffscreen
    }) | Select-Object -First 1 } 'binary comparison scrollbar'
    $range = [System.Windows.Automation.RangeValuePattern]$bar.GetCurrentPattern(
        [System.Windows.Automation.RangeValuePattern]::Pattern)
    Wait-For { if ($range.Current.Maximum -gt 3) { $true } } 'comparison scrollable range' | Out-Null
    $bounds = $hex.Current.BoundingRectangle
    if ($bounds.Width -lt 100 -or $bounds.Height -lt 100 -or
        $bounds.Left -lt 0 -or $bounds.Top -lt 0) { throw 'Binary comparison has no on-screen wheel target.' }
    if (![FileCatCompareWheelInput]::SetCursorPos([int]($bounds.Left + 80),
            [int]($bounds.Top + 80))) { throw 'Could not target the binary comparison.' }
    Start-Sleep -Milliseconds 250
    $range.SetValue(0)
    Wait-For { if ((Row $range) -eq 0) { $true } } 'comparison viewport reset' 5 | Out-Null
    $small = Send-Sequence $range -12 10 'ten fractional downward events'
    $range.SetValue(0)
    Wait-For { if ((Row $range) -eq 0) { $true } } 'comparison viewport reset' 5 | Out-Null
    $whole = Send-Sequence $range -120 1 'one whole downward event'
    if ($small -ne 3 -or $whole -ne $small) {
        throw "Comparison fractional and whole movement differ: $small, $whole"
    }
    $reverse = Send-Sequence $range 12 10 'ten fractional upward events'
    if ($reverse -ne 0) { throw "Comparison reverse movement failed: $reverse" }
    $detent = Send-Sequence $range -120 1 'conventional downward detent'
    $back = Send-Sequence $range 120 1 'conventional upward detent'
    if ($detent -ne 3 -or $back -ne 0) { throw "Conventional comparison wheel failed: $detent, $back" }
    if ((Get-FileHash -LiteralPath $leftFile -Algorithm SHA256).Hash -ne $leftHash -or
        (Get-FileHash -LiteralPath $rightFile -Algorithm SHA256).Hash -ne $rightHash) {
        throw 'Comparison changed an owned binary source.'
    }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit(10000) | Out-Null }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
