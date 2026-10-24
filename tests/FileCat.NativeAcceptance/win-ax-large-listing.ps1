param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatLargeListForeground {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report)) {
    throw 'Use a Git-built FileCat.exe under FileCatValidation and a fresh report directly inside that directory.'
}
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat processes before the isolated listing test.'
}
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$gitExe = 'C:\Program Files\Git\cmd\git.exe'
if (!(Test-Path -LiteralPath $gitExe -PathType Leaf)) {
    throw 'Git for Windows is required to verify the validation checkout.'
}
# VMware Tools may run this validation process under a different Admin token
# than the one that created the isolated checkout. Trust only this verified
# checkout for this one read; do not change the guest's global Git config.
$gitHead = (& $gitExe -c "safe.directory=$repo" -C $repo rev-parse HEAD 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $gitHead -notmatch '^[0-9a-f]{40}$') {
    throw 'The validation executable must be inside a Git checkout with a known commit.'
}

$root = Join-Path $validation ('LargeUiaListing\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'target'
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$original = @{ 'a.txt' = [Text.Encoding]::UTF8.GetBytes("alpha`n"); 'b.txt' = [Text.Encoding]::UTF8.GetBytes("beta`n") }
foreach ($name in $original.Keys) { [IO.File]::WriteAllBytes((Join-Path $source $name), $original[$name]) }
for ($index = 0; $index -lt 2000; $index++) {
    $name = 'native-list-{0:D4}.txt' -f $index
    [IO.File]::Create((Join-Path $source $name)).Dispose()
}

$process = $null
$result = [ordered]@{
    Passed = $false
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    GitHead = $gitHead
    GeneratedFiles = 2000
    Fixture = $root
}
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt 20) {
        $process.Refresh()
        if ($process.HasExited) { throw 'The test-owned FileCat exited before its window opened.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 150
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero -or
        [IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'The exact test-owned FileCat window was not found.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    [FileCatLargeListForeground]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null

    function Snapshot {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        if ($all.Count -gt 800) { throw "UI Automation enumerated $($all.Count) nodes." }
        $rows = @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -like 'native-list-*.txt' })
        $labels = @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -match 'files\b' })
        $status = $labels | Where-Object {
            if ($_.Current.Name -match '([\d\s,.\u00a0]+)\s+files\b') {
                ([regex]::Replace($Matches[1], '\D', '')) -eq '2002'
            } else { $false }
        } | Select-Object -First 1
        [pscustomobject]@{
            FileCount = if ($status) { 2002 } else { $null }
            Status = if ($status) { $status.Current.Name } else { $null }
            RowCount = $rows.Count
            Visited = $all.Count
            QuerySeconds = [math]::Round($clock.Elapsed.TotalSeconds, 3)
            VisibleNames = @($rows | ForEach-Object { $_.Current.Name })
            FirstRow = $rows | Where-Object { $_.Current.Name -eq 'native-list-0000.txt' } | Select-Object -First 1
        }
    }

    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $first = Snapshot
        if ($first.FileCount -eq 2002 -and $first.FirstRow) { break }
        Start-Sleep -Milliseconds 200
    } while ($clock.Elapsed.TotalSeconds -lt 30)
    if ($first.FileCount -ne 2002 -or !$first.FirstRow -or $first.RowCount -gt 120 -or
        $first.QuerySeconds -gt 5) { throw "The loaded accessibility listing was not bounded: $($first | ConvertTo-Json -Compress)." }
    $first.FirstRow.SetFocus()
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne 'native-list-0000.txt') {
        throw 'The first generated row did not take native UI Automation focus.'
    }
    $firstRow = $first.FirstRow.Current.BoundingRectangle
    if ($firstRow.Width -lt 40 -or $firstRow.Height -lt 12) { throw 'The first row has invalid native bounds.' }
    $jump = [Diagnostics.Stopwatch]::StartNew()
    [Windows.Forms.SendKeys]::SendWait('{END}')
    $lastName = 'native-list-1999.txt'
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt 10 -and
        [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne $lastName) {
        Start-Sleep -Milliseconds 100
    }
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne $lastName) {
        throw 'End did not focus the last generated file within ten seconds.'
    }
    $last = Snapshot
    if ($last.RowCount -gt 120 -or $last.Visited -gt 800 -or $last.QuerySeconds -gt 5 -or
        $jump.Elapsed.TotalSeconds -gt 10 -or $lastName -notin $last.VisibleNames) {
        throw "The last-row accessibility listing was not bounded: $($last | ConvertTo-Json -Compress)."
    }
    foreach ($name in $original.Keys) {
        if ([BitConverter]::ToString([IO.File]::ReadAllBytes((Join-Path $source $name))) -ne
            [BitConverter]::ToString([byte[]]$original[$name])) {
            throw "The original $name source bytes changed."
        }
    }
    if ((Get-ChildItem -LiteralPath $source -File).Count -ne 2002 -or
        (Get-ChildItem -LiteralPath $target).Count -ne 0) { throw 'The isolated source or target changed.' }
    $result.Loaded = @{ FileCount = $first.FileCount; Status = $first.Status; RowCount = $first.RowCount;
        Visited = $first.Visited; QuerySeconds = $first.QuerySeconds }
    $result.AfterEnd = @{ FileCount = $last.FileCount; Status = $last.Status; RowCount = $last.RowCount;
        Visited = $last.Visited; QuerySeconds = $last.QuerySeconds; Focused = $lastName }
    $result.EndKeySeconds = [math]::Round($jump.Elapsed.TotalSeconds, 3)
    $result.OriginalSourceBytesUnchanged = $true
    $result.TargetEmpty = $true
    $result.Passed = $true
}
catch { $result.Error = $_.Exception.Message }
finally {
    if ($process -and !$process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    }
    $resolvedRoot = [IO.Path]::GetFullPath($root)
    if (!$resolvedRoot.StartsWith($validation + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside FileCatValidation.' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
if (!$result.Passed) { throw $result.Error }
