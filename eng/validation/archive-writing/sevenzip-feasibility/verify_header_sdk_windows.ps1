param([string]$Root,[string]$Oracle,[string]$Tool,[string]$Archives)
$ErrorActionPreference='Stop';$verified=@();$files=@(Get-ChildItem -LiteralPath $Root -Filter '*.input' -File)
if($files.Count -ne 19){throw 'Independent SDK vector count differs.'}
foreach($file in $files){
 $parts=$file.BaseName.Split('-');$output=[IO.Path]::ChangeExtension($file.FullName,'.sdk')
 if(-not(Test-Path -LiteralPath $output)){
  if($parts[0] -eq 'Ppmd7'){& $Oracle ppmd model $file.FullName $output $parts[1] $parts[2]}
  else{& $Oracle filter $parts[0] $file.FullName $output $parts[1] 0}
  if($LASTEXITCODE){throw "Independent SDK execution failed: $($file.BaseName)"}
 }
 $expected=[IO.Path]::ChangeExtension($file.FullName,'.encoded')
 $hash=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
 if($hash -ne (Get-FileHash -LiteralPath $expected -Algorithm SHA256).Hash -or (Get-Item $output).Length -ne (Get-Item $expected).Length){throw "SDK bytes differ: $($file.BaseName)"}
 $verified+=@{name=$file.BaseName;encoded_bytes=(Get-Item $output).Length;sha256=$hash}
}
@{sdk_commit='b88c6bc09339de32f7d18bbab900686b244f42ba';oracle_sha256=(Get-FileHash -LiteralPath $Oracle -Algorithm SHA256).Hash;verified=$verified;production_dependency=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$Root\sdk-reader.json"
# Native process stdout stays binary: PowerShell's pipeline would decode/corrupt arbitrary payload bytes.
& $Tool i *> "$Archives\official-tool-info.txt"
if($LASTEXITCODE){throw 'Official test tool did not start.'}
$folders=@(Get-ChildItem -LiteralPath $Archives -Directory|Where-Object {Test-Path -LiteralPath "$($_.FullName)\members.json"})
if($folders.Count -ne 30){throw 'Official archive fixture count differs.'}
$payloads=@();$unsupported=@()
foreach($folder in $folders){
 $archive="$($folder.FullName)\members.7z";$rows=Get-Content -LiteralPath "$($folder.FullName)\members.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 # PowerShell 5 treats native stderr as error records under Stop; collect the exit status explicitly.
 $ErrorActionPreference='Continue'
 & $Tool t -bd -y $archive *> "$($folder.FullName)\official-test-v2.log"
 $toolCode=$LASTEXITCODE;$ErrorActionPreference='Stop'
 if($folder.Name -in @('codec-5','compressed-codec-5')){
  $text=Get-Content -LiteralPath "$($folder.FullName)\official-test-v2.log" -Raw
  if($toolCode -ne 2 -or $text -notlike '*Unsupported Method*'){throw 'The expected 7z Zstandard extension limitation changed.'}
  $unsupported+=@{fixture=$folder.Name;method_id='04f71101';reason='not registered by official 7-Zip 26.05'}
  continue
 }
 if($toolCode){throw "Official archive test failed: $($folder.Name)"}
 foreach($row in $rows){
  if($row.directory){continue}
  $start=New-Object Diagnostics.ProcessStartInfo
  $start.FileName=$Tool;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
  if($archive.Contains('"') -or $row.name.Contains('"')){throw 'Fixture quoting scope exceeded.'}
  $start.Arguments='x -so -bd -y "'+$archive+'" -- "'+$row.name+'"'
  $child=New-Object Diagnostics.Process;$child.StartInfo=$start;[void]$child.Start()
  $hash=[Security.Cryptography.SHA256]::Create();$count=0;$buffer=New-Object byte[] 65536
  try{
   $errorTask=$child.StandardError.ReadToEndAsync()
   while(($got=$child.StandardOutput.BaseStream.Read($buffer,0,$buffer.Length)) -gt 0){
    $count+=$got;if($count -gt $row.length -or $count -gt (3 -shl 20)){$child.Kill();throw 'Independent payload exceeds bound.'}
    [void]$hash.TransformBlock($buffer,0,$got,$null,0)
   }
   [void]$hash.TransformFinalBlock((New-Object byte[] 0),0,0)
   if(-not $child.WaitForExit(120000)){$child.Kill();throw 'Independent process exceeded deadline.'}
   $errorText=$errorTask.Result
   $actual=[BitConverter]::ToString($hash.Hash).Replace('-','')
   if($child.ExitCode -or $count -ne $row.length -or $actual -ne $row.sha256){throw "Independent official payload differs: $($folder.Name) $($row.name) $errorText"}
   $payloads+=@{fixture=$folder.Name;name=$row.name;bytes=$count;sha256=$actual}
  }finally{$hash.Dispose();$child.Dispose()}
 }
}
@{tool_sha256=(Get-FileHash -LiteralPath $Tool -Algorithm SHA256).Hash;tested_archives=($folders.Count-$unsupported.Count);unsupported=$unsupported;verified=$payloads;production_dependency=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$Archives\official-reader.json"
Write-Output "PASS: 19 independent SDK vectors; $($folders.Count-$unsupported.Count) official archive tests; $($payloads.Count) exact payloads; $($unsupported.Count) explicit unsupported extension."
