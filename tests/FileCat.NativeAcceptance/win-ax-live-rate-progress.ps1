param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][string]$ExpectedHead
)

# Run only in the logged-in, disposable Windows VMware guest. All transfer data is test-owned.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class FileCatRateDpi { [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd); }'

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notlike '*VMware*' -or
    !$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an idle VMware guest, Git-built FileCat.exe and a fresh validation report.'
}
$git = 'C:\Program Files\Git\cmd\git.exe'
$head = (& $git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($head -ne $ExpectedHead) { throw "Expected $ExpectedHead but found $head." }

function Wait-For([scriptblock]$Check, [string]$Description, [int]$Seconds = 25) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        try { $value = & $Check; if ($value) { return $value } }
        catch {
            $inner = $_.Exception
            while ($inner -and $inner -isnot [System.Runtime.InteropServices.COMException]) { $inner = $inner.InnerException }
            if (!$inner) { throw }
        }
        Start-Sleep -Milliseconds 120
    }
    throw "Timed out waiting for $Description."
}
function Descendants($Owner) {
    @($Owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
}
function Control($Owner, $Type, [string]$Name) {
    @(Descendants $Owner | Where-Object {
        $_.Current.ControlType -eq $Type -and $_.Current.Name -eq $Name -and !$_.Current.IsOffscreen
    } | Select-Object -First 1)
}
function Click($Owner, [string]$Name) {
    $button = Wait-For { Control $Owner ([System.Windows.Automation.ControlType]::Button) $Name } "button $Name"
    if (!$button.Current.IsEnabled) { throw "$Name is disabled." }
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Dialog-Click($Window, [string]$Dialog, [string]$Name) {
    $group = Wait-For {
        @(Descendants $Window | Where-Object { $_.Current.Name -eq $Dialog -and !$_.Current.IsOffscreen } |
            Select-Object -First 1)
    } "$Dialog overlay"
    Click $group $Name
}
function Edit-Value($Edit) {
    ([System.Windows.Automation.ValuePattern]$Edit.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
}
function Set-Edit($Edit, [string]$Text) {
    ([System.Windows.Automation.ValuePattern]$Edit.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue($Text)
}
function Prompt-Field($Window) {
    Wait-For { Control $Window ([System.Windows.Automation.ControlType]::Edit) 'Maximum speed in MB/s (0 removes the limit):' } 'live speed field'
}
function Prompt-Click($Window, [string]$Name) {
    $field = Prompt-Field $Window
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $parent = $walker.GetParent($field)
    for ($i = 0; $parent -and $i -lt 15; $i++) {
        $button = Control $parent ([System.Windows.Automation.ControlType]::Button) $Name
        if ($button) {
            if (!$button.Current.IsEnabled) { throw "Prompt $Name is disabled." }
            ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
                [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
            return
        }
        if ($parent -eq $Window) { break }
        $parent = $walker.GetParent($parent)
    }
    throw "Prompt button $Name was not found near its field."
}
function Progress-Value($Window, [string]$Name) {
    $bar = Control $Window ([System.Windows.Automation.ControlType]::ProgressBar) $Name
    if (!$bar) { return $null }
    $pattern = $null
    if (!$bar.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$pattern)) {
        return $null
    }
    ([System.Windows.Automation.RangeValuePattern]$pattern).Current.Value
}

$fixture = Join-Path $validation ('LiveRateProgress\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$large = Join-Path $source 'a-large.bin'
$stream = [IO.File]::Create($large)
try { $stream.SetLength(64L * 1024 * 1024) }
finally { $stream.Dispose() }
$sourceHash = (Get-FileHash -LiteralPath $large -Algorithm SHA256).Hash
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head; Fixture = $fixture
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    SourceSha256 = $sourceHash
}
$process = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    Wait-For { $process.Refresh(); if ($process.HasExited) { throw 'Owned FileCat exited early.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.MainWindowHandle } } 'FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) { throw 'Wrong executable owns the window.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $bounds = $window.Current.BoundingRectangle
    $result.Dpi = [FileCatRateDpi]::GetDpiForWindow($process.MainWindowHandle)
    $result.WindowBoundsPixels = @($bounds.Left, $bounds.Top, $bounds.Width, $bounds.Height)

    $result.Stage = 'review copy and exact initial cap'
    $row = Wait-For {
        @(Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -eq 'a-large.bin'
        } | Select-Object -First 1)
    } 'owned source row'
    $row.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{F5}')
    $destination = Wait-For { Control $window ([System.Windows.Automation.ControlType]::Edit) 'Destination' } 'Copy destination'
    $result.ReviewDestination = Edit-Value $destination
    if ($result.ReviewDestination.TrimEnd([char[]]@('\', '/')) -ine $target) { throw 'Copy review captured the wrong target.' }
    $options = Wait-For {
        @(Descendants $window | Where-Object {
            $_.Current.Name -eq 'Options' -and $_.Current.ControlType -in @(
                [System.Windows.Automation.ControlType]::Button,
                [System.Windows.Automation.ControlType]::CheckBox)
        } | Select-Object -First 1)
    } 'Copy Options'
    try { ([System.Windows.Automation.InvokePattern]$options.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
    catch { ([System.Windows.Automation.TogglePattern]$options.GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)).Toggle() }
    $initialLimit = Wait-For { Control $window ([System.Windows.Automation.ControlType]::Edit) 'Speed limit (MB/s):' } 'initial speed field'
    Set-Edit $initialLimit '0.0625'
    Dialog-Click $window 'Copy dialog' 'Copy'

    $result.Stage = 'pending progress'
    $result.Progress = Wait-For {
        $overall = Progress-Value $window 'Progress of the operation'
        $file = Progress-Value $window 'Progress of the current file'
        if ($null -ne $overall -and $overall -gt 0 -and $overall -lt 100 -and
            $null -ne $file -and $file -gt 0 -and $file -lt 100) {
            @{ Overall = $overall; CurrentFile = $file }
        }
    } 'visible partial operation and file bars' 40
    Click $window 'Details (Ctrl+J)'
    $result.Stage = 'untouched live limit'
    Click $window ('Limit transfer speed' + [char]0x2026)
    $field = Prompt-Field $window
    $result.InitialPromptValue = Edit-Value $field
    if ($result.InitialPromptValue -notin @('0.0625', '0,0625')) { throw 'The exact 0.0625 MB/s limit was rounded in the live prompt.' }
    Prompt-Click $window 'OK'
    Click $window ('Limit transfer speed' + [char]0x2026)
    $field = Prompt-Field $window
    $result.ReopenedPromptValue = Edit-Value $field
    if ($result.ReopenedPromptValue -ne $result.InitialPromptValue) { throw 'Untouched confirmation changed the live limit.' }

    $result.Stage = 'invalid inline error and correction'
    Set-Edit $field '-1'
    $result.InvalidHelp = Wait-For {
        $current = Prompt-Field $window
        if ($current.Current.HelpText -like 'Speed limit:*') { $current.Current.HelpText }
    } 'field-associated speed error'
    $errorElement = Wait-For {
        @(Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -eq $result.InvalidHelp -and !$_.Current.IsOffscreen
        } | Select-Object -First 1)
    } 'visible inline speed error'
    $ok = Control $window ([System.Windows.Automation.ControlType]::Button) 'OK'
    if (!$errorElement -or !$ok -or $ok.Current.IsEnabled) { throw 'Invalid speed did not retain a disabled prompt with an exposed error.' }
    Set-Edit $field '0.125'
    Prompt-Click $window 'OK'
    Click $window ('Limit transfer speed' + [char]0x2026)
    $field = Prompt-Field $window
    $result.EditedPromptValue = Edit-Value $field
    if ($result.EditedPromptValue -notin @('0.125', '0,125')) { throw 'Corrected live limit was not retained.' }
    Set-Edit $field '0.5'
    Prompt-Click $window 'Cancel'
    Click $window ('Limit transfer speed' + [char]0x2026)
    $field = Prompt-Field $window
    $result.AfterCancelValue = Edit-Value $field
    if ($result.AfterCancelValue -ne $result.EditedPromptValue) { throw 'Cancel changed the live limit.' }
    Prompt-Click $window 'Cancel'
    $result.SourceUnchanged = (Get-FileHash -LiteralPath $large -Algorithm SHA256).Hash -eq $sourceHash
    $result.Passed = $result.SourceUnchanged
    $result.Stage = 'complete'
} catch {
    $result.Passed = $false
    $result.Error = $_.Exception.ToString()
} finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    }
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 8), [Text.Encoding]::UTF8)
}
if (!$result.Passed) { exit 1 }
