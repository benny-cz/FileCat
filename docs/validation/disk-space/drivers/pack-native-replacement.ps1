param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+$')][string]$Revision)
$ErrorActionPreference='Stop'
$managed=Join-Path $PWD 'tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0'
$watch=[Diagnostics.Stopwatch]::StartNew()
$records=@()
foreach($platform in 'mac','linux','windows'){
 $stage=Join-Path $PWD ('.tmp/desktop-'+$Revision+'-'+$platform)
 if(Test-Path -LiteralPath $stage){throw 'Use a fresh native desktop stage'}
 New-Item -ItemType Directory -Path $stage|Out-Null
 # Reuse only known RID launchers/native dependencies; replace ALL managed inputs.
 Get-ChildItem -LiteralPath (Join-Path $PWD ('.tmp/desktop-r95-'+$platform))|Copy-Item -Destination $stage -Recurse
 Get-ChildItem -LiteralPath $managed -File|Where-Object{$_.Extension -in '.dll','.pdb','.json'}|Copy-Item -Destination $stage
 if(Test-Path -LiteralPath (Join-Path $stage 'native-validation')){throw 'Fixture data cannot be packaged'}
 $archive=Join-Path $PWD ('.tmp/desktop-'+$Revision+'-'+$platform+'.tgz')
 & tar.exe -czf $archive -C $stage .
 if($LASTEXITCODE){throw 'Native desktop archive failed'}
 $records+=@{platform=$platform;archive=$archive;bytes=(Get-Item -LiteralPath $archive).Length;sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()}
}
@{revision=$Revision;elapsedSeconds=$watch.Elapsed.TotalSeconds;platforms=$records}|ConvertTo-Json -Depth 6|Set-Content ('.tmp/pack-native-'+$Revision+'.json')
$records|Select-Object platform,bytes,sha256
