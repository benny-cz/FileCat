param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][string]$Source)
$ErrorActionPreference='Stop'
Add-Type -Path $Source
$verified=@()
for($method=0;$method -le 5;$method++){
 $folder=Join-Path $Root "method-$method"
 $expected=Get-Content -LiteralPath "$folder\members.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 $actual=[WindowsNativeRarOracle]::Read("$folder\members.rar")
 if($expected.Count -ne 14 -or $actual.Count -ne 14){throw 'Unexpected native member count.'}
 for($index=0;$index -lt 14;$index++){
  $e=$expected[$index];$a=$actual[$index]
  $nameMatches=[string]::Equals($a.Name,$e.name,[StringComparison]::Ordinal)
  if($e.directory){$nameMatches=$nameMatches -or [string]::Equals($a.Name,$e.name+'/',[StringComparison]::Ordinal)}
  if(-not $nameMatches -or $a.Directory -ne $e.directory -or $a.Length -ne $e.length -or $a.Mode -ne $e.mode -or $a.Seconds -ne $e.mtime_seconds -or $a.Nanoseconds -ne $e.mtime_nanos -or $a.Sha256 -ne $e.sha256){throw "Independent Windows native record differs: preset $method member $index"}
  $verified+=@{preset=$method;name=$a.Name;bytes=$a.Length;sha256=$a.Sha256;mode=$a.Mode;mtime_seconds=$a.Seconds;mtime_nanos=$a.Nanoseconds}
 }
}
@{reader=[WindowsNativeRarOracle]::Version();library='C:\Windows\System32\archiveint.dll';library_sha256=(Get-FileHash -LiteralPath C:\Windows\System32\archiveint.dll -Algorithm SHA256).Hash;verified=$verified;production_backend_qualified=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$Root\native-reader.json"
Write-Output "PASS: 84 independent Windows UTF-8 native record/payload/mode/nanosecond results."
