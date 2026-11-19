param([Parameter(Mandatory)][string]$TestRoot, [switch]$IsolatedSession)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($TestRoot)
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation')) + '\'
$machine = Get-CimInstance Win32_ComputerSystem
$login = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$session = (Get-Process -Id $PID).SessionId
$validSession = if ($IsolatedSession) {
    $session -eq 0 -and $root.StartsWith('C:\FileCatTests\', [StringComparison]::OrdinalIgnoreCase)
} else {
    $session -ne 0 -and $machine.UserName -eq $login -and $root.StartsWith($validation, [StringComparison]::OrdinalIgnoreCase)
}
if (!$validSession -or $machine.Manufacturer -notmatch 'VMware') {
    throw 'Use an isolated test directory in a disposable VMware guest with the selected desktop mode.'
}

function Assert-Idle {
    if (Get-Process dotnet,testhost,FileCat,FileCat.App.Tests,FileCat.*QualificationHost,FileCat.*CrashHarness,nvda -ErrorAction SilentlyContinue) {
        throw 'Another process is using this VM for FileCat testing.'
    }
}
Assert-Idle
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$uia = [System.Windows.Automation.AutomationElement]
$control = [System.Windows.Automation.ControlType]
$scope = [System.Windows.Automation.TreeScope]
$all = [System.Windows.Automation.Condition]::TrueCondition

function Measure-App {
    if (!$app) { return }
    $app.Refresh()
    if ($app.HasExited) { throw "Qualification host exited with $($app.ExitCode)." }
    $result.PeakHandles = [Math]::Max($result.PeakHandles, $app.HandleCount)
    $result.PeakWorkingSetBytes = [Math]::Max($result.PeakWorkingSetBytes, $app.WorkingSet64)
}
function Await([scriptblock]$Check, [string]$Description, [int]$Seconds = 30) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        Measure-App
        try { $value = & $Check; if ($value) { return $value } }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        catch [Runtime.InteropServices.COMException] { }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for $Description."
}
function Find($Owner, [string]$Name, $Type = $null) {
    $Owner.FindAll($scope::Descendants, $all) | Where-Object {
        $_.Current.Name -like $Name -and !$_.Current.IsOffscreen -and (!$Type -or $_.Current.ControlType -eq $Type)
    } | Select-Object -First 1
}
function Button($Owner, [string]$Name) {
    Await { $item = Find $Owner $Name $control::Button; if ($item -and $item.Current.IsEnabled) { $item } } "enabled $Name button"
}
function Invoke-Button($Owner, [string]$Name) {
    $button = Button $Owner $Name
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Menu-Item([string]$Name) {
    Await {
        $item = Find $main $Name $control::MenuItem
        if ($item) { return $item }
        foreach ($window in $uia::RootElement.FindAll($scope::Children, $all)) {
            if ($window.Current.ProcessId -eq $app.Id -and $window.Current.NativeWindowHandle -ne $main.Current.NativeWindowHandle) {
                $item = Find $window $Name $control::MenuItem
                if ($item) { return $item }
            }
        }
    } "menu item $Name"
}
function Expand-Menu([string]$Name) {
    $item = Menu-Item $Name
    $item.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
}
function Invoke-Menu([string]$Name) {
    $item = Menu-Item $Name
    if (!$item.Current.IsEnabled) { throw "The menu item $Name is disabled." }
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Select-Option([string]$Name) {
    $item = Await { Find $main $Name $control::RadioButton } "radio option $Name"
    $pattern = $null
    if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
        $pattern.Select()
    } else {
        $toggle = $item.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
    }
}
function Report-Window([string]$Title) {
    Await {
        $uia::RootElement.FindAll($scope::Children, $all) | Where-Object {
            $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like $Title
        } | Select-Object -First 1
    } $Title
}
function Close-Report($Window) {
    $Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
function Select-History([string]$Title) {
    $history = Await { Find $main 'Operations history' $control::List } 'Operations history'
    $row = Await { Find $history $Title $control::ListItem } "history $Title"
    $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    return $row
}
function Read-CallPage($Report, [int]$FirstCall) {
    $line = Await { Find $Report 'Line 1 of *FileCat recorded storage calls' $control::Text } 'rendered exact call page'
    $digest = $null
    for ($number = 2; $number -le 18; $number++) {
        Invoke-Button $Report 'Next report line'
        $line = Await { Find $Report "Line $number of *" $control::Text } "report line $number"
        $name = $line.Current.Name
        if ($name -match 'Durable snapshot SHA-256: ([0-9a-f]{64})$') { $digest = $Matches[1] }
        if ($name -like "*Call $FirstCall; selected root 1") {
            if (!$digest) { throw 'The call page did not expose its durable digest.' }
            return $digest
        }
    }
    throw "The loaded page did not start with call $FirstCall."
}

$app = $null
$exe = Join-Path $root 'app\FileCat.ArchiveQualificationHost.exe'
$fixture = Join-Path $root 'fixture'
$receipt = Join-Path $root 'operations-scale.json'
if ((Test-Path -LiteralPath $fixture) -or (Test-Path -LiteralPath $receipt)) { throw 'Use a fresh test root.' }
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($fixture))
if ($drive.DriveFormat -ne 'NTFS' -or $drive.AvailableFreeSpace -lt 256MB) { throw 'The fixture requires space on local NTFS.' }
$source = Join-Path $fixture 'left'
$inputFolder = Join-Path $source 'compress-me'
$other = Join-Path $fixture 'right'
$data = Join-Path $fixture 'data'
$budget = Join-Path $root 'operations-dispatcher.json'
$queuedPath = Join-Path $other 'queued-during-pause'
$result = [ordered]@{ Passed = $false; Stage = 'seed'; Error = $null; SessionId = $session; FileCount = 10000; PeakHandles = 0; PeakWorkingSetBytes = 0 }
function Set-Stage([string]$Stage) {
    $result.Stage = $Stage
    Set-Content -LiteralPath (Join-Path $root 'operations-stage.txt') -Value $Stage -Encoding UTF8
}
try {
    Set-Stage 'seed'
    $result.OS = Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture
    $result.Volume = $drive.DriveFormat
    $result.AppSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'FileCat.dll')).Hash
    $result.WindowsSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'FileCat.Platform.Windows.dll')).Hash
    $result.HostSha256 = (Get-FileHash -LiteralPath $exe).Hash
    New-Item -ItemType Directory -Path $inputFolder,$other,$data -Force | Out-Null
    if ($IsolatedSession) {
        $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
        $access = [Security.AccessControl.DirectorySecurity]::new()
        $access.SetOwner($owner)
        $access.SetAccessRuleProtection($true, $false)
        foreach ($principal in $owner,$system) {
            $access.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                $principal, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        }
        Set-Acl -LiteralPath $data -AclObject $access
    }
    $otherFile = Join-Path $other 'unrelated.txt'
    [IO.File]::WriteAllText($otherFile, 'Unrelated source.')
    $payload = [Text.Encoding]::ASCII.GetBytes(('U' * 8192))
    $seed = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $result.FileCount; $i++) {
        [IO.File]::WriteAllBytes((Join-Path $inputFolder ($i.ToString('D5') + '.bin')), $payload)
    }
    $result.SeedMs = $seed.Elapsed.TotalMilliseconds
    $first = Join-Path $inputFolder '00000.bin'
    $last = Join-Path $inputFolder '09999.bin'
    $hash = (Get-FileHash -LiteralPath $first).Hash
    if (([IO.File]::GetAttributes($inputFolder) -band [IO.FileAttributes]::Compressed) -ne 0) {
        throw 'The seed folder inherits NTFS compression.'
    }

    Set-Stage 'start recursive compression through review'
    Assert-Idle
    if ($IsolatedSession) { $env:FILECAT_RENDERING = 'compat' }
    $app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru -ArgumentList @(
        '--ntfs-storage-review', '--new-instance', '--reset-layout', '--data', "`"$data`"", '--left', "`"$source`"",
        '--right', "`"$other`"", '--ui-budget', "`"$budget`"")
    $main = Await { if ($app.MainWindowHandle -ne [IntPtr]::Zero) { $uia::FromHandle($app.MainWindowHandle) } } 'FileCat window'
    if ($app.SessionId -ne $session -or $main.Current.ProcessId -ne $app.Id) { throw 'The native window does not belong to the owned session and process.' }
    $folder = Await { Find $main 'compress-me' $control::ListItem } 'source folder'
    $folder.SetFocus()
    Expand-Menu 'File'
    Expand-Menu 'Inspect'
    Invoke-Menu 'NTFS compression*'
    Select-Option 'Compress'
    Select-Option 'Selected folders and their existing contents'
    $result.StartUtc = [DateTime]::UtcNow.ToString('O')
    $jobClock = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Button $main 'Compress'
    Invoke-Button $main 'Details (Ctrl+J)'
    $jobTitle = 'Compress*compress-me*selected folders and existing contents*'
    $null = Select-History $jobTitle
    $details = Await { Find $main 'Selected operation details' } 'selected operation details'
    $app.Refresh()
    $result.InitialHandles = $app.HandleCount
    $result.InitialWorkingSetBytes = $app.WorkingSet64
    Set-Stage 'wait for live Operations counts'
    Await {
        $text = Find $details '*checked*' $control::Text
        if ($text -and $text.Current.Name -match '^([\d,]+) checked\b') { [int]($Matches[1] -replace ',', '') -ge 160 }
    } '160 checked items in Operations' 240 | Out-Null
    if ((Find $details 'Show speed history' $control::Button) -or (Find $details '*[0-9]%*' $control::Text)) {
        throw 'Storage presented transfer speed history or a percentage during discovery.'
    }

    Set-Stage 'pause and queue a disjoint job'
    $pause = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Button $details 'Pause'
    $null = Button $details 'Resume'
    $result.PauseMs = $pause.Elapsed.TotalMilliseconds
    $result.PausedUtc = [DateTime]::UtcNow.ToString('O')
    $pausedCount = (Await { Find $details '*checked*' $control::Text } 'paused count').Current.Name
    $targetRow = Await { Find $main 'unrelated.txt' $control::ListItem } 'unrelated panel item'
    $targetRow.SetFocus()
    Expand-Menu 'File'
    Invoke-Menu 'Create folder*'
    $edit = Await { Find $main 'Folder name*' $control::Edit } 'unrelated folder name field'
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('queued-during-pause')
    Invoke-Button $main 'Create'
    $waiting = Select-History 'Create folder*queued-during-pause*'
    $result.WaitReason = (Await { Find $waiting '*storage changes run exclusively with other FileCat jobs*' $control::Text } 'storage wait reason').Current.Name
    if (Test-Path -LiteralPath $queuedPath) { throw 'Unrelated job ran during the pause.' }

    Set-Stage 'read and page live durable calls'
    $null = Select-History $jobTitle
    Invoke-Button $main 'Open report'
    $report = Report-Window '*Operation report*FileCat*'
    $callClock = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Button $report 'View recorded calls*'
    $calls = Report-Window '*Recorded storage calls*FileCat*'
    $null = Button $calls 'Next recorded call page'
    $result.FirstPageMs = $callClock.Elapsed.TotalMilliseconds
    $result.PrefixSha256 = Read-CallPage $calls 1
    $callClock.Restart()
    Invoke-Button $calls 'Next recorded call page'
    $null = Button $calls 'First recorded call page'
    $result.NextPageMs = $callClock.Elapsed.TotalMilliseconds
    if ((Read-CallPage $calls 65) -ne $result.PrefixSha256) { throw 'Paging changed the captured durable prefix.' }
    Close-Report $calls
    Close-Report $report
    $details = Await { Find $main 'Selected operation details' } 'compression details after report'
    $result.PausedCount = $pausedCount
    if ((Await { Find $details '*checked*' $control::Text } 'count after reading').Current.Name -ne $pausedCount -or
        (Test-Path -LiteralPath $queuedPath)) { throw 'Work advanced while paused.' }

    Set-Stage 'resume and verify the UI and NTFS result'
    $result.ResumedUtc = [DateTime]::UtcNow.ToString('O')
    Invoke-Button $details 'Resume'
    $expected = (10001).ToString('N0')
    $completedRow = Select-History $jobTitle
    $complete = Await {
        Find $completedRow "*Completed*$expected changed*The requested scope was fully checked*" $control::Text
    } 'complete storage result' 540
    $result.Completion = $complete.Current.Name
    Await { Test-Path -LiteralPath $queuedPath -PathType Container } 'unrelated queued operation to run' | Out-Null
    $result.JobMs = $jobClock.Elapsed.TotalMilliseconds
    $result.FinishedUtc = [DateTime]::UtcNow.ToString('O')
    $files = @(Get-ChildItem -LiteralPath $inputFolder -File)
    if ($files.Count -ne $result.FileCount -or ($files | Where-Object { !($_.Attributes -band [IO.FileAttributes]::Compressed) }) -or
        (Get-FileHash -LiteralPath $first).Hash -ne $hash -or (Get-FileHash -LiteralPath $last).Hash -ne $hash) {
        throw 'The NTFS fixture did not retain all files with the expected compressed attribute and sample contents.'
    }
    $result.JournalBytes = (Get-ChildItem -LiteralPath (Join-Path $data 'local\journal') -Filter 'job-storage-*' -File | Measure-Object Length -Sum).Sum
    Set-Stage 'completed'
    $result.Passed = $true
}
catch { $result.Error = $_ | Out-String; throw }
finally {
    if ($app -and !$app.HasExited) {
        [void]$app.CloseMainWindow()
        if (!$app.WaitForExit(30000)) { Stop-Process -Id $app.Id }
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receipt -Encoding UTF8
}
