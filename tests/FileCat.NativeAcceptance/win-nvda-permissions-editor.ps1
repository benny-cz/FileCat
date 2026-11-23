param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$NvdaPath,
    [Parameter(Mandatory)][string]$ReportPath)
# U1 G-UX: NVDA readout of File -> Inspect -> Permissions and ownership, the ordered Windows access editor and the owner
# editor, on an idle disposable VMware guest with a private reader profile. Keyboard only: the palette opens the
# inspector, Tab reaches its actions, the editor's add-entry fields are filled, an invalid mask is announced, Escape
# discards the draft, and the owner editor opens on its owner choice.
# Nothing is applied; the witness file's descriptor must be unchanged.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PermissionsReaderInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint k, uint t);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int k);
    [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
    public static void Key(byte k, bool extended) {
        uint f = extended ? 1u : 0u; byte s = (byte)MapVirtualKey(k, 0);
        keybd_event(k, s, f, UIntPtr.Zero); keybd_event(k, s, f | 2u, UIntPtr.Zero);
    }
    public static void Chord(byte[] keys) {
        foreach (byte k in keys) keybd_event(k, (byte)MapVirtualKey(k, 0), 0, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], (byte)MapVirtualKey(keys[i], 0), 2, UIntPtr.Zero);
    }
    public static void Release() {
        foreach (int k in new[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
            if ((GetAsyncKeyState(k) & 0x8000) != 0) keybd_event((byte)k, (byte)MapVirtualKey((uint)k, 0), 2, UIntPtr.Zero);
    }
}
'@
$AE = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
function All($root) { @($root.FindAll($scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) }
function Await([scriptblock]$check, [string]$label, [int]$seconds = 20) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $seconds) {
        try { $answer = & $check; if ($null -ne $answer -and $answer -ne $false) { return $answer } }
        catch [System.Windows.Automation.ElementNotAvailableException] {}
        catch [Runtime.InteropServices.COMException] {}
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out: $label"
}
function Main { $AE::FromHandle([IntPtr]$script:app.MainWindowHandle) }
function Named([string]$name, $type = $null) {
    @(All (Main) | Where-Object { $_.Current.Name -eq $name -and (!$type -or $_.Current.ControlType -eq $type) })[0]
}
function Key([byte]$key, [bool]$extended = $false) { [PermissionsReaderInput]::Key($key, $extended); Start-Sleep -Milliseconds 300 }
function Chord([byte[]]$keys) { [PermissionsReaderInput]::Chord($keys); Start-Sleep -Milliseconds 450 }
function Type-Text([string]$text) {
    foreach ($ch in $text.ToCharArray()) {
        $vk = [byte][char]::ToUpperInvariant($ch)
        if ($ch -eq '-') { $vk = 0xBD }
        if ([char]::IsUpper($ch)) { Chord @(0x10, $vk) } else { Key $vk }
    }
}
function Speech {
    $lines = @(Get-Content -LiteralPath $script:log -Encoding UTF8 -ErrorAction SilentlyContinue)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex); $script:speechIndex = $lines.Count
    @($fresh | Where-Object { $_ -match 'Speaking \[' } | ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
}
function Hear([string]$step, [string[]]$expected, [int]$seconds = 10) {
    $heard = @(); $clock = [Diagnostics.Stopwatch]::StartNew(); $missing = $expected
    while ($clock.Elapsed.TotalSeconds -lt $seconds) {
        $heard += @(Speech)
        $combined = ($heard -join ' ').Replace('\\', '\')
        $missing = @($expected | Where-Object { !$combined.Contains($_) })
        if ($missing.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    }
    $check = [ordered]@{ Step = $step; Expected = $expected; Missing = $missing; Spoken = $heard }
    $script:result.Checks += $check
    if ($missing.Count) { throw "NVDA did not speak $($missing -join ', ') at '$step': $($heard -join ' / ')" }
}
function Foreground {
    $handle = [IntPtr]$script:app.MainWindowHandle
    [PermissionsReaderInput]::Release()
    [void][PermissionsReaderInput]::SetForegroundWindow($handle)
    if ([PermissionsReaderInput]::GetForegroundWindow() -ne $handle) { throw 'The owned app is not foreground; refusing keys.' }
}

$root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $root.TrimEnd('\') + '\'
$appPath = [IO.Path]::GetFullPath($AppPath); $nvdaPath = [IO.Path]::GetFullPath($NvdaPath); $report = [IO.Path]::GetFullPath($ReportPath)
$machine = Get-CimInstance Win32_ComputerSystem
if ($machine.Manufacturer -notmatch 'VMware' -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0 -or
    !$appPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or !$nvdaPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    !$report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat, nvda -ErrorAction SilentlyContinue) -or (Get-AuthenticodeSignature -LiteralPath $nvdaPath).Status -ne 'Valid') {
    throw 'Use an idle interactive VMware guest, a Git-built app, a signed existing NVDA and fresh private validation paths.'
}
$fixture = Join-Path (Split-Path $report) ('fixture-' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'; $config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
$witness = Join-Path $left 'acl-witness.txt'
[IO.File]::WriteAllText($witness, "Permissions editor reader fixture`r`n")
$before = (Get-Acl -LiteralPath $witness).Sddl
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), "[general]`r`nshowWelcomeDialogAtStartup = False`r`n[update]`r`nautoCheck = False`r`nstartupNotification = False`r`nallowUsageStats = False`r`naskedAllowUsageStats = True`r`n")
$script:log = Join-Path $fixture 'nvda.log'; $script:speechIndex = 0; $script:app = $null; $reader = $null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$script:result = [ordered]@{ Passed = $false; Stage = 'starting'; Computer = $env:COMPUTERNAME; User = $env:USERNAME;
    Session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    Elevated = (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    AppDllSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path (Split-Path $appPath) 'FileCat.dll')).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvdaPath).VersionInfo.ProductVersion; Fixture = $fixture; Checks = @(); Error = $null }
try {
    $reader = Start-Process -FilePath $nvdaPath -WindowStyle Hidden -PassThru -ArgumentList @("--log-file=$script:log", '--log-level=12', "--config-path=$config", '--disable-addons')
    Await { (Test-Path -LiteralPath $script:log) -and (Select-String -LiteralPath $script:log -SimpleMatch 'NVDA initialized' -Quiet) } 'NVDA initialization' 30 | Out-Null
    $script:app = Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -PassThru -WindowStyle Normal -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    Await { $script:app.Refresh(); $script:app.MainWindowHandle -ne [IntPtr]::Zero } 'owned app window' | Out-Null
    Start-Sleep -Seconds 3
    $list = @(All (Main) | Where-Object { $_.Current.ClassName -eq 'FileList' })[0]
    if (!$list) { throw 'No native FileList in the owned app.' }
    $list.SetFocus(); Start-Sleep -Milliseconds 400
    Foreground
    Key 0x24 $true; Key 0x28 $true   # Home (parent entry), Down (the witness)
    Start-Sleep -Milliseconds 800; [void](Speech)

    $script:result.Stage = 'open inspector'
    Chord @(0x11, 0x10, 0x50)        # Ctrl+Shift+P
    Await { Named 'Search commands' $CT::Edit } 'command search' | Out-Null
    Type-Text 'permissions and ownership'
    Start-Sleep -Milliseconds 800
    Key 0x0D
    Await { Named 'Permissions and ownership' } 'the permissions inspector' 30 | Out-Null
    Hear 'inspector opens' @('Permissions and ownership dialog') 12

    # Keyboard users meet the inspector through its focusable controls: one full Tab cycle must speak the subject, its
    # access entries and the actions, so nothing that matters is reachable only by mouse or review cursor.
    $script:result.Stage = 'inspector Tab cycle'
    $cycle = @()
    for ($i = 0; $i -lt 40; $i++) {
        Key 0x09
        Start-Sleep -Milliseconds 250
        $spoken = (@(Speech) -join ' ')
        $cycle += $spoken
        $script:result.Checks += [ordered]@{ Step = "tab $i"; Spoken = $spoken }
        if ($i -gt 3 -and $spoken.Contains('Close') -and ($cycle -join ' ').Contains('Edit access ACL')) { break }
    }
    $heardCycle = ($cycle -join ' ').Replace('\\', '\')
    $script:result.TabCycleMissing = @(foreach ($part in 'acl-witness.txt', 'Allow', 'Edit access ACL') { if (!$heardCycle.Contains($part)) { $part } })
    $edit = Await { Named ('Edit access ACL' + [char]0x2026) $CT::Button } 'the Edit access ACL action'
    $edit.SetFocus(); Start-Sleep -Milliseconds 500
    Hear 'edit action focused' @('Edit access ACL', 'button') 8
    Key 0x0D
    Await { Named 'Ordered Windows access entries' } 'the access editor' 30 | Out-Null
    # Apply is unavailable until there is a change, so the editor must start on its ordered entries, not on the card.
    Hear 'editor opens' @('Edit access ACL dialog', 'Ordered Windows access entries') 12

    $script:result.Stage = 'add-entry fields'
    $box = Await { Named 'Add an explicit entry' $CT::CheckBox } 'add-entry check box'
    $box.SetFocus(); Start-Sleep -Milliseconds 500
    Hear 'add-entry check box' @('Add an explicit entry', 'check box') 8
    Key 0x20
    Hear 'add-entry toggled' @('checked') 8
    $sid = Await { Named 'New Windows entry SID' $CT::Edit } 'SID box'
    $sid.SetFocus(); Start-Sleep -Milliseconds 500
    Hear 'SID field' @('New Windows entry SID') 8
    $mask = Await { Named 'Windows access mask' $CT::Edit } 'mask box'
    $mask.SetFocus(); Start-Sleep -Milliseconds 500
    Hear 'mask field' @('Windows access mask') 8
    Type-Text 'zz'
    Hear 'invalid mask announced' @('Enter up to eight hexadecimal digits for the access mask.') 8
    $problem = @(All (Main) | Where-Object { $_.Current.ControlType -eq $CT::Text -and $_.Current.Name -match 'mask|SID|hexadecimal|invalid' } |
        ForEach-Object { $_.Current.Name })
    $script:result.ValidationTexts = $problem
    $script:result.ApplyEnabled = @(All (Main) | Where-Object { $_.Current.ControlType -eq $CT::Button -and $_.Current.Name -eq 'Apply' } |
        ForEach-Object { $_.Current.IsEnabled })
    if ($script:result.ApplyEnabled -contains $true) { throw 'Apply was available with an invalid access mask.' }

    $script:result.Stage = 'discard'
    Key 0x1B
    Hear 'escape with a dirty draft' @('Discard changes? dialog', 'Discard', 'button') 8
    Key 0x0D                         # Enter on the focused Discard
    Await { $null -eq (Named 'Ordered Windows access entries') } 'the editor to close after Discard' 15 | Out-Null
    Start-Sleep -Milliseconds 800
    $script:result.Checks += [ordered]@{ Step = 'after discard'; Spoken = (@(Speech) -join ' ') }
    $script:result.DiscardOffered = $true

    # The owner editor has no Apply until a different owner is chosen: it must open on the owner choice, and Escape on
    # a clean draft closes it without a discard prompt.
    $script:result.Stage = 'owner editor'
    Foreground
    Chord @(0x11, 0x10, 0x50)        # Ctrl+Shift+P
    Await { Named 'Search commands' $CT::Edit } 'command search' | Out-Null
    Type-Text 'permissions and ownership'
    Start-Sleep -Milliseconds 800
    Key 0x0D
    $owner = Await { Named ('Change owner' + [char]0x2026) $CT::Button } 'the Change owner action' 30
    Start-Sleep -Milliseconds 800; [void](Speech)
    $owner.SetFocus(); Start-Sleep -Milliseconds 500
    Hear 'owner action focused' @('Change owner', 'button') 8
    Key 0x0D
    Await { Named 'Assignable owner SID' } 'the owner editor' 30 | Out-Null
    Hear 'owner editor opens' @('Change owner dialog', 'Assignable owner SID', 'combo box') 12
    Key 0x1B
    Await { $null -eq (Named 'Assignable owner SID') } 'the owner editor to close on Escape' 15 | Out-Null
    Start-Sleep -Milliseconds 800
    $closing = (@(Speech) -join ' ')
    $script:result.Checks += [ordered]@{ Step = 'owner editor closed'; Spoken = $closing }
    if ($closing.Contains('Discard changes?')) { throw 'Escape on an unchanged owner draft asked to discard changes.' }
    $after = (Get-Acl -LiteralPath $witness).Sddl
    if ($after -ne $before) { throw "The witness descriptor changed: $before -> $after" }
    $script:result.WitnessSddl = $after
    $script:result.Passed = $script:result.TabCycleMissing.Count -eq 0
    $script:result.Stage = if ($script:result.Passed) { 'done' } else { "Tab cycle never spoke: $($script:result.TabCycleMissing -join ', ')" }
} catch { $script:result.Error = $_.Exception.Message } finally {
    [PermissionsReaderInput]::Release()
    if ($script:app -and !$script:app.HasExited) { $script:app.Kill(); [void]$script:app.WaitForExit(5000) }
    if ($reader -and !$reader.HasExited) { $reader.Kill(); [void]$reader.WaitForExit(5000) }
    [PermissionsReaderInput]::Release()
    $script:result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
