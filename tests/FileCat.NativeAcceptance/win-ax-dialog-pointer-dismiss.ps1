param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('Registry', 'Folder', 'Copy', 'Move', 'Delete')][string]$DialogKind = 'Registry',
    [switch]$MinimumOwner
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatDialogPointer {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(2, 0, 0, 0, 0);
        mouse_event(4, 0, 0, 0, 0);
    }
    public static uint ForegroundProcessId() {
        uint processId;
        GetWindowThreadProcessId(GetForegroundWindow(), out processId);
        return processId;
    }
}
'@

$validationRoot = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$app = [IO.Path]::GetFullPath($AppPath)
$root = [IO.Path]::GetFullPath($validationRoot) + [IO.Path]::DirectorySeparatorChar
if (!$app.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !(Test-Path -LiteralPath $app -PathType Leaf)) {
    throw 'Run a Git-built FileCat.exe from an isolated FileCatValidation folder.'
}
$fixture = Join-Path $validationRoot ('DialogPointerFixture\' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
New-Item -ItemType Directory -Force -Path $data, $source, $target | Out-Null
$sourceFile = Join-Path $source 'a.txt'
if ($DialogKind -in @('Copy', 'Move', 'Delete')) { [IO.File]::WriteAllText($sourceFile, 'dialog pointer source bytes') }
$sourceHash = if ($DialogKind -in @('Copy', 'Move', 'Delete')) { (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash } else { $null }
$dialogTitle = switch ($DialogKind) { 'Folder' { 'Create folder' } 'Copy' { 'Copy' } 'Move' { 'Move' } 'Delete' { 'Delete' } default { 'Create in Registry' } }
$process = $null

function Snapshot($window) {
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    return @($all)
}

function CloseButton($window) {
    $matches = @(Snapshot $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -eq 'Close dialog'
    })
    if ($matches.Count -gt 1) { throw 'More than one active dialog close control is exposed.' }
    return $(if ($matches.Count) { $matches[0] } else { $null })
}

function WaitForChooser($window, [bool]$visible) {
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 100
        $close = CloseButton $window
        if (($null -ne $close) -eq $visible) {
            if ($visible) {
                $names = @(Snapshot $window | ForEach-Object { $_.Current.Name })
                $expected = if ($DialogKind -in @('Copy', 'Move')) {
                    @($names | Where-Object { $_ -like "$DialogKind `"a.txt`"*" }).Count -gt 0 -and $names -contains 'Destination'
                } elseif ($DialogKind -eq 'Delete') {
                    $names -contains 'Delete' -and @($names | Where-Object { $_ -like '*"a.txt"*' }).Count -gt 0
                } else { $names -contains $dialogTitle }
                if (!$expected) { throw "A different dialog opened; expected $dialogTitle." }
            }
            return $close
        }
    }
    throw $(if ($visible) { "$dialogTitle did not open." } else { 'The dialog did not close after the native pointer click.' })
}

function EnsureForeground($process, $window) {
    [FileCatDialogPointer]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null
    [FileCatDialogPointer]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $frame = $window.Current.BoundingRectangle
    [FileCatDialogPointer]::Click([int]($frame.X + $frame.Width / 2), [int]($frame.Y + 14))
    Start-Sleep -Milliseconds 300
    $foreground = [FileCatDialogPointer]::ForegroundProcessId()
    if ($foreground -ne $process.Id) {
        throw "The isolated FileCat process $($process.Id) is not foreground (actual $foreground); refusing to send F7 to another app."
    }
}

function OpenDialog($process, $window) {
    EnsureForeground $process $window
    if ($DialogKind -in @('Copy', 'Move', 'Delete')) {
        $row = @(Snapshot $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -eq 'a.txt'
        })
        if ($row.Count -ne 1) { throw "The disposable $DialogKind source row is missing." }
        $pattern = $null
        if ($row[0].TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
        } else {
            $bounds = $row[0].Current.BoundingRectangle
            [FileCatDialogPointer]::Click([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2))
        }
        $buttons = @(Snapshot $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $_.Current.Name.StartsWith($DialogKind, [StringComparison]::Ordinal) -and $_.Current.IsEnabled
        } | Sort-Object { $_.Current.BoundingRectangle.Y })
        if ($buttons.Count -lt 1) { throw "The selected $DialogKind command is unavailable." }
        ([System.Windows.Automation.InvokePattern]$buttons[0].GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    } else {
        (New-Object -ComObject WScript.Shell).SendKeys('{F7}')
    }
    return WaitForChooser $window $true
}

try {
    $left = if ($DialogKind -eq 'Registry') { 'reg:HKCU\Software' } else { $source }
    $right = if ($DialogKind -eq 'Registry') { $validationRoot } else { $target }
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $left, '--right', $right) -PassThru
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and !$process.HasExited -and $deadline.Elapsed.TotalSeconds -lt 20)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The isolated FileCat window did not open.' }
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) { throw 'Unexpected executable owns the window.' }
    $windowDpi = [FileCatDialogPointer]::GetDpiForWindow($process.MainWindowHandle)
    if ($MinimumOwner) {
        # The app may apply its saved geometry after the native handle first appears.
        Start-Sleep -Seconds 1
        $scale = $windowDpi / 96.0
        $requestedWidth = [int][Math]::Round(640 * $scale)
        $requestedHeight = [int][Math]::Round(420 * $scale)
        [FileCatDialogPointer]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
        if (![FileCatDialogPointer]::MoveWindow($process.MainWindowHandle, 40, 40,
                $requestedWidth, $requestedHeight, $true)) {
            throw 'Could not resize the isolated FileCat window to its supported minimum.'
        }
        Start-Sleep -Milliseconds 500
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $ownerBounds = $window.Current.BoundingRectangle
    $ownerWidthDip = $ownerBounds.Width * 96 / $windowDpi
    $ownerHeightDip = $ownerBounds.Height * 96 / $windowDpi
    if ($MinimumOwner -and ($ownerWidthDip -gt 700 -or $ownerHeightDip -gt 480 -or
            $ownerWidthDip -lt 635 -or $ownerHeightDip -lt 415)) {
        throw "The requested minimum owner did not settle: $ownerWidthDip x $ownerHeightDip DIP."
    }
    Start-Sleep -Seconds 1

    $close = OpenDialog $process $window
    if ($close.Current.IsOffscreen -or !$close.Current.IsEnabled) { throw 'The close X is not reachable.' }
    if ($MinimumOwner) {
        $ownerBounds = $window.Current.BoundingRectangle
        $visibleActions = @(Snapshot $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $_.Current.IsEnabled -and !$_.Current.IsOffscreen -and
            $_.Current.BoundingRectangle.Width -gt 0 -and
            $_.Current.BoundingRectangle.Height -gt 0 -and
            $_.Current.Name -in @('Close dialog', 'Cancel', 'Create key', 'Create value')
        })
        if (@($visibleActions | Where-Object { $_.Current.Name -eq 'Close dialog' }).Count -ne 1 -or
            ($DialogKind -ne 'Registry' -and
             @($visibleActions | Where-Object { $_.Current.Name -eq 'Cancel' }).Count -ne 1)) {
            throw 'The minimum-size dialog did not expose its intended mouse cancellation controls.'
        }
        if ($DialogKind -eq 'Registry') {
            $choices = @(Snapshot $window | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
                ($_.Current.Name.StartsWith('Key', [StringComparison]::Ordinal) -or
                 $_.Current.Name.StartsWith('Value', [StringComparison]::Ordinal)) -and
                !$_.Current.IsOffscreen
            })
            if ($choices.Count -ne 2) {
                $names = @(Snapshot $window | Where-Object {
                    $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem
                } | ForEach-Object { $_.Current.Name } | Select-Object -Last 8) -join '; '
                throw "The Registry chooser did not expose both visible choices: $($choices.Count); list items: $names."
            }
            $visibleActions += $choices
        }
        foreach ($item in $visibleActions) {
            $area = $item.Current.BoundingRectangle
            if ($area.Left -lt $ownerBounds.Left -or $area.Top -lt $ownerBounds.Top -or
                $area.Right -gt $ownerBounds.Right -or $area.Bottom -gt $ownerBounds.Bottom) {
                throw "The minimum-size dialog action '$($item.Current.Name)' is outside the owner."
            }
        }
    }
    $help = $close.Current.HelpText
    if ($help -notlike '*Cancel this dialog*') { throw 'The close X lacks its accessible cancel explanation.' }
    $bounds = $close.Current.BoundingRectangle
    if ($bounds.Width -lt 25 -or $bounds.Height -lt 25) { throw 'The close X has an undersized native pointer target.' }
    $iconPixels = 0; $sumX = 0; $sumY = 0; $offsetX = 0; $offsetY = 0
    $capture = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($capture)
        try {
            $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0,
                [System.Drawing.Size]::new([int]$bounds.Width, [int]$bounds.Height))
        } finally { $graphics.Dispose() }
        $capture.Save($ReportPath + '.close.png', [System.Drawing.Imaging.ImageFormat]::Png)
        $corner = $capture.GetPixel(3, 3)
        $background = ($corner.R + $corner.G + $corner.B) / 3.0
        for ($y = 5; $y -lt $capture.Height - 5; $y++) {
            for ($x = 5; $x -lt $capture.Width - 5; $x++) {
                $pixel = $capture.GetPixel($x, $y)
                $brightness = ($pixel.R + $pixel.G + $pixel.B) / 3.0
                if ([Math]::Abs($brightness - $background) -ge 90) {
                    $iconPixels++; $sumX += $x; $sumY += $y
                }
            }
        }
        if ($iconPixels -lt 20 -or $iconPixels -gt ($capture.Width * $capture.Height / 3)) {
            throw "The close X could not be separated from its button background ($iconPixels pixels)."
        }
        $offsetX = $sumX / $iconPixels - ($capture.Width - 1) / 2.0
        $offsetY = $sumY / $iconPixels - ($capture.Height - 1) / 2.0
        if ([Math]::Abs($offsetX) -gt 1.5 -or [Math]::Abs($offsetY) -gt 1.5) {
            throw "The close X is visibly off center: $offsetX,$offsetY pixels."
        }
    } finally { $capture.Dispose() }
    [FileCatDialogPointer]::Click([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2))
    WaitForChooser $window $false | Out-Null

    $close = OpenDialog $process $window
    $frame = $window.Current.BoundingRectangle
    $outsideX = [int]($frame.X + 20)
    $outsideY = [int]($frame.Y + $frame.Height - 80)
    [FileCatDialogPointer]::Click($outsideX, $outsideY)
    WaitForChooser $window $false | Out-Null
    if (($DialogKind -eq 'Folder' -and @(Get-ChildItem -LiteralPath $source -Force).Count -ne 0) -or
        ($DialogKind -in @('Copy', 'Move', 'Delete') -and ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $sourceHash -or
            @(Get-ChildItem -LiteralPath $target -Force).Count -ne 0))) {
        throw 'The canceled dialog changed the disposable fixture.'
    }

    [pscustomobject]@{
        BuildSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
        WindowDpi = $windowDpi
        MinimumOwner = [bool]$MinimumOwner
        OwnerSizeDip = "$([Math]::Round($ownerWidthDip))x$([Math]::Round($ownerHeightDip))"
        DialogKind = $DialogKind
        DialogTitle = $dialogTitle
        DialogOpenedTwice = $true
        NativeCloseX = $true
        NativeBackdropClick = $true
        CloseAccessibleName = 'Close dialog'
        CloseAccessibleHelp = $help
        CloseBounds = "$($bounds.Width)x$($bounds.Height)"
        CloseIconPixels = $iconPixels
        CloseIconOffsetPixels = "$([Math]::Round($offsetX, 2)),$([Math]::Round($offsetY, 2))"
        OutsidePoint = "$outsideX,$outsideY"
        FixtureUnchanged = $true
        SourceSha256 = $sourceHash
        TargetEmpty = @(Get-ChildItem -LiteralPath $target -Force).Count -eq 0
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
catch {
    $_ | Out-String | Set-Content -LiteralPath ($ReportPath + '.error.txt') -Encoding UTF8
    throw
}
finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    $ownedRoot = [IO.Path]::GetFullPath((Join-Path $validationRoot 'DialogPointerFixture')) + [IO.Path]::DirectorySeparatorChar
    if ([IO.Path]::GetFullPath($fixture).StartsWith($ownedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
