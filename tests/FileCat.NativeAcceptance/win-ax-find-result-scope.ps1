param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [switch]$NarrowNoMatch,
    [switch]$RemappedShortcut
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatFindInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
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
if ($NarrowNoMatch -and $RemappedShortcut) {
    throw 'Run the narrow no-match and remapped shortcut boundaries separately.'
}
$fixture = Join-Path $validation ('FindResultScopeFixture\' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $fixture, $data | Out-Null
if ($RemappedShortcut) {
    [IO.File]::WriteAllText((Join-Path $data 'settings.json'),
        '{"SchemaVersion":1,"KeyBindings":{"find.removeAgain":["Ctrl+Shift+S"],"find.addNew":[]}}')
}
$first = Join-Path $fixture 'alpha.txt'
$second = Join-Path $fixture 'beta.txt'
$list = Join-Path $fixture 'owned.lst'
[IO.File]::WriteAllText($first, "alpha fixture`n")
[IO.File]::WriteAllText($second, "beta fixture`n")
[IO.File]::WriteAllLines($list, @($first, $second))
$hashes = @(@($first, $second, $list) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
$result = [ordered]@{
    Passed = $false
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    Fixture = $fixture
    ResultSource = 'owned.lst'
    Scope = $null
    NarrowNoMatch = $null
    RemappedShortcut = $null
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

function Send-FindKey([bool]$shift) {
    [FileCatFindInput]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)
    if ($shift) { [FileCatFindInput]::keybd_event(0x10, 0, 0, [IntPtr]::Zero) }
    [FileCatFindInput]::keybd_event(0x53, 0, 0, [IntPtr]::Zero)
    [FileCatFindInput]::keybd_event(0x53, 0, 2, [IntPtr]::Zero)
    if ($shift) { [FileCatFindInput]::keybd_event(0x10, 0, 2, [IntPtr]::Zero) }
    [FileCatFindInput]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
}

try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $fixture, '--right', $fixture, '--list', $list) -PassThru
    $window = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'the isolated FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'Unexpected executable owns the FileCat window.'
    }
    $row = Wait-For { @(Named $window ([System.Windows.Automation.ControlType]::ListItem) 'alpha.txt') |
        Select-Object -First 1 } 'the loaded alpha result-set row'
    ([System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    $row.SetFocus()
    $find = Wait-For { @(Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -like 'Find files*'
    }) | Select-Object -First 1 } 'Find files toolbar command'
    ([System.Windows.Automation.InvokePattern]$find.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $findWindow = Wait-For {
        @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                $_.Current.ProcessId -eq $process.Id -and $_.Current.Name -eq 'Find within results'
            }) | Select-Object -First 1
    } 'Find within results window'
    $searchWithin = Wait-For { @(Named $findWindow ([System.Windows.Automation.ControlType]::Edit) 'Search within') |
        Select-Object -First 1 } 'Search within field'
    $scope = Wait-For { @(Descendants $findWindow | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name -like '*result-set snapshot*'
    }) | Select-Object -First 1 } 'result-set scope guidance'
    $findAction = Wait-For { @(Named $findWindow ([System.Windows.Automation.ControlType]::Button) 'Find') |
        Select-Object -First 1 } 'Find action'
    $result.Scope = [ordered]@{
        Heading = $findWindow.Current.Name
        SearchWithin = $searchWithin.Current.Name
        SearchWithinValue = ([System.Windows.Automation.ValuePattern]$searchWithin.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
        SearchWithinReadOnly = ([System.Windows.Automation.ValuePattern]$searchWithin.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)).Current.IsReadOnly
        Guidance = $scope.Current.Name
        FindEnabled = $findAction.Current.IsEnabled
    }
    if ($result.Scope.SearchWithinValue -notlike '*owned.lst*' -or
        !$result.Scope.SearchWithinReadOnly -or
        $result.Scope.Guidance -notlike '*only this result-set snapshot*' -or
        !$result.Scope.FindEnabled) {
        throw 'Find did not identify the active immutable result-set scope.'
    }
    if ($RemappedShortcut) {
        $transform = [System.Windows.Automation.TransformPattern]$findWindow.GetCurrentPattern(
            [System.Windows.Automation.TransformPattern]::Pattern)
        if (!$transform.Current.CanResize) { throw 'Find window does not expose resize.' }
        $transform.Resize(640, 420)
        $intro = Wait-For { @(Descendants $findWindow | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -like '*Ctrl+Shift+S*'
        }) | Select-Object -First 1 } 'remapped Find gesture guidance'
        $introText = $intro.Current.Name
        $names = Wait-For { @(Named $findWindow ([System.Windows.Automation.ControlType]::Edit) 'Names') |
            Select-Object -First 1 } 'Names field'
        ([System.Windows.Automation.ValuePattern]$names.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)).SetValue('*')
        ([System.Windows.Automation.InvokePattern]$findAction.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        $listControl = Wait-For { @(Descendants $findWindow | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List
        }) |
            Select-Object -First 1 } 'found-items list'
        function Found-Names {
            @(Descendants $listControl | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem
            } | ForEach-Object { $_.Current.Name })
        }
        Wait-For {
            $rows = @(Found-Names)
            if (($rows -join '|') -match 'alpha.txt' -and ($rows -join '|') -match 'beta.txt') { $rows }
        } 'two initial Find results' | Out-Null
        ([System.Windows.Automation.ValuePattern]$names.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)).SetValue('alpha.txt')
        $listControl.SetFocus()
        [FileCatFindInput]::SetForegroundWindow([IntPtr]$findWindow.Current.NativeWindowHandle) | Out-Null
        Send-FindKey $false
        Start-Sleep -Milliseconds 500
        $oldChordRows = @(Found-Names)
        if (($oldChordRows -join '|') -notmatch 'alpha.txt' -or
            ($oldChordRows -join '|') -notmatch 'beta.txt') {
            throw 'The old Ctrl+S binding still changed Find result membership.'
        }
        Send-FindKey $true
        $remainingRows = Wait-For {
            $rows = @(Found-Names)
            if (($rows -join '|') -match 'beta.txt' -and
                ($rows -join '|') -notmatch 'alpha.txt') { $rows }
        } 'remapped subtract to retain only beta'
        $result.RemappedShortcut = [ordered]@{
            Width = $findWindow.Current.BoundingRectangle.Width
            Height = $findWindow.Current.BoundingRectangle.Height
            Intro = $introText
            OldChordRows = $oldChordRows
            RemainingRows = @($remainingRows)
        }
        if ($result.RemappedShortcut.Width -gt 700 -or $result.RemappedShortcut.Height -gt 480 -or
            $result.RemappedShortcut.Intro -like '*Ctrl+W*') {
            throw 'The narrow Find shortcut guidance did not match the configured bindings.'
        }
    }
    if ($NarrowNoMatch) {
        $transform = [System.Windows.Automation.TransformPattern]$findWindow.GetCurrentPattern(
            [System.Windows.Automation.TransformPattern]::Pattern)
        if (!$transform.Current.CanResize) { throw 'Find window does not expose resize.' }
        $transform.Resize(640, 420)
        $bounds = Wait-For {
            $rect = $findWindow.Current.BoundingRectangle
            if ($rect.Width -le 700 -and $rect.Height -le 480) { $rect }
        } 'the 640 by 420 Find window'
        $names = Wait-For { @(Named $findWindow ([System.Windows.Automation.ControlType]::Edit) 'Names') |
            Select-Object -First 1 } 'Names field'
        $query = 'native-no-match-' + [guid]::NewGuid().ToString('N')
        ([System.Windows.Automation.ValuePattern]$names.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)).SetValue($query)
        ([System.Windows.Automation.InvokePattern]$findAction.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        $headline = Wait-For { @(Descendants $findWindow | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -eq 'No matches'
        }) | Select-Object -First 1 } 'completed no-match headline'
        $explanation = Wait-For { @(Descendants $findWindow | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -like 'Criteria and scope:*'
        }) | Select-Object -First 1 } 'search-specific empty explanation'
        $headlineRect = $headline.Current.BoundingRectangle
        $result.NarrowNoMatch = [ordered]@{
            Width = $bounds.Width
            Height = $bounds.Height
            Headline = $headline.Current.Name
            Explanation = $explanation.Current.Name
            HeadlineVisible = $headlineRect.Width -gt 0 -and $headlineRect.Height -gt 0 -and
                $headlineRect.Left -ge $bounds.Left -and $headlineRect.Right -le $bounds.Right
        }
        if (!$result.NarrowNoMatch.HeadlineVisible -or
            $result.NarrowNoMatch.Explanation -notlike "*$query*") {
            throw 'Narrow Find did not expose a visible, query-specific no-match result.'
        }
    }
    ([System.Windows.Automation.WindowPattern]$findWindow.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern)).Close()
    $actual = @(@($first, $second, $list) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    if (@(Compare-Object -ReferenceObject $hashes -DifferenceObject $actual).Count -ne 0) {
        throw 'Opening Find changed an owned fixture.'
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
