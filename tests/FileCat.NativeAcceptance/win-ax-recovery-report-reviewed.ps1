param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Native report/decision route for a seed-recovery journal in the disposable
# Windows VM. It owns only this FileCat process and the Git-generated fixture.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$fixturePath = [IO.Path]::GetFullPath($FixturePath)
$reportPath = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !$fixturePath.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($reportPath) -ine $validation -or
    (Test-Path -LiteralPath $reportPath)) { throw 'Use a Git-built VM app and fresh owned report.' }
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat processes before the isolated recovery check.'
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
    throw 'The open recovery journal is outside the owned fixture.'
}
$photo = Join-Path $source 'photos\photo.jpg'
$note = Join-Path $source 'photos\notes.txt'
$photoHash = (Get-FileHash -LiteralPath $photo -Algorithm SHA256).Hash
$noteHash = (Get-FileHash -LiteralPath $note -Algorithm SHA256).Hash
$journalHash = (Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash
$sourceQuoted = $source.Replace('\', '\\')
$destinationQuoted = $destination.Replace('\', '\\')
if (@(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) {
    throw 'The owned destination is not empty.'
}
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$git = 'C:\Program Files\Git\cmd\git.exe'
$head = (& $git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'The test app is not from a readable Git checkout.'
}
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head; Fixture = $root
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    JournalSha256Before = $journalHash; PhotoSha256Before = $photoHash; NoteSha256Before = $noteHash
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
    if (!$button.Current.IsEnabled) { throw "Button $Prefix is disabled." }
    $name = $button.Current.Name
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    return $name
}
function Ensure-OperationsOpen($Window) {
    $list = @(Descendants $Window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
        $_.Current.Name -eq 'Choose an interrupted operation'
    } | Select-Object -First 1)
    if ($list.Count -ne 0) { return 'already open at recovery startup' }
    return Invoke-Button $Window 'Details (Ctrl+J)'
}
function Select-Interrupted($Window) {
    $list = Wait-For {
        @(Descendants $Window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
            $_.Current.Name -eq 'Choose an interrupted operation'
        } | Select-Object -First 1)
    } 'interrupted operation list'
    $items = @(Descendants $list | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem
    })
    if ($items.Count -ne 1) { throw "Expected one owned interrupted row; found $($items.Count)." }
    ([System.Windows.Automation.SelectionItemPattern]$items[0].GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    return $items[0].Current.Name
}
function Start-OwnedApp {
    $owned = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $destination) -PassThru
    Wait-For { $owned.Refresh(); if ($owned.HasExited) { throw 'Owned FileCat exited early.' }
        if ($owned.MainWindowHandle -ne [IntPtr]::Zero) { $owned.MainWindowHandle } } 'owned FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($owned.MainModule.FileName) -ine $app) { throw 'Wrong executable owns window.' }
    return $owned
}
function Close-OwnedApp($owned) {
    if ($owned -and !$owned.HasExited) {
        $owned.CloseMainWindow() | Out-Null
        if (!$owned.WaitForExit(10000)) { throw 'Owned FileCat did not close normally.' }
    }
}
function Verify-Files {
    if ((Get-FileHash -LiteralPath $photo -Algorithm SHA256).Hash -ne $photoHash -or
        (Get-FileHash -LiteralPath $note -Algorithm SHA256).Hash -ne $noteHash -or
        @(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) {
        throw 'Recovery report or reviewed decision changed fixture files.'
    }
}
function Report-Window($ProcessId) {
    @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
        $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like '*Interrupted recovery report*'
    } | Select-Object -First 1)
}
function Copy-Report($Window) {
    $sentinel = 'FileCatNativeRecovery-' + [guid]::NewGuid().ToString('N')
    [System.Windows.Forms.Clipboard]::SetText($sentinel)
    Invoke-Button $Window 'Copy all' | Out-Null
    return Wait-For {
        $text = [System.Windows.Forms.Clipboard]::GetText()
        if ($text.Length -gt 0 -and $text -ne $sentinel) { $text }
    } 'Copy all to replace the clipboard sentinel' 20
}

$owned = $null
$originalClipboard = [System.Windows.Forms.Clipboard]::GetDataObject()
try {
    $owned = Start-OwnedApp
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle)
    $result.Stage = 'inspect and copy report'
    $result.OperationsAction = Ensure-OperationsOpen $window
    $result.SelectedInterrupted = Select-Interrupted $window
    $result.ReportAction = Invoke-Button $window 'Review retained recovery report for '
    $reportWindow = Wait-For { Report-Window $owned.Id } 'native interrupted recovery report'
    $copy = Copy-Report $reportWindow
    $result.CopiedReport = @{
        Length = $copy.Length; SourcePath = $copy.Contains($sourceQuoted)
        DestinationPath = $copy.Contains($destinationQuoted)
        InspectionOnly = $copy.Contains('Inspection only; no files or recovery tracking changed')
        JsonEscapingExplained = $copy.Contains('Quoted paths use JSON escaping')
        FirstLine = @($copy -split "`n")[0].Trim()
    }
    if (!($result.CopiedReport.SourcePath -and $result.CopiedReport.DestinationPath -and
        $result.CopiedReport.InspectionOnly -and $result.CopiedReport.JsonEscapingExplained)) {
        throw 'Copy all omitted a full path or its inspection-only consequence.'
    }
    ([System.Windows.Automation.WindowPattern]$reportWindow.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern)).Close()
    Wait-For { if (!(Report-Window $owned.Id)) { $true } } 'report window closure' | Out-Null
    Verify-Files
    if ((Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash -ne $journalHash) {
        throw 'Read-only report changed the open journal.'
    }
    $result.Stage = 'Mark reviewed Cancel'
    $result.MarkAction = Invoke-Button $window 'Mark interrupted recovery reviewed for '
    $approve = Wait-For { Button $window 'Mark reviewed and stop reminders' } 'explicit reviewed approval'
    $names = @(Descendants $window | ForEach-Object { $_.Current.Name })
    $result.ReviewDisclosure = @{
        NoFileChange = (($names -join "`n").Contains('File changes: none'))
        ReportRetained = (($names -join "`n").Contains('bounded report remains'))
    }
    if (!$result.ReviewDisclosure.NoFileChange -or !$result.ReviewDisclosure.ReportRetained) {
        throw 'The native Mark reviewed decision hid a consequential disclosure.'
    }
    $result.CancelAction = Invoke-Button $window 'Cancel'
    Wait-For { if (!(Button $window 'Mark reviewed and stop reminders')) { $true } } 'review Cancel' | Out-Null
    Verify-Files
    if ((Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash -ne $journalHash -or
        !(Button $window 'Mark interrupted recovery reviewed for ')) {
        throw 'Cancel closed recovery tracking or changed the fixture.'
    }
    $result.CancelKeptJournalAndFiles = $true
    $result.Stage = 'deliberate Mark reviewed'
    $result.MarkAgainAction = Invoke-Button $window 'Mark interrupted recovery reviewed for '
    $result.ApproveAction = Invoke-Button $window 'Mark reviewed and stop reminders'
    $reviewedDir = Join-Path (Split-Path $journal) 'reviewed'
    $saved = Wait-For {
        if (!(Test-Path -LiteralPath $reviewedDir -PathType Container)) { return $null }
        @(Get-ChildItem -LiteralPath $reviewedDir -Filter 'review-*.json' | Where-Object {
            (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).JournalPath -eq $journal
        } | Select-Object -First 1)
    } 'durable reviewed recovery report'
    Wait-For {
        $current = Get-Content -LiteralPath $saved.FullName -Raw | ConvertFrom-Json
        if ($current.ClosureConfirmed -eq $true -and
            !(Button $window 'Mark interrupted recovery reviewed for ')) { $current }
    } 'review closure' | Out-Null
    $record = Get-Content -LiteralPath $saved.FullName -Raw | ConvertFrom-Json
    Verify-Files
    $result.SavedReview = @{
        Path = $saved.FullName; ClosureConfirmed = $record.ClosureConfirmed
        SourcePath = $record.Report.Contains($sourceQuoted)
        DestinationPath = $record.Report.Contains($destinationQuoted)
        NoFileChange = $record.Report.Contains('no files changed')
    }
    if (!($result.SavedReview.ClosureConfirmed -and $result.SavedReview.SourcePath -and
        $result.SavedReview.DestinationPath -and $result.SavedReview.NoFileChange)) {
        throw 'Saved reviewed report omitted a required result or path.'
    }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
} finally {
    try { Close-OwnedApp $owned }
    catch { $result.CloseError = $_.Exception.Message; $result.Passed = $false }
    if ($originalClipboard) {
        try { [System.Windows.Forms.Clipboard]::SetDataObject($originalClipboard, $true) }
        catch { $result.ClipboardRestoreError = $_.Exception.Message }
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
