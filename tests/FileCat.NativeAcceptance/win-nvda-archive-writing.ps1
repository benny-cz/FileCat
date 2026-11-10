param(
    [Parameter(Mandatory)][string] $HostPath,
    [Parameter(Mandatory)][string] $WorkerPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# Archive-writing accessibility (plan section 11.8, AT16; UX_GAP_AUDIT C) with NVDA, through the never-shipped qualification host
# (the real interface with native archive backends composed explicitly; AppContainer workers from the published app):
#   1. Pack with two marked files: the Create archive dialog, its path, format and compression controls are spoken by name;
#      choosing "gzip (compress one file)" speaks the polite problem text that offers TAR + gzip; TAR + gzip packs and the
#      operation's archive phases are announced.
#   2. Pack with one file as "gzip (compress one file)": the generated name keeps the whole file name (a.txt.gz).
#   3. Inside a.txt.gz, F7, F2 and F8 are refused and NVDA hears why.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatArchiveInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static void ClickTitle(IntPtr h) {
        RECT r; GetWindowRect(h, out r);
        double scale = GetDpiForWindow(h) / 96.0;
        SetCursorPos(Math.Max(r.Left + (int)(40 * scale), r.Right - (int)(300 * scale)), r.Top + (int)(12 * scale));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@
[void][FileCatArchiveInput]::SetThreadDpiAwarenessContext([IntPtr](-4))
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
    [FileCatArchiveInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [FileCatArchiveInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) { [FileCatArchiveInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    $reversed = @($codes); [array]::Reverse($reversed)
    foreach ($code in $reversed) { [FileCatArchiveInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    Start-Sleep -Milliseconds 300
}
function TakeSpeech {
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    @($fresh | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim().Replace('\\', '\') })
}
function Hear([string] $text, [int] $seconds = 8) {
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
    [void][FileCatArchiveInput]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 400
    if ([FileCatArchiveInput]::GetForegroundWindow() -ne $handle) { [FileCatArchiveInput]::ClickTitle($handle); Start-Sleep -Milliseconds 600 }
    if ([FileCatArchiveInput]::GetForegroundWindow() -ne $handle) { throw "$($window.Current.Name) is not foreground; refusing keys" }
}
function Focused { try { $AE::FocusedElement.Current.Name } catch { $null } }
function ReleaseModifiers {
    $released = @()
    foreach ($k in 0xA4, 0xA5, 0x12, 0xA2, 0xA3, 0x11, 0xA0, 0xA1, 0x10, 0x5B, 0x5C) {
        if ([FileCatArchiveInput]::GetAsyncKeyState($k) -band 0x8000) {
            [FileCatArchiveInput]::keybd_event([byte]$k, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50; $released += $k
        }
    }
    $released
}
function FocusRow([string] $name) {
    Key 0x24 $true  # Home
    for ($i = 0; $i -lt 12; $i++) {
        $at = Focused
        if ($at -eq $name -or $at -like "$name,*") { return }
        Key 0x28 $true
    }
    throw "The cursor never reached $name (at '$(Focused)')"
}
function Texts($root) { @(Descendants $root | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' } | ForEach-Object { $_.Current.Name }) }
# Chooses a format in the dialog's "Archive format" box through UI Automation and returns what NVDA said meanwhile.
function ChooseFormat($dialog, [string] $label) {
    $box = Await { In $dialog 'Archive format' 'ComboBox' } 'the Archive format box'
    $box.SetFocus(); Start-Sleep -Milliseconds 300
    $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 500
    $item = Await { @(Descendants $AE::RootElement | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.ListItem' -and $_.Current.Name -eq $label })[0] } "the format '$label'"
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400
    try { $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
    Start-Sleep -Milliseconds 900
}
function PathValue($dialog) {
    (Await { In $dialog 'Archive path' 'Edit' } 'the Archive path box').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

$hostExe = [IO.Path]::GetFullPath($HostPath); $worker = [IO.Path]::GetFullPath($WorkerPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath); $report = [IO.Path]::GetFullPath($ReportPath)
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation.TrimEnd('\') + '\'
if (-not $hostExe.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($hostExe) -ine 'FileCat.ArchiveQualificationHost.exe' -or
    -not $worker.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($worker) -ine 'FileCat.exe' -or
    -not $nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or (Get-Process -Name nvda -ErrorAction SilentlyContinue) -or
    (Get-Process -Name FileCat, FileCat.ArchiveQualificationHost -ErrorAction SilentlyContinue)) {
    throw 'Use an isolated qualification host and published worker, signed portable NVDA, a fresh validation report and no competing reader or FileCat'
}
$fixture = Join-Path $validation ('ArchiveWritingFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'; $config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
[IO.File]::WriteAllText((Join-Path $left 'a.txt'), "alpha`n")
[IO.File]::WriteAllText((Join-Path $left 'b.txt'), "beta`n")
foreach ($file in 'a.txt', 'b.txt') { [IO.File]::SetLastWriteTimeUtc((Join-Path $left $file), [DateTime]::new(2023, 11, 14, 22, 13, 20, [DateTimeKind]::Utc)) }
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
    Passed = $false; Stage = 'starting'; Fixture = $fixture; Dpi = $null
    HostSha256 = (Get-FileHash -LiteralPath $hostExe -Algorithm SHA256).Hash
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $hostExe) 'FileCat.dll') -Algorithm SHA256).Hash
    WorkerSha256 = (Get-FileHash -LiteralPath $worker -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion; Error = $null
}
$reader = $app = $null
$script:result.ReleasedBefore = @(ReleaseModifiers)
try {
    $reader = Start-Process -FilePath $nvda -ArgumentList @(
        "--log-file=$script:nvdaLog", '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Await {
        if ($reader.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $script:nvdaLog) -and (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA initialization' 30 | Out-Null

    $script:result.Stage = 'start'
    $app = Start-Process -FilePath $hostExe -WorkingDirectory (Split-Path $hostExe) -PassThru -ArgumentList @(
        '--data', $data, '--worker', $worker, '--new-instance', '--left', $left, '--right', $right)
    Await { $app.Refresh(); if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'the qualification host window' 40 | Out-Null
    Start-Sleep 3
    $main = $AE::FromHandle($app.MainWindowHandle)
    $script:result.Dpi = [FileCatArchiveInput]::GetDpiForWindow($app.MainWindowHandle)
    Activate $main

    # 1. Two marked files: the dialog, its fields, the one-file refusal and a TAR + gzip pack with phase announcements.
    $script:result.Stage = 'two files'
    FocusRow 'a.txt'; Key 0x2D $true; Key 0x2D $true  # Insert marks a.txt and b.txt
    [void](TakeSpeech)
    Chord @(0x12, 0x74)  # Alt+F5
    $dialog = Await { In $main 'Create archive dialog' } 'the Create archive dialog'
    Start-Sleep -Milliseconds 1200
    $opened = @(TakeSpeech)
    Key 0x09; Start-Sleep -Milliseconds 600  # Tab from the path to the format
    $script:result.CreateDialog = [ordered]@{ Opened = $opened; Tab = @(TakeSpeech); Focused = Focused; Texts = @(Texts $dialog) }
    foreach ($phrase in 'Create archive', 'Archive path') {
        if (-not @($opened | Where-Object { $_.Contains($phrase) }).Count) { throw "Opening the dialog did not speak '$phrase': $($opened -join ' / ')" }
    }
    if (-not @($script:result.CreateDialog.Tab | Where-Object { $_.Contains('Archive format') }).Count) { throw "Tab did not reach the Archive format box: $($script:result.CreateDialog.Tab -join ' / ')" }
    ChooseFormat $dialog 'gzip (compress one file)'
    $script:result.OneFileProblem = @(Hear 'A compressed file holds exactly one file')
    $pack = In $dialog 'Pack' 'Button'
    if ($pack.Current.IsEnabled) { throw 'Pack stayed available for two files as one compressed file' }
    ChooseFormat $dialog 'TAR + gzip'
    $script:result.TarPath = PathValue $dialog
    if (-not $script:result.TarPath.EndsWith('.tar.gz')) { throw "TAR + gzip did not name a .tar.gz: $($script:result.TarPath)" }
    Key 0x09; Start-Sleep -Milliseconds 600
    $script:result.CompressionSpeech = @(TakeSpeech)
    $pack = Await { $b = In $dialog 'Pack' 'Button'; if ($b.Current.IsEnabled) { $b } } 'an available Pack button'
    $pack.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Await { Test-Path -LiteralPath $script:result.TarPath } 'the packed TAR + gzip archive' 120 | Out-Null
    Start-Sleep -Milliseconds 1500
    $script:result.PackSpeech = @(TakeSpeech)
    if (-not @($script:result.PackSpeech | Where-Object { $_ -match ': (Preparing|Rebuilding|Checking|Saving)' }).Count) {
        throw "No archive phase was announced: $($script:result.PackSpeech -join ' / ')"
    }

    # 2. One file as gzip: the stream is named after the whole file.
    $script:result.Stage = 'one file'
    Activate $main
    Chord @(0x11, 0x6D)  # Ctrl+Num- unselects all
    FocusRow 'a.txt'
    [void](TakeSpeech)
    Chord @(0x12, 0x74)
    $dialog = Await { In $main 'Create archive dialog' } 'the second Create archive dialog'
    ChooseFormat $dialog 'gzip (compress one file)'
    $script:result.GzipPath = PathValue $dialog
    if (-not $script:result.GzipPath.EndsWith('a.txt.gz')) { throw "The compressed file is not named after the whole file: $($script:result.GzipPath)" }
    $pack = Await { $b = In $dialog 'Pack' 'Button'; if ($b.Current.IsEnabled) { $b } } 'Pack for one file'
    $pack.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Await { Test-Path -LiteralPath $script:result.GzipPath } 'the compressed file' 120 | Out-Null
    Start-Sleep -Milliseconds 1500
    $script:result.GzipSpeech = @(TakeSpeech)

    # 3. Inside a.txt.gz, folder, rename and delete are refused with the reason.
    $script:result.Stage = 'inside the compressed file'
    Activate $main
    FocusRow 'a.txt.gz'; Key 0x0D  # Enter opens it
    Await { @(Descendants $main | Where-Object { $_.Current.ClassName -eq 'FileList' -and $_.Current.Name -like '*a.txt.gz*' }).Count } 'the compressed file listing' 15 | Out-Null
    Start-Sleep -Milliseconds 800
    FocusRow 'a.txt'
    [void](TakeSpeech)
    $script:result.Refusals = [ordered]@{}
    foreach ($command in @(@{ Name = 'F7'; Key = 0x76 }, @{ Name = 'F2'; Key = 0x71 }, @{ Name = 'F8'; Key = 0x77 })) {
        Key ([byte]$command.Key)
        $script:result.Refusals[$command.Name] = @(Hear 'A compressed file holds exactly one file')
        Key 0x1B; Start-Sleep -Milliseconds 400
    }
    if (-not (Test-Path -LiteralPath $script:result.GzipPath)) { throw 'The compressed file disappeared' }
    $script:result.Passed = $true
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message; try { $script:result.LastSpeech = @(TakeSpeech); $script:result.LastFocused = Focused } catch { } }
finally {
    if ($app -and -not $app.HasExited) { $app.Kill(); $app.WaitForExit(5000) | Out-Null }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(5000) | Out-Null }
    $script:result.ReleasedAfter = @(ReleaseModifiers)
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
