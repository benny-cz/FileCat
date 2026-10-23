function Remove-BootstrapTemporary {
    param(
        [Parameter(Mandatory)][string]$Temporary,
        [Parameter(Mandatory)][string]$TemporaryParent,
        [ValidateRange(1, 30)][int]$Attempts = 12,
        [ValidateRange(0, 2000)][int]$DelayMilliseconds = 250
    )

    $parent = [IO.Path]::GetFullPath($TemporaryParent)
    $path = [IO.Path]::GetFullPath($Temporary)
    $name = [IO.Path]::GetFileName($path)
    $expected = [IO.Path]::GetFullPath((Join-Path $parent $name))
    if ($name -cnotmatch '^FileCatBootstrap-[0-9a-f]{32}$' -or
        !$path.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe native compiler cleanup path.'
    }
    if (!(Test-Path -LiteralPath $path)) { return $true }
    if (((Get-Item -LiteralPath $path).Attributes -band
         [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Native compiler cleanup cannot follow a reparse point.'
    }

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        }
        catch {
            # A scanner can briefly hold a compiler object after cl.exe exits.
            # Other failures remain visible in the final warning below.
        }
        if (!(Test-Path -LiteralPath $path)) { return $true }
        if ($attempt -lt $Attempts) { Start-Sleep -Milliseconds $DelayMilliseconds }
    }
    return $false
}
