param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$validationRoot = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
$resolvedApp = [IO.Path]::GetFullPath($AppPath)
if (!$resolvedApp.StartsWith(([IO.Path]::GetFullPath($validationRoot) + [IO.Path]::DirectorySeparatorChar),
        [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedApp) -ine 'FileCat.exe') {
    throw 'Use an isolated FileCat.exe below the guest FileCatValidation directory.'
}
if (!(Test-Path -LiteralPath $resolvedApp -PathType Leaf)) { throw 'The isolated FileCat build is missing.' }

$fixture = Join-Path $validationRoot ('CopyOptionCorrection\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$sourceFile = Join-Path $source 'a.txt'
$targetFile = Join-Path $target 'a.txt'
[IO.File]::WriteAllText($sourceFile, "native option correction`n")
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
        return @($all)
    }
    function Named([System.Windows.Automation.ControlType]$type, [string]$name) {
        return @((Snapshot) | Where-Object { $_.Current.ControlType -eq $type -and $_.Current.Name -eq $name })
    }
    function Await([scriptblock]$condition, [string]$description, [int]$seconds = 15) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do {
            $result = & $condition
            if ($result) { return $result }
            Start-Sleep -Milliseconds 200
        } while ($watch.Elapsed.TotalSeconds -lt $seconds)
        throw "Timed out waiting for $description."
    }
    function Approval {
        $buttons = @(Named ([System.Windows.Automation.ControlType]::Button) 'Copy')
        if (!$buttons) { return $null }
        # The key bar is below the modal card. Its Copy button is deliberately
        # still in the accessibility tree but cannot approve this review.
        return @($buttons | Sort-Object { $_.Current.BoundingRectangle.Y })[0]
    }
    function Invoke($element, [string]$description) {
        if (!$element) { throw "$description is missing." }
        $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }

    $row = Await { @(Named ([System.Windows.Automation.ControlType]::ListItem) 'a.txt')[0] } 'source file row'
    try { $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
    catch { $row.SetFocus() }
    Invoke (Await { @((Snapshot) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -like 'Copy*'
    } | Sort-Object { $_.Current.BoundingRectangle.Y })[0] } 'Copy command') 'Copy command'

    $summary = 'Copy "a.txt" (' + ([IO.FileInfo]$sourceFile).Length + ' bytes)'
    Await { (Named ([System.Windows.Automation.ControlType]::Text) $summary).Count -eq 1 -and (Approval) } 'one Copy review' | Out-Null
    $options = Await { @((Snapshot) | Where-Object {
        $_.Current.Name -eq 'Options' -and
        $_.Current.ControlType -in @([System.Windows.Automation.ControlType]::Button,
            [System.Windows.Automation.ControlType]::CheckBox)
    })[0] } 'Options expander'
    try { $options.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    catch { $options.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    $speed = Await { @(Named ([System.Windows.Automation.ControlType]::Edit) 'Speed limit (MB/s):')[0] } 'speed-limit field'
    $speed.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('-1')
    Invoke (Await { Approval } 'Copy approval') 'invalid Copy approval'
    $errorText = 'Speed limit: enter a finite, nonnegative number of MB/s.'
    Await { (Named ([System.Windows.Automation.ControlType]::Text) $errorText).Count -eq 1 } 'visible invalid-speed error' | Out-Null
    if ((Named ([System.Windows.Automation.ControlType]::Text) $summary).Count -ne 1 -or
        (Test-Path -LiteralPath $targetFile)) { throw 'Invalid speed closed the review or published a target.' }
    if ((Named ([System.Windows.Automation.ControlType]::Edit) 'Speed limit (MB/s):').Count -ne 1) {
        throw 'The editable speed field was replaced after the error.'
    }
    $fieldHelp = $speed.Current.HelpText
    if ($fieldHelp -notlike "*$errorText*") { throw "The speed field lacks accessible error help: $fieldHelp" }
    $speed.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('0.0625')
    Await { (Named ([System.Windows.Automation.ControlType]::Text) $errorText).Count -eq 0 } 'error to clear' | Out-Null
    if ($speed.Current.HelpText -like "*$errorText*") { throw 'Corrected speed retains stale error help.' }
    Invoke (Await { Approval } 'corrected Copy approval') 'corrected Copy approval'
    Await { (Test-Path -LiteralPath $targetFile) -and
        (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash -eq $originalHash } 'exact copied bytes' 30 | Out-Null
    if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $originalHash) {
        throw 'The source changed during Copy.'
    }
    [pscustomobject]@{
        Passed = $true
        App = $resolvedApp
        AppSha256 = (Get-FileHash -LiteralPath $resolvedApp -Algorithm SHA256).Hash
        Fixture = $fixture
        InvalidSpeed = '-1'
        VisibleError = $errorText
        SpeedFieldHelp = $fieldHelp
        SameDialogCorrectedSpeed = '0.0625'
        SourceSha256 = $originalHash
        TargetSha256 = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
catch {
    ($_ | Out-String) | Set-Content -LiteralPath ($ReportPath + '.error.txt') -Encoding UTF8
    throw
}
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        $process.WaitForExit(5000) | Out-Null
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    }
}
