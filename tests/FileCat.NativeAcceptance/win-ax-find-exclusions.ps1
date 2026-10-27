param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatFindExclusionInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, int flags, IntPtr extra);
}
'@

$validation = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith(([IO.Path]::GetFullPath($validation) + [IO.Path]::DirectorySeparatorChar),
        [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    $report -ine (Join-Path $validation ([IO.Path]::GetFileName($report))) -or
    (Test-Path -LiteralPath $report)) {
    throw 'Use the isolated Git-built FileCat.exe and a fresh validation report.'
}
$id = [guid]::NewGuid().ToString('N')
$fixture = Join-Path $validation ('FindExclusionFixture\' + $id)
$data = Join-Path $validation ('FindExclusionProfile\' + $id)
$cache = Join-Path $fixture 'cache'
New-Item -ItemType Directory -Force -Path $cache, $data | Out-Null
$visible = Join-Path $fixture 'visible.txt'
$ignored = Join-Path $cache 'ignored.txt'
[IO.File]::WriteAllText($visible, "visible source`n")
[IO.File]::WriteAllText($ignored, "ignored source`n")
[IO.File]::WriteAllText((Join-Path $data 'settings.json'),
    '{"SchemaVersion":1,"SearchLogOnErrors":false,"SearchIgnoredFolders":[{"Folder":"cache","Enabled":true}]}')
$before = @(@($visible, $ignored) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
$result = [ordered]@{
    Passed = $false
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    Fixture = $fixture
    Find = $null
    Error = $null
}
$process = $null

function Wait-For([scriptblock]$condition, [string]$description, [int]$seconds = 20) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $condition; if ($value) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $description"
}

function Descendants($root) {
    $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
}

function Named($root, $type, [string]$name) {
    @(Descendants $root | Where-Object {
        $_.Current.ControlType -eq $type -and $_.Current.Name -eq $name
    })
}

function Send-Key([byte]$key) {
    [FileCatFindExclusionInput]::keybd_event($key, 0, 0, [IntPtr]::Zero)
    [FileCatFindExclusionInput]::keybd_event($key, 0, 2, [IntPtr]::Zero)
}

try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $fixture, '--right', $fixture) -PassThru
    $window = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'the isolated FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'Unexpected executable owns the FileCat window.'
    }
    Wait-For { @(Named $window ([System.Windows.Automation.ControlType]::ListItem) 'visible.txt') |
        Select-Object -First 1 } 'owned source row' | Out-Null
    $toolbar = Wait-For { @(Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -like 'Find files*'
    }) | Select-Object -First 1 } 'Find toolbar command'
    ([System.Windows.Automation.InvokePattern]$toolbar.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $findWindow = Wait-For {
        @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                $_.Current.ProcessId -eq $process.Id -and $_.Current.Name -eq 'Find files'
            }) | Select-Object -First 1
    } 'Find files window'
    $transform = [System.Windows.Automation.TransformPattern]$findWindow.GetCurrentPattern(
        [System.Windows.Automation.TransformPattern]::Pattern)
    if (!$transform.Current.CanResize) { throw 'Find window does not expose resize.' }
    $handle = [IntPtr]$findWindow.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { throw 'Find window has no native handle.' }
    [FileCatFindExclusionInput]::ShowWindow($handle, 9) | Out-Null
    if (![FileCatFindExclusionInput]::SetWindowPos($handle, [IntPtr]::Zero, 100, 100, 640, 420, 0x40)) {
        throw 'Could not place the Find window on the guest desktop.'
    }
    $bounds = Wait-For {
        $rect = $findWindow.Current.BoundingRectangle
        if ($rect.Width -le 700 -and $rect.Height -le 480 -and
            $rect.Left -ge 0 -and $rect.Top -ge 0) { $rect }
    } 'the 640 by 420 Find window'
    $names = Wait-For { @(Named $findWindow ([System.Windows.Automation.ControlType]::Edit) 'Names') |
        Select-Object -First 1 } 'Names field'
    $query = 'native-no-match-' + [guid]::NewGuid().ToString('N')
    ([System.Windows.Automation.ValuePattern]$names.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue($query)
    $findAction = Wait-For { @(Named $findWindow ([System.Windows.Automation.ControlType]::Button) 'Find') |
        Select-Object -First 1 } 'Find action'
    ([System.Windows.Automation.InvokePattern]$findAction.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $headline = Wait-For { @(Descendants $findWindow | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name -eq 'No matches outside exclusions'
    }) | Select-Object -First 1 } 'qualified no-match headline'
    $qualification = Wait-For { @(Descendants $findWindow | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name -like '*1 not searched*1 ignored*'
    }) | Select-Object -First 1 } 'visible exclusion qualification'
    $explanation = Wait-For { @(Descendants $findWindow | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name -like 'Criteria and scope:*'
    }) | Select-Object -First 1 } 'search-specific scope explanation'
    $review = Wait-For { @(Named $findWindow ([System.Windows.Automation.ControlType]::Button) 'Review search log') |
        Select-Object -First 1 } 'Review search log action'
    $log = Wait-For { @(Descendants $findWindow | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -like 'Log (*'
    }) | Select-Object -First 1 } 'completion Log action'
    $reviewRect = $review.Current.BoundingRectangle
    $logRect = $log.Current.BoundingRectangle
    $headlineRect = $headline.Current.BoundingRectangle
    $qualificationRect = $qualification.Current.BoundingRectangle
    $result.Find = [ordered]@{
        Width = $bounds.Width
        Height = $bounds.Height
        Headline = $headline.Current.Name
        Qualification = $qualification.Current.Name
        Explanation = $explanation.Current.Name
        ReviewLogEnabled = $review.Current.IsEnabled
        ReviewLogBounds = [ordered]@{ Left = $reviewRect.Left; Top = $reviewRect.Top;
            Right = $reviewRect.Right; Bottom = $reviewRect.Bottom; Width = $reviewRect.Width;
            Height = $reviewRect.Height }
        WindowBounds = [ordered]@{ Left = $bounds.Left; Top = $bounds.Top;
            Right = $bounds.Right; Bottom = $bounds.Bottom }
        HeadlineVisible = $headlineRect.Width -gt 0 -and $headlineRect.Top -ge $bounds.Top -and
            $headlineRect.Bottom -le $bounds.Bottom
        QualificationVisible = $qualificationRect.Width -gt 0 -and
            $qualificationRect.Top -ge $bounds.Top -and $qualificationRect.Bottom -le $bounds.Bottom
        ReviewLogReachable = $log.Current.IsEnabled -and $logRect.Width -gt 0 -and
            $logRect.Top -ge $bounds.Top -and $logRect.Right -le $bounds.Right -and
            $logRect.Bottom -le $bounds.Bottom
        PrimaryLogBounds = [ordered]@{ Left = $logRect.Left; Top = $logRect.Top;
            Right = $logRect.Right; Bottom = $logRect.Bottom }
        LogOpenedByKeyboard = $false
    }
    if ($result.Find.Explanation -notlike "*$query*" -or !$result.Find.HeadlineVisible -or
        !$result.Find.QualificationVisible -or !$result.Find.ReviewLogReachable) {
        throw 'Narrow Find did not expose qualified, query-specific feedback and its log.'
    }
    $log.SetFocus()
    [FileCatFindExclusionInput]::SetForegroundWindow([IntPtr]$findWindow.Current.NativeWindowHandle) | Out-Null
    Send-Key 0x0D
    Wait-For { @(Descendants $findWindow | Where-Object {
        $_.Current.Name -eq 'Search log'
    }) | Select-Object -First 1 } 'Search log after keyboard Enter' | Out-Null
    $result.Find.LogOpenedByKeyboard = $true
    Send-Key 0x1B
    $after = @(@($visible, $ignored) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    if (@(Compare-Object -ReferenceObject $before -DifferenceObject $after).Count -ne 0) {
        throw 'Find changed an owned source.'
    }
    $result.Passed = $true
}
catch { $result.Error = $_.Exception.Message }
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
    }
    $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
