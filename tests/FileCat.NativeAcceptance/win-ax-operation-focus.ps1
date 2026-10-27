param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [switch]$CheckSelectionPreservation
)

# Use only in the disposable, logged-in Windows VMware guest. Every file and
# folder lives under a fresh FileCatValidation fixture; no product test hook.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report)) {
    throw 'Use a Git-built FileCat.exe and fresh report directly below FileCatValidation.'
}
if (Get-Process -Name FileCat -ErrorAction SilentlyContinue) {
    throw 'Close other FileCat processes before the isolated operation-focus test.'
}
$git = 'C:\Program Files\Git\cmd\git.exe'
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$head = (& $git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'The test app is not from a readable Git checkout.' }

function Wait-For([scriptblock]$Check, [string]$Description, [int]$Seconds = 20) {
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
function Find-Control($Owner, $Type, [string]$Name) {
    @(Descendants $Owner | Where-Object {
        $_.Current.ControlType -eq $Type -and $_.Current.Name -eq $Name -and !$_.Current.IsOffscreen
    } | Select-Object -First 1)
}
function Invoke-Button($Window, [string]$Name) {
    $button = Wait-For {
        @(Descendants $Window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $_.Current.Name -eq $Name -and !$_.Current.IsOffscreen
        } | Sort-Object { $_.Current.BoundingRectangle.Top } -Descending | Select-Object -First 1)
    } "button $Name"
    if (!$button.Current.IsEnabled) { throw "$Name is disabled." }
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Invoke-DialogButton($Window, [string]$DialogName, [string]$ButtonName) {
    $dialog = Wait-For {
        @(Descendants $Window | Where-Object {
            $_.Current.Name -eq $DialogName -and !$_.Current.IsOffscreen
        } | Select-Object -First 1)
    } "$DialogName overlay"
    $button = Wait-For {
        @(Descendants $dialog | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $_.Current.Name -eq $ButtonName -and !$_.Current.IsOffscreen
        } | Select-Object -First 1)
    } "$ButtonName in $DialogName"
    if (!$button.Current.IsEnabled) { throw "$ButtonName in $DialogName is disabled." }
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Row($Window, [string]$Name) {
    @(Descendants $Window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq $Name
    } | Select-Object -First 1)
}
function Find-HistoryList($Window) {
    Find-Control $Window ([System.Windows.Automation.ControlType]::List) 'Operations history'
}
function History-Snapshot($Window) {
    $list = Wait-For { Find-HistoryList $Window } 'Operations history list'
    $items = @(Descendants $list | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem
    })
    $selection = ([System.Windows.Automation.SelectionPattern]$list.GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern)).Current.GetSelection()
    @{
        Items = @($items | ForEach-Object { @{ Text = @((Descendants $_) | Where-Object {
                    $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text
                } | ForEach-Object { $_.Current.Name }); Offscreen = $_.Current.IsOffscreen } })
        Selected = @($selection | ForEach-Object { @{ Text = @((Descendants $_) | Where-Object {
                    $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text
                } | ForEach-Object { $_.Current.Name }); Offscreen = $_.Current.IsOffscreen;
                    Bounds = @($_.Current.BoundingRectangle.Left, $_.Current.BoundingRectangle.Top,
                        $_.Current.BoundingRectangle.Width, $_.Current.BoundingRectangle.Height) } })
        Bounds = @($list.Current.BoundingRectangle.Left, $list.Current.BoundingRectangle.Top,
            $list.Current.BoundingRectangle.Width, $list.Current.BoundingRectangle.Height)
    }
}

$fixture = Join-Path $validation ('OperationFocus\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$large = Join-Path $source 'a-large.bin'
$stream = [IO.File]::Create($large)
try { $stream.SetLength(64L * 1024 * 1024) }
finally { $stream.Dispose() }
$largeHash = (Get-FileHash -LiteralPath $large -Algorithm SHA256).Hash
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head; Fixture = $fixture
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    LargeFileSha256 = $largeHash
}
$process = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    Wait-For { $process.Refresh(); if ($process.HasExited) { throw 'Owned FileCat exited early.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.MainWindowHandle } } 'FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) { throw 'Wrong executable owns the window.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    for ($i = 1; $i -le 8; $i++) {
        $result.Stage = "finished job $i"
        $sourceRow = Wait-For { Row $window 'a-large.bin' } 'current large source row'
        $sourceRow.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{F7}')
        $edit = Wait-For {
            @(Descendants $window | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
                $_.Current.Name -like 'Folder name*' -and !$_.Current.IsOffscreen
            } | Select-Object -First 1)
        } 'Create folder name field'
        $name = 'finished-' + $i.ToString('00')
        ([System.Windows.Automation.ValuePattern]$edit.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)).SetValue($name)
        Invoke-Button $window 'Create'
        Wait-For { if (Test-Path -LiteralPath (Join-Path $source $name) -PathType Container) { $true } } "created $name"
        Start-Sleep -Milliseconds 250
    }
    $result.SeededFolders = @(Get-ChildItem -LiteralPath $source -Directory | ForEach-Object Name | Sort-Object)
    if ($result.SeededFolders.Count -ne 8) { throw 'Eight finished folder jobs were not created.' }
    $result.Stage = 'old finished selection'
    Invoke-Button $window 'Details (Ctrl+J)'
    $result.Before = History-Snapshot $window
    if ($result.Before.Items.Count -lt 4 -or $result.Before.Selected.Count -ne 1) {
        throw 'Native Operations history did not expose the virtualized completed list and a selected row.'
    }
    Invoke-Button $window 'Details (Ctrl+J)'
    $result.Stage = 'start ninth copy'
    $sourceRow = Wait-For { Row $window 'a-large.bin' } 'large source row after folders'
    $sourceRow.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{F5}')
    $destination = Wait-For { Find-Control $window ([System.Windows.Automation.ControlType]::Edit) 'Destination' } 'Copy destination'
    $result.ReviewDestination = ([System.Windows.Automation.ValuePattern]$destination.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    if ($result.ReviewDestination.TrimEnd([char[]]@('\', '/')) -ine $target) {
        throw "Copy review did not capture the target folder: $($result.ReviewDestination)"
    }
    $options = Wait-For {
        @(Descendants $window | Where-Object {
            $_.Current.Name -eq 'Options' -and $_.Current.ControlType -in @(
                [System.Windows.Automation.ControlType]::Button,
                [System.Windows.Automation.ControlType]::CheckBox)
        } | Select-Object -First 1)
    } 'Copy Options expander'
    try { ([System.Windows.Automation.InvokePattern]$options.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
    catch { ([System.Windows.Automation.TogglePattern]$options.GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)).Toggle() }
    $limit = Wait-For { Find-Control $window ([System.Windows.Automation.ControlType]::Edit) 'Speed limit (MB/s):' } 'Copy speed field'
    ([System.Windows.Automation.ValuePattern]$limit.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue('0.0625')
    $result.BeforeCopyJournalCount = @(Get-ChildItem -LiteralPath $data -Recurse -Filter '*.fcj' -File).Count
    $result.CopyCandidates = @((Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name -eq 'Copy' -and !$_.Current.IsOffscreen
    }) | ForEach-Object { @($_.Current.BoundingRectangle.Left,
        $_.Current.BoundingRectangle.Top, $_.Current.BoundingRectangle.Width,
        $_.Current.BoundingRectangle.Height) })
    Invoke-DialogButton $window 'Copy dialog' 'Copy'
    Start-Sleep -Milliseconds 900
    $result.AfterCopyVisible = @((Descendants $window | Where-Object {
        $_.Current.ControlType -in @([System.Windows.Automation.ControlType]::Text,
            [System.Windows.Automation.ControlType]::Button) -and !$_.Current.IsOffscreen
    }) | ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Last 75)
    $result.AfterCopyJournalCount = @(Get-ChildItem -LiteralPath $data -Recurse -Filter '*.fcj' -File).Count
    $result.AfterSubmit = Wait-For {
        $visible = @((Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            !$_.Current.IsOffscreen
        }) | ForEach-Object { $_.Current.Name })
        if (@($visible | Where-Object { $_ -like 'Copying*' }).Count -gt 0 -and
            @(Get-ChildItem -LiteralPath $data -Recurse -Filter '*.fcj' -File).Count -gt
                $result.BeforeCopyJournalCount) { $visible }
    } 'independently visible ninth Running Copy' 25
    $result.Stage = 'open Details during observed running copy'
    Invoke-Button $window 'Details (Ctrl+J)'
    $result.AfterInitial = History-Snapshot $window
    $result.After = Wait-For {
        $snapshot = History-Snapshot $window
        if ($snapshot.Selected.Count -eq 1 -and
            @($snapshot.Selected[0].Text | Where-Object { $_ -match 'Running' }).Count -gt 0) {
            $snapshot
        }
    } 'running Copy selected in Details' 12
    $result.Stage = 'complete'
    $result.Passed = $result.After.Items.Count -ge 4 -and $result.After.Selected.Count -eq 1 -and
        @($result.After.Selected[0].Text | Where-Object { $_ -match 'Copy' }).Count -gt 0 -and
        !$result.After.Selected[0].Offscreen -and
        (Get-FileHash -LiteralPath $large -Algorithm SHA256).Hash -eq $largeHash
    if (!$result.Passed) { throw 'Details did not select and reveal the ninth running Copy.' }
    if ($CheckSelectionPreservation) {
        $result.Stage = 'deliberate finished selection during active Copy'
        $history = Wait-For { Find-HistoryList $window } 'open Operations history'
        $finished = Wait-For {
            @(Descendants $history | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
                @((Descendants $_) | Where-Object {
                    $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
                    $_.Current.Name -eq 'Create folder "finished-08"'
                }).Count -gt 0
            } | Select-Object -First 1)
        } 'finished-08 history row'
        ([System.Windows.Automation.SelectionItemPattern]$finished.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
        $result.DeliberateSelection = History-Snapshot $window
        $result.ProgressBefore = @((Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
            $_.Current.Name -like 'Copying*' -and !$_.Current.IsOffscreen
        }) | ForEach-Object { $_.Current.Name } | Select-Object -First 1)
        if ($result.ProgressBefore.Count -ne 1) { throw 'Running Copy progress is not visible before the later tick.' }
        $result.ProgressAfter = Wait-For {
            $current = @((Descendants $window | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
                $_.Current.Name -like 'Copying*' -and !$_.Current.IsOffscreen
            }) | ForEach-Object { $_.Current.Name } | Select-Object -First 1)
            if ($current.Count -eq 1 -and $current[0] -cne $result.ProgressBefore[0]) { $current }
        } 'later active Copy progress' 30
        $result.SelectionAfterProgress = History-Snapshot $window
        $result.Passed = $result.DeliberateSelection.Selected.Count -eq 1 -and
            $result.SelectionAfterProgress.Selected.Count -eq 1 -and
            @($result.DeliberateSelection.Selected[0].Text | Where-Object {
                $_ -eq 'Create folder "finished-08"' }).Count -eq 1 -and
            @($result.SelectionAfterProgress.Selected[0].Text | Where-Object {
                $_ -eq 'Create folder "finished-08"' }).Count -eq 1 -and
            !$result.SelectionAfterProgress.Selected[0].Offscreen -and
            (Get-FileHash -LiteralPath $large -Algorithm SHA256).Hash -eq $largeHash
        if (!$result.Passed) { throw 'Later active progress stole the deliberate finished-row selection.' }
        $result.Stage = 'complete'
    }
}
catch {
    $result.Passed = $false
    $result.Error = $_.Exception.Message
    $result.ErrorType = $_.Exception.GetType().FullName
}
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) {
            $process.Kill()
            $process.WaitForExit(5000) | Out-Null
        }
    }
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 10), [Text.Encoding]::UTF8)
}
if (!$result.Passed) { exit 1 }
