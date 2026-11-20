$ErrorActionPreference='Stop'
$base='C:\FileCatDsa20261104R2\desktop-r397-indexed'
$r=Get-Content -LiteralPath (Join-Path $base 'native-indexed-result.json') -Raw|ConvertFrom-Json
if(-not $r.passed -or -not $r.ownedProcessExited -or -not $r.closed.sourceUnchanged -or $r.closed.deadlineClosed){throw 'Native receipt not qualified'}
$run=[IO.Path]::GetFullPath($r.closed.run)
if(-not $run.StartsWith((Join-Path $base 'app\native-validation')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Generated run escaped stage'}
$profile=([Guid]$r.closed.indexedProbe.profile).ToString('N')
$tokens=@(Get-ChildItem -LiteralPath (Join-Path $run 'data') -File -Recurse|Where-Object Name -match '^capacity-[0-9]{2}[.]state$'|ForEach-Object{
 $slot=[int]$_.Name.Substring(9,2);if($slot -ge 32){throw 'Invalid token slot'}
 $expected="FileCat profile capacity v1`n"+$profile+"`n"+$slot.ToString('D2',[Globalization.CultureInfo]::InvariantCulture)+"`n"
 $actual=[IO.File]::ReadAllBytes($_.FullName)
 if($actual.Length -gt 128 -or [Text.Encoding]::ASCII.GetString($actual) -cne $expected){throw 'Invalid immutable token'}
 if((Get-FileHash -LiteralPath (Join-Path $_.DirectoryName 'private-state.json')).Hash -ne $r.closed.indexedProbe.privateStateSha256){throw 'Profile state changed'}
 [ordered]@{slot=$slot;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();bytes=$actual.Length}
})
if($tokens.Count -lt 1 -or $tokens.Count -gt 32){throw 'File-backed capacity was not observed'}
[ordered]@{passed=$true;purpose='Read-only native indexed workflow file-capacity integration check after owned exit';tokenCount=$tokens.Count;tokens=$tokens;profileStateMatchesNativeReceipt=$true}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $base 'file-capacity-post-r398.json')
