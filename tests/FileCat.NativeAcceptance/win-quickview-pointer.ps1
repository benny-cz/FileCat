param(
    [Parameter(Mandatory = $true)][string] $AppPath,
    [Parameter(Mandatory = $true)][string] $ReportPath
)

trap {
    $_ | Out-String | Set-Content -LiteralPath ($ReportPath + '.error.txt') -Encoding UTF8
    exit 1
}

# Run inside the logged-in Windows VMware guest. The only pointer input goes
# to a test-owned FileCat process and an owned, empty source file.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FileCatPointerInput {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@

function Wait-For([scriptblock] $Check, [string] $Description, [int] $Seconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $value = & $Check
        if ($null -ne $value) { return $value }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $Description"
}

function Find-Element($Root, [string] $Name, $ControlType) {
    foreach ($element in $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                                      [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($element.Current.Name -eq $Name -and
            ($null -eq $ControlType -or $element.Current.ControlType -eq $ControlType)) {
            return $element
        }
    }
    return $null
}

function Send-CtrlQ {
    [FileCatPointerInput]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    [FileCatPointerInput]::keybd_event(0x51, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [FileCatPointerInput]::keybd_event(0x51, 0, 2, [UIntPtr]::Zero)
    [FileCatPointerInput]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
}

$validation = (Resolve-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'FileCatValidation')).Path
$app = (Resolve-Path -LiteralPath $AppPath).Path
$repo = (Get-Item -LiteralPath $app).Directory
while ($null -ne $repo -and -not (Test-Path -LiteralPath (Join-Path $repo.FullName '.git'))) {
    $repo = $repo.Parent
}
if ($null -eq $repo) { throw 'The app is not inside the isolated Git checkout' }
$gitMarker = Join-Path $repo.FullName '.git'
if ((Get-Item -LiteralPath $gitMarker -Force).PSIsContainer) {
    $gitDirectory = $gitMarker
} else {
    $markerText = (Get-Content -LiteralPath $gitMarker -Raw -Force).Trim()
    if (-not $markerText.StartsWith('gitdir: ', [StringComparison]::Ordinal)) {
        throw 'The isolated checkout has no readable Git directory'
    }
    $gitDirectory = $markerText.Substring(8)
    if (-not [IO.Path]::IsPathRooted($gitDirectory)) {
        $gitDirectory = Join-Path $repo.FullName $gitDirectory
    }
}
$reportParent = (Resolve-Path -LiteralPath (Split-Path -Parent $ReportPath)).Path
if (-not $app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $reportParent -ne $validation -or (Test-Path -LiteralPath $ReportPath) -or
    [IO.Path]::GetFileName($app) -ne 'FileCat.exe') {
    throw 'Use the isolated Git app and a fresh report in FileCatValidation'
}
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat instances before this isolated check'
}
$fixture = Join-Path (Join-Path $validation 'QuickViewPointer') ([Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
@($source, $target, $data) | ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
$item = Join-Path $source 'a-empty.txt'
[IO.File]::WriteAllBytes($item, [byte[]]@())
$result = [ordered]@{
    passed = $false
    stage = 'startup'
    fixture = $fixture
    gitHead = (Get-Content -LiteralPath (Join-Path $gitDirectory 'HEAD') -Raw).Trim()
    appDllSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll')).Hash.ToLowerInvariant()
    sourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $item).Hash.ToLowerInvariant()
}
$process = $null
$window = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', "`"$data`"",
        '--left', "`"$source`"", '--right', "`"$target`"") -PassThru -WindowStyle Normal
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $window = Wait-For {
        foreach ($candidate in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
            if ($candidate.Current.Name -like 'FileCat*') { return $candidate }
        }
        return $null
    } 'owned FileCat main window'
    $result.stage = 'focus owned empty file and open Quick View'
    $row = Wait-For {
        foreach ($candidate in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::ListItem))) {
            if ($candidate.Current.Name -eq 'a-empty.txt' -or
                $candidate.Current.Name -like 'a-empty.txt,*') { return $candidate }
        }
        return $null
    } 'owned empty-file row'
    $row.SetFocus()
    [FileCatPointerInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Send-CtrlQ
    $result.stage = 'physical pointer activation of full-viewer action'
    $button = Wait-For {
        $candidate = Find-Element $window 'Open focused file in full viewer' ([System.Windows.Automation.ControlType]::Button)
        if ($null -ne $candidate -and -not $candidate.Current.IsOffscreen) { return $candidate }
        return $null
    } 'visible Quick View full-viewer button'
    $rect = $button.Current.BoundingRectangle
    if ($rect.Width -lt 30 -or $rect.Height -lt 20) { throw "Full-viewer button has unusable bounds: $rect" }
    $x = [int][Math]::Round($rect.X + $rect.Width / 2)
    $y = [int][Math]::Round($rect.Y + $rect.Height / 2)
    if (-not [FileCatPointerInput]::SetCursorPos($x, $y)) { throw 'Cannot move the guest pointer' }
    Start-Sleep -Milliseconds 100
    $point = [FileCatPointerInput+Point]::new()
    [FileCatPointerInput]::GetCursorPos([ref]$point) | Out-Null
    $result.pointerTarget = @($x, $y)
    $result.pointerActual = @($point.X, $point.Y)
    $result.buttonBounds = @($rect.X, $rect.Y, $rect.Width, $rect.Height)
    if ([Math]::Abs($point.X - $x) -gt 2 -or [Math]::Abs($point.Y - $y) -gt 2) {
        throw 'The guest pointer did not reach the Quick View button'
    }
    [FileCatPointerInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [FileCatPointerInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    $viewer = Wait-For {
        foreach ($candidate in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
            if ($candidate.Current.Name -like 'a-empty.txt*FileCat Viewer') { return $candidate }
        }
        return $null
    } 'pointer-opened current-file viewer'
    $result.viewerTitle = $viewer.Current.Name
    $result.sourceUnchanged = ((Get-FileHash -Algorithm SHA256 -LiteralPath $item).Hash.ToLowerInvariant() -eq
                               $result.sourceSha256)
    $result.targetEmpty = -not (Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)
    $result.passed = [bool]($result.sourceUnchanged -and $result.targetEmpty)
    $result.stage = 'complete'
} catch {
    $result.error = $_.Exception.Message
    if ($null -ne $window) {
        try {
            $entries = [System.Collections.Generic.List[object]]::new()
            foreach ($element in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)) {
                if ($element.Current.Name) {
                    $entries.Add([ordered]@{ type = $element.Current.ControlType.ProgrammaticName;
                                             name = $element.Current.Name })
                }
                if ($entries.Count -ge 120) { break }
            }
            $result.uiaAtFailure = $entries.ToArray()
        } catch { $result.uiaDiagnosticError = $_.Exception.Message }
    }
} finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        try { Wait-Process -Id $process.Id -Timeout 5 -ErrorAction Stop } catch { }
    }
    $ownedParent = (Resolve-Path -LiteralPath (Join-Path $validation 'QuickViewPointer')).Path
    $ownedFixture = (Resolve-Path -LiteralPath $fixture).Path
    if (-not $ownedFixture.StartsWith($ownedParent + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a path outside the owned pointer fixture'
    }
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try {
            Remove-Item -LiteralPath $ownedFixture -Recurse -Force -ErrorAction Stop
            break
        } catch {
            if ($attempt -eq 9) { $result.cleanupError = $_.Exception.Message }
            Start-Sleep -Milliseconds 500
        }
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if (-not $result.passed) { exit 1 }
