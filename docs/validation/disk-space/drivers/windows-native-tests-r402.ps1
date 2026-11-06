$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'native-state-r402.txt'
try{
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75}
 $archive=Join-Path $root 'native-r402.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne '283ebe1ffd5a54a48824ee5ed990c59f163382ef28e6625494d7dc3b3e168221'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'native-r402';New-Item -ItemType Directory -Path $bin|Out-Null
 & tar.exe -xzf $archive -C $bin;if($LASTEXITCODE){throw 'Extraction failed'}
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Platform.Windows.Tests.dll') -class '*AnalysisWindowsTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'native-r402.xml') *> (Join-Path $root 'native-r402.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result
}catch{($_|Out-String)|Set-Content (Join-Path $root 'native-failure-r402.txt');'FAILED'|Set-Content $state;exit 1}
