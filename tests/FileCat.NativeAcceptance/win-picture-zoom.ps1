param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $ReportPath,
    [Parameter(Mandatory)][ValidateSet(96, 144, 192)][int] $ExpectedDpi
)

# Run inside the logged-in Windows VMware validation guest. The fixture,
# FileCat process and profile are owned by this one native acceptance run.
$ErrorActionPreference = 'Stop'
trap {
    $_ | Out-String | Set-Content -LiteralPath ($ReportPath + '.error.txt') -Encoding UTF8
    exit 1
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FileCatPictureInput {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@

function Wait-For([scriptblock] $Check, [string] $Description, [int] $Seconds = 25) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $value = & $Check
        if ($null -ne $value -and $value -ne $false) { return $value }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $Description"
}
function Find-ByName($Root, [string] $Name) {
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $Name))
}
function Find-ById($Root, [string] $Id) {
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
}
function Click-Element($Element) {
    $bounds = $Element.Current.BoundingRectangle
    if ($Element.Current.IsOffscreen -or $bounds.Width -lt 10 -or $bounds.Height -lt 10) {
        throw "Cannot click the visible control $($Element.Current.Name): $bounds"
    }
    $x = [int][Math]::Round($bounds.X + $bounds.Width / 2)
    $y = [int][Math]::Round($bounds.Y + $bounds.Height / 2)
    if (-not [FileCatPictureInput]::SetCursorPos($x, $y)) { throw 'Guest pointer move failed' }
    Start-Sleep -Milliseconds 60
    $point = [FileCatPictureInput+Point]::new()
    [FileCatPictureInput]::GetCursorPos([ref]$point) | Out-Null
    if ([Math]::Abs($point.X - $x) -gt 2 -or [Math]::Abs($point.Y - $y) -gt 2) {
        throw "Guest pointer missed $($Element.Current.Name)"
    }
    [FileCatPictureInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 55
    [FileCatPictureInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    return @($x, $y)
}
function Send-F3 {
    [FileCatPictureInput]::keybd_event(0x72, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 65
    [FileCatPictureInput]::keybd_event(0x72, 0, 2, [UIntPtr]::Zero)
}
function Save-Png([string] $Path, [int] $Width, [int] $Height) {
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::CornflowerBlue)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
function Find-Viewer($Desktop, $Condition, [string] $FileName) {
    foreach ($window in $Desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $Condition)) {
        if ($window.Current.Name.StartsWith($FileName + ' ', [StringComparison]::Ordinal) -and
            $window.Current.Name.EndsWith('FileCat Viewer', [StringComparison]::Ordinal)) {
            return $window
        }
    }
    return $null
}
function Zoom-State($Viewer) {
    $label = Find-ById $Viewer 'Picture zoom percentage'
    $fit = Find-ByName $Viewer 'Fit picture to window'
    $actual = Find-ByName $Viewer 'Picture at 100 percent'
    if ($null -eq $label -or $null -eq $fit -or $null -eq $actual) { return $null }
    return [ordered]@{
        text = $label.Current.Name
        fit = $fit.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString()
        actual = $actual.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString()
    }
}
function Choose-Zoom($Desktop, $Viewer, [string] $Level) {
    $button = Find-ByName $Viewer 'Choose picture zoom level'
    if ($null -eq $button) { throw 'The accessible picture zoom chooser is absent' }
    Click-Element $button | Out-Null
    $choice = Wait-For {
        foreach ($candidate in $Desktop.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, $Level))) {
            if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and
                -not $candidate.Current.IsOffscreen) { return $candidate }
        }
        return $null
    } "visible $Level picture zoom choice"
    Click-Element $choice | Out-Null
    $expected = if ($Level -eq 'Fit to window') { 'Fit*' } else { 'Zoom ' + $Level }
    return Wait-For { $state = Zoom-State $Viewer; if ($state -and $state.text -match
        ('^' + [regex]::Escape($expected).Replace('\*', '.*') + '$')) { return $state }; return $null }
        "$Level picture zoom state"
}

$validation = (Resolve-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'FileCatValidation')).Path
$app = (Resolve-Path -LiteralPath $AppPath).Path
$reportParent = (Resolve-Path -LiteralPath (Split-Path -Parent $ReportPath)).Path
if (-not $app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $reportParent -ne $validation -or (Test-Path -LiteralPath $ReportPath) -or
    [IO.Path]::GetFileName($app) -ne 'FileCat.exe') {
    throw 'Use the isolated Git app and a fresh report in FileCatValidation'
}
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat instances before this isolated check'
}
$repo = (Get-Item -LiteralPath $app).Directory
while ($null -ne $repo -and -not (Test-Path -LiteralPath (Join-Path $repo.FullName '.git'))) { $repo = $repo.Parent }
if ($null -eq $repo) { throw 'The app is not inside the isolated Git checkout' }
$marker = Join-Path $repo.FullName '.git'
if ((Get-Item -LiteralPath $marker -Force).PSIsContainer) { $gitDirectory = $marker }
else {
    $line = (Get-Content -LiteralPath $marker -Raw -Force).Trim()
    if (-not $line.StartsWith('gitdir: ', [StringComparison]::Ordinal)) { throw 'Unreadable Git marker' }
    $gitDirectory = $line.Substring(8)
    if (-not [IO.Path]::IsPathRooted($gitDirectory)) { $gitDirectory = Join-Path $repo.FullName $gitDirectory }
}
$fixture = Join-Path (Join-Path $validation 'PictureZoom') ([Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
@($source, $target, $data) | ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
$small = Join-Path $source 'a-small.png'
$wide = Join-Path $source 'b-wide.png'
Save-Png $small 8 8
Save-Png $wide 8192 64
$hashes = [ordered]@{
    'a-small.png' = (Get-FileHash -Algorithm SHA256 -LiteralPath $small).Hash.ToLowerInvariant()
    'b-wide.png' = (Get-FileHash -Algorithm SHA256 -LiteralPath $wide).Hash.ToLowerInvariant()
}
$result = [ordered]@{
    passed = $false; stage = 'startup'; expectedDpi = $ExpectedDpi; fixture = $fixture
    gitHead = (Get-Content -LiteralPath (Join-Path $gitDirectory 'HEAD') -Raw).Trim()
    appDllSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll')).Hash.ToLowerInvariant()
    sourceSha256 = $hashes
}
$process = $null
$viewer = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', "`"$data`"",
        '--left', "`"$source`"", '--right', "`"$target`"") -PassThru -WindowStyle Normal
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $main = Wait-For {
        foreach ($candidate in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
            if ($candidate.Current.Name -like 'FileCat*') { return $candidate }
        }
        return $null
    } 'owned FileCat window'
    $result.stage = 'small image: Fit equals 100 but remains a distinct mode'
    $row = Wait-For { Find-ByName $main 'a-small.png' } 'owned small picture row'
    $result.smallRowType = $row.Current.ControlType.ProgrammaticName
    $row.SetFocus()
    $result.smallFocus = [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name
    $result.mainWindowHandle = $process.MainWindowHandle.ToInt64()
    $result.activatedMain = [FileCatPictureInput]::SetForegroundWindow($process.MainWindowHandle)
    Send-F3
    $viewer = Wait-For { Find-Viewer $desktop $condition 'a-small.png' } 'small picture viewer'
    $nativeHandle = [IntPtr]$viewer.Current.NativeWindowHandle
    $result.windowDpi = [FileCatPictureInput]::GetDpiForWindow($nativeHandle)
    if ($result.windowDpi -ne $ExpectedDpi) { throw "Viewer DPI $($result.windowDpi) differs from expected $ExpectedDpi" }
    $result.smallFit = Wait-For { $state = Zoom-State $viewer
        if ($state -and $state.text -match '^Fit.*100%$') { return $state }; return $null } 'small fitted 100 percent'
    if ($result.smallFit.fit -ne 'On' -or $result.smallFit.actual -ne 'Off') {
        throw 'Small Fit at 100% falsely claims Actual size'
    }
    $result.smallActual = Choose-Zoom $desktop $viewer '100%'
    if ($result.smallActual.text -ne 'Zoom 100%' -or $result.smallActual.fit -ne 'Off' -or
        $result.smallActual.actual -ne 'On') { throw 'Explicit 100% did not select only Actual size' }
    $result.smallFitAgain = Choose-Zoom $desktop $viewer 'Fit to window'
    if ($result.smallFitAgain.text -notmatch '^Fit.*100%$' -or $result.smallFitAgain.fit -ne 'On') {
        throw 'Fit did not restore its distinct mode'
    }
    $viewer.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Wait-For { if ($null -eq (Find-Viewer $desktop $condition 'a-small.png')) { return $true } } 'small viewer close' | Out-Null
    $result.stage = 'wide capped image: exact presets, wheel and Fit resize'
    $row = Wait-For { Find-ByName $main 'b-wide.png' } 'owned wide picture row'
    $row.SetFocus()
    [FileCatPictureInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Send-F3
    $viewer = Wait-For { Find-Viewer $desktop $condition 'b-wide.png' } 'wide picture viewer'
    $nativeHandle = [IntPtr]$viewer.Current.NativeWindowHandle
    $limit = Wait-For { $found = Find-ById $viewer 'Picture decode limit'
        if ($found -and -not $found.Current.IsOffscreen -and $found.Current.Name -like 'Detail limited:*') { return $found } }
        'visible and accessible decode limit'
    $result.decodeLimit = $limit.Current.Name
    $result.decodeLimitBounds = @($limit.Current.BoundingRectangle.X,
                                  $limit.Current.BoundingRectangle.Y,
                                  $limit.Current.BoundingRectangle.Width,
                                  $limit.Current.BoundingRectangle.Height)
    foreach ($level in @('50%', '125%', '200%')) {
        $state = Choose-Zoom $desktop $viewer $level
        $result[('zoom' + $level.Replace('%',''))] = $state
        if ($state.text -ne ('Zoom ' + $level) -or $state.fit -ne 'Off' -or $state.actual -ne 'Off') {
            throw "$level wrongly claims Fit or Actual size"
        }
    }
    # The toolbar also has a Picture button. Aim at the center of the image
    # viewport so the wheel exercises the view rather than that mode button.
    $bounds = $viewer.Current.BoundingRectangle
    $wheelX = [int][Math]::Round($bounds.X + $bounds.Width / 2)
    $wheelY = [int][Math]::Round($bounds.Y + $bounds.Height / 2)
    if (-not [FileCatPictureInput]::SetCursorPos($wheelX, $wheelY)) {
        throw 'Could not move the guest pointer over the picture viewport'
    }
    $result.wheelTarget = @($wheelX, $wheelY)
    [FileCatPictureInput]::mouse_event(0x0800, 0, 0, 120, [UIntPtr]::Zero)
    $result.afterWheel = Wait-For { $state = Zoom-State $viewer
        if ($state -and $state.text -ne 'Zoom 200%') { return $state } } 'wheel custom zoom'
    if ($result.afterWheel.fit -ne 'Off' -or $result.afterWheel.actual -ne 'Off') {
        throw 'Wheel custom zoom claims Fit or 100%'
    }
    $result.beforeResize = Choose-Zoom $desktop $viewer 'Fit to window'
    $result.originalViewerWidth = $viewer.Current.BoundingRectangle.Width
    if (-not [FileCatPictureInput]::MoveWindow($nativeHandle, 80, 80, 980, 600, $true)) {
        throw 'Could not resize owned picture viewer'
    }
    $result.narrowViewerWidth = $viewer.Current.BoundingRectangle.Width
    $narrow = Wait-For { $state = Zoom-State $viewer
        if ($state -and $state.text -like 'Fit*') { return $state }
        return $null } 'narrow fitted zoom'
    if (-not [FileCatPictureInput]::MoveWindow($nativeHandle, 80, 80, 1350, 650, $true)) {
        throw 'Could not widen owned picture viewer'
    }
    $result.wideViewerWidth = $viewer.Current.BoundingRectangle.Width
    $result.fitNarrow = $narrow
    $result.fitWide = Wait-For { $state = Zoom-State $viewer
        if ($state -and $state.text -like 'Fit*' -and $state.text -ne $narrow.text) { return $state }
        return $null } 'wider fitted zoom'
    $result.sourceUnchanged = ((Get-FileHash -Algorithm SHA256 -LiteralPath $small).Hash.ToLowerInvariant() -eq $hashes['a-small.png'] -and
        (Get-FileHash -Algorithm SHA256 -LiteralPath $wide).Hash.ToLowerInvariant() -eq $hashes['b-wide.png'])
    $result.targetEmpty = -not (Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)
    $result.passed = [bool]($result.sourceUnchanged -and $result.targetEmpty)
    $result.stage = 'complete'
} catch {
    $result.error = $_.Exception.Message
    if ($null -ne $desktop -and $null -ne $condition) {
        try {
            $result.windowsAtFailure = @($desktop.FindAll([System.Windows.Automation.TreeScope]::Children,
                $condition) | ForEach-Object { $_.Current.Name })
            $result.focusAtFailure = [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name
        } catch { $result.uiaDiagnosticError = $_.Exception.Message }
    }
} finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        try { Wait-Process -Id $process.Id -Timeout 5 -ErrorAction Stop } catch { }
    }
    $base = (Resolve-Path -LiteralPath (Join-Path $validation 'PictureZoom')).Path
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    if (-not $resolved.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove outside the owned picture fixture'
    }
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop; break }
        catch {
            if ($attempt -eq 9) { $result.cleanupError = $_.Exception.Message }
            Start-Sleep -Milliseconds 500
        }
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if (-not $result.passed) { exit 1 }
