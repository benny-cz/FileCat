param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Native UIA acceptance on a disposable, Git-built Windows VM app and ZIP content fixture.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.IO.Compression
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatArchiveListForeground {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report)) {
    throw 'Use a Git-built FileCat.exe in FileCatValidation and a fresh report directly below it.'
}
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat processes before the isolated archive test.'
}
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$git = 'C:\Program Files\Git\cmd\git.exe'
$head = (& $git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'The test app does not belong to a readable Git checkout.'
}

function Wait-For([scriptblock]$Check, [string]$Description, [int]$Seconds = 25) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        try { $value = & $Check; if ($value) { return $value } }
        catch [System.Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $Description."
}

function Get-Nodes($Root) {
    $nodes = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    if ($nodes.Count -gt 800) { throw "UI Automation exposed $($nodes.Count) nodes." }
    return $nodes
}

function Find-Node($Root, $Type, [string]$Name) {
    @(Get-Nodes $Root | Where-Object { $_.Current.ControlType -eq $Type -and $_.Current.Name -eq $Name }) |
        Select-Object -First 1
}

$root = Join-Path $validation ('ArchiveListUia\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'target'
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$archive = Join-Path $source 'archive.zip'
$lastName = 'native-archive-1999.txt'
$firstName = 'native-archive-0000.txt'
$marker = 'FileCat archive accessibility action 1999'
$stream = [IO.File]::Create($archive)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        for ($i = 0; $i -lt 2000; $i++) {
            $name = 'native-archive-{0:D4}.txt' -f $i
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::NoCompression)
            if ($i -eq 1999) {
                $output = $entry.Open()
                try {
                    $bytes = [Text.Encoding]::UTF8.GetBytes($marker)
                    $output.Write($bytes, 0, $bytes.Length)
                }
                finally { $output.Dispose() }
            }
        }
    }
    finally { $zip.Dispose() }
}
finally { $stream.Dispose() }
$sourceHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
$result = [ordered]@{
    Passed = $false
    GitHead = $head
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    ArchiveSha256 = $sourceHash
    Fixture = $root
    Stage = 'launch'
}
$process = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    Wait-For { $process.Refresh(); if ($process.HasExited) { throw 'Owned FileCat exited early.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.MainWindowHandle } } 'owned main window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'The accessible process is not the exact Git-built test app.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    [FileCatArchiveListForeground]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    $result.Stage = 'open archive'
    $file = Wait-For { Find-Node $window ([System.Windows.Automation.ControlType]::ListItem) 'archive.zip' } 'owned archive row'
    $file.SetFocus()
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $list = Wait-For {
        @(Get-Nodes $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
            $_.Current.Name -like 'Panel 1, source,*archive.zip*' }) | Select-Object -First 1
    } 'archive provider list'
    $result.ArchiveList = $list.Current.Name
    $result.Stage = 'bounded semantics'
    $first = Wait-For { Find-Node $list ([System.Windows.Automation.ControlType]::ListItem) $firstName } 'first archive item'
    $loaded = Get-Nodes $window
    $visible = @($loaded | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -like 'native-archive-*.txt' })
    if ($loaded.Count -gt 800 -or $visible.Count -gt 120) { throw 'Archive list materialized too many native peers.' }
    $result.Loaded = @{ NodeCount = $loaded.Count; VisibleItems = $visible.Count }
    $first.SetFocus()
    $mark = [System.Windows.Automation.SelectionItemPattern]$first.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $mark.AddToSelection()
    if (!$mark.Current.IsSelected) { throw 'The first archive row did not become marked.' }
    $selection = [System.Windows.Automation.SelectionPattern]$list.GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern)
    $selected = @()
    $selectionClock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $selected = @($selection.Current.GetSelection())
        if ($selected.Count -eq 1 -and $selected[0].Current.Name -eq $firstName) { break }
        Start-Sleep -Milliseconds 150
    } while ($selectionClock.Elapsed.TotalSeconds -lt 3)
    $result.FirstSelection = @{ FirstMarked = $mark.Current.IsSelected;
        Count = $selected.Count; Names = @($selected | ForEach-Object { $_.Current.Name });
        Types = @($selected | ForEach-Object { $_.GetType().FullName });
        SettleSeconds = [math]::Round($selectionClock.Elapsed.TotalSeconds, 3);
        RowContainer = $mark.Current.SelectionContainer.Current.Name;
        RowContainerId = @($mark.Current.SelectionContainer.GetRuntimeId());
        ListId = @($list.GetRuntimeId());
        ListStatus = $list.Current.ItemStatus;
        FirstStatus = $first.Current.ItemStatus;
        CanSelectMultiple = $selection.Current.CanSelectMultiple }
    if ($selected.Count -ne 1) {
        $fresh = Find-Node $window ([System.Windows.Automation.ControlType]::List) $list.Current.Name
        $freshSelection = [System.Windows.Automation.SelectionPattern]$fresh.GetCurrentPattern(
            [System.Windows.Automation.SelectionPattern]::Pattern)
        $freshSelected = @($freshSelection.Current.GetSelection())
        $result.FreshSelection = @{ Count = $freshSelected.Count;
            Names = @($freshSelected | ForEach-Object { $_.Current.Name }) }
        if ($freshSelected.Count -eq 1 -and $freshSelected[0].Current.Name -eq $firstName) {
            $list = $fresh
            $selection = $freshSelection
            $selected = $freshSelected
        }
    }
    if ($selected.Count -ne 1 -or $selected[0].Current.Name -ne $firstName) {
        throw 'The native selected set differs from the one explicitly marked row.'
    }
    [Windows.Forms.SendKeys]::SendWait('{END}')
    $last = Wait-For { $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($focused.Current.Name -eq $lastName) { $focused } } 'last archive focus'
    $lastMark = [System.Windows.Automation.SelectionItemPattern]$last.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($lastMark.Current.IsSelected -or !$mark.Current.IsSelected) {
        throw 'Focus navigation changed the marked set.'
    }
    $selected = @($selection.Current.GetSelection())
    if ($selected.Count -ne 1 -or $selected[0].Current.Name -ne $firstName) {
        throw 'The offscreen marked item was omitted from native selection.'
    }
    $after = Get-Nodes $window
    $afterRows = @($after | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -like 'native-archive-*.txt' })
    if ($after.Count -gt 800 -or $afterRows.Count -gt 120) {
        throw 'The tail archive viewport exceeded the bounded peer budget.'
    }
    $result.AfterEnd = @{ NodeCount = $after.Count; VisibleItems = $afterRows.Count;
        Focused = $lastName; Selected = $selected[0].Current.Name; LastMarked = $lastMark.Current.IsSelected }
    $result.Stage = 'marked original location'
    [Windows.Forms.SendKeys]::SendWait('^+p')
    $search = Wait-For { Find-Node $window ([System.Windows.Automation.ControlType]::Edit) 'Search commands' } 'command search'
    $search.SetFocus()
    ([System.Windows.Automation.ValuePattern]$search.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue('Review marked items')
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $details = Wait-For { Find-Node $window ([System.Windows.Automation.ControlType]::Edit)
        'Full details of the reviewed item' } 'marked item full details'
    $value = ([System.Windows.Automation.ValuePattern]$details.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    if ($value -notlike "*$firstName*" -or $value -notlike '*archive.zip*') {
        throw "The marked review omitted the original archive location: $value"
    }
    $result.MarkedDetails = $value
    [Windows.Forms.SendKeys]::SendWait('{ESC}')
    $result.Stage = 'view actual archive member'
    $last = Wait-For { Find-Node $window ([System.Windows.Automation.ControlType]::ListItem) $lastName } 'last archive row after review'
    $last.SetFocus()
    [Windows.Forms.SendKeys]::SendWait('{F3}')
    $viewer = Wait-For {
        $tops = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        @($tops | Where-Object { $_.Current.ProcessId -eq $process.Id -and
            $_.Current.Name -like "*$lastName*FileCat Viewer*" }) | Select-Object -First 1
    } 'test-owned archive member viewer'
    $document = Wait-For { Find-Node $viewer ([System.Windows.Automation.ControlType]::Document) 'Text viewer' } 'archive text document'
    $text = ([System.Windows.Automation.ValuePattern]$document.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    if ($text -notlike "*$marker*") { throw "The actual archive member text was not exposed: $text" }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $sourceHash -or
        @(Get-ChildItem -LiteralPath $target -Force).Count -ne 0) {
        throw 'The read-only archive action changed the source or target.'
    }
    $result.ViewerText = $text
    $result.SourceUnchanged = $true
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
$result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $report -Encoding UTF8
if (!$result.Passed) { throw $result.Error }
