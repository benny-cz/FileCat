param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$BaseRevision,
 [Parameter(Mandatory=$true)][ValidateSet('mac','linux')][string]$Platform,
 [Parameter(Mandatory=$true)][string[]]$Methods,
 [ValidateSet('core','render')][string]$Phase='render'
)
$ErrorActionPreference='Stop'
foreach($method in $Methods){if($method-notmatch'^[A-Za-z0-9_.*]+$'){throw 'Use fully qualified xUnit method patterns'}}
$payload=Join-Path $PWD ('.tmp/batch-'+$BaseRevision+'-'+$Platform+'/'+$Phase)
if(!(Test-Path -LiteralPath $payload)){throw 'The immutable baseline stage is missing'}
$stage=Join-Path $PWD ('.tmp/focused-'+$Platform+'-'+$Revision)
if(Test-Path -LiteralPath $stage){throw 'A fresh focused stage is required'}
New-Item -ItemType Directory -Path $stage|Out-Null
$files=@(Get-ChildItem -LiteralPath $payload -File -Recurse|ForEach-Object{[ordered]@{path=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
$remoteRoot=if($Platform-eq'mac'){'/Users/benny'}else{'/home/benny'}
$plan=[ordered]@{revision=$Revision;baseRevision=$BaseRevision;payload=$remoteRoot+'/FileCatValidation/dsa-batch-20261107-'+$BaseRevision+'/payload/'+$Phase;assembly=if($Phase-eq'core'){'FileCat.Core.Tests.dll'}else{'FileCat.Rendering.Tests.dll'};methods=$Methods;files=$files}
$plan|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $stage 'plan.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-focused-unix.py') -Destination (Join-Path $stage 'run.py')
$archive=Join-Path $PWD ('.tmp/dsa-focused-'+$Platform+'-'+$Revision+'.tgz')
& tar.exe -czf $archive -C $stage .
if($LASTEXITCODE){throw 'Focused driver archive failed'}
Write-Output ([ordered]@{archive=$archive;bytes=(Get-Item -LiteralPath $archive).Length;verifiedBaselineFiles=$files.Count;methods=$Methods}|ConvertTo-Json -Depth 4)
