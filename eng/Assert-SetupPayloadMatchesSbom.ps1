<#
Refuses an installer payload whose files differ from the setup SBOM eng/publish.ps1 wrote for it: the installer must
package exactly the bytes its SBOM describes. The SBOM files of Inno Setup's own package (the license text the installer
adds to the installed tree) are the only ones the payload folder does not hold.
#>
param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$Sbom
)
$ErrorActionPreference = 'Stop'
$document = Get-Content -LiteralPath $Sbom -Raw | ConvertFrom-Json
$innoFiles = @($document.packages | Where-Object { $_.SPDXID -ceq 'SPDXRef-Inno-Setup' } | ForEach-Object { $_.hasFiles })
$expected = @{}
foreach ($file in $document.files) {
    if ($innoFiles -contains $file.SPDXID) { continue }
    if (-not $file.fileName.StartsWith('./')) { throw "The setup SBOM names a file outside its payload: $($file.fileName)" }
    $sha = @($file.checksums | Where-Object { $_.algorithm -ceq 'SHA256' })
    if ($sha.Count -ne 1) { throw "The setup SBOM has no single SHA-256 for $($file.fileName)" }
    $expected[$file.fileName.Substring(2)] = $sha[0].checksumValue
}
$root = [IO.Path]::GetFullPath($Payload)
$actual = @{}
foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
    $relative = [IO.Path]::GetRelativePath($root, $item.FullName).Replace('\', '/')
    $actual[$relative] = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$missing = @($expected.Keys | Where-Object { -not $actual.ContainsKey($_) } | Sort-Object)
$extra = @($actual.Keys | Where-Object { -not $expected.ContainsKey($_) } | Sort-Object)
$changed = @($expected.Keys | Where-Object { $actual.ContainsKey($_) -and $actual[$_] -cne $expected[$_] } | Sort-Object)
if ($missing.Count -or $extra.Count -or $changed.Count) {
    $first = @($missing + $extra + $changed)[0]
    throw "The installer payload differs from its setup SBOM: $($missing.Count) missing, $($extra.Count) extra, $($changed.Count) changed (first: $first)."
}
[pscustomobject]@{ Files = $actual.Count; Sbom = (Resolve-Path -LiteralPath $Sbom).Path }
