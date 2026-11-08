param([Parameter(Mandatory=$true)][string]$ArchiveSha256,[string]$Counts='1000,1000000,10000000',[string]$Shapes='wide,deep')
$ErrorActionPreference='Stop';$base=$PSScriptRoot;$app=Join-Path $base 'app'
function Json([string]$name,$value){$value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $base $name)}
function WaitIdle([string]$case){
 $deadline=[DateTime]::UtcNow.AddMinutes(30)
 while(@(Get-Process -Name 'FileCat*','dotnet' -ErrorAction SilentlyContinue).Count){
  Json 'state.json' @{state='waiting-for-idle';case=$case}
  if([DateTime]::UtcNow-ge$deadline){throw 'Shared guest stayed busy; no additional test started'}
  Start-Sleep -Seconds 30
 }
}
if((Get-FileHash -LiteralPath (Join-Path $base 'app.tgz') -Algorithm SHA256).Hash.ToLowerInvariant()-cne$ArchiveSha256){throw 'Archive mismatch'}
if(Test-Path -LiteralPath $app){throw 'Fresh extraction required'}
WaitIdle 'integrity';New-Item -ItemType Directory -Path $app|Out-Null
& tar.exe -xzf (Join-Path $base 'app.tgz') -C $app;if($LASTEXITCODE){throw 'Extraction failed'}
$inputs=Get-Content -LiteralPath (Join-Path $app 'inputs.json') -Raw|ConvertFrom-Json
foreach($item in $inputs){
 if([IO.Path]::GetFileName($item.name)-cne$item.name){throw 'Invalid input filename'}
 if((Get-FileHash -LiteralPath (Join-Path $app $item.name) -Algorithm SHA256).Hash.ToLowerInvariant()-cne$item.sha256){throw 'Input mismatch'}
}
$result=[ordered]@{purpose='Production CSV store/export scale, not filesystem source traversal or full-tool qualification';archiveSha256=$ArchiveSha256;inputs=$inputs;runs=@()}
try{
 $selectedCounts=@($Counts-split','|ForEach-Object{[int]$_});$selectedShapes=@($Shapes-split',')
 if(!$selectedCounts.Count-or@($selectedCounts|Sort-Object -Unique).Count-ne$selectedCounts.Count-or@($selectedCounts|Where-Object{$_-notin1000,1000000,10000000}).Count){throw 'Invalid selected scale counts'}
 if(!$selectedShapes.Count-or@($selectedShapes|Sort-Object -Unique).Count-ne$selectedShapes.Count-or@($selectedShapes|Where-Object{$_-notin'wide','deep'}).Count){throw 'Invalid selected scale shapes'}
 foreach($count in $selectedCounts){
  foreach($shape in $selectedShapes){
   $case=[string]$count+'-'+$shape;WaitIdle $case;$parent=Join-Path $base ('records-'+$case)
   if(Test-Path -LiteralPath $parent){throw 'A fresh generated scale parent is required'}
   New-Item -ItemType Directory -Path $parent|Out-Null
   $record=[ordered]@{records=$count;shape=$shape;warmup=$count-eq1000}
   Json 'state.json' @{state='running';case=$case;runnerPid=$PID}
   try{
    Set-Location -LiteralPath $app
    $ErrorActionPreference='Continue'
    & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $app 'FileCat.CoreCrashHarness.dll') --analysis-csv-scale $count $shape $parent 1> (Join-Path $base ($case+'.out')) 2> (Join-Path $base ($case+'.err'))
    $code=$LASTEXITCODE;$ErrorActionPreference='Stop';$record.exitCode=$code;$record.ownedProcessExited=$true
    if($code-ne0){throw ('Native scale child exited '+$code+'; inspect preserved stderr')}
    $lines=Get-Content -LiteralPath (Join-Path $base ($case+'.out'));$measurement=$lines[-1]|ConvertFrom-Json;$record.measurement=$measurement
    if(!$measurement.passed-or$measurement.records-ne$count-or$measurement.shape-cne$shape){throw 'Native scale child failed; inspect preserved output'}
    if($measurement.limitRefusal){
     if($count-ne10000000-or$measurement.fullCsvExportQualified-or!$measurement.retainedSourceUnchanged-or!$measurement.destinationWasNeverOpened-or$measurement.remainingQuotaBytes-ne0-or$measurement.remainingProfileQuotaBytes-ne0){throw 'Scratch refusal did not preserve the source and release resources'}
    }else{
     if(@($measurement.reports).Count-ne2){throw 'Missing full CSV outputs'}
     foreach($report in $measurement.reports){if(!$report.verifiedEveryDeclaredRow-or$report.lines-ne($count+2)){throw 'CSV completeness failed'}}
    }
    Write-Output ('PASS '+$case+' actual child exited')
   }catch{$record.error=$_.Exception.Message;throw}
   finally{$result.runs+=@($record);Json 'result.json' $result}
  }
 }
 $result.passed=$true;Json 'result.json' $result;Json 'state.json' @{state='finished';passed=$true}
}catch{Json 'state.json' @{state='finished';passed=$false;error=$_.Exception.Message};throw}
