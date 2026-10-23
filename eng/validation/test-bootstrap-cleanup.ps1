$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'The compiler-file lock regression requires Windows.'
}
. (Join-Path $PSScriptRoot 'Remove-BootstrapTemporary.ps1')
. (Join-Path $PSScriptRoot 'Stop-BootstrapCompiler.ps1')

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$parent = [IO.Path]::GetFullPath((Join-Path $repository 'tests/FileCat.NativeAcceptance/obj/bootstrap-cleanup-test'))
if (!$parent.StartsWith(($repository.TrimEnd('\') + '\'),
                       [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The test fixture left the repository.'
}
[void][IO.Directory]::CreateDirectory($parent)
$temporary = Join-Path $parent ('FileCatBootstrap-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporary)
$object = Join-Path $temporary 'NativeBootstrap.obj'
[IO.File]::WriteAllBytes($object, [byte[]](1, 2, 3))
$held = $null
try {
    $held = [IO.File]::Open($object, [IO.FileMode]::Open,
                            [IO.FileAccess]::Read, [IO.FileShare]::None)
    if (Remove-BootstrapTemporary -Temporary $temporary -TemporaryParent $parent -Attempts 2 -DelayMilliseconds 10) {
        throw 'A locked compiler object unexpectedly disappeared.'
    }
    if (!(Test-Path -LiteralPath $object -PathType Leaf)) {
        throw 'The locked compiler object was lost.'
    }
    $held.Dispose(); $held = $null
    if (!(Remove-BootstrapTemporary -Temporary $temporary -TemporaryParent $parent -Attempts 3 -DelayMilliseconds 10) -or
        (Test-Path -LiteralPath $temporary)) {
        throw 'The compiler temporary directory was not removed after the lock cleared.'
    }
    $rejected = $false
    try {
        Remove-BootstrapTemporary -Temporary $parent -TemporaryParent $repository | Out-Null
    }
    catch { $rejected = $true }
    if (!$rejected -or !(Test-Path -LiteralPath $parent -PathType Container)) {
        throw 'The cleanup guard accepted a directory outside its exact temporary child.'
    }
    $owned = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/cmd.exe') -ArgumentList '/d /c ping -n 30 127.0.0.1 >nul' -PassThru -WindowStyle Hidden
    try {
        if ($owned.HasExited) { throw 'The owned compiler substitute exited before the timeout test.' }
        Stop-BootstrapCompiler -Process $owned
        if (-not $owned.HasExited) { throw 'The timed-out owned compiler substitute survived.' }
    }
    finally {
        if (!$owned.HasExited) { $owned.Kill(); [void]$owned.WaitForExit(10000) }
        $owned.Dispose()
    }
    Write-Host 'Native bootstrap cleanup: lock tolerated, later cleanup succeeded, unsafe path rejected, timed-out process stopped.'
}
finally {
    if ($held) { $held.Dispose() }
    if (Test-Path -LiteralPath $temporary) {
        if (!(Remove-BootstrapTemporary -Temporary $temporary -TemporaryParent $parent -Attempts 3 -DelayMilliseconds 10)) {
            throw "Owned cleanup test fixture remains: $temporary"
        }
    }
}
