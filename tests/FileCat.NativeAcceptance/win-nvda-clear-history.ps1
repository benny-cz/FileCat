param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$NvdaPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Narrow, native Windows review of a completed Move's unused Undo record.
# Every source, destination, profile and NVDA log is test-owned.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatHistorySpeech {
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
    public static bool ClickTitle(IntPtr handle) {
        RECT rect;
        if (!GetWindowRect(handle, out rect)) return false;
        POINT point = new POINT { X = (rect.Left + rect.Right) / 2, Y = rect.Top + 12 };
        if (GetAncestor(WindowFromPoint(point), 2) != handle) return false;
        SetCursorPos(point.X, point.Y);
        mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0);
        return true;
    }
}
'@
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !$nvda.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat,nvda -ErrorAction SilentlyContinue)) {
    throw 'Use the signed VM NVDA, Git app, fresh report and no other FileCat/NVDA process.'
}
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$head = (& 'C:\Program Files\Git\cmd\git.exe' -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'App is not from a readable Git checkout.' }
$root = Join-Path $validation ('ClearHistoryNarrow\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'target'
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Force -Path $source,$target,$data | Out-Null
$sourceFile = Join-Path $source 'owned-history.txt'
$targetFile = Join-Path $target 'owned-history.txt'
[IO.File]::WriteAllText($sourceFile, "test-owned unused Undo`n", [Text.Encoding]::UTF8)
$sourceHash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash
$config = Join-Path $root 'nvda-config'
New-Item -ItemType Directory -Path $config | Out-Null
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$nvdaLog = Join-Path $root 'nvda.log'
$script:speechOffset = 0L
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head; Fixture = $root
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion
    WindowSize = '900x650'; SourceHash = $sourceHash
}
function Wait-For([scriptblock]$Check, [string]$Description, [int]$Seconds = 25) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $Seconds) {
        try { $value = & $Check; if ($value) { return $value } }
        catch {
            $inner = $_.Exception
            while ($inner -and $inner -isnot [System.Runtime.InteropServices.COMException]) {
                $inner = $inner.InnerException
            }
            if (!$inner) { throw }
        }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $Description."
}
function Descendants($Owner) {
    @($Owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Button($Owner,[string]$Name) {
    @(Descendants $Owner | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -eq $Name -and !$_.Current.IsOffscreen
    } | Select-Object -First 1)
}
function Invoke-Button($Owner,[string]$Name) {
    $button = Wait-For { Button $Owner $Name } "button $Name"
    if (!$button.Current.IsEnabled) { throw "$Name is disabled." }
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Invoke-DialogButton($Owner,[string]$Dialog,[string]$Name) {
    $group = Wait-For {
        @(Descendants $Owner | Where-Object {
            $_.Current.Name -eq $Dialog -and !$_.Current.IsOffscreen
        } | Select-Object -First 1)
    } "$Dialog group"
    Invoke-Button $group $Name
}
function Foreground([IntPtr]$Handle,[int]$ExpectedProcessId) {
    [FileCatHistorySpeech]::ShowWindow($Handle,9) | Out-Null
    (New-Object -ComObject WScript.Shell).AppActivate($ExpectedProcessId) | Out-Null
    [FileCatHistorySpeech]::SetForegroundWindow($Handle) | Out-Null
    Start-Sleep -Milliseconds 300
    [uint32]$actual = 0
    [FileCatHistorySpeech]::GetWindowThreadProcessId(
        [FileCatHistorySpeech]::GetForegroundWindow(),[ref]$actual) | Out-Null
    if ($actual -ne $ExpectedProcessId -and [FileCatHistorySpeech]::ClickTitle($Handle)) {
        Start-Sleep -Milliseconds 300
        [FileCatHistorySpeech]::GetWindowThreadProcessId(
            [FileCatHistorySpeech]::GetForegroundWindow(),[ref]$actual) | Out-Null
    }
    if ($actual -ne $ExpectedProcessId) { throw "FileCat is not foreground (actual $actual)." }
}
function TakeSpeech {
    if (!(Test-Path -LiteralPath $nvdaLog)) { return @() }
    $stream = [IO.File]::Open($nvdaLog,'Open','Read','ReadWrite')
    try {
        $stream.Seek($script:speechOffset,'Begin') | Out-Null
        $reader = [IO.StreamReader]::new($stream,[Text.Encoding]::UTF8)
        $content = $reader.ReadToEnd()
        $last = $content.LastIndexOf("`n")
        if ($last -lt 0) { return @() }
        $complete = $content.Substring(0,$last+1)
        $script:speechOffset += [Text.Encoding]::UTF8.GetByteCount($complete)
        @($complete -split "`r?`n" | Where-Object { $_.StartsWith('Speaking [') } |
            ForEach-Object { $_.Substring(9).Trim() })
    } finally { $stream.Dispose() }
}
$owned = $nvdaProcess = $null
try {
    $nvdaProcess = Start-Process -FilePath $nvda -ArgumentList @("--log-file=$nvdaLog",
        '--log-level=12',"--config-path=$config",'--disable-addons') -PassThru -WindowStyle Hidden
    Wait-For {
        if ($nvdaProcess.HasExited) { throw 'Owned NVDA exited early.' }
        if ((Test-Path -LiteralPath $nvdaLog) -and
            (Select-String -LiteralPath $nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA startup' 30 | Out-Null
    TakeSpeech | Out-Null
    $owned = Start-Process -FilePath $app -ArgumentList @('--new-instance','--data',$data,
        '--left',$source,'--right',$target) -PassThru
    Wait-For { $owned.Refresh(); if ($owned.HasExited) { throw 'Owned FileCat exited early.' }
        if ($owned.MainWindowHandle -ne [IntPtr]::Zero) { $owned.MainWindowHandle } } 'FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($owned.MainModule.FileName) -ine $app) { throw 'Wrong app owns window.' }
    [FileCatHistorySpeech]::MoveWindow($owned.MainWindowHandle,950,90,900,650,$true) | Out-Null
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle)
    Foreground $owned.MainWindowHandle $owned.Id
    $row = Wait-For {
        @(Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -eq 'owned-history.txt'
        } | Select-Object -First 1)
    } 'owned source row'
    $row.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{F6}')
    $result.Stage = 'Move review'
    Invoke-DialogButton $window 'Move dialog' 'Move'
    Wait-For { if ((Test-Path -LiteralPath $targetFile -PathType Leaf) -and
        !(Test-Path -LiteralPath $sourceFile)) { $true } } 'completed owned Move' 45 | Out-Null
    if ((Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash -ne $sourceHash) {
        throw 'Completed Move changed file bytes.'
    }
    Invoke-Button $window 'Details (Ctrl+J)'
    Wait-For { Button $window 'Undo' } 'unused Undo action' | Out-Null
    $result.UndoBefore = $true
    $result.Stage = 'Clear finished history review'
    Foreground $owned.MainWindowHandle $owned.Id
    TakeSpeech | Out-Null
    Invoke-Button $window ('Clear finished history' + [char]0x2026)
    $keep = Wait-For { Button $window 'Keep history' } 'Keep history choice'
    $list = Wait-For {
        @(Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
            $_.Current.Name -eq 'Finished operations and records to remove'
        } | Select-Object -First 1)
    } 'finished-history review list'
    $result.ListHelp = $list.Current.HelpText
    $result.KeepHelp = $keep.Current.HelpText
    $result.ReviewVisible = @((Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        !$_.Current.IsOffscreen
    }) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match 'Undo|Reports|File changes' } |
        Select-Object -Last 18)
    $result.KeepBounds = @($keep.Current.BoundingRectangle.Left,
        $keep.Current.BoundingRectangle.Top,$keep.Current.BoundingRectangle.Width,
        $keep.Current.BoundingRectangle.Height)
    Foreground $owned.MainWindowHandle $owned.Id
    Start-Sleep -Milliseconds 500
    $result.ListSpeech = @(TakeSpeech)
    $keep.SetFocus()
    Start-Sleep -Milliseconds 750
    $result.KeepSpeech = @(TakeSpeech)
    $heard = ($result.ListSpeech + $result.KeepSpeech) -join ' '
    $result.SpokeUndoLoss = $heard.Contains('unused Undo record')
    $result.SpokeNoFileChange = $heard.Contains('does not change files')
    Invoke-Button $window 'Keep history'
    Wait-For { if (!(Button $window 'Keep history')) { $true } } 'history review cancellation' | Out-Null
    $result.UndoAfter = [bool](Button $window 'Undo')
    $result.ReportAfter = [bool](Button $window 'Open report')
    $result.FilesIntact = !(Test-Path -LiteralPath $sourceFile) -and
        (Test-Path -LiteralPath $targetFile -PathType Leaf) -and
        (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash -eq $sourceHash
    $result.Passed = $result.UndoBefore -and $result.UndoAfter -and
        $result.ReportAfter -and $result.FilesIntact -and
        $result.SpokeUndoLoss -and $result.SpokeNoFileChange -and
        $result.KeepBounds[2] -gt 0 -and $result.KeepBounds[3] -gt 0
    $result.Stage = 'complete'
} catch {
    $result.Error = $_.Exception.Message
    if ($window) {
        try {
            $result.VisibleButtonsAtFailure = @((Descendants $window | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
                !$_.Current.IsOffscreen
            }) | ForEach-Object { $_.Current.Name } | Select-Object -Last 45)
        } catch { }
    }
} finally {
    if ($owned -and !$owned.HasExited) {
        $owned.CloseMainWindow() | Out-Null
        if (!$owned.WaitForExit(5000)) {
            $owned.Kill(); $owned.WaitForExit(5000) | Out-Null
        }
    }
    if ($nvdaProcess -and !$nvdaProcess.HasExited) {
        $nvdaProcess.Kill(); $nvdaProcess.WaitForExit(5000) | Out-Null
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
