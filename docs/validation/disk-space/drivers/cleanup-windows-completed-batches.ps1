param([string[]]$Keep=@('batch-r955','batch-r959','batch-r963'),
 [ValidatePattern('^cleanup-r[0-9]+(?:-preview)?[.]json$')][string]$ReportName='cleanup-r964.json',
 [switch]$DryRun)
$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2'
if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){throw 'Another FileCat/test instance is active; cleanup postponed.'}
$resolvedRoot=[IO.Path]::GetFullPath((Get-Item -LiteralPath $root).FullName).TrimEnd('\')
if($resolvedRoot-cne$root){throw 'Unexpected validation root'}
$ancestor=Get-Item -LiteralPath $resolvedRoot
while($null-ne$ancestor){if($ancestor.Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'The validation root traverses a link'};$ancestor=$ancestor.Parent}
$removed=@();$skipped=@()
foreach($batch in (Get-ChildItem -LiteralPath $resolvedRoot -Directory)){
 if($batch.Name-notmatch'^batch-r[0-9]+[a-z]?$'-or$batch.Name-in$Keep){continue}
 if($batch.Attributes-band[IO.FileAttributes]::ReparsePoint){$skipped+=$batch.Name;continue}
 $state=Join-Path $batch.FullName 'state.txt'
 if(!(Test-Path -LiteralPath $state)-or([IO.File]::ReadAllText($state).Trim()-notmatch'^FINISHED exit=[0-9]+( phase=[a-z]+)?$')){$skipped+=$batch.Name;continue}
 foreach($leaf in @('payload','bundle.tgz')){
  $target=Join-Path $batch.FullName $leaf
  if(!(Test-Path -LiteralPath $target)){continue}
  $item=Get-Item -LiteralPath $target -Force
  $absolute=[IO.Path]::GetFullPath($item.FullName)
  if(!$absolute.StartsWith($resolvedRoot+'\',[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetDirectoryName($absolute)-cne$batch.FullName-or$item.Name-cne$leaf){throw 'Cleanup target escaped its verified batch'}
  if($item.Attributes-band[IO.FileAttributes]::ReparsePoint){$skipped+=$absolute;continue}
  if($item.PSIsContainer){
   $children=@(Get-ChildItem -LiteralPath $absolute -Force -Recurse)
   if(@($children|Where-Object{$_.Attributes-band[IO.FileAttributes]::ReparsePoint}).Count){$skipped+=$absolute;continue}
   $bytes=($children|Where-Object{!$_.PSIsContainer}|Measure-Object Length -Sum).Sum
   if(!$DryRun){Remove-Item -LiteralPath $absolute -Recurse -Force}
  }else{$bytes=$item.Length;if(!$DryRun){Remove-Item -LiteralPath $absolute -Force}}
  $removed+=@{path=$absolute;bytes=$bytes}
 }
}
$drive=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
[ordered]@{root=$resolvedRoot;dryRun=[bool]$DryRun;keep=$Keep;removed=$removed;skipped=$skipped;capacity=$drive.Size;available=$drive.FreeSpace}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $resolvedRoot $ReportName)
