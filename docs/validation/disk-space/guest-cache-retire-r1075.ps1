$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath('C:\FileCatDsa20261104R2')
$prefix=$root.TrimEnd('\')+'\'
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
  if(($item.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0){throw 'A cache entry is a reparse point; no recursive removal is permitted'}
  if($item.PSIsContainer){foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($next)){$pending.Push($entry)}}
 }
 return $resolved
}
AssertIdle
foreach($directory in Get-ChildItem -LiteralPath $root -Directory){
 if($directory.Name-cnotmatch'^batch-r(10[0-6][0-9])[a-z]?$'-or[int]$Matches[1]-gt1068){continue}
 $state=Join-Path $directory.FullName 'state.txt';$runner=Join-Path $directory.FullName 'run.ps1'
 if(!(Test-Path -LiteralPath $runner -PathType Leaf)-or!(Test-Path -LiteralPath $state -PathType Leaf)){continue}
 if(([IO.File]::ReadAllText($state)).Trim()-notmatch'^FINISHED exit=[01]( phase=[a-z]+)?$'){continue}
 $target=AssertOwned $directory.FullName;AssertIdle
 Remove-Item -LiteralPath $target -Recurse -Force
 $removed+=$target
}
$empty=Join-Path $root 'batch-r1075'
if(Test-Path -LiteralPath $empty){
 $target=AssertOwned $empty;$entries=@(Get-ChildItem -LiteralPath $target -Force)
 if($entries.Count-ne1-or$entries[0].Name-cne'space-preflight.json'-or$entries[0].PSIsContainer){throw 'The unstarted incoming directory is not diagnostic-only'}
 AssertIdle;Remove-Item -LiteralPath $entries[0].FullName;Remove-Item -LiteralPath $target
}
$after=[long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace
[ordered]@{observedUtc=[DateTime]::UtcNow.ToString('o');root=$root;beforeBytes=$before;afterBytes=$after;removed=$removed;preservedBaselines=@('batch-r1071','batch-r1072')}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $root 'cache-retire-r1075.json')
