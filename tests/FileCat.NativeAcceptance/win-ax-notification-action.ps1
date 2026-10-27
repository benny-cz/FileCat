param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Tests the real Restore previous workspace notification action at 150%.
# The named workspace and local files live only under a fresh --data fixture.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatActionNative {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
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
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an idle interactive VMware guest and a Git-built validation app.'
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
    $element = $owner.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new($names, $controls))
    if ($element -and !$element.Current.IsOffscreen) { $element }
}
function Notice($owner) {
    $element = Find-Id $owner 'NotificationInfoText'
    if ($element) { $element.Current.Name }
}
function Invoke($element, [string]$description) {
    if (!$element -or !$element.Current.IsEnabled) { throw "$description is unavailable." }
    ([System.Windows.Automation.InvokePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Tabs($owner) {
    $all = $owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -like '*, Unlocked tab, *' } | ForEach-Object { $_.Current.Name })
}
function Command($owner, $process, [string]$name) {
    [FileCatActionNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('^+p')
    $search = Wait-For { Find-Name $owner 'Search commands' ([System.Windows.Automation.ControlType]::Edit) } 'command search'
    ([System.Windows.Automation.ValuePattern]$search.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue($name)
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
}

$fixture = Join-Path (Join-Path $validation 'NotificationAction') ([guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
New-Item -ItemType Directory -Path $data, $source, $target -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'sentinel.txt'), "unchanged`n")
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $source 'sentinel.txt') -Algorithm SHA256).Hash
$workspaceName = 'notice-action-' + [guid]::NewGuid().ToString('N')
$workspaceFile = Join-Path (Join-Path $data 'workspaces') ($workspaceName + '.json')
$result = [ordered]@{ Passed = $false; Stage = 'startup'; Fixture = $fixture;
    AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash;
    Dpi = $null; WindowBounds = $null; WorkspaceName = $workspaceName;
    SavedTabs = $null; BeforeOpenTabs = $null; OpenedTabs = $null; RestoredTabs = $null;
    OpenNotice = $null; ActionButton = $null; RestoreNotice = $null;
    HistoryText = $null; SourceExact = $false; TargetEmpty = $false;
    DebugUi = $null; Error = $null }
$process = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    $window = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'FileCat window'
    $result.Dpi = [FileCatActionNative]::GetDpiForWindow($process.MainWindowHandle)
    [FileCatActionNative]::MoveWindow($process.MainWindowHandle, 40, 40, 1180, 860, $true) | Out-Null
    [FileCatActionNative]::SetCursorPos(10, 10) | Out-Null
    $rect = $window.Current.BoundingRectangle
    $result.WindowBounds = @($rect.Left,$rect.Top,$rect.Width,$rect.Height)

    $result.Stage = 'save two-tab workspace'
    Command $window $process 'Save workspace as'
    $prompt = Wait-For {
        Find-Name $window 'Name for the current panels, tabs, and targets:' ([System.Windows.Automation.ControlType]::Edit)
    } 'workspace name prompt'
    ([System.Windows.Automation.ValuePattern]$prompt.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue($workspaceName)
    Invoke (Wait-For { Find-Name $window 'Save as' ([System.Windows.Automation.ControlType]::Button) } 'Save as') 'Save as'
    Wait-For { Test-Path -LiteralPath $workspaceFile -PathType Leaf } 'owned saved workspace' | Out-Null
    $saved = Get-Content -LiteralPath $workspaceFile -Raw | ConvertFrom-Json
    $result.SavedTabs = @($saved.Panels | ForEach-Object { $_.Tabs }).Count

    $result.Stage = 'add an unsaved tab and reopen saved workspace'
    [FileCatActionNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('^t')
    $result.BeforeOpenTabs = @(Wait-For { $tabs = Tabs $window; if ($tabs.Count -eq 3) { $tabs } } 'third tab')
    Command $window $process 'Open workspace'
    Wait-For { Find-Name $window 'Open workspace' ([System.Windows.Automation.ControlType]::List) } 'workspace chooser' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $result.OpenNotice = Wait-For { $message = Notice $window; if ($message -like "*Workspace `"$workspaceName`" opened*") { $message } } 'open-workspace notice' 8
    $action = Wait-For { Find-Id $window 'RestoreWorkspaceNotificationButton' } 'restore action' 3
    $rect = $action.Current.BoundingRectangle
    $result.ActionButton = [ordered]@{ Name = $action.Current.Name; Enabled = $action.Current.IsEnabled;
        Bounds = @($rect.Left,$rect.Top,$rect.Width,$rect.Height) }
    $result.OpenedTabs = @(Tabs $window)
    Invoke $action 'Restore previous workspace'
    $result.RestoreNotice = Wait-For { $message = Notice $window; if ($message -like 'Previous workspace restored*') { $message } } 'restore notice' 8
    $result.RestoredTabs = @(Tabs $window)

    $result.Stage = 'retained action history and unchanged files'
    Invoke (Find-Id $window 'NotificationHistoryButton') 'notification History'
    $history = Wait-For {
        $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
            $_.Current.Name -like 'Recent notifications, newest first*' })[0]
    } 'retained action history'
    $result.HistoryText = ([System.Windows.Automation.ValuePattern]$history.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    $result.SourceExact = (Get-FileHash -LiteralPath (Join-Path $source 'sentinel.txt') -Algorithm SHA256).Hash -eq $sourceHash
    $result.TargetEmpty = @(Get-ChildItem -LiteralPath $target -Force).Count -eq 0
    $result.Passed = ($result.Dpi -in @(144,192) -and $result.SavedTabs -eq 2 -and
        $result.BeforeOpenTabs.Count -eq 3 -and $result.OpenedTabs.Count -eq 2 -and
        $result.RestoredTabs.Count -eq 3 -and
        $result.ActionButton.Name -eq 'Restore previous workspace' -and
        $result.ActionButton.Enabled -and $result.ActionButton.Bounds[2] -gt 0 -and
        $result.ActionButton.Bounds[3] -gt 0 -and
        $result.HistoryText -like '*Action: Restore previous workspace*' -and
        $result.SourceExact -and $result.TargetEmpty)
    $result.Stage = 'complete'
}
catch {
    $result.Error = $_.Exception.Message
    if ($window) {
        try {
            $result.DebugUi = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition) |
                Where-Object { $_.Current.Name -and !$_.Current.IsOffscreen } |
                Select-Object -First 65 |
                ForEach-Object { $_.Current.ControlType.ProgrammaticName + ': ' + $_.Current.Name })
        } catch { }
    }
}
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
    $ownedRoot = [IO.Path]::GetFullPath((Join-Path $validation 'NotificationAction')) + [IO.Path]::DirectorySeparatorChar
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (!$resolvedFixture.StartsWith($ownedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture cleanup escaped the owned validation root.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
if (!$result.Passed) { exit 1 }
