param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$HarnessPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Run in the interactive Windows test desktop. Seed an isolated profile with
# the purple theme, create the screenshot's four-panel layout with real pointer
# input, and retain a native image as well as UI Automation value/bounds proof.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FileCatPanelMaskInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern void mouse_event(
        uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    public static void Click(int x, int y) {
        if (!SetCursorPos(x, y)) throw new InvalidOperationException("Cannot position the test pointer.");
        System.Threading.Thread.Sleep(80);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@

$app = [IO.Path]::GetFullPath($AppPath)
$harness = [IO.Path]::GetFullPath($HarnessPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$screenshot = [IO.Path]::ChangeExtension($report, '.png')
$run = Join-Path (Split-Path -Parent $report) ([IO.Path]::GetFileNameWithoutExtension($report) + '-fixture')
$result = [ordered]@{ Passed = $false; Theme = 'Psychedelic'; AppDllSha256 = $null;
    Filters = @(); Screenshot = $screenshot; FixtureIntact = $false; Error = $null }
$process = $null

function Find-Visible([string]$name, [int]$ownerProcessId) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $matches = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $condition)
    foreach ($item in $matches) {
        if ($item.Current.ProcessId -eq $ownerProcessId -and !$item.Current.IsOffscreen) { return $item }
    }
    throw "Visible native control '$name' was not found in the isolated FileCat process."
}

function Click-Element($element) {
    if (!$element) { throw 'The native pointer target is unavailable.' }
    $bounds = $element.Current.BoundingRectangle
    if ($bounds.Width -le 0 -or $bounds.Height -le 0) { throw 'The native pointer target has no bounds.' }
    [FileCatPanelMaskInput]::Click([int]($bounds.X + $bounds.Width / 2),
        [int]($bounds.Y + $bounds.Height / 2))
    Start-Sleep -Milliseconds 250
}

function Capture-Screen {
    if (Test-Path -LiteralPath $screenshot) { return }
    $width = [FileCatPanelMaskInput]::GetSystemMetrics(0)
    $height = [FileCatPanelMaskInput]::GetSystemMetrics(1)
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen(0, 0, 0, 0, [System.Drawing.Size]::new($width, $height)) }
        finally { $graphics.Dispose() }
        $bitmap.Save($screenshot, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

try {
    if (!(Test-Path -LiteralPath $app -PathType Leaf) -or
        !(Test-Path -LiteralPath $harness -PathType Leaf) -or
        (Test-Path -LiteralPath $report) -or (Test-Path -LiteralPath $screenshot) -or
        (Test-Path -LiteralPath $run)) { throw 'The isolated app, harness, or fresh report path is unavailable.' }
    New-Item -ItemType Directory -Path $run | Out-Null
    $fixturePath = Join-Path $run 'selection-fixture.json'
    & $harness seed-selection $fixturePath
    if ($LASTEXITCODE -ne 0) { throw 'The isolated fixture seeder failed.' }
    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    New-Item -ItemType Directory -Path $fixture.data | Out-Null
    '{"SchemaVersion":1,"Theme":"Psychedelic"}' |
        Set-Content -LiteralPath (Join-Path $fixture.data 'settings.json') -Encoding UTF8
    $result.AppDllSha256 = (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($app, '.dll')) -Algorithm SHA256).Hash
    # Windows paths cannot contain quotes; the seeded paths end in a folder
    # name rather than a slash, so enclosing each path also handles spaces in
    # the interactive user's profile on Windows PowerShell 5.1.
    $arguments = '--new-instance --data "' + $fixture.data + '" --left "' +
        $fixture.source + '" --right "' + $fixture.destination + '"'
    $process = Start-Process -FilePath $app -ArgumentList $arguments -PassThru
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -or $process.HasExited) { break }
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero -or $process.HasExited -or
        [IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'The exact test-built FileCat app did not open in the interactive desktop.'
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    [FileCatPanelMaskInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $filterCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Filter')
    if ($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $filterCondition).Count -ne 2) {
        throw 'The isolated app did not start with two panel filter fields.'
    }
    Click-Element (Find-Visible 'Panels' $process.Id)
    Click-Element (Find-Visible 'Add panel below' $process.Id)
    $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $destinationTab = @($buttons) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name.StartsWith('destination,', [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    Click-Element $destinationTab
    Click-Element (Find-Visible 'Panels' $process.Id)
    Click-Element (Find-Visible 'Add panel below' $process.Id)
    $filters = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $filterCondition))
    if ($filters.Count -ne 4) { throw "Expected four panel filters, got $($filters.Count)." }
    $result.Filters = @($filters | ForEach-Object {
        [pscustomobject]@{ Value = $_.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern).Current.Value;
            Bounds = [string]$_.Current.BoundingRectangle; Offscreen = $_.Current.IsOffscreen }
    })
    if (@($result.Filters | Where-Object { $_.Value -ne '*.*' -or $_.Offscreen }).Count) {
        throw 'A panel has an absent or hidden default mask.'
    }
    Capture-Screen
    $changed = @(@('a.txt', 'b.txt', 'c.txt', 'd.txt') | Where-Object {
        (Get-Content -LiteralPath (Join-Path $fixture.source $_) -Raw) -ne ('selection fixture ' + $_)
    })
    $result.FixtureIntact = $changed.Count -eq 0 -and
        @(Get-ChildItem -LiteralPath $fixture.destination).Count -eq 0
    if (!$result.FixtureIntact) { throw 'The owned source or destination fixture changed.' }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
    if ($process -and !$process.HasExited) {
        try { Capture-Screen } catch { $result.Error += '; screenshot: ' + $_.Exception.Message }
    }
} finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) {
            $process.Refresh()
            if (!$process.HasExited -and [IO.Path]::GetFullPath($process.MainModule.FileName) -ieq $app) {
                Stop-Process -Id $process.Id -Force
            }
        }
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
