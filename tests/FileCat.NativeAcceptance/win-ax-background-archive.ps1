param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [ValidateSet('Archive', 'Registry')][string]$Provider = 'Archive',
    [switch]$BackgroundOnly
)

# Right-click owned read-only archive or Registry whitespace with a marked row.
# The optional keyboard item-menu diagnostic remains separate from this route.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatArchiveBackgroundInput {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, int flags, UIntPtr extra);
    public static void RightClick(int x, int y) {
        if (!SetCursorPos(x, y)) throw new InvalidOperationException("Cannot target archive whitespace.");
        System.Threading.Thread.Sleep(100);
        mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(75);
        mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
    }
    public static void ShiftF10() {
        keybd_event(0x10, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        keybd_event(0x79, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        keybd_event(0x79, 0, 2, UIntPtr.Zero);
        keybd_event(0x10, 0, 2, UIntPtr.Zero);
    }
    public static void MenuKey() {
        keybd_event(0x5D, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        keybd_event(0x5D, 0, 2, UIntPtr.Zero);
    }
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !$report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    (Test-Path -LiteralPath $report)) {
    throw 'Use an isolated Git-built FileCat.exe and fresh validation report.'
}
$id = [guid]::NewGuid().ToString('N')
$registryRoot = "Registry::HKEY_CURRENT_USER\Software\FileCatValidation\BackgroundContext\$id"
$registryLocation = "reg:HKCU\Software\FileCatValidation\BackgroundContext\$id"
$fixture = Join-Path $validation ('ArchiveBackgroundFixture\' + $id)
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
$staging = Join-Path $fixture 'staging'
New-Item -ItemType Directory -Force -Path $source, $target, $data, $staging | Out-Null
[IO.File]::WriteAllText((Join-Path $staging 'member.txt'), 'owned archive member')
$archive = Join-Path $source 'owned.tar'
& "$env:WINDIR\System32\tar.exe" -cf $archive -C $staging member.txt
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw 'Could not create the owned read-only TAR fixture.'
}
$originalHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($Provider -eq 'Registry') {
    New-Item -Path "$registryRoot\member" -Force | Out-Null
}
$result = [ordered]@{
    Passed = $false
    Provider = $Provider
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    ArchiveSha256 = $originalHash
    ArchiveList = $null
    Background = @()
    ArchiveBackgroundPassed = $false
    KeyboardItem = @()
    ShiftF10Opened = $false
    MenuKeyOpened = $false
    KeyboardDiagnostic = $null
    MarkRetained = $false
    FocusRetained = $false
    Error = $null
}
$process = $null

function Descendants($owner) {
    @($owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Wait-For([scriptblock]$condition, [string]$description, [int]$seconds = 20) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $condition; if ($null -ne $value -and $value -ne $false) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 125
    }
    throw "Timed out waiting for $description"
}
function Menu-Items([int]$processId) {
    $tops = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    $items = @()
    foreach ($top in $tops) {
        if ($top.Current.ProcessId -ne $processId) { continue }
        $items += @(Descendants $top | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and
            !$_.Current.IsOffscreen
        })
    }
    return $items
}

try {
    $left = if ($Provider -eq 'Registry') { $registryLocation } else { $source }
    $rowName = if ($Provider -eq 'Registry') { 'member' } else { 'owned.tar' }
    $memberName = if ($Provider -eq 'Registry') { 'member' } else { 'member.txt' }
    $process = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $target)
    $main = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'owned FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) {
        throw 'An unexpected executable owns the FileCat window.'
    }
    [FileCatArchiveBackgroundInput]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    [FileCatArchiveBackgroundInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $zipRow = Wait-For { @(Descendants $main | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq $rowName
    }) | Select-Object -First 1 } 'owned provider row'
    $zipRow.SetFocus()
    if ($Provider -eq 'Archive') { [Windows.Forms.SendKeys]::SendWait('{ENTER}') }
    $list = Wait-For { @(Descendants $main | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
        $_.Current.Name -like $(if ($Provider -eq 'Registry') { "Panel 1,*$id*" } else { 'Panel 1,*owned.tar*' })
    }) | Select-Object -First 1 } 'provider list'
    $result.ArchiveList = $list.Current.Name
    $member = Wait-For { @(Descendants $list | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq $memberName
    }) | Select-Object -First 1 } 'provider member row'
    $mark = [System.Windows.Automation.SelectionItemPattern]$member.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $mark.AddToSelection()
    $member.SetFocus()
    if (!$mark.Current.IsSelected) { throw 'The owned provider member was not marked.' }
    $bounds = $list.Current.BoundingRectangle
    if ($bounds.Width -lt 120 -or $bounds.Height -lt 180 -or
        $bounds.Left -lt 0 -or $bounds.Top -lt 0) { throw 'Archive list has no whitespace target.' }
    $clickX = [int]($bounds.Left + 60)
    $clickY = [int]($bounds.Bottom - 35)
    $memberBounds = $member.Current.BoundingRectangle
    if ($clickY -lt $memberBounds.Bottom + 25) { throw 'The pointer target intersects the member row.' }
    [FileCatArchiveBackgroundInput]::RightClick($clickX, $clickY)
    $background = Wait-For {
        $items = @(Menu-Items $process.Id)
        if (@($items | Where-Object { $_.Current.Name -eq 'Copy location name' }).Count) { $items }
    } 'archive background menu'
    $result.Background = @($background | ForEach-Object {
        [ordered]@{ Name = $_.Current.Name; Enabled = $_.Current.IsEnabled;
            Help = $_.Current.HelpText }
    })
    $names = @($result.Background | ForEach-Object { $_.Name })
    $ellipsis = [char]0x2026
    if ($names -notcontains 'Reread' -or $names -notcontains 'Copy location name' -or
        $names -contains "Delete$ellipsis" -or $names -contains "Move$ellipsis" -or
        $names -contains "Rename$ellipsis") { throw 'Archive background menu has the wrong actions.' }
    foreach ($action in @('Paste items or go to pasted path', "Create folder$ellipsis")) {
        $item = @($result.Background | Where-Object { $_.Name -eq $action })
        if ($item.Count -ne 1 -or $item[0].Enabled -or [string]::IsNullOrWhiteSpace($item[0].Help)) {
            throw "Archive background action $action lacks a disabled reason."
        }
    }
    [Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 200
    $result.MarkRetained = $mark.Current.IsSelected
    $result.FocusRetained = [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq $memberName
    if (!$result.MarkRetained -or !$result.FocusRetained) {
        throw 'Dismissing the background menu changed mark or focus.'
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $originalHash -or
        @(Get-ChildItem -LiteralPath $target -Force).Count -ne 0 -or
        ($Provider -eq 'Registry' -and @(Get-ChildItem -LiteralPath $registryRoot).Count -ne 1)) {
        throw 'Archive background context changed the owned fixture.'
    }
    $result.ArchiveBackgroundPassed = $true
    if (!$BackgroundOnly) {
    [FileCatArchiveBackgroundInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $member.SetFocus()
    [FileCatArchiveBackgroundInput]::ShiftF10()
    try {
        $itemMenu = Wait-For {
            $items = @(Menu-Items $process.Id)
            if (@($items | Where-Object { $_.Current.Name -eq 'Open' }).Count) { $items }
        } 'Shift+F10 item context menu' 3
        $result.ShiftF10Opened = $true
    } catch {
        [FileCatArchiveBackgroundInput]::MenuKey()
        $itemMenu = Wait-For {
            $items = @(Menu-Items $process.Id)
            if (@($items | Where-Object { $_.Current.Name -eq 'Open' }).Count) { $items }
        } 'Menu-key item context menu' 5
        $result.MenuKeyOpened = $true
    }
    $result.KeyboardItem = @($itemMenu | ForEach-Object { $_.Current.Name })
    if ($result.KeyboardItem -contains 'Copy location name' -or
        $result.KeyboardItem -notcontains 'Open') {
        throw 'Keyboard item context was confused with archive whitespace.'
    }
    [Windows.Forms.SendKeys]::SendWait('{ESC}')
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $originalHash -or
        @(Get-ChildItem -LiteralPath $target -Force).Count -ne 0) {
        throw 'Archive context input changed the owned fixture.'
    }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
    if ($process -and !$process.HasExited -and $main) {
        try {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            $result.KeyboardDiagnostic = [ordered]@{
                Focused = $focused.Current.Name
                ForegroundHandle = [FileCatArchiveBackgroundInput]::GetForegroundWindow().ToInt64()
                MainHandle = $process.MainWindowHandle.ToInt64()
                MenuItems = @(Menu-Items $process.Id | ForEach-Object { $_.Current.Name })
            }
            $rect = [FileCatArchiveBackgroundInput+Rect]::new()
            if ([FileCatArchiveBackgroundInput]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
                $bitmap = [Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
                try {
                    $graphics = [Drawing.Graphics]::FromImage($bitmap)
                    try { $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size) }
                    finally { $graphics.Dispose() }
                    $image = [IO.Path]::ChangeExtension($report, '.png')
                    $bitmap.Save($image, [Drawing.Imaging.ImageFormat]::Png)
                    $result.KeyboardDiagnostic.Screenshot = $image
                } finally { $bitmap.Dispose() }
            }
        } catch { $result.KeyboardDiagnostic = @{ Error = $_.Exception.Message } }
    }
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit(10000) | Out-Null }
    if ($Provider -eq 'Registry' -and $registryRoot.StartsWith(
        'Registry::HKEY_CURRENT_USER\Software\FileCatValidation\BackgroundContext\',
        [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $registryRoot)) {
        Remove-Item -LiteralPath $registryRoot -Recurse -Force
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { throw $result.Error }
