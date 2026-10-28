param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][string]$ExpectedHead
)

# Run only in the logged-in, disposable Windows VMware guest, with test-owned Git data.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class FileCatGitDpi { [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd); }'

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$repo = [IO.Path]::GetFullPath((Join-Path (Split-Path $app) '..\..\..\..\..'))
$git = 'C:\Program Files\Git\cmd\git.exe'
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notlike '*VMware*' -or
    !$app.StartsWith($validation + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($app) -ine 'FileCat.exe' -or
    !(Test-Path -LiteralPath $app -PathType Leaf) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat -ErrorAction SilentlyContinue)) {
    throw 'Use an idle VMware guest, a Git-built FileCat.exe and a fresh validation report.'
}
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
function Row($window, [string]$name, [string]$status) {
    @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -eq $name -and !$_.Current.IsOffscreen -and
        $_.Current.HelpText -like "*Git $status*"
    } | Select-Object -First 1)
}
function Capture-Icon($row, [string]$path) {
    $bounds = $row.Current.BoundingRectangle
    $x = [int][Math]::Floor($bounds.Left + 5)
    $y = [int][Math]::Floor($bounds.Top + ($bounds.Height - 16) / 2)
    $bitmap = [Drawing.Bitmap]::new(16, 16)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($x, $y, 0, 0, [Drawing.Size]::new(16, 16)) }
        finally { $graphics.Dispose() }
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        $pixels = New-Object byte[] (16 * 16 * 4)
        $at = 0
        for ($py = 0; $py -lt 16; $py++) {
            for ($px = 0; $px -lt 16; $px++) {
                $color = $bitmap.GetPixel($px, $py)
                $pixels[$at++] = $color.R; $pixels[$at++] = $color.G
                $pixels[$at++] = $color.B; $pixels[$at++] = $color.A
            }
        }
        return ,$pixels
    }
    finally { $bitmap.Dispose() }
}
function Git([string[]]$arguments) {
    & $git -c user.name=FileCat -c user.email=filecat@example.com -c core.autocrlf=false -C $source @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git $($arguments -join ' ') failed." }
}

$fixture = Join-Path $validation ('GitBadgeUx\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'target'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Force -Path $source, $target, $data | Out-Null
$cleanPng = Join-Path $validation 'git-badge-win11-clean.png'
$changedPng = Join-Path $validation 'git-badge-win11-changed.png'
$result = [ordered]@{
    Passed = $false; Stage = 'fixture'; GitHead = $head; Fixture = $fixture
    DllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
}
$process = $null
try {
    Git -arguments @('init', '-q')
    $tracked = Join-Path $source 'tracked.txt'
    [IO.File]::WriteAllText($tracked, "first`n")
    Git -arguments @('add', 'tracked.txt')
    Git -arguments @('commit', '-q', '-m', 'first')
    $result.InitialSourceSha256 = (Get-FileHash -LiteralPath $tracked -Algorithm SHA256).Hash

    $result.Stage = 'launch and clean badge'
    $process = Start-Process -FilePath $app -ArgumentList @('--new-instance', '--data', $data,
        '--left', $source, '--right', $target) -PassThru
    Wait-For { $process.Refresh(); if ($process.HasExited) { throw 'Owned FileCat exited early.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.MainWindowHandle } } 'FileCat window'
    if ([IO.Path]::GetFullPath($process.MainModule.FileName) -ine $app) { throw 'Wrong executable owns the window.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $result.Dpi = [FileCatGitDpi]::GetDpiForWindow($process.MainWindowHandle)
    $clean = Wait-For { Row $window 'tracked.txt' 'clean' } 'Git clean row'
    $result.CleanHelpText = $clean.Current.HelpText
    Start-Sleep -Milliseconds 300
    $cleanPixels = Capture-Icon $clean $cleanPng

    $result.Stage = 'watcher change and repainted badge'
    [IO.File]::AppendAllText($tracked, "second`n")
    $changed = Wait-For { Row $window 'tracked.txt' 'changed' } 'Git changed row after filesystem notification' 35
    $result.ChangedHelpText = $changed.Current.HelpText
    Start-Sleep -Milliseconds 300
    $changedPixels = Capture-Icon $changed $changedPng
    $different = 0
    for ($i = 0; $i -lt $cleanPixels.Length; $i++) { if ($cleanPixels[$i] -ne $changedPixels[$i]) { $different++ } }
    $result.DifferentIconChannels = $different
    if ($different -eq 0) { throw 'The visible 16-pixel icon did not change with the Git status.' }
    $result.ChangedSourceSha256 = (Get-FileHash -LiteralPath $tracked -Algorithm SHA256).Hash
    $result.CleanIconPng = $cleanPng
    $result.ChangedIconPng = $changedPng
    $result.Passed = $true
    $result.Stage = 'complete'
}
catch { $result['Error'] = $_.Exception.Message; throw }
finally {
    if ($process) {
        try { $process.CloseMainWindow() | Out-Null; if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id -Force } } catch { }
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report
}
