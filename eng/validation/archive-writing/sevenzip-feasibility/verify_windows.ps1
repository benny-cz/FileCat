param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][string]$Source)
$ErrorActionPreference='Stop'
Add-Type -Path $Source
$verified=@()
for($method=0;$method -le 5;$method++){
 $folder=Join-Path $Root "codec-$method"
 $expected=Get-Content -LiteralPath "$folder\members.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 $actual=[WindowsNativeSevenZipOracle]::Read("$folder\members.7z")
 if($expected.Count -ne 7 -or $actual.Count -ne 7){throw 'Unexpected native member count.'}
 for($index=0;$index -lt 7;$index++){
  $e=$expected[$index];$a=$actual[$index]
  $nameMatches=[string]::Equals($a.Name,$e.name,[StringComparison]::Ordinal)
  if($e.directory){$nameMatches=$nameMatches -or [string]::Equals($a.Name,$e.name+'/',[StringComparison]::Ordinal)}
  if(-not $nameMatches -or $a.Directory -ne $e.directory -or $a.Length -ne $e.length -or $a.Mode -ne $e.mode -or $a.Seconds -ne $e.mtime_seconds -or $a.Nanoseconds -ne $e.mtime_nanos -or $a.Sha256 -ne $e.sha256){throw "Independent Windows native record differs: preset $method member $index"}
  if($a.CreatedSet -ne ($null -ne $e.ctime_seconds) -or $a.AccessedSet -ne ($null -ne $e.atime_seconds)){throw 'Independent optional timestamp presence differs.'}
  if($a.CreatedSet -and ($a.CreatedSeconds -ne $e.ctime_seconds -or $a.CreatedNanoseconds -ne $e.ctime_nanos)){throw 'Independent creation time differs.'}
  if($a.AccessedSet -and ($a.AccessedSeconds -ne $e.atime_seconds -or $a.AccessedNanoseconds -ne $e.atime_nanos)){throw 'Independent access time differs.'}
  $verified+=@{preset=$method;name=$a.Name;bytes=$a.Length;sha256=$a.Sha256;mode=$a.Mode;mtime_seconds=$a.Seconds;mtime_nanos=$a.Nanoseconds;ctime_seconds=$e.ctime_seconds;ctime_nanos=$e.ctime_nanos;atime_seconds=$e.atime_seconds;atime_nanos=$e.atime_nanos}
 }
}
@{reader=[WindowsNativeSevenZipOracle]::Version();library='C:\Windows\System32\archiveint.dll';library_sha256=(Get-FileHash -LiteralPath C:\Windows\System32\archiveint.dll -Algorithm SHA256).Hash;verified=$verified;production_backend_qualified=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$Root\native-reader.json"
Write-Output "PASS: 42 independent Windows UTF-8 native record/payload/mode/nanosecond results."
