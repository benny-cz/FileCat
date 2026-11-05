param([Parameter(Mandatory=$true)][string]$ExpectedHash,[switch]$Stage)
$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2\desktop-r62-input';$app=Join-Path $root 'app';$state=Join-Path $root 'state.txt'
New-Item -ItemType Directory -Force $root|Out-Null
if($Stage){
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.CommandLine -match '(?i)FileCat.*([.]dll|[.]csproj|[.]sln)'}).Count){'BUSY'|Set-Content $state;exit 75}
 $archive='C:\FileCatDsa20261104R2\desktop-r62-windows.tgz'
 if((Get-FileHash $archive).Hash -ine $ExpectedHash){throw 'Archive mismatch'}
 if(Test-Path (Join-Path $app 'native-validation')){throw 'Use a fresh run root'}
 New-Item -ItemType Directory -Force $app|Out-Null
 & tar.exe -xzf $archive -C $app
 if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
 'STAGED'|Set-Content $state;exit 0
}
$run=$null;$ownedPid=0;$verified=$false
try{
 $until=[DateTime]::UtcNow.AddSeconds(20)
 while([DateTime]::UtcNow -lt $until){$runs=@(Get-ChildItem (Join-Path $app 'native-validation') -Directory -Filter 'dsa-*' -ErrorAction SilentlyContinue);if($runs.Count -eq 1 -and (Test-Path (Join-Path $runs[0].FullName 'report.json'))){$run=$runs[0].FullName;break};Start-Sleep -Milliseconds 200}
 if(-not $run){throw 'No owned native run appeared'}
 $report=Get-Content (Join-Path $run 'report.json') -Raw|ConvertFrom-Json;$ownedPid=[int]$report.processId
 $process=Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
 if($process -and $process.MainModule.FileName -ine (Join-Path $app 'FileCat.exe')){throw 'Native process path mismatch'}
 if($report.appSha256 -ine (Get-FileHash (Join-Path $app 'FileCat.dll')).Hash){throw 'Native app hash mismatch'}
 $verified=$true;$ownedPid|Set-Content (Join-Path $root 'owned.pid');'RUNNING'|Set-Content $state
 $until=[DateTime]::UtcNow.AddMinutes(3)
 while((Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 200}
 $report=Get-Content (Join-Path $run 'report.json') -Raw|ConvertFrom-Json
 $input=Get-Content (Join-Path $run 'windows-input.json') -Raw|ConvertFrom-Json
 Copy-Item (Join-Path $run 'report.json') (Join-Path $root 'report.json');Copy-Item (Join-Path $run 'windows-input.json') (Join-Path $root 'windows-input.json')
 if($input.processId -ne $ownedPid -or $report.processId -ne $ownedPid){throw 'Report owner mismatch'}
 if($input.state -ne 'passed-native-input-subset' -or -not $report.sourceUnchanged -or $report.deadlineClosed -or (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)){throw ('Native subset failed: '+$input.error)}
 'FINISHED exit=0'|Set-Content $state
}catch{($_|Out-String)|Set-Content (Join-Path $root 'failure.txt');'FAILED'|Set-Content $state}
finally{
 if($verified -and (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)){
  New-Item -ItemType File -Force (Join-Path $run 'close.request')|Out-Null
  $until=[DateTime]::UtcNow.AddSeconds(8)
  while((Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 100}
 }
 if($verified){('ownedProcessExited='+(-not [bool](Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)))|Set-Content (Join-Path $root 'owned-exit.txt')}
}
