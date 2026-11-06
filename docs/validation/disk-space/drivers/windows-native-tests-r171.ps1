$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'native-state-r171.txt'
try{
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){'BUSY'|Set-Content $state;exit 75}
 $archive=Join-Path $root 'native-r171.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne 'bde09b4149234a056b5c2da1485f6650cbd0c53a848959c29ff3322e18694d02'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'native-r171';New-Item -ItemType Directory -Path $bin|Out-Null
 & tar.exe -xzf $archive -C $bin;if($LASTEXITCODE){throw 'Extraction failed'}
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Platform.Windows.Tests.dll') -class '*AnalysisWindowsTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'native-r171.xml') *> (Join-Path $root 'native-r171.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result
}catch{($_|Out-String)|Set-Content (Join-Path $root 'native-failure-r171.txt');'FAILED'|Set-Content $state;exit 1}
