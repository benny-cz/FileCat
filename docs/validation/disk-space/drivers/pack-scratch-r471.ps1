$ErrorActionPreference='Stop'
foreach($spec in @(@('core','tests/FileCat.Core.Tests/bin/Debug/net10.0'),@('native','tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0'))){
 $kind=$spec[0];$stage=Join-Path $PWD ('.tmp/'+$kind+'-r471')
 if(Test-Path -LiteralPath $stage){throw 'Use a fresh stage'}
 New-Item -ItemType Directory -Path $stage|Out-Null
 Get-ChildItem -LiteralPath (Join-Path $PWD $spec[1]) -File|Copy-Item -Destination $stage
 $archive=Join-Path $PWD ('.tmp/dsa-'+$kind+'-r471.tgz')
 & tar.exe -czf $archive -C $stage .;if($LASTEXITCODE){throw 'Archive failed'}
 $hash=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
 if($kind -eq 'core'){
  foreach($pair in @(@('windows-core-r84.ps1','windows-core-r471.ps1'),@('unix-core-r84.sh','unix-core-r471.sh'))){
   $script=[IO.File]::ReadAllText((Join-Path $PWD ('.tmp/'+$pair[0]))).Replace('r84','r471') -replace '[0-9a-f]{64}',$hash
   [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/'+$pair[1])),$script,[Text.UTF8Encoding]::new($false))
  }
 }else{
  $script=[IO.File]::ReadAllText((Join-Path $PWD '.tmp/windows-native-tests-r81.ps1')).Replace('r81','r471') -replace '[0-9a-f]{64}',$hash
  [IO.File]::WriteAllText((Join-Path $PWD '.tmp/windows-native-tests-r471.ps1'),$script,[Text.UTF8Encoding]::new($false))
 }
 Write-Output ($kind+' '+$hash)
}
