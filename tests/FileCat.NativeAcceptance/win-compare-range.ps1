param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $ReportPath,
    [string] $NvdaPath
)

# Native 640 x 320 Compare route in the logged-in Windows validation VM.
# Uses only new, test-owned files and a dedicated FileCat data directory.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatCompareRangeInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
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
    $bounds = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $bounds.Width -lt 8 -or $bounds.Height -lt 8) {
        throw "Pointer target is not reachable: $($element.Current.Name)"
    }
    $x = [int][Math]::Round($bounds.X + $bounds.Width / 2)
    $y = [int][Math]::Round($bounds.Y + $bounds.Height / 2)
    if (-not [FileCatCompareRangeInput]::SetCursorPos($x, $y)) { throw 'Could not move guest pointer' }
    Start-Sleep -Milliseconds 80
    [FileCatCompareRangeInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [FileCatCompareRangeInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
function ClipboardText([string] $expected) {
    Await {
        $actual = [Windows.Forms.Clipboard]::GetText()
        if ($actual -eq $expected) { $actual }
    } "exact clipboard text '$expected'" 10
}
function TakeSpeech {
    if (-not $script:nvdaLog -or -not (Test-Path -LiteralPath $script:nvdaLog)) { return @() }
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    return @($fresh | Where-Object { $_ -match 'Speaking \[' } |
        ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) {
        [FileCatCompareRangeInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
    }
    [array]::Reverse($codes)
    foreach ($code in $codes) {
        [FileCatCompareRangeInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
    }
}
function OpenCompare([string] $name, [string] $left, [string] $right, [string] $data) {
    $process = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    $main = Await {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'owned FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'Unexpected executable owns the FileCat window'
    }
    $row = Await {
        @(Descendants $main | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -eq $name
        } | Select-Object -First 1)[0]
    } "source file $name"
    [FileCatCompareRangeInput]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    $script:result.Text.MainWindow = $main.Current.Name
    $script:result.Text.RowBounds = @($row.Current.BoundingRectangle.X, $row.Current.BoundingRectangle.Y,
        $row.Current.BoundingRectangle.Width, $row.Current.BoundingRectangle.Height)
    Click $row
    $script:result.Text.Foreground = [FileCatCompareRangeInput]::SetForegroundWindow($process.MainWindowHandle)
    Start-Sleep -Milliseconds 300
    Chord ([byte[]]@(0x11, 0x49)) # Ctrl+I, Compare files
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $compare = Await {
        @($desktop.FindAll([System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                $_.Current.ProcessId -eq $process.Id -and
                $_.Current.Name.StartsWith('Compare: ' + $name)
            } | Select-Object -First 1)[0]
    } "Compare window for $name" 30
    $handle = [IntPtr]$compare.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { throw 'Compare has no native window handle' }
    [FileCatCompareRangeInput]::ShowWindow($handle, 9) | Out-Null
    if (-not [FileCatCompareRangeInput]::SetWindowPos($handle, [IntPtr]::Zero,
        60, 60, 640, 320, 0x0040)) { throw 'Could not size Compare to 640 x 320' }
    [FileCatCompareRangeInput]::SetForegroundWindow($handle) | Out-Null
    return @{ Process = $process; Window = $compare; Handle = $handle }
}

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    -not $report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    -not (Test-Path -LiteralPath $app -PathType Leaf) -or
    (Test-Path -LiteralPath $report)) {
    throw 'Use an isolated Git-built FileCat.exe and a fresh validation report'
}
$fixture = Join-Path $validation ('CompareRangeFixture\' + [Guid]::NewGuid().ToString('N'))
$left = Join-Path $fixture 'left'
$right = Join-Path $fixture 'right'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $left, $right, $data | Out-Null
$textName = 'range-compare.txt'
$binaryName = 'range-compare.bin'
$textLeft = Join-Path $left $textName
$textRight = Join-Path $right $textName
$binaryLeft = Join-Path $left $binaryName
$binaryRight = Join-Path $right $binaryName
[IO.File]::WriteAllText($textLeft, "one changed`nsame`nlast left`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($textRight, "one CHANGED`nsame`nlast right`n", [Text.UTF8Encoding]::new($false))
$bytesLeft = [byte[]](0..127)
$bytesRight = [byte[]](0..127)
$bytesRight[15] = 0xAA; $bytesRight[16] = 0xBB
$bytesRight[17] = 0xCC; $bytesRight[18] = 0xDD
[IO.File]::WriteAllBytes($binaryLeft, $bytesLeft)
[IO.File]::WriteAllBytes($binaryRight, $bytesRight)
$sources = @($textLeft, $textRight, $binaryLeft, $binaryRight)
$hashes = @($sources | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
$nvda = $null
if ($NvdaPath) {
    $nvda = [IO.Path]::GetFullPath($NvdaPath)
    if (-not $nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
        -not (Test-Path -LiteralPath $nvda -PathType Leaf) -or
        (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or
        (Get-Process -Name nvda -ErrorAction SilentlyContinue)) {
        throw 'Require signed validation NVDA and no competing NVDA process'
    }
}
$result = [ordered]@{
    Passed = $false; Stage = 'starting';
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    Fixture = $fixture; Text = @{}; Hex = @{}; Error = $null
}
$owned = $nvdaProcess = $null
$script:nvdaLog = $null
$script:speechIndex = 0
try {
    if ($nvda) {
        $nvdaConfig = Join-Path $fixture 'nvda-config'
        New-Item -ItemType Directory -Path $nvdaConfig | Out-Null
        [IO.File]::WriteAllText((Join-Path $nvdaConfig 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
        $script:nvdaLog = Join-Path $fixture 'nvda.log'
        $nvdaProcess = Start-Process -FilePath $nvda -ArgumentList @(
            "--log-file=$script:nvdaLog", '--log-level=12',
            "--config-path=$nvdaConfig", '--disable-addons') -PassThru -WindowStyle Hidden
        Await {
            if ($nvdaProcess.HasExited) { throw 'Owned NVDA exited before initialization' }
            if ((Test-Path -LiteralPath $script:nvdaLog) -and
                (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
        } 'NVDA initialization' 30 | Out-Null
        $result.NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion
        TakeSpeech | Out-Null
    }
    $result.Stage = 'text compare'
    $owned = OpenCompare $textName $left $right $data
    $window = $owned.Window
    $cell = Await { Named $window 'Left source line 1' } 'changed left source line'
    Click $cell
    $copy = Await { Named $window 'Copy line' } 'Copy line action'
    Click $copy
    $result.Text.Clipboard = ClipboardText 'one changed'
    $find = Await { Named $window 'Show or hide comparison Find bar' } 'Find toggle'
    Click $find
    $search = Await { Named $window 'Find text in comparison (Ctrl+F)' } 'Find text field'
    $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('changed')
    $next = Await { Named $window 'Next match' } 'Next match action'
    Await { if ($next.Current.IsEnabled) { $true } } 'enabled Next match' | Out-Null
    Click $next
    $status = Await {
        @(Descendants $window | Where-Object {
            $_.Current.AutomationId -eq 'Comparison match status' -and
            $_.Current.Name -match 'Match 1 of 2|1 match'
        } | Select-Object -First 1)[0]
    } 'comparison match status'
    $result.Text.MatchStatus = $status.Current.Name
    if ($nvda) {
        Start-Sleep -Milliseconds 700
        $result.Text.Speech = @(TakeSpeech)
    }
    $result.Text.Window = @($window.Current.BoundingRectangle.Width, $window.Current.BoundingRectangle.Height)
    $owned.Process.Kill(); $owned.Process.WaitForExit(10000) | Out-Null
    $owned = $null

    $result.Stage = 'binary compare'
    $owned = OpenCompare $binaryName $left $right $data
    $window = $owned.Window
    Chord ([byte[]]@(0x12, 0x42)) # Alt+B, select source byte range
    $side = Await { Named $window 'Source side for byte selection' } 'byte-range side selector'
    $offset = Await { Named $window 'Start offset in decimal or 0x hexadecimal' } 'byte offset'
    $count = Await { Named $window 'Number of source bytes in decimal' } 'byte count'
    $side.SetFocus()
    if ($nvda) {
        $side.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $rightOption = Await {
            $desktop = [System.Windows.Automation.AutomationElement]::RootElement
            @($desktop.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                    $_.Current.ProcessId -eq $owned.Process.Id -and
                    $_.Current.Name -eq 'Right file'
                } | Select-Object -First 1)[0]
        } 'right source popup option' 10
        Click $rightOption
    } else {
        Chord ([byte[]]@(0x28)) # Down, from default left to right source
    }
    $sourcePath = Await { Named $window 'Full path of selected source file' } 'selected source path'
    $result.Hex.SourcePath = Await {
        $value = $sourcePath.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($value.EndsWith('\right\' + $binaryName, [StringComparison]::OrdinalIgnoreCase)) { $value }
    } 'right source selection'
    $offset.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('0xF')
    $count.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('4')
    $count.SetFocus()
    Chord ([byte[]]@(0x0D)) # Enter, confirm reviewed range
    $hex = Await {
        @(Descendants $window | Where-Object {
            $_.Current.ClassName -eq 'HexCompareView' -and
            $_.Current.Name -eq 'Bytes of both files'
        } | Select-Object -First 1)[0]
    } 'binary comparison document'
    $hex.SetFocus()
    Chord ([byte[]]@(0x11, 0x43)) # Ctrl+C, source bytes
    $result.Hex.Clipboard = Await {
        $value = [Windows.Forms.Clipboard]::GetText()
        if ($value -ne 'one changed') { $value }
    } 'updated binary clipboard' 10
    if ($result.Hex.Clipboard -ne 'AA BB CC DD') {
        throw "Incorrect source bytes copied: $($result.Hex.Clipboard)"
    }
    $result.Hex.Range = $hex.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($result.Hex.Range -notmatch 'Right file, offset 0xF, 4 selected bytes') {
        throw "Incorrect native byte-range evidence: $($result.Hex.Range)"
    }
    $result.Hex.Window = @($window.Current.BoundingRectangle.Width, $window.Current.BoundingRectangle.Height)
    if ($nvda) {
        Chord ([byte[]]@(0x2D, 0x09)) # NVDA+Tab announces the focused document
        Start-Sleep -Milliseconds 700
        $result.Hex.Speech = @(TakeSpeech)
        if (-not (($result.Text.Speech -join ' ') -match 'Match 1 of 2') -or
            -not (($result.Hex.Speech -join ' ') -match 'Right file.*0xF.*4 selected bytes')) {
            throw 'NVDA did not speak the comparison match and selected byte-range status'
        }
    }
    $result.Stage = 'source and destination integrity'
    for ($i = 0; $i -lt $sources.Count; $i++) {
        if ((Get-FileHash -LiteralPath $sources[$i] -Algorithm SHA256).Hash -ne $hashes[$i]) {
            throw "Compare changed source $($sources[$i])"
        }
    }
    if (@(Get-ChildItem -LiteralPath $data -File -Recurse).Count -gt 100) {
        throw 'Unexpected writes in isolated Compare data directory'
    }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
} finally {
    if ($owned -and -not $owned.Process.HasExited) {
        $owned.Process.Kill(); $owned.Process.WaitForExit(10000) | Out-Null
    }
    if ($nvdaProcess -and -not $nvdaProcess.HasExited) {
        $nvdaProcess.Kill(); $nvdaProcess.WaitForExit(10000) | Out-Null
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (-not $result.Passed) { throw $result.Error }
