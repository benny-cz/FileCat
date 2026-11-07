param([Parameter(Mandatory=$true)][string]$PacketSha256)
$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297'
$root="$task\sevenzip-bcj2-source-v2";$out="$task\sevenzip-bcj2-v2"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 if((Test-Path -LiteralPath $root) -or (Test-Path -LiteralPath $out)){throw 'Fresh batch destinations already exist.'}
 if((Get-FileHash -LiteralPath "$task\sevenzip-bcj2-source-v2.tar.gz" -Algorithm SHA256).Hash -ne $PacketSha256){throw 'Source packet differs.'}
 [void][IO.Directory]::CreateDirectory($root);[void][IO.Directory]::CreateDirectory($out)
 & tar -xzf "$task\sevenzip-bcj2-source-v2.tar.gz" -C $root
 if($LASTEXITCODE){throw 'Source extraction failed.'}
 Copy-Item -LiteralPath "$root\eng\validation\archive-writing\sevenzip-feasibility\WindowsNativeSevenZipHeaderOracle.cs" -Destination "$out\WindowsNativeSevenZipHeaderOracle.cs"
 Copy-Item -LiteralPath "$root\eng\validation\archive-writing\sevenzip-feasibility\verify_header_windows.ps1" -Destination "$out\verify-windows-native.ps1"
 Set-Location $root;$dotnet="$task\dotnet\dotnet.exe"
 $env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:MSBUILDDISABLENODEREUSE='1'
 foreach($project in @('src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj','eng/ArtifactSbom/ArtifactSbom.csproj')){
  $name=[IO.Path]::GetFileNameWithoutExtension($project)
  & $dotnet build $project -c Release --disable-build-servers -p:NuGetAudit=false -m:1 -v minimal *> "$out\$name-build.log"
  if($LASTEXITCODE){throw "Build failed: $project"}
 }
 [void][IO.Directory]::CreateDirectory("$out\tools")
 if((Get-FileHash -LiteralPath "$task\sevenzip-bcj2-tools-v1.tar.gz" -Algorithm SHA256).Hash -ne '9fc7b0edf4748630e164d6efe4ee712b2d78c7bb27e7069b92404e685c604c6f'){throw 'Private SDK tool packet differs.'}
 & tar -xzf "$task\sevenzip-bcj2-tools-v1.tar.gz" -C "$out\tools"
 if($LASTEXITCODE){throw 'SDK packet extraction failed.'}
 $toolRows=Get-Content -LiteralPath "$out\tools\files.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 foreach($row in $toolRows.files){$path=Join-Path "$out\tools" $row.path;if((Get-Item -LiteralPath $path).Length -ne $row.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $row.sha256){throw 'Private SDK file differs.'}}
 [void][IO.Directory]::CreateDirectory("$out\current-native")
 $native="$task\sevenzip-ppmd-filter-v2\current-native\archive-3.8.11.dll"
 if((Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ne '9E561C2EC6E9F3239BB32284208CD97FB3A83E3EB8C8C797BB7BBC52361BF404'){throw 'Private PPMd oracle differs.'}
 Copy-Item -LiteralPath $native -Destination "$out\current-native\archive-3.8.11.dll"
 $PacketSha256|Set-Content -Encoding UTF8 "$out\source-packet-sha256.txt"
 $sid=(Get-LocalUser -Name 'fcarchive42829297').SID.Value
 & icacls $root /grant "*${sid}:(OI)(CI)F" /T /Q *> "$out\source-test-acl.log"
 if($LASTEXITCODE){throw 'Owned test-tree ACL provisioning failed.'}
 & icacls $out /grant "*${sid}:(OI)(CI)F" /T /Q *> "$out\evidence-test-acl.log"
 if($LASTEXITCODE){throw 'Owned evidence-tree ACL provisioning failed.'}
 $script=@'
$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297'
$root="$task\sevenzip-bcj2-source-v2";$out="$task\sevenzip-bcj2-v2"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 if($identity.Groups.Contains([Security.Principal.SecurityIdentifier]'S-1-5-32-544')){throw 'Actual standard account required.'}
 if(-not(Test-Path "Registry::HKEY_USERS\$($identity.User.Value)")){throw 'Standard profile is not loaded.'}
 @{Name=$identity.Name;Sid=$identity.User.Value;AdministratorsMember=$false;HiveLoaded=$true}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\account.json"
 $env:TEMP=Join-Path $env:USERPROFILE 'AppData\Local\Temp';$env:TMP=$env:TEMP
 $env:FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY="$out\child-diagnostics"
 [void][IO.Directory]::CreateDirectory($env:FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY)
 $env:FILECAT_SEVENZIP_BCJ2_EXPORT_ROOT="$out\bcj2";$env:FILECAT_SEVENZIP_CODEC_EXPORT_ROOT="$out\oracles";$env:FILECAT_SEVENZIP_FILTER_EXPORT_ROOT="$out\vectors"
 foreach($folder in @('bcj2','oracles','vectors')){[void][IO.Directory]::CreateDirectory("$out\$folder")}
 $env:DOTNET_ROOT="$task\dotnet";$env:PATH="$task\dotnet;"+$env:PATH;$env:MSBUILDDISABLENODEREUSE='1';$env:PYTHONDONTWRITEBYTECODE='1'
 Set-Location C:\Windows\System32
 $selected='FullyQualifiedName~SevenZipBcj2WritingTests|FullyQualifiedName~ArchiveWorkerSecretTests|FullyQualifiedName~ArchiveWorkerTransportSecretTests|FullyQualifiedName~ArchiveWorkerCancellationTests|FullyQualifiedName~ArchiveAssessmentClientTests|FullyQualifiedName~ArchiveVerificationClientTests|FullyQualifiedName~ArchiveManifestWireTests|FullyQualifiedName~ArchiveProxyTests|FullyQualifiedName~ArchiveWorkerTrafficPolicyTests|FullyQualifiedName~ArchivePasswordPolicyTests|FullyQualifiedName~ArchivePasswordWritingTests|FullyQualifiedName~ArchiveAesCbcWritingTests|FullyQualifiedName~SevenZipCompressedHeaderWritingTests|FullyQualifiedName~SevenZipFilterEncodingTests|FullyQualifiedName~SevenZipWritingTests|FullyQualifiedName~ArchivePasswordPrimitiveTests|FullyQualifiedName~SevenZipCrcTests|FullyQualifiedName~SevenZipPpmdTests|FullyQualifiedName~SevenZipHeaderBudgetTests|FullyQualifiedName~SevenZipEncodedHeaderTests|FullyQualifiedName~EmptySevenZipTests|FullyQualifiedName~Rar5WritingTests|FullyQualifiedName~RarReaderTests|FullyQualifiedName~ArchiveWriteBoundaryTests|FullyQualifiedName~ArchiveCapturedInputsTests|FullyQualifiedName~ArchiveDirectoryWireTests|FullyQualifiedName~ArchiveTransform|FullyQualifiedName~ArchiveCreationOriginTests|FullyQualifiedName~UnixArchiveStorageTests'
 $ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' exec 'C:\Users\fcarchive42829297\FileCatValidation\Capture-42829297-v7\tester\vstest.console.dll' "$root\tests\FileCat.Core.Tests\bin\Release\net10.0\FileCat.Core.Tests.dll" "/TestCaseFilter:$selected" '/Logger:trx;LogFileName=archive-worker-secrets.trx' "/ResultsDirectory:$out\results" *> "$out\tests.log"
 $code=$LASTEXITCODE;$ErrorActionPreference='Stop';if($code){throw "7z/crypto/common regression failed: $code"}
 $helpers="$root\eng\validation\archive-writing\sevenzip-feasibility";$readerTools="$task\password-oracle-tools-v1";$python="$readerTools\python\python.exe";$tool="$readerTools\7zip\7z.exe"
 function PythonCheck([string]$name,[string[]]$arguments){& $python -B @arguments *> "$out\$name.log";if($LASTEXITCODE){throw "Independent validation failed: $name"}}
 PythonCheck 'bcj2-sdk' @("$helpers\verify_bcj2_sdk.py","$out\bcj2","$out\tools\windows\sdk-oracle.exe")
 PythonCheck 'previous-sdk' @("$helpers\verify_sdk.py","$out\vectors","$out\tools\windows\sdk-oracle.exe")
 PythonCheck 'reader-module-reuse' @("$helpers\verify_oracle_module_reuse.py",$readerTools,'windows')
 if((Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash -ne '94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e'){throw 'Official full 7-Zip oracle differs.'}
 PythonCheck 'bcj2-official' @("$helpers\verify_bcj2_cli.py","$out\bcj2",$tool,"$readerTools\modules")
 PythonCheck 'previous-official' @("$helpers\verify_header_cli.py","$out\oracles",$tool)
 PythonCheck 'bcj2-native-survey' @("$helpers\verify_bcj2_native.py","$out\bcj2",'C:\Windows\System32\archiveint.dll')
 & "$out\verify-windows-native.ps1" -Root "$out\oracles" -Source "$out\WindowsNativeSevenZipHeaderOracle.cs" -PpmdLibrary "$out\current-native\archive-3.8.11.dll" *> "$out\previous-native.log"
 $expected=(Get-Content -LiteralPath "$root\docs\validation\archive-writing\archive-password-checkpoint.json" -Encoding UTF8 -Raw|ConvertFrom-Json).unencrypted_archive_sha256
 foreach($property in $expected.PSObject.Properties){if((Get-FileHash -LiteralPath "$out\oracles\$($property.Name)\members.7z" -Algorithm SHA256).Hash -ne $property.Value){throw 'A previous archive changed.'}}
 $expected|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\previous-archives-unchanged.json"
 $env:NUGET_PACKAGES=Join-Path $env:USERPROFILE '.nuget\packages'
 $packet=Get-Content -LiteralPath "$out\source-packet-sha256.txt" -Encoding UTF8 -Raw
 PythonCheck 'sbom-controls' @("$root\eng\validation\validate-artifact-sbom.py",'--evidence-dir',"$out\sbom-controls",'--source-packet-sha256',$packet.Trim())
 'PASS'|Set-Content -Encoding UTF8 "$out\standard-complete.txt"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$out\standard-error.txt";exit 1}
'@
 [IO.File]::WriteAllText("$out\run-standard.ps1",$script)
 $credential=New-Object Management.Automation.PSCredential('.\fcarchive42829297',(ConvertTo-SecureString 'a' -AsPlainText -Force))
 $child=Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory 'C:\Windows\System32' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$out\run-standard.ps1") -Credential $credential -LoadUserProfile -WindowStyle Hidden -RedirectStandardOutput "$out\standard-launch.out" -RedirectStandardError "$out\standard-launch.err" -Wait -PassThru
 @{Pid=$child.Id;ExitCode=$child.ExitCode;ProfileLoaded=$true}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\standard-launch.json"
 if($child.ExitCode){throw 'Standard-user 7z/crypto batch failed.'}
 [void][IO.Directory]::CreateDirectory("$out\binaries")
 foreach($folder in @("$root\tests\FileCat.Core.Tests\bin\Release\net10.0","$root\src\FileCat.Archives.Writing\bin\Release\net10.0","$root\tests\FileCat.WorkerProbe\bin\Release\net10.0")){
  foreach($file in Get-ChildItem -LiteralPath $folder -File){if($file.Name -like 'FileCat.*' -and $file.Extension -in @('.dll','.pdb')){Copy-Item -LiteralPath $file.FullName -Destination "$out\binaries\$($file.Name)" -Force}}
 }
 & $dotnet "$root\eng\validation\archive-writing\PdbSourceAudit\bin\Release\net10.0\PdbSourceAudit.dll" $root "$out\binaries" "$out\compiler-sources" 'C:\Users\Admin\.nuget\packages' *> "$out\compiler-source-audit.log"
 if($LASTEXITCODE){throw 'Compiler-source checksums differ.'}
 [void][IO.Directory]::CreateDirectory("$out\sbom-binaries")
 foreach($extension in @('.dll','.pdb')){Copy-Item -LiteralPath "$out\sbom-controls\artifacts\bin\ArtifactSbom\release\ArtifactSbom$extension" -Destination "$out\sbom-binaries\ArtifactSbom$extension"}
 $sbomFacts=Get-Content -LiteralPath "$out\sbom-controls\artifact-sbom-controls.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 & $dotnet "$root\eng\validation\archive-writing\PdbSourceAudit\bin\Release\net10.0\PdbSourceAudit.dll" $sbomFacts.SourceRoot "$out\sbom-binaries" "$out\sbom-compiler-sources" $sbomFacts.ArtifactsRoot '--artifact-sbom' *> "$out\sbom-source-audit.log"
 if($LASTEXITCODE){throw 'SBOM compiler-source checksums differ.'}

 Copy-Item -LiteralPath "$task\sevenzip-bcj2-source-v2.tar.gz" -Destination "$out\sevenzip-bcj2-source-v2.tar.gz"
 Copy-Item -LiteralPath $MyInvocation.MyCommand.Path -Destination "$out\executed-batch.ps1"
 $busy=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -match '^(FileCat.*|dotnet|testhost)(\.exe)?$'}|Select-Object Name,ProcessId,CommandLine)
 @{Processes=$busy;CollectedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 "$out\final-census.json"
 if($busy.Count){throw 'A tester is active; defer final evidence collection.'}
 'PASS: BCJ2/codec/crypto regression and independent checks; native-survey gaps and full production qualification remain explicit.'|Set-Content -Encoding UTF8 "$out\complete.txt"
 $rows=@(Get-ChildItem -LiteralPath $out -File -Recurse|Sort-Object FullName|ForEach-Object {@{path=$_.FullName.Substring($out.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
 @{files=$rows;production_backend_qualified=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$out\files.json"
 & tar -czf "$task\sevenzip-bcj2-evidence-v2.tar.gz" -C $task sevenzip-bcj2-v2
 if($LASTEXITCODE){throw 'Evidence archive failed.'}
 @{bytes=(Get-Item "$task\sevenzip-bcj2-evidence-v2.tar.gz").Length;sha256=(Get-FileHash -LiteralPath "$task\sevenzip-bcj2-evidence-v2.tar.gz" -Algorithm SHA256).Hash;rows=$rows.Count}|ConvertTo-Json|Set-Content -Encoding UTF8 "$task\sevenzip-bcj2-evidence-v2.json"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$task\sevenzip-bcj2-error-v2.txt";exit 1}
