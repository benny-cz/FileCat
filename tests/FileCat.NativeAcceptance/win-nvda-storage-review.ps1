param(
    [Parameter(Mandatory)][string]$TestRoot,
    [Parameter(Mandatory)][string]$NvdaPath,
    [switch]$VerifyCanceledKeyBackup
)

$ErrorActionPreference = 'Stop'
$testRoot = [IO.Path]::GetFullPath($TestRoot)
$validationRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation')) + '\'
$nvdaPath = [IO.Path]::GetFullPath($NvdaPath)
if (!(Get-Process -Id $PID).SessionId -or
    (Get-CimInstance Win32_ComputerSystem).Manufacturer -notmatch 'VMware' -or
    !$testRoot.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    !$nvdaPath.StartsWith($validationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    (Get-AuthenticodeSignature -LiteralPath $nvdaPath).Status -ne 'Valid') {
    throw 'Run the owned storage UI fixture with signed NVDA in an interactive VMware test profile.'
}
$log = Join-Path $testRoot 'storage-review-result.json'
if (Test-Path -LiteralPath $log) { throw 'Use a fresh test root to preserve prior receipts.' }
$record = [ordered]@{ Passed = $false; Stage = 'process preflight'; Error = $null; Controls = @{}; Speech = @{} }
$app = $reader = $null
try {
    if (Get-Process dotnet,testhost,FileCat,FileCat.App.Tests,FileCat.*QualificationHost,FileCat.*CrashHarness,nvda -ErrorAction SilentlyContinue) {
        throw 'A test process is already using this VM.'
    }
    if ($VerifyCanceledKeyBackup) {
        $record.Stage = 'verify existing fixture key'
        $currentKey = & (Join-Path $env:WINDIR 'System32\cipher.exe') /y | Out-String
        $keyLine = [regex]::Match($currentKey, '(?im)^[ \t]*(?:[a-f0-9]{4}[ \t]*){10}\r?$')
        if ($LASTEXITCODE -ne 0 -or !$keyLine.Success) {
            throw 'The backup cancellation fixture needs a pre-existing default EFS certificate.'
        }
        $fixtureThumbprint = $keyLine.Value -replace '\s', ''
        if (!(Get-Item -LiteralPath "Cert:\CurrentUser\My\$fixtureThumbprint").HasPrivateKey) {
            throw 'The existing fixture certificate does not have its private key.'
        }
    }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StorageReviewInput {
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect bounds);
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
}
'@
    $ui = [System.Windows.Automation.AutomationElement]
    $scope = [System.Windows.Automation.TreeScope]
    $all = [System.Windows.Automation.Condition]::TrueCondition
    function Await([scriptblock]$probe, [string]$description) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while ($watch.Elapsed.TotalSeconds -lt 18) {
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
        [void](Take-Speech)
        Key @(0x0D)
        Await { Find-Control $title } $title | Out-Null
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
    function Review-Decision {
        $reviewName = "Review decision$([char]0x2026)"
        $review = Await { Find-Control $reviewName 'Button' } 'the operation decision review button'
        Require-Front
        $review.SetFocus()
        Await { $ui::FocusedElement.Current.Name -eq $reviewName } 'operation decision review focus' | Out-Null
        Key @(0x20)
    }
    function Verify-KeyBackupCancellation {
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
        $record.Stage = 'cancel the parented Windows EFS backup wizard'
        Review-Decision
        $certificates = Await { Find-Control 'Reviewed EFS certificate' 'ComboBox' } 'EFS recovery certificate'
        $options = @($certificates.FindAll($scope::Descendants, $all) | ForEach-Object { $_.Current.Name })
        if (($options -join ' ') -notmatch [regex]::Escape($fixtureThumbprint)) {
            throw 'The reviewed certificate does not match the pre-existing fixture key.'
        }
        Tab-To $backupName
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
        [StorageReviewInput]::Send(@(0x1B), $false)
        Await { [StorageReviewInput]::GetForegroundWindow() -eq $handle } 'focus returning from the backup wizard' | Out-Null
        $record.WizardRestoredFocus = $true
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
    $fixture = Join-Path $testRoot ('session-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $record.Fixture = Split-Path -Leaf $fixture
    $left = Join-Path $fixture 'left-review-long-path-for-storage-inspection'
    $right = Join-Path $fixture 'right'
    $state = Join-Path $fixture 'state'
    $nvdaConfig = Join-Path $fixture 'nvda'
    $speechPath = Join-Path $fixture 'nvda.log'
    $script:speechIndex = 0
    New-Item -ItemType Directory -Path $left,$right,$state,$nvdaConfig -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $nvdaConfig 'nvda.ini'), "[general]`r`nshowWelcomeDialogAtStartup = False`r`n[update]`r`nautoCheck = False`r`nstartupNotification = False`r`nallowUsageStats = False`r`naskedAllowUsageStats = True`r`n")
    $file = Join-Path $left 'alpha-review.txt'
    [IO.File]::WriteAllText($file, ('FileCat storage review fixture. ' * 512))
    $initialHash = (Get-FileHash -LiteralPath $file).Hash
    $record.Stage = 'launch'
    if (Get-Process dotnet,testhost,FileCat,FileCat.App.Tests,FileCat.*QualificationHost,FileCat.*CrashHarness,nvda -ErrorAction SilentlyContinue) {
        throw 'A test process started before the qualification instance could launch.'
    }
    $exe = Join-Path $testRoot 'app\FileCat.ArchiveQualificationHost.exe'
    $record.FileCatDllSHA256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'FileCat.dll')).Hash
    $record.WindowsDllSHA256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'FileCat.Platform.Windows.dll')).Hash
    $record.NvdaVersion = (Get-Item -LiteralPath $nvdaPath).VersionInfo.ProductVersion
    $app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru -ArgumentList @(
        '--ntfs-storage-review', '--new-instance', '--reset-layout', '--data', "`"$state`"", '--left', "`"$left`"", '--right', "`"$right`"")
    $main = Await {
        $app.Refresh()
        if ($app.HasExited) { throw "The qualification instance exited with $($app.ExitCode)." }
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $ui::FromHandle($app.MainWindowHandle) }
    } 'the qualification window'
    $list = Await {
        $main.FindAll($scope::Descendants, $all) | Where-Object {
            $_.Current.ClassName -eq 'FileList' -and ($_.FindAll($scope::Children, $all) | Where-Object { $_.Current.Name -eq 'alpha-review.txt' })
        } | Select-Object -First 1
    } 'the populated source list'
    $handle = [IntPtr]$main.Current.NativeWindowHandle
    [void][StorageReviewInput]::SetForegroundWindow($handle)
    if (![StorageReviewInput]::ResizeClient($handle, 640, 420)) { throw 'Could not resize the owned window.' }
    $record.ClientDip = Await {
        $size = [StorageReviewInput]::ClientDip($handle)
        if ($size.Length -eq 2 -and $size[0] -ge 640 -and $size[0] -le 644 -and $size[1] -ge 420 -and $size[1] -le 424) { $size }
    } 'the 640 by 420 DIP client'
    $record.Dpi = [StorageReviewInput]::GetDpiForWindow($handle)
    $record.Stage = 'launch NVDA'
    $list.SetFocus()
    $reader = Start-Process -FilePath $nvdaPath -WindowStyle Hidden -PassThru -ArgumentList @(
        "--log-file=`"$speechPath`"", '--log-level=12', "--config-path=`"$nvdaConfig`"", '--disable-addons')
    Await { Select-String -LiteralPath $speechPath -SimpleMatch 'NVDA initialized' -Quiet -ErrorAction SilentlyContinue } 'NVDA startup' | Out-Null
    Hear 'active FileCat window' 'FileCat.{0,180}window|Panel 1, source'
    Key @(0x24) $true
    Key @(0x28) $true
    Hear 'focused file' 'alpha-review\.txt'
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
    if ($VerifyCanceledKeyBackup) {
        Verify-KeyBackupCancellation
    }
    else {
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
        [void](Take-Speech)
        Key @(0x1B)
        Await { !(Find-Control '*Windows file encryption (EFS)*') } 'EFS review dismissal' | Out-Null
        Hear 'focus after dismissal' 'alpha-review\.txt'
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
        [void](Take-Speech)
        Key @(0x1B)
        Await { !(Find-Control 'NTFS compression*') } 'compression review dismissal' | Out-Null
        Hear 'focus after compression dismissal' 'alpha-review\.txt'
    }
    if ((Get-FileHash -LiteralPath $file).Hash -ne $initialHash -or
        ((Get-Item -LiteralPath $file).Attributes -band ([IO.FileAttributes]::Encrypted -bor [IO.FileAttributes]::Compressed))) {
        throw 'The review changed the fixture without approval.'
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
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $log -Encoding UTF8
}
