param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$BaselineRevision,
 [Parameter(Mandatory=$true)][string]$SourceManifest
)
$ErrorActionPreference='Stop'
foreach($entry in (Get-Content -LiteralPath $SourceManifest -Raw | ConvertFrom-Json)){
 if((Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash.ToLowerInvariant()-ne$entry.sha256){throw 'Frozen source changed'}
}
$stage=Join-Path $PWD ('.tmp/batch-'+$BaselineRevision+'-windows')
$input=Join-Path $PWD ('.tmp/cache-input-'+$Revision)
if(Test-Path -LiteralPath $input){throw 'Fresh cache plan directory required'}
New-Item -ItemType Directory -Path $input|Out-Null
$assemblies=@{core='FileCat.Core.Tests';native='FileCat.Platform.Windows.Tests';render='FileCat.Rendering.Tests'}
$files=@(Get-ChildItem -LiteralPath $stage -File -Recurse|ForEach-Object{
 $relative=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/')
 $parts=$relative.Split('/',2)
 $current=Join-Path $PWD ('tests/'+$assemblies[$parts[0]]+'/bin/Debug/net10.0/'+$parts[1])
 $hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 if((Get-FileHash -LiteralPath $current -Algorithm SHA256).Hash.ToLowerInvariant()-ne$hash){throw ('Built input changed: '+$relative)}
 [ordered]@{path=$relative;sha256=$hash}
})
$baseline='C:\FileCatDsa20261104R2\batch-'+$BaselineRevision+'\payload'
[ordered]@{baseline=$baseline;files=$files}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $input 'plan.json')
$archive=Join-Path $PWD ('.tmp/dsa-batch-windows-'+$Revision+'.tgz')
if(Test-Path -LiteralPath $archive){throw 'Fresh archive required'}
& tar.exe -czf $archive -C $input .
if($LASTEXITCODE){throw 'Cache plan archive failed'}
$runner=[IO.File]::ReadAllText((Join-Path $PWD '.tmp/batch-windows-grouping-template.ps1'))
$start=$runner.IndexOf(" `$payload=Join-Path `$base 'payload'")
$finish=$runner.IndexOf(' $overall=0')
if($start-lt0-or$finish-le$start){throw 'Unexpected extraction boundary'}
$reuse=@'
 $control=Join-Path $base 'cache-input'
 New-Item -ItemType Directory -Path $control|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $control
 if($LASTEXITCODE){throw 'Cache plan extraction failed'}
 $plan=Get-Content -LiteralPath (Join-Path $control 'plan.json') -Raw|ConvertFrom-Json
 if($plan.baseline-cne'@BASELINE@'){throw 'Unexpected cached baseline'}
 $payload=$plan.baseline
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains('..')-or$item.path.Contains(':')){throw 'Unsafe cached path'}
  $file=Join-Path $payload $item.path
  if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()-cne$item.sha256){throw ('Cached input changed: '+$item.path)}
 }
 ('VERIFIED cachedFiles='+$plan.files.Count+' changedFiles=0')|Set-Content (Join-Path $base 'cache-proof.txt')
'@
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$runner=$runner.Substring(0,$start)+$reuse.Replace('@BASELINE@',$baseline)+"`n"+$runner.Substring($finish)
[IO.File]::WriteAllText((Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')),$runner.Replace('@HASH@',$hash).Replace('@REV@',$Revision).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
[ordered]@{revision=$Revision;baseline=$BaselineRevision;verifiedInputs=$files.Count;changedInputs=0;archiveBytes=(Get-Item -LiteralPath $archive).Length;sha256=$hash}|ConvertTo-Json|Set-Content ('.tmp/cache-proof-'+$Revision+'.json')
Write-Output ('Reusing '+$files.Count+' unchanged Windows inputs; transfer '+(Get-Item -LiteralPath $archive).Length+' bytes.')
