param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# AC-S3-067 with NVDA: About opens from command search. Tab reaches Copy details and both folder buttons with NVDA
# speaking each; Enter on Copy details puts the details on the clipboard and NVDA hears that it did, and the copied
# Settings and Local data paths match the ones About displays. Each folder button opens its folder in a new tab, the
# working tab staying, and NVDA hears where. About stays open through all of it and closes only by its Close button.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatAboutInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
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
    [FileCatAboutInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [FileCatAboutInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) { [FileCatAboutInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    [array]::Reverse($codes)
    foreach ($code in $codes) { [FileCatAboutInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
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
    [void][FileCatAboutInput]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 400
    if ([FileCatAboutInput]::GetForegroundWindow() -ne $handle) { [FileCatAboutInput]::ClickTitle($handle); Start-Sleep -Milliseconds 600 }
    if ([FileCatAboutInput]::GetForegroundWindow() -ne $handle) { throw "$($window.Current.Name) is not foreground; refusing keys" }
}
function Focused { try { $AE::FocusedElement.Current.Name } catch { $null } }
# Tab until the named control has keyboard focus, recording what NVDA says at each stop.
function TabTo([string] $name, [int] $limit = 16) {
    for ($i = 0; $i -lt $limit; $i++) {
        Key 0x09
        Start-Sleep -Milliseconds 450
        $stop = [ordered]@{ Focused = Focused; Spoken = @(TakeSpeech) }
        $script:result.TabStops += $stop
        if ($stop.Focused -eq $name) { return $stop }
    }
    throw "Tab never reached $name"
}
# Speech heard within a few seconds that contains the text (NVDA logs each phrase as a Python literal, so a path's
# backslashes come doubled).
function Hear([string] $text, [int] $seconds = 6) {
    $heard = @()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        $heard += @(TakeSpeech)
        if (@($heard | Where-Object { $_.Replace('\\', '\').Contains($text) }).Count) { return $heard }
        Start-Sleep -Milliseconds 200
    }
    throw "NVDA did not say '$text': $($heard -join ' / ')"
}
# The text a fact's value shows: its UI Automation text, or its name when that is all it exposes.
function Shown($element) {
    $patterns = $element.GetSupportedPatterns()
    if ($patterns -contains [System.Windows.Automation.TextPattern]::Pattern) {
        return $element.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).DocumentRange.GetText(-1)
    }
    if ($patterns -contains [System.Windows.Automation.ValuePattern]::Pattern) {
        return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    }
    $element.Current.Name
}
function TabNames($main) {
    @(Descendants $main | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and
        $_.Current.Name -match ', (Unlocked tab|Navigation lock|Root lock)' } | ForEach-Object { $_.Current.Name })
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
$fixture = Join-Path $validation ('AboutFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'; $config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
[IO.File]::WriteAllText((Join-Path $left 'a.txt'), "about fixture`n")
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
    Passed = $false; Stage = 'starting'; Fixture = $fixture; Dpi = $null; TabStops = @(); Shown = $null; Clipboard = $null
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; Error = $null
}
$reader = $script:owned = $null
$clipboardBefore = try { Get-Clipboard -Raw } catch { $null }
try {
    Set-Clipboard -Value 'FileCat About acceptance: not yet copied'
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
    $script:result.Dpi = [FileCatAboutInput]::GetDpiForWindow($script:owned.MainWindowHandle)
    Activate $main
    $tabsBefore = @(TabNames $main)
    $script:result.TabsBefore = $tabsBefore

    # About from command search, by keyboard.
    $script:result.Stage = 'open About'
    Chord @(0x11, 0x10, 0x50)  # Ctrl+Shift+P
    Await { In $main 'Search commands' 'Edit' } 'command search' | Out-Null
    Chord @(0x11, 0x41)
    TypeText 'about filecat'
    Start-Sleep -Milliseconds 700
    [void](TakeSpeech)
    Key 0x0D
    $dialog = Await { In $main 'About FileCat dialog' } 'About'
    Start-Sleep -Milliseconds 1500
    $script:result.OpenSpeech = @(TakeSpeech)

    # What About displays for the two folders: each value names itself with its label ("Settings: C:\…").
    $script:result.Stage = 'displayed facts'
    $settingsValue = Await { @(Descendants $dialog | Where-Object { $_.Current.Name -like 'Settings: *' })[0] } 'the Settings value'
    $localValue = Await { @(Descendants $dialog | Where-Object { $_.Current.Name -like 'Local data: *' })[0] } 'the Local data value'
    $script:result.Shown = [ordered]@{
        Settings = $settingsValue.Current.Name; LocalData = $localValue.Current.Name
        SettingsText = Shown $settingsValue
        Texts = @(Descendants $dialog | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' } |
            ForEach-Object { $_.Current.Name })
    }
    $expectedSettings = $data; $expectedLocal = Join-Path $data 'local'
    $script:result.Expected = [ordered]@{ Settings = $expectedSettings; LocalData = $expectedLocal }
    if ($settingsValue.Current.Name -ne "Settings: $expectedSettings" -or $localValue.Current.Name -ne "Local data: $expectedLocal") {
        throw "About shows other folders than FileCat uses: $($settingsValue.Current.Name) / $($localValue.Current.Name)"
    }

    # Copy details by keyboard: on the way NVDA names each value with its label; Copy details is heard, About stays,
    # and the clipboard has the displayed paths.
    $script:result.Stage = 'copy details'
    $copyStop = TabTo 'Copy details'
    if (-not (@($copyStop.Spoken) -like '*Copy details*')) { throw "NVDA did not name Copy details: $($copyStop.Spoken -join ' / ')" }
    $spokenStops = @($script:result.TabStops | ForEach-Object { @($_.Spoken) } | ForEach-Object { $_.Replace('\\', '\') })
    foreach ($fact in "Settings: $expectedSettings", "Local data: $expectedLocal", 'Theme: ', 'Profile: ') {
        if (-not @($spokenStops | Where-Object { $_.Contains($fact) }).Count) { throw "Tab did not reach a value NVDA spoke as '$fact'" }
    }
    Key 0x0D
    $script:result.CopySpeech = @(Hear 'Copied details')
    if (-not (In $main 'About FileCat dialog')) { throw 'Copy details closed About' }
    $copied = Get-Clipboard -Raw
    $script:result.Clipboard = $copied
    foreach ($line in $settingsValue.Current.Name, $localValue.Current.Name) {
        if (-not ($copied -split "`r?`n" -contains $line)) { throw "The copied details lack the displayed '$line'" }
    }
    if ($copied -notmatch '^FileCat ') { throw 'The copied details do not start with the version' }

    # Each folder button opens a new tab there; the working tab stays; About stays.
    foreach ($button in @(@{ Name = 'Open settings folder'; Path = $expectedSettings }, @{ Name = 'Open local data folder'; Path = $expectedLocal })) {
        $script:result.Stage = $button.Name
        $stop = TabTo $button.Name
        if (-not (@($stop.Spoken) -like "*$($button.Name)*")) { throw "NVDA did not name $($button.Name): $($stop.Spoken -join ' / ')" }
        $help = (In $dialog $button.Name 'Button').Current.HelpText
        if ($help -notlike "*$($button.Path)*") { throw "$($button.Name) does not say where it goes: $help" }
        Key 0x0D
        $script:result."$($button.Name) speech" = @(Hear "Opened $($button.Path) in a new tab")
        if (-not (In $main 'About FileCat dialog')) { throw "$($button.Name) closed About" }
        Await { @(TabNames $main | Where-Object { $_.EndsWith(", $($button.Path)") }).Count } "a tab at $($button.Path)" 8 | Out-Null
    }
    $tabsAfter = @(TabNames $main)
    $script:result.TabsAfter = $tabsAfter
    foreach ($tab in $tabsBefore) { if ($tabsAfter -notcontains $tab) { throw "The working tab changed: $tab" } }

    # Close, by keyboard, is what dismisses it.
    $script:result.Stage = 'close'
    $closeStop = TabTo 'Close'
    Key 0x0D
    Await { -not (In $main 'About FileCat dialog') } 'About to close' 8 | Out-Null
    $script:result.CloseSpeech = @($closeStop.Spoken) + @(TakeSpeech)
    $script:result.Passed = $true
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message }
finally {
    if ($script:owned -and -not $script:owned.HasExited) { $script:owned.Kill(); $script:owned.WaitForExit(5000) | Out-Null }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(5000) | Out-Null }
    if ($null -ne $clipboardBefore) { try { Set-Clipboard -Value $clipboardBefore } catch { } }
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
