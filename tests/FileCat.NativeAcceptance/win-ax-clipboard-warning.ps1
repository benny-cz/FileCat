param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Isolated Windows VMware guest. Copies an existing HKCU Registry key through
# FileCat at 150% scale; no Registry writes or file operations are requested.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatClipboardWarningNative {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notlike '*VMware*' -or
    (Get-Process -Id $PID).SessionId -eq 0 -or
    !$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue) -or
    !(Test-Path 'HKCU:\Software\Microsoft')) {
    throw 'Use an idle interactive VMware guest, its Git-built app, and an existing HKCU key.'
}

function Wait-For([scriptblock]$check, [string]$description, [int]$seconds = 15) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $seconds) {
        try { $found = & $check; if ($found) { return $found } }
        catch {
            $inner = $_.Exception
            while ($inner -and $inner -isnot [Runtime.InteropServices.COMException]) { $inner = $inner.InnerException }
            if (!$inner) { throw }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $description."
}
function Find-Id($owner, [string]$id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $element = $owner.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($element -and !$element.Current.IsOffscreen) { $element }
}
function Find-Name($owner, [string]$name, $type) {
    $names = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $controls = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
    $condition = [System.Windows.Automation.AndCondition]::new($names, $controls)
    $element = $owner.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($element -and !$element.Current.IsOffscreen) { $element }
}
function Invoke($element, [string]$description) {
    if (!$element -or !$element.Current.IsEnabled) { throw "$description is unavailable." }
    ([System.Windows.Automation.InvokePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}

$fixture = Join-Path (Join-Path $validation 'ClipboardWarning') ([guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
$target = Join-Path $fixture 'target'
New-Item -ItemType Directory -Path $data, $target -Force | Out-Null
$sentinel = Join-Path $target 'sentinel.txt'
[IO.File]::WriteAllText($sentinel, "unchanged`n")
$targetHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
$clipboardOriginal = [System.Windows.Forms.Clipboard]::GetDataObject()
$clipboardMarker = 'FileCat AC-S2-029 clipboard sentinel ' + [guid]::NewGuid().ToString('N')
$result = [ordered]@{ Passed = $false; Stage = 'startup'; Fixture = $fixture;
    AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash;
    Dpi = $null; WindowBounds = $null; SelectedKey = $null; Warning = $null;
    ClipboardKept = $false; CopyButton = $null; HistoryButton = $null;
    DismissButton = $null; CopiedWarning = $null; HistoryText = $null;
    Dismissed = $false; TargetExact = $false; Error = $null }
$process = $null
try {
    [System.Windows.Forms.Clipboard]::SetText($clipboardMarker)
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', 'reg:HKCU\Software', '--right', $target) -PassThru
    $window = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'FileCat window'
    $result.Dpi = [FileCatClipboardWarningNative]::GetDpiForWindow($process.MainWindowHandle)
    [FileCatClipboardWarningNative]::MoveWindow($process.MainWindowHandle, 40, 40, 1180, 860, $true) | Out-Null
    [FileCatClipboardWarningNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $rect = $window.Current.BoundingRectangle
    $result.WindowBounds = @($rect.Left, $rect.Top, $rect.Width, $rect.Height)
    $result.Stage = 'Registry selection and clipboard warning'
    $row = Wait-For { Find-Name $window 'Microsoft' ([System.Windows.Automation.ControlType]::ListItem) } 'HKCU Software Microsoft key'
    ([System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    $row.SetFocus()
    $result.SelectedKey = $row.Current.Name
    [FileCatClipboardWarningNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('^c')
    $notice = Wait-For { Find-Id $window 'NotificationErrorText' } 'clipboard warning'
    $result.Warning = $notice.Current.Name
    $result.ClipboardKept = [System.Windows.Forms.Clipboard]::GetText() -ceq $clipboardMarker
    foreach ($pair in @(@('CopyButton','NotificationCopyButton'),
                       @('HistoryButton','NotificationHistoryButton'),
                       @('DismissButton','NotificationCloseButton'))) {
        $button = Wait-For { Find-Id $window $pair[1] } $pair[1]
        $rect = $button.Current.BoundingRectangle
        $result[$pair[0]] = [ordered]@{ Name = $button.Current.Name;
            Enabled = $button.Current.IsEnabled; Bounds = @($rect.Left,$rect.Top,$rect.Width,$rect.Height) }
    }
    Invoke (Find-Id $window 'NotificationCopyButton') 'Copy notification'
    Start-Sleep -Milliseconds 200
    $result.CopiedWarning = [System.Windows.Forms.Clipboard]::GetText()
    Invoke (Find-Id $window 'NotificationHistoryButton') 'Recent notifications'
    $history = Wait-For {
        $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
            $_.Current.Name -like 'Recent notifications, newest first*' })[0]
    } 'retained clipboard warning'
    $result.HistoryText = ([System.Windows.Automation.ValuePattern]$history.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Invoke (Find-Id $window 'NotificationCloseButton') 'Dismiss notification'
    $result.Dismissed = !(Find-Id $window 'NotificationErrorText')
    $result.TargetExact = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -eq $targetHash -and
        @(Get-ChildItem -LiteralPath $target -Force).Count -eq 1
    $result.Passed = ($result.Dpi -in @(144,192) -and
        $result.SelectedKey -eq 'Microsoft' -and
        $result.Warning -like '*selection includes items outside the file system*' -and
        $result.ClipboardKept -and
        @(@($result.CopyButton,$result.HistoryButton,$result.DismissButton) |
            Where-Object { $_.Enabled -and $_.Bounds[2] -gt 0 -and $_.Bounds[3] -gt 0 }).Count -eq 3 -and
        $result.CopiedWarning -like '*selection includes items outside the file system*' -and
        $result.HistoryText -like '*selection includes items outside the file system*' -and
        $result.Dismissed -and $result.TargetExact)
    $result.Stage = 'complete'
}
catch { $result.Error = $_.Exception.Message }
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
    if ($clipboardOriginal) { [System.Windows.Forms.Clipboard]::SetDataObject($clipboardOriginal, $true) }
    else { [System.Windows.Forms.Clipboard]::Clear() }
    $ownedRoot = [IO.Path]::GetFullPath((Join-Path $validation 'ClipboardWarning')) + [IO.Path]::DirectorySeparatorChar
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (!$resolvedFixture.StartsWith($ownedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture cleanup escaped the owned validation root.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
if (!$result.Passed) { exit 1 }
