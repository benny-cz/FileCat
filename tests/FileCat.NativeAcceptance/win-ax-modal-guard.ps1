param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatModalPointer {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(2, 0, 0, 0, 0);
        mouse_event(4, 0, 0, 0, 0);
    }
}
'@

$validationRoot = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$resolvedApp = [IO.Path]::GetFullPath($AppPath)
if (!$resolvedApp.StartsWith(([IO.Path]::GetFullPath($validationRoot) + [IO.Path]::DirectorySeparatorChar),
        [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedApp) -ine 'FileCat.exe') {
    throw 'Use an isolated FileCat.exe below the guest FileCatValidation directory.'
}
if (!(Test-Path -LiteralPath $resolvedApp -PathType Leaf)) { throw 'The isolated FileCat build is missing.' }

$fixture = Join-Path $validationRoot ('ModalGuardFixture\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$sourceFile = Join-Path $source 'a.txt'
$targetFile = Join-Path $target 'a.txt'
[IO.File]::WriteAllText($sourceFile, 'modal source remains')
$originalHash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash
$process = $null
try {
    $process = Start-Process -FilePath $resolvedApp -ArgumentList @('--new-instance', '--data', $data, '--left', $source, '--right', $target) -PassThru
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and !$process.HasExited -and $deadline.Elapsed.TotalSeconds -lt 15)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'FileCat did not open its main window.' }
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $resolvedApp) { throw 'The window belongs to another executable.' }
    Start-Sleep -Seconds 2
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    [FileCatModalPointer]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    [FileCatModalPointer]::SetForegroundWindow($process.MainWindowHandle) | Out-Null

    function Snapshot {
        $all = $null
        for ($read = 0; $read -lt 4 -and $null -eq $all; $read++) {
            try {
                $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)
            }
            catch [Runtime.InteropServices.COMException] {
                if ($read -eq 3) { throw }
                Start-Sleep -Milliseconds 250
            }
        }
        $buttons = @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button })
        [pscustomobject]@{
            All = $all
            Buttons = $buttons
            CancelCount = @($buttons | Where-Object { $_.Current.Name -eq 'Cancel' }).Count
            CopyReviewCount = @($all | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
                $_.Current.Name -like 'Copy "a.txt"*'
            }).Count
            MoveReviewCount = @($all | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
                $_.Current.Name -like 'Move "a.txt"*'
            }).Count
            DeleteReviewCount = @($all | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
                $_.Current.Name -like '*a.txt*Recycle Bin*'
            }).Count
            DestinationCount = @($all | Where-Object { $_.Current.Name -eq 'Destination' }).Count
        }
    }
    function Button([string]$name, [bool]$toolbar) {
        $matches = @( (Snapshot).Buttons | Where-Object { $_.Current.Name -like "$name*" } |
            Sort-Object { $_.Current.BoundingRectangle.Y })
        if ($matches.Count -lt 2) { throw "$name background controls are missing." }
        if ($toolbar) { return $matches[0] }
        return $matches[-1]
    }
    function ClickElement($element) {
        $bounds = $element.Current.BoundingRectangle
        [FileCatModalPointer]::Click([int]($bounds.X + $bounds.Width / 2),
            [int]($bounds.Y + $bounds.Height / 2))
    }

    $fileRow = @((Snapshot).All | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq 'a.txt'
    })[0]
    if (!$fileRow) { throw 'The fixture source row is missing.' }
    try { $fileRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
    catch { ClickElement $fileRow }
    Start-Sleep -Milliseconds 250
    $journeys = @()
    foreach ($verb in @('Copy', 'Move', 'Delete')) {
        (Button $verb $true).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $before = Snapshot
        $expectedDestination = if ($verb -eq 'Delete') { 0 } else { 1 }
        $reviewProperty = "${verb}ReviewCount"
        for ($attempt = 0; $attempt -lt 20 -and
            ($before.CancelCount -ne 1 -or $before.$reviewProperty -ne 1 -or
             $before.DestinationCount -ne $expectedDestination);
            $attempt++) {
            Start-Sleep -Milliseconds 250
            $before = Snapshot
        }
        if ($before.CancelCount -ne 1 -or $before.$reviewProperty -ne 1 -or
            $before.DestinationCount -ne $expectedDestination) {
            throw "Exactly one $verb review must be visible (Cancel=$($before.CancelCount), Review=$($before.$reviewProperty), Destination=$($before.DestinationCount))."
        }

        $attempts = @()
        foreach ($choice in @(@('Copy', $true), @('Move', $true), @('Delete', $true),
                             @('Copy', $false), @('Move', $false), @('Delete', $false))) {
            $control = Button $choice[0] $choice[1]
            # Invoke bypasses the pointer shield. The main command gate must keep this modal's decision ownership.
            $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 300
            $after = Snapshot
            if ($after.CancelCount -ne 1 -or $after.$reviewProperty -ne 1 -or
                $after.DestinationCount -ne $expectedDestination) {
                throw "$($choice[0]) changed the open $verb review."
            }
            if (Test-Path -LiteralPath $targetFile) { throw "$($choice[0]) published a target behind the review." }
            if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $originalHash) {
                throw "$($choice[0]) changed the source behind the review."
            }
            $attempts += [pscustomobject]@{ Command = $choice[0]; Toolbar = $choice[1]; ModalStillSingle = $true }
        }
        $cancel = @((Snapshot).Buttons | Where-Object { $_.Current.Name -eq 'Cancel' })[0]
        ClickElement $cancel
        Start-Sleep -Milliseconds 350
        $closed = Snapshot
        if ($closed.CancelCount -ne 0 -or $closed.$reviewProperty -ne 0) { throw "Cancel did not close the $verb review." }
        if (Test-Path -LiteralPath $targetFile) { throw "A target appeared after $verb Cancel." }
        if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $originalHash) { throw "Source bytes changed after $verb Cancel." }
        $journeys += [pscustomobject]@{
            Review = $verb
            Before = @{ Cancel = $before.CancelCount; Review = $before.$reviewProperty; Destination = $before.DestinationCount }
            Attempts = $attempts
            AfterCancel = @{ Cancel = $closed.CancelCount; Review = $closed.$reviewProperty }
        }
    }
    [pscustomobject]@{
        Passed = $true
        App = $resolvedApp
        AppSha256 = (Get-FileHash -LiteralPath $resolvedApp -Algorithm SHA256).Hash
        Fixture = $fixture
        Journeys = $journeys
        SourceSha256 = $originalHash
        TargetAbsent = $true
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
catch {
    ($_ | Out-String) | Set-Content -LiteralPath ($ReportPath + '.error.txt') -Encoding UTF8
    throw
}
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        $process.WaitForExit(5000) | Out-Null
    }
}
