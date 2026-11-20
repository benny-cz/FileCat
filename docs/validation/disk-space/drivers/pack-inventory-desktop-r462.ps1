$ErrorActionPreference='Stop'
$managed=Join-Path $PWD 'tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0'
foreach($platform in 'mac','linux','windows'){
 $stage=Join-Path $PWD ('.tmp/desktop-r462-'+$platform)
 if(Test-Path -LiteralPath $stage){throw 'Use a fresh desktop stage'}
 New-Item -ItemType Directory -Path $stage|Out-Null
 # The .NET apphost is a generic RID-native launcher for DiskSpaceHarness.dll. Preserve the
 # known runner/native dependency set; replace every managed assembly/config from this build.
 Get-ChildItem -LiteralPath (Join-Path $PWD ('.tmp/desktop-r95-'+$platform))|Copy-Item -Destination $stage -Recurse
 Get-ChildItem -LiteralPath $managed -File|Where-Object{$_.Extension -in '.dll','.pdb','.json'}|Copy-Item -Destination $stage
 $archive=Join-Path $PWD ('.tmp/desktop-r462-'+$platform+'.tgz')
 & tar.exe -czf $archive -C $stage .;if($LASTEXITCODE){throw 'Desktop archive failed'}
 Write-Output ($platform+' '+(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant())
}
