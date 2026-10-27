param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Native Win32 high-resolution wheel input over owned Text and Hex viewers.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatViewerWheelInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
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
$fixture = Join-Path $validation ('FractionalViewerFixture\' + $id)
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$file = Join-Path $source 'wheel-viewer.txt'
$content = (0..299 | ForEach-Object { 'line {0:d3} -- native wheel source' -f $_ }) -join "`n"
[IO.File]::WriteAllText($file, $content + "`n", [Text.UTF8Encoding]::new($false))
$originalHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
$result = [ordered]@{
    Passed = $false
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    File = $file
    Text = @()
    Hex = @()
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
function Document($viewer, [string]$className) {
    Wait-For { @(Descendants $viewer | Where-Object {
        $_.Current.ClassName -eq $className -and !$_.Current.IsOffscreen
    }) | Select-Object -First 1 } "$className document"
}
function VerticalBar($viewer, [string]$name) {
    $bar = Wait-For { @(Descendants $viewer | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ScrollBar -and
        $_.Current.Name -eq $name -and !$_.Current.IsOffscreen
    }) | Select-Object -First 1 } "$name scrollbar"
    return [System.Windows.Automation.RangeValuePattern]$bar.GetCurrentPattern(
        [System.Windows.Automation.RangeValuePattern]::Pattern)
}
function Point-At($viewer, $document) {
    $handle = [IntPtr]$viewer.Current.NativeWindowHandle
    [FileCatViewerWheelInput]::ShowWindow($handle, 9) | Out-Null
    [FileCatViewerWheelInput]::SetForegroundWindow($handle) | Out-Null
    $bounds = $document.Current.BoundingRectangle
    if ($bounds.Width -lt 100 -or $bounds.Height -lt 100 -or
        $bounds.Left -lt 0 -or $bounds.Top -lt 0) { throw 'Viewer document has no on-screen wheel target.' }
    if (![FileCatViewerWheelInput]::SetCursorPos([int]($bounds.Left + 80),
            [int]($bounds.Top + 80))) { throw 'Could not target the viewer document.' }
    Start-Sleep -Milliseconds 250
}
function Wheel([int]$delta, [int]$times) {
    for ($i = 0; $i -lt $times; $i++) {
        [FileCatViewerWheelInput]::Wheel($delta)
        Start-Sleep -Milliseconds 35
    }
    Start-Sleep -Milliseconds 250
}
function Text-Top($document) {
    $value = ([System.Windows.Automation.ValuePattern]$document.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    $match = [regex]::Match($value, '^line (\d{3})', 'Multiline')
    if (!$match.Success) { throw "Text viewport lacks the first numbered line: $($value.Substring(0,[Math]::Min(80,$value.Length)))" }
    [int]$match.Groups[1].Value
}
function Hex-Top($bar) { [int][Math]::Round($bar.Current.Value) }

try {
    $process = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $source, '--right', $target)
    $main = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'owned FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'An unexpected executable owns the FileCat window.'
    }
    $row = Wait-For { @(Descendants $main | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq 'wheel-viewer.txt'
    }) | Select-Object -First 1 } 'owned viewer source row'
    [FileCatViewerWheelInput]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    [FileCatViewerWheelInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $row.SetFocus()
    [Windows.Forms.SendKeys]::SendWait('{F3}')
    $viewer = Wait-For {
        @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                $_.Current.ProcessId -eq $process.Id -and
                $_.Current.Name.StartsWith('wheel-viewer.txt ') -and
                $_.Current.Name.EndsWith('FileCat Viewer')
            }) | Select-Object -First 1
    } 'owned viewer window'
    $text = Document $viewer 'TextViewer'
    $textBar = VerticalBar $viewer 'Text vertical position'
    Point-At $viewer $text
    $textBar.SetValue(0)
    if ((Text-Top $text) -ne 0) { throw 'Text viewer did not start at line zero.' }
    Wheel -12 10
    $textSmall = Text-Top $text
    $result.Text += [ordered]@{ Input = 'ten 12-unit downward events'; TopLine = $textSmall }
    $textBar.SetValue(0)
    Wait-For { if ((Text-Top $text) -eq 0) { $true } } 'Text viewport reset' 5 | Out-Null
    Wheel -120 1
    $textWhole = Text-Top $text
    $result.Text += [ordered]@{ Input = 'one 120-unit downward event'; TopLine = $textWhole }
    if ($textSmall -ne 3 -or $textWhole -ne $textSmall) {
        throw "Text fractional and whole movement differ: $textSmall, $textWhole"
    }
    Wheel 12 10
    $textReverse = Text-Top $text
    $result.Text += [ordered]@{ Input = 'ten 12-unit upward events'; TopLine = $textReverse }
    if ($textReverse -ne 0) { throw "Text reverse movement failed: $textReverse" }

    $hexButton = Wait-For { @(Descendants $viewer | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -eq 'Hex' -and !$_.Current.IsOffscreen
    }) | Select-Object -First 1 } 'Hex mode button'
    $toggle = $null
    if ($hexButton.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern,
            [ref]$toggle)) { $toggle.Toggle() }
    else {
        ([System.Windows.Automation.InvokePattern]$hexButton.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    }
    $hex = Document $viewer 'HexView'
    $hexBar = VerticalBar $viewer 'Hex vertical position'
    Point-At $viewer $hex
    $hexBar.SetValue(0)
    if ((Hex-Top $hexBar) -ne 0) { throw 'Hex viewer did not start at row zero.' }
    Wheel -12 10
    $hexSmall = Hex-Top $hexBar
    $result.Hex += [ordered]@{ Input = 'ten 12-unit downward events'; TopRow = $hexSmall }
    $hexBar.SetValue(0)
    Wait-For { if ((Hex-Top $hexBar) -eq 0) { $true } } 'Hex viewport reset' 5 | Out-Null
    Wheel -120 1
    $hexWhole = Hex-Top $hexBar
    $result.Hex += [ordered]@{ Input = 'one 120-unit downward event'; TopRow = $hexWhole }
    if ($hexSmall -ne 3 -or $hexWhole -ne $hexSmall) {
        throw "Hex fractional and whole movement differ: $hexSmall, $hexWhole"
    }
    Wheel 12 10
    $hexReverse = Hex-Top $hexBar
    $result.Hex += [ordered]@{ Input = 'ten 12-unit upward events'; TopRow = $hexReverse }
    if ($hexReverse -ne 0) { throw "Hex reverse movement failed: $hexReverse" }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $originalHash -or
        @(Get-ChildItem -LiteralPath $target -Force).Count -ne 0) {
        throw 'Viewer input changed the owned source or target.'
    }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit(10000) | Out-Null }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
