param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$PythonHelperPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('ftp', 'ftpes', 'ftps', 'sftp')][string]$Protocol = 'ftp',
    [string]$SftpFixtureId,
    [string]$ExpectedHostFingerprints,
    [string]$SftpPassword
)

# Native Win11 UI Automation acceptance for a test-owned 2,001-file provider.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatRemoteLargeForeground {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static uint ForegroundProcessId() {
        uint processId;
        GetWindowThreadProcessId(GetForegroundWindow(), out processId);
        return processId;
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
if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !$helper.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($helper) -ine 'win-ftp-owned-stage.py' -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    !(Test-Path -LiteralPath $helper -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report)) {
    throw 'Use an isolated Git-built FileCat, copied helper and fresh guest-local report.'
}
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat processes before the isolated provider test.'
}
if ($Protocol -eq 'sftp' -and ($SftpFixtureId -cnotmatch '^[0-9a-f]{32}$' -or
    [string]::IsNullOrWhiteSpace($ExpectedHostFingerprints) -or
    [string]::IsNullOrEmpty($SftpPassword))) {
    throw 'SFTP needs an exact owned Ubuntu fixture, independent host fingerprints and a transient credential.'
}
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$gitExe = 'C:\Program Files\Git\cmd\git.exe'
if (!(Test-Path -LiteralPath $gitExe -PathType Leaf)) {
    throw 'Git for Windows is required to verify the exact validation checkout.'
}
$gitHead = (& $gitExe -c "safe.directory=$repo" -C $repo rev-parse HEAD 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $gitHead -notmatch '^[0-9a-f]{40}$') {
    throw 'The validation app must be inside a Git checkout with a known commit.'
}

$root = Join-Path $validation ('LargeRemoteUiaListing\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$localTarget = Join-Path $root 'local-target'
$serverRoot = Join-Path $root 'server'
$data = Join-Path $root 'data'
$ready = Join-Path $root 'server.ready'
$serverError = Join-Path $root 'server.stderr.txt'
New-Item -ItemType Directory -Force -Path $source, $localTarget, $serverRoot, $data | Out-Null
$original = @{ 'a.txt' = [Text.Encoding]::UTF8.GetBytes("alpha`n"); 'b.txt' = [Text.Encoding]::UTF8.GetBytes("beta`n") }
foreach ($name in $original.Keys) { [IO.File]::WriteAllBytes((Join-Path $source $name), $original[$name]) }
$target = Join-Path $serverRoot 'target.txt'
$targetHash = $null
if ($Protocol -ne 'sftp') {
    [IO.File]::WriteAllBytes($target, [Text.Encoding]::UTF8.GetBytes("owned unchanged remote target`n"))
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    for ($index = 0; $index -lt 2000; $index++) {
        [IO.File]::Create((Join-Path $serverRoot ('native-list-{0:D4}.txt' -f $index))).Dispose()
    }
}
$python = (Get-Command python -ErrorAction Stop).Source
$server = $null
$process = $null
$result = [ordered]@{
    Passed = $false; Protocol = $Protocol; Fixture = $root; GitHead = $gitHead;
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash;
    GeneratedFiles = 2000; SftpFixtureId = $SftpFixtureId
}

function Await([scriptblock]$condition, [string]$description, [int]$seconds = 30) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 200
    } while ($clock.Elapsed.TotalSeconds -lt $seconds)
    throw "Timed out waiting for $description."
}
function Snapshot($window) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $all = @(SnapshotAll $window)
    if ($all.Count -gt 800) { throw "UI Automation enumerated $($all.Count) nodes." }
    $rows = @($all | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        ($_.Current.Name -like 'native-list-*.txt' -or $_.Current.Name -eq 'target.txt')
    })
    $status = @($all | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name -match '([\d\s,.\u00a0]+)\s+files\b' -and
        ([regex]::Replace($Matches[1], '\D', '')) -eq '2001'
    } | Select-Object -First 1)
    [pscustomobject]@{
        FileCount = if ($status.Count) { 2001 } else { $null }
        Status = if ($status.Count) { $status[0].Current.Name } else { $null }
        RowCount = $rows.Count
        Visited = $all.Count
        QuerySeconds = [math]::Round($clock.Elapsed.TotalSeconds, 3)
        VisibleNames = @($rows | ForEach-Object { $_.Current.Name })
        FirstRow = @($rows | Where-Object { $_.Current.Name -eq 'native-list-0000.txt' })[0]
    }
}
function One($window, $type, [string]$name) {
    @((SnapshotAll $window) | Where-Object {
        $_.Current.ControlType -eq $type -and $_.Current.Name -eq $name -and
        !$_.Current.IsOffscreen
    })[0]
}
function SnapshotAll($window) {
    for ($retry = 0; $retry -lt 8; $retry++) {
        try {
            return @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition))
        } catch {
            $message = $_.Exception.ToString()
            if (($message -notmatch 'E_FAIL|ElementNotAvailable|RPC_E_CALL_REJECTED') -or
                $retry -eq 7 -or !$process -or $process.HasExited) { throw }
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            Start-Sleep -Milliseconds 150
        }
    }
}
function Button($window, [string]$name) {
    @((SnapshotAll $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -eq $name -and $_.Current.IsEnabled -and !$_.Current.IsOffscreen
    })[0]
}
function ActivateOwnedApp {
    [FileCatRemoteLargeForeground]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    Start-Sleep -Milliseconds 200
    if ([FileCatRemoteLargeForeground]::ForegroundProcessId() -ne $process.Id) {
        [FileCatRemoteLargeForeground]::ClickTitle($process.MainWindowHandle)
        Start-Sleep -Milliseconds 300
    }
    if ([FileCatRemoteLargeForeground]::ForegroundProcessId() -ne $process.Id) {
        throw 'The owned FileCat did not become foreground; refusing to send keys.'
    }
}

try {
    if ($Protocol -eq 'sftp') {
        $address = "sftp://benny@192.168.58.129/home/benny/FileCatValidation/WindowsLargeSftpListing/$SftpFixtureId/"
    } else {
        $mode = switch ($Protocol) { 'ftpes' { 'serve-tls' } 'ftps' { 'serve-implicit-tls' } default { 'serve' } }
        $server = Start-Process -FilePath $python -ArgumentList @($helper, $mode, $serverRoot, $ready) -PassThru `
            -WindowStyle Hidden -RedirectStandardError $serverError
        $port = Await {
            if ($server.HasExited) { throw 'The owned FTP server exited before readiness.' }
            if (Test-Path -LiteralPath $ready -PathType Leaf) {
                $text = (Get-Content -LiteralPath $ready -Raw).Trim()
                if ($text -match '^\d+$') { [int]$text }
            }
        } 'owned FTP port' 15
        if ($port -lt 1 -or $port -gt 65535) { throw 'The owned FTP port is invalid.' }
        $address = "${Protocol}://tester@127.0.0.1:$port/"
    }
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $localTarget) -PassThru
    Await {
        $process.Refresh()
        if ($process.HasExited) { throw 'The test-owned FileCat exited before its window opened.' }
        $process.MainWindowHandle -ne [IntPtr]::Zero
    } 'owned FileCat window' 20 | Out-Null
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'A different executable owns the test window.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $rightLocation = Await { @((SnapshotAll $window) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
        $_.Current.Name -eq 'Location' -and !$_.Current.IsOffscreen
    } | Sort-Object { $_.Current.BoundingRectangle.X })[-1] } 'right Location field'
    ActivateOwnedApp
    $rightLocation.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($address)
    $rightLocation.SetFocus()
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -ne $process.Id) {
        throw 'The owned Location field did not take keyboard focus.'
    }
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    if ($Protocol -eq 'ftp') {
        $consent = Await { Button $window 'Connect unencrypted' } 'explicit plaintext FTP consent' 15
        $consent.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $result.PlaintextConsent = $true
    }
    $password = Await { One $window ([System.Windows.Automation.ControlType]::Edit) 'Password' } 'owned password field'
    $password.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue(
        $(if ($Protocol -eq 'sftp') { $SftpPassword } else { 'secret-pass' }))
    $connect = Await { Button $window 'Connect' } 'remote Connect action'
    $connect.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if ($Protocol -eq 'sftp') {
        $trust = Await { Button $window 'Connect once' } 'new SFTP host-key decision' 30
        $expected = @($ExpectedHostFingerprints.Split(',') |
            Where-Object { $_ -match '^SHA256:[A-Za-z0-9+/]{43}$' })
        if ($expected.Count -eq 0) { throw 'No valid independent SSH host-key fingerprint was provided.' }
        $shown = @((SnapshotAll $window) | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            !$_.Current.IsOffscreen
        } | ForEach-Object { $_.Current.Name })
        $match = @($shown | Where-Object { $expected -contains $_ })
        if (!$match.Count -or 'New server key' -notin $shown) {
            throw 'The native SFTP host-key prompt did not match an independently observed server key.'
        }
        $result.HostKeyMatched = $match[0]
        $trust.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    if ($Protocol -in @('ftpes', 'ftps')) {
        $trust = Await { Button $window 'Connect once' } 'fresh FTPS certificate decision'
        $trust.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $result.CertificateAcceptedOnce = $true
    }
    $first = Await {
        $state = Snapshot $window
        if ($state.FileCount -eq 2001 -and $state.FirstRow) { $state }
    } 'complete remote provider listing' 70
    if ($first.RowCount -gt 120 -or $first.Visited -gt 800 -or $first.QuerySeconds -gt 5) {
        throw "The loaded provider accessibility tree was not bounded: $($first | ConvertTo-Json -Compress)."
    }
    $firstRow = $first.FirstRow.Current.BoundingRectangle
    if ($firstRow.Width -lt 40 -or $firstRow.Height -lt 12) { throw 'The first provider row has invalid native bounds.' }
    $first.FirstRow.SetFocus()
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne 'native-list-0000.txt') {
        throw 'The first provider row did not take native UI Automation focus.'
    }
    ActivateOwnedApp
    $jump = [Diagnostics.Stopwatch]::StartNew()
    [Windows.Forms.SendKeys]::SendWait('{END}')
    Await {
        [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq 'target.txt'
    } 'last provider item after End' 12 | Out-Null
    $last = Snapshot $window
    if ($last.FileCount -ne 2001 -or $last.RowCount -gt 120 -or $last.Visited -gt 800 -or
        $last.QuerySeconds -gt 5 -or $jump.Elapsed.TotalSeconds -gt 12 -or
        'target.txt' -notin $last.VisibleNames) {
        throw "The final provider accessibility tree was not bounded: $($last | ConvertTo-Json -Compress)."
    }
    foreach ($name in $original.Keys) {
        if ([BitConverter]::ToString([IO.File]::ReadAllBytes((Join-Path $source $name))) -ne
            [BitConverter]::ToString([byte[]]$original[$name])) { throw "The $name source bytes changed." }
    }
    if ((Get-ChildItem -LiteralPath $localTarget).Count -ne 0) {
        throw 'The initial local target changed during the provider journey.'
    }
    if ($Protocol -ne 'sftp' -and
        ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $targetHash -or
         (Get-ChildItem -LiteralPath $serverRoot -File).Count -ne 2001 -or
         @((Get-ChildItem -LiteralPath $serverRoot -Filter 'native-list-*.txt') |
             Where-Object { $_.Length -ne 0 }).Count -ne 0)) {
        throw 'The owned loopback server files changed during the provider journey.'
    }
    $result.Loaded = @{ FileCount = $first.FileCount; Status = $first.Status; RowCount = $first.RowCount;
        Visited = $first.Visited; QuerySeconds = $first.QuerySeconds }
    $result.AfterEnd = @{ FileCount = $last.FileCount; Status = $last.Status; RowCount = $last.RowCount;
        Visited = $last.Visited; QuerySeconds = $last.QuerySeconds; Focused = 'target.txt' }
    $result.EndKeySeconds = [math]::Round($jump.Elapsed.TotalSeconds, 3)
    $result.SourceBytesUnchanged = $true
    $result.ServerFilesUnchanged = if ($Protocol -eq 'sftp') { $null } else { $true }
    $result.Passed = $true
} catch { $result.Error = $_.Exception.Message }
finally {
    if ($process -and !$process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    }
    if ($server -and !$server.HasExited) {
        Stop-Process -Id $server.Id
        $server.WaitForExit(10000) | Out-Null
    }
    $resolvedRoot = [IO.Path]::GetFullPath($root)
    if (!$resolvedRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing fixture cleanup outside FileCatValidation.'
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
if (!$result.Passed) { throw $result.Error }
