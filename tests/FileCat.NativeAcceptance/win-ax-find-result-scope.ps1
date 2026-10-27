param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$validation = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith(([IO.Path]::GetFullPath($validation) + [IO.Path]::DirectorySeparatorChar),
        [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    $report -ine (Join-Path $validation ([IO.Path]::GetFileName($report))) -or
    (Test-Path -LiteralPath $report)) {
    throw 'Use the isolated Git-built FileCat.exe and a fresh validation report.'
}
$fixture = Join-Path $validation ('FindResultScopeFixture\' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $fixture, $data | Out-Null
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
