param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$NvdaPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Isolated Windows VMware acceptance: two conflicts separated by a rate-limited,
# test-owned copy. No product hooks or unrelated user files are used.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$nvda = [IO.Path]::GetFullPath($NvdaPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (!$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or !(Test-Path -LiteralPath $app -PathType Leaf) -or
    !$nvda.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($nvda) -ine 'nvda.exe' -or
    (Get-AuthenticodeSignature -LiteralPath $nvda).Status -ne 'Valid' -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat,nvda -ErrorAction SilentlyContinue)) {
    throw 'Use the Git-built VM app, signed NVDA, fresh report and no other FileCat/NVDA process.'
}
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$head = (& 'C:\Program Files\Git\cmd\git.exe' -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'App is not from a readable Git checkout.' }

function Wait-For([scriptblock]$Check, [string]$Description, [int]$Seconds = 25) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $Seconds) {
        try { $value = & $Check; if ($value) { return $value } }
        catch {
            $inner = $_.Exception
            while ($inner -and $inner -isnot [System.Runtime.InteropServices.COMException]) {
                $inner = $inner.InnerException
            }
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
function Control($Owner,$Type,[string]$Name) {
    @(Descendants $Owner | Where-Object {
        $_.Current.ControlType -eq $Type -and $_.Current.Name -eq $Name -and !$_.Current.IsOffscreen
    } | Select-Object -First 1)
}
function Click($Owner,[string]$Name) {
    $button = Wait-For { Control $Owner ([System.Windows.Automation.ControlType]::Button) $Name } "button $Name"
    if (!$button.Current.IsEnabled) { throw "$Name is disabled." }
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Click-Starts($Owner,[string]$Prefix) {
    $button = Wait-For { @(Descendants $Owner | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $_.Current.Name.StartsWith($Prefix,[StringComparison]::Ordinal) -and
        $_.Current.IsEnabled -and !$_.Current.IsOffscreen
    } | Select-Object -First 1) } "button beginning $Prefix"
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Dialog-Click($Window,[string]$Dialog,[string]$Name) {
    $group = Wait-For { @(Descendants $Window | Where-Object {
        $_.Current.Name -eq $Dialog -and !$_.Current.IsOffscreen
    } | Select-Object -First 1) } "$Dialog overlay"
    Click $group $Name
}
function Take-Speech {
    if (!(Test-Path -LiteralPath $nvdaLog)) { return @() }
    $stream = [IO.File]::Open($nvdaLog,'Open','Read','ReadWrite')
    try {
        $stream.Seek($script:speechOffset,'Begin') | Out-Null
        $reader = [IO.StreamReader]::new($stream,[Text.Encoding]::UTF8)
        $content = $reader.ReadToEnd()
        $last = $content.LastIndexOf("`n")
        if ($last -lt 0) { return @() }
        $complete = $content.Substring(0,$last+1)
        $script:speechOffset += [Text.Encoding]::UTF8.GetByteCount($complete)
        @($complete -split "`r?`n" | Where-Object { $_.StartsWith('Speaking [') } |
            ForEach-Object { $_.Substring(9).Trim() })
    } finally { $stream.Dispose() }
}
function Issue-Rows($Window) {
    $list = Wait-For { Control $Window ([System.Windows.Automation.ControlType]::List) 'Selected operation issues' } 'issue list'
    @(Descendants $list | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem
    })
}
function Live-Issue($Window,[int]$Count) {
    @(Descendants $Window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.AutomationId -eq 'operations-new-issue' -and
        $_.Current.Name.StartsWith("New issue; $Count total",[StringComparison]::Ordinal) -and
        !$_.Current.IsOffscreen
    } | Select-Object -First 1)
}

$fixture = Join-Path $validation ('OperationIssueSpeech\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
$config = Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $source,$target,$data,$config | Out-Null
foreach ($name in @('one.txt','two.txt')) {
    [IO.File]::WriteAllText((Join-Path $source $name),"new $name",[Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $target $name),"old $name",[Text.Encoding]::UTF8)
}
$padding = Join-Path $source 'padding.bin'
$stream = [IO.File]::Create($padding)
try { $stream.SetLength(4L * 1024 * 1024) } finally { $stream.Dispose() }
$sourceHashes = @{}
$targetHashes = @{}
foreach ($name in @('one.txt','two.txt')) {
    $sourceHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash
    $targetHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash
}
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'), @"
[general]
	showWelcomeDialogAtStartup = False
[update]
	autoCheck = False
	startupNotification = False
	allowUsageStats = False
	askedAllowUsageStats = True
"@)
$nvdaLog = Join-Path $fixture 'nvda.log'
$script:speechOffset = 0L
$result = [ordered]@{
    Passed = $false; Stage = 'launch'; GitHead = $head; Fixture = $fixture
    AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    NvdaVersion = (Get-Item -LiteralPath $nvda).VersionInfo.ProductVersion
}
$owned = $nvdaProcess = $null
try {
    $nvdaProcess = Start-Process -FilePath $nvda -ArgumentList @("--log-file=$nvdaLog",
        '--log-level=12',"--config-path=$config",'--disable-addons') -PassThru -WindowStyle Hidden
    Wait-For {
        if ($nvdaProcess.HasExited) { throw 'Owned NVDA exited early.' }
        if ((Test-Path -LiteralPath $nvdaLog) -and
            (Select-String -LiteralPath $nvdaLog -Pattern 'NVDA initialized' -SimpleMatch -Quiet)) { $true }
    } 'NVDA startup' 30 | Out-Null
    Take-Speech | Out-Null
    $owned = Start-Process -FilePath $app -ArgumentList @('--new-instance','--data',$data,
        '--left',$source,'--right',$target) -PassThru
    Wait-For { $owned.Refresh(); if ($owned.HasExited) { throw 'Owned FileCat exited early.' }
        if ($owned.MainWindowHandle -ne [IntPtr]::Zero) { $owned.MainWindowHandle } } 'FileCat window' | Out-Null
    if ([IO.Path]::GetFullPath($owned.MainModule.FileName) -ine $app) { throw 'Wrong app owns window.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle)
    $result.Stage = 'select three owned sources'
    $rows = @(Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -in @('one.txt','padding.bin','two.txt')
    } | Sort-Object { $_.Current.BoundingRectangle.Left })
    $sourceRows = @($rows | Select-Object -First 3)
    if ($sourceRows.Count -ne 3 -or @($sourceRows | ForEach-Object { $_.Current.Name } | Sort-Object) -join ',' -ne 'one.txt,padding.bin,two.txt') {
        throw 'The three source entries were not visible in the left panel.'
    }
    ([System.Windows.Automation.SelectionItemPattern]$sourceRows[0].GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    foreach ($row in $sourceRows[1..2]) {
        ([System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)).AddToSelection()
    }
    $sourceRows[0].SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{F5}')
    $result.Stage = 'Copy review'
    $destination = Wait-For { Control $window ([System.Windows.Automation.ControlType]::Edit) 'Destination' } 'Copy destination'
    $result.Destination = ([System.Windows.Automation.ValuePattern]$destination.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    if ($result.Destination.TrimEnd([char[]]@('\','/')) -ine $target) { throw 'Copy destination is not the owned target.' }
    $options = Wait-For { Control $window ([System.Windows.Automation.ControlType]::Button) 'Options' } 'Copy options'
    try { ([System.Windows.Automation.InvokePattern]$options.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
    catch { ([System.Windows.Automation.TogglePattern]$options.GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)).Toggle() }
    $limit = Wait-For { Control $window ([System.Windows.Automation.ControlType]::Edit) 'Speed limit (MB/s):' } 'speed limit'
    ([System.Windows.Automation.ValuePattern]$limit.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).SetValue('0.25')
    Dialog-Click $window 'Copy dialog' 'Copy'
    $result.Stage = 'select awaiting-decision job before any issues'
    Click $window 'Details (Ctrl+J)'
    $history = Wait-For { Control $window ([System.Windows.Automation.ControlType]::List) 'Operations history' } 'Operations history'
    $selectedHistory = ([System.Windows.Automation.SelectionPattern]$history.GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern)).Current.GetSelection()
    if ($selectedHistory.Count -ne 1 -or @(Issue-Rows $window).Count -ne 0) {
        throw 'The pending Copy was not selected before its first issue.'
    }
    Take-Speech | Out-Null
    $result.Stage = 'first conflict'
    Click-Starts $window 'Review decision'
    Click $window 'Skip'
    $firstLive = Wait-For { Live-Issue $window 1 } 'first live issue announcement'
    $result.FirstAnnouncement = $firstLive.Current.Name
    if (!$result.FirstAnnouncement.Contains('one.txt')) { throw 'First live issue omitted its path.' }
    Start-Sleep -Milliseconds 1200
    $result.FirstSpeech = @(Take-Speech)
    $issueList = Wait-For { Control $window ([System.Windows.Automation.ControlType]::List) 'Selected operation issues' } 'issue list'
    $firstRow = Wait-For { @(Issue-Rows $window | Where-Object {
        $_.Current.Name.Contains('one.txt')
    } | Select-Object -First 1) } 'first issue row'
    ([System.Windows.Automation.SelectionItemPattern]$firstRow.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    $firstRow.SetFocus()
    $result.SelectedIssueBefore = $firstRow.Current.Name
    $result.Stage = 'later second conflict after rate-limited copy'
    Click-Starts $window 'Review decision'
    Click $window 'Skip'
    $secondLive = Wait-For { Live-Issue $window 2 } 'second live issue announcement' 45
    $result.SecondAnnouncement = $secondLive.Current.Name
    if (!$result.SecondAnnouncement.Contains('two.txt')) { throw 'Second live issue omitted its path.' }
    Start-Sleep -Milliseconds 1200
    $result.SecondSpeech = @(Take-Speech)
    $selectedIssue = ([System.Windows.Automation.SelectionPattern]$issueList.GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern)).Current.GetSelection()
    $result.SelectedIssueAfter = @($selectedIssue | ForEach-Object { $_.Current.Name })
    $result.IssueRowsAfter = @(Issue-Rows $window | ForEach-Object { $_.Current.Name })
    $result.IssueSummaryAfter = @(Descendants $window | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and
        $_.Current.Name.StartsWith('Showing first 2 of 2 issues',[StringComparison]::Ordinal)
    } | ForEach-Object { $_.Current.Name } | Select-Object -First 1)
    Wait-For { if (Test-Path -LiteralPath (Join-Path $target 'padding.bin') -PathType Leaf) { $true } } 'copied padding' 45 | Out-Null
    $result.ExactFiles = @(foreach ($name in @('one.txt','two.txt')) {
        (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash -eq $sourceHashes[$name] -and
        (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash -eq $targetHashes[$name]
    }) -notcontains $false
    $result.PaddingExact = (Get-FileHash -LiteralPath (Join-Path $target 'padding.bin') -Algorithm SHA256).Hash -eq
        (Get-FileHash -LiteralPath $padding -Algorithm SHA256).Hash
    $result.FirstSpoken = (($result.FirstSpeech -join ' ').Contains('New issue') -and
        ($result.FirstSpeech -join ' ').Contains('one.txt'))
    $result.SecondSpoken = (($result.SecondSpeech -join ' ').Contains('New issue') -and
        ($result.SecondSpeech -join ' ').Contains('two.txt'))
    $result.Passed = $result.FirstSpoken -and $result.SecondSpoken -and
        $result.ExactFiles -and $result.PaddingExact -and
        $result.IssueSummaryAfter.Count -eq 1 -and $result.SelectedIssueAfter.Count -eq 1 -and
        $result.SelectedIssueAfter[0] -eq $result.SelectedIssueBefore
    if (!$result.Passed) { throw 'Native issue announcement, selection or exact-file check failed.' }
    $result.Stage = 'complete'
} catch {
    $result.Error = $_.Exception.Message
    try {
        $result.VisibleButtonsAtFailure = @((Descendants $window | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            !$_.Current.IsOffscreen
        }) | ForEach-Object { $_.Current.Name } | Select-Object -Last 50)
    } catch { }
} finally {
    if ($owned -and !$owned.HasExited) {
        $owned.CloseMainWindow() | Out-Null
        if (!$owned.WaitForExit(5000)) { $owned.Kill(); $owned.WaitForExit(5000) | Out-Null }
    }
    if ($nvdaProcess -and !$nvdaProcess.HasExited) {
        $nvdaProcess.Kill(); $nvdaProcess.WaitForExit(5000) | Out-Null
    }
    [IO.File]::WriteAllText($report,($result | ConvertTo-Json -Depth 7),[Text.Encoding]::UTF8)
}
if (!$result.Passed) { exit 1 }
