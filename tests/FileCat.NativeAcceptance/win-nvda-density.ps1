param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# AC-S3-059 with NVDA: Settings switches to Comfortable density and a 24-unit content size by keyboard-free UI
# Automation; then the viewer and the hex editor, each at its minimum size and maximized, must keep their modes,
# Find, More, size and (editor) Save/Undo/Redo controls on screen inside the window, open Find and More by pointer,
# and reach every one of them by Tab with NVDA speaking its name.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatDensityInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
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
function Windows {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $script:owned.Id)
    @($AE::RootElement.FindAll($TreeScope::Children, $condition))
}
function WindowNamed([string] $pattern) { @(Windows | Where-Object { $_.Current.Name -like $pattern })[0] }
function In($root, [string] $name, [string] $type = $null) {
    @(Descendants $root | Where-Object {
        $_.Current.Name -eq $name -and (-not $type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$type") })[0]
}
function Click($element) {
    $r = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $r.Width -lt 6 -or $r.Height -lt 6) { throw "Unreachable pointer target: $($element.Current.Name)" }
    [void][FileCatDensityInput]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 100
    [FileCatDensityInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [FileCatDensityInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 500
}
function Key([byte] $code, [bool] $extended = $false) {
    $flags = if ($extended) { 1 } else { 0 }
    [FileCatDensityInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [FileCatDensityInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) { [FileCatDensityInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    [array]::Reverse($codes)
    foreach ($code in $codes) { [FileCatDensityInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    Start-Sleep -Milliseconds 300
}
function TypeText([string] $letters) {
    foreach ($c in $letters.ToUpperInvariant().ToCharArray()) { if ($c -eq ' ') { Key 0x20 } else { Key ([byte][char]$c) } }
}
function TakeSpeech {
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    @($fresh | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
}
function Activate($window) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][FileCatDensityInput]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 400
    if ([FileCatDensityInput]::GetForegroundWindow() -ne $handle) { [FileCatDensityInput]::ClickTitle($handle); Start-Sleep -Milliseconds 600 }
    if ([FileCatDensityInput]::GetForegroundWindow() -ne $handle) { throw "$($window.Current.Name) is not foreground; refusing keys" }
}

# Escape from the content closes a viewer (and an unchanged editor); UI Automation's Close otherwise.
function CloseWindow($window, [string] $pattern) {
    Activate $window
    Key 0x1B
    $script:closedBy = 'Escape'
    try { Await { -not (WindowNamed $pattern) } 'the window to close' 5 | Out-Null; return } catch { }
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $script:closedBy = 'WindowPattern.Close'
    Await { -not (WindowNamed $pattern) } 'the window to close' 10 | Out-Null
}

# One window at one size: every control on screen inside the window, Find and More open by pointer, and every
# control reached by Tab with NVDA speaking its name.
function CheckWindow($window, [string] $size, [string[]] $controls, [string] $findToggle, [string] $more, [byte] $enterKey = 0,
    [string[]] $notTabStops = @()) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    if ($size -eq 'minimum') {
        [void][FileCatDensityInput]::ShowWindow($handle, 9)  # restore
        Start-Sleep -Milliseconds 500
        [void][FileCatDensityInput]::SetWindowPos($handle, [IntPtr]::Zero, 40, 40, 200, 200, 0x0014)  # no z-order, no activate
    } else {
        [void][FileCatDensityInput]::ShowWindow($handle, 3)  # maximize
    }
    Start-Sleep 2
    Activate $window
    $frame = $window.Current.BoundingRectangle
    $entry = [ordered]@{ Window = $window.Current.Name; Size = $size; Frame = $frame.ToString(); Controls = @() }
    foreach ($name in $controls) {
        $control = In $window $name
        if (-not $control) { throw "$size $($window.Current.Name): no '$name'" }
        $r = $control.Current.BoundingRectangle
        $inside = -not $control.Current.IsOffscreen -and $r.Width -ge 6 -and $r.Height -ge 6 -and
            $r.Left -ge $frame.Left - 1 -and $r.Top -ge $frame.Top - 1 -and $r.Right -le $frame.Right + 1 -and $r.Bottom -le $frame.Bottom + 1
        $entry.Controls += [ordered]@{ Name = $name; Bounds = $r.ToString(); Inside = $inside }
        if (-not $inside) { throw "$size $($window.Current.Name): '$name' is not on screen inside the window ($r in $frame)" }
    }
    # Pointer: Find opens its field (closing it first if an earlier pass left it open); More opens its menu.
    $field = In $window 'Find in file' 'Edit'
    if ($field -and -not $field.Current.IsOffscreen) {
        Click (In $window $findToggle)
        Await { $f = In $window 'Find in file' 'Edit'; if (-not $f -or $f.Current.IsOffscreen) { $true } } "$size Find field to hide" 8 | Out-Null
    }
    Click (In $window $findToggle)
    Await { $f = In $window 'Find in file' 'Edit'; if ($f -and -not $f.Current.IsOffscreen) { $true } } "$size Find field" 8 | Out-Null
    Click (In $window $more)
    $entry.MoreItems = Await {
        $items = @(Descendants $window | Where-Object {
            $_.Current.ControlType.ProgrammaticName -eq 'ControlType.MenuItem' -and -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name })
        if ($items.Count) { , $items }
    } "$size More menu" 8
    Key 0x1B
    Start-Sleep -Milliseconds 400
    # Keyboard and NVDA: Tab through the window (after the key that leaves a grid keeping Tab, if any); every control
    # must be spoken.
    [void](TakeSpeech)
    $spoken = @()
    if ($enterKey) {
        # From the grid: the pointer steps above left the keyboard in Find or More.
        (In $window 'Hex view').SetFocus()
        Start-Sleep -Milliseconds 400
        [void](TakeSpeech)
        Key $enterKey
        Start-Sleep -Milliseconds 400
        foreach ($line in @(TakeSpeech)) { if ($line -match "^\[LangChangeCommand \('[^']*'\), '([^']*)'") { $spoken += $Matches[1] } }
    }
    $tabbed = @($controls | Where-Object { $notTabStops -notcontains $_ })
    for ($i = 0; $i -lt 45 -and @($tabbed | Where-Object { $spoken -notcontains $_ }).Count; $i++) {
        Key 0x09
        Start-Sleep -Milliseconds 250
        foreach ($line in @(TakeSpeech)) {
            if ($line -match "^\[LangChangeCommand \('[^']*'\), '([^']*)'") { $spoken += $Matches[1] }
        }
    }
    $entry.SpokenByTab = @($spoken | Select-Object -Unique)
    $missing = @($tabbed | Where-Object { $spoken -notcontains $_ })
    if ($missing.Count) {
        $script:result.FailedTabSpeech = @($spoken)
        throw "$size $($window.Current.Name): Tab with NVDA never reached $($missing -join ', ') (heard: $((@($spoken) | Select-Object -Unique) -join ', '))"
    }
    $script:result.Checks += $entry
}

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath); $nvda = [IO.Path]::GetFullPath($NvdaPath); $report = [IO.Path]::GetFullPath($ReportPath)
if (-not $app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    -not $nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or (Get-Process -Name nvda -ErrorAction SilentlyContinue) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an isolated Git app, signed portable NVDA, a fresh validation report and no competing reader or FileCat'
}
$fixture = Join-Path $validation ('DensityFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'; $config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
$text = (1..60 | ForEach-Object { "Line $_ of the density fixture, long enough to wrap in a narrow viewer window." }) -join "`n"
[IO.File]::WriteAllText((Join-Path $left 'a.txt'), $text + "`n")
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $left 'a.txt') -Algorithm SHA256).Hash
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
    Passed = $false; Stage = 'starting'; Fixture = $fixture; Dpi = $null; Checks = @(); Settings = $null
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; Error = $null
}
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
    $script:result.Dpi = [FileCatDensityInput]::GetDpiForWindow($script:owned.MainWindowHandle)
    Activate $main

    # Settings: Comfortable density and a 24-unit content size.
    $script:result.Stage = 'Settings'
    Chord @(0x11, 0xBC)  # Ctrl+Comma
    $dialog = Await { In $main 'Settings dialog' } 'Settings'
    $appearance = Await { In $main 'Appearance' 'TabItem' } 'the Appearance tab'
    $how = [ordered]@{}
    if ($appearance.GetSupportedPatterns() -contains [System.Windows.Automation.SelectionItemPattern]::Pattern) {
        $appearance.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $how.Tab = 'select'
    } else { Click $appearance; $how.Tab = 'click' }
    Start-Sleep -Milliseconds 700
    $density = Await { In $main 'Interface density' 'ComboBox' } 'Interface density'
    Click $density
    $comfortable = Await { @(Descendants $AE::RootElement | Where-Object { $_.Current.Name -eq 'Comfortable' -and
        $_.Current.ControlType.ProgrammaticName -eq 'ControlType.ListItem' -and -not $_.Current.IsOffscreen })[0] } 'the Comfortable choice'
    Click $comfortable
    $how.Density = 'click'
    Start-Sleep -Milliseconds 500
    $size = Await { In $main 'Text, hex and comparison size' } 'the content size'
    $edit = @(@($size) + @(Descendants $size) | Where-Object {
        $_.GetSupportedPatterns() -contains [System.Windows.Automation.ValuePattern]::Pattern })[0]
    if ($size.GetSupportedPatterns() -contains [System.Windows.Automation.RangeValuePattern]::Pattern) {
        $size.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(24); $how.Size = 'range'
    } elseif ($edit) {
        $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('24'); $how.Size = 'value'
        Click $edit; Key 0x09
    } else {
        # By keyboard, as a user would: Tab from the density box until NVDA announces the size, then Up arrows, one
        # unit each, to the 32 maximum and Down to 24.
        [void](TakeSpeech)
        $reached = $false
        for ($i = 0; $i -lt 12 -and -not $reached; $i++) {
            Key 0x09
            Start-Sleep -Milliseconds 400
            foreach ($line in @(TakeSpeech)) { if ($line -like "*'Text, hex and comparison size'*") { $reached = $true } }
        }
        if (-not $reached) { throw 'Tab never reached the content size' }
        for ($i = 0; $i -lt 23; $i++) { Key 0x26 $true }
        for ($i = 0; $i -lt 8; $i++) { Key 0x28 $true }
        Key 0x09
        $how.Size = 'up arrows'
    }
    $script:result.SettingsMethod = $how
    Start-Sleep -Milliseconds 400
    Click (In $main 'OK' 'Button')
    Await { -not (In $main 'Settings dialog') } 'Settings to close' 10 | Out-Null
    $saved = Get-ChildItem -LiteralPath $data -Recurse -Filter 'settings*.json' | Select-Object -First 1
    $json = if ($saved) { Get-Content -Raw -LiteralPath $saved.FullName | ConvertFrom-Json } else { $null }
    $script:result.Settings = [ordered]@{ File = $saved.FullName; RowDensity = $json.RowDensity; ContentFontSize = $json.ContentFontSize }
    if ($json.RowDensity -ne 'Comfortable' -or [double]$json.ContentFontSize -lt 24) {
        throw "Settings saved density '$($json.RowDensity)' and content size '$($json.ContentFontSize)'"
    }

    # The viewer on a.txt.
    $script:result.Stage = 'viewer'
    Activate $main
    Key 0x28 $true  # Down to a.txt
    Key 0x72        # F3
    $viewer = Await { WindowNamed 'a.txt*Viewer*' } 'the viewer'
    $sizeLabel = try { Await { In $viewer 'Text and hex size' } 'the viewer size label' 4 } catch { $null }
    $script:result.ViewerContentSize = if ($sizeLabel) { $sizeLabel.Current.Name } else { 'label not exposed' }
    $script:result.ViewerTexts = @(Descendants $viewer | Where-Object { $_.Current.Name -like '*size*' } | ForEach-Object { $_.Current.Name })
    $viewerControls = @('Text', 'Hex', 'Info', 'Show or hide viewer Find bar', 'More viewer actions', 'Decrease text and hex size', 'Increase text and hex size')
    foreach ($size in 'minimum', 'maximized') { CheckWindow $viewer $size $viewerControls 'Show or hide viewer Find bar' 'More viewer actions' }
    # Modes by pointer and keyboard, read from the mode buttons' toggle states.
    function Toggled($window, [string] $name) {
        $button = In $window $name
        $button -and $button.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On'
    }
    Click (In $viewer 'Hex')
    Await { Toggled $viewer 'Hex' } 'Hex mode by pointer' 8 | Out-Null
    Activate $viewer
    Key 0x73  # F4: back to text
    Await { Toggled $viewer 'Text' } 'Text mode by F4' 8 | Out-Null
    $script:result.Modes = 'Hex by pointer, Text by F4'
    CloseWindow $viewer 'a.txt*Viewer*'
    $script:result.ViewerClosed = $script:closedBy

    # The hex editor on a.txt.
    $script:result.Stage = 'hex editor'
    Activate $main
    Chord @(0x11, 0x10, 0x50)
    Await { In $main 'Search commands' 'Edit' } 'command search' | Out-Null
    Chord @(0x11, 0x41)
    TypeText 'edit file bytes in hex'
    Start-Sleep -Milliseconds 600
    Key 0x0D
    $editor = Await { WindowNamed 'a.txt*Hex Editor*' } 'the hex editor'
    $editorControls = @('Save', 'Undo', 'Redo', 'Show or hide hex editor Find bar', 'More hex editor actions', 'Decrease editor content size', 'Increase editor content size')
    # One unsaved byte so Save and Undo are enabled (disabled buttons are not Tab stops); undone before closing.
    Activate $editor
    (In $editor 'Hex view').SetFocus()
    Start-Sleep -Milliseconds 300
    Key 0x34; Key 0x31  # "41": the first byte becomes 'A'
    Await { (In $editor 'Save' 'Button').Current.IsEnabled } 'Save to be enabled after an edit' 8 | Out-Null
    $script:result.EditorOpenSpeech = @(TakeSpeech)
    # Tab switches the grid's hex and text columns; F6 is the editor's documented way to its toolbar.
    foreach ($size in 'minimum', 'maximized') {
        Activate $editor
        (In $editor 'Hex view').SetFocus()
        Start-Sleep -Milliseconds 300
        CheckWindow $editor $size $editorControls 'Show or hide hex editor Find bar' 'More hex editor actions' 0x75 @('Redo')
    }
    # Undo the edit by keyboard; Redo becomes available and Save no longer has anything to save.
    Activate $editor
    (In $editor 'Hex view').SetFocus()
    Start-Sleep -Milliseconds 300
    Chord @(0x11, 0x5A)
    Await { (In $editor 'Redo' 'Button').Current.IsEnabled } 'Redo after Ctrl+Z' 8 | Out-Null
    $script:result.EditorUndo = [ordered]@{ Save = (In $editor 'Save' 'Button').Current.IsEnabled; Redo = $true }
    CloseWindow $editor 'a.txt*Hex Editor*'
    $script:result.EditorClosed = $script:closedBy
    if ((Get-FileHash -LiteralPath (Join-Path $left 'a.txt') -Algorithm SHA256).Hash -ne $sourceHash) { throw 'The journey changed a.txt' }
    $script:result.Passed = $true
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message }
finally {
    if ($script:owned -and -not $script:owned.HasExited) { $script:owned.Kill(); $script:owned.WaitForExit(5000) | Out-Null }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(5000) | Out-Null }
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
