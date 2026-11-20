param([string]$ExpectedHash)
$ErrorActionPreference='Stop';$base='C:\FileCatDsa20261104R2\scratch-scale-r117';$app=Join-Path $base 'app';$archive='C:\FileCatDsa20261104R2\scratch-scale-r117.tgz'
function Gate{
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){throw 'BUSY: postpone this machine'}
}
$result=[ordered]@{purpose='Native indexed scratch primitive; not full analysis scale qualification';archiveSha256=$ExpectedHash;platform=[Environment]::OSVersion.VersionString;runs=@()}
try{
 Gate
 if((Get-FileHash -LiteralPath $archive).Hash -ne $ExpectedHash){throw 'Archive mismatch'}
 if(Test-Path -LiteralPath $app){throw 'Use a fresh scale stage'}
 New-Item -ItemType Directory -Path $app|Out-Null
 tar.exe -xzf $archive -C $app;if($LASTEXITCODE){throw 'Extraction failed'}
 $result.hashes=[ordered]@{};foreach($name in @('FileCat.Core.dll','FileCat.CoreCrashHarness.dll','FileCat.Platform.Windows.dll')){$result.hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $app $name)).Hash.ToLowerInvariant()}
 foreach($count in @(1000000,10000000)){
  Gate
  $parent=Join-Path $base ('records-'+$count);New-Item -ItemType Directory -Path $parent|Out-Null
  $record=[ordered]@{records=$count};$process=$null
  try{
   $out=Join-Path $base ($count.ToString()+'.out');$err=Join-Path $base ($count.ToString()+'.err')
   $start=[Diagnostics.ProcessStartInfo]::new()
   $start.FileName='C:\Program Files\dotnet\dotnet.exe';$start.WorkingDirectory=$app
   $start.Arguments=(Join-Path $app 'FileCat.CoreCrashHarness.dll')+' --analysis-scratch-vector '+$count+' '+$parent
   $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
   $process=[Diagnostics.Process]::Start($start);$nativeProcessHandle=$process.Handle
   $outputTask=$process.StandardOutput.ReadToEndAsync();$errorTask=$process.StandardError.ReadToEndAsync()
   $record.processId=$process.Id;@{state='running';records=$count;ownedPid=$process.Id}|ConvertTo-Json|Set-Content (Join-Path $base 'state.json')
   if(-not $process.WaitForExit(900000)){$process.Kill();$process.WaitForExit(15000)|Out-Null;$record.deadlineClosed=$true;throw 'Storage scale deadline exceeded'}
   $record.exitCode=$process.ExitCode;$record.ownedProcessExited=$process.HasExited
   if($null -eq $record.exitCode){throw 'No retained child exit code'}
   [IO.File]::WriteAllText($out,$outputTask.GetAwaiter().GetResult());[IO.File]::WriteAllText($err,$errorTask.GetAwaiter().GetResult())
   $record.measurement=Get-Content -LiteralPath $out -Raw|ConvertFrom-Json
   if($record.exitCode -ne 0 -or -not $record.measurement.passed -or $record.measurement.records -ne $count -or -not $record.measurement.verifiedEveryRecord -or $record.measurement.remainingQuotaBytes -ne 0){throw ('Storage scale failed '+(Get-Content -LiteralPath $err -Raw))}
  }catch{$record.error=$_|Out-String;throw}finally{
   if($process -and -not $process.HasExited){$process.Kill();$process.WaitForExit(15000)|Out-Null;$record.forcedTermination=$true}
   $result.runs+=,$record;$result|ConvertTo-Json -Depth 15|Set-Content (Join-Path $base 'result.json')
  }
 }
 Gate
 $result.passed=$true;$result|ConvertTo-Json -Depth 15|Set-Content (Join-Path $base 'result.json')
 @{state='finished';passed=$true}|ConvertTo-Json|Set-Content (Join-Path $base 'state.json')
}catch{
 $result.error=$_|Out-String
 if(Test-Path -LiteralPath $base){$result|ConvertTo-Json -Depth 15|Set-Content (Join-Path $base 'result.json');@{state='failed';error=$result.error}|ConvertTo-Json|Set-Content (Join-Path $base 'state.json')}
 throw
}
