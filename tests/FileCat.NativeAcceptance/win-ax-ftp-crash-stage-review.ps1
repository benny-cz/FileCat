param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$DotnetPath,
    [Parameter(Mandatory)][string]$HarnessPath,
    [Parameter(Mandatory)][string]$PythonHelperPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('Copy', 'Move')][string]$Kind = 'Copy'
)

# Interactive Windows VM only. The test owns one loopback FTP server, one
# upload worker, one FileCat window and one disposable FileCatValidation root.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$safePrefix = $validation.TrimEnd('\') + '\'
$app = [IO.Path]::GetFullPath($AppPath)
$harness = [IO.Path]::GetFullPath($HarnessPath)
$helper = [IO.Path]::GetFullPath($PythonHelperPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if ([IO.Path]::GetDirectoryName($report) -ine $validation -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    [IO.Path]::GetFileName($harness) -ine 'FileCat.RemoteCrashHarness.dll' -or
    [IO.Path]::GetFileName($helper) -ine 'win-ftp-owned-stage.py') {
    throw 'The native FTP fixture paths are invalid.'
}
foreach ($path in @($app, $harness, $helper)) {
    if (!$path.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "A required Git-built or copied fixture is outside FileCatValidation: $path"
    }
}
if (!(Test-Path -LiteralPath $DotnetPath -PathType Leaf)) { throw 'The guest dotnet runtime is missing.' }
$python = (Get-Command python -ErrorAction Stop).Source
$root = Join-Path $validation ('FtpCrashReview-' + [guid]::NewGuid().ToString('N'))
$serverRoot = Join-Path $root 'server'
$data = Join-Path $root 'data'
$journalDir = Join-Path $data 'local\journal'
$sourceDir = Join-Path $root 'source'
$source = Join-Path $sourceDir 'owned-upload.bin'
$ready = Join-Path $root 'server.ready'
$inspectReceipt = Join-Path $root 'stage-inspection.json'
New-Item -ItemType Directory -Force -Path $serverRoot, $journalDir, $sourceDir | Out-Null
$bytes = [byte[]]::new(8 * 1024 * 1024)
[Random]::new(240).NextBytes($bytes)
[IO.File]::WriteAllBytes($source, $bytes)
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$server = $null
$worker = $null
$appProcess = $null
$result = [ordered]@{ Passed = $false; Fixture = $root; AppSha256 =
    (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    Kind = $Kind; SourceSha256 = $sourceHash; Journal = $null; Stage = $null; StageBytes = 0;
    StageObservations = @();
    RowHint = $false; DialogHint = $false; ReportHint = $false;
    NoAutomaticRemoteDeletion = $false; ExplicitStageDeletion = $false;
    JournalClosedAfterReview = $false; Error = $null }

function Snapshot($window) {
    for ($retry = 0; $retry -lt 4; $retry++) {
        try { return @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)) }
        catch [Runtime.InteropServices.COMException] {
            if ($retry -eq 3) { throw }
            Start-Sleep -Milliseconds 150
        }
    }
}
function Await([scriptblock]$condition, [string]$description, [int]$seconds = 30) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 150
    } while ($clock.Elapsed.TotalSeconds -lt $seconds)
    throw "Timed out waiting for $description."
}
function Button($window, [string]$prefix) {
    return @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name.StartsWith($prefix, [StringComparison]::Ordinal) -and
        $_.Current.IsEnabled -and !$_.Current.IsOffscreen
    })[0]
}
function ClickButton($window, [string]$prefix) {
    (Await { Button $window $prefix } "enabled $prefix button").GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Names($window) {
    return @((Snapshot $window) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
}
function JournalOpen([string]$path) {
    return (Test-Path -LiteralPath $path -PathType Leaf) -and
        !(Select-String -LiteralPath $path -SimpleMatch '"t":"end"' -Quiet)
}

try {
    $server = Start-Process -FilePath $python -ArgumentList @($helper, 'serve', $serverRoot, $ready) -PassThru -WindowStyle Hidden
    $port = Await {
        if ($server.HasExited) { throw 'Owned FTP server exited before readiness.' }
        if (Test-Path -LiteralPath $ready -PathType Leaf) {
            $value = (Get-Content -LiteralPath $ready -Raw).Trim()
            if ($value -match '^\d+$') { [int]$value }
        }
    } 'loopback FTP readiness'
    $worker = Start-Process -FilePath $DotnetPath -ArgumentList @($harness, $port, $source, $journalDir, $Kind.ToLowerInvariant()) -PassThru -WindowStyle Hidden
    $stage = Await {
        if ($worker.HasExited) { throw 'Upload worker exited before a partial remote stage.' }
        $seen = @(Get-ChildItem -LiteralPath $serverRoot -File -Force | Where-Object {
            $_.Name.StartsWith('.fc-', [StringComparison]::Ordinal)
        })
        $result.StageObservations = @($seen | ForEach-Object { "$($_.Name):$($_.Length)" })
        $seen[0]
    } 'unpublished FTP stage creation' 45
    # This pyftpdlib build buffers live writes until the FTP data connection
    # closes. The 64 KiB/s worker needs over two minutes for 8 MiB, so a
    # fifteen-second owned interval is safely before publication and lets
    # this server flush at least one buffered block to its hidden file.
    Start-Sleep -Seconds 15
    if ($worker.HasExited) { throw 'The upload worker exited before the interruption point.' }
    Stop-Process -Id $worker.Id -Force
    $worker.WaitForExit(10000) | Out-Null
    $stage = Await {
        $item = Get-Item -LiteralPath $stage.FullName -Force -ErrorAction SilentlyContinue
        if ($item -and $item.Length -gt 0 -and $item.Length -lt $bytes.Length) { $item }
    } 'retained nonzero partial FTP stage' 20
    if (!$worker.HasExited -or (Test-Path -LiteralPath (Join-Path $serverRoot 'owned-upload.bin'))) {
        throw 'The exact crashed upload published a target or survived termination.'
    }
    $journal = @(Get-ChildItem -LiteralPath $journalDir -File -Filter 'job-*.fcj')
    if ($journal.Count -ne 1 -or !(JournalOpen $journal[0].FullName)) {
        throw "The exact FTP $Kind recovery journal was not retained open."
    }
    $journalStem = [IO.Path]::GetFileNameWithoutExtension($journal[0].Name)
    $prefix = '.fc-' + $journalStem.Substring($journalStem.Length - 8) + '-'
    if (!$stage.Name.StartsWith($prefix, [StringComparison]::Ordinal)) {
        throw "The remote stage $($stage.Name) does not belong to the crashed journal."
    }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceHash) {
        throw "The crashed FTP $Kind changed the source bytes."
    }
    $result.Journal = $journal[0].FullName
    $result.Stage = $stage.Name
    $result.StageBytes = $stage.Length

    $appProcess = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $sourceDir, '--right', $serverRoot) -PassThru
    Await {
        $appProcess.Refresh()
        !$appProcess.HasExited -and $appProcess.MainWindowHandle -ne [IntPtr]::Zero
    } 'isolated FileCat recovery window' | Out-Null
    if ([IO.Path]::GetFullPath($appProcess.MainModule.FileName) -ine $app) {
        throw 'A different executable owns the recovery window.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
    $action = Await { Button $window 'Review local cleanup' } 'remote recovery action'
    $result.RowHint = ((Names $window) -join "`n").Contains($prefix)
    $action.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $approve = Await { Button $window 'Keep remote upload for review' } 'conservative cleanup approval'
    $dialog = (Names $window) -join "`n"
    $result.DialogHint = $dialog.Contains('Candidate temporary names begin: ' + $prefix) -and
        $dialog.Contains('server files are not inspected or deleted')
    $approve.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $reviewed = Join-Path $journalDir 'reviewed'
    $review = Await {
        if (Test-Path -LiteralPath $reviewed -PathType Container) {
            @(Get-ChildItem -LiteralPath $reviewed -File -Filter 'review-*.json' | Where-Object {
                (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).JournalPath -eq $journal[0].FullName
            })[0]
        }
    } 'retained review report'
    $reviewData = Get-Content -LiteralPath $review.FullName -Raw | ConvertFrom-Json
    $result.ReportHint = $reviewData.Report.Contains($prefix)
    $result.NoAutomaticRemoteDeletion = (JournalOpen $journal[0].FullName) -and
        (Test-Path -LiteralPath $stage.FullName -PathType Leaf)
    if (!$result.RowHint -or !$result.DialogHint -or !$result.ReportHint -or
        !$result.NoAutomaticRemoteDeletion) {
        throw 'Native recovery review omitted the stage identity or changed the remote object.'
    }

    & $python $helper inspect-delete $port $stage.Name $source $inspectReceipt
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $inspectReceipt -PathType Leaf)) {
        throw 'The exact remote stage could not be inspected and deleted through FTP.'
    }
    $inspection = Get-Content -LiteralPath $inspectReceipt -Raw | ConvertFrom-Json
    $result.ExplicitStageDeletion = $inspection.stage -eq $stage.Name -and
        $inspection.stage_bytes -eq $result.StageBytes -and
        @($inspection.manifest_after | Where-Object { $_ }).Count -eq 0 -and
        !(Test-Path -LiteralPath $stage.FullName)
    if (!$result.ExplicitStageDeletion) { throw 'Manual FTP cleanup did not remove only the inspected stage.' }

    ClickButton $window "Mark interrupted recovery reviewed for $Kind"
    $mark = Await { Button $window 'Mark reviewed and stop reminders' } 'explicit reviewed decision'
    $mark.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Await { !(JournalOpen $journal[0].FullName) } 'closed reviewed FTP journal' | Out-Null
    $result.JournalClosedAfterReview = $true
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceHash -or
        @(Get-ChildItem -LiteralPath $serverRoot -File -Force).Count -ne 0) {
        throw 'Review or cleanup changed source bytes or left a remote object.'
    }
    $result.Passed = $true
}
catch { $result.Error = $_ | Out-String }
finally {
    if ($appProcess -and !$appProcess.HasExited) {
        $appProcess.CloseMainWindow() | Out-Null
        $appProcess.WaitForExit(5000) | Out-Null
        if (!$appProcess.HasExited) { Stop-Process -Id $appProcess.Id -Force }
    }
    if ($worker -and !$worker.HasExited) { Stop-Process -Id $worker.Id -Force }
    if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id -Force }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
