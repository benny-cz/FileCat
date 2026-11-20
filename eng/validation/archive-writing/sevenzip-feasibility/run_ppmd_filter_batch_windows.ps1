param([Parameter(Mandatory=$true)][string]$PacketSha256)
$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297'
$root="$task\sevenzip-ppmd-filter-source-v2";$out="$task\sevenzip-ppmd-filter-v2"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 if((Test-Path -LiteralPath $root) -or (Test-Path -LiteralPath $out)){throw 'Fresh batch destinations already exist.'}
 if((Get-FileHash -LiteralPath "$task\sevenzip-ppmd-filter-source-v2.tar.gz" -Algorithm SHA256).Hash -ne $PacketSha256){throw 'Source packet differs.'}
 [void][IO.Directory]::CreateDirectory($root);[void][IO.Directory]::CreateDirectory($out)
 & tar -xzf "$task\sevenzip-ppmd-filter-source-v2.tar.gz" -C $root
 if($LASTEXITCODE){throw 'Source extraction failed.'}
 Copy-Item -LiteralPath "$root\eng\validation\archive-writing\sevenzip-feasibility\WindowsNativeSevenZipOracle.cs" -Destination "$out\WindowsNativeSevenZipOracle.cs"
 Copy-Item -LiteralPath "$root\eng\validation\archive-writing\sevenzip-feasibility\verify_windows.ps1" -Destination "$out\verify-windows-native.ps1"
 Set-Location $root;$dotnet="$task\dotnet\dotnet.exe"
 $env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:MSBUILDDISABLENODEREUSE='1'
 foreach($project in @('src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj')){
  $name=[IO.Path]::GetFileNameWithoutExtension($project)
  & $dotnet build $project -c Release --disable-build-servers -p:NuGetAudit=false -m:1 -v minimal *> "$out\$name-build.log"
  if($LASTEXITCODE){throw "Build failed: $project"}
 }
 [void][IO.Directory]::CreateDirectory("$out\tools")
 if((Get-FileHash -LiteralPath "$task\sevenzip-ppmd-filter-tools-v1.tar.gz" -Algorithm SHA256).Hash -ne 'b63159c5ef42c8406b4f6ec8e344a183f1cfd6ba39602c9e268ba81c7caefc2a'){throw 'Private tool packet differs.'}
 & tar -xzf "$task\sevenzip-ppmd-filter-tools-v1.tar.gz" -C "$out\tools"
 if($LASTEXITCODE){throw 'Private test-tool extraction failed.'}
 $toolRows=Get-Content -LiteralPath "$out\tools\files.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 foreach($row in $toolRows.files){
  $p=Join-Path "$out\tools" $row.path
  if((Get-Item $p).Length -ne $row.bytes -or (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $row.sha256){throw 'Private test tool source/binary differs.'}
 }
 Copy-Item -LiteralPath "$root\eng\validation\archive-writing\sevenzip-feasibility\verify_sdk_windows.ps1" -Destination "$out\verify-sdk.ps1"
 $sid=(Get-LocalUser -Name 'fcarchive42829297').SID.Value
 & icacls $root /grant "*${sid}:(OI)(CI)F" /T /Q *> "$out\source-test-acl.log"
 if($LASTEXITCODE){throw 'Owned test-tree ACL provisioning failed.'}
 & icacls $out /grant "*${sid}:(OI)(CI)F" /T /Q *> "$out\evidence-test-acl.log"
 if($LASTEXITCODE){throw 'Owned evidence-tree ACL provisioning failed.'}
 $script=@'
$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297'
$root="$task\sevenzip-ppmd-filter-source-v2";$out="$task\sevenzip-ppmd-filter-v2"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 if($identity.Groups.Contains([Security.Principal.SecurityIdentifier]'S-1-5-32-544')){throw 'Actual standard account required.'}
 if(-not(Test-Path "Registry::HKEY_USERS\$($identity.User.Value)")){throw 'Standard profile is not loaded.'}
 @{Name=$identity.Name;Sid=$identity.User.Value;AdministratorsMember=$false;HiveLoaded=$true}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\account.json"
 $env:TEMP=Join-Path $env:USERPROFILE 'AppData\Local\Temp';$env:TMP=$env:TEMP
 $env:FILECAT_SEVENZIP_FILTER_EXPORT_ROOT="$out\vectors"
 $env:FILECAT_SEVENZIP_CODEC_EXPORT_ROOT="$out\oracles";[void][IO.Directory]::CreateDirectory($env:FILECAT_SEVENZIP_CODEC_EXPORT_ROOT)
 Set-Location C:\Windows\System32
 $selected='FullyQualifiedName~SevenZipFilterEncodingTests|FullyQualifiedName~SevenZipWritingTests|FullyQualifiedName~ArchivePasswordPrimitiveTests|FullyQualifiedName~SevenZipCrcTests|FullyQualifiedName~SevenZipPpmdTests|FullyQualifiedName~SevenZipHeaderBudgetTests|FullyQualifiedName~SevenZipEncodedHeaderTests|FullyQualifiedName~EmptySevenZipTests|FullyQualifiedName~Rar5WritingTests|FullyQualifiedName~RarReaderTests|FullyQualifiedName~ArchiveWriteBoundaryTests|FullyQualifiedName~ArchiveCapturedInputsTests|FullyQualifiedName~ArchiveDirectoryWireTests|FullyQualifiedName~ArchiveTransform|FullyQualifiedName~ArchiveCreationOriginTests|FullyQualifiedName~UnixArchiveStorageTests'
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' exec 'C:\Users\fcarchive42829297\FileCatValidation\Capture-42829297-v7\tester\vstest.console.dll' "$root\tests\FileCat.Core.Tests\bin\Release\net10.0\FileCat.Core.Tests.dll" "/TestCaseFilter:$selected" '/Logger:trx;LogFileName=sevenzip-ppmd-filter.trx' "/ResultsDirectory:$out\results" *> "$out\tests.log"
 $code=$LASTEXITCODE;$ErrorActionPreference='Stop';if($code){throw "7z/crypto/common regression failed: $code"}
 & "$out\verify-sdk.ps1" -Root "$out\vectors" -Oracle "$out\tools\windows\sdk-oracle.exe" -Tool "$out\tools\windows\7za.exe" -Archives "$out\oracles" *> "$out\sdk-official.log"
 & "$out\verify-windows-native.ps1" -Root "$out\oracles" -Source "$out\WindowsNativeSevenZipOracle.cs" *> "$out\native-utf8-api.log"
 'PASS'|Set-Content -Encoding UTF8 "$out\standard-complete.txt"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$out\standard-error.txt";exit 1}
'@
 [IO.File]::WriteAllText("$out\run-standard.ps1",$script)
 $credential=New-Object Management.Automation.PSCredential('.\fcarchive42829297',(ConvertTo-SecureString 'a' -AsPlainText -Force))
 $child=Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory 'C:\Windows\System32' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$out\run-standard.ps1") -Credential $credential -LoadUserProfile -WindowStyle Hidden -RedirectStandardOutput "$out\standard-launch.out" -RedirectStandardError "$out\standard-launch.err" -Wait -PassThru
 @{Pid=$child.Id;ExitCode=$child.ExitCode;ProfileLoaded=$true}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\standard-launch.json"
 if($child.ExitCode){throw 'Standard-user 7z/crypto batch failed.'}
 [void][IO.Directory]::CreateDirectory("$out\binaries")
 foreach($folder in @("$root\tests\FileCat.Core.Tests\bin\Release\net10.0","$root\src\FileCat.Archives.Writing\bin\Release\net10.0")){
  foreach($file in Get-ChildItem -LiteralPath $folder -File){if($file.Name -like 'FileCat.*' -and $file.Extension -in @('.dll','.pdb')){Copy-Item -LiteralPath $file.FullName -Destination "$out\binaries\$($file.Name)" -Force}}
 }
 & $dotnet "$root\eng\validation\archive-writing\PdbSourceAudit\bin\Release\net10.0\PdbSourceAudit.dll" $root "$out\binaries" "$out\compiler-sources" 'C:\Users\Admin\.nuget\packages' *> "$out\compiler-source-audit.log"
 if($LASTEXITCODE){throw 'Compiler-source checksums differ.'}
 Copy-Item -LiteralPath "$task\sevenzip-ppmd-filter-source-v2.tar.gz" -Destination "$out\sevenzip-ppmd-filter-source-v2.tar.gz"
 Copy-Item -LiteralPath $MyInvocation.MyCommand.Path -Destination "$out\executed-batch.ps1"
 $busy=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -match '^(FileCat.*|dotnet|testhost)(\.exe)?$'}|Select-Object Name,ProcessId,CommandLine)
 @{Processes=$busy;CollectedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 "$out\final-census.json"
 if($busy.Count){throw 'A tester is active; defer final evidence collection.'}
 'PASS'|Set-Content -Encoding UTF8 "$out\complete.txt"
 $rows=@(Get-ChildItem -LiteralPath $out -File -Recurse|Sort-Object FullName|ForEach-Object {@{path=$_.FullName.Substring($out.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
 @{files=$rows;production_backend_qualified=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$out\files.json"
 & tar -czf "$task\sevenzip-ppmd-filter-evidence-v2.tar.gz" -C $task sevenzip-ppmd-filter-v2
 if($LASTEXITCODE){throw 'Evidence archive failed.'}
 @{bytes=(Get-Item "$task\sevenzip-ppmd-filter-evidence-v2.tar.gz").Length;sha256=(Get-FileHash -LiteralPath "$task\sevenzip-ppmd-filter-evidence-v2.tar.gz" -Algorithm SHA256).Hash;rows=$rows.Count}|ConvertTo-Json|Set-Content -Encoding UTF8 "$task\sevenzip-ppmd-filter-evidence-v2.json"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$task\sevenzip-ppmd-filter-error-v2.txt";exit 1}
