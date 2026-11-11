param(
    [Parameter(Mandatory)][string] $HostPath,
    [Parameter(Mandatory)][string] $WorkerPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# Archive-writing accessibility (plan section 11.8, AT16; UX_GAP_AUDIT C) with NVDA, through the never-shipped qualification host
# (the real interface with native archive backends composed explicitly; AppContainer workers from the published app):
#   0. For one file, 7z's compression, options and password rows are spoken by name as Tab reaches them.
#   1. Pack with two marked files: the Create archive dialog, its path, format and compression controls are spoken by name;
#      choosing "gzip (compress one file)" speaks the polite problem text that offers TAR + gzip; TAR + gzip packs and the
#      operation's archive phases are announced.
#   2. Pack with one file as "gzip (compress one file)": the generated name keeps the whole file name (a.txt.gz).
#   3. Inside a.txt.gz, F7, F2 and F8 are refused and NVDA hears why.
#   4. Copying a.txt into the TAR + gzip archive that holds it: the in-job collision question is spoken from its focused
#      choice (Skip) with the count and an example; Escape cancels the operation and the archive is unchanged.
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
# 256 MiB of incompressible data, so each packing phase lasts seconds and reaches the screen reader: two tiny files
# finished within milliseconds on Windows, and so did 48 MiB: only the final result was announced (v19 runs 2 and 3).
$payload = New-Object byte[] (256MB); (New-Object Random 7).NextBytes($payload)
[IO.File]::WriteAllBytes((Join-Path $left 'b.txt'), $payload)
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
    # NVDA needed 75 s to initialize while Windows servicing (TiWorker) loaded the disk; only readiness is awaited here.
    Await {
        if ($reader.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $script:nvdaLog) -and (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA initialization' 150 | Out-Null

    $script:result.Stage = 'start'
    $app = Start-Process -FilePath $hostExe -WorkingDirectory (Split-Path $hostExe) -PassThru -ArgumentList @(
        '--data', $data, '--worker', $worker, '--new-instance', '--left', $left, '--right', $right)
    Await { $app.Refresh(); if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'the qualification host window' 40 | Out-Null
    Start-Sleep 3
    $main = $AE::FromHandle($app.MainWindowHandle)
    $script:result.Dpi = [FileCatArchiveInput]::GetDpiForWindow($app.MainWindowHandle)
    Activate $main

    # 0. Format details by name (plan 11.8, UX audit C): for one file, 7z's compression, its options and the password rows
    #    are spoken as Tab reaches them; Escape then cancels the dialog before anything is written. NVDA, like Orca, reads
    #    the options' header as a toggle button: 'pressed' (quoted, so that 'not pressed' does not match) is its word for
    #    expanded.
    $script:result.Stage = 'format details'
    FocusRow 'a.txt'; [void](TakeSpeech)
    Chord @(0x12, 0x74)  # Alt+F5
    $details = Await { In $main 'Create archive dialog' } 'the Create archive dialog'
    Start-Sleep -Milliseconds 1200; [void](TakeSpeech)
    ChooseFormat $details '7z'
    (In $details 'Archive format' 'ComboBox').SetFocus(); Start-Sleep -Milliseconds 700; [void](TakeSpeech)
    $script:result.FormatDetails = [ordered]@{}
    foreach ($step in @(@(0x09, 'Compression'), @(0x09, '7z options'), @(0x20, "'pressed'"), @(0x09, '7z compression method'),
            @(0x09, '7z preprocessing'), @(0x09, '7z dictionary'), @(0x09, '7z header compression'), @(0x09, 'Password protection'),
            @(0x28, 'Protect contents'), @(0x09, 'Archive password'), @(0x09, 'Confirm archive password'))) {
        Key ([byte]$step[0]) ($step[0] -eq 0x28)
        $script:result.FormatDetails[$step[1]] = @(Hear $step[1])
    }
    Key 0x1B  # Escape
    Await { if (-not (In $main 'Create archive dialog')) { $true } } 'the canceled dialog to close' | Out-Null
    Start-Sleep -Milliseconds 600

    # 1. Two marked files: the dialog, its fields, the one-file refusal and a TAR + gzip pack with phase announcements.
    $script:result.Stage = 'two files'
    # Ctrl+A marks both files. Insert would not: NVDA keeps Insert as its own modifier and passes only a quick double
    # press through, so two presses marked one file (first v19 run).
    FocusRow 'a.txt'; Chord @(0x11, 0x41)
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
    # The selected compression is spoken when Tab reaches it from the format box.
    (In $dialog 'Archive format' 'ComboBox').SetFocus(); Start-Sleep -Milliseconds 700; [void](TakeSpeech)
    Key 0x09
    $script:result.CompressionSpeech = @(Hear 'Compression')
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
    Key 0x09; Start-Sleep -Milliseconds 600  # Tab: the archives were created in the other panel's folder
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

    # 4. Copying a.txt into the TAR + gzip archive that already holds it (plan 11.8 collision choice): the in-job question
    #    is spoken from its focused choice (Skip, the default) with the count and an example; Escape cancels the operation
    #    and the archive stays as it was.
    $script:result.Stage = 'collision review'
    $tarName = [IO.Path]::GetFileName($script:result.TarPath)
    $tarHash = (Get-FileHash -LiteralPath $script:result.TarPath -Algorithm SHA256).Hash
    Key 0x08; Start-Sleep -Milliseconds 800  # Backspace: up from a.txt.gz to its folder
    FocusRow $tarName; Key 0x0D
    Await { @(Descendants $main | Where-Object { $_.Current.ClassName -eq 'FileList' -and $_.Current.Name -like "*$tarName*" }).Count } 'the TAR + gzip listing' 20 | Out-Null
    Start-Sleep -Milliseconds 800
    Key 0x09; Start-Sleep -Milliseconds 600  # Tab: back to the source panel
    FocusRow 'a.txt'; [void](TakeSpeech)
    Key 0x74  # F5
    Await { In $main 'Copy dialog' } 'the Copy dialog' | Out-Null
    Start-Sleep -Milliseconds 800
    Key 0x0D  # Enter copies into the archive
    # Background work never takes the keyboard: the operation says a decision waits in Operations (Ctrl+J), where Tab
    # reaches 'Review decision...' and Space opens the question with its first choice focused.
    $script:result.DecisionNotice = @(Hear 'a decision is required' 120)
    Chord @(0x11, 0x4A)  # Ctrl+J
    Start-Sleep -Milliseconds 800
    $review = 'Review decision' + [char]0x2026
    for ($i = 0; $i -lt 12 -and (Focused) -ne $review; $i++) { Key 0x09 }
    if ((Focused) -ne $review) { throw "Tab never reached the Review decision button (at '$(Focused)')" }
    [void](TakeSpeech); Key 0x20  # Space
    $script:result.CollisionSpeech = @(Hear 'can replace an existing file' 30)
    foreach ($phrase in 'Names already in the archive', 'Skip', 'a.txt') {
        if (-not @($script:result.CollisionSpeech | Where-Object { $_.Contains($phrase) }).Count) {
            throw "The collision choice did not speak '$phrase': $($script:result.CollisionSpeech -join ' / ')"
        }
    }
    Key 0x1B  # Escape cancels the operation
    Start-Sleep -Milliseconds 2500
    if ((Get-FileHash -LiteralPath $script:result.TarPath -Algorithm SHA256).Hash -ne $tarHash) { throw 'Escape changed the TAR + gzip archive' }
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
