param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$NvdaPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [switch]$NarrowHighContrast,
    [switch]$ShortNarrow,
    [switch]$PictureView,
    [switch]$FindResults,
    [switch]$VerifyNativeProperties
)

$ErrorActionPreference = 'Stop'
if ($FindResults -and $PictureView) { throw 'Choose the Find list fixture or the panel picture fixture.' }
if ($ShortNarrow -and !$NarrowHighContrast) { throw 'ShortNarrow requires NarrowHighContrast.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DetailsNativeInput {
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after,
        int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint key, uint kind);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static bool Resize(IntPtr window, int widthDip, int heightDip) {
        IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // Per-monitor v2, in physical pixels.
        if (previous == IntPtr.Zero) return false;
        try {
            uint dpi = GetDpiForWindow(window);
            return dpi != 0 && SetWindowPos(window, IntPtr.Zero, 70, 70,
                (int)Math.Round(widthDip * dpi / 96.0), (int)Math.Round(heightDip * dpi / 96.0), 0x0014);
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }
    public static int[] VisibleFrameSize(IntPtr window) {
        Rect frame;
        if (DwmGetWindowAttribute(window, 9, out frame, Marshal.SizeOf(typeof(Rect))) != 0) return new int[0];
        return new[] { frame.Right - frame.Left, frame.Bottom - frame.Top };
    }
    public static void Send(byte[] keys, bool extended) {
        uint flags = extended ? 1u : 0u;
        foreach (byte key in keys) keybd_event(key, (byte)MapVirtualKey(key, 0), flags, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--)
            keybd_event(keys[i], (byte)MapVirtualKey(keys[i], 0), flags | 2u, UIntPtr.Zero);
    }
}
'@

$ui = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$all = [System.Windows.Automation.Condition]::TrueCondition
$root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation')) + '\'
$appPath = [IO.Path]::GetFullPath($AppPath)
$nvdaPath = [IO.Path]::GetFullPath($NvdaPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
# The disposable VMware guest, or the disposable Windows on Arm guest that UTM runs under QEMU on the test Mac.
$guest = (Get-CimInstance Win32_ComputerSystem).Manufacturer
# Ask the OS: an emulated x64 launcher passes its own PROCESSOR_ARCHITECTURE down to native Arm64 children.
$validationGuest = $guest -match 'VMware' -or ($guest -match 'QEMU|UTM' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64')
if ($session -eq 0 -or !$validationGuest -or
    !$appPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($appPath) -ine 'FileCat.exe' -or
    !$nvdaPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or (Get-AuthenticodeSignature -LiteralPath $nvdaPath).Status -ne 'Valid' -or
    !$report.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $report) -or
    (Get-Process dotnet,testhost,FileCat,FileCat.ShellHost,FileCat.*QualificationHost,nvda -ErrorAction SilentlyContinue)) {
    throw 'Use an idle interactive validation guest, an owned FileCat build and signed NVDA below FileCatValidation, and a fresh receipt path.'
}

function Await([scriptblock]$probe, [string]$label, [int]$seconds = 20) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $seconds) {
        try { $found = & $probe; if ($found) { return $found } }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        catch [Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 140
    }
    throw "Timed out waiting for $label."
}

function OwnedDetails {
    @($ui::RootElement.FindAll($scope::Children, $all) | Where-Object {
        $_.Current.ProcessId -eq $application.Id -and $_.Current.Name -like '*File-system details*'
    }) | Select-Object -First 1
}

function Send-Key($window, [byte[]]$keys, [bool]$extended = $false) {
    if ($window.Current.ProcessId -ne $application.Id -or
        [DetailsNativeInput]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle) {
        throw 'FileCat does not own the foreground keyboard input.'
    }
    [DetailsNativeInput]::Send($keys, $extended)
    Start-Sleep -Milliseconds 250
}

function Take-Speech {
    $lines = @(Get-Content -LiteralPath $speechPath -Encoding UTF8 -ErrorAction SilentlyContinue)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    @($fresh | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
}

# A clipboard viewer (the UTM guest's SPICE agent, a clipboard manager) can hold the clipboard open for a moment.
function Use-Clipboard([scriptblock]$action) {
    for ($attempt = 1; ; $attempt++) {
        try { return & $action }
        catch {
            $busy = $_.Exception -is [Runtime.InteropServices.ExternalException] -or
                $_.Exception.InnerException -is [Runtime.InteropServices.ExternalException]
            if (!$busy -or $attempt -ge 12) { throw }
        }
        Start-Sleep -Milliseconds 400
    }
}

function Hear([string]$label, [string]$pattern) {
    $phrases = @()
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 12) {
        $phrases += @(Take-Speech)
        if (($phrases -join ' ') -match $pattern) {
            $result.Steps += @{ Action = $label; Speech = $phrases }
            return
        }
        Start-Sleep -Milliseconds 150
    }
    $result.Steps += @{ Action = $label; Speech = $phrases }
    throw "NVDA did not speak $label."
}

function Tab-To($window, [string]$name) {
    for ($index = 0; $index -lt 24; $index++) {
        [void](Take-Speech)
        Send-Key $window @(0x09)
        $focused = $ui::FocusedElement.Current
        if ($focused.Name -eq $name -and !$focused.IsOffscreen) {
            Hear $name ([regex]::Escape($name))
            return
        }
    }
    throw "Tab never reached $name."
}

$fixture = Join-Path (Split-Path $report) ('fixture-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$left = Join-Path $fixture ('left-' + 'long-path-' * 6)
$right = Join-Path $fixture 'right'
$data = Join-Path $fixture 'state'
$config = Join-Path $fixture 'nvda'
New-Item -ItemType Directory -Force -Path $left,$right,$data,$config | Out-Null
$requestedWidth = 520
$requestedHeight = 420
$widthLimit = 540
if ($NarrowHighContrast) {
    [IO.File]::WriteAllText((Join-Path $data 'settings.json'), '{"SchemaVersion":1,"Theme":"HighContrast","ThemeAnimations":false}')
    $requestedWidth = 336
    $requestedHeight = 560
    $widthLimit = 324
}
if ($ShortNarrow) { $requestedHeight = 420 }
$file = Join-Path $left 'alpha-details.txt'
$other = Join-Path $left 'beta-other.txt'
$payload = 'FILECAT-OWNED-FILE-PAYLOAD-CANARY'
$alternate = 'FILECAT-OWNED-ADS-PAYLOAD-CANARY'
[IO.File]::WriteAllText($file, $payload)
[IO.File]::WriteAllText($other, 'Another fixture')
Set-Content -LiteralPath $file -Stream 'filecat-test' -Value $alternate -NoNewline
$beforeHash = (Get-FileHash -LiteralPath $file).Hash
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), "[general]`r`nshowWelcomeDialogAtStartup = False`r`n[update]`r`nautoCheck = False`r`nstartupNotification = False`r`nallowUsageStats = False`r`naskedAllowUsageStats = True`r`n")
$speechPath = Join-Path $fixture 'nvda.log'
$script:speechIndex = 0
$application = $reader = $clipboardBefore = $null
$result = [ordered]@{
    Passed = $false; Stage = 'start'; Session = $session; OS = [Environment]::OSVersion.Version.ToString()
    AppDllSHA256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $appPath) 'FileCat.dll')).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvdaPath).VersionInfo.ProductVersion
    NarrowHighContrast = [bool]$NarrowHighContrast
    ShortNarrow = [bool]$ShortNarrow
    PictureView = [bool]$PictureView
    FindResults = [bool]$FindResults
    VerifyNativeProperties = [bool]$VerifyNativeProperties
    Fixture = Split-Path $fixture -Leaf; Steps = @(); Error = $null
}
try {
    $clipboardBefore = try { Use-Clipboard { Get-Clipboard -Raw } } catch { $null }
    Use-Clipboard { Set-Clipboard -Value 'FileCat details clipboard sentinel' }
    $application = Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -WindowStyle Normal -PassThru `
        -ArgumentList @('--new-instance', '--data', "`"$data`"", '--left', "`"$left`"", '--right', "`"$right`"")
    $main = Await {
        $application.Refresh()
        if ($application.MainWindowHandle -ne [IntPtr]::Zero) { $ui::FromHandle($application.MainWindowHandle) }
    } 'FileCat main window' 90 # a cold start on the Windows on Arm VM took over 30 s
    $list = Await {
        # A window taken early in a slow cold start can stay an empty shell to UI Automation; take it again each time.
        $application.Refresh()
        if ($application.MainWindowHandle -ne [IntPtr]::Zero) { $script:main = $ui::FromHandle($application.MainWindowHandle) }
        $named = @($main.FindAll($scope::Descendants, $all) | Where-Object { $_.Current.ClassName -eq 'FileList' })[0]
        if ($named -and ($named.FindAll($scope::Children, $all) | Where-Object { $_.Current.Name -eq 'alpha-details.txt' })) { $named }
    } 'the populated source list' 60
    $result.Stage = 'keyboard selection'
    [void][DetailsNativeInput]::SetForegroundWindow([IntPtr]$main.Current.NativeWindowHandle)
    $list.SetFocus()
    Await { $ui::FocusedElement.Current.ProcessId -eq $application.Id } 'focus in FileCat' | Out-Null
    $reader = Start-Process -FilePath $nvdaPath -WindowStyle Hidden -PassThru -ArgumentList @(
        "--log-file=`"$speechPath`"", '--log-level=12', "--config-path=`"$config`"", '--disable-addons')
    Await { (Test-Path -LiteralPath $speechPath) -and (Select-String -LiteralPath $speechPath -SimpleMatch 'NVDA initialized' -Quiet) } 'NVDA' 120 | Out-Null # NVDA's x64 build runs emulated on Windows on Arm
    # Emulated NVDA on Windows on Arm sometimes speaks the window's title alone, without its role.
    Hear 'the active FileCat window' "'FileCat [^']{0,200}'|FileCat.{0,180}window|Panel 1, source"
    Send-Key $main @(0x24) $true
    [void](Take-Speech)
    Send-Key $main @(0x28) $true
    Hear 'the focused file' 'alpha-details\.txt'
    $origin = $main
    if ($FindResults) {
        $result.Stage = 'standalone Find results'
        Send-Key $main @(0x12, 0x76) # Alt+F7.
        $origin = Await {
            $ui::RootElement.FindAll($scope::Children, $all) | Where-Object {
                $_.Current.ProcessId -eq $application.Id -and $_.Current.Name -eq 'Find files'
            } | Select-Object -First 1
        } 'the owned standalone Find window'
        $findHandle = [IntPtr]$origin.Current.NativeWindowHandle
        Await { [DetailsNativeInput]::GetForegroundWindow() -eq $findHandle } 'focus in standalone Find' | Out-Null
        $fields = $origin.FindAll($scope::Descendants, $all)
        $names = $fields | Where-Object { $_.Current.Name -eq 'Names' -and $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Edit' } | Select-Object -First 1
        $where = $fields | Where-Object { $_.Current.Name -eq 'Look in' -and $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Edit' } | Select-Object -First 1
        if (!$names -or !$where) { throw 'The named Find fields are unavailable.' }
        $where.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($left)
        $names.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('alpha-details.txt')
        $names.SetFocus()
        Send-Key $origin @(0x0D)
        $findList = Await {
            $origin.FindAll($scope::Descendants, $all) | Where-Object {
                $_.Current.ClassName -eq 'FileList' -and
                    ($_.FindAll($scope::Children, $all) | Where-Object { $_.Current.Name -eq 'alpha-details.txt' })
            } | Select-Object -First 1
        } 'the matching original in Find results'
        $findList.SetFocus()
        [void](Take-Speech)
        Send-Key $origin @(0x24) $true
        Hear 'the focused Find result' 'alpha-details\.txt'
    }
    if ($PictureView) {
        $result.Stage = 'switch to picture view'
        Add-Type -AssemblyName System.Windows.Forms
        Send-Key $main @(0x11, 0x10, 0x50)
        Await {
            $focus = $ui::FocusedElement.Current
            $focus.ProcessId -eq $application.Id -and $focus.Name -eq 'Search commands' -and
                [DetailsNativeInput]::GetForegroundWindow() -eq [IntPtr]$main.Current.NativeWindowHandle
        } 'FileCat command search' | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait('Show or hide pictures')
        Send-Key $main @(0x0D)
        Await {
            $main.FindAll($scope::Descendants, $all) | Where-Object {
                $_.Current.ClassName -eq 'FileList' -and $_.Current.Name -like 'Panel 1, source, pictures grid,*' -and
                    ($_.FindAll($scope::Children, $all) | Where-Object { $_.Current.Name -eq 'alpha-details.txt' })
            } | Select-Object -First 1
        } 'the same file in the picture grid' | Out-Null
    }
    [void](Take-Speech)
    Send-Key $origin @(0x11, 0x12, 0x0D)
    $details = Await { OwnedDetails } 'file-system details window'
    Hear 'the details window' 'File-system details'

    $result.Stage = 'summary and full record'
    $handle = [IntPtr]$details.Current.NativeWindowHandle
    $dpi = [DetailsNativeInput]::GetDpiForWindow($handle)
    if ($dpi -lt 96 -or ![DetailsNativeInput]::Resize($handle, $requestedWidth, $requestedHeight)) { throw 'Could not resize the owned details window.' }
    $result.Dpi = $dpi
    Await {
        $frame = [DetailsNativeInput]::VisibleFrameSize($handle)
        if ($frame.Length -ne 2) { return $false }
        $widthDip = $frame[0] * 96 / $dpi
        $heightDip = $frame[1] * 96 / $dpi
        $result.WindowSizeDip = @([int]$widthDip, [int]$heightDip)
        $widthDip -ge ($requestedWidth - 28) -and $widthDip -le $widthLimit -and
            $heightDip -ge ($requestedHeight - 55) -and $heightDip -le ($requestedHeight + 20)
    } 'the narrow details window' | Out-Null
    if ($ShortNarrow) { Tab-To $details 'Next report line' }
    Tab-To $details 'Read full record'
    [void](Take-Speech)
    Send-Key $details @(0x0D)
    Hear 'the full observation' 'Full.{0,20}observation'
    Tab-To $details 'Copy all'
    Send-Key $details @(0x0D)
    $copied = Await {
        $current = Use-Clipboard { Get-Clipboard -Raw }
        if ($current -like '*Full record*' -and $current.Contains($file)) { $current }
    } 'the full report on the clipboard'
    if ($copied -notlike '*metadata only*' -or $copied -notlike '*supplementary-data values and previews*' -or
        $copied.Contains($payload) -or $copied.Contains($alternate) -or $copied.Contains('beta-other.txt')) {
        throw 'The copy differs from the focused file and metadata-only scope.'
    }
    $result.ClipboardBytes = [Text.Encoding]::UTF8.GetByteCount($copied)
    Tab-To $details 'Show summary'
    [void](Take-Speech)
    Send-Key $details @(0x0D)
    Hear 'the summary observation' 'Summary.{0,20}observation'
    Tab-To $details 'Show full record'
    [void](Take-Speech)
    Send-Key $details @(0x0D)
    Hear 'the cached full observation' 'Full.{0,20}observation'

    $result.Stage = 'return to focused row'
    [void](Take-Speech)
    Send-Key $details @(0x1B)
    Await { !(OwnedDetails) } 'the details window to close' | Out-Null
    $returnedSpeech = if ($PictureView) { 'pictures grid.*alpha-details\.txt' } else { 'alpha-details\.txt' }
    Hear 'focus returning to the same file' $returnedSpeech
    if ($FindResults) {
        if ([DetailsNativeInput]::GetForegroundWindow() -ne $findHandle -or
            !($findList.FindAll($scope::Children, $all) | Where-Object { $_.Current.Name -eq 'alpha-details.txt' })) {
            throw 'The original Find results window and row are not active.'
        }
        $result.FindResultRestored = $true
    }
    if ($VerifyNativeProperties) {
        $result.Stage = 'native Windows Properties'
        $propertyTitles = @(
            [IO.Path]::GetFileNameWithoutExtension($file) + ' Properties'
            [IO.Path]::GetFileName($file) + ' Properties'
        )
        $findProperties = {
            $ui::RootElement.FindAll($scope::Children, $all) | Where-Object {
                $_.Current.Name -in $propertyTitles -and $_.Current.ClassName -eq '#32770'
            } | Select-Object -First 1
        }
        if (& $findProperties) { throw 'The fixture Properties window was already open.' }
        Await {
            [DetailsNativeInput]::GetForegroundWindow() -eq [IntPtr]$origin.Current.NativeWindowHandle
        } 'the focused FileCat window' | Out-Null
        [void](Take-Speech)
        Send-Key $origin @(0x12, 0x0D)
        $properties = Await { & $findProperties } 'the native Properties dialog for the same file'
        $propertyTitle = 'alpha-details(?:\.txt)? Properties'
        Hear 'native Properties for the same file' "$propertyTitle|General.{0,8}property page"
        $titleOnOpen = ($result.Steps[-1].Speech -join ' ') -match $propertyTitle
        if (OwnedDetails) { throw 'Alt+Enter also opened File-system details.' }
        $propertyHandle = [IntPtr]$properties.Current.NativeWindowHandle
        if ([DetailsNativeInput]::GetForegroundWindow() -ne $propertyHandle) {
            throw 'The native Properties dialog does not own the foreground keyboard input.'
        }
        if (!$titleOnOpen) {
            # NVDA's x64 build, emulated on Windows on Arm, can stall as the sheet opens and announce only its page; NVDA+T reads the title.
            [void](Take-Speech)
            [DetailsNativeInput]::Send(@(0x2D, 0x54), $false) # Numpad Insert, an NVDA key by default, with T.
            Hear 'the native Properties title on request' $propertyTitle
        }
        $result.NativeProperties = @{
            Title = $properties.Current.Name; ClassName = $properties.Current.ClassName; TitleSpokenOnOpen = $titleOnOpen
        }
        [DetailsNativeInput]::Send(@(0x1B), $false)
        Await { !(& $findProperties) } 'the native Properties dialog to close' | Out-Null
    }
    if ((Get-FileHash -LiteralPath $file).Hash -ne $beforeHash -or
        (Get-Content -LiteralPath $file -Stream 'filecat-test' -Raw) -ne $alternate) { throw 'A fixture changed during inspection.' }
    $result.Passed = $true
    $result.Stage = 'complete'
}
catch { $result.Error = $_.Exception.Message; throw }
finally {
    if ($application -and !$application.HasExited) { $application.CloseMainWindow() | Out-Null; if (!$application.WaitForExit(4000)) { $application.Kill() } }
    if ($reader -and !$reader.HasExited) { $reader.Kill(); $reader.WaitForExit(4000) | Out-Null }
    if ($null -ne $clipboardBefore) {
        try { Use-Clipboard { Set-Clipboard -Value $clipboardBefore } } catch { $result.ClipboardRestoreError = $_.Exception.Message }
    }
    $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $report -Encoding UTF8
}
