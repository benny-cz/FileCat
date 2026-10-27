param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Native Windows wheel messages at 12/120 and 120/120 of a detent. The
# isolated FileCat process, source, profile and report stay in Validation.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatFractionalWheelInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, int data, UIntPtr extra);
    public static void Wheel(int delta) { mouse_event(0x0800, 0, 0, delta, UIntPtr.Zero); }
}
'@

$validation = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$prefix = [IO.Path]::GetFullPath($validation).TrimEnd('\') + '\'
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    !$report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    [IO.Path]::GetDirectoryName($report) -ine [IO.Path]::GetFullPath($validation) -or
    (Test-Path -LiteralPath $report)) {
    throw 'Use the isolated Git-built FileCat.exe and a fresh validation report.'
}
$id = [guid]::NewGuid().ToString('N')
$source = Join-Path $validation ('FractionalWheelFixture\' + $id + '\source')
$target = Join-Path $validation ('FractionalWheelFixture\' + $id + '\target')
$data = Join-Path $validation ('FractionalWheelProfile\' + $id)
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
0..199 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $source ('item-{0:d3}.txt' -f $_)), "owned wheel source $($_)`n") }
$before = @((Get-ChildItem -LiteralPath $source -File | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}))
$result = [ordered]@{
    Passed = $false
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    Source = $source
    States = @()
    Error = $null
}
$process = $null

function Wait-For([scriptblock]$condition, [string]$description, [int]$seconds = 20) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $condition; if ($null -ne $value -and $value -ne $false) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 125
    }
    throw "Timed out waiting for $description"
}

function Top-Row($scroll, [int]$count) {
    $view = $scroll.Current.VerticalViewSize
    $capacity = [int][Math]::Round($count * $view / 100.0)
    $maximum = [Math]::Max(0, $count - $capacity)
    $percent = $scroll.Current.VerticalScrollPercent
    if ($percent -lt 0) { throw 'The file list is not vertically scrollable.' }
    return [int][Math]::Round($maximum * $percent / 100.0)
}

function Send-Sequence($scroll, [int]$count, [int]$delta, [int]$times, [string]$name) {
    for ($i = 0; $i -lt $times; $i++) {
        [FileCatFractionalWheelInput]::Wheel($delta)
        Start-Sleep -Milliseconds 35
    }
    Start-Sleep -Milliseconds 250
    $top = Top-Row $scroll $count
    $script:result.States += [ordered]@{ Name = $name; Delta = $delta; Events = $times; TopRow = $top }
    return $top
}

try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    $window = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'owned FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'An unexpected executable owns the FileCat window.'
    }
    $list = Wait-For {
        @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
                $_.Current.Name -like 'Panel 1,*'
            }) | Select-Object -First 1
    } 'first file list'
    $scroll = [System.Windows.Automation.ScrollPattern]$list.GetCurrentPattern(
        [System.Windows.Automation.ScrollPattern]::Pattern)
    Wait-For { if ($scroll.Current.VerticallyScrollable) { $true } } 'vertically scrollable list' | Out-Null
    $rect = $list.Current.BoundingRectangle
    if ($rect.Width -lt 80 -or $rect.Height -lt 80 -or $rect.Left -lt 0 -or $rect.Top -lt 0) {
        throw 'The owned list has no on-screen pointer target.'
    }
    [FileCatFractionalWheelInput]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    [FileCatFractionalWheelInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    if (![FileCatFractionalWheelInput]::SetCursorPos([int]($rect.Left + 60), [int]($rect.Top + 60))) {
        throw 'Could not position the pointer over the file list.'
    }
    Start-Sleep -Milliseconds 300
    $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 0)
    if ((Top-Row $scroll 200) -ne 0) { throw 'The owned list could not be reset to row zero.' }
    $small = Send-Sequence $scroll 200 -12 10 'ten fractional downward events'
    $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 0)
    $whole = Send-Sequence $scroll 200 -120 1 'one whole downward event'
    if ($small -ne 3 -or $whole -ne $small) {
        throw "Fractional and whole wheel movement differ: $small versus $whole"
    }
    $reverse = Send-Sequence $scroll 200 12 10 'ten fractional upward events'
    if ($reverse -ne 0) { throw "Reverse wheel movement did not return to row zero: $reverse" }
    $detent = Send-Sequence $scroll 200 -120 1 'conventional downward detent'
    if ($detent -ne 3) { throw "Conventional wheel did not move three rows: $detent" }
    $return = Send-Sequence $scroll 200 120 1 'conventional upward detent'
    if ($return -ne 0) { throw "Conventional reverse did not return to row zero: $return" }
    $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100)
    $last = Top-Row $scroll 200
    if ($last -lt 3) { throw 'The list has too few scrollable rows for the end boundary.' }
    $clamped = Send-Sequence $scroll 200 -12 10 'fractional input at list end'
    $backFromEnd = Send-Sequence $scroll 200 12 10 'reverse after list-end clamp'
    if ($clamped -ne $last -or $backFromEnd -ne $last - 3) {
        throw "List-end clamp or reversal failed: $last, $clamped, $backFromEnd"
    }
    $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 0)
    $atStart = Send-Sequence $scroll 200 12 10 'fractional input at list start'
    $forwardFromStart = Send-Sequence $scroll 200 -12 10 'reverse after list-start clamp'
    if ($atStart -ne 0 -or $forwardFromStart -ne 3) {
        throw "List-start clamp or reversal failed: $atStart, $forwardFromStart"
    }
    $after = @((Get-ChildItem -LiteralPath $source -File | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }))
    if (@(Compare-Object $before $after -SyncWindow 0).Count -ne 0 -or
        @(Get-ChildItem -LiteralPath $target -Force).Count -ne 0) {
        throw 'Wheel input changed the owned fixture.'
    }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit(10000) | Out-Null }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
