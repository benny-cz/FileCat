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
public static class FileCatFindRegistryNative {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
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
$fixture = Join-Path $validation ('FindRegistryScopeFixture\' + [guid]::NewGuid().ToString('N'))
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $target, $data | Out-Null
$sentinel = Join-Path $target 'unchanged.txt'
[IO.File]::WriteAllText($sentinel, "Registry Find is read-only`n")
$sentinelHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
$result = [ordered]@{
    Passed = $false
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    Fixture = $fixture
    RegistryOrigin = 'reg:HKCU\Software'
    Scope = $null
    Error = $null
}
$process = $null

function Wait-For([scriptblock]$condition, [string]$description, [int]$seconds = 20) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $description"
}

function Descendants($root) {
    for ($read = 0; $read -lt 6; $read++) {
        try {
            return $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
        }
        catch [Runtime.InteropServices.COMException] {
            if ($read -eq 5) { throw }
            Start-Sleep -Milliseconds 150
        }
    }
}

function Named($root, $type, [string]$name) {
    @(Descendants $root | Where-Object {
        $_.Current.ControlType -eq $type -and $_.Current.Name -eq $name
    })
}

try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', 'reg:HKCU\Software', '--right', $target) -PassThru
    $window = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'the isolated FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'Unexpected executable owns the FileCat window.'
    }
    $row = Wait-For { @(Named $window ([System.Windows.Automation.ControlType]::ListItem) 'Microsoft') |
        Select-Object -First 1 } 'HKCU Software Microsoft key'
    ([System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    $row.SetFocus()
    [FileCatFindRegistryNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $find = Wait-For { @(Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -like 'Find files*'
    }) | Select-Object -First 1 } 'Find files toolbar command'
    ([System.Windows.Automation.InvokePattern]$find.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $scope = Wait-For { @(Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name -like 'Search below *' -and $_.Current.Name -like '*Registry links*'
    }) | Select-Object -First 1 } 'Registry Find scope statement'
    $heading = Wait-For { @(Descendants $window | Where-Object {
        $_.Current.Name -eq 'Find in Registry'
    }) | Select-Object -First 1 } 'Registry Find dialog heading'
    $close = Wait-For { @(Named $window ([System.Windows.Automation.ControlType]::Button) 'Close') |
        Select-Object -Last 1 } 'Registry Find Close action'
    $result.Scope = [ordered]@{
        Heading = $heading.Current.Name
        Guidance = $scope.Current.Name
        SearchTextCount = @(Named $window ([System.Windows.Automation.ControlType]::Edit) 'Registry search text').Count
        CloseEnabled = $close.Current.IsEnabled
    }
    if ($result.Scope.Guidance -notmatch 'HKCU|HKEY_CURRENT_USER' -or
        $result.Scope.Guidance -notmatch 'Software' -or
        $result.Scope.SearchTextCount -ne 1 -or !$result.Scope.CloseEnabled) {
        throw 'Registry Find did not name the current HKCU Software scope with usable controls.'
    }
    ([System.Windows.Automation.InvokePattern]$close.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    if ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -ne $sentinelHash) {
        throw 'Read-only Registry Find changed the owned filesystem sentinel.'
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
