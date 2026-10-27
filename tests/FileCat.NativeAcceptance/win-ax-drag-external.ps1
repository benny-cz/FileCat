param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('Local', 'Archive', 'MixedRefusal', 'InternalArchiveRefusal', 'ExternalIntoArchiveRefusal', 'ExternalIntoLocalReview', 'ExternalIntoVanishedTarget')][string]$Scope = 'Local'
)

# Runs only in the disposable Windows VM. The host captures a screen while
# HoverReady exists, then creates HoverCaptured to release the held mouse.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.IO.Compression
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeDragInput {
    [StructLayout(LayoutKind.Sequential)] public struct CursorInfo {
        public int Size; public int Flags; public IntPtr Cursor; public System.Drawing.Point Position;
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int x, int y, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, int flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
'@ -ReferencedAssemblies System.Drawing

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report)) {
    throw 'Use a Git-built FileCat.exe and fresh report directly below FileCatValidation.'
}
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat processes before the isolated drag test.'
}
$git = 'C:\Program Files\Git\cmd\git.exe'
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$head = (& $git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'The test app does not belong to a readable Git checkout.'
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
        Start-Sleep -Milliseconds 120
    }
    throw "Timed out waiting for $Description."
}
function Get-TopWindow([string]$Title) {
    $tops = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    @($tops | Where-Object { $_.Current.Name -like "*$Title*" -and
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and
        $_.Current.ClassName -eq 'CabinetWClass' }) | Select-Object -First 1
}
function Close-OldTestExplorers {
    $tops = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($top in @($tops | Where-Object { ($_.Current.Name -like 'DropTarget-*' -or
        $_.Current.Name -like 'DropSource-*') -and
        $_.Current.ClassName -eq 'CabinetWClass' })) {
        ([System.Windows.Automation.WindowPattern]$top.GetCurrentPattern(
            [System.Windows.Automation.WindowPattern]::Pattern)).Close()
    }
}
function Get-Row($Window, [string]$Name) {
    $all = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    if ($all.Count -gt 1200) { throw "UI Automation exposed $($all.Count) nodes." }
    @($all | Where-Object { $_.Current.ControlType -eq
        [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq $Name }) |
        Select-Object -First 1
}
function Get-ExplorerRow($Window, [string]$Name) {
    $all = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $stem = [IO.Path]::GetFileNameWithoutExtension($Name)
    @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        ($_.Current.Name -eq $Name -or $_.Current.Name -eq $stem) }) | Select-Object -First 1
}
function Cursor-Receipt {
    $info = New-Object NativeDragInput+CursorInfo
    $info.Size = [Runtime.InteropServices.Marshal]::SizeOf($info)
    if (![NativeDragInput]::GetCursorInfo([ref]$info)) { throw 'GetCursorInfo failed.' }
    @{ Handle = $info.Cursor.ToInt64(); Flags = $info.Flags;
       X = $info.Position.X; Y = $info.Position.Y }
}
function Drop-UiSnapshot($Window) {
    $all = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    @{
        Text = @($all | Where-Object { $_.Current.Name -match '(?i)drop|reconnect|destination|review' } |
            Select-Object -First 30 | ForEach-Object { $_.Current.Name })
        Lists = @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
            $_.Current.Name -like 'Panel 2*' } | ForEach-Object { $_.Current.Name })
        Cursor = Cursor-Receipt
    }
}
function Move-Pointer([int]$FromX, [int]$FromY, [int]$ToX, [int]$ToY) {
    for ($i = 1; $i -le 25; $i++) {
        $x = [int]($FromX + ($ToX - $FromX) * $i / 25)
        $y = [int]($FromY + ($ToY - $FromY) * $i / 25)
        [NativeDragInput]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 22
    }
}

$fixture = Join-Path $validation ('DragExternal\' + [guid]::NewGuid().ToString('N'))
$externalSource = $Scope -in @('ExternalIntoArchiveRefusal', 'ExternalIntoLocalReview', 'ExternalIntoVanishedTarget')
$source = if ($externalSource) {
    Join-Path $fixture ('DropSource-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
} else { Join-Path $fixture 'source' }
$target = Join-Path $fixture ('DropTarget-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$names = @('alpha-native-drag.txt', 'beta-native-drag.txt')
$sourceHashes = @{}
$archivePath = Join-Path $source 'archive.zip'
$archiveHash = $null
$targetArchivePath = Join-Path $target 'target.zip'
$targetArchiveHash = $null
if ($Scope -in @('Local', 'InternalArchiveRefusal', 'ExternalIntoArchiveRefusal', 'ExternalIntoLocalReview', 'ExternalIntoVanishedTarget')) {
    [IO.File]::WriteAllText((Join-Path $source $names[0]), 'FileCat external drag alpha ' + $fixture, [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $source $names[1]), 'FileCat external drag beta ' + $fixture, [Text.Encoding]::UTF8)
    foreach ($name in $names) {
        $sourceHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash
    }
    if ($Scope -in @('InternalArchiveRefusal', 'ExternalIntoArchiveRefusal')) {
        $stream = [IO.File]::Create($targetArchivePath)
        try {
            $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
            try {
                $entry = $zip.CreateEntry('unchanged-sentinel.txt')
                $output = $entry.Open()
                try {
                    $bytes = [Text.Encoding]::UTF8.GetBytes('Unsupported drop target must remain unchanged.')
                    $output.Write($bytes, 0, $bytes.Length)
                }
                finally { $output.Dispose() }
            }
            finally { $zip.Dispose() }
        }
        finally { $stream.Dispose() }
        $targetArchiveHash = (Get-FileHash -LiteralPath $targetArchivePath -Algorithm SHA256).Hash
    }
    elseif ($Scope -in @('ExternalIntoLocalReview', 'ExternalIntoVanishedTarget')) {
        [IO.File]::WriteAllText((Join-Path $target 'unchanged-sentinel.txt'),
            'The review must not start a transfer.', [Text.Encoding]::UTF8)
    }
}
else {
    $stream = [IO.File]::Create($archivePath)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $archiveNames = if ($Scope -eq 'Archive') { $names } else { @($names[1]) }
            foreach ($name in $archiveNames) {
                $bytes = [Text.Encoding]::UTF8.GetBytes("FileCat external ZIP drag $name $fixture")
                $sourceHashes[$name] = [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace('-', '')
                $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::NoCompression)
                $output = $entry.Open()
                try { $output.Write($bytes, 0, $bytes.Length) }
                finally { $output.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $hasher.Dispose(); $stream.Dispose() }
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($Scope -eq 'MixedRefusal') {
        [IO.File]::WriteAllText((Join-Path $source $names[0]), 'FileCat external drag alpha ' + $fixture, [Text.Encoding]::UTF8)
        $sourceHashes[$names[0]] = (Get-FileHash -LiteralPath (Join-Path $source $names[0]) -Algorithm SHA256).Hash
        $now = [DateTime]::UtcNow.ToString('o')
        $state = @{
            SchemaVersion = 1
            Sets = @(@{
                Id = 'mixed-native-drag'; Name = 'Mixed native drag'; CreatedUtc = $now; ModifiedUtc = $now
                Items = @(
                    @{ Parent = @{ s = 'fs'; p = $source }; Name = $names[0]; Kind = 'File'; Size = (Get-Item (Join-Path $source $names[0])).Length },
                    @{ Parent = @{ s = 'zip'; p = ''; c = @{ s = 'fs'; p = $archivePath } }; Name = $names[1]; Kind = 'File'; Size = $bytes.Length }
                )
            })
        }
        [IO.File]::WriteAllText((Join-Path $data 'working-sets.json'), ($state | ConvertTo-Json -Depth 10), [Text.Encoding]::UTF8)
    }
}
$hoverReady = $report + '.HoverReady'
$hoverCaptured = $report + '.HoverCaptured'
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head; Scope = $Scope
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    Fixture = $fixture; SourceHashes = $sourceHashes; SourceArchiveSha256 = $archiveHash
    TargetArchiveSha256 = $targetArchiveHash; RequestedNames = $names
}
if ($Scope -in @('ExternalIntoLocalReview', 'ExternalIntoVanishedTarget')) {
    $result.TargetSentinelSha256 = (Get-FileHash -LiteralPath (Join-Path $target 'unchanged-sentinel.txt') -Algorithm SHA256).Hash
}
$process = $null
$mouseDown = $false
$controlDown = $false
try {
    Close-OldTestExplorers
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    Wait-For { $process.Refresh(); if ($process.HasExited) { throw 'Owned FileCat exited early.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.MainWindowHandle } } 'FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'The accessible process is not the exact Git-built test app.'
    }
    $appX = if ($externalSource) { 955 } else { 30 }
    $explorerX = if ($externalSource) { 30 } else { 955 }
    [NativeDragInput]::MoveWindow($process.MainWindowHandle, $appX, 60, 875, 910, $true) | Out-Null
    [NativeDragInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $result.Stage = 'Explorer target'
    $explorerFolder = if ($externalSource) { $source } else { $target }
    Start-Process -FilePath explorer.exe -ArgumentList @($explorerFolder) | Out-Null
    $explorer = Wait-For { Get-TopWindow ([IO.Path]::GetFileName($explorerFolder)) } 'test-owned Explorer folder'
    $explorerHandle = [IntPtr]::new($explorer.Current.NativeWindowHandle)
    [NativeDragInput]::MoveWindow($explorerHandle, $explorerX, 60, 895, 910, $true) | Out-Null
    Start-Sleep -Milliseconds 450
    [NativeDragInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    if ($Scope -eq 'Archive') {
        $result.Stage = 'open source archive'
        $archiveRow = Wait-For { Get-Row $window 'archive.zip' } 'archive row'
        $archiveRow.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Wait-For { Get-Row $window $names[0] } 'first archive member' | Out-Null
    }
    elseif ($Scope -eq 'MixedRefusal') {
        $result.Stage = 'open mixed working set'
        $places = Wait-For {
            $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
                $_.Current.Name -like 'Working sets*' }) | Select-Object -First 1
        } 'Working sets place'
        ([System.Windows.Automation.InvokePattern]$places.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        $set = Wait-For { Get-Row $window 'Mixed native drag' } 'test-owned working set'
        $set.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Wait-For { Get-Row $window $names[1] } 'archive member in working set' | Out-Null
    }
    elseif ($Scope -in @('InternalArchiveRefusal', 'ExternalIntoArchiveRefusal')) {
        $result.Stage = 'open unsupported target archive'
        $targetZip = Wait-For { Get-Row $window 'target.zip' } 'target archive row'
        $targetZip.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        $targetSentinel = Wait-For { Get-Row $window 'unchanged-sentinel.txt' } 'target archive member'
    }
    $result.Stage = 'selected complete scope'
    if ($externalSource) {
        # FileCat restores its saved normal bounds shortly after launch. Place
        # both windows once more after navigation so the source and target do
        # not overlap during the physical drag.
        [NativeDragInput]::MoveWindow($process.MainWindowHandle, 955, 60, 875, 910, $true) | Out-Null
        [NativeDragInput]::MoveWindow($explorerHandle, 30, 60, 895, 910, $true) | Out-Null
        Start-Sleep -Milliseconds 350
        [NativeDragInput]::SetForegroundWindow($explorerHandle) | Out-Null
        $first = Wait-For { Get-ExplorerRow $explorer $names[0] } 'first Explorer source row'
        $second = Wait-For { Get-ExplorerRow $explorer $names[1] } 'second Explorer source row'
    }
    else {
        $first = Wait-For { Get-Row $window $names[0] } 'first source row'
        $second = Wait-For { Get-Row $window $names[1] } 'second source row'
    }
    foreach ($row in @($first, $second)) {
        Wait-For {
            $selection = [System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern)
            $selection.AddToSelection()
            $true
        } 'native source selection' 5 | Out-Null
    }
    if ($externalSource) {
        $first = Wait-For { Get-ExplorerRow $explorer $names[0] } 'selected first Explorer row'
        $second = Wait-For { Get-ExplorerRow $explorer $names[1] } 'selected second Explorer row'
    }
    else {
        $first = Get-Row $window $names[0]
        $second = Get-Row $window $names[1]
    }
    $firstMarked = [System.Windows.Automation.SelectionItemPattern]$first.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $secondMarked = [System.Windows.Automation.SelectionItemPattern]$second.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (!$firstMarked.Current.IsSelected -or !$secondMarked.Current.IsSelected) { throw 'The two selected source rows did not remain marked.' }
    $bounds = $first.Current.BoundingRectangle
    $fromX = [int]($bounds.Left + [Math]::Min(105, $bounds.Width / 2))
    $fromY = [int]($bounds.Top + $bounds.Height / 2)
    $explorerBounds = $explorer.Current.BoundingRectangle
    if ($Scope -in @('InternalArchiveRefusal', 'ExternalIntoArchiveRefusal', 'ExternalIntoLocalReview', 'ExternalIntoVanishedTarget')) {
        if ($Scope -in @('ExternalIntoLocalReview', 'ExternalIntoVanishedTarget')) {
            $targetSentinel = Wait-For { Get-Row $window 'unchanged-sentinel.txt' } 'local target sentinel'
        }
        $targetPeer = [System.Windows.Automation.SelectionItemPattern]$targetSentinel.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $rightList = $targetPeer.Current.SelectionContainer
        if ($rightList.Current.ControlType -ne [System.Windows.Automation.ControlType]::List) {
            throw 'The archive sentinel has no accessible target list.'
        }
        $result.TargetList = $rightList.Current.Name
        $rightBounds = $rightList.Current.BoundingRectangle
        $toX = [int]($rightBounds.Left + $rightBounds.Width / 2)
        $toY = [int]($rightBounds.Top + [Math]::Min(220, $rightBounds.Height - 60))
    }
    else {
        $toX = [int]($explorerBounds.Left + $explorerBounds.Width / 2)
        $toY = [int]($explorerBounds.Top + [Math]::Min(470, $explorerBounds.Height - 100))
    }
    $result.Geometry = @{ FirstRow = @($bounds.Left, $bounds.Top, $bounds.Width, $bounds.Height);
        Explorer = @($explorerBounds.Left, $explorerBounds.Top, $explorerBounds.Width, $explorerBounds.Height);
        From = @($fromX, $fromY); To = @($toX, $toY) }
    $result.Stage = 'native drag hover'
    [NativeDragInput]::SetCursorPos($fromX, $fromY) | Out-Null
    Start-Sleep -Milliseconds 130
    [NativeDragInput]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    $mouseDown = $true
    Move-Pointer $fromX $fromY ($fromX + 12) ($fromY + 10)
    if ($Scope -eq 'MixedRefusal') {
        $notice = Wait-For {
            $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            @($all | Where-Object { $_.Current.Name -like 'No items were dragged from the 2 selected*' }) |
                Select-Object -First 1
        } 'complete-scope drag refusal' 8
        $result.Refusal = $notice.Current.Name
        if ($result.Refusal -notlike '*1 local and 1 non-file-system*' -or $result.Refusal -notlike '*F5*') {
            throw "The native refusal did not explain the complete scope and alternative: $($result.Refusal)"
        }
    }
    else {
        Start-Sleep -Milliseconds 400
        # Explorer defaults to Move on the same volume. Ctrl requests a truthful
        # Copy, so source preservation is part of these accepted export routes.
        [NativeDragInput]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
        $controlDown = $true
    }
    Move-Pointer ($fromX + 12) ($fromY + 10) $toX $toY
    Start-Sleep -Milliseconds 650
    $result.HoverCursor = Cursor-Receipt
    $hoverChild = [NativeDragInput]::WindowFromPoint([Drawing.Point]::new($toX, $toY))
    $hoverRoot = [NativeDragInput]::GetAncestor($hoverChild, 2)
    $expectedHover = if ($Scope -in @('InternalArchiveRefusal', 'ExternalIntoArchiveRefusal', 'ExternalIntoLocalReview', 'ExternalIntoVanishedTarget')) { $process.MainWindowHandle } else { $explorerHandle }
    $result.HoverWindow = @{ Expected = $expectedHover.ToInt64(); Actual = $hoverRoot.ToInt64() }
    if ($hoverRoot -ne $expectedHover) { throw 'A different window covered the intended drop target.' }
    if ($Scope -in @('InternalArchiveRefusal', 'ExternalIntoArchiveRefusal')) {
        $notice = Wait-For {
            $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            @($all | Where-Object { $_.Current.Name -like 'This panel cannot receive dropped files*' }) |
                Select-Object -First 1
        } 'hover explanation for unsupported external file drop' 4
        $result.HoverRefusal = $notice.Current.Name
    }
    [IO.File]::WriteAllText($hoverReady, 'capture hover', [Text.Encoding]::ASCII)
    Wait-For { Test-Path -LiteralPath $hoverCaptured } 'host hover screenshot' 45 | Out-Null
    if ($Scope -eq 'ExternalIntoVanishedTarget') {
        # Remove only this run's disposable destination after the valid hover.
        # A changed DragOver effect may suppress Drop, so retain either the
        # hover refusal or the drop-time revalidation refusal.
        [IO.Directory]::Delete($target, $true)
        $result.RemovedTargetBeforeDrop = !(Test-Path -LiteralPath $target)
        [NativeDragInput]::SetCursorPos(($toX + 2), $toY) | Out-Null
        Start-Sleep -Milliseconds 350
        $result.UiAfterRemoval = Wait-For { Drop-UiSnapshot $window } 'post-removal UI snapshot' 5
    }
    $result.Stage = 'external drop'
    [NativeDragInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    $mouseDown = $false
    if ($controlDown) {
        [NativeDragInput]::keybd_event(0x11, 0, 0x0002, [UIntPtr]::Zero)
        $controlDown = $false
    }
    if ($Scope -in @('Local', 'Archive')) {
        Wait-For { if (@(Get-ChildItem -LiteralPath $target -File).Count -eq 2) { $true } } 'two Explorer copies' 30 | Out-Null
    }
    elseif ($Scope -in @('InternalArchiveRefusal', 'ExternalIntoArchiveRefusal')) {
        $result.Refusal = $result.HoverRefusal
        if ($result.Refusal -notlike '*local folder*' -or $result.Refusal -notlike '*F5*') {
            throw "Unsupported-target feedback omitted a usable alternative: $($result.Refusal)"
        }
    }
    elseif ($Scope -eq 'ExternalIntoLocalReview') {
        $result.Stage = 'review local target'
        $destinationEdit = Wait-For {
            $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
                $_.Current.Name -eq 'Destination' }) | Select-Object -First 1
        } 'drop review destination' 12
        $result.ReviewDestination = ([System.Windows.Automation.ValuePattern]$destinationEdit.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
        if ($result.ReviewDestination.TrimEnd([char[]]@('\', '/')) -ine
            $target.TrimEnd([char[]]@('\', '/'))) {
            throw "Native review destination differed from the hovered local folder: $($result.ReviewDestination)"
        }
        $journal = Join-Path $data 'local\journal'
        $result.JournalFileCountBeforeCancel = if (Test-Path -LiteralPath $journal) {
            @(Get-ChildItem -LiteralPath $journal -File -Recurse).Count
        } else { 0 }
        if ($result.JournalFileCountBeforeCancel -ne 0) { throw 'The drop started a job before review approval.' }
        $cancel = Wait-For {
            $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
                $_.Current.Name -eq 'Cancel' -and $_.Current.IsEnabled }) | Select-Object -First 1
        } 'review Cancel button'
        ([System.Windows.Automation.InvokePattern]$cancel.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        $result.CanceledReview = $true
    }
    elseif ($Scope -eq 'ExternalIntoVanishedTarget') {
        $result.UiAfterDrop = Wait-For { Drop-UiSnapshot $window } 'post-drop UI snapshot' 5
        $messages = @($result.UiAfterRemoval.Text) + @($result.UiAfterDrop.Text)
        $refusals = @($messages | Where-Object {
            $_ -eq 'The drop target changed during the drag. Drop again onto the folder you want to use.' -or
            $_ -like 'The drop folder is no longer available*' -or
            $_ -like 'This folder is not available for a drop*'
        } | Select-Object -First 1)
        if ($refusals.Count -ne 1 -or
            ($refusals[0] -notlike '*Drop again*' -and $refusals[0] -notlike '*F5*')) {
            throw 'The changed or vanished target gave no actionable native refusal.'
        }
        $result.Refusal = $refusals[0]
        if (@($result.UiAfterDrop.Text | Where-Object { $_ -eq 'Destination' -or $_ -like 'To (panel*' }).Count -ne 0) {
            throw 'A transfer review opened for a folder different from the one first hovered.'
        }
    }
    else { Start-Sleep -Milliseconds 600 }
    $targetNames = if (Test-Path -LiteralPath $target) {
        @(Get-ChildItem -LiteralPath $target -File | ForEach-Object { $_.Name } | Sort-Object)
    } else { @() }
    $result.TargetNames = $targetNames
    $result.TargetHashes = @{}
    foreach ($name in $targetNames) { $result.TargetHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash }
    if ($Scope -in @('Archive', 'MixedRefusal')) {
        $result.ArchiveStillPresentSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    }
    if ($Scope -ne 'Archive') {
        $result.SourceStillPresent = @{}
        $sourceNames = if ($Scope -eq 'MixedRefusal') { @($names[0]) } else { $names }
        foreach ($name in $sourceNames) {
            $result.SourceStillPresent[$name] = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash
        }
    }
    if ($Scope -in @('InternalArchiveRefusal', 'ExternalIntoArchiveRefusal')) {
        $result.TargetArchiveStillPresentSha256 = (Get-FileHash -LiteralPath $targetArchivePath -Algorithm SHA256).Hash
        $journal = Join-Path $data 'local\journal'
        $result.JournalFileCount = if (Test-Path -LiteralPath $journal) { @(Get-ChildItem -LiteralPath $journal -File -Recurse).Count } else { 0 }
        if (@(Compare-Object @('target.zip') $targetNames).Count -ne 0 -or
            $result.TargetArchiveStillPresentSha256 -ne $targetArchiveHash -or
            $result.JournalFileCount -ne 0 -or
            $names.Where({ $result.SourceStillPresent[$_] -ne $sourceHashes[$_] }).Count -ne 0) {
            throw 'Unsupported drop changed source, target archive, Explorer manifest or job journal.'
        }
    }
    elseif ($Scope -eq 'ExternalIntoLocalReview') {
        $result.TargetSentinelStillPresentSha256 = $result.TargetHashes['unchanged-sentinel.txt']
        if (@(Compare-Object @('unchanged-sentinel.txt') $targetNames).Count -ne 0 -or
            $result.TargetSentinelStillPresentSha256 -ne $result.TargetSentinelSha256 -or
            $names.Where({ $result.SourceStillPresent[$_] -ne $sourceHashes[$_] }).Count -ne 0) {
            throw 'Canceled local review changed its source or target.'
        }
    }
    elseif ($Scope -eq 'ExternalIntoVanishedTarget') {
        $journal = Join-Path $data 'local\journal'
        $result.JournalFileCount = if (Test-Path -LiteralPath $journal) {
            @(Get-ChildItem -LiteralPath $journal -File -Recurse).Count
        } else { 0 }
        if (!$result.RemovedTargetBeforeDrop -or (Test-Path -LiteralPath $target) -or
            $targetNames.Count -ne 0 -or
            $result.JournalFileCount -ne 0 -or
            $names.Where({ $result.SourceStillPresent[$_] -ne $sourceHashes[$_] }).Count -ne 0) {
            throw 'The vanished target returned, a job started, or a source changed.'
        }
    }
    elseif ($Scope -eq 'MixedRefusal') {
        if ($targetNames.Count -ne 0 -or $result.SourceStillPresent[$names[0]] -ne $sourceHashes[$names[0]]) {
            throw 'The refused mixed drag published a partial target or changed the local source.'
        }
    }
    else {
        if (@(Compare-Object ($names | Sort-Object) $targetNames).Count -ne 0) { throw 'Explorer received an incomplete or extra manifest.' }
        foreach ($name in $names) {
            if ($result.TargetHashes[$name] -ne $sourceHashes[$name] -or
                $Scope -eq 'Local' -and $result.SourceStillPresent[$name] -ne $sourceHashes[$name]) {
                throw "Content or source changed for $name."
            }
        }
    }
    if ($Scope -in @('Archive', 'MixedRefusal') -and $result.ArchiveStillPresentSha256 -ne $archiveHash) {
        throw 'The source archive changed during external drag.'
    }
    $result.Passed = $true
    $result.Stage = 'complete'
}
catch {
    $result.Error = $_.Exception.Message
    $result.ErrorType = $_.Exception.GetType().FullName
    if ($_.Exception.InnerException) {
        $result.ErrorInnerType = $_.Exception.InnerException.GetType().FullName
    }
}
finally {
    if ($mouseDown) { [NativeDragInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) }
    if ($controlDown) { [NativeDragInput]::keybd_event(0x11, 0, 0x0002, [UIntPtr]::Zero) }
    if ($process -and !$process.HasExited) { $process.CloseMainWindow() | Out-Null }
    if ($explorer) {
        try { ([System.Windows.Automation.WindowPattern]$explorer.GetCurrentPattern(
            [System.Windows.Automation.WindowPattern]::Pattern)).Close() }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 10), [Text.Encoding]::UTF8)
}
if (!$result.Passed) { exit 1 }
