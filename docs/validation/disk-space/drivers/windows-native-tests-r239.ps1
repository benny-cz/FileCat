$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'native-state-r239.txt'
try{
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{-not $_.CommandLine -or $_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){'BUSY'|Set-Content $state;exit 75}
 $archive=Join-Path $root 'native-r239.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne '1afb8e554d89f74542684e795b718ba32f70ce7e67148cb0a3324e2c2c332e65'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'native-r239';New-Item -ItemType Directory -Path $bin|Out-Null
 & tar.exe -xzf $archive -C $bin;if($LASTEXITCODE){throw 'Extraction failed'}
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Platform.Windows.Tests.dll') -class '*AnalysisWindowsTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'native-r239.xml') *> (Join-Path $root 'native-r239.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result
}catch{($_|Out-String)|Set-Content (Join-Path $root 'native-failure-r239.txt');'FAILED'|Set-Content $state;exit 1}
