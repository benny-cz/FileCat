param(
    [Parameter(Mandatory)][string] $HarnessPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# Native capability boundary: an owned, instrumented source says CanSeek=false.
# The real Viewer prompt must reject L2 before a line scan and NVDA must speak
# the reason. Run only in the logged-in Windows VMware validation guest.
$ErrorActionPreference = 'Stop'
trap {
    $_ | Out-String | Set-Content -LiteralPath ($ReportPath + '.error.txt') -Encoding UTF8
    exit 1
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatNonseekInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
'@
function Await([scriptblock] $Check, [string] $Description, [int] $Seconds = 25) {
    $until = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $value = & $Check
        if ($null -ne $value -and $value -ne $false) { return $value }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $until)
    throw "Timed out waiting for $Description"
}
function Descendants($Root) {
    return @($Root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Named($Root, [string] $Name) {
    return @(Descendants $Root | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1)[0]
}
function Prompt($Window) {
    $box = @(Descendants $Window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
        $_.Current.Name -like 'Byte offset*'
    } | Select-Object -First 1)[0]
    $go = Named $Window 'Go'
    if ($box -and $go) { return @{ box = $box; go = $go } }
}
function Click($Element) {
    $r = $Element.Current.BoundingRectangle
    if ($Element.Current.IsOffscreen -or $r.Width -lt 10 -or $r.Height -lt 10) {
        throw "Unreachable $($Element.Current.Name)"
    }
    $x = [int][Math]::Round($r.X + $r.Width / 2)
    $y = [int][Math]::Round($r.Y + $r.Height / 2)
    if (-not [FileCatNonseekInput]::SetCursorPos($x, $y)) { throw 'Guest cursor move failed' }
    Start-Sleep -Milliseconds 80
    [FileCatNonseekInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [FileCatNonseekInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
function Key([byte] $Code) {
    [FileCatNonseekInput]::keybd_event($Code, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [FileCatNonseekInput]::keybd_event($Code, 0, 2, [UIntPtr]::Zero)
}
function Stats([string] $Path) {
    $raw = (Get-Content -LiteralPath $Path -Raw).Trim().Split(',')
    if ($raw.Count -ne 2) { throw 'Unreadable nonseekable-source read counters' }
    return @{ calls = [int]$raw[0]; bytes = [long]$raw[1] }
}
function TakeSpeech {
    if (-not (Test-Path -LiteralPath $nvdaLog)) { return @() }
    $lines = @(Get-Content -LiteralPath $nvdaLog -Encoding UTF8)
    $new = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    return @($new | Where-Object { $_ -match 'Speaking \[' } |
        ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
}

$validation = (Resolve-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'FileCatValidation')).Path
$harness = (Resolve-Path -LiteralPath $HarnessPath).Path
$nvda = (Resolve-Path -LiteralPath $NvdaPath).Path
$parent = (Resolve-Path -LiteralPath (Split-Path -Parent $ReportPath)).Path
if (-not $harness.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($harness) -ne 'GoToNonseekHarness.exe' -or
    -not $nvda.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($nvda) -ne 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or
    $parent -ne $validation -or (Test-Path -LiteralPath $ReportPath) -or
    (Get-Process -Name GoToNonseekHarness,nvda,FileCat -ErrorAction SilentlyContinue)) {
    throw 'Require isolated Git harness, signed NVDA, fresh report and no competing processes'
}
$repo = (Get-Item -LiteralPath $harness).Directory
while ($repo -and -not (Test-Path -LiteralPath (Join-Path $repo.FullName '.git'))) { $repo = $repo.Parent }
if (-not $repo) { throw 'Harness is outside the isolated Git checkout' }
$marker = Join-Path $repo.FullName '.git'
if ((Get-Item -LiteralPath $marker -Force).PSIsContainer) { $gitDir = $marker }
else {
    $line = (Get-Content -LiteralPath $marker -Force -Raw).Trim()
    if (-not $line.StartsWith('gitdir: ')) { throw 'Unreadable Git worktree marker' }
    $gitDir = $line.Substring(8)
    if (-not [IO.Path]::IsPathRooted($gitDir)) { $gitDir = Join-Path $repo.FullName $gitDir }
}
$head = (Get-Content -LiteralPath (Join-Path $gitDir 'HEAD') -Raw).Trim()
if ($head -notmatch '^[0-9a-f]{40}$') { throw 'Harness checkout is not detached at an exact commit' }
$root = Join-Path (Join-Path $validation 'GoToNonseekDriver') ([Guid]::NewGuid().ToString('N'))
$config = Join-Path $root 'nvda-config'
New-Item -ItemType Directory -Path $config -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$nvdaLog = Join-Path $root 'nvda.log'
$harnessReport = $ReportPath + '.harness.json'
$statsPath = $harnessReport + '.stats'
if ((Test-Path -LiteralPath $harnessReport) -or (Test-Path -LiteralPath $statsPath)) {
    throw 'Harness report or stats path already exists'
}
$script:speechIndex = 0
$result = [ordered]@{
    passed = $false; stage = 'startup'; gitHead = $head
    nvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion
}
$nvdaProcess = $owned = $null
try {
    $nvdaProcess = Start-Process -FilePath $nvda -ArgumentList @("--log-file=$nvdaLog",
        '--log-level=12', "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Await {
        if ($nvdaProcess.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $nvdaLog) -and
            (Select-String -LiteralPath $nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { return $true }
    } 'owned NVDA initialization' 30 | Out-Null
    TakeSpeech | Out-Null
    $owned = Start-Process -FilePath $harness -ArgumentList @("`"$harnessReport`"") -PassThru -WindowStyle Normal
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $owned.Id)
    $window = Await {
        foreach ($candidate in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCondition)) {
            if ($candidate.Current.Name.EndsWith('FileCat Viewer')) { return $candidate }
        }
    } 'native nonseekable Viewer'
    $result.windowBounds = @($window.Current.BoundingRectangle.X, $window.Current.BoundingRectangle.Y,
        $window.Current.BoundingRectangle.Width, $window.Current.BoundingRectangle.Height)
    $goTo = Await {
        @(Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $_.Current.Name -like 'Go to*'
        } | Select-Object -First 1)[0]
    } 'Go to toolbar action'
    [FileCatNonseekInput]::SetForegroundWindow($owned.MainWindowHandle) | Out-Null
    Click $goTo
    $prompt = Await { Prompt $window } 'Go to prompt'
    $result.stage = 'nonseekable line draft'
    $result.readsBefore = Stats (Await { if (Test-Path -LiteralPath $statsPath) { return $statsPath } } 'source counters')
    TakeSpeech | Out-Null
    $prompt.box.SetFocus()
    $prompt.box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('L2')
    $result.reason = Await {
        if ($prompt.go.Current.IsEnabled) { return $null }
        if ($prompt.box.Current.HelpText -notlike '*seekable*') { return $null }
        $text = @(Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -like '*seekable source*'
        } | Select-Object -First 1)[0]
        if ($text) { return $text.Current.Name }
    } 'visible, field-associated nonseekable reason'
    $result.draft = $prompt.box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Key 0x0D # Invalid Enter repeats the reason without dismissing the prompt.
    Start-Sleep -Milliseconds 900
    $result.speech = @(TakeSpeech)
    if (($result.speech -join ' ') -notmatch 'seekable|cannot scan to a line') {
        $prompt.box.SetFocus()
        Start-Sleep -Milliseconds 900
        $result.speech += @(TakeSpeech)
    }
    $result.readsAfter = Stats $statsPath
    $result.promptRetained = $null -ne (Prompt $window)
    $result.spokenReason = ($result.speech -join ' ') -match 'seekable|cannot scan to a line'
    Key 0x1B # Cancel via keyboard; no unsupported scan should have started.
    Await { if (-not (Prompt $window)) { return $true } } 'Escape dismissal' | Out-Null
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $finished = Await {
        if (Test-Path -LiteralPath $harnessReport) { return Get-Content -LiteralPath $harnessReport -Raw | ConvertFrom-Json }
    } 'harness source integrity report'
    $result.harness = $finished
    $result.passed = [bool]($result.draft -eq 'L2' -and $result.promptRetained -and
        $result.spokenReason -and $result.readsBefore.calls -eq $result.readsAfter.calls -and
        $result.readsBefore.bytes -eq $result.readsAfter.bytes -and
        -not $finished.sourceCanSeek -and $finished.sourceUnchanged -and $finished.targetEmpty)
    $result.stage = 'complete'
} catch {
    $result.error = $_.Exception.Message
    if ($window) {
        try { $result.visibleNamesAtFailure = @(Descendants $window | Where-Object { -not $_.Current.IsOffscreen } |
            ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -First 35) } catch { }
    }
} finally {
    if ($owned -and -not $owned.HasExited) {
        Stop-Process -Id $owned.Id -Force
        try { Wait-Process -Id $owned.Id -Timeout 5 -ErrorAction Stop } catch { }
    }
    if ($nvdaProcess -and -not $nvdaProcess.HasExited) {
        Start-Process -FilePath $nvda -ArgumentList @('-q') -Wait -WindowStyle Hidden | Out-Null
        if (-not $nvdaProcess.WaitForExit(15000)) { $nvdaProcess.Kill() }
    }
    $base = (Resolve-Path -LiteralPath (Join-Path $validation 'GoToNonseekDriver')).Path
    $resolved = (Resolve-Path -LiteralPath $root).Path
    if (-not $resolved.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside owned nonseekable driver folder'
    }
    for ($i = 0; $i -lt 10; $i++) {
        try { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop; break }
        catch { if ($i -eq 9) { $result.cleanupError = $_.Exception.Message }; Start-Sleep -Milliseconds 500 }
    }
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if (-not $result.passed) { exit 1 }
