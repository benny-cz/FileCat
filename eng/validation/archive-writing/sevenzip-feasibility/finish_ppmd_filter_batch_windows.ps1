$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297'
$root="$task\sevenzip-ppmd-filter-source-v2";$out="$task\sevenzip-ppmd-filter-v2"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 [xml]$trx=Get-Content -LiteralPath "$out\results\sevenzip-ppmd-filter.trx" -Raw
 if($trx.TestRun.ResultSummary.Counters.failed -ne '0' -or $trx.TestRun.ResultSummary.Counters.passed -ne '441'){throw 'The managed batch has not passed.'}
 $sdk=Get-Content -LiteralPath "$out\vectors\sdk-reader.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 $official=Get-Content -LiteralPath "$out\oracles\official-reader.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 if($sdk.verified.Count -ne 19 -or $official.tested_archives -ne 20 -or $official.verified.Count -ne 44){throw 'The independent SDK/official proofs have not passed.'}
 if((Get-FileHash -LiteralPath "$task\ppmd-native-tools-v3.tar.gz" -Algorithm SHA256).Hash -ne '3e0f5a2d446dbc88b045280bbf643514f06966c0a58b9a483fe565d90c18bb49'){throw 'Private current-reader tool packet differs.'}
 [void][IO.Directory]::CreateDirectory("$out\current-native")
 & tar -xzf "$task\ppmd-native-tools-v3.tar.gz" -C "$out\current-native"
 if($LASTEXITCODE){throw 'Current reader extraction failed.'}
 $rows=Get-Content -LiteralPath "$out\current-native\files.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 foreach($row in $rows.files){$p=Join-Path "$out\current-native" $row.path;if((Get-Item $p).Length -ne $row.bytes -or (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $row.sha256){throw 'Current reader input differs.'}}
 Copy-Item -LiteralPath "$task\verify-native-ppmd-v3.ps1" -Destination "$out\verify-native-ppmd-v3.ps1"
 $script=@'
$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297';$out="$task\sevenzip-ppmd-filter-v2"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 if($identity.Groups.Contains([Security.Principal.SecurityIdentifier]'S-1-5-32-544')){throw 'Actual standard account required.'}
 if(-not(Test-Path "Registry::HKEY_USERS\$($identity.User.Value)")){throw 'Standard profile is not loaded.'}
 @{Name=$identity.Name;Sid=$identity.User.Value;AdministratorsMember=$false;HiveLoaded=$true}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\current-native-account.json"
 & "$out\verify-native-ppmd-v3.ps1" -Root "$out\oracles" -Source "$out\WindowsNativeSevenZipOracle.cs" -PpmdLibrary "$out\current-native\archive-3.8.11.dll" *> "$out\native-current-v3.log"
 'PASS'|Set-Content -Encoding UTF8 "$out\standard-current-native-complete.txt"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$out\standard-current-native-error.txt";exit 1}
'@
 [IO.File]::WriteAllText("$out\run-current-native-standard.ps1",$script)
 $credential=New-Object Management.Automation.PSCredential('.\fcarchive42829297',(ConvertTo-SecureString 'a' -AsPlainText -Force))
 $child=Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory 'C:\Windows\System32' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$out\run-current-native-standard.ps1") -Credential $credential -LoadUserProfile -WindowStyle Hidden -RedirectStandardOutput "$out\current-native-launch.out" -RedirectStandardError "$out\current-native-launch.err" -Wait -PassThru
 @{Pid=$child.Id;ExitCode=$child.ExitCode;ProfileLoaded=$true}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\current-native-launch.json"
 if($child.ExitCode){throw 'Current native standard-account verification failed.'}
 $dotnet="$task\dotnet\dotnet.exe"
 Copy-Item -LiteralPath "$task\run-ppmd-filter-v2.ps1" -Destination "$out\original-executed-batch.ps1"
 Copy-Item -LiteralPath "$task\sevenzip-ppmd-filter-error-v2.txt" -Destination "$out\original-parent-error.txt"
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
 & tar -czf "$task\sevenzip-ppmd-filter-evidence-v3.tar.gz" -C $task sevenzip-ppmd-filter-v2
 if($LASTEXITCODE){throw 'Evidence archive failed.'}
 @{bytes=(Get-Item "$task\sevenzip-ppmd-filter-evidence-v3.tar.gz").Length;sha256=(Get-FileHash -LiteralPath "$task\sevenzip-ppmd-filter-evidence-v3.tar.gz" -Algorithm SHA256).Hash;rows=$rows.Count}|ConvertTo-Json|Set-Content -Encoding UTF8 "$task\sevenzip-ppmd-filter-evidence-v3.json"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$task\ppmd-filter-finish-error-v3.txt";exit 1}
