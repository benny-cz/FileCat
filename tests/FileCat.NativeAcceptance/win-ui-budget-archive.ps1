param(
    [Parameter(Mandatory)][string] $HostPath,
    [Parameter(Mandatory)][string] $WorkerPath,
    [Parameter(Mandatory)][string] $ReportPath,
    [int] $Files = 20000
)

# Archive-writing UI budget (plan section 11.8, AT16): "no archive work on the UI thread and no repeated callback exceeding
# 50 ms due to archive progress/capability updates", measured in the real interface through the never-shipped
# qualification host and its --ui-budget probe (a 10 ms UI-thread timer that notes each over-budget gap with its time).
# In the interactive desktop, with keys going only to the owned FileCat window, the run records UTC windows for:
#   idle         no input;
#   navigation   Down/Up in a folder (no archive work), the baseline for key handling;
#   pack         packing a folder of $Files small files as TAR + gzip, with Down/Up kept going until the archive exists;
#   open         opening that archive (its listing and write capability are built), with Down/Up going;
#   add          F5 of one file into it: the format scan, the member review and the job, with Tab kept going;
# and attributes each gap over 50 ms to its window (and to garbage collection when a pause covers most of it).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatBudgetInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static void ClickTitle(IntPtr h) {
        RECT r; GetWindowRect(h, out r);
        double scale = GetDpiForWindow(h) / 96.0;
        SetCursorPos(Math.Max(r.Left + (int)(40 * scale), r.Right - (int)(300 * scale)), r.Top + (int)(12 * scale));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@
[void][FileCatBudgetInput]::SetThreadDpiAwarenessContext([IntPtr](-4))
$TreeScope = [System.Windows.Automation.TreeScope]
$AE = [System.Windows.Automation.AutomationElement]
function Descendants($element) { @($element.FindAll($TreeScope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) }
function Await([scriptblock] $check, [string] $description, [int] $seconds = 20) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $check; if ($null -ne $value -and $value -ne $false) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for $description"
}
function In($root, [string] $name, [string] $type = $null) {
    @(Descendants $root | Where-Object {
        $_.Current.Name -eq $name -and (-not $type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$type") })[0]
}
function Owned { [FileCatBudgetInput]::GetForegroundWindow() -eq $script:handle }
function Key([byte] $code, [bool] $extended = $false) {
    if (-not (Owned)) { throw 'FileCat lost the foreground; refusing keys' }
    $flags = if ($extended) { 1 } else { 0 }
    [FileCatBudgetInput]::keybd_event($code, 0, $flags, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30
    [FileCatBudgetInput]::keybd_event($code, 0, $flags -bor 2, [UIntPtr]::Zero)
}
function Chord([byte[]] $codes) {
    if (-not (Owned)) { throw 'FileCat lost the foreground; refusing keys' }
    foreach ($code in $codes) { [FileCatBudgetInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    $reversed = @($codes); [array]::Reverse($reversed)
    foreach ($code in $reversed) { [FileCatBudgetInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    Start-Sleep -Milliseconds 300
}
function Activate($window) {
    [void][FileCatBudgetInput]::SetForegroundWindow($script:handle)
    Start-Sleep -Milliseconds 400
    if (-not (Owned)) { [FileCatBudgetInput]::ClickTitle($script:handle); Start-Sleep -Milliseconds 600 }
    if (-not (Owned)) { throw "$($window.Current.Name) is not foreground; refusing keys" }
}
function Focused { try { $AE::FocusedElement.Current.Name } catch { $null } }
function FocusRow([string] $name) {
    Key 0x24 $true; Start-Sleep -Milliseconds 250  # Home
    for ($i = 0; $i -lt 12; $i++) {
        $at = Focused
        if ($at -eq $name -or $at -like "$name,*") { return }
        Key 0x28 $true; Start-Sleep -Milliseconds 250
    }
    throw "The cursor never reached $name (at '$(Focused)')"
}
function ChooseFormat($dialog, [string] $label) {
    $box = Await { In $dialog 'Archive format' 'ComboBox' } 'the Archive format box'
    $box.SetFocus(); Start-Sleep -Milliseconds 300
    $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 500
    $item = Await { @(Descendants $AE::RootElement | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.ListItem' -and $_.Current.Name -eq $label })[0] } "the format '$label'"
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400
    try { $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
    Start-Sleep -Milliseconds 900
}
# Presses keys every 150 ms (Down/Up keeps the cursor near where it is; Tab only moves focus) until the condition holds,
# then for $after seconds more; never Enter, Space or Escape.
function KeepPressing([string] $mode, [scriptblock] $until, [int] $limit, [double] $after = 3) {
    $watch = [Diagnostics.Stopwatch]::StartNew(); $done = $null; $down = $true; $presses = 0
    while ($true) {
        if ($null -eq $done -and (& $until)) { $done = $watch.Elapsed.TotalSeconds }
        if ($null -ne $done -and $watch.Elapsed.TotalSeconds -ge $done + $after) { break }
        if ($watch.Elapsed.TotalSeconds -gt $limit) { throw "Timed out after $limit s in $($script:result.Stage)" }
        if ($mode -eq 'tab') { Key 0x09 } else { Key ($(if ($down) { 0x28 } else { 0x26 })) $true; $down = -not $down }
        $presses++
        Start-Sleep -Milliseconds 120
    }
    $presses
}
# Windows PowerShell leaves ISO times as text; PowerShell 7 makes them DateTime values.
function Utc($value) {
    if ($value -is [DateTime]) { return $value.ToUniversalTime() }
    [DateTime]::Parse([string]$value, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
}
function Phase([string] $name, [scriptblock] $body) {
    $script:result.Stage = $name
    $start = [DateTime]::UtcNow
    $detail = & $body
    $script:phases += [ordered]@{ Name = $name; StartUtc = $start; EndUtc = [DateTime]::UtcNow; Detail = $detail }
}

$hostExe = [IO.Path]::GetFullPath($HostPath); $worker = [IO.Path]::GetFullPath($WorkerPath); $report = [IO.Path]::GetFullPath($ReportPath)
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation.TrimEnd('\') + '\'
if (-not $hostExe.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($hostExe) -ine 'FileCat.ArchiveQualificationHost.exe' -or
    -not $worker.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($worker) -ine 'FileCat.exe' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report) -or $Files -lt 1000 -or
    (Get-Process -Name FileCat, FileCat.ArchiveQualificationHost -ErrorAction SilentlyContinue)) {
    throw 'Use an isolated qualification host and published worker, a fresh report and no other FileCat'
}
$fixture = Join-Path $validation ('ArchiveUiBudget\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'; $many = Join-Path $left 'many'
New-Item -ItemType Directory -Force -Path $data, $left, $right | Out-Null
$content = [Text.Encoding]::ASCII.GetBytes('x' * 64)
for ($d = 0; $d -lt [Math]::Ceiling($Files / 200); $d++) {
    $folder = Join-Path $many ('d{0:D3}' -f $d); [void][IO.Directory]::CreateDirectory($folder)
    for ($f = 0; $f -lt 200 -and $d * 200 + $f -lt $Files; $f++) { [IO.File]::WriteAllBytes((Join-Path $folder ('f{0:D3}.txt' -f $f)), $content) }
}
[IO.File]::WriteAllText((Join-Path $left 'a.txt'), "alpha`n")
$probeReport = Join-Path $fixture 'ui-budget.json'
$script:phases = @()
$script:result = [ordered]@{
    Passed = $false; Stage = 'starting'; Fixture = $fixture; Files = $Files; Error = $null
    HostSha256 = (Get-FileHash -LiteralPath $hostExe -Algorithm SHA256).Hash
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $hostExe) 'FileCat.dll') -Algorithm SHA256).Hash
    WorkerSha256 = (Get-FileHash -LiteralPath $worker -Algorithm SHA256).Hash
    Processors = [Environment]::ProcessorCount
}
$app = $null
try {
    $app = Start-Process -FilePath $hostExe -WorkingDirectory (Split-Path $hostExe) -PassThru -ArgumentList @(
        '--data', $data, '--worker', $worker, '--ui-budget', $probeReport, '--new-instance', '--left', $left, '--right', $right)
    Await { $app.Refresh(); if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'the qualification host window' 60 | Out-Null
    Start-Sleep 3
    $script:handle = $app.MainWindowHandle
    $main = $AE::FromHandle($script:handle)
    Activate $main
    Await { Test-Path -LiteralPath $probeReport } 'the UI budget probe' 15 | Out-Null

    Phase 'idle' { Start-Sleep 8 }
    FocusRow 'a.txt'
    Phase 'navigation' { KeepPressing 'arrows' { $true } 30 8 }

    $tar = $null
    FocusRow 'many'
    Chord @(0x12, 0x74)  # Alt+F5
    $dialog = Await { In $main 'Create archive dialog' } 'the Create archive dialog'
    ChooseFormat $dialog 'TAR + gzip'
    $tar = (Await { In $dialog 'Archive path' 'Edit' } 'the Archive path box').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if (-not $tar.EndsWith('many.tar.gz')) { throw "Unexpected archive path: $tar" }
    $pack = Await { $b = In $dialog 'Pack' 'Button'; if ($b.Current.IsEnabled) { $b } } 'an available Pack button'
    $pack.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Await { -not (In $main 'Create archive dialog') } 'the Create archive dialog to close' | Out-Null
    Phase 'pack' { KeepPressing 'arrows' { Test-Path -LiteralPath $tar } 900 }
    $script:result.ArchiveBytes = (Get-Item -LiteralPath $tar).Length

    Key 0x09; Start-Sleep -Milliseconds 600  # Tab: the archive is in the other panel's folder
    FocusRow 'many.tar.gz'
    Key 0x0D  # Enter opens it
    Phase 'open' { KeepPressing 'arrows' { @(Descendants $main | Where-Object { $_.Current.ClassName -eq 'FileList' -and $_.Current.Name -like '*many.tar.gz*' }).Count } 300 }

    $before = (Get-FileHash -LiteralPath $tar -Algorithm SHA256).Hash
    Key 0x09; Start-Sleep -Milliseconds 600  # back to the source panel
    FocusRow 'a.txt'
    Key 0x74  # F5
    Await { In $main 'Copy dialog' } 'the Copy dialog' | Out-Null
    Start-Sleep -Milliseconds 800
    Key 0x0D  # Enter adds a.txt to the archive
    # FileCat holds the archive exclusively while it updates it on Windows: a refused read means not done yet.
    Phase 'add' { KeepPressing 'tab' { try { (Get-FileHash -LiteralPath $tar -Algorithm SHA256).Hash -ne $before } catch { $false } } 900 }

    Start-Sleep -Milliseconds 2500  # one more probe report after the last window
    $probe = Get-Content -LiteralPath $probeReport -Raw | ConvertFrom-Json
    $script:result.Probe = [ordered]@{ Ticks = $probe.Ticks; OverBudget = $probe.OverBudget; WorstMs = $probe.WorstMs; Histogram = $probe.Histogram }
    $script:result.Phases = @(foreach ($phase in $script:phases) {
        $gaps = @($probe.Gaps | Where-Object { $end = Utc $_.EndUtc; $end -ge $phase.StartUtc -and $end -le $phase.EndUtc })
        $unexplained = @($gaps | Where-Object { $_.GcPauseMs -lt $_.Ms / 2 })
        [ordered]@{ Name = $phase.Name; Seconds = [Math]::Round(($phase.EndUtc - $phase.StartUtc).TotalSeconds, 1); Presses = $phase.Detail
            OverBudget = $gaps.Count; Unexplained = $unexplained.Count; WorstMs = ($gaps | Measure-Object Ms -Maximum).Maximum; Gaps = $gaps }
    })
    # One gap can be a one-off (a dialog or listing laid out once); repeated unexplained gaps fail the budget.
    $script:result.Passed = -not @($script:result.Phases | Where-Object { $_.Name -in 'pack', 'open', 'add' -and $_.Unexplained -gt 1 }).Count
    $script:result.Stage = 'done'
}
catch { $script:result.Error = $_.Exception.Message }
finally {
    if ($app -and -not $app.HasExited) { $app.Kill(); $app.WaitForExit(5000) | Out-Null }
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
