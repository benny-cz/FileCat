param([Parameter(Mandatory=$true)][string]$PacketSha256)
$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297'
$root="$task\sevenzip-bcj2-supplement-source-v3";$out="$task\sevenzip-bcj2-supplement-v3"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 if((Test-Path -LiteralPath $root) -or (Test-Path -LiteralPath $out)){throw 'Fresh supplement paths already exist.'}
 if((Get-FileHash -LiteralPath "$task\sevenzip-bcj2-supplement-source-v3.tar.gz" -Algorithm SHA256).Hash -ne $PacketSha256){throw 'Supplement source differs.'}
 [void][IO.Directory]::CreateDirectory($root);[void][IO.Directory]::CreateDirectory($out)
 & tar -xzf "$task\sevenzip-bcj2-supplement-source-v3.tar.gz" -C $root
 if($LASTEXITCODE){throw 'Source extraction failed.'}
 Set-Location $root;$dotnet="$task\dotnet\dotnet.exe";$env:MSBUILDDISABLENODEREUSE='1'
 foreach($project in @('src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj')){
  $name=[IO.Path]::GetFileNameWithoutExtension($project)
  & $dotnet build $project -c Release --disable-build-servers -p:NuGetAudit=false -m:1 -v minimal *> "$out\$name-build.log"
  if($LASTEXITCODE){throw "Build failed: $project"}
 }
 $sid=(Get-LocalUser -Name 'fcarchive42829297').SID.Value
 foreach($directory in @($root,$out)){
  & icacls $directory /grant "*${sid}:(OI)(CI)F" /T /Q *> "$out\$([IO.Path]::GetFileName($directory))-acl.log"
  if($LASTEXITCODE){throw 'Owned supplement ACL failed.'}
 }
 foreach($file in @("$task\sevenzip-bcj2-supplement-source-v3.tar.gz","$task\sdk-bcj2-decode-oracle-v1.exe")){
  & icacls $file /grant "*${sid}:RX" *> "$out\$([IO.Path]::GetFileName($file))-acl.log"
  if($LASTEXITCODE){throw 'Private supplement input access failed.'}
 }
 $log=[IO.File]::Open("$task\bcj2-supplement-python.log",[IO.FileMode]::CreateNew);$log.Dispose()
 & icacls "$task\bcj2-supplement-python.log" /grant "*${sid}:M" *> "$out\external-log-acl.log"
 if($LASTEXITCODE){throw 'Private supplement log access failed.'}
 $PacketSha256|Set-Content -Encoding UTF8 "$out\source-packet-sha256.txt"
 $script=@'
$ErrorActionPreference='Stop';$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297'
$root="$task\sevenzip-bcj2-supplement-source-v3";$out="$task\sevenzip-bcj2-supplement-v3"
try {
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 if($identity.Groups.Contains([Security.Principal.SecurityIdentifier]'S-1-5-32-544')){throw 'Actual standard account required.'}
 if(-not(Test-Path "Registry::HKEY_USERS\$($identity.User.Value)")){throw 'Standard profile is not loaded.'}
 @{Name=$identity.Name;Sid=$identity.User.Value;AdministratorsMember=$false;HiveLoaded=$true}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\account.json"
 $packet=(Get-Content -LiteralPath "$out\source-packet-sha256.txt" -Encoding UTF8 -Raw).Trim()
 $env:TEMP=Join-Path $env:USERPROFILE 'AppData\Local\Temp';$env:TMP=$env:TEMP
 $ErrorActionPreference='Continue'
 & "$task\password-oracle-tools-v1\python\python.exe" -B "$root\eng\validation\archive-writing\sevenzip-feasibility\run_bcj2_supplement.py" $task $packet '--windows-prepared' *> "$task\bcj2-supplement-python.log"
 $code=$LASTEXITCODE;$ErrorActionPreference='Stop';if($code){throw 'Supplement failed.'}
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$out\standard-error.txt";exit 1}
'@
 [IO.File]::WriteAllText("$out\run-standard.ps1",$script)
 $credential=New-Object Management.Automation.PSCredential('.\fcarchive42829297',(ConvertTo-SecureString 'a' -AsPlainText -Force))
 $child=Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory 'C:\Windows\System32' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$out\run-standard.ps1") -Credential $credential -LoadUserProfile -WindowStyle Hidden -RedirectStandardOutput "$task\bcj2-supplement-standard-launch.out" -RedirectStandardError "$task\bcj2-supplement-standard-launch.err" -Wait -PassThru
 if($child.ExitCode){throw 'Standard-user supplement failed.'}
 & "$root\eng\validation\archive-writing\sevenzip-feasibility\finish_bcj2_supplement_windows.ps1"
 if($LASTEXITCODE){throw 'Supplement evidence finalization failed.'}
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$task\sevenzip-bcj2-supplement-error-v3.txt";exit 1}
