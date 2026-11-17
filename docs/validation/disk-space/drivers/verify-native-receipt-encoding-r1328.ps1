$ErrorActionPreference='Stop'
$receipt=Join-Path $PWD '.tmp/windows-native-receipt-encoding-r1328.json'
if(Test-Path -LiteralPath $receipt){throw 'Fresh encoding receipt required'}
& pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'preflight-indexed-r732.ps1') -Platform windows -Revision r1328encoding
if($LASTEXITCODE){exit $LASTEXITCODE}
$code=@'
$ErrorActionPreference='Stop'
$sample='caf'+[char]0xe9+' '+[char]0xb7+' '+[char]::ConvertFromUtf32(0x1f600)
[ordered]@{sample=$sample;captured=@{location='r';cohort='uncertain'}}|ConvertTo-Json -Depth 30|Set-Content -Encoding UTF8 -LiteralPath 'C:\FileCatDsa20261104R2\batch-r1327\encoding-check.json'
'@
$encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
$vmrun='C:\Program Files\VMware\VMware Workstation\vmrun.exe'
$vm='V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx'
& $vmrun -T ws -vp a -gu filecatdsa_r2 -gp a runProgramInGuest $vm 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -WindowStyle Hidden -EncodedCommand $encoded
if($LASTEXITCODE){throw 'Guest UTF8 encoding check failed'}
& $vmrun -T ws -vp a -gu filecatdsa_r2 -gp a copyFileFromGuestToHost $vm 'C:\FileCatDsa20261104R2\batch-r1327\encoding-check.json' $receipt
if($LASTEXITCODE){throw 'Encoding receipt copy failed'}
$verify=@'
import hashlib,json,pathlib
r=pathlib.Path.cwd();p=r/'.tmp/windows-native-receipt-encoding-r1328.json';raw=p.read_bytes()
data=json.loads(raw.decode('utf-8-sig'));assert data['sample']=='café · 😀'
assert data['captured']==dict(location='r',cohort='uncertain')
proof=r/'.tmp/native-receipt-encoding-proof-r1328.json';assert not proof.exists()
proof.write_text(json.dumps(dict(passed=True,receiptSha256=hashlib.sha256(raw).hexdigest(),actualWindowsPowerShell5=True,explicitUtf8=True,nonAsciiRoundTrip=True),indent=2))
print('Windows PowerShell UTF8 receipt round-trip passed')
'@
$verify|python -
if($LASTEXITCODE){throw 'Exact UTF8 receipt validation failed'}
& pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'preflight-indexed-r732.ps1') -Platform windows -Revision r1328encodingpost
if($LASTEXITCODE-notin 0,75){throw 'Encoding check post-inventory failed'}
