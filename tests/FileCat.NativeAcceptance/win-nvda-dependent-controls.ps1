param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# AC-S3-063 with NVDA (mouse tracking and tooltip reporting on): in Settings › Behavior, a subordinate option keeps its
# draft value while its parent is turned off by keyboard, says why it is unavailable to a pointer hovering it, and
# comes back with that value when the parent is on again; Cancel keeps the saved choices. In Settings › Columns, the
# Name column's Remove and Up and, once New profile has been pressed by keyboard up to the limit, New and Duplicate
# say why they are unavailable; Cancel keeps the saved profiles.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatDependentInput {
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
    [FileCatDependentInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [FileCatDependentInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) { [FileCatDependentInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    $reversed = @($codes); [array]::Reverse($reversed)
    foreach ($code in $reversed) { [FileCatDependentInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    Start-Sleep -Milliseconds 300
}
# A modifier left down (an Alt whose release a stopping screen reader swallowed) turns later keys into shortcuts.
function ReleaseModifiers {
    $released = @()
    foreach ($k in 0xA4, 0xA5, 0x12, 0xA2, 0xA3, 0x11, 0xA0, 0xA1, 0x10, 0x5B, 0x5C) {
        if ([FileCatDependentInput]::GetAsyncKeyState($k) -band 0x8000) {
            [FileCatDependentInput]::keybd_event([byte]$k, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50; $released += $k
        }
    }
    $released
}
function TakeSpeech {
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    @($fresh | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim().Replace('\\', '\') })
}
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
    [void][FileCatDependentInput]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 400
    if ([FileCatDependentInput]::GetForegroundWindow() -ne $handle) { [FileCatDependentInput]::ClickTitle($handle); Start-Sleep -Milliseconds 600 }
    if ([FileCatDependentInput]::GetForegroundWindow() -ne $handle) { throw "$($window.Current.Name) is not foreground; refusing keys" }
}
function Focused { try { $AE::FocusedElement.Current.Name } catch { $null } }
function Toggle($element) { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() }
function State($element) {
    [ordered]@{ Name = $element.Current.Name; Enabled = $element.Current.IsEnabled; Help = $element.Current.HelpText
                Toggle = if ($element.GetSupportedPatterns() -contains [System.Windows.Automation.TogglePattern]::Pattern) { Toggle $element } else { $null } }
}
# The pointer rests on the control; NVDA's mouse tracking names it and its tooltip, when it opens, is read too.
function Hover($element, [string] $expected) {
    $r = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $r.Width -lt 6 -or $r.Height -lt 6) { throw "Unreachable pointer target: $($element.Current.Name)" }
    [void][FileCatDependentInput]::SetCursorPos(5, 5); Start-Sleep -Milliseconds 300
    [void](TakeSpeech)
    [void][FileCatDependentInput]::SetCursorPos([int]($r.X + [Math]::Min(30, $r.Width / 2)), [int]($r.Y + $r.Height / 2))
    $heard = @(Hear $expected 6)
    $tips = @(Descendants $AE::RootElement | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.ToolTip' } |
        ForEach-Object { $_.Current.Name })
    [ordered]@{ Control = $element.Current.Name; Heard = $heard; ToolTips = $tips }
}
function SelectTab($dialog, [string] $name) {
    $tab = Await { In $dialog $name 'TabItem' } "the $name tab"
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
}
function OpenSettings($main) {
    Activate $main
    Chord @(0x11, 0xBC)  # Ctrl+Comma
    Await { In $main 'Settings dialog' } 'Settings'
}
function CloseSettings($main) {
    Key 0x1B  # Escape cancels
    Await { -not (In $main 'Settings dialog') } 'Settings to close' 8 | Out-Null
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
$fixture = Join-Path $validation ('DependentFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'; $config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
[presentation]
	reportTooltips = True
[mouse]
	enableMouseTracking = True
"@)
$script:nvdaLog = Join-Path $fixture 'nvda.log'
$script:speechIndex = 0
$script:result = [ordered]@{
    Passed = $false; Stage = 'starting'; Fixture = $fixture; Dpi = $null; Behavior = [ordered]@{}; Columns = [ordered]@{}
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; Error = $null
}
$script:result.ReleasedBefore = @(ReleaseModifiers)
$reader = $script:owned = $null
try {
    $reader = Start-Process -FilePath $nvda -ArgumentList @(
        "--log-file=$script:nvdaLog", '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Await {
        if ($reader.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $script:nvdaLog) -and (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA initialization' 30 | Out-Null
    $script:owned = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    Await { $script:owned.Refresh(); if ($script:owned.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'owned FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($script:owned.MainModule.FileName) -ine $app) { throw 'Unexpected FileCat executable' }
    Start-Sleep 3
    $main = $AE::FromHandle($script:owned.MainWindowHandle)
    $script:result.Dpi = [FileCatDependentInput]::GetDpiForWindow($script:owned.MainWindowHandle)
    $settingsBefore = Get-ChildItem -LiteralPath $data -Recurse -Filter 'settings.json' -ErrorAction SilentlyContinue | Get-FileHash -Algorithm SHA256

    # A. Behavior: the subordinate folder-sizing option.
    $script:result.Stage = 'Behavior'
    $dialog = OpenSettings $main
    SelectTab $dialog 'Behavior'
    $parentName = 'Space computes the size of a marked folder'
    # Windows PowerShell reads a script without a byte-order mark as ANSI: the ellipsis is spelled out.
    $childName = "$([char]0x2026)also on network, removable, and cloud locations"
    $parent = Await { In $dialog $parentName 'CheckBox' } 'the folder sizing option'
    $child = Await { In $dialog $childName 'CheckBox' } 'its subordinate option'
    $b = $script:result.Behavior
    $b.Initial = [ordered]@{ Parent = State $parent; Child = State $child }
    # By keyboard: the parent on, Tab to the child and change it (a draft), back to the parent and turn it off.
    $parent.SetFocus(); Start-Sleep -Milliseconds 500
    [void](TakeSpeech)
    if ((Toggle $parent) -ne 'On') { Key 0x20; Start-Sleep -Milliseconds 300 }
    Key 0x09
    $b.ChildByTab = [ordered]@{ Focused = Focused; Spoken = @(TakeSpeech) }
    if ($b.ChildByTab.Focused -ne $childName) { throw "Tab from the parent reached '$($b.ChildByTab.Focused)'" }
    $draft = if ((Toggle $child) -eq 'On') { 'Off' } else { 'On' }
    Key 0x20
    Await { (Toggle $child) -eq $draft } 'the draft change of the subordinate option' 5 | Out-Null
    Chord @(0x10, 0x09)  # Shift+Tab
    if ((Focused) -ne $parentName) { throw "Shift+Tab reached '$(Focused)'" }
    [void](TakeSpeech)
    Key 0x20
    Await { -not $child.Current.IsEnabled } 'the subordinate option to be unavailable' 5 | Out-Null
    $b.ParentOff = [ordered]@{ Spoken = @(TakeSpeech); Child = State $child }
    if ($b.ParentOff.Child.Toggle -ne $draft) { throw "Turning the parent off reset the draft to $($b.ParentOff.Child.Toggle)" }
    if ($b.ParentOff.Child.Help -ne 'Turn on folder sizing with Space to use this option.') { throw "The unavailable option says '$($b.ParentOff.Child.Help)'" }
    # Tab goes past the unavailable option, as for any unavailable control.
    Key 0x09
    $b.TabPastUnavailable = [ordered]@{ Focused = Focused; Spoken = @(TakeSpeech) }
    # By pointer: hovering it, NVDA names it and reads why it is unavailable.
    $b.Hover = Hover $child 'Turn on folder sizing with Space to use this option.'
    # The parent on again: the draft is back.
    $parent.SetFocus(); Start-Sleep -Milliseconds 400
    Key 0x20
    Await { $child.Current.IsEnabled } 'the subordinate option to return' 5 | Out-Null
    $b.ParentOn = [ordered]@{ Spoken = @(TakeSpeech); Child = State $child }
    if ($b.ParentOn.Child.Toggle -ne $draft) { throw "The returning option lost its draft: $($b.ParentOn.Child.Toggle)" }
    # Cancel: the saved choices stay.
    CloseSettings $main
    $dialog = OpenSettings $main
    SelectTab $dialog 'Behavior'
    $b.AfterCancel = [ordered]@{ Parent = State (In $dialog $parentName 'CheckBox'); Child = State (In $dialog $childName 'CheckBox') }
    if ($b.AfterCancel.Parent.Toggle -ne $b.Initial.Parent.Toggle -or $b.AfterCancel.Child.Toggle -ne $b.Initial.Child.Toggle) {
        throw 'Cancel kept a draft choice'
    }

    # B. Columns: the Name column and the profile limit.
    $script:result.Stage = 'Columns'
    SelectTab $dialog 'Columns'
    $c = $script:result.Columns
    $profiles = Await { In $dialog 'Column profiles' 'List' } 'the profile list'
    $columns = Await { In $dialog 'Columns of the selected profile' 'List' } 'the column list'
    $profileCount = @($profiles.FindAll($TreeScope::Children, [System.Windows.Automation.Condition]::TrueCondition)).Count
    $c.Profiles = $profileCount
    $nameColumn = Await { @($columns.FindAll($TreeScope::Children, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -like 'Name*' })[0] } 'the Name column'
    $nameColumn.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 500
    $removeColumn = In $dialog 'Remove column' 'Button'
    $upColumn = In $dialog 'Up column' 'Button'
    $c.NameSelected = [ordered]@{ Remove = State $removeColumn; Up = State $upColumn }
    if ($removeColumn.Current.IsEnabled -or $removeColumn.Current.HelpText -ne 'The Name column is required.') { throw "Remove column for Name: $($removeColumn.Current.HelpText)" }
    if ($upColumn.Current.IsEnabled -or $upColumn.Current.HelpText -ne 'The first column cannot move up.') { throw "Up column for Name: $($upColumn.Current.HelpText)" }
    $c.RemoveHover = Hover $removeColumn 'The Name column is required.'
    # By keyboard to the limit: New profile pressed until no more can be made.
    $new = In $dialog 'New profile' 'Button'
    $new.SetFocus(); Start-Sleep -Milliseconds 400
    [void](TakeSpeech)
    $presses = 0
    while ($new.Current.IsEnabled -and $presses -lt 12) { Key 0x20; $presses++; Start-Sleep -Milliseconds 250 }
    # The last press leaves the keyboard on the new profile's row, and NVDA hears why New stopped.
    $limitSpeech = @(Hear 'Maximum profiles reached (10).')
    $c.Limit = [ordered]@{
        Presses = $presses
        Profiles = @($profiles.FindAll($TreeScope::Children, [System.Windows.Automation.Condition]::TrueCondition)).Count
        New = State $new; Duplicate = State (In $dialog 'Duplicate profile' 'Button'); Focused = Focused; Spoken = $limitSpeech
    }
    if ($c.Limit.Profiles -ne 10) { throw "The limit stopped at $($c.Limit.Profiles) profiles" }
    if ($c.Limit.Focused -notlike 'Alt+9*') { throw "At the limit the keyboard went to '$($c.Limit.Focused)'" }
    if ($c.Limit.New.Help -ne 'Maximum profiles reached (10).' -or $c.Limit.Duplicate.Help -ne 'Maximum profiles reached (10).') {
        throw "At the limit New says '$($c.Limit.New.Help)' and Duplicate '$($c.Limit.Duplicate.Help)'"
    }
    $c.NewHover = Hover $new 'Maximum profiles reached (10).'
    # Cancel: the saved profiles stay.
    CloseSettings $main
    $dialog = OpenSettings $main
    SelectTab $dialog 'Columns'
    $profiles = Await { In $dialog 'Column profiles' 'List' } 'the profile list again'
    $c.AfterCancel = @($profiles.FindAll($TreeScope::Children, [System.Windows.Automation.Condition]::TrueCondition)).Count
    if ($c.AfterCancel -ne $profileCount) { throw "Cancel kept $($c.AfterCancel) profiles" }
    CloseSettings $main
    $settingsAfter = Get-ChildItem -LiteralPath $data -Recurse -Filter 'settings.json' -ErrorAction SilentlyContinue | Get-FileHash -Algorithm SHA256
    if (@($settingsBefore).Hash -join ',' -ne (@($settingsAfter).Hash -join ',')) { throw 'Cancelled Settings changed settings.json' }
    $script:result.Passed = $true
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message }
finally {
    if ($script:owned -and -not $script:owned.HasExited) { $script:owned.Kill(); $script:owned.WaitForExit(5000) | Out-Null }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(5000) | Out-Null }
    $script:result.ReleasedAfter = @(ReleaseModifiers)
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
