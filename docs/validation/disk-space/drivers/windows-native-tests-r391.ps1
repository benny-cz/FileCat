$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2';$state=Join-Path $root 'native-state-r391.txt'
try{
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75}
 $archive=Join-Path $root 'native-r391.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash -ne 'c85ba4f4e92a07206186ca9df411b5d3b1dade38c2d2654899af6fa32d1213bc'){throw 'Archive mismatch'}
 $bin=Join-Path $root 'native-r391';New-Item -ItemType Directory -Path $bin|Out-Null
 & tar.exe -xzf $archive -C $bin;if($LASTEXITCODE){throw 'Extraction failed'}
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $bin 'FileCat.Platform.Windows.Tests.dll') -method '*NativePrivateFiles*' -noLogo -noColor -maxThreads 1 -xml (Join-Path $root 'native-r391.xml') *> (Join-Path $root 'native-r391.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result
}catch{($_|Out-String)|Set-Content (Join-Path $root 'native-failure-r391.txt');'FAILED'|Set-Content $state;exit 1}
