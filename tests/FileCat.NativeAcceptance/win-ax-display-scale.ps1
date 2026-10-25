param(
    [Parameter(Mandatory)][ValidateSet(100, 150, 200)][int]$Percent,
    [Parameter(Mandatory)][string]$ReportPath
)

# Only for the isolated, logged-in VMware validation guest. Restore the
# original scale after the native journey; never run this on a workstation.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileCatDisplayScale {
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$report = [IO.Path]::GetFullPath($ReportPath)
$computer = Get-CimInstance Win32_ComputerSystem
if ($computer.Manufacturer -notlike '*VMware*' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    !(Test-Path -LiteralPath $validation -PathType Container) -or
    (Get-Process -Id $PID).SessionId -eq 0) {
    throw 'Display scale changes require the isolated interactive VMware validation guest.'
}

function Await([scriptblock]$condition, [string]$description) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 150
    } while ($clock.Elapsed.TotalSeconds -lt 15)
    throw "Timed out waiting for $description."
}
function ScaleCombo {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox -and
            $_.Current.AutomationId -eq 'SystemSettings_Display_Scaling_ItemSizeOverride_ComboBox'
        })[0]
}
function SelectedPercent($combo) {
    $chosen = @($combo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
        })[0]
    if (!$chosen -or $chosen.Current.Name -notmatch '^([0-9]+)%') {
        throw 'Could not read the selected Windows display scale.'
    }
    return [int]$Matches[1]
}

$result = [ordered]@{ Passed = $false; Requested = $Percent; Original = $null;
    Selected = $null; CallerDpiAtStart = [FileCatDisplayScale]::GetDpiForSystem();
    SettingsClosed = $false; SettingsCloseError = $null; Error = $null }
$settingsWindow = $null
try {
    Start-Process 'ms-settings:display'
    $combo = Await { ScaleCombo } 'Display scale control'
    $settingsWindow = $combo
    # The inner SystemSettings window has no WindowPattern on this Windows 11
    # build; its ApplicationFrameHost parent does. Close the actual frame,
    # instead of leaving Settings over the next native pointer journey.
    while ($settingsWindow -and -not $settingsWindow.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::IsWindowPatternAvailableProperty)) {
        $settingsWindow = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($settingsWindow)
    }
    $result.Original = SelectedPercent $combo
    if ($result.Original -ne $Percent) {
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $option = Await {
            @($combo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                    $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
                    $_.Current.Name -match ('^' + $Percent + '%') -and !$_.Current.IsOffscreen
                })[0]
        } "$Percent percent option"
        $option.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    }
    $result.Selected = Await {
        $current = SelectedPercent (ScaleCombo)
        if ($current -eq $Percent) { $current }
    } 'selected display scale'
    # Windows may cache this caller's DPI. Launch the acceptance app in a new
    # process and require its measured window DPI to prove the new scale.
    $result.Passed = $true
}
catch { $result.Error = $_.Exception.Message }
finally {
    if ($settingsWindow) {
        try {
            $settingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
            $result.SettingsClosed = $true
        }
        catch { $result.SettingsCloseError = $_.Exception.Message }
    }
    $result | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
