param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# Run only in the logged-in Windows VMware validation guest. This uses an
# isolated Git build, profile, source and destination; it never approves a job.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatGoToInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
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
function GoToToolbar($Viewer) {
    return @(Descendants $Viewer | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -like 'Go to*'
    } | Select-Object -First 1)[0]
}
function Prompt($Viewer) {
    $box = @(Descendants $Viewer | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
        $_.Current.Name -like 'Byte offset*'
    } | Select-Object -First 1)[0]
    $go = Named $Viewer 'Go'
    $cancel = Named $Viewer 'Cancel'
    if ($box -and $go -and $cancel) { return @{ box = $box; go = $go; cancel = $cancel } }
    return $null
}
function Click($Element) {
    $r = $Element.Current.BoundingRectangle
    if ($Element.Current.IsOffscreen -or $r.Width -lt 10 -or $r.Height -lt 10) {
        throw "Unreachable control: $($Element.Current.Name)"
    }
    $x = [int][Math]::Round($r.X + $r.Width / 2)
    $y = [int][Math]::Round($r.Y + $r.Height / 2)
    if (-not [FileCatGoToInput]::SetCursorPos($x, $y)) { throw 'Guest cursor move failed' }
    Start-Sleep -Milliseconds 60
    [FileCatGoToInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [FileCatGoToInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    return @($x, $y)
}
function Key([byte] $Code) {
    [FileCatGoToInput]::keybd_event($Code, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 55
    [FileCatGoToInput]::keybd_event($Code, 0, 2, [UIntPtr]::Zero)
}
function Set-Draft($Box, [string] $Text) {
    $Box.SetFocus()
    $Box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
}
function Current-Draft($Box) {
    return $Box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Any-Name($Root, [string] $Pattern) {
    return @(Descendants $Root | Where-Object { $_.Current.Name -like $Pattern } | Select-Object -First 1)[0]
}

$validation = (Resolve-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'FileCatValidation')).Path
$app = (Resolve-Path -LiteralPath $AppPath).Path
$parent = (Resolve-Path -LiteralPath (Split-Path -Parent $ReportPath)).Path
if (-not $app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ne 'FileCat.exe' -or $parent -ne $validation -or
    (Test-Path -LiteralPath $ReportPath) -or (Get-Process FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use a fresh report, isolated Git app, and no other FileCat process'
}
$repo = (Get-Item -LiteralPath $app).Directory
while ($repo -and -not (Test-Path -LiteralPath (Join-Path $repo.FullName '.git'))) { $repo = $repo.Parent }
if (-not $repo) { throw 'No isolated Git checkout owns the app' }
$marker = Join-Path $repo.FullName '.git'
if ((Get-Item -LiteralPath $marker -Force).PSIsContainer) { $gitDir = $marker }
else {
    $line = (Get-Content -LiteralPath $marker -Force -Raw).Trim()
    if (-not $line.StartsWith('gitdir: ')) { throw 'Invalid Git worktree marker' }
    $gitDir = $line.Substring(8)
    if (-not [IO.Path]::IsPathRooted($gitDir)) { $gitDir = Join-Path $repo.FullName $gitDir }
}
$fixture = Join-Path (Join-Path $validation 'ViewerGoTo') ([Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
@($source, $target, $data) | ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
$file = Join-Path $source 'a-lines.txt'
$lines = 1..1200 | ForEach-Object { 'line {0:D4} {1}' -f $_, ('x' * 60) }
[IO.File]::WriteAllText($file, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
$result = [ordered]@{
    passed = $false; stage = 'startup'; gitHead = (Get-Content -LiteralPath (Join-Path $gitDir 'HEAD') -Raw).Trim()
    appDllSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll')).Hash.ToLowerInvariant()
    sourceSha256 = $hash
}
$process = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', "`"$data`"",
        '--left', "`"$source`"", '--right', "`"$target`"") -PassThru -WindowStyle Normal
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $main = Await {
        foreach ($w in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCondition)) {
            if ($w.Current.Name -like 'FileCat*') { return $w }
        }
    } 'owned main window'
    $row = Await { Named $main 'a-lines.txt' } 'owned text row'
    $row.SetFocus()
    [FileCatGoToInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Key 0x72 # F3 opens the selected file.
    $viewer = Await {
        foreach ($w in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCondition)) {
            if ($w.Current.Name.StartsWith('a-lines.txt ') -and $w.Current.Name.EndsWith('FileCat Viewer')) { return $w }
        }
    } 'owned Text viewer'
    $handle = [IntPtr]$viewer.Current.NativeWindowHandle
    if (-not [FileCatGoToInput]::MoveWindow($handle, 80, 80, 480, 300, $true)) {
        throw 'Could not set minimum viewer viewport'
    }
    $result.viewerBounds = @($viewer.Current.BoundingRectangle.X, $viewer.Current.BoundingRectangle.Y,
        $viewer.Current.BoundingRectangle.Width, $viewer.Current.BoundingRectangle.Height)
    $result.stage = 'invalid draft and native pointer correction'
    Click (Await { Named $viewer 'Hex' } 'Hex mode button') | Out-Null
    Await { Any-Name $viewer 'Mode: Hex' } 'Hex mode' | Out-Null
    Click (Await { GoToToolbar $viewer } 'Go to toolbar action') | Out-Null
    $prompt = Await { Prompt $viewer } 'Go to prompt'
    $r = $prompt.go.Current.BoundingRectangle
    $v = $viewer.Current.BoundingRectangle
    if ($r.Right -gt $v.Right -or $r.Bottom -gt $v.Bottom -or $r.Left -lt $v.Left -or $r.Top -lt $v.Top) {
        throw 'Go action lies outside the 480x300 viewer'
    }
    $result.goBounds = @($r.X, $r.Y, $r.Width, $r.Height)
    $label = Await {
        @(Descendants $viewer | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -like 'Byte offset (0x1F00*'
        } | Select-Object -First 1)[0]
    } 'wrapped Go to instruction'
    $labelRect = $label.Current.BoundingRectangle
    if ($label.Current.IsOffscreen -or $labelRect.Right -gt $v.Right -or
        $labelRect.Left -lt $v.Left -or $labelRect.Bottom -ge $r.Top) {
        throw 'The Go to instruction is clipped or overlaps the action footer'
    }
    $result.instructionBounds = @($labelRect.X, $labelRect.Y, $labelRect.Width, $labelRect.Height)
    Set-Draft $prompt.box '0xZZ'
    $result.invalidHex = Await {
        if (-not $prompt.go.Current.IsEnabled -and (Current-Draft $prompt.box) -eq '0xZZ') {
            $error = Any-Name $viewer '*hexadecimal bytes*'
            if ($error) { return $error.Current.Name }
        }
    } 'retained invalid hex and inline reason'
    Set-Draft $prompt.box '0x10'
    Await { $prompt.go.Current.IsEnabled } 'corrected Go action' | Out-Null
    $result.goPointer = Click $prompt.go
    Await { if (-not (Prompt $viewer)) { return $true } } 'prompt close after pointer Go' | Out-Null
    $result.afterHex = Await {
        $status = Any-Name $viewer '*0x10*'
        if ($status) { return $status.Current.Name }
    } 'Hex cursor offset 0x10'
    $result.stage = 'native keyboard line and Escape'
    Click (Await { Named $viewer 'Text' } 'Text mode button') | Out-Null
    Await { Any-Name $viewer 'Mode: Text' } 'Text mode' | Out-Null
    Click (Await { GoToToolbar $viewer } 'Go to toolbar action') | Out-Null
    $prompt = Await { Prompt $viewer } 'second Go to prompt'
    Set-Draft $prompt.box 'L900'
    Await { $prompt.go.Current.IsEnabled } 'enabled line target' | Out-Null
    $prompt.box.SetFocus()
    Key 0x0D # Enter in the prompt accepts the line target.
    Await { if (-not (Prompt $viewer)) { return $true } } 'prompt close after keyboard Enter' | Out-Null
    $result.lineStatus = Await {
        $status = Any-Name $viewer 'Line 900.*'
        if ($status) { return $status.Current.Name }
    } 'Line 900 navigation status'
    Click (Await { GoToToolbar $viewer } 'Go to toolbar action') | Out-Null
    $prompt = Await { Prompt $viewer } 'third Go to prompt'
    Set-Draft $prompt.box 'L1000'
    $prompt.box.SetFocus()
    Key 0x1B # Escape cancels the new draft.
    Await { if (-not (Prompt $viewer)) { return $true } } 'Escape dismissal' | Out-Null
    $result.sourceUnchanged = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant() -eq $hash
    $result.targetEmpty = -not (Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)
    $result.passed = [bool]($result.sourceUnchanged -and $result.targetEmpty)
    $result.stage = 'complete'
} catch {
    $result.error = $_.Exception.Message
    if ($viewer) {
        try { $result.visibleNamesAtFailure = @(Descendants $viewer | Where-Object { -not $_.Current.IsOffscreen } |
            ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -First 35) } catch { }
    }
} finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        try { Wait-Process -Id $process.Id -Timeout 5 -ErrorAction Stop } catch { }
    }
    $base = (Resolve-Path -LiteralPath (Join-Path $validation 'ViewerGoTo')).Path
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    if (-not $resolved.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the owned Go to fixture'
    }
    for ($i = 0; $i -lt 10; $i++) {
        try { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop; break }
        catch { if ($i -eq 9) { $result.cleanupError = $_.Exception.Message }; Start-Sleep -Milliseconds 500 }
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if (-not $result.passed) { exit 1 }
