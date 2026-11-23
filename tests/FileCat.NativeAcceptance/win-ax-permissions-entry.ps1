param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][ValidateSet('ordinary', 'privileged', 'unsupported')][string]$Expect)
# G-PKG: the permissions entry of an installed FileCat on Windows on Arm, through UI Automation and keys only. The palette
# runs File -> Inspect -> Permissions and ownership on a fresh witness file in a private fixture.
#   ordinary     a native Arm64 process at medium integrity opens the inspector with its editors.
#   privileged   an elevated process refuses the command with its reason and opens no inspector.
#   unsupported  an x64 copy running emulated opens the inspector with the Arm64-build reason and no editor.
# The administrator route is not offered in any of them. Nothing is applied: the witness keeps its descriptor.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PermissionsEntryInput {
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
function Names { @(All (Main) | ForEach-Object { try { $_.Current.Name } catch { $null } } | Where-Object { $_ } | Select-Object -Unique) }
function Key([byte]$key, [bool]$extended = $false) { [PermissionsEntryInput]::Key($key, $extended); Start-Sleep -Milliseconds 300 }
function Chord([byte[]]$keys) { [PermissionsEntryInput]::Chord($keys); Start-Sleep -Milliseconds 450 }
function Type-Text([string]$text) {
    foreach ($ch in $text.ToCharArray()) {
        $vk = [byte][char]::ToUpperInvariant($ch)
        if ($ch -eq ' ') { $vk = 0x20 }
        if ([char]::IsUpper($ch)) { Chord @(0x10, $vk) } else { Key $vk }
    }
}
function Foreground {
    $handle = [IntPtr]$script:app.MainWindowHandle
    [PermissionsEntryInput]::Release()
    [void][PermissionsEntryInput]::SetForegroundWindow($handle)
    if ([PermissionsEntryInput]::GetForegroundWindow() -ne $handle) { throw 'The owned app is not foreground; refusing keys.' }
}

$appPath = [IO.Path]::GetFullPath($AppPath); $report = [IO.Path]::GetFullPath($ReportPath)
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::Arm64 -or
    [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0 -or (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an idle interactive Windows on Arm session and a fresh report path.'
}
$fixture = Join-Path (Split-Path $report) ('fixture-' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'; $left = Join-Path $fixture 'left'; $right = Join-Path $fixture 'right'
New-Item -ItemType Directory -Force -Path $data, $left, $right | Out-Null
$witness = Join-Path $left 'acl-witness.txt'
[IO.File]::WriteAllText($witness, "Permissions entry fixture`r`n")
$before = (Get-Acl -LiteralPath $witness).Sddl
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$groups = (& whoami.exe /groups /fo csv) -join ' '
$machine = 0
try {
    $bytes = [IO.File]::ReadAllBytes($appPath)
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    $machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
} catch {}
$script:app = $null
$script:result = [ordered]@{ Passed = $false; Expect = $Expect; Stage = 'starting'; Computer = $env:COMPUTERNAME; User = $env:USERNAME
    Session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    Integrity = if ($groups -match 'S-1-16-12288') { 'High' } elseif ($groups -match 'S-1-16-8192') { 'Medium' } else { 'Other' }
    AppPath = $appPath; AppMachine = ('0x{0:X4}' -f $machine)
    AppDllSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path (Split-Path $appPath) 'FileCat.dll')).Hash
    Fixture = $fixture; InspectorOpened = $false; Findings = @(); Names = @(); Error = $null }
try {
    $script:app = Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -PassThru -WindowStyle Normal `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    # A first launch after an install can take minutes on a Windows on Arm guest (and longer for an emulated x64 copy).
    $launched = [Diagnostics.Stopwatch]::StartNew()
    Await { $script:app.Refresh(); if ($script:app.HasExited) { throw "FileCat exited with $($script:app.ExitCode)." }
        $script:app.MainWindowHandle -ne [IntPtr]::Zero } 'owned app window' 300 | Out-Null
    $script:result.WindowSeconds = [math]::Round($launched.Elapsed.TotalSeconds, 1)
    Start-Sleep -Seconds 4
    $list = Await { @(All (Main) | Where-Object { $_.Current.ClassName -eq 'FileList' })[0] } 'the file list' 30
    $list.SetFocus(); Start-Sleep -Milliseconds 400
    Foreground
    Key 0x24 $true; Key 0x28 $true   # Home (parent entry), Down (the witness)
    Start-Sleep -Milliseconds 600

    $script:result.Stage = 'run the permissions command'
    Chord @(0x11, 0x10, 0x50)        # Ctrl+Shift+P
    Await { Named 'Search commands' $CT::Edit } 'command search' | Out-Null
    Type-Text 'permissions and ownership'
    Start-Sleep -Milliseconds 900
    $script:result.PaletteNames = @(Names | Where-Object { $_ -match 'ermission' })
    Key 0x0D
    if ($Expect -eq 'privileged') { Start-Sleep -Seconds 6 }
    else { Await { Named 'Permissions and ownership' } 'the permissions inspector' 45 | Out-Null; Start-Sleep -Seconds 3 }
    $script:result.InspectorOpened = [bool](Named 'Permissions and ownership')
    $names = @(Names)
    $script:result.Names = @($names | Select-Object -First 250)
    $text = ($names + $script:result.PaletteNames) -join "`n"
    $ellipsis = [string][char]0x2026
    $editors = @("Edit access ACL$ellipsis", "Change owner$ellipsis") | Where-Object { $names -contains $_ }
    $authorized = $names -contains "Inspect with authorization$ellipsis"
    $emulated = $text.Contains('needs the Arm64 build of FileCat; this copy runs emulated')
    $refused = $text.Contains('Open your usual FileCat window to inspect permissions.') -or $text.Contains('This sign-in has no standard-user token.')
    $script:result.Findings = [ordered]@{ Editors = @($editors); AuthorizedRoute = $authorized; EmulatedReason = $emulated; ElevatedReason = $refused }
    $script:result.Stage = 'evaluate'
    switch ($Expect) {
        'ordinary' { $ok = $script:result.InspectorOpened -and $editors.Count -eq 2 -and !$authorized -and !$emulated -and $machine -eq 0xAA64 -and $script:result.Integrity -eq 'Medium' }
        'privileged' { $ok = !$script:result.InspectorOpened -and $refused -and $script:result.Integrity -eq 'High' }
        'unsupported' { $ok = $script:result.InspectorOpened -and $emulated -and $editors.Count -eq 0 -and !$authorized -and $machine -eq 0x8664 }
    }
    if ($script:result.InspectorOpened) { Foreground; Key 0x1B; Start-Sleep -Milliseconds 800 }
    $after = (Get-Acl -LiteralPath $witness).Sddl
    $script:result.WitnessUnchanged = $after -ceq $before
    $script:result.Passed = [bool]($ok -and $script:result.WitnessUnchanged)
} catch {
    $script:result.Error = $_.Exception.Message
} finally {
    if ($script:app -and !$script:app.HasExited) {
        [void]$script:app.CloseMainWindow()
        if (!$script:app.WaitForExit(15000)) { Stop-Process -Id $script:app.Id -Force }
    }
    [PermissionsEntryInput]::Release()
    $script:result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding UTF8
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
if (!$script:result.Passed) { exit 1 }
