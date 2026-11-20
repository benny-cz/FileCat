param([Parameter(Mandatory=$true)][string]$ManifestPath,[long]$TargetFreeBytes=8GB)
$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2'
$rootItem=Get-Item -LiteralPath $root
if($rootItem.FullName-cne$root-or($rootItem.Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Unexpected test root'}
if(@(Get-Process -Name 'FileCat*','dotnet' -ErrorAction SilentlyContinue).Count){throw 'BUSY: postpone cleanup while a tester is active'}
$records=Get-Content -LiteralPath $ManifestPath -Raw|ConvertFrom-Json
$result=[ordered]@{before=[long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace;removed=@();skipped=@()}
try{
 foreach($record in $records){
  if([long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace-ge$TargetFreeBytes){break}
  if($result.removed.Count-ge32){break}
  if(@(Get-Process -Name 'FileCat*','dotnet' -ErrorAction SilentlyContinue).Count){throw 'BUSY: stopped before another cleanup'}
  if($record.revision-cnotmatch'^r10[0-9][0-9]$'-or[int]$record.revision.Substring(1)-gt1094){throw 'Only completed old test revisions are eligible'}
  $target=[IO.Path]::GetFullPath((Join-Path $root ('batch-'+$record.revision)))
  if(!$target.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetDirectoryName($target)-cne$root){throw 'Cleanup target escaped the named test root'}
  if(!(Test-Path -LiteralPath $target)){continue}
  $item=Get-Item -LiteralPath $target
  if($item.Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'A retired payload is a reparse point'}
  $runner=Join-Path $target 'run.ps1'
  if(!(Test-Path -LiteralPath $runner)-or(Get-FileHash -LiteralPath $runner).Hash-cne$record.sha256){$result.skipped+=@($record.revision);continue}
  if(@(Get-ChildItem -LiteralPath $target -Recurse -Force|Where-Object{$_.Attributes-band[IO.FileAttributes]::ReparsePoint}).Count){throw 'A retired payload contains a reparse point'}
  Remove-Item -LiteralPath $target -Recurse -Force
  $result.removed+=@($record.revision)
 }
}finally{
 $result.after=[long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace
 $result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $root 'retired-payload-cleanup-r1101.json')
}
