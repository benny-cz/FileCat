param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$NvdaPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Native NVDA observation of the distinct interrupted-recovery decision.
# A seed-recovery journal must be open. This route Cancels; it cannot close it.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatRecoveryNarrow {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
    public static bool ClickUncoveredTitle(IntPtr handle) {
        RECT rect;
        if (!GetWindowRect(handle, out rect)) return false;
        POINT point = new POINT { X = (rect.Left + rect.Right) / 2, Y = rect.Top + 12 };
        if (GetAncestor(WindowFromPoint(point), 2) != handle) return false;
        SetCursorPos(point.X, point.Y);
        mouse_event(2, 0, 0, 0, 0);
        mouse_event(4, 0, 0, 0, 0);
        return true;
    }
}
'@
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$fixturePath = [IO.Path]::GetFullPath($FixturePath)
$reportPath = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !$nvda.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or
    !$fixturePath.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($reportPath) -ine $validation -or
    (Test-Path -LiteralPath $reportPath)) { throw 'Use the signed VM NVDA, Git app and fresh owned report.' }
if ((Get-Process -Name FileCat -ErrorAction SilentlyContinue) -or
    (Get-Process -Name nvda -ErrorAction SilentlyContinue)) {
    throw 'Close other FileCat and NVDA processes before the owned speech route.'
}
$fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($fixture.root)
$source = [IO.Path]::GetFullPath($fixture.source)
$destination = [IO.Path]::GetFullPath($fixture.destination)
$data = [IO.Path]::GetFullPath($fixture.data)
$journal = [IO.Path]::GetFullPath($fixture.JournalPath)
$escaped = @($source, $destination, $data, $journal) | Where-Object {
    -not $_.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)
}
if (!$root.StartsWith((Join-Path $validation 'Recovery') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $escaped.Count -ne 0 -or !(Test-Path -LiteralPath $journal -PathType Leaf)) {
    throw 'The open journal is outside the test-owned fixture.'
}
$photo = Join-Path $source 'photos\photo.jpg'
$note = Join-Path $source 'photos\notes.txt'
$hashes = @{
    Photo = (Get-FileHash -LiteralPath $photo -Algorithm SHA256).Hash
    Note = (Get-FileHash -LiteralPath $note -Algorithm SHA256).Hash
    Journal = (Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash
}
if (@(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) { throw 'Owned destination is not empty.' }
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$head = (& 'C:\Program Files\Git\cmd\git.exe' -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'App is not from a readable Git checkout.' }
$config = Join-Path $root 'nvda-recovery-config'
New-Item -ItemType Directory -Path $config -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$nvdaLog = Join-Path $root 'nvda-recovery.log'
$script:speechOffset = 0L
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head; Fixture = $root
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion
    NvdaLog = $nvdaLog; WindowSize = '900x650'; SpeechSteps = @()
}
function Wait-For([scriptblock]$Check, [string]$Description, [int]$Seconds = 25) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        try { $value = & $Check; if ($value) { return $value } }
        catch {
            $inner = $_.Exception
            while ($inner -and $inner -isnot [System.Runtime.InteropServices.COMException]) {
                $inner = $inner.InnerException
            }
            if (!$inner) { throw }
        }
        Start-Sleep -Milliseconds 140
    }
    throw "Timed out waiting for $Description."
}
function Descendants($Owner) {
    @($Owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Button($Window, [string]$Prefix) {
    @(Descendants $Window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name.StartsWith($Prefix, [StringComparison]::Ordinal) -and
        !$_.Current.IsOffscreen
    } | Select-Object -First 1)
}
function Invoke-Button($Window, [string]$Prefix) {
    $button = Wait-For { Button $Window $Prefix } "button $Prefix"
    $name = $button.Current.Name
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    return $name
}
function Foreground($Handle, [int]$ExpectedPid) {
    [FileCatRecoveryNarrow]::ShowWindow($Handle, 9) | Out-Null
    (New-Object -ComObject WScript.Shell).AppActivate($ExpectedPid) | Out-Null
    [FileCatRecoveryNarrow]::SetForegroundWindow($Handle) | Out-Null
    Start-Sleep -Milliseconds 300
    [uint32]$actual = 0
    [FileCatRecoveryNarrow]::GetWindowThreadProcessId(
        [FileCatRecoveryNarrow]::GetForegroundWindow(), [ref]$actual) | Out-Null
    if ($actual -ne $ExpectedPid -and [FileCatRecoveryNarrow]::ClickUncoveredTitle($Handle)) {
        Start-Sleep -Milliseconds 350
        [FileCatRecoveryNarrow]::GetWindowThreadProcessId(
            [FileCatRecoveryNarrow]::GetForegroundWindow(), [ref]$actual) | Out-Null
    }
    if ($actual -ne $ExpectedPid) { throw "FileCat is not foreground (actual process $actual)." }
    return $actual
}
function TakeSpeech {
    if (!(Test-Path -LiteralPath $nvdaLog)) { return @() }
    $stream = [IO.File]::Open($nvdaLog, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($script:speechOffset, 'Begin') | Out-Null
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $last = $text.LastIndexOf("`n")
        if ($last -lt 0) { return @() }
        $complete = $text.Substring(0, $last + 1)
        $script:speechOffset += [Text.Encoding]::UTF8.GetByteCount($complete)
        return @($complete -split "`r?`n" | Where-Object { $_.StartsWith('Speaking [') } |
            ForEach-Object { $_.Substring(9).Trim() })
    } finally { $stream.Dispose() }
}
function Hear([string]$Step, [scriptblock]$Action, [string[]]$Expected) {
    TakeSpeech | Out-Null
    & $Action
    $heard = @()
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $heard += @(TakeSpeech)
        $spoken = ($heard -join ' ').Replace('\\', '\')
        if (@($Expected | Where-Object {
            $spoken.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -lt 0
        }).Count -eq 0) { break }
        Start-Sleep -Milliseconds 150
    } while ($timer.Elapsed.TotalSeconds -lt 8)
    $spoken = ($heard -join ' ').Replace('\\', '\')
    $passed = @($Expected | Where-Object {
        $spoken.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -lt 0
    }).Count -eq 0
    $result.SpeechSteps += @{ Step = $Step; Expected = $Expected; Heard = $heard; Passed = $passed }
    return $passed
}

$nvdaProcess = $owned = $null
try {
    $nvdaProcess = Start-Process -FilePath $nvda -ArgumentList @("--log-file=$nvdaLog",
        '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Wait-For {
        if ($nvdaProcess.HasExited) { throw 'Owned NVDA exited early.' }
        if ((Test-Path -LiteralPath $nvdaLog) -and
            (Select-String -LiteralPath $nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA startup' 30 | Out-Null
    TakeSpeech | Out-Null
    $owned = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $destination) -PassThru
    Wait-For { $owned.Refresh(); if ($owned.HasExited) { throw 'Owned FileCat exited early.' }
        if ($owned.MainWindowHandle -ne [IntPtr]::Zero) { $owned.MainWindowHandle } } 'owned FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($owned.MainModule.FileName) -ine $app) { throw 'Wrong executable owns window.' }
    if (![FileCatRecoveryNarrow]::MoveWindow($owned.MainWindowHandle, 950, 90, 900, 650, $true)) {
        throw 'Could not set narrow FileCat window geometry.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle)
    $result.ForegroundBeforeReview = Foreground $owned.MainWindowHandle $owned.Id
    $list = Wait-For {
        @(Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
            $_.Current.Name -eq 'Choose an interrupted operation'
        } | Select-Object -First 1)
    } 'interrupted operation list'
    $items = @(Descendants $list | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem
    })
    if ($items.Count -ne 1) { throw 'The owned recovery row was not unique.' }
    ([System.Windows.Automation.SelectionItemPattern]$items[0].GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    $result.Stage = 'narrow Mark reviewed review'
    TakeSpeech | Out-Null
    $result.MarkAction = Invoke-Button $window 'Mark interrupted recovery reviewed for '
    $approve = Wait-For { Button $window 'Mark reviewed and stop reminders' } 'Mark reviewed decision'
    $cancel = Wait-For { Button $window 'Cancel' } 'review Cancel'
    $result.ForegroundAtReview = Foreground $owned.MainWindowHandle $owned.Id
    $names = @(Descendants $window | ForEach-Object { $_.Current.Name })
    $result.ReviewAccessible = @{
        NoFileChange = (($names -join "`n").Contains('File changes: none'))
        RetainedReport = (($names -join "`n").Contains('bounded report remains'))
        ApproveVisible = !$approve.Current.IsOffscreen
        CancelVisible = !$cancel.Current.IsOffscreen
    }
    Start-Sleep -Milliseconds 500
    $result.DialogSpeech = @(TakeSpeech)
    $cancel.SetFocus()
    Start-Sleep -Milliseconds 250
    TakeSpeech | Out-Null
    $approve.SetFocus()
    Start-Sleep -Milliseconds 800
    $result.ApprovalFocusSpeech = @(TakeSpeech)
    $result.ReviewControls = @((Descendants $window | Where-Object {
        $_.Current.Name -like '*File changes:*' -or $_.Current.Name -like '*bounded report*' -or
        $_.Current.Name -like '*stop offering recovery*'
    } | ForEach-Object { @{ Role = $_.Current.ControlType.ProgrammaticName;
        Name = $_.Current.Name; Offscreen = $_.Current.IsOffscreen } } | Select-Object -First 18))
    $result.CancelAction = Invoke-Button $window 'Cancel'
    Wait-For { if (!(Button $window 'Mark reviewed and stop reminders')) { $true } } 'review cancellation' | Out-Null
    $result.Stage = 'narrow report line navigation'
    $result.ReportAction = Invoke-Button $window 'Review retained recovery report for '
    $reportWindow = Wait-For {
        @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ProcessId -eq $owned.Id -and $_.Current.Name -like '*Interrupted recovery report*'
        } | Select-Object -First 1)
    } 'native report window'
    $result.ForegroundAtReport = Foreground ([IntPtr]$reportWindow.Current.NativeWindowHandle) $owned.Id
    Wait-For { Button $reportWindow 'Next report line' } 'report line navigator' | Out-Null
    $result.ReportSpeech = @()
    for ($i = 0; $i -lt 14; $i++) {
        TakeSpeech | Out-Null
        Invoke-Button $reportWindow 'Next report line' | Out-Null
        $heard = @()
        $voiceTimer = [Diagnostics.Stopwatch]::StartNew()
        do {
            $heard += @(TakeSpeech)
            if (($heard -join ' ') -match "Line ($($i + 2)) of") { break }
            Start-Sleep -Milliseconds 150
        } while ($voiceTimer.Elapsed.TotalSeconds -lt 2)
        $result.ReportSpeech += @{ Line = $i + 2; Heard = $heard }
    }
    ([System.Windows.Automation.WindowPattern]$reportWindow.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern)).Close()
    $dialogHeard = @($result.DialogSpeech + $result.ApprovalFocusSpeech) -join ' '
    $destinationLine = @($result.ReportSpeech | Where-Object { $_.Line -eq 7 } | ForEach-Object { $_.Heard -join ' ' }) -join ' '
    $sourceLine = @($result.ReportSpeech | Where-Object { $_.Line -eq 13 } | ForEach-Object { $_.Heard -join ' ' }) -join ' '
    for ($i = 0; $i -lt 4; $i++) {
        $destinationLine = $destinationLine.Replace('\\', '\')
        $sourceLine = $sourceLine.Replace('\\', '\')
    }
    $result.SpokenConsequences = @{
        NoFileChange = $dialogHeard.Contains('File changes: none')
        ReportRetained = $dialogHeard.Contains('bounded report remains')
    }
    $result.SpokenReport = @{
        DestinationLine = $destinationLine.Contains($destination)
        SourceLine = $sourceLine.Contains((Join-Path $source 'photos'))
    }
    if ((Get-FileHash -LiteralPath $photo -Algorithm SHA256).Hash -ne $hashes.Photo -or
        (Get-FileHash -LiteralPath $note -Algorithm SHA256).Hash -ne $hashes.Note -or
        (Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash -ne $hashes.Journal -or
        @(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) {
        throw 'Spoken review changed fixture files or the open journal.'
    }
    $result.FilesIntact = $true
    $result.DiagnosticCompleted = $result.ReviewAccessible.NoFileChange -and
        $result.ReviewAccessible.RetainedReport -and $result.ReviewAccessible.ApproveVisible -and
        $result.ReviewAccessible.CancelVisible -and $result.FilesIntact
    $result.Passed = $result.DiagnosticCompleted -and
        $result.SpokenConsequences.NoFileChange -and $result.SpokenConsequences.ReportRetained -and
        $result.SpokenReport.DestinationLine -and $result.SpokenReport.SourceLine
} catch {
    $result.Error = $_.Exception.Message
} finally {
    if ($owned -and !$owned.HasExited) {
        $owned.CloseMainWindow() | Out-Null
        if (!$owned.WaitForExit(8000)) { Stop-Process -Id $owned.Id -Force }
    }
    if ($nvdaProcess -and !$nvdaProcess.HasExited) {
        $nvdaProcess.Kill(); $nvdaProcess.WaitForExit(5000) | Out-Null
    }
    $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
