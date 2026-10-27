param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Read-only native persistence check after win-ax-recovery-report-reviewed.ps1.
# Uses only the disposable VMware guest's Git-generated fixture.
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
    throw 'Close other FileCat processes before this isolated recovery check.'
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
    $escaped.Count -ne 0) { throw 'Fixture paths escaped the test-owned root.' }
$reviewedDir = Join-Path (Split-Path $journal) 'reviewed'
$saved = @(Get-ChildItem -LiteralPath $reviewedDir -Filter 'review-*.json' | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).JournalPath -eq $journal
})
if ($saved.Count -ne 1) { throw "Expected one reviewed record for this journal; found $($saved.Count)." }
$record = Get-Content -LiteralPath $saved[0].FullName -Raw | ConvertFrom-Json
if ($record.ClosureConfirmed -ne $true) { throw 'The saved record did not confirm closure.' }
$photo = Join-Path $source 'photos\photo.jpg'
$note = Join-Path $source 'photos\notes.txt'
$photoHash = (Get-FileHash -LiteralPath $photo -Algorithm SHA256).Hash
$noteHash = (Get-FileHash -LiteralPath $note -Algorithm SHA256).Hash
$sourceQuoted = $source.Replace('\', '\\')
$destinationQuoted = $destination.Replace('\', '\\')
if (@(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) {
    throw 'Reviewed closure changed the destination before reopen.'
}
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$git = 'C:\Program Files\Git\cmd\git.exe'
$head = (& $git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'The app is not from a readable Git checkout.'
}
$result = [ordered]@{
    Passed = $false; Stage = 'reopen'; GitHead = $head; Fixture = $root
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    SavedReport = $saved[0].FullName; ClosureConfirmed = $record.ClosureConfirmed
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
function Report-Window($ProcessId) {
    @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
        $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like '*Reviewed recovery report*'
    } | Select-Object -First 1)
}

$owned = $null
$originalClipboard = [System.Windows.Forms.Clipboard]::GetDataObject()
try {
    $owned = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $destination) -PassThru
    Wait-For { $owned.Refresh(); if ($owned.HasExited) { throw 'Owned FileCat exited early.' }
        if ($owned.MainWindowHandle -ne [IntPtr]::Zero) { $owned.MainWindowHandle } } 'owned FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($owned.MainModule.FileName) -ine $app) { throw 'Wrong executable owns window.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle)
    if (Button $window 'Mark interrupted recovery reviewed for ') {
        throw 'Reviewed recovery notice returned after confirmed closure.'
    }
    $reviewed = @(Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
        $_.Current.Name -eq 'Reviewed recovery reports'
    })
    if ($reviewed.Count -eq 0) {
        # The status-bar Details action appears only while a job is active;
        # the persistent Operations toolbar command opens saved reports.
        $result.DetailsAction = Invoke-Button $window 'Operations'
    }
    $reviewed = Wait-For {
        @(Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
            $_.Current.Name -eq 'Reviewed recovery reports'
        } | Select-Object -First 1)
    } 'reviewed recovery reports after restart'
    $result.ReviewedListName = $reviewed.Current.Name
    $result.OpenAction = Invoke-Button $window 'Open reviewed recovery report for '
    $reportWindow = Wait-For { Report-Window $owned.Id } 'reopened reviewed report window'
    $sentinel = 'FileCatReviewedReport-' + [guid]::NewGuid().ToString('N')
    [System.Windows.Forms.Clipboard]::SetText($sentinel)
    Invoke-Button $reportWindow 'Copy all' | Out-Null
    $copy = Wait-For {
        $text = [System.Windows.Forms.Clipboard]::GetText()
        if ($text.Length -gt 0 -and $text -ne $sentinel) { $text }
    } 'reopened saved report Copy all'
    $result.CopiedReport = @{
        Length = $copy.Length; SourcePath = $copy.Contains($sourceQuoted)
        DestinationPath = $copy.Contains($destinationQuoted)
        ReviewedConsequence = $copy.Contains('Marked reviewed; no files changed by this action')
        SavedTextExact = ($copy -ceq $record.Report)
    }
    if (!($result.CopiedReport.SourcePath -and $result.CopiedReport.DestinationPath -and
        $result.CopiedReport.ReviewedConsequence -and $result.CopiedReport.SavedTextExact)) {
        throw 'Reopened Copy all omitted a complete path or differed from the saved reviewed report.'
    }
    if ((Get-FileHash -LiteralPath $photo -Algorithm SHA256).Hash -ne $photoHash -or
        (Get-FileHash -LiteralPath $note -Algorithm SHA256).Hash -ne $noteHash -or
        @(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) {
        throw 'Reading the saved report changed fixture files.'
    }
    ([System.Windows.Automation.WindowPattern]$reportWindow.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern)).Close()
    $result.FilesIntact = $true
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
    if ($window) {
        try {
            $result.AccessibleControls = @(Descendants $window | Where-Object {
                $_.Current.ControlType -in @([System.Windows.Automation.ControlType]::Button,
                    [System.Windows.Automation.ControlType]::List,
                    [System.Windows.Automation.ControlType]::Window)
            } | ForEach-Object { @{ Type = $_.Current.ControlType.ProgrammaticName;
                                   Name = $_.Current.Name; Offscreen = $_.Current.IsOffscreen } } | Select-Object -First 75)
        } catch { $result.AccessibleSnapshotError = $_.Exception.Message }
    }
} finally {
    if ($owned -and !$owned.HasExited) {
        $owned.CloseMainWindow() | Out-Null
        if (!$owned.WaitForExit(10000)) { Stop-Process -Id $owned.Id -Force }
    }
    if ($originalClipboard) {
        try { [System.Windows.Forms.Clipboard]::SetDataObject($originalClipboard, $true) }
        catch { $result.ClipboardRestoreError = $_.Exception.Message }
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
