<# Validates the complete frozen compiler payload before it contributes installer bytes. #>
param([Parameter(Mandatory)][string]$ToolRoot)
$ErrorActionPreference = 'Stop'
if (-not ([IO.Path]::IsPathRooted($ToolRoot) -and [IO.Path]::GetPathRoot($ToolRoot).Length -ge 3)) { throw 'Compiler root must be absolute.' }
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'toolchains/inno-setup.json') -Raw | ConvertFrom-Json
foreach ($file in $pin.Files) {
    $path = Join-Path $ToolRoot $file.Path
    $item = Get-Item -LiteralPath $path
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        $item.Length -ne $file.Bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.SHA256) {
        throw "Frozen Inno Setup input differs: $($file.Path)"
    }
}
[pscustomobject]@{
    Executable = Join-Path $ToolRoot 'ISCC.exe'
    Version = $pin.Version
    FilesVerified = $pin.Files.Count
    ManifestSHA256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'toolchains/inno-setup.json') -Algorithm SHA256).Hash.ToLowerInvariant()
}
