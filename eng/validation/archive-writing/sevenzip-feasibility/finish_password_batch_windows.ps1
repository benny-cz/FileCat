$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297';$out="$task\archive-passwords-v4";$source="$task\archive-passwords-source-v4"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 if(-not(Test-Path -LiteralPath "$out\self-tests-ready.txt") -or -not(Test-Path -LiteralPath "$out\password-oracles-ready.txt")){throw 'The managed or independent password batch has not passed.'}
 if(Test-Path -LiteralPath "$out\files.json"){throw 'Evidence is already frozen.'}
 [void][IO.Directory]::CreateDirectory("$out\tools")
 if((Get-FileHash -LiteralPath "$task\sevenzip-ppmd-filter-tools-v1.tar.gz" -Algorithm SHA256).Hash -ne 'b63159c5ef42c8406b4f6ec8e344a183f1cfd6ba39602c9e268ba81c7caefc2a'){throw 'Private tool packet differs.'}
 & tar -xzf "$task\sevenzip-ppmd-filter-tools-v1.tar.gz" -C "$out\tools"
 if($LASTEXITCODE){throw 'Private test-tool extraction failed.'}
 $manifest=Get-Content -LiteralPath "$out\tools\files.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 foreach($row in $manifest.files){$p=Join-Path "$out\tools" $row.path;if((Get-Item -LiteralPath $p).Length -ne $row.bytes -or (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $row.sha256){throw 'Private test tool differs.'}}
 [void][IO.Directory]::CreateDirectory("$out\current-native")
 $dll="$task\sevenzip-ppmd-filter-v2\current-native\archive-3.8.11.dll"
 if((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne '9E561C2EC6E9F3239BB32284208CD97FB3A83E3EB8C8C797BB7BBC52361BF404'){throw 'Previously qualified current-reader DLL differs.'}
 Copy-Item -LiteralPath $dll -Destination "$out\current-native\archive-3.8.11.dll"
 $sid=(Get-LocalUser -Name 'fcarchive42829297').SID.Value
 & icacls $out /grant "*${sid}:(OI)(CI)F" /T /Q *> "$out\continuation-evidence-acl.log"
 if($LASTEXITCODE){throw 'Owned evidence ACL failed.'}
 $script=@'
$ErrorActionPreference='Stop';$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297';$out="$task\archive-passwords-v4";$source="$task\archive-passwords-source-v4"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 if($identity.Groups.Contains([Security.Principal.SecurityIdentifier]'S-1-5-32-544')){throw 'Actual standard account required.'}
 @{Name=$identity.Name;Sid=$identity.User.Value;AdministratorsMember=$false}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\inherited-oracle-account.json"
 $helpers="$source\eng\validation\archive-writing\sevenzip-feasibility"
 & "$helpers\verify_header_sdk_windows.ps1" -Root "$out\vectors" -Oracle "$out\tools\windows\sdk-oracle.exe" -Tool "$out\tools\windows\7za.exe" -Archives "$out\oracles" *> "$out\sdk-official.log"
 & "$helpers\verify_header_windows.ps1" -Root "$out\oracles" -Source "$helpers\WindowsNativeSevenZipHeaderOracle.cs" -PpmdLibrary "$out\current-native\archive-3.8.11.dll" *> "$out\native-utf8-api.log"
 'PASS'|Set-Content -Encoding UTF8 "$out\inherited-oracles-ready.txt"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$out\inherited-oracle-standard-error.txt";exit 1}
'@
 [IO.File]::WriteAllText("$out\run-inherited-oracles-standard.ps1",$script)
 $credential=New-Object Management.Automation.PSCredential('.\fcarchive42829297',(ConvertTo-SecureString 'a' -AsPlainText -Force))
 $child=Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory C:\Windows\System32 -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$out\run-inherited-oracles-standard.ps1") -Credential $credential -LoadUserProfile -WindowStyle Hidden -RedirectStandardOutput "$out\inherited-oracle-launch.out" -RedirectStandardError "$out\inherited-oracle-launch.err" -Wait -PassThru
 @{Pid=$child.Id;ExitCode=$child.ExitCode}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\inherited-oracle-launch.json"
 if($child.ExitCode){throw 'Standard-account inherited oracle checks failed.'}
 Copy-Item -LiteralPath "$task\password-oracle-tools-v1" -Destination "$out\password-test-tools" -Recurse
 Copy-Item -LiteralPath "$task\password-oracle-tools-v1.tar.gz" -Destination "$out\password-oracle-tools-v1.tar.gz"
 [void][IO.Directory]::CreateDirectory("$out\earlier-attempts")
 foreach($version in @(1,2,3)){
  $old="$task\archive-passwords-v$version";if(-not(Test-Path -LiteralPath $old)){continue}
  $dest="$out\earlier-attempts\v$version";[void][IO.Directory]::CreateDirectory($dest)
  foreach($name in @('tests.log','password-independent.log','standard-error.txt','self-tests-ready.txt')){if(Test-Path -LiteralPath "$old\$name"){Copy-Item -LiteralPath "$old\$name" -Destination "$dest\$name"}}
  if(Test-Path -LiteralPath "$old\results"){Get-ChildItem -LiteralPath "$old\results" -Filter '*.trx' -File|Copy-Item -Destination $dest}
  $packet="$task\archive-passwords-source-v$version.tar.gz"
  if(Test-Path -LiteralPath $packet){@{source_packet=$packet;bytes=(Get-Item -LiteralPath $packet).Length;sha256=(Get-FileHash -LiteralPath $packet -Algorithm SHA256).Hash}|ConvertTo-Json|Set-Content -Encoding UTF8 "$dest\retained-source-identity.json"}
 }
 Copy-Item -LiteralPath $MyInvocation.MyCommand.Path -Destination "$out\executed-finish.ps1"
 $busy=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -match '^(FileCat.*|dotnet|testhost)(\.exe)?$'}|Select-Object Name,ProcessId,CommandLine)
 @{Processes=$busy;CollectedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 "$out\final-census.json"
 if($busy.Count){throw 'A tester is active; defer final evidence collection.'}
 'PASS: password foundation and inherited codec/header checks; production qualification remains open.'|Set-Content -Encoding UTF8 "$out\complete.txt"
 $files=@(Get-ChildItem -LiteralPath $out -Recurse -File)
 if(@(Get-ChildItem -LiteralPath $out -Recurse|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Evidence contains a reparse point.'}
 $rows=@($files|Sort-Object FullName|ForEach-Object {@{path=$_.FullName.Substring($out.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
 @{files=$rows;production_backend_qualified=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$out\files.json"
 & tar -czf "$task\archive-passwords-evidence-v4.tar.gz" -C $task archive-passwords-v4
 if($LASTEXITCODE){throw 'Evidence archive failed.'}
 @{bytes=(Get-Item "$task\archive-passwords-evidence-v4.tar.gz").Length;sha256=(Get-FileHash -LiteralPath "$task\archive-passwords-evidence-v4.tar.gz" -Algorithm SHA256).Hash;rows=$rows.Count}|ConvertTo-Json|Set-Content -Encoding UTF8 "$task\archive-passwords-evidence-v4.json"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$task\archive-passwords-finish-error-v4.txt";exit 1}
