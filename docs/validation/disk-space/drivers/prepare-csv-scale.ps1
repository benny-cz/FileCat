param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,[Parameter(Mandatory=$true)][string]$BuildLog)
$ErrorActionPreference='Stop'
$build=Get-Content -LiteralPath $BuildLog -Raw
foreach($value in @('Build succeeded.','0 Warning(s)','0 Error(s)')){if(!$build.Contains($value)){throw 'Clean completed build required'}}
$stage=Join-Path $PWD ('.tmp/csv-scale-'+$Revision);if(Test-Path -LiteralPath $stage){throw 'Fresh scale stage required'}
$bin=Join-Path $PWD 'tests/FileCat.CoreCrashHarness/bin/Debug/net10.0'
$known=Join-Path $PWD '.tmp/batch-r1096-windows/core/FileCat.Core.dll'
if(@(git diff --name-only HEAD -- src).Count-or@(git ls-files --others --exclude-standard -- src).Count){throw 'Scale cannot reuse qualified products after an unqualified product-source change'}
if([Reflection.AssemblyName]::GetAssemblyName((Join-Path $bin 'FileCat.Core.dll')).FullName-cne[Reflection.AssemblyName]::GetAssemblyName($known).FullName){throw 'The scale harness references another Core assembly identity'}
New-Item -ItemType Directory -Path $stage|Out-Null
Get-ChildItem -LiteralPath $bin -File|Copy-Item -Destination $stage
# Committing the qualified source changes generated Git build metadata. Keep the
# exact qualified product DLL/PDB; the new harness uses the same assembly identity.
Copy-Item -LiteralPath $known -Destination $stage -Force
Copy-Item -LiteralPath '.tmp/batch-r1096-windows/core/FileCat.Core.pdb' -Destination $stage -Force
Copy-Item -LiteralPath '.tmp/batch-r1096-windows/native/FileCat.Platform.Windows.dll' -Destination $stage
$inputs=@(Get-ChildItem -LiteralPath $stage -File|Sort-Object Name|ForEach-Object{[ordered]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();bytes=$_.Length}})
$inputs|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $stage 'inputs.json')
$archive=Join-Path $PWD ('.tmp/csv-scale-'+$Revision+'.tgz');if(Test-Path -LiteralPath $archive){throw 'Fresh scale archive required'}
& tar.exe -czf $archive -C $stage .;if($LASTEXITCODE){throw 'Scale archive failed'}
$source=@('tests/FileCat.CoreCrashHarness/Program.cs','tests/FileCat.CoreCrashHarness/CsvReportScale.cs')
[ordered]@{revision=$Revision;qualifiedProductRevision='r1096';gitBase=(git rev-parse HEAD);buildLog=$BuildLog;archive=$archive;sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $archive).Length;inputs=$inputs;
 sources=@($source|ForEach-Object{[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()}})}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath ('.tmp/csv-scale-manifest-'+$Revision+'.json')
Write-Output ('Prepared one portable immutable scale bundle: '+(Get-Item -LiteralPath $archive).Length+' bytes')
