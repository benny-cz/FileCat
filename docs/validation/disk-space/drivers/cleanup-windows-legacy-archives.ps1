param([switch]$DryRun, [ValidatePattern('^legacy-archives-r[0-9]+(?:-preview)?[.]json$')][string]$ReportName)
$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2'
if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){throw 'Testing is active; cleanup postponed'}
$directory=Get-Item -LiteralPath $root
if([IO.Path]::GetFullPath($directory.FullName)-cne$root){throw 'Unexpected task root'}
$ancestor=$directory
while($null-ne$ancestor){if($ancestor.Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'Linked task root refused'};$ancestor=$ancestor.Parent}
$removed=@()
foreach($file in (Get-ChildItem -LiteralPath $root -File -Filter '*.tgz')){
 if($file.Name-cnotmatch'^(render|core(?:-all)?|native(?:-all)?)-r([0-9]+)[a-z]?[.]tgz$'-or[int]$Matches[2]-gt516){continue}
 $absolute=[IO.Path]::GetFullPath($file.FullName)
 if([IO.Path]::GetDirectoryName($absolute)-cne$root-or($file.Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Archive target escaped the verified task root'}
 $removed+=@{path=$absolute;bytes=$file.Length}
 if(!$DryRun){Remove-Item -LiteralPath $absolute -Force}
}
$drive=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
if(!$ReportName){$ReportName='legacy-archives-r1014'+$(if($DryRun){'-preview'}else{''})+'.json'}
[ordered]@{dryRun=[bool]$DryRun;root=$root;removed=$removed;available=[long]$drive.FreeSpace;capacity=[long]$drive.Size}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $root $ReportName)
