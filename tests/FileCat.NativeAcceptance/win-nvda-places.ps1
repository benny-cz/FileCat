param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# AC-S3-058 at 150% (144 DPI) with NVDA: four and then six panels, each panel's Places row with every place reachable
# through the row or its More menu; a place pinned and unpinned by keyboard from More's "Pin places to bar" list, the
# pinned button appearing in and leaving the row; and Help from command search by keyboard: a query without a match
# names Keyboard reference and its key, F1 opens it, Escape returns to the search with the query. NVDA's speech log
# records the spoken names. Set 150% first with win-ax-display-scale.ps1.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatPlacesInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    public static void ClickTitle(IntPtr h) {
        RECT r; GetWindowRect(h, out r);
        double scale = GetDpiForWindow(h) / 96.0;
        SetCursorPos(Math.Max(r.Left + (int)(40 * scale), r.Right - (int)(300 * scale)), r.Top + (int)(12 * scale));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@
$TreeScope = [System.Windows.Automation.TreeScope]
$Root = [System.Windows.Automation.AutomationElement]::RootElement
function Descendants($element) {
    @($element.FindAll($TreeScope::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
}
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
function OwnedElements([switch] $PopupsOnly) {
    # The main window and its popups (menus are top-level windows of the same process).
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:owned.Id)
    foreach ($window in @($Root.FindAll($TreeScope::Children, $condition))) {
        if ($PopupsOnly -and $window.Current.NativeWindowHandle -eq [int]$script:owned.MainWindowHandle) { continue }
        $window; Descendants $window
    }
}
function Named([string] $name, [string] $type = $null) {
    @(OwnedElements | Where-Object {
        $_.Current.Name -eq $name -and (-not $type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$type") -and
            -not $_.Current.IsOffscreen
    })
}
function Click($element) {
    $r = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $r.Width -lt 8 -or $r.Height -lt 8) { throw "Unreachable pointer target: $($element.Current.Name)" }
    [void][FileCatPlacesInput]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 100
    [FileCatPlacesInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [FileCatPlacesInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 400
}
function Key([byte] $code, [bool] $extended = $false) {
    $flags = if ($extended) { 1 } else { 0 }
    [FileCatPlacesInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [FileCatPlacesInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 220
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) { [FileCatPlacesInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    [array]::Reverse($codes)
    foreach ($code in $codes) { [FileCatPlacesInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    Start-Sleep -Milliseconds 250
}
function TypeText([string] $letters) { foreach ($c in $letters.ToUpperInvariant().ToCharArray()) { Key ([byte][char]$c) } }
function TakeSpeech {
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    return @($fresh | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
}
function Step([string] $label, [scriptblock] $check) {
    $script:result.Stage = $label
    $value = & $check
    Start-Sleep -Milliseconds 700
    $script:result.Steps += [ordered]@{ Step = $label; Value = $value; Speech = @(TakeSpeech) }
    $value
}
function RunCommand([string] $query) {
    Chord @(0x11, 0x10, 0x50)
    Await { Named 'Search commands' 'Edit' | Select-Object -First 1 } 'command search' | Out-Null
    Chord @(0x11, 0x41)  # Ctrl+A: replace an earlier query
    TypeText $query
    Start-Sleep -Milliseconds 500
    Key 0x0D
    Start-Sleep -Milliseconds 900
}
function MoreButtons { @(Named 'More places' 'Button') }
# The Places row of the panel whose More button this is: the shown buttons on its line, after the More button of
# the panel to its left (if any) and before its own. Avalonia gives the row no accessible container of its own.
function RowPlaces($more) {
    $m = $more.Current.BoundingRectangle
    $line = $m.Y + $m.Height / 2
    $leftEdge = (@(MoreButtons | ForEach-Object { $_.Current.BoundingRectangle } |
        Where-Object { [Math]::Abs($_.Y + $_.Height / 2 - $line) -le 6 -and $_.Right -le $m.Left + 1 } |
        ForEach-Object { $_.Right }) + @(-1e9) | Measure-Object -Maximum).Maximum
    @(OwnedElements | Where-Object {
        $r = $_.Current.BoundingRectangle
        $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $_.Current.Name -ne 'More places' -and
            -not $_.Current.IsOffscreen -and $r.Width -ge 8 -and $r.Height -ge 8 -and
            [Math]::Abs($r.Y + $r.Height / 2 - $line) -le 6 -and $r.Right -le $m.Left + 1 -and $r.Left -ge $leftEdge
    } | Sort-Object { $_.Current.BoundingRectangle.X } | ForEach-Object { $_.Current.Name })
}
function OpenMore($more) {
    Click $more
    Await { Named 'Pin places to bar' 'MenuItem' | Select-Object -First 1 } 'the More places menu' | Out-Null
}
# The items of the open menus, in order: menus are overlays in the main window, so the menu bar's own items
# (recorded before any menu opened) are left out.
function MenuItems {
    @(OwnedElements | Where-Object {
        $_.Current.ControlType.ProgrammaticName -eq 'ControlType.MenuItem' -and -not $_.Current.IsOffscreen } |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -notin $script:menuBar })
}
function CloseMenus { Key 0x1B; Key 0x1B; Start-Sleep -Milliseconds 300 }
# Down through the open menu until NVDA has just spoken the item named so. Counting keys is not enough: a list
# reopens on the item used last, and UI Automation's focus does not follow Avalonia's menu highlight.
function FocusMenuItem([string] $name) {
    $script:spokenItem = $null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 350
        foreach ($entry in @(TakeSpeech)) {
            if ($entry -match "^\[LangChangeCommand \('[^']*'\), '([^']*)'") { $script:spokenItem = $Matches[1] }
        }
        if ($script:spokenItem -eq $name) { return }
        Key 0x28 $true
    }
    throw "NVDA never spoke $name in the menu (last: $script:spokenItem)"
}

# Opens More and, by keyboard, its "Pin places to bar" list: the first-level items and the list's places.
function OpenPinList($more) {
    [void](TakeSpeech)
    OpenMore $more
    Start-Sleep -Milliseconds 300
    $items = @(MenuItems)
    if ($items -notcontains 'Pin places to bar') { throw 'More has no Pin places to bar' }
    FocusMenuItem 'Pin places to bar'
    Key 0x27 $true  # Right: into the pin list
    $list = Await {
        $all = @(MenuItems)
        if ($all.Count -gt $items.Count) { , @($all | Select-Object -Skip $items.Count) }
    } 'the pin list'
    [ordered]@{ Items = $items; List = @($list) }
}

function PinList($more) {
    $opened = OpenPinList $more
    CloseMenus
    [ordered]@{ Overflow = @($opened.Items | Where-Object { $_ -notin @('Pin places to bar', 'Clear pinned Places') })
                All = $opened.List }
}

# A place's check state in the pin list (On or Off), read without changing it.
function PinState($more, [string] $place) {
    OpenPinList $more | Out-Null
    $item = @(OwnedElements | Where-Object {
        $_.Current.ControlType.ProgrammaticName -eq 'ControlType.MenuItem' -and $_.Current.Name -eq $place -and -not $_.Current.IsOffscreen
    } | Where-Object { $_.GetSupportedPatterns() -contains [System.Windows.Automation.TogglePattern]::Pattern }) | Select-Object -Last 1
    $state = if ($item) { $item.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() } else { 'absent' }
    CloseMenus
    $state
}

# Toggles a place in the pin list by keyboard (Down to it, Enter).
function TogglePin($more, [string] $place) {
    $opened = OpenPinList $more
    if ($opened.List -notcontains $place) { throw "$place is not in the pin list" }
    FocusMenuItem $place
    Key 0x0D
    Start-Sleep -Milliseconds 900
}

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    -not $nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or (Get-Process -Name nvda -ErrorAction SilentlyContinue) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an isolated Git app, signed portable NVDA, a fresh validation report and no competing reader or FileCat'
}
$fixture = Join-Path $validation ('PlacesFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'
$config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
Set-Content -LiteralPath (Join-Path $left 'a.txt') 'places fixture'
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
    Passed = $false; Stage = 'starting'; Fixture = $fixture; Dpi = $null; Steps = @()
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; Error = $null
}
$reader = $script:owned = $null
try {
    $reader = Start-Process -FilePath $nvda -ArgumentList @(
        "--log-file=$script:nvdaLog", '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Await {
        if ($reader.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $script:nvdaLog) -and
            (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA initialization' 30 | Out-Null
    $script:owned = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    Await { $script:owned.Refresh(); if ($script:owned.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'owned FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($script:owned.MainModule.FileName) -ine $app) { throw 'Unexpected FileCat executable' }
    Start-Sleep 3
    $script:result.Dpi = [FileCatPlacesInput]::GetDpiForWindow($script:owned.MainWindowHandle)
    if ($script:result.Dpi -ne 144) { throw "FileCat runs at $($script:result.Dpi) DPI, not 150% (144)" }
    [FileCatPlacesInput]::ClickTitle($script:owned.MainWindowHandle)
    Start-Sleep -Milliseconds 700
    if ([FileCatPlacesInput]::GetForegroundWindow() -ne $script:owned.MainWindowHandle) { throw 'FileCat is not foreground; refusing keys' }
    [void](TakeSpeech)
    $script:menuBar = @()
    $script:menuBar = @(MenuItems)
    $script:result.MenuBar = $script:menuBar

    # Maximized, as six panels at 150% need: each panel keeps a minimum size.
    $mainElement = [System.Windows.Automation.AutomationElement]::FromHandle($script:owned.MainWindowHandle)
    $mainElement.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState(
        [System.Windows.Automation.WindowVisualState]::Maximized)
    Start-Sleep 2
    $script:result.MaximizedBounds = $mainElement.Current.BoundingRectangle.ToString()

    # Four panels.
    RunCommand 'add panel to the right'
    RunCommand 'add panel to the right'
    $four = Step 'four panels' { (MoreButtons).Count }
    if ($four -ne 4) { throw "Expected 4 Places rows, found $four" }
    $more = (MoreButtons)[0]
    $lists = Step 'four panels: every place in the row or More' {
        $row = @(RowPlaces $more); $pins = PinList $more
        [ordered]@{ Row = $row; Overflow = $pins.Overflow; All = $pins.All
                    Missing = @($pins.All | Where-Object { $_ -notin $row -and $_ -notin $pins.Overflow }) }
    }
    if ($lists.Missing.Count) { throw "Places reachable neither in the row nor in More: $($lists.Missing -join ', ')" }
    $candidate = @($lists.All | Where-Object { $_ -notin $lists.Row } | Select-Object -First 1)[0]
    if (-not $candidate) { throw 'Every place is already in the row; nothing to pin' }
    $script:result.Pinned = $candidate

    # Pin by keyboard: open More, go to "Pin places to bar", into its list, down to the place, Enter.
    $before = @(RowPlaces $more)
    Step "pin $candidate by keyboard" {
        TogglePin $more $candidate
        Await { if ($candidate -in @(RowPlaces (MoreButtons)[0])) { , @(RowPlaces (MoreButtons)[0]) } } "$candidate in the Places row" 10
    } | Out-Null
    Step "$candidate is checked in the pin list" { PinState (MoreButtons)[0] $candidate } | ForEach-Object {
        if ($_ -ne 'On') { throw "$candidate shows $_ in the pin list after pinning" } }
    # The row fills spare room with other places, so unpinning restores the row as it was before.
    Step "unpin $candidate by keyboard" {
        TogglePin (MoreButtons)[0] $candidate
        try { Await { $now = @(RowPlaces (MoreButtons)[0]); if (-not (Compare-Object $now $before)) { , $now } } 'the row as before the pin' 10 }
        catch {
            $script:result.RowAfterUnpin = @(RowPlaces (MoreButtons)[0]); $script:result.RowBeforePin = $before
            $script:result.UnpinSpeech = @(TakeSpeech); $script:result.OpenMenuItems = @(MenuItems)
            throw
        }
    } | Out-Null
    Step "$candidate is unchecked in the pin list" { PinState (MoreButtons)[0] $candidate } | ForEach-Object {
        if ($_ -ne 'Off') { throw "$candidate shows $_ in the pin list after unpinning" } }

    # Help from command search by keyboard.
    $hint = Step 'a query without a match names Keyboard reference' {
        Chord @(0x11, 0x10, 0x50)
        Await { Named 'Search commands' 'Edit' | Select-Object -First 1 } 'command search' | Out-Null
        TypeText 'zqxjw'
        Await { @(OwnedElements | Where-Object { $_.Current.Name -like 'No command matches*Keyboard reference (F1)*' }).Count -gt 0 } 'the no-match hint' 10
    }
    Step 'F1 opens Keyboard reference' {
        # The search's own button shows the words too; the reference adds its own title.
        $before = (Named 'Keyboard reference').Count
        Key 0x70
        Await { (Named 'Keyboard reference').Count -gt $before } 'Keyboard reference' 10
    } | Out-Null
    $back = Step 'Escape returns to the search with its query' {
        Key 0x1B
        Await {
            $search = Named 'Search commands' 'Edit' | Select-Object -First 1
            if ($search -and $search.Current.HasKeyboardFocus) { $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
        } 'the focused search' 10
    }
    if ($back -ne 'zqxjw') { throw "The search came back with '$back'" }
    Key 0x1B

    # Six panels: one more below panel 1 and one below panel 3; every panel's places stay reachable.
    foreach ($number in 1, 3) {
        $list = @(OwnedElements | Where-Object {
            $_.Current.ControlType.ProgrammaticName -eq 'ControlType.List' -and $_.Current.Name -like "Panel $number,*" })[0]
        if (-not $list) { throw "No file list for panel $number" }
        $list.SetFocus()
        Start-Sleep -Milliseconds 600
        RunCommand 'add panel below'
    }
    $six = Step 'six panels' { (MoreButtons).Count }
    if ($six -ne 6) {
        $script:result.Notices = @(OwnedElements | Where-Object { $_.Current.Name -like '*no room for another panel*' } |
            ForEach-Object { $_.Current.Name })
        throw "Expected 6 Places rows, found $six"
    }
    $narrow = @(MoreButtons | Sort-Object { $_.Current.BoundingRectangle.Width })[0]
    $sixLists = Step 'six panels: every place in the row or More' {
        $row = @(RowPlaces $narrow); $pins = PinList $narrow
        [ordered]@{ Row = $row; Overflow = $pins.Overflow; All = $pins.All
                    Missing = @($pins.All | Where-Object { $_ -notin $row -and $_ -notin $pins.Overflow }) }
    }
    if ($sixLists.Missing.Count) { throw "Six panels: places reachable neither in the row nor in More: $($sixLists.Missing -join ', ')" }
    $script:result.Speech = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8 | Where-Object { $_ -match 'Speaking \[' } |
        ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
    $spoken = $script:result.Speech -join ' '
    foreach ($name in @('Pin places to bar', $candidate, 'Keyboard reference')) {
        if ($spoken -notmatch [regex]::Escape($name)) { throw "NVDA did not speak '$name'" }
    }
    $script:result.Passed = $true
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message }
finally {
    if ($script:owned -and -not $script:owned.HasExited) { $script:owned.Kill(); $script:owned.WaitForExit(5000) | Out-Null }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(5000) | Out-Null }
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
