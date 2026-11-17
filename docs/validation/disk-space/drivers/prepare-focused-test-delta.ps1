param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$BaselineRevision,
 [Parameter(Mandatory=$true)][string[]]$Methods,
 [ValidatePattern('^r[0-9]+[a-z]?$')][string]$WindowsBaselineRevision,
 [ValidateSet('core','render')][string]$Phase='render'
)
$ErrorActionPreference='Stop'
foreach($method in $Methods){if($method-notmatch'^[A-Za-z0-9_.*]+$'){throw 'Use fully qualified method patterns'}}
$assembly=if($Phase-eq'core'){'FileCat.Core.Tests'}else{'FileCat.Rendering.Tests'}
$bin=Join-Path $PWD ('tests/'+$assembly+'/bin/Debug/net10.0')
$proof=@()
foreach($platform in 'windows','mac','linux'){
 $baseline=Join-Path $PWD ('.tmp/batch-'+$BaselineRevision+'-'+$platform+'/'+$Phase)
 $stage=Join-Path $PWD ('.tmp/focused-delta-'+$platform+'-'+$Revision)
 if(Test-Path -LiteralPath $stage){throw 'Fresh focused delta stage required'}
 New-Item -ItemType Directory -Path $stage|Out-Null
 $files=@(Get-ChildItem -LiteralPath $baseline -File -Recurse|ForEach-Object{
  $relative=[IO.Path]::GetRelativePath($baseline,$_.FullName).Replace('\','/')
  $current=Join-Path $bin $relative
  $old=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();$new=(Get-FileHash -LiteralPath $current -Algorithm SHA256).Hash.ToLowerInvariant()
  $patch=$old-ne$new
  if($patch){
   if($relative-notin ($assembly+'.dll'),($assembly+'.pdb')){throw ('Production input changed; a broader batch is required: '+$relative)}
   $target=Join-Path $stage ('patch/'+$relative);New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null;Copy-Item -LiteralPath $current -Destination $target
  }
  [ordered]@{path=$relative;baselineSha256=$old;sha256=$new;patch=$patch}
 })
 $guestRevision=if($platform-eq'windows'-and$WindowsBaselineRevision){$WindowsBaselineRevision}else{$BaselineRevision}
 $remote=if($platform-eq'windows'){'C:\FileCatDsa20261104R2\batch-'+$guestRevision+'\payload\'+$Phase}elseif($platform-eq'mac'){'/Users/benny/FileCatValidation/dsa-batch-20261107-'+$BaselineRevision+'/payload/'+$Phase}else{'/home/benny/FileCatValidation/dsa-batch-20261107-'+$BaselineRevision+'/payload/'+$Phase}
 [ordered]@{revision=$Revision;baseline=$remote;assembly=$assembly+'.dll';methods=$Methods;files=$files}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $stage 'plan.json')
 if($platform-eq'windows'){
  $template=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'run-focused-test-delta.ps1')).Replace('@BASELINE@',$remote).Replace('@REV@',$Revision)
  [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')),$template,[Text.UTF8Encoding]::new($false))
 }else{Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-focused-test-delta.py') -Destination (Join-Path $stage 'run.py')}
 $archive=Join-Path $PWD ('.tmp/dsa-batch-'+$platform+'-'+$Revision+'.tgz')
 & tar.exe -czf $archive -C $stage .;if($LASTEXITCODE){throw 'Focused delta archive failed'}
 $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
 if($platform-eq'windows'){
  $path=Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1');$runner=[IO.File]::ReadAllText($path).Replace('@HASH@',$hash);[IO.File]::WriteAllText($path,$runner,[Text.UTF8Encoding]::new($false))
 }else{
  $runner=@'
set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != '@HASH@' ]; then printf 'FAILED phase=integrity\n' > state.txt; exit 74; fi
umask 077
mkdir driver
tar -xzf bundle.tgz -C driver
python3 driver/run.py
'@
  [IO.File]::WriteAllText((Join-Path $PWD ('.tmp/batch-'+$platform+'-'+$Revision+'.sh')),$runner.Replace('@HASH@',$hash).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
 }
 $proof+=@{platform=$platform;baseline=$BaselineRevision;guestBaseline=$guestRevision;verifiedFiles=$files.Count;changed=@($files|Where-Object{$_.patch}|ForEach-Object{$_.path});bytes=(Get-Item -LiteralPath $archive).Length;sha256=$hash}
}
$proof|ConvertTo-Json -Depth 5|Set-Content ('.tmp/focused-delta-proof-'+$Revision+'.json')
$proof|Select-Object platform,verifiedFiles,bytes|Format-Table
