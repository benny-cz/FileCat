param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$PythonHelperPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('ftp', 'ftpes', 'sftp')][string]$Protocol = 'ftp',
    [string]$SftpFixtureId,
    [string]$ExpectedHostFingerprints,
    [string]$SftpPassword
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
$result = [ordered]@{
    Passed = $false; Fixture = $root; AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    Protocol = $Protocol; SftpFixtureId = $SftpFixtureId;
    RemoteVerificationRequired = ($Protocol -eq 'sftp'); HostKeyPrompt = $false;
    HostKeyFingerprintMatched = $false; Dpi = 0; Consent = $false; CertificatePrompt = $false;
    CertificateAcceptedOnce = $false; SignedIn = $false; RemoteListed = $false;
    WarningVisible = $false; WarningText = $null; NarrowBounds = $null;
    WarningClearedForKeepBoth = $false; ConflictWarning = $false;
    ConflictBounds = $null; ConflictSkipped = $false;
    SourceUnchanged = $false; TargetUnchanged = $(if ($Protocol -eq 'sftp') { $null } else { $false });
    Observed = @(); Error = $null
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

try {
    if ($Protocol -eq 'sftp') {
        $address = "sftp://benny@192.168.58.129/home/benny/FileCatValidation/SftpReplaceReview/$SftpFixtureId/"
    } else {
        $serveMode = if ($Protocol -eq 'ftpes') { 'serve-tls' } else { 'serve' }
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
    $rightLocation.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($address)
    $rightLocation.SetFocus()
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
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
    if ($Protocol -eq 'ftpes') {
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
    (New-Object -ComObject WScript.Shell).SendKeys('{HOME}{DOWN}{DOWN}')
    $warning = Await { @((Snapshot $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.StartsWith("Another app can change this remote file", [StringComparison]::Ordinal) -and
        !$_.Current.IsOffscreen
    })[0] } 'visible remote replacement risk'
    $result.WarningVisible = $true
    $result.WarningText = $warning.Current.Name
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
    (New-Object -ComObject WScript.Shell).SendKeys('{HOME}{DOWN}{DOWN}{DOWN}{DOWN}')
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
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
