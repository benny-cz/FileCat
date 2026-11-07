$ErrorActionPreference='Stop'
$task='C:\Users\admin\FileCatValidation\ArchiveWriting-42829297'
$root="$task\sevenzip-bcj2-supplement-source-v3";$out="$task\sevenzip-bcj2-supplement-v3"
try {
 if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){throw 'Another tester is active; postpone.'}
 if(Test-Path -LiteralPath "$out\complete.txt"){throw 'Supplement is already complete.'}
 $packet=(Get-Content -LiteralPath "$out\source-packet-sha256.txt" -Encoding UTF8 -Raw).Trim()
 if((Get-FileHash -LiteralPath "$task\sevenzip-bcj2-supplement-source-v3.tar.gz" -Algorithm SHA256).Hash -ne $packet){throw 'Source packet differs.'}
 [xml]$trx=Get-Content -LiteralPath "$out\results\bcj2-supplement.trx" -Encoding UTF8 -Raw
 $ns=New-Object Xml.XmlNamespaceManager($trx.NameTable);$ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
 $results=@($trx.SelectNodes('//t:UnitTestResult',$ns))
 if($results.Count -ne 81 -or @($results|Where-Object {$_.outcome -ne 'Passed'}).Count){throw 'The 81 focused tests must already pass.'}
 $graphs=Get-Content -LiteralPath "$out\independent-graphs\graph-bcj2.json" -Encoding UTF8 -Raw|ConvertFrom-Json
 if($graphs.verified.Count -ne 42){throw 'The independent graphs must already pass.'}
 $audit="$root\eng\validation\archive-writing\PdbSourceAudit\bin\Release\net10.0\PdbSourceAudit.dll"
 # Test assemblies include xUnit-generated source under the administrator's restore cache. The tested application
 # remains standard-user; this read-only compiler/evidence audit needs the account that owns those source files.
 & "$task\dotnet\dotnet.exe" $audit $root "$out\binaries" "$out\compiler-sources" 'C:\Users\Admin\.nuget\packages' *> "$out\source-audit-parent.log"
 if($LASTEXITCODE){throw 'Compiler-source checksums differ.'}
 Copy-Item -LiteralPath "$task\sevenzip-bcj2-supplement-source-v3.tar.gz" -Destination "$out\sevenzip-bcj2-supplement-source-v3.tar.gz"
 Copy-Item -LiteralPath "$root\eng\validation\archive-writing\sevenzip-feasibility\run_bcj2_supplement.py" -Destination "$out\executed-batch.py"
 Copy-Item -LiteralPath $MyInvocation.MyCommand.Path -Destination "$out\executed-finalization.ps1"
 if(Test-Path -LiteralPath "$task\bcj2-supplement-resume-python.log"){Copy-Item -LiteralPath "$task\bcj2-supplement-resume-python.log" -Destination "$out\standard-python-startup-and-audit-failures.log"}
 $busy=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -match '^(FileCat.*|dotnet|testhost)(\.exe)?$'}|Select-Object Name,ProcessId,CommandLine)
 if($busy.Count){throw 'Another tester is active; defer evidence freezing.'}
 @{owned_filecat_or_test_processes=0;CollectedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -Encoding UTF8 "$out\final-census.json"
 'PASS: 81 focused BCJ2 tests and 42 independently decoded graph payloads. Full production qualification remains open.'|Set-Content -Encoding UTF8 "$out\complete.txt"
 $rows=@(Get-ChildItem -LiteralPath $out -File -Recurse|Sort-Object FullName|ForEach-Object {@{path=$_.FullName.Substring($out.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
 @{files=$rows;production_backend_qualified=$false}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$out\files.json"
 & tar -czf "$task\sevenzip-bcj2-supplement-evidence-v3.tar.gz" -C $task sevenzip-bcj2-supplement-v3
 if($LASTEXITCODE){throw 'Evidence archive failed.'}
 @{bytes=(Get-Item "$task\sevenzip-bcj2-supplement-evidence-v3.tar.gz").Length;sha256=(Get-FileHash -LiteralPath "$task\sevenzip-bcj2-supplement-evidence-v3.tar.gz" -Algorithm SHA256).Hash;rows=$rows.Count}|ConvertTo-Json|Set-Content -Encoding UTF8 "$task\sevenzip-bcj2-supplement-evidence-v3.json"
}catch{$_|Out-String|Set-Content -Encoding UTF8 "$task\sevenzip-bcj2-supplement-finalization-error-v1.txt";exit 1}
