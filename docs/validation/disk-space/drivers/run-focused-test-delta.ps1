$ErrorActionPreference='Stop'
$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-Process -Name dotnet -ErrorAction SilentlyContinue).Count }
if(Busy){'BUSY phase=verify'|Set-Content $state;exit 75}
try{
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash.ToLowerInvariant()-ne'@HASH@'){throw 'Focused archive hash mismatch'}
 $driver=Join-Path $base 'driver';if(Test-Path -LiteralPath $driver){throw 'Fresh driver directory required'}
 New-Item -ItemType Directory -Path $driver|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $driver;if($LASTEXITCODE){throw 'Focused driver extraction failed'}
 $plan=Get-Content -Raw -LiteralPath (Join-Path $driver 'plan.json')|ConvertFrom-Json
 if($plan.baseline-ne'@BASELINE@'){throw 'Unexpected baseline root'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh focused payload required'}
 foreach($item in $plan.files){
  if([IO.Path]::IsPathRooted($item.path)-or$item.path.Contains(':')-or@($item.path-split'/'|Where-Object{$_-eq'..'}).Count){throw 'Invalid cached input path'}
  if((Get-FileHash -LiteralPath (Join-Path $plan.baseline $item.path) -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.baselineSha256){throw 'Cached input changed'}
 }
 foreach($item in $plan.files){
  if($item.patch-and$item.path-notin $plan.assembly,($plan.assembly-replace'[.]dll$','.pdb')){throw 'Production input patch refused'}
  $source=if($item.patch){Join-Path $driver ('patch/'+$item.path)}else{Join-Path $plan.baseline $item.path}
  $target=Join-Path $payload $item.path;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null;Copy-Item -LiteralPath $source -Destination $target
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()-ne$item.sha256){throw 'Copied input mismatch'}
 }
 ('VERIFIED cachedFiles='+$plan.files.Count+' changedFiles='+@($plan.files|Where-Object{$_.patch}).Count)|Set-Content (Join-Path $base 'cache-proof.txt')
 if(Busy){'BUSY phase=render'|Set-Content $state;exit 75}
 $arguments=@((Join-Path $payload $plan.assembly))
 foreach($method in $plan.methods){$arguments+=@('-method',$method)}
 $arguments+=@('-noLogo','-noColor','-maxThreads','2','-xml',(Join-Path $base 'render-@REV@.xml'))
 'RUNNING phase=render'|Set-Content $state;Set-Location -LiteralPath $payload;$ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base 'render-@REV@.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FAILED'|Set-Content $state;exit 1}
