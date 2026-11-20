$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath('C:\FileCatDsa20261104R2');$prefix=$root.TrimEnd('\')+'\'
$allowed=@('render-r100','render-r248','render-r256','render-r259','render-r264','render-r320','render-r330','render-r346','render-r351','render-r359','render-r369','render-r38','render-r394','render-r40','render-r403','render-r40b','render-r411','render-r419','render-r42','render-r423','render-r432','render-r438','render-r44','render-r441','render-r45','render-r450','render-r453','render-r46','render-r461','render-r47','render-r472','render-r47k','render-r48','render-r480','render-r491','render-r501','render-r53','render-r57','render-r59','render-r61','render-r68','render-r70','render-r72','render-r78','render-r84','render-r86','render-r89','render-r94','render-r98')
$removed=@();$before=[long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace
function AssertIdle {if(@(Get-Process -Name 'FileCat*','dotnet' -ErrorAction SilentlyContinue).Count){throw 'Another tester is active; cache retirement postponed'}}
function CheckTree([string]$path){
 $resolved=[IO.Path]::GetFullPath((Get-Item -LiteralPath $path -Force).FullName)
 if(!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetDirectoryName($resolved)-cne$root){throw 'The final target is outside this task root'}
 if([IO.Path]::GetFileName($resolved)-in@('batch-r1071','batch-r1072')){throw 'Current baselines are excluded'}
 $stack=New-Object 'System.Collections.Generic.Stack[string]';$stack.Push($resolved);$count=0
 while($stack.Count){
  $item=Get-Item -LiteralPath $stack.Pop() -Force
  if(++$count-gt100000-or($item.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0){throw 'Tree limit or reparse point; recursive removal refused'}
  if($item.PSIsContainer){foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($item.FullName)){$stack.Push($entry)}}
 }
 return $resolved
}
function Baselines {
 @('batch-r1071','batch-r1072')|ForEach-Object{
  $file=Join-Path (Join-Path $root $_) 'payload\render\FileCat.dll'
  [ordered]@{name=$_;productSha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash}
 }
}
$baselineBefore=@(Baselines)
try {
 AssertIdle
 foreach($leaf in $allowed){
  if($leaf-cnotmatch'^render-r[0-9]{1,3}[a-z]?$'){throw 'Only the literal old renderer list is admitted'}
  if([long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace-ge8GB){break}
  $candidate=Join-Path $root $leaf;if(!(Test-Path -LiteralPath $candidate -PathType Container)){continue}
  if(!(Test-Path -LiteralPath (Join-Path $candidate 'FileCat.Rendering.Tests.dll') -PathType Leaf)){continue}
  $target=CheckTree $candidate;AssertIdle;Remove-Item -LiteralPath $target -Recurse -Force;$removed+=$target
 }
 $empty=Join-Path $root 'batch-r1075'
 if(Test-Path -LiteralPath $empty){
  $target=CheckTree $empty;$items=@(Get-ChildItem -LiteralPath $target -Force)
  if($items.Count-ne1-or$items[0].Name-cne'space-preflight.json'-or$items[0].PSIsContainer){throw 'Incoming directory is no longer diagnostic-only'}
  AssertIdle;Remove-Item -LiteralPath $items[0].FullName;Remove-Item -LiteralPath $target
 }
} finally {
 $baselineAfter=@(Baselines)
 if(($baselineBefore|ConvertTo-Json -Compress)-cne($baselineAfter|ConvertTo-Json -Compress)){throw 'Preserved baseline hashes changed'}
 [ordered]@{observedUtc=[DateTime]::UtcNow.ToString('o');root=$root;beforeBytes=$before;afterBytes=[long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace;removed=$removed;baselineBefore=$baselineBefore;baselineAfter=$baselineAfter}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $root 'render-cache-retire-r1075c.json')
}
