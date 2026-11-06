$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'native-state-r179.txt'
try{
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){'BUSY'|Set-Content $state;exit 75}
 $archive=Join-Path $root 'native-r179.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne 'f9b3b3655d7abb918b92f3f076deab7ca948b1194dde45da100db4cc1243a652'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'native-r179';New-Item -ItemType Directory -Path $bin|Out-Null
 & tar.exe -xzf $archive -C $bin;if($LASTEXITCODE){throw 'Extraction failed'}
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Platform.Windows.Tests.dll') -class '*AnalysisWindowsTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'native-r179.xml') *> (Join-Path $root 'native-r179.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result
}catch{($_|Out-String)|Set-Content (Join-Path $root 'native-failure-r179.txt');'FAILED'|Set-Content $state;exit 1}
