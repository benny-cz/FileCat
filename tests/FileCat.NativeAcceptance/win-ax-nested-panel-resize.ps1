param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# One bounded native Windows 150% route for AC-S2-030. A four-panel nested
# arrangement is loaded from test-owned Git-schema JSON, then keyboard and
# mouse resize the real splitters. The saved arrangement restores geometry.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatNestedResizeNative {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
    public static void Drag(int x, int y, int dx) {
        SetCursorPos(x,y); mouse_event(2,0,0,0,0);
        for (int step=1; step<=8; step++) {
            SetCursorPos(x + dx*step/8,y);
            System.Threading.Thread.Sleep(45);
        }
        mouse_event(4,0,0,0,0);
    }
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notlike '*VMware*' -or
    (Get-Process -Id $PID).SessionId -eq 0 -or
    !$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an idle, signed-in Windows VMware guest and its Git-built app.'
}

function Wait-For([scriptblock]$check, [string]$description, [int]$seconds = 15) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $seconds) {
        try { $found = & $check; if ($found) { return $found } }
        catch {
            $inner = $_.Exception
            while ($inner -and $inner -isnot [Runtime.InteropServices.COMException]) { $inner = $inner.InnerException }
            if (!$inner) { throw }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $description."
}
function Find-Name($owner, [string]$name, $type) {
    $names = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $controls = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
    $element = $owner.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new($names, $controls))
    if ($element -and !$element.Current.IsOffscreen) { $element }
}
function Slider($owner, [string]$orientation) {
    $all = $owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    @($all | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Slider -and
        $_.Current.Name -like "$orientation panel boundary*" -and !$_.Current.IsOffscreen
    } | Select-Object -First 1)[0]
}
function Snapshot($element) {
    $rect = $element.Current.BoundingRectangle
    $range = [System.Windows.Automation.RangeValuePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.RangeValuePattern]::Pattern)
    [ordered]@{ Name = $element.Current.Name; Share = $range.Current.Value;
        Bounds = @($rect.Left,$rect.Top,$rect.Width,$rect.Height);
        Help = $element.Current.HelpText; Focused = $element.Current.HasKeyboardFocus }
}
function Lists($owner) {
    $all = $owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and
        $_.Current.Name -match '^Panel [1-4],' -and !$_.Current.IsOffscreen } |
        Sort-Object { [int]([regex]::Match($_.Current.Name, '^Panel ([1-4])').Groups[1].Value) })
}
function Command($owner, $process, [string]$name) {
    [FileCatNestedResizeNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('^+p')
    $search = Wait-For { Find-Name $owner 'Search commands' ([System.Windows.Automation.ControlType]::Edit) } 'command search'
    ([System.Windows.Automation.ValuePattern]$search.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue($name)
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
}

$fixture = Join-Path (Join-Path $validation 'NestedPanelResize') ([guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
$paths = @(1..4 | ForEach-Object { Join-Path $fixture "panel-$_" })
New-Item -ItemType Directory -Path $data -Force | Out-Null
$originalHashes = @()
foreach ($path in $paths) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    foreach ($name in @('a.txt','b.txt','c.txt')) {
        [IO.File]::WriteAllText((Join-Path $path $name), "$path/$name`n")
    }
    $originalHashes += @(Get-ChildItem -LiteralPath $path -File | Sort-Object Name |
        ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
}
$ids = @(1..4 | ForEach-Object { "native-panel-$_" })
function Leaf([int]$index) { @{ PanelId = $ids[$index]; Size = 1; Children = @() } }
$state = [ordered]@{
    SchemaVersion = 1; Name = 'Native nested panels'
    Panels = @(0..3 | ForEach-Object {
        @{ Id = $ids[$_]; TargetPanelId = $ids[($_ + 1) % 4]; ActiveTab = 0;
           Tabs = @(@{ Location = @{ s = 'fs'; p = $paths[$_] } }) }
    })
    ActivePanelId = $ids[0]
    Tree = @{ Stacked = $false; Size = 1; Children = @(
        @{ Stacked = $true; Size = 1; Children = @((Leaf 0),(Leaf 1)) },
        @{ Stacked = $true; Size = 1; Children = @((Leaf 2),(Leaf 3)) }
    ) }
}
$workspaces = Join-Path $data 'workspaces'
New-Item -ItemType Directory -Path $workspaces -Force | Out-Null
$saved = Join-Path $workspaces 'Native nested panels.json'
$state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $saved -Encoding UTF8
$savedHash = (Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash
$result = [ordered]@{ Passed = $false; Stage = 'startup'; Fixture = $fixture;
    AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash;
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash;
    Dpi = $null; WindowBounds = $null; PanelLists = $null;
    HorizontalInitial = $null; HorizontalChanged = $null; HorizontalCanceled = $null;
    VerticalInitial = $null; VerticalCommitted = $null; VerticalPointer = $null;
    VerticalRestored = $null; VisibleRows = $null; SavedExact = $false;
    SourcesExact = $false; Error = $null; DebugUi = $null }
$process = $null
$window = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance','--data',$data,
        '--left',$paths[0],'--right',$paths[1]) -PassThru
    $window = Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
    } 'FileCat window'
    $result.Dpi = [FileCatNestedResizeNative]::GetDpiForWindow($process.MainWindowHandle)
    [FileCatNestedResizeNative]::MoveWindow($process.MainWindowHandle, 40, 40, 1450, 1200, $true) | Out-Null
    [FileCatNestedResizeNative]::SetCursorPos(10, 10) | Out-Null
    $rect = $window.Current.BoundingRectangle
    $result.WindowBounds = @($rect.Left,$rect.Top,$rect.Width,$rect.Height)

    $result.Stage = 'open four-panel nested workspace'
    Command $window $process 'Open workspace'
    Wait-For { Find-Name $window 'Open workspace' ([System.Windows.Automation.ControlType]::List) } 'owned workspace chooser' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $lists = Wait-For { $found = Lists $window; if ($found.Count -eq 4) { $found } } 'four named panel lists'
    $result.PanelLists = @($lists | ForEach-Object {
        $r = $_.Current.BoundingRectangle
        @{ Name = $_.Current.Name; Bounds = @($r.Left,$r.Top,$r.Width,$r.Height) }
    })
    $result.VisibleRows = @($lists | ForEach-Object {
        $list = $_; $bounds = $list.Current.BoundingRectangle
        $rows = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        @($rows | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -in @('a.txt','b.txt','c.txt') -and !$_.Current.IsOffscreen -and
            $_.Current.BoundingRectangle.Top -ge $bounds.Top -and
            $_.Current.BoundingRectangle.Bottom -le $bounds.Bottom + 1 }).Count
    })

    $horizontal = Wait-For { Slider $window 'Horizontal' } 'horizontal nested splitter'
    $vertical = Wait-For { Slider $window 'Vertical' } 'vertical outer splitter'
    $result.HorizontalInitial = Snapshot $horizontal
    $result.VerticalInitial = Snapshot $vertical
    $result.Stage = 'keyboard horizontal adjustment and Escape'
    Command $window $process 'Resize panel'
    Wait-For { $f = [System.Windows.Automation.AutomationElement]::FocusedElement;
        if ($f -and $f.Current.ControlType -eq [System.Windows.Automation.ControlType]::Slider -and
            $f.Current.Name -like 'Horizontal panel boundary*') { $f } } 'focused horizontal splitter' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    $result.HorizontalChanged = Snapshot (Wait-For {
        $s = Slider $window 'Horizontal'; if ($s -and (Snapshot $s).Share -gt $result.HorizontalInitial.Share) { $s }
    } 'changed horizontal share')
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    $result.HorizontalCanceled = Snapshot (Wait-For {
        $s = Slider $window 'Horizontal'; if ($s -and (Snapshot $s).Share -eq $result.HorizontalInitial.Share) { $s }
    } 'restored horizontal share')

    $result.Stage = 'keyboard vertical commit and native pointer drag'
    Command $window $process 'Resize panel'
    Wait-For { $f = [System.Windows.Automation.AutomationElement]::FocusedElement;
        if ($f -and $f.Current.Name -like 'Horizontal panel boundary*') { $f } } 'focused nested splitter' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
    Wait-For { $f = [System.Windows.Automation.AutomationElement]::FocusedElement;
        if ($f -and $f.Current.Name -like 'Vertical panel boundary*') { $f } } 'focused outer splitter' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('{RIGHT}')
    $result.VerticalCommitted = Snapshot (Wait-For {
        $s = Slider $window 'Vertical'; if ($s -and (Snapshot $s).Share -gt $result.VerticalInitial.Share) { $s }
    } 'changed vertical share')
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $vertical = Slider $window 'Vertical'
    $r = $vertical.Current.BoundingRectangle
    [FileCatNestedResizeNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [FileCatNestedResizeNative]::Drag([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2), 48)
    $result.VerticalPointer = Snapshot (Wait-For {
        $s = Slider $window 'Vertical'; if ($s -and (Snapshot $s).Share -gt $result.VerticalCommitted.Share) { $s }
    } 'pointer-adjusted vertical share')

    $result.Stage = 'restore named geometry and inspect all lists'
    Command $window $process 'Open workspace'
    Wait-For { Find-Name $window 'Open workspace' ([System.Windows.Automation.ControlType]::List) } 'restore chooser' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $result.VerticalRestored = Snapshot (Wait-For {
        $s = Slider $window 'Vertical'; if ($s -and (Snapshot $s).Share -eq $result.VerticalInitial.Share) { $s }
    } 'restored vertical share')
    $result.SavedExact = (Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -eq $savedHash
    $hashes = @(foreach ($path in $paths) {
        Get-ChildItem -LiteralPath $path -File | Sort-Object Name |
            ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $result.SourcesExact = @($hashes).Count -eq 12 -and
        [string]::Join(',', $hashes) -ceq [string]::Join(',', $originalHashes)
    $result.Passed = ($result.Dpi -eq 144 -and $result.PanelLists.Count -eq 4 -and
        @($result.VisibleRows | Where-Object { $_ -ge 2 }).Count -eq 4 -and
        $result.HorizontalChanged.Share -gt $result.HorizontalInitial.Share -and
        $result.HorizontalCanceled.Share -eq $result.HorizontalInitial.Share -and
        [math]::Abs($result.HorizontalCanceled.Bounds[1] - $result.HorizontalInitial.Bounds[1]) -le 1 -and
        $result.VerticalCommitted.Share -gt $result.VerticalInitial.Share -and
        $result.VerticalPointer.Share -gt $result.VerticalCommitted.Share -and
        $result.VerticalRestored.Share -eq $result.VerticalInitial.Share -and
        [math]::Abs($result.VerticalRestored.Bounds[0] - $result.VerticalInitial.Bounds[0]) -le 1 -and
        $result.HorizontalInitial.Name -like '*panels 1 and 2*' -and
        $result.VerticalInitial.Name -like '*panels 1, 2 and 3, 4*' -and
        $result.SavedExact -and $result.SourcesExact)
    $result.Stage = 'complete'
}
catch {
    $result.Error = $_.Exception.Message
    if ($window) {
        try {
            $result.DebugUi = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition) |
                Where-Object { $_.Current.Name -and !$_.Current.IsOffscreen } |
                Select-Object -First 70 |
                ForEach-Object { $_.Current.ControlType.ProgrammaticName + ': ' + $_.Current.Name })
        } catch { }
    }
}
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    }
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $report -Encoding UTF8
    $ownedRoot = [IO.Path]::GetFullPath((Join-Path $validation 'NestedPanelResize')) + [IO.Path]::DirectorySeparatorChar
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (!$resolvedFixture.StartsWith($ownedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture cleanup escaped the owned validation root.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
if (!$result.Passed) { exit 1 }
