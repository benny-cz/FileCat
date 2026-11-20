$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath('C:\FileCatDsa20261104R2')
$prefix=$root.TrimEnd('\')+'\'
# This literal list was selected from the completed-batch inventory. No current baseline is admitted.
$allowed=@('batch-r525','batch-r535','batch-r545','batch-r549','batch-r557','batch-r569','batch-r585','batch-r589','batch-r598','batch-r610','batch-r614','batch-r626','batch-r632','batch-r636','batch-r640','batch-r644','batch-r654','batch-r666','batch-r672','batch-r675','batch-r680','batch-r685','batch-r689','batch-r706','batch-r710','batch-r714','batch-r723','batch-r727','batch-r739','batch-r743','batch-r747','batch-r753')
$before=[long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace
$removed=@()
function AssertIdle {
 if(@(Get-Process -Name 'FileCat*','dotnet' -ErrorAction SilentlyContinue).Count){throw 'Another tester is active; cache retirement postponed'}
}
function AssertOwned([string]$path) {
 $resolved=[IO.Path]::GetFullPath((Get-Item -LiteralPath $path -Force).FullName)
 if(!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetDirectoryName($resolved)-cne$root){throw 'Cache path escaped the explicitly named task root'}
 $pending=New-Object 'System.Collections.Generic.Stack[string]';$pending.Push($resolved);$count=0
 while($pending.Count){
  $next=$pending.Pop();if(++$count-gt100000){throw 'Cache tree inspection limit'}
  $item=Get-Item -LiteralPath $next -Force
  if(($item.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0){throw 'A cache entry is a reparse point; recursive removal refused'}
  if($item.PSIsContainer){foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($next)){$pending.Push($entry)}}
 }
 return $resolved
}
try {
 AssertIdle
 foreach($leaf in $allowed){
  if($leaf-in@('batch-r1071','batch-r1072')-or$leaf-cnotmatch'^batch-r[0-9]{3}$'){throw 'Current baselines and unlisted names cannot be removed'}
  if([long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace-ge8GB){break}
  $candidate=Join-Path $root $leaf
  if(!(Test-Path -LiteralPath $candidate -PathType Container)){continue}
  $state=Join-Path $candidate 'state.txt';$runner=Join-Path $candidate 'run.ps1'
  if(!(Test-Path -LiteralPath $runner -PathType Leaf)-or!(Test-Path -LiteralPath $state -PathType Leaf)){throw 'An enumerated candidate no longer has task ownership evidence'}
  if(([IO.File]::ReadAllText($state)).Trim()-notmatch'^FINISHED exit=[01]( phase=[a-z]+)?$'){throw 'An enumerated candidate is no longer completed'}
  $target=AssertOwned $candidate;AssertIdle
  Remove-Item -LiteralPath $target -Recurse -Force
  $removed+=$target
 }
 $empty=Join-Path $root 'batch-r1075'
 if(Test-Path -LiteralPath $empty){
  $target=AssertOwned $empty;$entries=@(Get-ChildItem -LiteralPath $target -Force)
  if($entries.Count-ne1-or$entries[0].Name-cne'space-preflight.json'-or$entries[0].PSIsContainer){throw 'The unstarted incoming directory is not diagnostic-only'}
  AssertIdle;Remove-Item -LiteralPath $entries[0].FullName;Remove-Item -LiteralPath $target
 }
} finally {
 $after=[long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace
 [ordered]@{observedUtc=[DateTime]::UtcNow.ToString('o');root=$root;beforeBytes=$before;afterBytes=$after;allowed=$allowed;removed=$removed;preservedBaselines=@('batch-r1071','batch-r1072')}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $root 'cache-retire-r1075b.json')
}
