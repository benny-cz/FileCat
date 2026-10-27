param(
    [Parameter(Mandatory = $true)][string]$AppPath,
    [Parameter(Mandatory = $true)][string]$ReportPath
)

# Logged-in Windows VMware guest only. The host captures two full-screen
# frames at each READY marker and creates its matching .done file afterward.
# All preferences and files are restored in finally, including failure paths.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FileCatMotionNative {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SystemParametersInfoW(uint action, uint parameter, ref int value, uint flags);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    public static extern bool SystemParametersInfoSet(uint action, uint parameter, IntPtr value, uint flags);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
    public static void Click(int x, int y) {
        if (!SetCursorPos(x, y)) throw new InvalidOperationException("Could not position owned test pointer.");
        System.Threading.Thread.Sleep(100);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(100);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@

$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$app = [IO.Path]::GetFullPath($AppPath)
$report = [IO.Path]::GetFullPath($ReportPath)
$root = Join-Path $validation ('ThemeMotion\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'target'
$data = Join-Path $root 'data'
$expectedApp = Join-Path $validation 'GitMain-AC-S2-013-4c042d37\src\FileCat.App\bin\Release\net10.0\FileCat.exe'
$content = [Text.Encoding]::UTF8.GetBytes("VISIBLE CONTENT STAYS FIXED`n")
$result = [ordered]@{ Passed = $false; Head = '4c042d37'; AppDllSha256 = $null;
    OriginalAnimation = $null; RestoredAnimation = $null; Phases = @();
    SourceUnchanged = $false; TargetEmpty = $false; Error = $null }
$process = $null
$original = $null

function Get-Animation {
    $value = 1
    if (![FileCatMotionNative]::SystemParametersInfoW(0x1042, 0, [ref]$value, 0)) {
        throw 'Windows did not expose SPI_GETCLIENTAREAANIMATION.'
    }
    return $value
}

function Set-Animation([int]$enabled) {
    if (![FileCatMotionNative]::SystemParametersInfoSet(0x1043, 0, [IntPtr]$enabled, 2)) {
        throw 'Windows refused SPI_SETCLIENTAREAANIMATION.'
    }
    if ((Get-Animation) -ne $enabled) { throw 'Windows did not retain the requested animation setting.' }
}

function Foreground-ProcessId {
    [uint32]$id = 0
    [void][FileCatMotionNative]::GetWindowThreadProcessId(
        [FileCatMotionNative]::GetForegroundWindow(), [ref]$id)
    return $id
}

function Handoff([string]$phase) {
    if (!$process -or $process.HasExited) { throw "FileCat exited before $phase." }
    [FileCatMotionNative]::Click(770, 130) # centered in the owned 220,115,1100x760 title bar
    [void][FileCatMotionNative]::SetForegroundWindow($process.MainWindowHandle)
    [void][FileCatMotionNative]::SetCursorPos(1750, 1000)
    Start-Sleep -Milliseconds 300
    $foreground = Foreground-ProcessId
    if ($foreground -ne $process.Id) { throw "FileCat lost native foreground at ${phase}: $foreground" }
    $ready = [IO.Path]::ChangeExtension($report, ".${phase}.ready")
    $done = [IO.Path]::ChangeExtension($report, ".${phase}.done")
    [IO.File]::WriteAllText($ready, $phase)
    $result.Phases += [ordered]@{ Name = $phase; Animation = Get-Animation;
        ForegroundPid = $foreground; AppPid = $process.Id }
    Write-Output "READY $phase $done"
    $deadline = [DateTime]::UtcNow.AddMinutes(4)
    while (!(Test-Path -LiteralPath $done)) {
        if ($process.HasExited -or [DateTime]::UtcNow -ge $deadline) {
            throw "The $phase capture handoff expired."
        }
        Start-Sleep -Milliseconds 200
    }
    Remove-Item -LiteralPath $ready, $done -Force
}

try {
    if (!$app.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        ![string]::Equals($app, $expectedApp, [StringComparison]::OrdinalIgnoreCase) -or
        $report -ne (Join-Path $validation 'ac-s2-013-win-motion-4c042d37-r6.json') -or
        !(Test-Path -LiteralPath $app -PathType Leaf) -or
        (Test-Path -LiteralPath $report) -or (Get-Process FileCat -ErrorAction SilentlyContinue)) {
        throw 'Use the exact test-owned Git app, fresh guest receipt, and no other FileCat process.'
    }
    foreach ($folder in @($source, $target, $data)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    [IO.File]::WriteAllBytes((Join-Path $source 'visible.txt'), $content)
    [IO.File]::WriteAllText((Join-Path $source '.hidden.txt'), "hidden but actionable`n")
    [IO.File]::WriteAllText((Join-Path $data 'settings.json'),
        '{"SchemaVersion":1,"Theme":"Psychedelic","ThemeAnimations":true}')
    $result.AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    if ($result.AppDllSha256 -ne 'D4A4E64C10BCB6C41D0D37AA6516ABD22DDD4BD54D60FEC05B813974D6BD9952') {
        throw 'The Windows app DLL does not match the verified 4c042d37 Git build.'
    }
    $original = Get-Animation
    $result.OriginalAnimation = $original
    Set-Animation 1
    $arguments = '--new-instance --data "{0}" --left "{1}" --right "{2}"' -f $data, $source, $target
    $process = Start-Process -FilePath $app -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Normal -PassThru
    for ($attempt = 0; $attempt -lt 80 -and $process.MainWindowHandle -eq [IntPtr]::Zero; $attempt++) {
        if ($process.HasExited) { throw 'The Git-built app exited before its main window opened.' }
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "The owned FileCat window has no handle (pid=$($process.Id), exited=$($process.HasExited), session=$($process.SessionId))."
    }
    [uint32]$windowOwner = 0
    [void][FileCatMotionNative]::GetWindowThreadProcessId($process.MainWindowHandle, [ref]$windowOwner)
    $result.WindowAtLaunch = [ordered]@{ Handle = [string]$process.MainWindowHandle;
        OwnerPid = $windowOwner; ProcessId = $process.Id; HasExited = $process.HasExited;
        SessionId = $process.SessionId }
    if (![FileCatMotionNative]::MoveWindow($process.MainWindowHandle, 220, 115, 1100, 760, $true)) {
        throw "The owned FileCat window could not be placed (Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error()), owner=$windowOwner, exited=$($process.HasExited))."
    }
    foreach ($item in @(@('moving', 1), @('reduced', 0), @('resumed', 1))) {
        Set-Animation $item[1]
        Start-Sleep -Seconds 6  # live preference poll is three seconds
        Handoff $item[0]
    }
    $result.SourceUnchanged = [Convert]::ToBase64String(
        [IO.File]::ReadAllBytes((Join-Path $source 'visible.txt'))) -eq
        [Convert]::ToBase64String($content)
    $result.TargetEmpty = @(Get-ChildItem -LiteralPath $target -Force).Count -eq 0
    if (!$result.SourceUnchanged -or !$result.TargetEmpty) { throw 'The native motion route changed its fixture.' }
    $result.Passed = $true
}
catch { $result.Error = $_.Exception.Message }
finally {
    if ($process -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(10000)) { $process.Kill(); $process.WaitForExit() }
    }
    if ($null -ne $original) {
        try { Set-Animation $original; $result.RestoredAnimation = Get-Animation }
        catch { $result.Error = "Restore failed: $($_.Exception.Message); $($result.Error)"; $result.Passed = $false }
    }
    foreach ($phase in @('moving', 'reduced', 'resumed')) {
        foreach ($suffix in @('ready', 'done')) {
            $marker = [IO.Path]::ChangeExtension($report, ".${phase}.${suffix}")
            if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }
        }
    }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
}
if (!$result.Passed) { exit 1 }
