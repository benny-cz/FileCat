param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Run only inside the isolated, logged-in Windows VMware guest. Uses an
# elevated VMware guest token and a fresh --data root; no product test hook.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatNoticeNative {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$preflight = [ordered]@{
    VMware = (Get-CimInstance Win32_ComputerSystem).Manufacturer -like '*VMware*'
    Elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    InteractiveSession = (Get-Process -Id $PID).SessionId -ne 0
    AppWithinValidation = $app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
    AppExists = (Test-Path -LiteralPath $app -PathType Leaf)
    Idle = !(Get-Process -Name FileCat -ErrorAction SilentlyContinue)
}
if (!$preflight.VMware -or !$preflight.Elevated -or !$preflight.InteractiveSession -or
    !$preflight.AppWithinValidation -or !$preflight.AppExists -or !$preflight.Idle -or
    !$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report)) {
    if ([IO.Path]::GetDirectoryName($report) -ieq $validation -and !(Test-Path -LiteralPath $report)) {
        @{ Passed = $false; Stage = 'preflight'; Checks = $preflight } |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding UTF8
    }
    throw 'Use an elevated, idle Windows VMware guest, a Git-built app and a fresh validation report.'
}

function Wait-For([scriptblock]$check, [string]$description, [int]$seconds = 12) {
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
function Descendants($owner) {
    @($owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Find-Name($owner, [string]$needle, $type = $null) {
    @(Descendants $owner | Where-Object {
        $_.Current.Name -like "*$needle*" -and !$_.Current.IsOffscreen -and
        (!$type -or $_.Current.ControlType -eq $type)
    } | Select-Object -First 1)[0]
}
function Find-Id($owner, [string]$id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $element = $owner.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($element -and !$element.Current.IsOffscreen) { $element }
}
function Button($owner, [string]$name) {
    Find-Name $owner $name ([System.Windows.Automation.ControlType]::Button)
}
function Invoke($element, [string]$description) {
    if (!$element -or !$element.Current.IsEnabled) { throw "$description is unavailable." }
    ([System.Windows.Automation.InvokePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}

$ownedRoot = Join-Path $validation 'AdminNotification'
$fixture = Join-Path $ownedRoot ([guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
@($data, $source, $target) | ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
[IO.File]::WriteAllBytes((Join-Path $source 'sentinel.txt'), [Text.Encoding]::UTF8.GetBytes("unchanged`n"))
$originalHash = (Get-FileHash -LiteralPath (Join-Path $source 'sentinel.txt') -Algorithm SHA256).Hash
$result = [ordered]@{ Passed = $false; Stage = 'startup'; Fixture = $fixture;
    AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash;
    Elevated = $true; Dpi = $null; WindowBounds = $null; Notice = $null;
    CopyButton = $null; HistoryButton = $null; DismissButton = $null;
    CopiedText = $null; Expired = $false; HistoryText = $null;
    SourceExact = $false; TargetEmpty = $false; Error = $null }
$process = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    $window = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'elevated FileCat window'
    $result.Dpi = [FileCatNoticeNative]::GetDpiForWindow($process.MainWindowHandle)
    [FileCatNoticeNative]::MoveWindow($process.MainWindowHandle, 40, 40, 1180, 860, $true) | Out-Null
    [FileCatNoticeNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [FileCatNoticeNative]::SetCursorPos(10, 10) | Out-Null
    $rect = $window.Current.BoundingRectangle
    $result.WindowBounds = @($rect.Left, $rect.Top, $rect.Width, $rect.Height)
    $result.Stage = 'administrator notice and accessible controls'
    $notice = Wait-For { Find-Id $window 'NotificationInfoText' } 'administrator notice' 8
    $result.Notice = $notice.Current.Name
    foreach ($pair in @(@('CopyButton','NotificationCopyButton'),
                       @('HistoryButton','NotificationHistoryButton'),
                       @('DismissButton','NotificationCloseButton'))) {
        $button = Wait-For { Find-Id $window $pair[1] } $pair[1] 2
        $result[$pair[0]] = [ordered]@{ Name = $button.Current.Name;
            Enabled = $button.Current.IsEnabled; Bounds = @($button.Current.BoundingRectangle.Left,
                $button.Current.BoundingRectangle.Top, $button.Current.BoundingRectangle.Width,
                $button.Current.BoundingRectangle.Height) }
    }
    Invoke (Find-Id $window 'NotificationCopyButton') 'Copy notification'
    Start-Sleep -Milliseconds 300
    $result.CopiedText = Get-Clipboard -Raw
    $result.Stage = 'timed expiration after moving focus and pointer away'
    $list = Wait-For { Find-Name $window 'Panel 1,' ([System.Windows.Automation.ControlType]::List) } 'source list'
    $list.SetFocus()
    [FileCatNoticeNative]::SetCursorPos(10, 10) | Out-Null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt 12) {
        if (!(Find-Id $window 'NotificationInfoText')) { $result.Expired = $true; break }
        Start-Sleep -Milliseconds 250
    }
    $result.Stage = 'history after expiration'
    [FileCatNoticeNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('^+p')
    $search = Wait-For { Find-Name $window 'Search commands' ([System.Windows.Automation.ControlType]::Edit) } 'command search'
    ([System.Windows.Automation.ValuePattern]$search.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue('Recent notifications')
    Wait-For { Find-Name $window 'Recent notifications' ([System.Windows.Automation.ControlType]::ListItem) } 'history command' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $history = Wait-For { Find-Name $window 'Recent notifications, newest first' ([System.Windows.Automation.ControlType]::Edit) } 'retained history'
    $result.HistoryText = ([System.Windows.Automation.ValuePattern]$history.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    $result.SourceExact = (Get-FileHash -LiteralPath (Join-Path $source 'sentinel.txt') -Algorithm SHA256).Hash -eq $originalHash
    $result.TargetEmpty = @(Get-ChildItem -LiteralPath $target -Force).Count -eq 0
    $result.Passed = ($result.Dpi -in @(144, 192) -and
        $result.Notice -like '*running as administrator*' -and
        @(@($result.CopyButton, $result.HistoryButton, $result.DismissButton) |
            Where-Object { $_.Enabled -and $_.Bounds[2] -gt 0 -and $_.Bounds[3] -gt 0 }).Count -eq 3 -and
        $result.CopiedText -like '*running as administrator*' -and
        $result.Expired -and $result.HistoryText -like '*running as administrator*' -and
        $result.SourceExact -and $result.TargetEmpty)
    $result.Stage = 'complete'
}
catch { $result.Error = $_.Exception.Message }
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (!$resolvedFixture.StartsWith(([IO.Path]::GetFullPath($ownedRoot) + [IO.Path]::DirectorySeparatorChar),
        [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped the owned validation root.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
if (!$result.Passed) { exit 1 }
