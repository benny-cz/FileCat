$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'native-state-r216.txt'
try{
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{-not $_.CommandLine -or $_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){'BUSY'|Set-Content $state;exit 75}
 $archive=Join-Path $root 'native-r216.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne '932cdb1d88f169534493a1af9e9e3d213e1703b9954c1b9e8dc284a656b127c2'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'native-r216';New-Item -ItemType Directory -Path $bin|Out-Null
 & tar.exe -xzf $archive -C $bin;if($LASTEXITCODE){throw 'Extraction failed'}
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Platform.Windows.Tests.dll') -class '*AnalysisWindowsTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'native-r216.xml') *> (Join-Path $root 'native-r216.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result
}catch{($_|Out-String)|Set-Content (Join-Path $root 'native-failure-r216.txt');'FAILED'|Set-Content $state;exit 1}
