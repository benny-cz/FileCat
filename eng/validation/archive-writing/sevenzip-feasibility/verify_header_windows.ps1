param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][string]$Source,[string]$PpmdLibrary)
$ErrorActionPreference='Stop'
Add-Type -Path $Source
if($PpmdLibrary){
 if(-not [IO.Path]::IsPathRooted($PpmdLibrary) -or -not(Test-Path -LiteralPath $PpmdLibrary)){throw 'Private PPMd oracle is absent.'}
 $text=[IO.File]::ReadAllText($Source).Replace('WindowsNativeSevenZipOracle','WindowsCurrentPpmdOracle').Replace('NativeSevenZipMember','NativeCurrentPpmdMember').Replace('C:\Windows\System32\archiveint.dll',$PpmdLibrary)
 Add-Type -TypeDefinition $text
}
$verified=@()
$folders=@(Get-ChildItem -LiteralPath $Root -Directory|Where-Object {$_.Name -like 'codec-*' -or $_.Name -like 'compressed-codec-*' -or $_.Name -like 'header-*' -or ($_.Name -like 'filter-*' -and $_.Name -notlike 'filter-Swap*' -and $_.Name -notlike 'filter-RiscV*')})
if($folders.Count -ne 27){throw 'Unexpected native fixture count.'}
foreach($item in $folders){
 $method=$item.Name;$folder=$item.FullName
 $expected=Get-Content -LiteralPath "$folder\members.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 if($method -eq 'compressed-codec-6'){$actual=[WindowsNativeSevenZipOracle]::ReadHeaders("$folder\members.7z")}
 elseif($method -eq 'codec-6' -and $PpmdLibrary){$actual=[WindowsCurrentPpmdOracle]::Read("$folder\members.7z")}
 else{$actual=[WindowsNativeSevenZipOracle]::Read("$folder\members.7z")}
 if($expected.Count -lt 1 -or $expected.Count -gt 2048 -or $actual.Count -ne $expected.Count){throw 'Unexpected native member count.'}
 for($index=0;$index -lt $expected.Count;$index++){
  $e=$expected[$index];$a=$actual[$index]
  $nameMatches=[string]::Equals($a.Name,$e.name,[StringComparison]::Ordinal)
  if($e.directory){$nameMatches=$nameMatches -or [string]::Equals($a.Name,$e.name+'/',[StringComparison]::Ordinal)}
  if(-not $nameMatches -or $a.Directory -ne $e.directory -or $a.Length -ne $e.length -or $a.Mode -ne $e.mode -or $a.Seconds -ne $e.mtime_seconds -or $a.Nanoseconds -ne $e.mtime_nanos -or ($a.PayloadChecked -and $a.Sha256 -ne $e.sha256)){throw "Independent Windows native record differs: preset $method member $index"}
  if($a.CreatedSet -ne ($null -ne $e.ctime_seconds) -or $a.AccessedSet -ne ($null -ne $e.atime_seconds)){throw 'Independent optional timestamp presence differs.'}
  if($a.CreatedSet -and ($a.CreatedSeconds -ne $e.ctime_seconds -or $a.CreatedNanoseconds -ne $e.ctime_nanos)){throw 'Independent creation time differs.'}
  if($a.AccessedSet -and ($a.AccessedSeconds -ne $e.atime_seconds -or $a.AccessedNanoseconds -ne $e.atime_nanos)){throw 'Independent access time differs.'}
  $verified+=@{payload_checked=$a.PayloadChecked;preset=$method;name=$a.Name;bytes=$a.Length;sha256=$a.Sha256;mode=$a.Mode;mtime_seconds=$a.Seconds;mtime_nanos=$a.Nanoseconds;ctime_seconds=$e.ctime_seconds;ctime_nanos=$e.ctime_nanos;atime_seconds=$e.atime_seconds;atime_nanos=$e.atime_nanos}
 }
}
$extra=$null
if($PpmdLibrary){$extra=@{reader=[WindowsCurrentPpmdOracle]::Version();library=$PpmdLibrary;library_sha256=(Get-FileHash -LiteralPath $PpmdLibrary -Algorithm SHA256).Hash;fixture='codec-6';system_3_8_9_failure_documented=$true;compressed_ppmd_metadata_only=$true}}
@{reader=[WindowsNativeSevenZipOracle]::Version();library='C:\Windows\System32\archiveint.dll';library_sha256=(Get-FileHash -LiteralPath C:\Windows\System32\archiveint.dll -Algorithm SHA256).Hash;ppmd_reader=$extra;secondary_reader_unavailable_filters=@('RiscV','Swap2','Swap4');verified=$verified;production_backend_qualified=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$Root\native-reader.json"
Write-Output "PASS: $($verified.Count) independent Windows UTF-8 native record/payload/mode/nanosecond results."
