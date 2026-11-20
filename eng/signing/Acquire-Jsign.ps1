<#
.SYNOPSIS
  Acquires the pinned Jsign release (eng/toolchains/jsign.json) into a new tool directory and finds Java to run it.
.DESCRIPTION
  Jsign signs Windows PE files in place, with a local PKCS#12 key (dry runs) or SSL.com eSigner's cloud key, to which
  it sends only digests. JarFile supplies an offline copy of the same pinned jar. Returns the verified jar and Java.
#>
param(
    [Parameter(Mandatory)][string]$ToolRoot,
    [string]$JarFile
)
$ErrorActionPreference = 'Stop'
if (-not ([IO.Path]::IsPathRooted($ToolRoot) -and [IO.Path]::GetPathRoot($ToolRoot).Length -ge 3)) { throw 'ToolRoot must be an absolute path.' }
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)
if (Test-Path -LiteralPath $ToolRoot) { throw "ToolRoot already exists: $ToolRoot" }
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../toolchains/jsign.json') -Raw | ConvertFrom-Json
[void][IO.Directory]::CreateDirectory($ToolRoot)
$jar = Join-Path $ToolRoot "jsign-$($pin.Version).jar"
if ($JarFile) { Copy-Item -LiteralPath $JarFile -Destination $jar } else { Invoke-WebRequest -Uri $pin.JarURL -OutFile $jar }
if ((Get-Item -LiteralPath $jar).Length -ne $pin.JarBytes -or
    (Get-FileHash -LiteralPath $jar -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.JarSHA256) {
    throw 'The Jsign jar differs from the pinned release.'
}

# An explicit FILECAT_JAVA wins; then the JDKs GitHub's Windows images publish; then JAVA_HOME; then PATH.
$candidates = @($env:FILECAT_JAVA)
foreach ($javaHome in @($env:JAVA_HOME_21_X64, $env:JAVA_HOME_17_X64, $env:JAVA_HOME)) {
    if ($javaHome) { $candidates += Join-Path $javaHome 'bin/java.exe' }
}
$onPath = Get-Command java -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($onPath) { $candidates += $onPath.Source }
$java = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
if (-not $java) { throw 'No Java runtime found for Jsign (set FILECAT_JAVA, JAVA_HOME or PATH).' }
$versionOutput = @(& $java -version 2>&1)
if ($LASTEXITCODE -ne 0) { throw "Java at $java does not run." }
$javaVersion = "$($versionOutput[0])".Trim()

[pscustomobject]@{
    Jar = $jar
    JarSHA256 = $pin.JarSHA256
    Version = $pin.Version
    Java = [IO.Path]::GetFullPath($java)
    JavaVersion = $javaVersion
}
