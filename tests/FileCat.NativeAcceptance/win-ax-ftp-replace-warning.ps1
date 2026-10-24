param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$PythonHelperPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('ftp', 'ftpes', 'ftps', 'sftp')][string]$Protocol = 'ftp',
    [string]$SftpFixtureId,
    [string]$ExpectedHostFingerprints,
    [string]$SftpPassword,
    [string]$NvdaPath
)

# Interactive Windows VM only. Use a fresh loopback FTP/FTPS server or an
# independently seeded Ubuntu SFTP target; cancel or skip before replacement.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatFtpWarningSize {
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static uint ForegroundProcessId() {
        uint processId;
        GetWindowThreadProcessId(GetForegroundWindow(), out processId);
        return processId;
    }
    public static void PressNavigation(byte vk) {
        byte scan = (byte)MapVirtualKey(vk, 0);
        keybd_event(vk, scan, 1u, UIntPtr.Zero);
        keybd_event(vk, scan, 3u, UIntPtr.Zero);
        System.Threading.Thread.Sleep(90);
    }
    public static void ClickTitle(IntPtr handle) {
        int screenWidth = GetSystemMetrics(0), screenHeight = GetSystemMetrics(1);
        for (int attempt = 0; attempt < 5; attempt++) {
            RECT r;
            GetWindowRect(handle, out r);
            POINT p = new POINT { X = (r.Left + r.Right) / 2, Y = r.Top + 12 };
            if (GetAncestor(WindowFromPoint(p), 2) == handle) {
                SetCursorPos(p.X, p.Y);
                mouse_event(2, 0, 0, 0, 0);
                mouse_event(4, 0, 0, 0, 0);
                return;
            }
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            int[,] corners = { { 0, 0 }, { 0, screenHeight - height - 60 },
                { screenWidth - width, screenHeight - height - 60 },
                { screenWidth - width, 0 }, { 0, 0 } };
            MoveWindow(handle, Math.Max(0, corners[attempt, 0]), Math.Max(0, corners[attempt, 1]), width, height, true);
            System.Threading.Thread.Sleep(300);
        }
        throw new InvalidOperationException("Another window covers every title-bar position of the owned app.");
    }
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation.TrimEnd('\') + '\'
$app = [IO.Path]::GetFullPath($AppPath)
$helper = [IO.Path]::GetFullPath($PythonHelperPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if ([IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    [IO.Path]::GetFileName($helper) -ine 'win-ftp-owned-stage.py' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    @(@($app, $helper) | Where-Object {
        !$_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath $_ -PathType Leaf)
    }).Count -gt 0) {
    throw 'Use the isolated Git-built app, copied helper and guest-local report.'
}
if ($Protocol -eq 'sftp' -and ($SftpFixtureId -cnotmatch '^[0-9a-f]{32}$' -or
    [string]::IsNullOrWhiteSpace($ExpectedHostFingerprints) -or
    [string]::IsNullOrEmpty($SftpPassword))) {
    throw 'SFTP requires an exact owned Ubuntu fixture ID, checked host fingerprints and a test credential.'
}
$nvda = $null
if ($NvdaPath) {
    $nvda = [IO.Path]::GetFullPath($NvdaPath)
    if (!$nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
        !(Test-Path -LiteralPath $nvda -PathType Leaf) -or
        (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid') {
        throw 'Use a signed portable NVDA copy below FileCatValidation.'
    }
    if (Get-Process -Name nvda -ErrorAction SilentlyContinue) {
        throw 'Another NVDA instance is running; the warning test must own the speech log.'
    }
}

$root = Join-Path $validation ('FtpReplaceReview-' + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $root 'source'
$serverRoot = Join-Path $root 'server'
$data = Join-Path $root 'data'
$ready = Join-Path $root 'server.ready'
$serverError = Join-Path $root 'server.stderr.txt'
New-Item -ItemType Directory -Force -Path $sourceDir, $serverRoot, $data | Out-Null
$source = Join-Path $sourceDir 'target.txt'
$target = Join-Path $serverRoot 'target.txt'
[IO.File]::WriteAllText($source, "owned incoming content`n")
if ($Protocol -ne 'sftp') { [IO.File]::WriteAllText($target, "owned existing remote content`n") }
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$targetHash = if ($Protocol -ne 'sftp') { (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash } else { $null }
$python = (Get-Command python -ErrorAction Stop).Source
$server = $null
$process = $null
$window = $null
$nvdaProcess = $null
$speechLog = Join-Path $root 'nvda.log'
$script:speechOffset = 0L
$result = [ordered]@{
    Passed = $false; Fixture = $root; AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    Protocol = $Protocol; SftpFixtureId = $SftpFixtureId;
    RemoteVerificationRequired = ($Protocol -eq 'sftp'); HostKeyPrompt = $false;
    HostKeyFingerprintMatched = $false; Dpi = 0; Consent = $false; CertificatePrompt = $false;
    CertificateAcceptedOnce = $false; SignedIn = $false; RemoteListed = $false;
    WarningVisible = $false; WarningText = $null; NarrowBounds = $null;
    NvdaVersion = $null; WarningSpoken = $null; WarningSpeech = @();
    WarningClearedForKeepBoth = $false; ConflictWarning = $false;
    ConflictWarningSpoken = $null; ConflictSpeech = @();
    ConflictBounds = $null; ConflictSkipped = $false;
    SourceUnchanged = $false; TargetUnchanged = $(if ($Protocol -eq 'sftp') { $null } else { $false });
    Observed = @(); Error = $null
}

function TakeSpeech {
    if (!(Test-Path -LiteralPath $speechLog)) { return @() }
    $stream = [IO.File]::Open($speechLog, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($script:speechOffset, 'Begin') | Out-Null
        $reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $last = $text.LastIndexOf("`n")
        if ($last -lt 0) { return @() }
        $complete = $text.Substring(0, $last + 1)
        $script:speechOffset += [Text.Encoding]::UTF8.GetByteCount($complete)
        return @($complete -split "`r?`n" | Where-Object { $_.StartsWith('Speaking [') } |
            ForEach-Object { $_.Substring(9).Trim() })
    } finally { $stream.Dispose() }
}
function AwaitWarningSpeech([string]$label) {
    $heard = @()
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt 7) {
        $heard += @(TakeSpeech)
        $spoken = $heard -join ' '
        if ($spoken.Contains("after FileCat's final check") -and
            $spoken.Contains('newer change could be lost') -and
            $spoken.Contains('Choose Keep both')) { return $heard }
        Start-Sleep -Milliseconds 150
    }
    throw "$label was visible but NVDA did not speak the complete final-check risk. Heard: $($heard -join ' | ')"
}

function Snapshot($window) {
    for ($retry = 0; $retry -lt 8; $retry++) {
        try { return @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)) }
        catch [Runtime.InteropServices.COMException] {
            if ($retry -eq 7) { throw }
            Start-Sleep -Milliseconds 150
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            if ($retry -eq 7 -or !$process -or $process.HasExited) { throw }
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            Start-Sleep -Milliseconds 150
        }
    }
}
function Await([scriptblock]$condition, [string]$description, [int]$seconds = 30) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 150
    } while ($clock.Elapsed.TotalSeconds -lt $seconds)
    throw "Timed out waiting for $description."
}
function One($window, $type, [string]$name) {
    return @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq $type -and $_.Current.Name -eq $name -and
        !$_.Current.IsOffscreen
    })[0]
}
function Button($window, [string]$name) {
    return @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -eq $name -and $_.Current.IsEnabled -and !$_.Current.IsOffscreen
    } | Sort-Object { $_.Current.BoundingRectangle.Y })[0]
}
function ClickButton($window, [string]$name) {
    (Await { Button $window $name } "enabled $name button").GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function ActivateOwnedApp {
    [FileCatFtpWarningSize]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 200
    if ([FileCatFtpWarningSize]::ForegroundProcessId() -ne $process.Id) {
        [FileCatFtpWarningSize]::ClickTitle($process.MainWindowHandle)
        Start-Sleep -Milliseconds 300
    }
    if ([FileCatFtpWarningSize]::ForegroundProcessId() -ne $process.Id) {
        throw 'The isolated FileCat process did not become foreground; refusing to send keys.'
    }
}

try {
    if ($nvda) {
        $nvdaConfig = Join-Path $root 'nvda-config'
        New-Item -ItemType Directory -Path $nvdaConfig | Out-Null
        [IO.File]::WriteAllText((Join-Path $nvdaConfig 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
        $nvdaProcess = Start-Process -FilePath $nvda -ArgumentList @(
            "--log-file=$speechLog", '--log-level=12', "--config-path=$nvdaConfig", '--disable-addons') -PassThru
        Await {
            if ($nvdaProcess.HasExited) { throw 'Owned NVDA exited during startup.' }
            (Test-Path -LiteralPath $speechLog) -and
                (Select-String -LiteralPath $speechLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)
        } 'owned NVDA speech logger' 30 | Out-Null
        $result.NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion
    }
    if ($Protocol -eq 'sftp') {
        $address = "sftp://benny@192.168.58.129/home/benny/FileCatValidation/SftpReplaceReview/$SftpFixtureId/"
    } else {
        $serveMode = switch ($Protocol) {
            'ftpes' { 'serve-tls' }
            'ftps' { 'serve-implicit-tls' }
            default { 'serve' }
        }
        $server = Start-Process -FilePath $python -ArgumentList @($helper, $serveMode, $serverRoot, $ready) -PassThru -WindowStyle Hidden -RedirectStandardError $serverError
        $port = Await {
            if ($server.HasExited) { throw 'Owned loopback FTP server exited.' }
            if (Test-Path -LiteralPath $ready -PathType Leaf) {
                $value = (Get-Content -LiteralPath $ready -Raw).Trim()
                if ($value -match '^\d+$') { [int]$value }
            }
        } 'loopback FTP readiness'
        $address = "${Protocol}://tester@127.0.0.1:$port/"
    }
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $sourceDir, '--right', $serverRoot) -PassThru
    Await {
        $process.Refresh()
        !$process.HasExited -and $process.MainWindowHandle -ne [IntPtr]::Zero
    } 'isolated FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'A different executable owns the test window.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $rightLocation = Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
        $_.Current.Name -eq 'Location' -and !$_.Current.IsOffscreen
    } | Sort-Object { $_.Current.BoundingRectangle.X })[-1] } 'right panel Location field'
    ActivateOwnedApp
    $rightLocation.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($address)
    $rightLocation.SetFocus()
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -ne $process.Id) {
        throw 'The owned FileCat Location field did not receive keyboard focus.'
    }
    (New-Object -ComObject WScript.Shell).SendKeys('{ENTER}')
    if ($Protocol -eq 'ftp') {
        $consent = Await { Button $window 'Connect unencrypted' } 'explicit FTP consent' 15
        $consent.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $result.Consent = $true
    }
    $password = Await { One $window ([System.Windows.Automation.ControlType]::Edit) 'Password' } 'remote password field'
    $password.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue(
        $(if ($Protocol -eq 'sftp') { $SftpPassword } else { 'secret-pass' }))
    ClickButton $window 'Connect'
    $result.SignedIn = $true
    if ($Protocol -eq 'sftp') {
        $acceptKey = Await { Button $window 'Connect once' } 'new SFTP host-key decision' 30
        $result.HostKeyPrompt = @((Snapshot $window) | Where-Object {
            $_.Current.Name -eq 'New server key' -and !$_.Current.IsOffscreen
        }).Count -gt 0
        $expected = @($ExpectedHostFingerprints.Split(',') | Where-Object { $_ -match '^SHA256:[A-Za-z0-9+/]{43}$' })
        if ($expected.Count -eq 0) { throw 'No valid independent SSH host-key fingerprint was provided.' }
        $shown = @((Snapshot $window) | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            !$_.Current.IsOffscreen
        } | ForEach-Object { $_.Current.Name })
        $result.HostKeyFingerprintMatched = @($shown | Where-Object { $expected -contains $_ }).Count -gt 0
        if (!$result.HostKeyPrompt -or !$result.HostKeyFingerprintMatched) {
            throw 'The SFTP host-key prompt did not match an independently observed server key.'
        }
        $acceptKey.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    if ($Protocol -in @('ftpes', 'ftps')) {
        $acceptOnce = Await { Button $window 'Connect once' } 'unverified FTPS certificate decision' 30
        $result.CertificatePrompt = @((Snapshot $window) | Where-Object {
            $_.Current.Name -like 'Unverified server certificate*' -and !$_.Current.IsOffscreen
        }).Count -gt 0
        if (!$result.CertificatePrompt) { throw 'The FTPS fixture did not expose its unverified-certificate decision.' }
        $acceptOnce.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $result.CertificateAcceptedOnce = $true
    }
    Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq 'target.txt'
    }).Count -ge 2 } 'source and remote target listing' 60 | Out-Null
    $result.RemoteListed = $true

    $sourceRow = @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq 'target.txt'
    } | Sort-Object { $_.Current.BoundingRectangle.X })[0]
    $sourceRow.SetFocus()
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    ClickButton $window 'Copy'
    $destination = Await { One $window ([System.Windows.Automation.ControlType]::Edit) 'Destination' } 'Copy destination'
    $result.Observed += "Destination: $($destination.Current.Name)"
    $options = Await { Button $window 'Options' } 'transfer options expander'
    $options.SetFocus()
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    (New-Object -ComObject WScript.Shell).SendKeys(' ')
    $conflict = Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox -and
        !$_.Current.IsOffscreen
    } | Sort-Object { $_.Current.BoundingRectangle.Y })[0] } 'conflict policy'
    $conflict.SetFocus()
    if ($nvda) { TakeSpeech | Out-Null }
    [FileCatFtpWarningSize]::PressNavigation(0x24)
    [FileCatFtpWarningSize]::PressNavigation(0x28)
    [FileCatFtpWarningSize]::PressNavigation(0x28)
    $warning = Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.StartsWith("Another app can change this remote file", [StringComparison]::Ordinal) -and
        !$_.Current.IsOffscreen
    })[0] } 'visible remote replacement risk'
    $result.WarningVisible = $true
    $result.WarningText = $warning.Current.Name
    if ($nvda) {
        $result.WarningSpeech = @(AwaitWarningSpeech 'Copy review warning')
        $result.WarningSpoken = $true
    }
    if ($result.WarningText.IndexOf("after FileCat's final check", [StringComparison]::Ordinal) -lt 0 -or
        $result.WarningText.IndexOf('newer change could be lost', [StringComparison]::Ordinal) -lt 0 -or
        $result.WarningText.IndexOf('Choose Keep both', [StringComparison]::Ordinal) -lt 0) {
        throw 'The native warning omitted the residual risk or safer choice.'
    }

    $handle = [IntPtr]$process.MainWindowHandle
    $result.Dpi = [FileCatFtpWarningSize]::GetDpiForWindow($handle)
    $scale = $result.Dpi / 96.0
    if (![FileCatFtpWarningSize]::MoveWindow($handle, 80, 80,
            [int][Math]::Round(720 * $scale), [int][Math]::Round(540 * $scale), $true)) {
        throw 'Could not resize the owned remote replacement review.'
    }
    Start-Sleep -Milliseconds 350
    $frame = $window.Current.BoundingRectangle
    $warnBounds = $warning.Current.BoundingRectangle
    $cancel = Await { Button $window 'Cancel' } 'narrow review Cancel'
    $cancelBounds = $cancel.Current.BoundingRectangle
    $result.NarrowBounds = [ordered]@{ Frame = "$frame"; Warning = "$warnBounds"; Cancel = "$cancelBounds" }
    if ($frame.Width -gt (730 * $scale) -or $warnBounds.Height -le (30 * $scale) -or
        $warnBounds.Left -lt $frame.Left -or $warnBounds.Right -gt $frame.Right -or
        $warnBounds.Top -lt $frame.Top -or $warnBounds.Bottom -gt $frame.Bottom -or
        $cancelBounds.Bottom -gt $frame.Bottom -or $cancelBounds.Right -gt $frame.Right) {
        throw 'The narrow remote replacement review clipped its warning or Cancel action.'
    }
    $conflict.SetFocus()
    [FileCatFtpWarningSize]::PressNavigation(0x24)
    1..4 | ForEach-Object { [FileCatFtpWarningSize]::PressNavigation(0x28) }
    Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.StartsWith("Another app can change this remote file", [StringComparison]::Ordinal) -and
        !$_.Current.IsOffscreen
    }).Count -eq 0 } 'risk warning hidden for Keep both'
    $result.WarningClearedForKeepBoth = $true
    ClickButton $window 'Cancel'

    ClickButton $window 'Copy'
    $secondDestination = Await { One $window ([System.Windows.Automation.ControlType]::Edit) 'Destination' } 'second Copy destination'
    $result.Observed += 'SecondDestination: ' + $secondDestination.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    $result.Observed += @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox
    } | ForEach-Object { 'SecondCombo: ' + $_.Current.Name })
    ClickButton $window 'Copy'
    $reviewDecision = Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name.StartsWith('Review decision', [StringComparison]::Ordinal) -and
        $_.Current.IsEnabled -and !$_.Current.IsOffscreen
    })[0] } 'background operation review action'
    $reviewDecision.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $conflictWarning = Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.StartsWith("Another app can change this remote file", [StringComparison]::Ordinal) -and
        !$_.Current.IsOffscreen
    })[0] } 'per-item remote replacement warning' 15
    $result.ConflictWarning = $true
    if ($nvda) {
        $result.ConflictSpeech = @(AwaitWarningSpeech 'per-item conflict warning')
        $result.ConflictWarningSpoken = $true
    }
    $conflictFrame = $window.Current.BoundingRectangle
    $conflictBounds = $conflictWarning.Current.BoundingRectangle
    $skip = Await { Button $window 'Skip' } 'per-item Skip decision'
    $skipBounds = $skip.Current.BoundingRectangle
    $result.ConflictBounds = [ordered]@{
        Frame = "$conflictFrame"; Warning = "$conflictBounds"; Skip = "$skipBounds"
    }
    if ($conflictBounds.Left -lt $conflictFrame.Left -or
        $conflictBounds.Right -gt $conflictFrame.Right -or
        $conflictBounds.Top -lt $conflictFrame.Top -or
        $conflictBounds.Bottom -gt $conflictFrame.Bottom -or
        $skipBounds.Bottom -gt $conflictFrame.Bottom) {
        throw 'The per-item remote conflict warning or safe Skip choice escaped the narrow window.'
    }
    $skip.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Await { !(Button $window 'Skip') } 'per-item decision closure'
    $result.ConflictSkipped = $true
    $result.SourceUnchanged = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -eq $sourceHash
    if ($Protocol -ne 'sftp') {
        $result.TargetUnchanged = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $targetHash
    }
    if (!$result.SourceUnchanged -or ($Protocol -ne 'sftp' -and (!$result.TargetUnchanged -or
        @(Get-ChildItem -LiteralPath $serverRoot -File -Force).Count -ne 1))) {
        throw 'The canceled replacement review changed the owned source or remote target.'
    }
    $result.Passed = $true
}
catch {
    $result.Error = $_ | Out-String
    if (Test-Path -LiteralPath $serverError -PathType Leaf) {
        $result.Observed += 'ServerError: ' + (Get-Content -LiteralPath $serverError -Raw)
    }
    if ($process) { $result.Observed += "ProcessHasExited: $($process.HasExited)" }
    $journalDir = Join-Path $data 'local\journal'
    if (Test-Path -LiteralPath $journalDir) {
        $result.Observed += @(Get-ChildItem -LiteralPath $journalDir -File -Force |
            ForEach-Object { "Journal: $($_.Name) $($_.Length) bytes" })
    }
    try {
        if ($window) {
            $result.Observed += @((Snapshot $window) | Select-Object -First 220 | ForEach-Object {
                "$($_.Current.ControlType.ProgrammaticName): $($_.Current.Name)"
            })
        }
    } catch { }
}
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        $process.WaitForExit(5000) | Out-Null
        if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
    if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id -Force }
    if ($nvdaProcess -and !$nvdaProcess.HasExited) {
        Start-Process -FilePath $nvda -ArgumentList @('-q') -Wait | Out-Null
        if (!$nvdaProcess.WaitForExit(15000)) { $nvdaProcess.Kill() }
    }
    if ($nvda) { $result.SpeechLog = $speechLog }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
