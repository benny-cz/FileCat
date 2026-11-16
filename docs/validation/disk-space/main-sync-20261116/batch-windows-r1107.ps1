$ErrorActionPreference='Stop'
$base='C:\FileCatDsa20261104R2\batch-r1107'
if(Test-Path -LiteralPath $base){throw 'A fresh focused result directory is required'}
New-Item -ItemType Directory -Path $base|Out-Null
if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){'BUSY phase=verify'|Set-Content (Join-Path $base 'state.txt');exit 75}
$bin='C:\FileCatDsa20261104R2\batch-r1105\payload\native'
if((Get-FileHash -LiteralPath (Join-Path $bin 'FileCat.Core.dll') -Algorithm SHA256).Hash-ne'0044E7E9D18B8B3A89A2BF7E5BB79FB4673777C4BCA1EADE4757B288EC6F8E5A'){throw 'Core changed'}
if((Get-FileHash -LiteralPath (Join-Path $bin 'FileCat.Platform.Windows.dll') -Algorithm SHA256).Hash-ne'F335EC247EA5FF3FA5CEA25A4608F433B57D5E17E5D8B93791559321A9979499'){throw 'Native library changed'}
('RUNNING phase=native runnerPid='+$PID)|Set-Content (Join-Path $base 'state.txt')
[ordered]@{user=(& whoami.exe);session=(Get-Process -Id $PID).SessionId;temp=$env:TEMP;coreSha256='0044E7E9D18B8B3A89A2BF7E5BB79FB4673777C4BCA1EADE4757B288EC6F8E5A';nativeSha256='F335EC247EA5FF3FA5CEA25A4608F433B57D5E17E5D8B93791559321A9979499'}|ConvertTo-Json|Set-Content (Join-Path $base 'environment.json')
Set-Location -LiteralPath $bin
$ErrorActionPreference='Continue'
& 'C:\Program Files\dotnet\dotnet.exe' FileCat.Platform.Windows.Tests.dll -method '*WindowsFileOperationsTests.Recycle_reports_outcome_and_restore_uses_the_bin_item' -noLogo -noColor -maxThreads 1 -xml (Join-Path $base 'native-r1107.xml') *> (Join-Path $base 'native-r1107.log')
$result=$LASTEXITCODE
('FINISHED exit='+$result)|Set-Content (Join-Path $base 'state.txt')
exit $result