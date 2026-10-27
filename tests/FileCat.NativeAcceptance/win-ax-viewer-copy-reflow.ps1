param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Run in the isolated, signed-in Windows validation desktop. Own the source,
# FileCat process and clipboard sentinel; leave other windows untouched.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatViewerNativeInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after,
        int x, int y, int width, int height, uint flags);
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -PathType Leaf) -or
    !$report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $report)) { throw 'Use a Git-built isolated app and fresh validation report.' }
if (@(Get-CimInstance Win32_Process -Filter "Name = 'FileCat.exe'").Count) {
    throw 'Another FileCat process is running in this validation guest.'
}

function Descendants($owner) {
    @($owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Named($owner, [string]$name) {
    @(Descendants $owner | Where-Object { $_.Current.Name -eq $name -and !$_.Current.IsOffscreen })
}
function One($items, [string]$description) {
    $matches = @($items)
    if ($matches.Count -ne 1) { throw "Expected one $description, found $($matches.Count)." }
    $matches[0]
}
function Until([scriptblock]$test, [string]$description, [int]$seconds = 15) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $seconds) {
        $found = & $test
        if ($found) { return $found }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $description."
}
function Invoke($control, [string]$description) {
    $pattern = $null
    if (!$control.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,
        [ref]$pattern)) { throw "$description has no UIA Invoke pattern." }
    $pattern.Invoke()
}
function Value($control) {
    ([System.Windows.Automation.ValuePattern]$control.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
}
function FocusViewer($viewer, $document) {
    $handle = [IntPtr]$viewer.Current.NativeWindowHandle
    [FileCatViewerNativeInput]::ShowWindow($handle, 9) | Out-Null
    [FileCatViewerNativeInput]::SetForegroundWindow($handle) | Out-Null
    $document.SetFocus()
    Start-Sleep -Milliseconds 120
    if ([FileCatViewerNativeInput]::GetForegroundWindow() -ne $handle) {
        throw 'The isolated viewer does not own native keyboard input.'
    }
}
function ResizeViewer($viewer, [int]$width, [int]$height) {
    $handle = [IntPtr]$viewer.Current.NativeWindowHandle
    if (![FileCatViewerNativeInput]::SetWindowPos($handle, [IntPtr]::Zero,
            60, 60, $width, $height, 0x0040)) { throw 'Native viewer resize failed.' }
    Start-Sleep -Milliseconds 250
    $bounds = $viewer.Current.BoundingRectangle
    # UIA reports the Avalonia client frame; SetWindowPos sizes the Win32 outer
    # window, which includes a title bar and borders on this guest theme.
    if ($bounds.Width -lt $width - 45 -or $bounds.Width -gt $width + 24 -or
        $bounds.Height -lt $height - 45 -or $bounds.Height -gt $height + 24) {
        throw "Viewer did not settle at ${width}x${height}: $bounds"
    }
    @([int]$bounds.Width, [int]$bounds.Height)
}

$fixture = Join-Path $validation ('ViewerCopyReflow\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$phrase = 'caf' + [char]0xE9 + ' ' + [char]::ConvertFromUtf32(0x1F363)
$content = ('A' * 180) + ' ' + $phrase + ' ' + ('B' * 180) + "`r`n"
$bytes = [Text.Encoding]::UTF8.GetBytes($content)
$phraseBytes = [Text.Encoding]::UTF8.GetBytes($phrase)
$offset = 181
$file = Join-Path $source 'mixed-source.txt'
[IO.File]::WriteAllBytes($file, $bytes)
$result = [ordered]@{
    passed = $false
    fixture = $fixture
    appDllSha256 = (Get-FileHash (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    sourceSha256 = (Get-FileHash $file -Algorithm SHA256).Hash
    phraseOffset = $offset
    phraseByteCount = $phraseBytes.Length
}
$originalClipboard = $null
$process = $null
try {
    try { $originalClipboard = [Windows.Forms.Clipboard]::GetText() } catch {}
    [Windows.Forms.Clipboard]::SetText('FileCat native Copy sentinel')
    $process = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $source, '--right', $target)
    $main = Until {
        @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.ProcessId -eq $process.Id -and $_.Current.Name.StartsWith('FileCat') }) |
            Select-Object -First 1
    } 'test-owned FileCat window' 35
    $row = Until {
        @(Descendants $main | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -eq 'mixed-source.txt' }) | Select-Object -First 1
    } 'test-owned source row'
    FocusViewer $main $row
    $selection = $null
    if ($row.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref]$selection)) { $selection.Select() }
    [Windows.Forms.SendKeys]::SendWait('{F3}')
    $viewer = Until {
        @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.ProcessId -eq $process.Id -and
                $_.Current.Name.StartsWith('mixed-source.txt ') -and
                $_.Current.Name.EndsWith('FileCat Viewer') }) | Select-Object -First 1
    } 'test-owned viewer'
    Until { @(Named $viewer 'Mode: Text').Count -eq 1 } 'Text mode' | Out-Null
    Invoke (One (Named $viewer 'Show or hide viewer Find bar') 'Find toggle') 'Find toggle'
    $query = Until { @(Named $viewer 'Find in file') | Select-Object -First 1 } 'Find query'
    ([System.Windows.Automation.ValuePattern]$query.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue($phrase)
    Invoke (One (Named $viewer 'Find next match') 'Find Next button') 'Find Next'
    $text = Until {
        @(Descendants $viewer | Where-Object { $_.Current.ClassName -eq 'TextViewer' -and
            !$_.Current.IsOffscreen }) | Select-Object -First 1
    } 'read-only Text document'
    Until { if ((Value $text).Contains($phrase)) { $true } } 'UTF-8 match in Text viewport' | Out-Null
    FocusViewer $viewer $text
    [Windows.Forms.SendKeys]::SendWait('^a')
    $expectedText = "Selected source bytes 0 through $($bytes.Length - 1), $($bytes.Length) bytes"
    Until { if ($text.Current.HelpText.Contains($expectedText)) { $true } } 'explicit whole-source selection' | Out-Null
    $textValue = [System.Windows.Automation.ValuePattern]$text.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    if (!$textValue.Current.IsReadOnly) { throw 'Text document is exposed as editable.' }
    $wrap = @(Named $viewer 'Wrap') | Select-Object -First 1
    if ($wrap -and $wrap.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne
            [System.Windows.Automation.ToggleState]::On) { $wrap.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    1..2 | ForEach-Object { Invoke (One (Named $viewer 'Increase text and hex size') 'larger text button') 'larger text' }
    $result.textSizes = [ordered]@{}
    foreach ($case in @(@('narrow', 480, 300), @('wide', 800, 550))) {
        $result.textSizes[$case[0]] = ResizeViewer $viewer $case[1] $case[2]
        if (!$text.Current.HelpText.Contains($expectedText)) { throw "Text selection lost after $($case[0]) resize." }
    }
    $result.textSelectionStatus = $text.Current.HelpText
    FocusViewer $viewer $text
    [Windows.Forms.SendKeys]::SendWait('^c')
    Until { if ([Windows.Forms.Clipboard]::GetText() -ceq $content) { $true } } 'exact source text on Windows clipboard' | Out-Null
    $result.textClipboardExact = $true

    $hexMode = One (Named $viewer 'Hex') 'Hex mode button'
    $modeToggle = $null
    if ($hexMode.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern,
            [ref]$modeToggle)) { $modeToggle.Toggle() }
    else { Invoke $hexMode 'Hex mode' }
    Until { @(Named $viewer 'Mode: Hex').Count -eq 1 } 'Hex mode' | Out-Null
    $query = One (Named $viewer 'Find in file') 'Hex Find query'
    $hexBytes = One (Named $viewer 'Hex bytes') 'Hex byte query checkbox'
    if ($hexBytes.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne
            [System.Windows.Automation.ToggleState]::On) { $hexBytes.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    $hex = [BitConverter]::ToString($phraseBytes).Replace('-', '')
    ([System.Windows.Automation.ValuePattern]$query.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue(($hex -replace '(..)(?!$)', '$1 '))
    Invoke (One (Named $viewer 'Find next match') 'Hex Find Next button') 'Hex Find Next'
    $document = Until {
        @(Descendants $viewer | Where-Object { $_.Current.ClassName -eq 'HexView' -and
            !$_.Current.IsOffscreen }) | Select-Object -First 1
    } 'read-only Hex document'
    $expectedHex = "Selected bytes $offset through $($offset + $phraseBytes.Length - 1), $($phraseBytes.Length) bytes"
    Until { if ((Value $document).Contains($expectedHex)) { $true } } 'exact Hex source range' | Out-Null
    $hexValue = [System.Windows.Automation.ValuePattern]$document.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    if (!$hexValue.Current.IsReadOnly) { throw 'Hex document is exposed as editable.' }
    $result.hexSizes = [ordered]@{}
    foreach ($case in @(@('narrow', 480, 300), @('wide', 800, 550))) {
        $result.hexSizes[$case[0]] = ResizeViewer $viewer $case[1] $case[2]
        if (!(Value $document).Contains($expectedHex)) { throw "Hex selection lost after $($case[0]) resize." }
    }
    $result.hexSelectionStatus = Value $document
    FocusViewer $viewer $document
    [Windows.Forms.SendKeys]::SendWait('^c')
    Until { if ([Windows.Forms.Clipboard]::GetText() -ceq $hex) { $true } } 'exact Hex source bytes on Windows clipboard' | Out-Null
    $result.hexClipboard = $hex
    if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $result.sourceSha256 -or
        @(Get-ChildItem -LiteralPath $target).Count) { throw 'Viewer Copy changed the owned source or target.' }
    $result.sourceUnchanged = $true
    $result.targetEmpty = $true
    $result.passed = $true
} catch {
    $result.error = $_.Exception.ToString()
} finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(10000)) { $process.Kill() }
    }
    if ($null -ne $originalClipboard) {
        try { [Windows.Forms.Clipboard]::SetText($originalClipboard) } catch {}
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
if (!$result.passed) { throw $result.error }
