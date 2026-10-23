function Stop-BootstrapCompiler {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)
    if ($Process.HasExited) { return }
    # Windows PowerShell 5.1 has no Process.Kill(entireProcessTree) overload.
    # The PID belongs to the one cmd.exe launched for this build, and /T ends
    # the compiler and linker children before temporary-file cleanup.
    & (Join-Path $env:SystemRoot 'System32/taskkill.exe') /T /F /PID $Process.Id | Out-Null
    if ($LASTEXITCODE -ne 0 -and -not $Process.HasExited) {
        $Process.Kill()
        [void]$Process.WaitForExit(10000)
        throw 'Could not stop the owned native compiler tree.'
    }
    if (-not $Process.WaitForExit(10000)) {
        $Process.Kill()
        if (-not $Process.WaitForExit(10000)) { throw 'Owned native compiler did not stop.' }
    }
}
