$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297';$out="$task\archive-passwords-v4";$source="$task\archive-passwords-source-v4";$tools="$task\password-oracle-tools-v1"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 if(-not(Test-Path -LiteralPath "$out\self-tests-ready.txt")){throw 'The combined self-tests have not passed.'}
 if(Test-Path -LiteralPath $tools){throw 'Fresh private-oracle destination already exists.'}
 if((Get-FileHash -LiteralPath "$task\password-oracle-tools-v1.tar.gz" -Algorithm SHA256).Hash -ne '7733129e7dc3caef86e5f2f4f0b90af3edbc674ae706190c52e533d4fed3ee16'){throw 'Oracle packet differs.'}
 [void][IO.Directory]::CreateDirectory($tools)
 & tar -xzf "$task\password-oracle-tools-v1.tar.gz" -C $tools
 if($LASTEXITCODE){throw 'Oracle packet extraction failed.'}
 $manifest=Get-Content -LiteralPath "$tools\files.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 foreach($row in $manifest.files){$path=Join-Path $tools $row.path;if((Get-Item $path).Length -ne $row.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $row.sha256){throw 'Oracle file differs.'}}
 $runtime=Get-Content -LiteralPath "$tools\python-runtime.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 Expand-Archive -LiteralPath "$tools\$($runtime.filename)" -DestinationPath "$tools\python"
 $pth=Get-ChildItem -LiteralPath "$tools\python" -Filter '*._pth' -File
 if(@($pth).Count -ne 1){throw 'Unexpected private runtime path configuration.'}
 [IO.File]::AppendAllText($pth.FullName,"`n..\modules`n",[Text.Encoding]::ASCII)
 & "$tools\python\python.exe" -B "$source\eng\validation\archive-writing\sevenzip-feasibility\install_password_oracle_modules.py" $tools windows *> "$out\oracle-module-setup.log"
 if($LASTEXITCODE){throw 'Private wheel setup failed.'}
 $setup=Start-Process -FilePath "$tools\7z2605-x64.exe" -ArgumentList @('/S',"/D=$tools\7zip") -WindowStyle Hidden -Wait -PassThru
 if($setup.ExitCode -or -not(Test-Path -LiteralPath "$tools\7zip\7z.exe")){throw 'Private official 7-Zip setup failed.'}
 $sid=(Get-LocalUser -Name 'fcarchive42829297').SID.Value
 & icacls $tools /grant "*${sid}:(OI)(CI)RX" /T /Q *> "$out\oracle-tool-acl.log"
 if($LASTEXITCODE){throw 'Private tool ACL failed.'}
 $script=@'
$ErrorActionPreference='Stop';$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297';$out="$task\archive-passwords-v4";$source="$task\archive-passwords-source-v4";$tools="$task\password-oracle-tools-v1"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 if($identity.Groups.Contains([Security.Principal.SecurityIdentifier]'S-1-5-32-544')){throw 'Actual standard account required.'}
 @{Name=$identity.Name;Sid=$identity.User.Value;AdministratorsMember=$false}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\password-oracle-account.json"
 & "$tools\python\python.exe" -B "$source\eng\validation\archive-writing\sevenzip-feasibility\verify_password_archives.py" "$out\password-oracles" "$tools\7zip\7z.exe" "$tools\modules" *> "$out\password-independent.log"
 if($LASTEXITCODE){throw 'Independent password readers failed.'}
 'PASS'|Set-Content -Encoding UTF8 "$out\password-oracles-ready.txt"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$out\password-oracle-standard-error.txt";exit 1}
'@
 [IO.File]::WriteAllText("$out\run-password-oracles-standard.ps1",$script)
 $credential=New-Object Management.Automation.PSCredential('.\fcarchive42829297',(ConvertTo-SecureString 'a' -AsPlainText -Force))
 $child=Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory C:\Windows\System32 -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$out\run-password-oracles-standard.ps1") -Credential $credential -LoadUserProfile -WindowStyle Hidden -RedirectStandardOutput "$out\password-oracle-launch.out" -RedirectStandardError "$out\password-oracle-launch.err" -Wait -PassThru
 @{Pid=$child.Id;ExitCode=$child.ExitCode}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\password-oracle-launch.json"
 if($child.ExitCode){throw 'Standard-account independent password verification failed.'}
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$task\password-oracle-setup-error-v1.txt";exit 1}
