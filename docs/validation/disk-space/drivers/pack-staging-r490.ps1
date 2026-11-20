$ErrorActionPreference='Stop'
foreach($spec in @(@('core','tests/FileCat.Core.Tests/bin/Debug/net10.0'),@('native','tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0'))){
 $kind=$spec[0];$stage=Join-Path $PWD ('.tmp/'+$kind+'-r490');if(Test-Path -LiteralPath $stage){throw 'Fresh stage required'}
 New-Item -ItemType Directory -Path $stage|Out-Null
 Get-ChildItem -LiteralPath (Join-Path $PWD $spec[1]) -File|Copy-Item -Destination $stage
 $archive=Join-Path $PWD ('.tmp/dsa-'+$kind+'-r490.tgz');& tar.exe -czf $archive -C $stage .;if($LASTEXITCODE){throw 'Archive failed'}
 $hash=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
 $templates=if($kind-eq'core'){@('unix-core-r468.sh','windows-core-r468.ps1')}else{@('windows-native-tests-r471.ps1')}
 foreach($name in $templates){
  $revision=if($kind-eq'core'){'r468'}else{'r471'}
  $script=[IO.File]::ReadAllText((Join-Path $PWD ('.tmp/'+$name))).Replace($revision,'r490') -replace '[0-9a-f]{64}',$hash
  [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/'+$name.Replace($revision,'r490'))),$script,[Text.UTF8Encoding]::new($false))
 }
 Write-Output ($kind+' '+$hash)
}
& .tmp/pack-render.ps1 -Revision r491
foreach($platform in 'mac','linux','windows'){
 $hash=(Get-FileHash ('.tmp/dsa-render-'+$platform+'-r491.tgz')).Hash.ToLowerInvariant()
 $name=if($platform-eq'windows'){'windows-render-r472.ps1'}else{'unix-render-'+$platform+'-r472.sh'}
 $script=[IO.File]::ReadAllText((Join-Path $PWD ('.tmp/'+$name))).Replace('r472','r491') -replace '[0-9a-f]{64}',$hash
 [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/'+$name.Replace('r472','r491'))),$script,[Text.UTF8Encoding]::new($false))
}
