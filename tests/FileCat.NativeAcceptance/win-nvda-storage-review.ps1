[CmdletBinding(DefaultParameterSetName = 'Review')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Review')][string]$TestRoot,
    [Parameter(Mandatory, ParameterSetName = 'Review')][string]$NvdaPath,
    [Parameter(Mandatory, ParameterSetName = 'Compile')][string]$NativeInputAssembly,
    [switch]$HostedArm64,
    [switch]$HighContrastPictures,
    [switch]$VerifyDuplicateRoots,
    [switch]$VerifyOperationsRecovery,
    [switch]$VerifyAlreadySatisfied,
    [switch]$VerifyCanceledKeyBackup,
    [switch]$VerifyCompletedKeyBackup,
    [switch]$VerifySelectedFileBackup,
    # The shipped FileCat.exe with its default composition instead of the qualification host; for the modes that need
    # no qualification hooks (not Operations recovery, which holds the native call through a host-only fixture).
    [switch]$ReleaseBuild
)

$ErrorActionPreference = 'Stop'
$nativeInputSource = @'
using System;
using System.Runtime.InteropServices;
public static class StorageReviewInput {
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint key, uint kind);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static void Send(byte[] keys, bool extended) {
        uint flags = extended ? 1u : 0u;
        foreach (byte key in keys) keybd_event(key, (byte)MapVirtualKey(key, 0), flags, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--)
            keybd_event(keys[i], (byte)MapVirtualKey(keys[i], 0), flags | 2u, UIntPtr.Zero);
    }
    public static bool ResizeClient(IntPtr window, int width, int height) {
        Rect outer, client;
        if (!GetWindowRect(window, out outer) || !GetClientRect(window, out client)) return false;
        uint dpi = GetDpiForWindow(window);
        if (dpi == 0) return false;
        return SetWindowPos(window, IntPtr.Zero, 0, 0,
            (int)Math.Round(width * dpi / 96.0) + outer.Right - outer.Left - client.Right,
            (int)Math.Round(height * dpi / 96.0) + outer.Bottom - outer.Top - client.Bottom, 0x0016);
    }
    public static int[] ClientDip(IntPtr window) {
        Rect bounds;
        uint dpi = GetDpiForWindow(window);
        if (dpi == 0 || !GetClientRect(window, out bounds)) return Array.Empty<int>();
        return new[] { (int)Math.Round(bounds.Right * 96.0 / dpi), (int)Math.Round(bounds.Bottom * 96.0 / dpi) };
    }
    public static bool ClickUncoveredTitle(IntPtr window) {
        Rect bounds;
        if (!GetWindowRect(window, out bounds)) return false;
        var point = new Point { X = (bounds.Left + bounds.Right) / 2, Y = bounds.Top + 12 };
        if (GetAncestor(WindowFromPoint(point), 2) != window || !SetCursorPos(point.X, point.Y)) return false;
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        return true;
    }
    public static uint ForegroundProcess() {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return 0;
        uint pid;
        GetWindowThreadProcessId(foreground, out pid);
        return pid;
    }
    public static int InputDesktopError() {
        IntPtr desktop = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS.
        if (desktop == IntPtr.Zero) return Marshal.GetLastWin32Error();
        CloseDesktop(desktop);
        return 0;
    }
}
'@
if ($PSCmdlet.ParameterSetName -eq 'Compile') {
    Add-Type -TypeDefinition $nativeInputSource -OutputAssembly $NativeInputAssembly -OutputType Library
    return
}
if (@($VerifyCanceledKeyBackup, $VerifyCompletedKeyBackup, $VerifySelectedFileBackup, $VerifyDuplicateRoots,
    $VerifyOperationsRecovery, $VerifyAlreadySatisfied).Where({ $_.IsPresent }).Count -gt 1) {
    throw 'Choose one of the optional fixture modes.'
}
if ($ReleaseBuild -and ($VerifyOperationsRecovery -or $HostedArm64)) {
    throw 'The release build has no qualification hooks; run Operations recovery and hosted runs with the qualification host.'
}
$testRoot = [IO.Path]::GetFullPath($TestRoot)
$nvdaPath = [IO.Path]::GetFullPath($NvdaPath)
$session = (Get-Process -Id $PID).SessionId
# The disposable Windows on Arm guest that UTM runs under QEMU on the test Mac reviews with the native ARM64 storage.
$localArm64 = $false
if ($HostedArm64) {
    if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows' -or
        $env:RUNNER_ARCH -ne 'ARM64' -or ![IO.Path]::IsPathRooted($env:RUNNER_TEMP)) {
        throw 'Use the hosted Windows ARM64 qualification run.'
    }
    $validationRoot = [IO.Path]::GetFullPath((Join-Path $env:RUNNER_TEMP 'filecat-native-nvda')) + '\'
} else {
    $guest = (Get-CimInstance Win32_ComputerSystem).Manufacturer
    # Ask the OS: an emulated x64 launcher passes its own PROCESSOR_ARCHITECTURE down to native Arm64 children.
    $localArm64 = $guest -match 'QEMU|UTM' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64'
    if ($guest -notmatch 'VMware' -and !$localArm64) {
        throw 'Use the disposable VMware validation guest or the Windows on Arm guest under UTM.'
    }
    $validationRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation')) + '\'
}
if (!$session -or
    !$testRoot.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    !$nvdaPath.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    (Get-AuthenticodeSignature -LiteralPath $nvdaPath).Status -ne 'Valid') {
    throw 'Run the owned storage UI fixture with signed NVDA in the interactive validation profile.'
}
$log = Join-Path $testRoot 'storage-review-result.json'
if (Test-Path -LiteralPath $log) { throw 'Use a fresh test root to preserve prior receipts.' }
$record = [ordered]@{ Passed = $false; Stage = 'process preflight'; Error = $null; Session = $session; Controls = @{}; Speech = @{} }
$app = $reader = $backupPath = $null
$previousDpiContext = [IntPtr]::Zero
try {
    if (Get-Process dotnet,testhost,FileCat,FileCat.ShellHost,FileCat.App.Tests,FileCat.*QualificationHost,FileCat.*CrashHarness,nvda -ErrorAction SilentlyContinue) {
        throw 'A test process is already using this VM.'
    }
    if ($VerifyCanceledKeyBackup -or $VerifyCompletedKeyBackup -or $VerifySelectedFileBackup) {
        $record.Stage = 'verify existing fixture key'
        $currentKey = & (Join-Path $env:WINDIR 'System32\cipher.exe') /y | Out-String
        $keyLine = [regex]::Match($currentKey, '(?im)^[ \t]*(?:[a-f0-9]{4}[ \t]*){10}\r?$')
        if ($LASTEXITCODE -ne 0 -or !$keyLine.Success) {
            throw 'The backup fixture needs a pre-existing default EFS certificate.'
        }
        $fixtureThumbprint = $keyLine.Value -replace '\s', ''
        if (!(Get-Item -LiteralPath "Cert:\CurrentUser\My\$fixtureThumbprint").HasPrivateKey) {
            throw 'The existing fixture certificate does not have its private key.'
        }
    }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Windows.Forms
    if ($VerifyCompletedKeyBackup -or $VerifySelectedFileBackup) {
        Add-Type -Path (Join-Path $PSScriptRoot 'WindowsEfsBackupWizardDriver.cs')
    }
    if ($HostedArm64) {
        Add-Type -Path (Join-Path $validationRoot 'StorageReviewInput.dll')
    } else {
        Add-Type -TypeDefinition $nativeInputSource
    }
    $previousDpiContext = [StorageReviewInput]::SetThreadDpiAwarenessContext([IntPtr](-4)) # Per-monitor v2.
    if ($previousDpiContext -eq [IntPtr]::Zero) { throw 'Could not use physical pixels for the window measurements.' }
    $ui = [System.Windows.Automation.AutomationElement]
    $scope = [System.Windows.Automation.TreeScope]
    $all = [System.Windows.Automation.Condition]::TrueCondition
    function Await([scriptblock]$probe, [string]$description, [int]$seconds = 18) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while ($watch.Elapsed.TotalSeconds -lt $seconds) {
            try { $value = & $probe; if ($value) { return $value } }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
            catch [Runtime.InteropServices.COMException] { }
            Start-Sleep -Milliseconds 120
        }
        throw "Timed out waiting for $description."
    }
    function Require-Front {
        if ($main.Current.ProcessId -ne $app.Id -or [StorageReviewInput]::GetForegroundWindow() -ne [IntPtr]$main.Current.NativeWindowHandle) {
            throw 'The qualification instance does not own the foreground window.'
        }
    }
    function Key([byte[]]$keys, [bool]$extended = $false) {
        Require-Front
        [StorageReviewInput]::Send($keys, $extended)
        Start-Sleep -Milliseconds 300
    }
    function Find-Control([string]$name, [string]$kind) {
        $main.FindAll($scope::Descendants, $all) | Where-Object {
            $_.Current.Name -like $name -and (!$kind -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$kind")
        } | Select-Object -First 1
    }
    function Take-Speech {
        $lines = @(Get-Content -LiteralPath $speechPath -Encoding UTF8 -ErrorAction SilentlyContinue)
        $new = @($lines | Select-Object -Skip $script:speechIndex)
        $script:speechIndex = $lines.Count
        $new | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() }
    }
    function Hear([string]$label, [string]$pattern) {
        $record.Speech[$label] = @()
        Await {
            $record.Speech[$label] += @(Take-Speech)
            ($record.Speech[$label] -join ' ') -match $pattern
        } "NVDA speech for $label" | Out-Null
    }
    function Require-Footer([string]$action, [bool]$enabled) {
        foreach ($name in 'Cancel','Queue',$action) {
            $button = Find-Control $name 'Button'
            if (!$button -or $button.Current.IsOffscreen -or $button.Current.IsEnabled -ne ($enabled -or $name -eq 'Cancel')) {
                throw "The review footer does not expose the expected $name state."
            }
        }
    }
    function Require-Consent([string]$name, [bool]$checked) {
        $box = Find-Control $name 'CheckBox'
        if (!$box) { throw "Missing consent: $name" }
        $toggle = $box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if (($toggle.Current.ToggleState.ToString() -eq 'On') -ne $checked) { throw "Unexpected consent state: $name" }
        return $box.Current.Name
    }
    function Show-Command([string]$phrase, [string]$title) {
        Key @(0x11, 0x10, 0x50)
        Await { $ui::FocusedElement.Current.Name -eq 'Search commands' } 'command search focus' | Out-Null
        Require-Front
        [System.Windows.Forms.SendKeys]::SendWait($phrase)
        Start-Sleep -Milliseconds 300
        # On a slow guest the palette can still be filtering; Enter then runs whatever was first a moment ago.
        Await { Find-Control "$phrase*" 'ListItem' } "the $phrase command in the palette" | Out-Null
        [void](Take-Speech)
        Key @(0x0D)
        Await { Find-Control $title } $title 60 | Out-Null
    }
    function Snapshot([string]$label) {
        $record.Controls[$label] = @($main.FindAll($scope::Descendants, $all) | ForEach-Object {
            $item = $_.Current
            if ($item.Name) { [pscustomobject]@{Name=$item.Name; Type=$item.ControlType.ProgrammaticName; Enabled=$item.IsEnabled; Offscreen=$item.IsOffscreen} }
        })
    }
    function Tab-To([string]$name, [string]$kind) {
        for ($i = 0; $i -lt 30; $i++) {
            $focus = $ui::FocusedElement.Current
            if ($focus.Name -eq $name -and !$focus.IsOffscreen -and
                (!$kind -or $focus.ControlType.ProgrammaticName -eq "ControlType.$kind")) { return }
            Key @(0x09)
        }
        throw "Keyboard focus did not reach $name."
    }
    function Complete-AlreadySatisfied([string]$button, [string]$action) {
        $record.Stage = "$action already-satisfied result"
        Tab-To $button 'Button'
        [void](Take-Speech)
        Key @(0x0D)
        $result = Await {
            Find-Control "*$action*0 changed, 1 already satisfied, 0 skipped, 0 failed, 0 uncertain, 0 not processed*"
        } "the completed $action result"
        if ($result.Current.IsOffscreen) { throw "$action has no visible completed result." }
        $record.AlreadySatisfied[$action] = $result.Current.Name
        Snapshot $record.Stage
    }
    function Verify-OriginalLocations([string]$reviewName) {
        $record.Stage = "$reviewName original locations"
        $name = 'Selected items and original locations, select text to copy'
        [void](Take-Speech)
        $items = Find-Control $name 'Edit'
        if (!$items -or $items.Current.IsOffscreen) {
            Tab-To 'Selected items (2)' 'Button'
            Key @(0x20)
            $items = Await { Find-Control $name 'Edit' } 'the expanded original locations'
        }
        Tab-To $name 'Edit'
        Hear "$reviewName original locations control" 'Selected items and original locations'
        $value = $items.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current
        if (!$value.IsReadOnly) { throw 'The original locations control must be read-only.' }
        foreach ($path in $file,$duplicate) {
            $quoted = ConvertTo-Json -InputObject $path -Compress
            if (!$value.Value.Contains("Original location: $quoted")) { throw "The review omitted $path." }
        }
        $record.ReviewedOriginals[$reviewName] = @($file,$duplicate)
    }
    function Find-Report([string]$title) {
        Await {
            $ui::RootElement.FindAll($scope::Children, $all) | Where-Object {
                $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like "*$title*"
            } | Select-Object -First 1
        } $title
    }
    function Find-MenuItem([string]$name) {
        Await {
            $item = Find-Control $name 'MenuItem'
            if ($item) { return $item }
            foreach ($popup in $ui::RootElement.FindAll($scope::Children, $all)) {
                if ($popup.Current.ProcessId -ne $app.Id -or $popup.Current.NativeWindowHandle -eq $main.Current.NativeWindowHandle) { continue }
                $item = $popup.FindAll($scope::Descendants, $all) | Where-Object {
                    $_.Current.Name -like $name -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem
                } | Select-Object -First 1
                if ($item) { return $item }
            }
        } "menu item $name"
    }
    function Open-CompressionFromMenu {
        foreach ($menuName in 'File','Inspect') {
            (Find-MenuItem $menuName).GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        }
        (Find-MenuItem 'NTFS compression*').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Await { Find-Control 'Choose action' 'Button' } 'the new compression review' | Out-Null
    }
    function Acknowledge-PendingReport($report) {
        $main = $report
        $handle = [IntPtr]$report.Current.NativeWindowHandle
        [void][StorageReviewInput]::SetForegroundWindow($handle)
        Await { Find-Control 'Line 1 of *FileCat storage history review*' 'Text' } 'the displayed storage snapshot' | Out-Null
        Tab-To "Acknowledge reviewed history$([char]0x2026)" 'Button'
        Key @(0x20)
    }
    function Invoke-StorageStop([string]$markers, [string]$focusedName, [bool]$verifyBlocker = $false) {
        $record.Stage = "submit compression for $focusedName"
        Open-CompressionFromMenu
        Tab-To 'Compress' 'RadioButton'
        Key @(0x20)
        Await { Find-Control 'Every selected item passed inspection for this action.' } 'completed compression inspection' | Out-Null
        Tab-To 'Compress' 'Button'
        Key @(0x20)
        $returned = Join-Path $markers 'set-returned'
        Await {
            if (Test-Path -LiteralPath $returned) { (Get-Item -LiteralPath $returned).Length -eq 1 }
        } 'native compression SET to return' | Out-Null
        if ([IO.File]::ReadAllBytes($returned)[0] -ne 1) { throw 'Windows did not report a successful compression SET.' }
        if ($verifyBlocker) {
            $record.Stage = 'leave the blocked review for its running operation'
            Await {
                $focus = $ui::FocusedElement.Current
                $focus.Name -eq $focusedName -or $focus.ClassName -eq 'FileList'
            } 'file focus after submitting the first review' | Out-Null
            [void](Take-Speech)
            Open-CompressionFromMenu
            $blocker = 'Cancel preparation and show the blocking operation'
            Await { Find-Control $blocker 'Button' } 'the review escape to the blocking operation' | Out-Null
            Tab-To $blocker 'Button'
            Key @(0x2D, 0x09) # NVDA+Tab reports the current focus.
            Hear 'blocked preparation escape' 'Cancel preparation and show the blocking operation'
            Key @(0x20)
            Await {
                !(Find-Control $blocker 'Button') -and $ui::FocusedElement.Current.Name -like "*$focusedName*Running*"
            } 'focus on the exact running blocker after dismissing review' | Out-Null
            Snapshot 'running blocker'
        }
        $record.Stage = "stop compression for $focusedName"
        if (!$verifyBlocker) { Key @(0x11, 0x4A) }
        [void](Take-Speech)
        Tab-To 'Stop' 'Button'
        Hear "Stop control for $focusedName" '\bStop\b'
        $stop = $ui::FocusedElement.Current
        if (!$stop.IsEnabled -or $stop.HelpText -notlike '*An admitted call finishes; completed changes remain.*') {
            throw 'The Stop control did not explain what happens to admitted and completed calls.'
        }
        Key @(0x20)
        Await { Find-Control '*Stopping*waiting for the current call to finish*' } 'stopping state before postcondition release' | Out-Null
        Snapshot "stopping $focusedName"
        New-Item -ItemType File -Path (Join-Path $markers 'fail-postcondition') | Out-Null
        $record.Stage = "review pending compression for $focusedName"
        Await { Find-Control "*Review storage changes for *$focusedName*" 'Button' } 'same-session pending storage review' | Out-Null
        Await { Find-Control '*Result not confirmed*' } 'uncertain result in Operations' | Out-Null
        [void](Take-Speech)
        Key @(0x11, 0x4A)
        Await {
            $focus = $ui::FocusedElement.Current
            $focus.Name -like "*Review storage changes for *$focusedName*" -and !$focus.IsOffscreen
        } 'keyboard focus in the pending storage review' | Out-Null
        Hear "pending review for $focusedName" 'Review storage changes'
        Snapshot "pending $focusedName"
    }
    function Review-Decision {
        $reviewName = "Review decision$([char]0x2026)"
        $review = Await { Find-Control $reviewName 'Button' } 'the operation decision review button'
        Require-Front
        $review.SetFocus()
        Await { $ui::FocusedElement.Current.Name -eq $reviewName } 'operation decision review focus' | Out-Null
        Key @(0x20)
    }
    function Approve-Decryption {
        Tab-To 'Decrypt'
        Key @(0x20)
        Await { Find-Control 'Every selected item passed inspection for this action.' } 'completed decryption inspection' | Out-Null
        [void](Require-Consent $pathConsent $false)
        $decryptConsent = Require-Consent 'I understand the selected files will lose EFS protection*' $false
        if (Find-Control $streamConsent 'CheckBox') { throw 'The encryption acknowledgment stayed visible for decryption.' }
        Require-Footer 'Decrypt' $false
        [void](Take-Speech)
        Tab-To $decryptConsent
        Hear 'decryption acknowledgment' 'selected files will lose EFS protection'
        Key @(0x20)
        [void](Require-Consent $decryptConsent $true)
        Require-Footer 'Decrypt' $false
        Tab-To $pathConsent
        Key @(0x20)
        Require-Footer 'Decrypt' $true
        Snapshot 'decrypt review'
    }
    function Invoke-KeyWizard([string]$button, [bool]$complete) {
        Tab-To $button 'Button'
        Key @(0x20)
        $wizard = Await {
            $front = [StorageReviewInput]::GetForegroundWindow()
            if ([StorageReviewInput]::GetWindow($front, 4) -eq $handle) {
                $element = $ui::FromHandle($front)
                if ($element.Current.ProcessId -eq $app.Id -and $element.Current.Name -eq 'Back up EFS key') { $front }
            }
        } 'the FileCat-owned Windows certificate export wizard'
        $record.WizardParentMatched = $true
        if ([StorageReviewInput]::GetForegroundWindow() -ne $wizard) { throw 'The owned backup wizard lost focus.' }
        if ($complete) {
            $fixturePassword = [guid]::NewGuid().ToString('N') + '!a7'
            [FileCat.NativeAcceptance.WindowsEfsBackupWizardDriver]::Drive($handle, $true, $backupPath, $fixturePassword)
        }
        else {
            [StorageReviewInput]::Send(@(0x1B), $false)
        }
        Await { [StorageReviewInput]::GetForegroundWindow() -eq $handle } 'focus returning from the backup wizard' | Out-Null
        $record.WizardRestoredFocus = $true
        if ($complete) {
            $restored = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
                $backupPath, $fixturePassword, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
            try {
                if (!$restored.HasPrivateKey -or $restored.Thumbprint -ne $fixtureThumbprint) {
                    throw 'The exported PFX did not contain the reviewed certificate and private key.'
                }
            }
            finally { $restored.Dispose() }
            $record.BackupMatched = $true
        }
    }
    function Verify-KeyBackup {
        $setupName = "Set up EFS$([char]0x2026)"
        $backupName = "Back up EFS key$([char]0x2026)"
        $record.Stage = 'accept setup for the existing EFS key'
        Tab-To 'Encrypt with EFS' 'Button'
        [void](Take-Speech)
        Key @(0x20)
        Hear 'pending EFS decision' 'a decision is required'
        Review-Decision
        Await { Find-Control $setupName 'Button' } 'EFS setup decision' | Out-Null
        Await { $ui::FocusedElement.Current.Name -eq 'Cancel operation' } 'setup default cancel focus' | Out-Null
        Hear 'EFS setup' 'Set up EFS|Windows may create or enroll an encryption key'
        Tab-To $setupName
        Key @(0x20)
        $record.Stage = 'open the parented Windows EFS backup wizard'
        Review-Decision
        $certificates = Await { Find-Control 'Reviewed EFS certificate' 'ComboBox' } 'EFS recovery certificate'
        $options = @($certificates.FindAll($scope::Descendants, $all) | ForEach-Object { $_.Current.Name })
        if (($options -join ' ') -notmatch [regex]::Escape($fixtureThumbprint)) {
            throw 'The reviewed certificate does not match the pre-existing fixture key.'
        }
        Invoke-KeyWizard $backupName $VerifyCompletedKeyBackup.IsPresent
        if ($VerifyCompletedKeyBackup) {
            $record.Stage = 'verify encryption after the successful backup'
            Await {
                if ((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::Encrypted) {
                    Find-Control '*Encrypt with EFS*1 changed*0 failed*0 uncertain*'
                }
            } 'terminal encryption and the on-disk EFS attribute' | Out-Null
            Snapshot 'completed encryption result'
            $query = [Diagnostics.ProcessStartInfo]::new()
            $query.FileName = Join-Path $env:WINDIR 'System32\cipher.exe'
            $query.Arguments = '/c "' + $file + '"'
            $query.UseShellExecute = $false
            $query.CreateNoWindow = $true
            $query.RedirectStandardOutput = $true
            $cipher = [Diagnostics.Process]::Start($query)
            try {
                $output = $cipher.StandardOutput.ReadToEndAsync()
                if (!$cipher.WaitForExit(15000)) {
                    $cipher.Kill()
                    throw 'The independent EFS certificate query timed out.'
                }
                $queryExit = $cipher.ExitCode
                $observedKeys = $output.GetAwaiter().GetResult()
            }
            finally { $cipher.Dispose() }
            if ($queryExit -ne 0 -or ($observedKeys -replace '\s','') -notmatch [regex]::Escape($fixtureThumbprint)) {
                throw 'The encrypted file does not report the exported fixture certificate.'
            }
            if ((Get-FileHash -LiteralPath $file).Hash -ne $initialHash -or
                (Get-Content -LiteralPath $file -Stream 'proof' -Raw) -cne $streamText) {
                throw 'Encryption changed the main or alternate stream bytes.'
            }
            $record.EncryptedStreamsVerified = $true
            $record.Stage = 'verify decryption through the FileCat command'
            Show-Command 'EFS protection' '*Windows file encryption (EFS)*'
            Approve-Decryption
            Tab-To 'Decrypt' 'Button'
            Key @(0x20)
            Await {
                if (!((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::Encrypted)) {
                    Find-Control '*Decrypt EFS*1 changed*0 failed*0 uncertain*'
                }
            } 'terminal decryption and clear on-disk attributes' | Out-Null
            Snapshot 'completed decryption result'
            return
        }
        Review-Decision
        Await { Find-Control 'Windows canceled the backup.*' } 'canceled backup retry text' | Out-Null
        Hear 'EFS recovery after wizard cancellation' 'Choose EFS recovery|Windows canceled the backup'
        Snapshot 'recovery after wizard cancellation'
        Tab-To 'Cancel operation'
        Key @(0x20)
        Await { !(Find-Control 'Reviewed EFS certificate' 'ComboBox') } 'recovery dismissal' | Out-Null
        Await { Find-Control '*0 changed*Stopped; completed changes remain.*' } 'the canceled operation result' | Out-Null
        Snapshot 'canceled encryption result'
    }
    function Verify-SelectedFileKeyBackup {
        $certificate = 'Selected file EFS certificate'
        $backupName = 'Back up the selected EFS certificate and private key'
        function Read-ReturnedResult([string]$label, [string]$message) {
            $picker = Await { Find-Control $certificate 'ComboBox' } 'the reopened certificate picker'
            if ($picker.Current.HelpText -notlike "$message*") { throw 'The certificate picker does not describe the current backup result.' }
            # A native foreground transition can interrupt a live announcement. Verify that
            # keyboard users can hear the result on demand by returning to the certificate.
            Tab-To 'Done' 'Button'
            [void](Take-Speech)
            Tab-To $certificate 'ComboBox'
            Hear $label ([regex]::Escape($message))
        }
        $record.Stage = 'review the selected file certificate'
        Show-Command 'Back up EFS key' 'Back up EFS key*'
        Await { $ui::FocusedElement.Current.Name -eq $certificate } 'selected-file certificate focus' | Out-Null
        Hear 'selected-file key dialog' 'Selected file EFS certificate|Back up EFS key'
        $backup = Find-Control $backupName 'Button'
        if (!$backup -or $backup.Current.IsOffscreen -or $backup.Current.IsEnabled) {
            throw 'The selected-file backup action must be visible and disabled until a certificate is selected.'
        }
        Snapshot 'before certificate choice'
        Key @(0x28) $true
        Await { (Find-Control $backupName 'Button').Current.IsEnabled } 'explicit certificate choice' | Out-Null

        $record.Stage = 'cancel the selected-file key export'
        Invoke-KeyWizard $backupName $false
        Await { Find-Control 'Windows canceled this key export.' } 'the canceled selected-file key result' | Out-Null
        Read-ReturnedResult 'canceled selected-file key result' 'Windows canceled this key export.'
        if (Test-Path -LiteralPath $backupPath) { throw 'Cancellation left a PFX.' }
        Snapshot 'canceled selected-file backup'
        $record.CanceledWithoutArtifact = $true

        $record.Stage = 'complete the selected-file key export'
        Invoke-KeyWizard $backupName $true
        Await { Find-Control 'Windows reported private-key export completed. 1 of the 1 selected items still matched this key when checked.' } 'the selected-file coverage result' | Out-Null
        Read-ReturnedResult 'selected-file export result' 'Windows reported private-key export completed.'
        $fileDetails = 'Selected items, original locations and EFS certificate associations'
        $files = Find-Control $fileDetails 'Edit'
        $value = $files.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($value -notmatch 'alpha-review\.txt' -or $value -notmatch 'Matched the exported key when checked') {
            throw 'The completed review did not identify the original selected file as covered.'
        }
        $record.SelectedFileCoverage = 1
        Snapshot 'completed selected-file backup'
        [void](Take-Speech)
        Tab-To $fileDetails 'Edit'
        Hear 'spoken selected-file coverage' 'Matched the exported key when checked'
        [void](Take-Speech)
        Tab-To 'Done' 'Button'
        Key @(0x20)
        Await { !(Find-Control $certificate 'ComboBox') } 'selected-file key review dismissal' | Out-Null
        Hear 'focus after selected-file backup' 'alpha-review\.txt'
    }
    $fixture = Join-Path $testRoot ('session-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $record.Fixture = Split-Path -Leaf $fixture
    $left = Join-Path $fixture 'left-review-long-path-for-storage-inspection'
    $right = Join-Path $fixture 'right'
    $state = Join-Path $fixture 'state'
    $nvdaConfig = Join-Path $fixture 'nvda'
    $speechPath = Join-Path $fixture 'nvda.log'
    $script:speechIndex = 0
    New-Item -ItemType Directory -Path $left,$right,$state,$nvdaConfig -Force | Out-Null
    if ($HostedArm64) {
        $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
        $access = [Security.AccessControl.DirectorySecurity]::new()
        $access.SetOwner($owner)
        $access.SetAccessRuleProtection($true, $false)
        foreach ($principal in $owner,$system) {
            $access.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                $principal, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        }
        Set-Acl -LiteralPath $state -AclObject $access
    }
    if ($HighContrastPictures -or $VerifyOperationsRecovery) {
        [IO.File]::WriteAllText((Join-Path $state 'settings.json'),
            '{"SchemaVersion":1,"Theme":"HighContrast","ThemeAnimations":false,"RowDensity":"Comfortable","FontSize":24,"ContentFontSize":24}')
    }
    $record.HighContrastPictures = [bool]$HighContrastPictures
    $record.VerifyDuplicateRoots = [bool]$VerifyDuplicateRoots
    $record.VerifyOperationsRecovery = [bool]$VerifyOperationsRecovery
    if ($VerifyAlreadySatisfied) { $record.AlreadySatisfied = @{} }
    [IO.File]::WriteAllText((Join-Path $nvdaConfig 'nvda.ini'), "[general]`r`nshowWelcomeDialogAtStartup = False`r`n[update]`r`nautoCheck = False`r`nstartupNotification = False`r`nallowUsageStats = False`r`naskedAllowUsageStats = True`r`n")
    $file = Join-Path $left 'alpha-review.txt'
    [IO.File]::WriteAllText($file, ('FileCat storage review fixture. ' * 512))
    $initialHash = (Get-FileHash -LiteralPath $file).Hash
    $listArgument = @()
    $expectedFileRows = 1
    $operationsArgument = @()
    if ($VerifyOperationsRecovery) {
        $olderMarker = Join-Path $state 'first-postcondition'
        $currentMarker = Join-Path $state 'second-postcondition'
        New-Item -ItemType Directory -Path $olderMarker,$currentMarker | Out-Null
        $operationsArgument = @('--ntfs-storage-postcondition-fixture', "`"$olderMarker`"")
        $secondFile = Join-Path $right 'beta-operations-review.txt'
        [IO.File]::WriteAllText($secondFile, ('Separate current storage-operations fixture. ' * 512))
        $secondHash = (Get-FileHash -LiteralPath $secondFile).Hash
    }
    if ($VerifyDuplicateRoots) {
        $duplicate = Join-Path $right 'alpha-review.txt'
        [IO.File]::WriteAllText($duplicate, 'Distinct payload in the second original.')
        $duplicateHash = (Get-FileHash -LiteralPath $duplicate).Hash
        $selectionList = Join-Path $fixture 'originals.txt'
        [IO.File]::WriteAllLines($selectionList, @($file,$duplicate))
        $listArgument = @('--list', "`"$selectionList`"")
        $expectedFileRows = 2
        $record.ReviewedOriginals = @{}
    }
    if ($VerifyCompletedKeyBackup -or $VerifySelectedFileBackup -or $VerifyAlreadySatisfied) {
        $streamText = 'FileCat alternate stream for the EFS fixture.'
        Set-Content -LiteralPath $file -Stream 'proof' -Value $streamText -NoNewline -Encoding ASCII
    }
    if ($VerifyCompletedKeyBackup -or $VerifySelectedFileBackup) {
        $backupPath = Join-Path $fixture 'fixture-key.pfx'
    }
    if ($VerifySelectedFileBackup) { [IO.File]::Encrypt($file) }
    $record.Stage = 'launch'
    if (Get-Process dotnet,testhost,FileCat,FileCat.ShellHost,FileCat.App.Tests,FileCat.*QualificationHost,FileCat.*CrashHarness,nvda -ErrorAction SilentlyContinue) {
        throw 'A test process started before the qualification instance could launch.'
    }
    $exe = Join-Path $testRoot $(if ($ReleaseBuild) { 'app\FileCat.exe' } else { 'app\FileCat.ArchiveQualificationHost.exe' })
    $record.ReleaseBuild = [bool]$ReleaseBuild
    $record.FileCatDllSHA256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'FileCat.dll')).Hash
    $record.WindowsDllSHA256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'FileCat.Platform.Windows.dll')).Hash
    $record.NvdaVersion = (Get-Item -LiteralPath $nvdaPath).VersionInfo.ProductVersion
    $qualification = if ($ReleaseBuild) { @() } elseif ($HostedArm64 -or $localArm64) { @('--ntfs-storage-arm64-review') } else { @('--ntfs-storage-review') }
    $applicationArguments = $qualification + @(
        '--new-instance', '--reset-layout', '--data', "`"$state`"", '--left', "`"$left`"", '--right', "`"$right`"") +
        $listArgument + $operationsArgument
    $app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru -ArgumentList $applicationArguments
    $main = Await {
        $app.Refresh()
        if ($app.HasExited) { throw "The qualification instance exited with $($app.ExitCode)." }
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $ui::FromHandle($app.MainWindowHandle) }
    } 'the qualification window' 90 # a cold start on the Windows on Arm VM took over 30 s
    $list = Await {
        # A window taken early in a slow cold start can stay an empty shell to UI Automation; take it again each time.
        $app.Refresh()
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $script:main = $ui::FromHandle($app.MainWindowHandle) }
        $main.FindAll($scope::Descendants, $all) | Where-Object {
            $_.Current.ClassName -eq 'FileList' -and
                @($_.FindAll($scope::Children, $all) | Where-Object { $_.Current.Name -eq 'alpha-review.txt' }).Count -eq $expectedFileRows
        } | Select-Object -First 1
    } 'the populated source list' 60
    $handle = [IntPtr]$main.Current.NativeWindowHandle
    [void][StorageReviewInput]::SetForegroundWindow($handle)
    if (![StorageReviewInput]::ResizeClient($handle, 640, 420)) { throw 'Could not resize the owned window.' }
    $record.ClientDip = Await {
        $size = [StorageReviewInput]::ClientDip($handle)
        if ($size.Length -eq 2 -and $size[0] -ge 640 -and $size[0] -le 644 -and $size[1] -ge 420 -and $size[1] -le 424) { $size }
    } 'the 640 by 420 DIP client'
    $record.Dpi = [StorageReviewInput]::GetDpiForWindow($handle)
    $record.Foreground = [ordered]@{
        AppProcess = $app.Id
        ActiveConsoleSession = [StorageReviewInput]::WTSGetActiveConsoleSessionId()
        InputDesktopError = [StorageReviewInput]::InputDesktopError()
        BeforeNvda = [StorageReviewInput]::ForegroundProcess()
        AfterNvda = $null
        ActivationRequested = $null
    }
    $record.Stage = 'launch NVDA'
    $list.SetFocus()
    $reader = Start-Process -FilePath $nvdaPath -WindowStyle Hidden -PassThru -ArgumentList @(
        "--log-file=`"$speechPath`"", '--log-level=12', "--config-path=`"$nvdaConfig`"", '--disable-addons')
    Await {
        $reader.Refresh()
        if ($reader.HasExited) { throw "NVDA exited with $($reader.ExitCode) during startup." }
        Select-String -LiteralPath $speechPath -SimpleMatch 'NVDA initialized' -Quiet -ErrorAction SilentlyContinue
    } 'NVDA startup' 120 | Out-Null # NVDA's x64 build runs emulated on Windows on Arm
    $record.Foreground.AfterNvda = [StorageReviewInput]::ForegroundProcess()
    if ([StorageReviewInput]::GetForegroundWindow() -ne $handle) {
        $record.Foreground.ActivationRequested = [StorageReviewInput]::SetForegroundWindow($handle)
    }
    if ([StorageReviewInput]::GetForegroundWindow() -ne $handle) {
        if (![StorageReviewInput]::ClickUncoveredTitle($handle)) { throw 'A foreground window covers the owned FileCat fixture.' }
        Await { [StorageReviewInput]::GetForegroundWindow() -eq $handle } 'foreground FileCat after NVDA startup' | Out-Null
    }
    $list.SetFocus()
    Key @(0x2D, 0x09)
    # Emulated NVDA on Windows on Arm sometimes speaks the window's title alone, without its role.
    Hear 'active FileCat window' "'FileCat [^']{0,200}'|FileCat.{0,180}window|Panel 1, source"
    Key @(0x24) $true
    Key @(0x28) $true
    Hear 'focused file' 'alpha-review\.txt'
    if ($VerifyOperationsRecovery) {
        Invoke-StorageStop $olderMarker 'alpha-review.txt' $true
        $review = Find-Control '*Review storage changes for *alpha-review.txt*' 'Button'
        Tab-To $review.Current.Name 'Button'
        Key @(0x20)
        $report = Find-Report 'Storage history snapshot'
        Acknowledge-PendingReport $report
        $record.Stage = 'acknowledge the first uncertain history'
        Await { Find-Control 'Acknowledge reviewed history' 'Button' } 'exact-history acknowledgment prompt' | Out-Null
        [void][StorageReviewInput]::SetForegroundWindow($handle)
        Tab-To 'Acknowledge reviewed history' 'Button'
        Key @(0x20)
        Await { Find-Control '*Reviewed; effect remains uncertain*' 'ListItem' } 'first reviewed uncertainty' | Out-Null
        $record.Stage = 'restart to load retained history'
        [void]$app.CloseMainWindow()
        if (!$app.WaitForExit(10000)) { throw 'The first qualification instance did not close.' }
        $applicationArguments = $qualification + @('--new-instance', '--reset-layout',
            '--data', "`"$state`"", '--left', "`"$right`"", '--right', "`"$left`"",
            '--ntfs-storage-postcondition-fixture', "`"$currentMarker`"")
        $app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru -ArgumentList $applicationArguments
        $main = Await {
            $app.Refresh()
            if ($app.HasExited) { throw "The restarted instance exited with $($app.ExitCode)." }
            if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $ui::FromHandle($app.MainWindowHandle) }
        } 'the restarted qualification window' 90
        $handle = [IntPtr]$main.Current.NativeWindowHandle
        [void][StorageReviewInput]::SetForegroundWindow($handle)
        if (![StorageReviewInput]::ResizeClient($handle, 640, 420)) { throw 'Could not resize the restarted window.' }
        $record.Stage = 'verify the earlier acknowledgment after restart'
        $earlier = Await { Find-Control 'Review storage changes for Storage changes*' 'Button' } 'the startup history candidate'
        Key @(0x11, 0x4A)
        Tab-To $earlier.Current.Name 'Button'
        Key @(0x20)
        $report = Find-Report 'Storage history snapshot'
        $report.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Await {
            !(Find-Control 'Review storage changes for Storage changes*' 'Button') -and
                (Find-Control '*Reviewed; effect remains uncertain*' 'ListItem')
        } 'the earlier acknowledgment verified against its journal' | Out-Null
        $second = Await { Find-Control 'beta-operations-review.txt' 'ListItem' } 'the second selected file'
        $second.SetFocus()
        Invoke-StorageStop $currentMarker 'beta-operations-review.txt'
        $record.Stage = 'reopen the earlier reviewed uncertainty'
        $retained = Await { Find-Control '*Reviewed; effect remains uncertain*' 'ListItem' } 'retained storage history row'
        [void](Take-Speech)
        Tab-To $retained.Current.Name 'ListItem'
        Hear 'retained storage history row' 'Reviewed; effect remains uncertain'
        Key @(0x20)
        Await {
            $row = Find-Control '*Reviewed; effect remains uncertain*' 'ListItem'
            if ($row) { $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected }
        } 'the selected retained storage history row' | Out-Null
        Key @(0x0D)
        $report = Find-Report 'Retained storage review'
        Await {
            $report.FindAll($scope::Descendants, $all) | Where-Object {
                $_.Current.Name -like '*Reviewed; effect remains uncertain*'
            } | Select-Object -First 1
        } 'the retained uncertainty statement' | Out-Null
        $report.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        if (!(Find-Control '*Review storage changes for *beta-operations-review.txt*' 'Button') -or
            ((Get-FileHash -LiteralPath $file).Hash -ne $initialHash) -or
            ((Get-FileHash -LiteralPath $secondFile).Hash -ne $secondHash) -or
            !(([IO.File]::GetAttributes($file)) -band [IO.FileAttributes]::Compressed) -or
            !(([IO.File]::GetAttributes($secondFile)) -band [IO.FileAttributes]::Compressed)) {
            throw 'Reviewing the older history must leave both payloads, compression, and the new pending review intact.'
        }
        $record.Stage = 'complete'
        $record.Passed = $true
        return
    }
    if ($VerifyDuplicateRoots) {
        $record.Stage = 'mark both originals'
        Key @(0x11, 0x10, 0x41)
        Await { $list.Current.HelpText -match '2 marked' } 'both original items to be marked' | Out-Null
    }
    if ($HighContrastPictures) {
        $record.Stage = 'enter picture view'
        Show-Command 'Show or hide pictures' 'Pictures: thumbnail grid in this tab.'
        $focused = $ui::FocusedElement.Current
        if ($focused.Name -notmatch 'alpha-review\.txt' -and $focused.ClassName -ne 'FileList') {
            throw 'Opening the picture grid did not return focus to the file pane.'
        }
    }
    if ($VerifySelectedFileBackup) {
        Verify-SelectedFileKeyBackup
        $attributes = (Get-Item -LiteralPath $file).Attributes
        if (!($attributes -band [IO.FileAttributes]::Encrypted) -or
            (Get-FileHash -LiteralPath $file).Hash -ne $initialHash -or
            (Get-Content -LiteralPath $file -Stream 'proof' -Raw) -cne $streamText) {
            throw 'The selected-file backup changed the original EFS state or stream bytes.'
        }
        $record.EncryptedStreamsVerified = $true
        $record.Stage = 'complete'
        $record.Passed = $true
        return
    }
    $record.Stage = 'open EFS review'
    Show-Command 'EFS protection' '*Windows file encryption (EFS)*'
    Hear 'EFS review' 'Windows file encryption'
    Require-Footer 'Choose action' $false
    Snapshot 'initial EFS review'
    Tab-To 'Encrypt with EFS'
    Key @(0x20)
    Await { Find-Control 'Every selected item passed inspection for this action.' } 'completed EFS inspection' | Out-Null
    $pathConsent = Require-Consent 'I accept this concurrent link-change limitation*' $false
    $streamConsent = Require-Consent 'I allow Windows to remove compression from all data streams*' $false
    Require-Footer 'Encrypt with EFS' $false
    [void](Take-Speech)
    Tab-To $pathConsent
    Hear 'pathname acknowledgment' 'concurrent link-change limitation'
    Key @(0x20)
    [void](Require-Consent $pathConsent $true)
    Require-Footer 'Encrypt with EFS' $false
    [void](Take-Speech)
    Tab-To $streamConsent
    Hear 'all-stream decompression acknowledgment' 'remove compression from all data streams'
    Key @(0x20)
    Require-Footer 'Encrypt with EFS' $true
    Snapshot 'encrypt review'
    if ($VerifyCanceledKeyBackup -or $VerifyCompletedKeyBackup) {
        Verify-KeyBackup
    }
    else {
        Approve-Decryption
        if ($VerifyDuplicateRoots) { Verify-OriginalLocations 'EFS' }
        if ($VerifyAlreadySatisfied) { Complete-AlreadySatisfied 'Decrypt' 'Decrypt EFS' }
        else {
            [void](Take-Speech)
            Key @(0x1B)
            Await { !(Find-Control '*Windows file encryption (EFS)*') } 'EFS review dismissal' | Out-Null
            Hear 'focus after dismissal' 'alpha-review\.txt'
        }
        $record.Stage = 'open compression review'
        Show-Command 'NTFS compression' 'NTFS compression*'
        Hear 'compression review' 'NTFS compression'
        Tab-To 'Compress'
        Key @(0x20)
        Await { Find-Control 'Every selected item passed inspection for this action.' } 'completed compression inspection' | Out-Null
        Require-Footer 'Compress' $true
        Tab-To 'Remove compression'
        Key @(0x20)
        Await { (Find-Control 'Remove compression' 'Button').Current.IsEnabled } 'removal footer state' | Out-Null
        Require-Footer 'Remove compression' $true
        Snapshot 'compression review'
        if ($VerifyDuplicateRoots) { Verify-OriginalLocations 'Compression' }
        if ($VerifyAlreadySatisfied) { Complete-AlreadySatisfied 'Remove compression' 'Remove compression' }
        else {
            [void](Take-Speech)
            Key @(0x1B)
            Await { !(Find-Control 'NTFS compression*') } 'compression review dismissal' | Out-Null
            Hear 'focus after compression dismissal' 'alpha-review\.txt'
        }
    }
    if ((Get-FileHash -LiteralPath $file).Hash -ne $initialHash -or
        ((Get-Item -LiteralPath $file).Attributes -band ([IO.FileAttributes]::Encrypted -bor [IO.FileAttributes]::Compressed))) {
        throw 'The fixture did not finish with its original content and clear attributes.'
    }
    if ($VerifyDuplicateRoots -and ((Get-FileHash -LiteralPath $duplicate).Hash -ne $duplicateHash -or
        ((Get-Item -LiteralPath $duplicate).Attributes -band ([IO.FileAttributes]::Encrypted -bor [IO.FileAttributes]::Compressed)))) {
        throw 'The second original did not finish with its initial content and clear attributes.'
    }
    if ($VerifyCompletedKeyBackup) {
        if ((Get-Content -LiteralPath $file -Stream 'proof' -Raw) -cne $streamText) { throw 'Decryption changed the alternate stream bytes.' }
        $record.DecryptedStreamsVerified = $true
    }
    if ($VerifyAlreadySatisfied -and (Get-Content -LiteralPath $file -Stream 'proof' -Raw) -cne $streamText) {
        throw 'The already-satisfied operation changed the alternate stream bytes.'
    }
    $record.Stage = 'complete'
    $record.Passed = $true
}
catch {
    $record.Error = $_.Exception.Message
    if ($main) { Snapshot 'failure' }
    throw
}
finally {
    if ($app -and !$app.HasExited) { [void]$app.CloseMainWindow(); if (!$app.WaitForExit(4000)) { $app.Kill() } }
    if ($reader -and !$reader.HasExited) { $reader.Kill(); [void]$reader.WaitForExit(4000) }
    if ($previousDpiContext -ne [IntPtr]::Zero) {
        [void][StorageReviewInput]::SetThreadDpiAwarenessContext($previousDpiContext)
    }
    try {
        if ($backupPath -and (Test-Path -LiteralPath $backupPath)) {
            Remove-Item -LiteralPath $backupPath
            $record.BackupArtifactRemoved = !(Test-Path -LiteralPath $backupPath)
        }
    }
    catch { $record.Passed = $false; $record.Error = 'Could not remove the disposable PFX.'; throw }
    finally {
        $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $log -Encoding UTF8
    }
}
