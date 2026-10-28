param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# AC-S3-065 with NVDA: a one-panel workspace made in the app (panel 2 closed from command search) is saved on exit and
# restored by the next launch as one panel with no target. There F5 and F6 ask for a destination, saying why none is
# filled in, and a copy to a typed folder works. The panel's sole tab survives a real middle click and Ctrl+W, NVDA
# hearing why each time; nothing is recorded as a closed tab (Ctrl+Shift+T says so), and the tab's history goes back.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatOnePanelInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    public static void ClickTitle(IntPtr h) {
        RECT r; GetWindowRect(h, out r);
        double scale = GetDpiForWindow(h) / 96.0;
        SetCursorPos(Math.Max(r.Left + (int)(40 * scale), r.Right - (int)(300 * scale)), r.Top + (int)(12 * scale));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@
$TreeScope = [System.Windows.Automation.TreeScope]
$AE = [System.Windows.Automation.AutomationElement]
function Descendants($element) { @($element.FindAll($TreeScope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) }
function Await([scriptblock] $check, [string] $description, [int] $seconds = 20) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $check; if ($null -ne $value -and $value -ne $false) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $description"
}
function In($root, [string] $name, [string] $type = $null) {
    @(Descendants $root | Where-Object {
        $_.Current.Name -eq $name -and (-not $type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$type") })[0]
}
function Key([byte] $code, [bool] $extended = $false) {
    $flags = if ($extended) { 1 } else { 0 }
    [FileCatOnePanelInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [FileCatOnePanelInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250
}
function Chord([byte[]] $codes, [byte] $extendedLast = 0) {
    foreach ($code in $codes) {
        $flags = if ($extendedLast -and $code -eq $codes[-1]) { 1 } else { 0 }
        [FileCatOnePanelInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    }
    $reversed = @($codes); [array]::Reverse($reversed)
    foreach ($code in $reversed) {
        $flags = if ($extendedLast -and $code -eq $codes[-1]) { 3 } else { 2 }
        [FileCatOnePanelInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 300
}
function TypeText([string] $letters) {
    foreach ($c in $letters.ToUpperInvariant().ToCharArray()) { if ($c -eq ' ') { Key 0x20 } else { Key ([byte][char]$c) } }
}
function Pointer($element, [uint32] $down, [uint32] $up) {
    $r = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $r.Width -lt 6 -or $r.Height -lt 6) { throw "Unreachable pointer target: $($element.Current.Name)" }
    [void][FileCatOnePanelInput]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 150
    [FileCatOnePanelInput]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [FileCatOnePanelInput]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 600
}
function TakeSpeech {
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    @($fresh | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim().Replace('\\', '\') })
}
# Speech heard within a few seconds that contains the text.
function Hear([string] $text, [int] $seconds = 6) {
    $heard = @()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        $heard += @(TakeSpeech)
        if (@($heard | Where-Object { $_.Contains($text) }).Count) { return $heard }
        Start-Sleep -Milliseconds 200
    }
    throw "NVDA did not say '$text': $($heard -join ' / ')"
}
function Activate($window) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][FileCatOnePanelInput]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 400
    if ([FileCatOnePanelInput]::GetForegroundWindow() -ne $handle) { [FileCatOnePanelInput]::ClickTitle($handle); Start-Sleep -Milliseconds 600 }
    if ([FileCatOnePanelInput]::GetForegroundWindow() -ne $handle) { throw "$($window.Current.Name) is not foreground; refusing keys" }
}
function Focused { try { $AE::FocusedElement.Current.Name } catch { $null } }
# A modifier left down (an Alt whose release a stopping screen reader swallowed) turns every later key into a
# shortcut: let go of any that are down, before the journey and after it.
function ReleaseModifiers {
    $released = @()
    foreach ($k in 0xA4, 0xA5, 0x12, 0xA2, 0xA3, 0x11, 0xA0, 0xA1, 0x10, 0x5B, 0x5C) {
        if ([FileCatOnePanelInput]::GetAsyncKeyState($k) -band 0x8000) {
            [FileCatOnePanelInput]::keybd_event([byte]$k, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50; $released += $k
        }
    }
    $released
}
function Lists($main) {
    @(Descendants $main | Where-Object { $_.Current.ClassName -eq 'FileList' } | ForEach-Object { $_ })
}
function TabNames($main) {
    @(Descendants $main | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and
        $_.Current.Name -match ', (Unlocked tab|Navigation lock|Root lock)' } | ForEach-Object { $_.Current.Name })
}
# Moves the cursor to the row whose name starts with the item's name ("a.txt, file, …").
function FocusRow([string] $name) {
    Key 0x24 $true  # Home
    for ($i = 0; $i -lt 8; $i++) {
        $at = Focused
        if ($at -eq $name -or $at -like "$name,*") { return }
        Key 0x28 $true
    }
    throw "The cursor never reached $name (at '$(Focused)')"
}
function StartApp([string[]] $arguments) {
    $process = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru -ArgumentList $arguments
    Await { $process.Refresh(); if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'owned FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) { throw 'Unexpected FileCat executable' }
    Start-Sleep 3
    $process
}

$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation.TrimEnd('\') + '\'
if (-not $app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    -not $nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or (Get-Process -Name nvda -ErrorAction SilentlyContinue) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an isolated Git app, signed portable NVDA, a fresh validation report and no competing reader or FileCat'
}
$fixture = Join-Path $validation ('OnePanelFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'; $config = Join-Path $fixture 'nvda-config'
$sub = Join-Path $left 'sub'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config, $sub | Out-Null
[IO.File]::WriteAllText((Join-Path $left 'a.txt'), "copied in one-panel mode`n")
[IO.File]::WriteAllText((Join-Path $left 'b.txt'), "not moved`n")
[IO.File]::WriteAllText((Join-Path $sub 'inner.txt'), "inside`n")
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$script:nvdaLog = Join-Path $fixture 'nvda.log'
$script:speechIndex = 0
$script:result = [ordered]@{
    Passed = $false; Stage = 'starting'; Fixture = $fixture; Dpi = $null; SavedPanels = $null; Restored = $null
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; Error = $null
}
$reader = $first = $second = $null
$script:result.ReleasedBefore = @(ReleaseModifiers)
try {
    $reader = Start-Process -FilePath $nvda -ArgumentList @(
        "--log-file=$script:nvdaLog", '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Await {
        if ($reader.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $script:nvdaLog) -and (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA initialization' 30 | Out-Null

    # 1. Make the workspace one panel in the app and leave it, which saves it.
    $script:result.Stage = 'close panel 2'
    $first = StartApp @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    $main = $AE::FromHandle($first.MainWindowHandle)
    $script:result.Dpi = [FileCatOnePanelInput]::GetDpiForWindow($first.MainWindowHandle)
    Activate $main
    $lists = Await { $l = @(Lists $main); if ($l.Count -eq 2) { $l } } 'two panels'
    $script:result.ListsBefore = @($lists | ForEach-Object { $_.Current.Name })
    $panel2 = @($lists | Where-Object { $_.Current.Name -like 'Panel 2*' })[0]
    if (-not $panel2) { throw "No panel 2: $($script:result.ListsBefore -join ' | ')" }
    Pointer $panel2 2 4
    Await { @(Lists $main | Where-Object { $_.Current.Name -like 'Panel 2, source*' }).Count } 'panel 2 to be the source' 8 | Out-Null
    Chord @(0x11, 0x10, 0x50)  # Ctrl+Shift+P
    Await { In $main 'Search commands' 'Edit' } 'command search' | Out-Null
    Chord @(0x11, 0x41)
    TypeText 'close panel'
    Start-Sleep -Milliseconds 700
    [void](TakeSpeech)
    Key 0x0D
    Start-Sleep -Milliseconds 1500
    # It asks first; focus is on its Close panel button.
    try { $confirm = Await { In $main 'Close panel dialog' } 'the Close panel question' 8 }
    catch {
        $script:result.ClosePanelMissing = [ordered]@{
            Speech = @(TakeSpeech); Focused = Focused; Lists = @(Lists $main | ForEach-Object { $_.Current.Name })
            Named = @(Descendants $main | Where-Object { $_.Current.Name -like '*dialog*' -or $_.Current.Name -like '*panel*' } |
                ForEach-Object { "$($_.Current.ControlType.ProgrammaticName): $($_.Current.Name)" } | Select-Object -First 30)
        }
        throw
    }
    $script:result.ClosePanel = [ordered]@{
        Speech = @(TakeSpeech); Focused = Focused
        Texts = @(Descendants $confirm | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' } | ForEach-Object { $_.Current.Name })
    }
    if ($script:result.ClosePanel.Focused -ne 'Close panel') { throw "The question's focus is on '$($script:result.ClosePanel.Focused)'" }
    Key 0x0D
    Await { @(Lists $main).Count -eq 1 } 'one panel' 10 | Out-Null
    $script:result.ListsAfterClose = @(Lists $main | ForEach-Object { $_.Current.Name })
    # Closing the window, as its close button does, saves the workspace.
    $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    if (-not $first.WaitForExit(15000)) { throw 'FileCat did not exit when its window closed' }
    $workspaceFile = Join-Path $data 'workspace.json'
    $saved = Get-Content -Raw -LiteralPath $workspaceFile | ConvertFrom-Json
    $script:result.SavedPanels = @($saved.Panels).Count
    if (@($saved.Panels).Count -ne 1) { throw "The saved workspace has $(@($saved.Panels).Count) panels" }

    # 2. The next launch restores one panel with no target.
    $script:result.Stage = 'restore'
    [void](TakeSpeech)
    $second = StartApp @('--new-instance', '--data', $data)
    $main = $AE::FromHandle($second.MainWindowHandle)
    Activate $main
    $lists = Await { $l = @(Lists $main); if ($l.Count -ge 1) { $l } } 'the restored panel'
    Start-Sleep -Milliseconds 800
    $script:result.Restored = @(Lists $main | ForEach-Object { $_.Current.Name })
    if (@($script:result.Restored).Count -ne 1 -or $script:result.Restored[0] -notlike "*$left*") {
        throw "The launch restored $($script:result.Restored -join ' | ')"
    }
    # The lone panel is the source of what F5 and F6 do; nothing is its target.
    if ($script:result.Restored[0] -match ', target,') { throw "The lone panel is called a target: $($script:result.Restored[0])" }
    $script:result.RestoreSpeech = @(TakeSpeech)

    # 3. F5 asks for a destination and says why none is filled in; a typed folder takes the copy.
    $script:result.Stage = 'F5'
    FocusRow 'a.txt'
    [void](TakeSpeech)
    Key 0x74  # F5
    $dialog = Await { In $main 'Copy dialog' } 'the Copy dialog'
    $destination = Await { In $dialog 'Destination' 'Edit' } 'the Destination box'
    Start-Sleep -Milliseconds 1200
    $script:result.CopyDialog = [ordered]@{
        Destination = $destination.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        Help = $destination.Current.HelpText
        Texts = @(Descendants $dialog | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' } | ForEach-Object { $_.Current.Name })
        Speech = @(TakeSpeech)
    }
    if ($script:result.CopyDialog.Destination) { throw "F5 filled in a destination: $($script:result.CopyDialog.Destination)" }
    if (-not @($script:result.CopyDialog.Speech | Where-Object { $_.Contains('There is no target panel. Type the folder to copy to.') }).Count) {
        throw "NVDA did not hear why F5 has no destination: $($script:result.CopyDialog.Speech -join ' / ')"
    }
    $destination.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($right)
    # Enter starts the copy once the typed folder is checked; until then it keeps the dialog open.
    for ($attempt = 0; $attempt -lt 5 -and (In $main 'Copy dialog'); $attempt++) {
        Start-Sleep -Milliseconds 800
        $script:result.CopyDialog.TypedHelp = $destination.Current.HelpText
        $destination.SetFocus()
        Start-Sleep -Milliseconds 200
        Key 0x0D
        try { Await { -not (In $main 'Copy dialog') } 'the Copy dialog to close' 3 | Out-Null } catch { }
    }
    Await { Test-Path -LiteralPath (Join-Path $right 'a.txt') } 'the copy into the typed folder' 15 | Out-Null
    $script:result.CopySpeech = @(TakeSpeech)

    # 4. F6 asks the same way; Escape leaves b.txt where it was.
    $script:result.Stage = 'F6'
    Activate $main
    Await { -not (In $main 'Copy dialog') } 'the Copy dialog to close' 8 | Out-Null
    FocusRow 'b.txt'
    [void](TakeSpeech)
    Key 0x75  # F6
    $dialog = Await { In $main 'Move dialog' } 'the Move dialog'
    $destination = Await { In $dialog 'Destination' 'Edit' } 'the Move destination box'
    Start-Sleep -Milliseconds 1200
    $script:result.MoveDialog = [ordered]@{
        Destination = $destination.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        Help = $destination.Current.HelpText
        Speech = @(TakeSpeech)
    }
    if ($script:result.MoveDialog.Destination) { throw "F6 filled in a destination: $($script:result.MoveDialog.Destination)" }
    if (-not @($script:result.MoveDialog.Speech | Where-Object { $_.Contains('There is no target panel. Type the folder to move to.') }).Count) {
        throw "NVDA did not hear why F6 has no destination: $($script:result.MoveDialog.Speech -join ' / ')"
    }
    Key 0x1B
    Await { -not (In $main 'Move dialog') } 'the Move dialog to close' 8 | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $left 'b.txt'))) { throw 'Escape from F6 moved b.txt' }

    # 5. Into sub, so the tab has history; then the sole tab survives a middle click and Ctrl+W.
    $script:result.Stage = 'last tab'
    FocusRow 'sub'
    Key 0x0D
    Await { @(Lists $main | Where-Object { $_.Current.Name -like "*$sub*" }).Count } 'the sub folder' 8 | Out-Null
    $tab = Await { @(Descendants $main | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and
        $_.Current.Name -match ', Unlocked tab, ' })[0] } 'the sole tab'
    [void](TakeSpeech)
    Pointer $tab 0x20 0x40  # middle click
    $script:result.MiddleClick = [ordered]@{ Speech = @(Hear 'A panel always keeps one tab'); Tabs = @(TabNames $main) }
    if (@($script:result.MiddleClick.Tabs).Count -ne 1) { throw "A middle click changed the tabs: $($script:result.MiddleClick.Tabs -join ' | ')" }
    Activate $main
    Chord @(0x11, 0x57)  # Ctrl+W
    $script:result.CtrlW = [ordered]@{ Speech = @(Hear 'A panel always keeps one tab'); Tabs = @(TabNames $main) }
    if (@($script:result.CtrlW.Tabs).Count -ne 1) { throw 'Ctrl+W closed the sole tab' }
    Chord @(0x11, 0x10, 0x54)  # Ctrl+Shift+T
    $script:result.Reopen = @(Hear 'No recently closed tabs')

    # 6. The tab's own history still goes back.
    $script:result.Stage = 'history'
    Chord @(0x12, 0x25) 0x25  # Alt+Left
    Await { @(Lists $main | Where-Object { $_.Current.Name -like "*$left" -or $_.Current.Name -like "*$left,*" }).Count } 'Back to the left folder' 8 | Out-Null
    $script:result.AfterBack = @(Lists $main | ForEach-Object { $_.Current.Name })
    $script:result.Passed = $true
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message }
finally {
    foreach ($process in $first, $second) {
        if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(5000) | Out-Null }
    $script:result.ReleasedAfter = @(ReleaseModifiers)
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
