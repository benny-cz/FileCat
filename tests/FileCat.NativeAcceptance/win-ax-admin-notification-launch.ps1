param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][string]$LaunchReportPath
)

# VMware's interactive guest launcher uses a filtered UAC token. This short
# task starts the acceptance runner with the logged-in VM user's full token.
# It never stores a password and removes the task before exiting.
$ErrorActionPreference = 'Stop'
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$launchReport = [IO.Path]::GetFullPath($LaunchReportPath)
$runner = Join-Path $validation 'win-ax-admin-notification.ps1'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notlike '*VMware*' -or
    !$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or
    !$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    [IO.Path]::GetDirectoryName($launchReport) -ine $validation -or
    !(Test-Path -LiteralPath $runner -PathType Leaf) -or
    (Test-Path -LiteralPath $report) -or (Test-Path -LiteralPath $launchReport)) {
    throw 'Launch only from an elevated VMware guest token with fresh validation receipts.'
}

$taskName = 'FileCatValidation-AC-S2-029-' + [guid]::NewGuid().ToString('N')
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$arguments = '-NoProfile -Sta -ExecutionPolicy Bypass -File "' + $runner +
    '" -AppPath "' + $app + '" -ReportPath "' + $report + '"'
$result = [ordered]@{ Passed = $false; Stage = 'registration'; User = $identity;
    TaskName = $taskName; RunnerReportPresent = $false; LastTaskResult = $null; Error = $null }
try {
    $action = New-ScheduledTaskAction -Execute (Join-Path $PSHOME 'powershell.exe') -Argument $arguments
    $user = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Highest
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $user |
        Out-Null
    $result.Stage = 'running'
    Start-ScheduledTask -TaskName $taskName
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $state = $null
    while ($clock.Elapsed.TotalSeconds -lt 45) {
        $state = (Get-ScheduledTask -TaskName $taskName).State
        if ((Test-Path -LiteralPath $report) -and $state -ne 'Running' -and $state -ne 'Queued') { break }
        Start-Sleep -Milliseconds 200
    }
    $result.RunnerReportPresent = Test-Path -LiteralPath $report
    $result.LastTaskResult = (Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult
    $result.Passed = $result.RunnerReportPresent -and $result.LastTaskResult -eq 0 -and
        $state -notin @('Running','Queued')
    $result.Stage = 'complete'
}
catch { $result.Error = $_.Exception.Message }
finally {
    if ($state -in @('Running','Queued')) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    }
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $launchReport -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
