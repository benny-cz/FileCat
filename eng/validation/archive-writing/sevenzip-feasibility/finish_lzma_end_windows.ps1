$ErrorActionPreference='Stop';$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297';$root="$task\lzma-end-source-v3";$out="$task\lzma-end-v3"
try {
 $deadline=[DateTime]::UtcNow.AddMinutes(30)
 while(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){if([DateTime]::UtcNow -ge $deadline){throw 'Another tester is active; postpone.'};Start-Sleep -Seconds 15}
 if(Test-Path -LiteralPath "$out\files.json"){throw 'Evidence is already frozen.'}
 if((Get-FileHash -LiteralPath "$task\lzma-end-source-v3.tar.gz" -Algorithm SHA256).Hash -ne '6d6becf38cc131af566e8f12bf5f591cff93e5f388ecdba36a26e1661c337304'){throw 'Source packet changed.'}
 Copy-Item -LiteralPath "$out\sdk-independent-resume.log" -Destination "$out\sdk-independent-first-resume.log"
 Copy-Item -LiteralPath "$out\standard-resume-error.txt" -Destination "$out\first-resume-error.txt"
 Copy-Item -LiteralPath "$task\lzma-end-resume-error-v3.txt" -Destination "$out\first-resume-parent-error.txt"
 Copy-Item -LiteralPath "$task\check_lzma_end_windows.py" -Destination "$out\first-resume-helper.py"
 $sid=(Get-LocalUser -Name 'fcarchive42829297').SID.Value
 & icacls "$task\check_lzma_end_windows_v2.py" /grant "*${sid}:R" /Q *> "$out\resume-helper-read-acl.log"
 if($LASTEXITCODE){throw 'Resume helper read grant failed.'}
 $script=@'
$ErrorActionPreference='Stop';$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297';$root="$task\lzma-end-source-v3";$out="$task\lzma-end-v3"
try {
 $deadline=[DateTime]::UtcNow.AddMinutes(30)
 while(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){if([DateTime]::UtcNow -ge $deadline){throw 'Another tester is active; postpone.'};Start-Sleep -Seconds 15}
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent();if($identity.Groups.Contains([Security.Principal.SecurityIdentifier]'S-1-5-32-544')){throw 'Standard account required.'}
 $ErrorActionPreference='Continue'
 & "$task\password-oracle-tools-v1\python\python.exe" -B "$task\check_lzma_end_windows_v2.py" $root $out $task *> "$out\sdk-independent-resume.log"
 $code=$LASTEXITCODE;$ErrorActionPreference='Stop';if($code){throw 'Remaining independent checks failed.'}
 'PASS'|Set-Content -Encoding UTF8 "$out\standard-complete-resume.txt"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$out\standard-resume-error.txt";exit 1}
'@
 [IO.File]::WriteAllText("$out\run-standard-resume.ps1",$script)
 $credential=New-Object Management.Automation.PSCredential('.\fcarchive42829297',(ConvertTo-SecureString 'a' -AsPlainText -Force))
 $child=Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory 'C:\Windows\System32' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$out\run-standard-resume.ps1") -Credential $credential -LoadUserProfile -WindowStyle Hidden -RedirectStandardOutput "$out\standard-resume-launch.out" -RedirectStandardError "$out\standard-resume-launch.err" -Wait -PassThru
 @{Pid=$child.Id;ExitCode=$child.ExitCode;ProfileLoaded=$true}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\standard-resume-launch.json"
 if($child.ExitCode){throw 'Standard remaining-checks resume failed.'}
 Copy-Item -LiteralPath "$task\check_lzma_end_windows_v2.py" -Destination "$out\executed-followup-resume.py"
 Copy-Item -LiteralPath "$task\lzma-end-error-v3.txt" -Destination "$out\initial-parent-helper-error.txt"
 [void][IO.Directory]::CreateDirectory("$out\prior-helper-failure-v1")
 foreach($file in @('tests.log','standard-error.txt','standard-launch.json','account.json')){Copy-Item -LiteralPath "$task\lzma-end-v1\$file" -Destination "$out\prior-helper-failure-v1\$file"}
 [void][IO.Directory]::CreateDirectory("$out\prior-helper-failure-v1\results")
 Copy-Item -LiteralPath "$task\lzma-end-v1\results\archive-worker-secrets.trx" -Destination "$out\prior-helper-failure-v1\results\archive-worker-secrets.trx"
 Copy-Item -LiteralPath "$task\lzma-end-error-v1.txt" -Destination "$out\prior-helper-failure-v1\parent-error.txt"
 Copy-Item -LiteralPath "$task\lzma-end-source-v1.tar.gz" -Destination "$out\prior-helper-failure-v1\source.tar.gz"
 $dotnet="$task\dotnet\dotnet.exe"
 $deps="$root\src\FileCat.Archives.Writing\bin\Release\net10.0\FileCat.Archives.Writing.deps.json"
 $libraries=(Get-Content -LiteralPath $deps -Encoding UTF8 -Raw|ConvertFrom-Json).libraries.PSObject.Properties.Name
 if(@($libraries|Where-Object {$_ -match 'Rar15|DiscUtils'}).Count){throw 'Reader-only dependency entered the writing closure.'}
 Copy-Item -LiteralPath $deps -Destination "$out\FileCat.Archives.Writing.deps.json"
 @{libraries=@($libraries|Sort-Object);Rar15_or_DiscUtils_present=$false}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\writing-closure.json"
 [void][IO.Directory]::CreateDirectory("$out\binaries")
 foreach($folder in @("$root\tests\FileCat.Core.Tests\bin\Release\net10.0","$root\src\FileCat.Archives.Writing\bin\Release\net10.0","$root\tests\FileCat.WorkerProbe\bin\Release\net10.0")){
  foreach($file in Get-ChildItem -LiteralPath $folder -File){if($file.Name -like 'FileCat.*' -and $file.Extension -in @('.dll','.pdb')){Copy-Item -LiteralPath $file.FullName -Destination "$out\binaries\$($file.Name)" -Force}}
 }
 & $dotnet "$root\eng\validation\archive-writing\PdbSourceAudit\bin\Release\net10.0\PdbSourceAudit.dll" $root "$out\binaries" "$out\compiler-sources" 'C:\Users\Admin\.nuget\packages' *> "$out\compiler-source-audit.log"
 if($LASTEXITCODE){throw 'Compiler-source checksums differ.'}
 Copy-Item -LiteralPath "$task\lzma-end-source-v3.tar.gz" -Destination "$out\lzma-end-source-v3.tar.gz"
 Copy-Item -LiteralPath $MyInvocation.MyCommand.Path -Destination "$out\executed-batch.ps1"
 $busy=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -match '^(FileCat.*|dotnet|testhost)(\.exe)?$'}|Select-Object Name,ProcessId,CommandLine)
 @{Processes=$busy;CollectedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 "$out\final-census.json"
 if($busy.Count){throw 'A tester is active; defer final evidence collection.'}
 'PASS: exact LZMA/LZMA2 endings, official SDK differential and affected regressions. Catalog/transaction/UI/qualification remain open.'|Set-Content -Encoding UTF8 "$out\complete.txt"
 $rows=@(Get-ChildItem -LiteralPath $out -File -Recurse|Sort-Object FullName|ForEach-Object {@{path=$_.FullName.Substring($out.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
 @{files=$rows;production_backend_qualified=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$out\files.json"
 & tar -czf "$task\lzma-end-evidence-v3.tar.gz" -C $task lzma-end-v3
 if($LASTEXITCODE){throw 'Evidence archive failed.'}
 @{bytes=(Get-Item "$task\lzma-end-evidence-v3.tar.gz").Length;sha256=(Get-FileHash -LiteralPath "$task\lzma-end-evidence-v3.tar.gz" -Algorithm SHA256).Hash;rows=$rows.Count}|ConvertTo-Json|Set-Content -Encoding UTF8 "$task\lzma-end-evidence-v3.json"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$task\lzma-end-resume-error-v3.txt";exit 1}
