param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$NvdaPath,
    [Parameter(Mandatory)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DetailsNativeInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint key, uint kind);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
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
if ($session -eq 0 -or (Get-CimInstance Win32_ComputerSystem).Manufacturer -notmatch 'VMware' -or
    !$appPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($appPath) -ine 'FileCat.exe' -or
    !$nvdaPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or (Get-AuthenticodeSignature -LiteralPath $nvdaPath).Status -ne 'Valid' -or
    !$report.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $report) -or
    (Get-Process FileCat,nvda -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session)) {
    throw 'Use an idle interactive VMware guest, an owned FileCat build and signed NVDA below FileCatValidation, and a fresh receipt path.'
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
    Fixture = Split-Path $fixture -Leaf; Steps = @(); Error = $null
}
try {
    $clipboardBefore = Get-Clipboard -Raw -ErrorAction SilentlyContinue
    Set-Clipboard -Value 'FileCat details clipboard sentinel'
    $reader = Start-Process -FilePath $nvdaPath -WindowStyle Hidden -PassThru -ArgumentList @(
        "--log-file=`"$speechPath`"", '--log-level=12', "--config-path=`"$config`"", '--disable-addons')
    Await { (Test-Path -LiteralPath $speechPath) -and (Select-String -LiteralPath $speechPath -SimpleMatch 'NVDA initialized' -Quiet) } 'NVDA' 30 | Out-Null
    $application = Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -WindowStyle Normal -PassThru `
        -ArgumentList @('--new-instance', '--data', "`"$data`"", '--left', "`"$left`"", '--right', "`"$right`"")
    $main = Await {
        $application.Refresh()
        if ($application.MainWindowHandle -ne [IntPtr]::Zero) { $ui::FromHandle($application.MainWindowHandle) }
    } 'FileCat main window' 30
    $list = Await {
        $named = @($main.FindAll($scope::Descendants, $all) | Where-Object { $_.Current.ClassName -eq 'FileList' })[0]
        if ($named -and ($named.FindAll($scope::Children, $all) | Where-Object { $_.Current.Name -eq 'alpha-details.txt' })) { $named }
    } 'the populated source list'
    $result.Stage = 'keyboard selection'
    [void][DetailsNativeInput]::SetForegroundWindow([IntPtr]$main.Current.NativeWindowHandle)
    $list.SetFocus()
    Await { $ui::FocusedElement.Current.ProcessId -eq $application.Id } 'focus in FileCat' | Out-Null
    Send-Key $main @(0x24) $true
    [void](Take-Speech)
    Send-Key $main @(0x28) $true
    Hear 'the focused file' 'alpha-details\.txt'
    [void](Take-Speech)
    Send-Key $main @(0x11, 0x12, 0x0D)
    $details = Await { OwnedDetails } 'file-system details window'
    Hear 'the details window' 'File-system details'

    $result.Stage = 'summary and full record'
    $handle = [IntPtr]$details.Current.NativeWindowHandle
    $dpi = [DetailsNativeInput]::GetDpiForWindow($handle)
    if ($dpi -lt 96 -or ![DetailsNativeInput]::SetWindowPos($handle, [IntPtr]::Zero, 70, 70,
        [int](520 * $dpi / 96), [int](420 * $dpi / 96), 0x0014)) { throw 'Could not resize the owned details window.' }
    $result.Dpi = $dpi
    $bounds = Await {
        $rectangle = $details.Current.BoundingRectangle
        if ($rectangle.Width * 96 / $dpi -le 540 -and $rectangle.Height * 96 / $dpi -le 440) { $rectangle }
    } 'the narrow details window'
    $result.WindowSizeDip = @([int]($bounds.Width * 96 / $dpi), [int]($bounds.Height * 96 / $dpi))
    Tab-To $details 'Read full record'
    [void](Take-Speech)
    Send-Key $details @(0x0D)
    Hear 'the full observation' 'Full.{0,20}observation'
    Tab-To $details 'Copy all'
    Send-Key $details @(0x0D)
    $copied = Await {
        $current = Get-Clipboard -Raw
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
    Hear 'focus returning to the same file' 'alpha-details\.txt'
    if ((Get-FileHash -LiteralPath $file).Hash -ne $beforeHash -or
        (Get-Content -LiteralPath $file -Stream 'filecat-test' -Raw) -ne $alternate) { throw 'A fixture changed during inspection.' }
    $result.Passed = $true
    $result.Stage = 'complete'
}
catch { $result.Error = $_.Exception.Message; throw }
finally {
    if ($application -and !$application.HasExited) { $application.CloseMainWindow() | Out-Null; if (!$application.WaitForExit(4000)) { $application.Kill() } }
    if ($reader -and !$reader.HasExited) { $reader.Kill(); $reader.WaitForExit(4000) | Out-Null }
    if ($null -ne $clipboardBefore) { Set-Clipboard -Value $clipboardBefore }
    $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $report -Encoding UTF8
}
