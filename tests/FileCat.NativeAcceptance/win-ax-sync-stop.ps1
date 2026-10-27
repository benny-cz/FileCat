param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Disposable Windows VMware fixture for AC-S2-021. The reviewed Mirror plan
# contains one Copy child (small then large) and one target-only removal.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatSyncStopInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    public static void Press(byte vk) {
        byte scan = (byte)MapVirtualKey(vk, 0);
        keybd_event(vk, scan, 0, UIntPtr.Zero);
        keybd_event(vk, scan, 2, UIntPtr.Zero);
    }
}
'@
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use a Git-built VM app, a fresh report and no other FileCat process.'
}
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$head = (& 'C:\Program Files\Git\cmd\git.exe' -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'The app is not from a readable Git checkout.' }

function Wait-For([scriptblock]$Check,[string]$Description,[int]$Seconds=30) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $Seconds) {
        try { $value = & $Check; if ($value) { return $value } }
        catch {
            $inner = $_.Exception
            while ($inner -and $inner -isnot [System.Runtime.InteropServices.COMException]) { $inner = $inner.InnerException }
            if (!$inner) { throw }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $Description."
}
function Descendants($Owner) {
    @($Owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Control($Owner,$Type,[string]$Name) {
    @(Descendants $Owner | Where-Object {
        $_.Current.ControlType -eq $Type -and $_.Current.Name -eq $Name -and !$_.Current.IsOffscreen
    } | Select-Object -First 1)
}
function Button-Starts($Owner,[string]$Prefix) {
    @(Descendants $Owner | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name.StartsWith($Prefix,[StringComparison]::Ordinal) -and
        $_.Current.IsEnabled -and !$_.Current.IsOffscreen
    } | Select-Object -First 1)
}
function Click-Starts($Owner,[string]$Prefix) {
    $button = Wait-For { Button-Starts $Owner $Prefix } "button beginning $Prefix"
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Window-ByName([int]$ProcessId,[string]$Name) {
    @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -eq $Name
        } | Select-Object -First 1)
}
function Visible-Text($Owner) {
    @((Descendants $Owner | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and !$_.Current.IsOffscreen
    }) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
}
function Child-Report($Owner,[int]$ProcessId,[string]$Title) {
    $history = Wait-For { Control $Owner ([System.Windows.Automation.ControlType]::List) 'Operations history' } 'Operations history'
    $row = Wait-For { @(Descendants $history | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        @((Descendants $_) | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -eq $Title
        }).Count -gt 0
    } | Select-Object -First 1) } "history row $Title"
    ([System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Click-Starts $Owner 'Open report'
    $reportWindow = Wait-For { @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like '*Operation report*'
        } | Select-Object -First 1) } "$Title report window"
    $sentinel = 'FileCatSyncReport-' + [guid]::NewGuid().ToString('N')
    [System.Windows.Forms.Clipboard]::SetText($sentinel)
    Click-Starts $reportWindow 'Copy all'
    $text = Wait-For {
        $value = [System.Windows.Forms.Clipboard]::GetText()
        if ($value.Length -gt 0 -and $value -ne $sentinel) { $value }
    } "$Title report contents"
    ([System.Windows.Automation.WindowPattern]$reportWindow.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern)).Close()
    return $text
}

$fixture = Join-Path $validation ('SyncStop\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source,$target,$data | Out-Null
$small = Join-Path $source 'a-small.txt'
$large = Join-Path $source 'b-large.bin'
$extra = Join-Path $target 'z-extra.txt'
[IO.File]::WriteAllText($small,'first committed copy effect',[Text.Encoding]::UTF8)
[IO.File]::WriteAllText($extra,'target-only item stays until approved removal',[Text.Encoding]::UTF8)
$stream = [IO.File]::Create($large)
try { $stream.SetLength(1024L * 1024 * 1024) } finally { $stream.Dispose() }
$smallHash = (Get-FileHash -LiteralPath $small -Algorithm SHA256).Hash
$largeHash = (Get-FileHash -LiteralPath $large -Algorithm SHA256).Hash
$extraHash = (Get-FileHash -LiteralPath $extra -Algorithm SHA256).Hash
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head; Fixture = $fixture
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    SmallHash = $smallHash; LargeHash = $largeHash; ExtraHash = $extraHash
}
$originalClipboard = [System.Windows.Forms.Clipboard]::GetDataObject()
$owned = $null
try {
    $owned = Start-Process -FilePath $app -ArgumentList @('--new-instance','--data',$data,
        '--left',$source,'--right',$target) -PassThru
    Wait-For { $owned.Refresh(); if ($owned.HasExited) { throw 'Owned FileCat exited.' }
        if ($owned.MainWindowHandle -ne [IntPtr]::Zero) { $owned.MainWindowHandle } } 'FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($owned.MainModule.FileName) -ine $app) { throw 'Wrong app owns the window.' }
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle)
    [FileCatSyncStopInput]::SetForegroundWindow($owned.MainWindowHandle) | Out-Null
    $result.Stage = 'compare owned folders'
    $search = Wait-For { Control $main ([System.Windows.Automation.ControlType]::Edit) 'Search commands' } 'command search'
    ([System.Windows.Automation.ValuePattern]$search.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue('compare directories')
    $search.SetFocus()
    [FileCatSyncStopInput]::Press(0x0D)
    $recursive = Wait-For {
        Control $main ([System.Windows.Automation.ControlType]::CheckBox) 'Include subfolders: a preview of every difference below, marking nothing'
    } 'recursive comparison choice'
    ([System.Windows.Automation.TogglePattern]$recursive.GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    $compare = Wait-For { Button-Starts $main 'Compare' } 'Compare action'
    ([System.Windows.Automation.InvokePattern]$compare.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $diff = Wait-For { @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ProcessId -eq $owned.Id -and $_.Current.Name -like 'Compare folders:*'
        } | Select-Object -First 1) } 'completed folder comparison' 60
    Click-Starts $diff 'Synchronize'
    $sync = Wait-For { Window-ByName $owned.Id 'Synchronize folders' } 'sync review'
    $result.Stage = 'review Mirror copy and removal'
    $mode = Wait-For { Control $sync ([System.Windows.Automation.ControlType]::ComboBox) 'Mode' } 'sync mode'
    $mode.SetFocus()
    [FileCatSyncStopInput]::SetForegroundWindow([IntPtr]$sync.Current.NativeWindowHandle) | Out-Null
    [FileCatSyncStopInput]::Press(0x28)
    $result.PlanText = Wait-For {
        $text = Visible-Text $sync
        if (@($text | Where-Object { $_ -like '*copy 2*' }).Count -gt 0 -and
            @($text | Where-Object { $_ -like '*remove 1*' }).Count -gt 0) { $text }
    } 'reviewed copy 2 and remove 1 plan'
    $consent = @(Descendants $sync | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::CheckBox -and
        $_.Current.Name -like 'Allow permanent deletion from target*' -and !$_.Current.IsOffscreen
    } | Select-Object -First 1)
    if ($consent.Count -eq 1) {
        ([System.Windows.Automation.TogglePattern]$consent[0].GetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
        $result.PermanentRemovalConsent = $true
    }
    $run = Wait-For { Button-Starts $sync 'Synchronize from ' } 'reviewed Synchronize action'
    $result.ReviewedAction = $run.Current.Name
    if (!$result.ReviewedAction.Contains('copy 2') -or !$result.ReviewedAction.Contains('remove 1')) {
        throw 'The approved action did not include exactly the intended copy and removal.'
    }
    ([System.Windows.Automation.InvokePattern]$run.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $result.Stage = 'first copy effect before Stop'
    Click-Starts $main 'Details (Ctrl+J)'
    $smallTarget = Join-Path $target 'a-small.txt'
    Wait-For { if ((Test-Path -LiteralPath $smallTarget -PathType Leaf) -and
        (Get-FileHash -LiteralPath $smallTarget -Algorithm SHA256).Hash -eq $smallHash) { $true } } 'first exact copied file' 60 | Out-Null
    $result.BeforeStop = Visible-Text $main | Where-Object {
        $_ -match 'Synchronization|Copy|copy|remove|Running|Queued|Paused'
    } | Select-Object -Last 25
    if (!(Test-Path -LiteralPath $extra -PathType Leaf) -or
        (Get-FileHash -LiteralPath $extra -Algorithm SHA256).Hash -ne $extraHash) {
        throw 'Target-only item changed before the group Stop action.'
    }
    $result.Stage = 'Stop group during unfinished copy'
    Click-Starts $main 'Stop this synchronization'
    $result.StopAcknowledgement = Wait-For {
        @(Visible-Text $main | Where-Object { $_ -like 'Synchronization stopped*' } | Select-Object -First 1)
    } 'stopped synchronization outcome' 90
    $result.AfterStop = Visible-Text $main | Where-Object {
        $_ -match 'Synchronization|Copy|copy|remove|Canceled|done|skipped'
    } | Select-Object -Last 35
    $result.Stage = 'stable exact effects and child reports'
    Start-Sleep -Seconds 3
    $result.SmallExact = (Test-Path -LiteralPath $smallTarget -PathType Leaf) -and
        (Get-FileHash -LiteralPath $smallTarget -Algorithm SHA256).Hash -eq $smallHash
    $result.ExtraExact = (Test-Path -LiteralPath $extra -PathType Leaf) -and
        (Get-FileHash -LiteralPath $extra -Algorithm SHA256).Hash -eq $extraHash
    $result.SourceExact = (Get-FileHash -LiteralPath $small -Algorithm SHA256).Hash -eq $smallHash -and
        (Get-FileHash -LiteralPath $large -Algorithm SHA256).Hash -eq $largeHash
    $result.OverrunFiles = @(Get-ChildItem -LiteralPath $target -File -Force | Where-Object {
        $_.Name -notin @('a-small.txt','z-extra.txt')
    } | ForEach-Object Name)
    $result.Stage = 'inspect both retained child reports'
    $copyReport = Child-Report $main $owned.Id 'Synchronize: copy new items'
    $removalReport = Child-Report $main $owned.Id 'Synchronize: remove items only in the target'
    $result.CopyReport = [ordered]@{
        Canceled = $copyReport.Contains('State: Canceled')
        OneDone = $copyReport.Contains('Items: 1 done; 0 skipped; 0 failed')
        CorrectAction = $copyReport.Contains('Requested action: Copy')
        TargetNamed = $copyReport.Contains('target')
    }
    $result.RemovalReport = [ordered]@{
        Canceled = $removalReport.Contains('State: Canceled')
        ZeroDone = $removalReport.Contains('Items: 0 done; 0 skipped; 0 failed')
        CorrectAction = $removalReport.Contains('Requested action: Recycle') -or
            $removalReport.Contains('Requested action: Delete')
    }
    $result.Passed = $result.SmallExact -and $result.ExtraExact -and $result.SourceExact -and
        $result.OverrunFiles.Count -eq 0 -and $result.StopAcknowledgement.Count -eq 1 -and
        $result.CopyReport.Canceled -and $result.CopyReport.OneDone -and
        $result.CopyReport.CorrectAction -and $result.CopyReport.TargetNamed -and
        $result.RemovalReport.Canceled -and $result.RemovalReport.ZeroDone -and
        $result.RemovalReport.CorrectAction
    if (!$result.Passed) { throw 'Stopped sync effects or retained child reports were incomplete.' }
    $result.Stage = 'complete'
} catch {
    $result.Error = $_.Exception.Message
    try { $result.VisibleAtFailure = @(Visible-Text $main | Select-Object -Last 45) } catch { }
    try { $result.ButtonsAtFailure = @(Descendants $main | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and !$_.Current.IsOffscreen
    } | ForEach-Object { $_.Current.Name } | Select-Object -Last 45) } catch { }
} finally {
    if ($owned -and !$owned.HasExited) {
        $owned.CloseMainWindow() | Out-Null
        if (!$owned.WaitForExit(7000)) { $owned.Kill(); $owned.WaitForExit(5000) | Out-Null }
    }
    if ($originalClipboard) {
        try { [System.Windows.Forms.Clipboard]::SetDataObject($originalClipboard,$true) }
        catch { $result.ClipboardRestoreError = $_.Exception.Message }
    }
    [IO.File]::WriteAllText($report,($result | ConvertTo-Json -Depth 7),[Text.Encoding]::UTF8)
}
if (!$result.Passed) { exit 1 }
