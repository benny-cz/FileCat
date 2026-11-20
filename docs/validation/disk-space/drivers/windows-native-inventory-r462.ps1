param([ValidateSet('Stage','Observe')][string]$Mode,[string]$ExpectedHash)
$ErrorActionPreference='Stop'
$base='C:\FileCatDsa20261104R2\desktop-r462-inventory';$app=Join-Path $base 'app';$state=Join-Path $base 'state.txt'
function Gate([int]$Owned=0){
 $others=@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue|Where-Object Id -ne $Owned)
 $runners=@(Get-Process -Name dotnet -ErrorAction SilentlyContinue)
 if($others.Count -or $runners.Count){throw 'BUSY: postpone this machine'}
}
function ReadReport([string]$Path){try{Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json}catch{return $null}}
if($Mode -eq 'Stage'){
 Gate
 $archive='C:\FileCatDsa20261104R2\desktop-r462-windows.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne $ExpectedHash){throw 'Archive mismatch'}
 if(Test-Path -LiteralPath $app){throw 'Use a fresh native memory stage'}
 New-Item -ItemType Directory -Path $app|Out-Null
 & tar.exe -xzf $archive -C $app;if($LASTEXITCODE){throw 'Extraction failed'}
 'STAGED'|Set-Content $state;exit 0
}
function VerifyInventory($Inv){
 $path=[IO.Path]::GetFullPath($Inv.file);$prefix=[IO.Path]::GetFullPath((Join-Path $app 'native-validation'))+'\'
 if(-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Inventory outside owned fixture'}
 $info=Get-Item -LiteralPath $path;if($info.Length -ne $Inv.Bytes -or $info.Length -ge 33554432){throw 'Inventory size mismatch'}
 $kinds=@('manifest','object','path-node','storage-fact','issue','coverage');$counts=[ordered]@{};foreach($kind in $kinds){$counts[$kind]=0}
 $objects=New-Object 'System.Collections.Generic.HashSet[string]';$paths=@{};$roots=New-Object 'System.Collections.Generic.HashSet[string]'
 $digest=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
 $inventoryStream=[IO.File]::OpenRead($path);$reader=$null;$trailer=$false;$stage=0;$files=0
 try{
  if($inventoryStream.Length -lt 1){throw 'Empty inventory'};$inventoryStream.Position=$inventoryStream.Length-1;if($inventoryStream.ReadByte() -ne 10){throw 'Missing final LF'};$inventoryStream.Position=0
  $utf8=New-Object Text.UTF8Encoding($false,$true);$reader=New-Object IO.StreamReader($inventoryStream,$utf8,$false,4096)
  while($null -ne ($line=$reader.ReadLine())){
   if($trailer -or $line.Length -gt 1048576){throw 'Trailing data or oversized line'};$row=$line|ConvertFrom-Json
   if($row.kind -eq 'trailer'){
    $actual=([BitConverter]::ToString($digest.GetHashAndReset())).Replace('-','').ToLowerInvariant();if($row.sha256 -ne $actual){throw 'Trailer hash mismatch'}
    foreach($kind in $kinds){if([UInt64]$row.counts.$kind -ne $counts[$kind]){throw 'Trailer counts mismatch'}};$trailer=$true;continue
   }
   $digest.AppendData($utf8.GetBytes($line+"`n"));$at=[Array]::IndexOf($kinds,$row.kind);if($at -lt $stage){throw 'Record order mismatch'};$stage=$at;$counts[$row.kind]++
   switch($row.kind){
    'manifest'{if($counts.manifest -ne 1 -or $row.schema -ne '1' -or $row.inventory -ne $Inv.inventoryId -or $row.session -ne $Inv.Identity.SessionId -or [UInt64]$row.historicalTaint -ne $Inv.Taint){throw 'Manifest mismatch'};foreach($root in $row.roots){$null=$roots.Add($root[0])}}
    'object'{if([UInt64]$row.id -ne $counts.object){throw 'Object ID mismatch'};$null=$objects.Add($row.id)}
    'path-node'{
     $id=[UInt64]$row.id;$parent=[UInt64]$row.parent
     if($id -ne $counts['path-node'] -or -not $roots.Contains($row.root) -or ($parent -ne 0 -and $paths[$parent] -ne 'Directory') -or ($row.object -and -not $objects.Contains($row.object))){throw 'Path ID/reference mismatch'}
     $paths[$id]=$row.entryKind;if($row.entryKind -eq 'File' -and $row.included){$files++}
    }
    'storage-fact'{if([UInt64]$row.id -ne $counts['storage-fact'] -or ($row.object -and -not $objects.Contains($row.object)) -or ($row.directory -and $paths[[UInt64]$row.directory] -ne 'Directory')){throw 'Fact reference mismatch'}}
    'issue'{if($row.node -and -not $paths.ContainsKey([UInt64]$row.node)){throw 'Issue reference mismatch'}}
    'coverage'{if($counts.coverage -ne 1 -or $row.qualification[0] -ne $Inv.CoverageDetails.EnumerationKnown -or [UInt64]$row.qualification[1] -ne $Inv.CoverageDetails.EligibleDirectories -or $row.qualification[2] -ne $Inv.CoverageDetails.StaleScopesComplete -or $row.qualification[3] -ne $Inv.CoverageDetails.HasUnlocatedStaleScopes){throw 'Qualified coverage mismatch'}}
    default{throw 'Unknown inventory record'}
   }
  }
 }finally{if($reader){$reader.Dispose()}else{$inventoryStream.Dispose()};$digest.Dispose()}
 if(-not $trailer -or $counts.manifest -ne 1 -or $counts.coverage -ne 1 -or $counts['path-node'] -ne $Inv.Paths -or $counts.object -ne $Inv.Objects -or $counts['storage-fact'] -ne $Inv.Facts -or $counts.issue -ne $Inv.SampledIssues -or (Get-FileHash -LiteralPath $path).Hash -ne $Inv.sha256){throw 'Whole inventory mismatch'}
 return [ordered]@{counts=$counts;regularPaths=$files;sha256=$Inv.sha256;originalDensePathIds=$true;relationshipsAndTrailerVerified=$true}
}
$record=[ordered]@{purpose='Actual Windows memory preparation/scope/retained view/query subset; no physical-input or compositor qualification';passed=$false}
$run=$null;$owned=0;$process=$null
try{
 $until=[DateTime]::UtcNow.AddSeconds(300)
 do{
  $candidates=@(Get-ChildItem -LiteralPath (Join-Path $app 'native-validation') -Directory -ErrorAction SilentlyContinue|ForEach-Object{
   $r=ReadReport (Join-Path $_.FullName 'report.json');if($r -and $r.state -ne 'closed'){[pscustomobject]@{Run=$_.FullName;Report=$r}}
  })
  if($candidates.Count -eq 1){
   $run=$candidates[0].Run;$report=$candidates[0].Report;$owned=[int]$report.processId;Gate $owned
   if($null -eq $process){$process=Get-Process -Id $owned;if($process.Path -ne (Join-Path $app 'FileCat.exe')){throw 'Owned image path mismatch'};$null=$process.Handle}
   if($process.HasExited){throw 'Owned native process exited before completion'}
   if($report.indexedProbeError){throw $report.indexedProbeError}
   if($report.indexedProbeFinished){break}
  }
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 if(-not $report.indexedProbeFinished){throw 'The same native memory workflow did not finish'}
 $probe=$report.indexedProbe;$inv=$report.inventoryProbe
 if(-not $report.indexedInventoryProbeRequested -or $report.sessions -ne 1 -or $probe.view -ne 'Table' -or $inv.Paths -le 4096){throw 'Native full inventory view mismatch'}
 if(-not $inv.fullDatasetNotPreview -or -not $inv.conflictPreserved -or -not $inv.acquisitionOwnerClosedBeforeSave){throw 'Native save ownership mismatch'}
 $record.wireVerification=VerifyInventory $inv
 if($record.wireVerification.regularPaths -ne $report.fixtureRegularPaths){throw 'Native regular-file count differs'}
 # Only the FileCat visual tree rendered by this owned process, never desktop/system capture.
 Copy-Item -LiteralPath (Join-Path $run 'native-inventory-window-render.png') -Destination (Join-Path $base 'native-inventory-window-render.png')
 $record.captureSha256=(Get-FileHash -LiteralPath (Join-Path $base 'native-inventory-window-render.png')).Hash.ToLowerInvariant()
 $record.processId=$owned;$record.finished=$report;$record.passed=$true
}catch{$record.error=$_|Out-String;throw}finally{
 if($run){[IO.File]::WriteAllText((Join-Path $run 'close.request'),'close only this owned memory fixture')}
 if($process){$record.ownedProcessExited=$process.WaitForExit(30000);$process.Dispose()}
 if($run){
  $closed=ReadReport (Join-Path $run 'report.json');$record.closed=$closed
  if($closed.state -ne 'closed' -or $closed.indexedProbeError -or -not $closed.sourceUnchanged -or $closed.deadlineClosed -or -not $record.ownedProcessExited){$record.passed=$false}
  if($closed.inventoryProbe){
   $inv=$closed.inventoryProbe
   try{$record.wireAfterClose=VerifyInventory $inv}catch{$record.passed=$false;$record.closeVerificationError=[string]$_.Exception.Message}
   $record.privateStateUnchangedAfterClose=((Get-FileHash -LiteralPath $inv.privateStateFile).Hash -eq $inv.privateStateSha256)
   if(-not $record.privateStateUnchangedAfterClose -or @(Get-ChildItem -LiteralPath $closed.indexedProbe.scratch -Force).Count){$record.passed=$false}
  }
 }
 $record|ConvertTo-Json -Depth 14|Set-Content (Join-Path $base 'native-inventory-result.json')
}
if(-not $record.passed){throw 'Native memory cleanup/source verification failed'}
Gate
'PASS'|Set-Content $state
