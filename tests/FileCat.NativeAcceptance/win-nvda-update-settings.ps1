param(
    [Parameter(Mandatory)][string] $AppPath,
    [Parameter(Mandatory)][string] $NvdaPath,
    [Parameter(Mandatory)][string] $ReportPath
)

# Isolated native Settings/Privacy route at enlarged FileCat text, followed by
# one explicit Help update check while automatic checks are turned off.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatSettingsInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
'@
function Descendants($root) {
    @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Await([scriptblock] $check, [string] $description, [int] $seconds = 25) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        try { $value = & $check; if ($null -ne $value -and $value -ne $false) { return $value } }
        catch [Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 125
    }
    throw "Timed out waiting for $description"
}
function Named($root, [string] $name) {
    @(Descendants $root | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1)[0]
}
function Click($element) {
    $r = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $r.Width -lt 8 -or $r.Height -lt 8) {
        throw "Unreachable pointer target: $($element.Current.Name)"
    }
    if (-not [FileCatSettingsInput]::SetCursorPos([int]($r.X + $r.Width / 2),
        [int]($r.Y + $r.Height / 2))) { throw 'Could not move guest pointer' }
    Start-Sleep -Milliseconds 80
    [FileCatSettingsInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [FileCatSettingsInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
function Chord([byte[]] $codes) {
    foreach ($code in $codes) {
        [FileCatSettingsInput]::keybd_event($code, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
    }
    [array]::Reverse($codes)
    foreach ($code in $codes) {
        [FileCatSettingsInput]::keybd_event($code, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
    }
}
function TakeSpeech {
    $lines = @(Get-Content -LiteralPath $script:nvdaLog -Encoding UTF8)
    $fresh = @($lines | Select-Object -Skip $script:speechIndex)
    $script:speechIndex = $lines.Count
    return @($fresh | Where-Object { $_ -match 'Speaking \[' } |
        ForEach-Object { $_.Substring($_.IndexOf('Speaking [') + 9).Trim() })
}

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    -not $nvda.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or
    -not $report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or
    (Get-Process -Name nvda -ErrorAction SilentlyContinue)) {
    throw 'Use isolated Git app, signed portable NVDA, fresh validation report and no competing reader'
}
$fixture = Join-Path $validation ('UpdateSettingsFixture\' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'data'
$left = Join-Path $fixture 'left'
$right = Join-Path $fixture 'right'
$config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data, $left, $right, $config | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'settings.json'),
    '{"SchemaVersion":1,"FontSize":21,"CheckForUpdates":true}', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$script:nvdaLog = Join-Path $fixture 'nvda.log'
$script:speechIndex = 0
$result = [ordered]@{
    Passed = $false; Stage = 'starting'; Fixture = $fixture;
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash;
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion;
    Privacy = @{}; ManualCheck = @{}; Error = $null
}
$reader = $owned = $null
try {
    $reader = Start-Process -FilePath $nvda -ArgumentList @(
        "--log-file=$script:nvdaLog", '--log-level=12',
        "--config-path=$config", '--disable-addons') -PassThru -WindowStyle Hidden
    Await {
        if ($reader.HasExited) { throw 'Owned NVDA exited before initialization' }
        if ((Test-Path -LiteralPath $script:nvdaLog) -and
            (Select-String -LiteralPath $script:nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA initialization' 30 | Out-Null
    TakeSpeech | Out-Null
    $owned = Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -PassThru `
        -ArgumentList @('--new-instance', '--data', $data, '--left', $left, '--right', $right)
    $main = Await {
        $owned.Refresh()
        if ($owned.MainWindowHandle -ne [IntPtr]::Zero) {
            [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle)
        }
    } 'owned FileCat window'
    if ([IO.Path]::GetFullPath($owned.MainModule.FileName) -ine $app) {
        throw 'Unexpected FileCat executable'
    }
    [FileCatSettingsInput]::SetForegroundWindow($owned.MainWindowHandle) | Out-Null
    Chord ([byte[]]@(0x11, 0xBC)) # Ctrl+Comma, Settings
    $privacy = Await { Named $main 'Privacy' } 'Privacy tab'
    Click $privacy
    $result.Stage = 'privacy wording and speech'
    $updates = Await { Named $main 'Automatically check for updates' } 'automatic update checkbox'
    $result.Privacy.Initial = $updates.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString()
    $result.Privacy.HelpText = $updates.Current.HelpText
    $note = Await {
        @(Descendants $main | Where-Object {
            $_.Current.Name -like 'At most once a day at startup*'
        } | Select-Object -First 1)[0]
    } 'update privacy explanation'
    $result.Privacy.Note = $note.Current.Name
    $updates.SetFocus()
    Start-Sleep -Milliseconds 800
    $result.Privacy.Speech = @(TakeSpeech)
    if ($result.Privacy.Initial -ne 'On' -or
        $result.Privacy.Note -notmatch 'does not download or install update packages' -or
        $result.Privacy.Note -notmatch 'Help.*Check for updates.*network locations still connect' -or
        ($result.Privacy.Speech -join ' ') -notmatch 'does not download or install update packages') {
        throw 'The enlarged Privacy option did not expose and speak the full automatic/manual distinction'
    }
    Click $updates
    $result.Privacy.AfterToggle = $updates.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString()
    if ($result.Privacy.AfterToggle -ne 'Off') { throw 'Automatic update preference did not toggle off' }
    $ok = Await { Named $main 'OK' } 'Settings OK button'
    Click $ok
    $settingsFile = Join-Path $data 'settings.json'
    $result.Privacy.SavedOff = Await {
        if (Test-Path -LiteralPath $settingsFile) {
            $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
            if ($settings.CheckForUpdates -eq $false) { $true }
        }
    } 'saved automatic-check preference'

    $result.Stage = 'explicit manual update check'
    TakeSpeech | Out-Null
    $help = Await {
        @(Descendants $main | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and
            $_.Current.Name -eq 'Help'
        } | Select-Object -First 1)[0]
    } 'Help menu'
    Click $help
    $check = Await {
        $desktop = [System.Windows.Automation.AutomationElement]::RootElement
        @($desktop.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                $_.Current.ProcessId -eq $owned.Id -and
                $_.Current.Name -like 'Check for updates*'
            } | Select-Object -First 1)[0]
    } 'Help Check for updates action'
    $result.ManualCheck.Action = $check.Current.Name
    Click $check
    $dialog = Await { Named $main 'Check for updates dialog' } 'explicit update-check result'
    $result.ManualCheck.Dialog = $dialog.Current.Name
    $result.ManualCheck.Text = @(Descendants $dialog | ForEach-Object { $_.Current.Name } |
        Where-Object { $_ -match 'No release has been published|latest release|could not|cannot|failed|newer release|downloads or installs' })
    if (-not $result.ManualCheck.Text.Count) {
        throw 'The manual update-check result did not explain its outcome'
    }
    Start-Sleep -Milliseconds 500
    $result.ManualCheck.Speech = @(TakeSpeech)
    $result.Privacy.StoredFontSize = (Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json).FontSize
    if ($result.Privacy.StoredFontSize -ne 21) { throw 'Settings were not tested at enlarged 21-point text' }
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
    if ($owned -and -not $owned.HasExited) {
        try {
            $desktop = [System.Windows.Automation.AutomationElement]::RootElement
            $result.DebugWindows = @($desktop.FindAll([System.Windows.Automation.TreeScope]::Children,
                [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                    $_.Current.ProcessId -eq $owned.Id
                } | ForEach-Object {
                    [ordered]@{ Name = $_.Current.Name; Type = $_.Current.ControlType.ProgrammaticName;
                        Children = @(Descendants $_ | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
                })
            $result.DebugSpeech = @(TakeSpeech)
        } catch { $result.DebugError = $_.Exception.Message }
    }
} finally {
    if ($owned -and -not $owned.HasExited) { $owned.Kill(); $owned.WaitForExit(10000) | Out-Null }
    if ($reader -and -not $reader.HasExited) { $reader.Kill(); $reader.WaitForExit(10000) | Out-Null }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (-not $result.Passed) { throw $result.Error }
