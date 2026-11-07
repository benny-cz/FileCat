param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][string[]]$CoreMethods,
 [Parameter(Mandatory=$true)][ValidateRange(1,10000)][int]$CoreCases,
 [Parameter(Mandatory=$true)][string[]]$RenderMethods,
 [Parameter(Mandatory=$true)][ValidateRange(1,10000)][int]$RenderCases
)
$ErrorActionPreference='Stop'
foreach($method in @($CoreMethods)+@($RenderMethods)){if($method-notmatch'^[A-Za-z0-9_.*]+$'){throw 'Use fully qualified method patterns'}}
function Selectors([string[]]$methods){(@($methods|ForEach-Object{"'-method','"+$_+"'"})-join',')}
function ReplaceRequired([string]$text,[string]$old,[string]$replacement){
 if(!$text.Contains($old)){throw 'The immutable batch runner has an unexpected shape or already has previews'}
 return $text.Replace($old,$replacement)
}
$path=Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')
$runner=[IO.File]::ReadAllText($path)
$runner=ReplaceRequired $runner "foreach(`$phase in 'core','native','render')" "foreach(`$phase in 'smoke','preview','core','native','render')"
$runner=ReplaceRequired $runner '$bin=Join-Path $payload $phase' '$bin=Join-Path $payload (@{smoke=''core'';preview=''render'';core=''core'';native=''native'';render=''render''}[$phase])'
$runner=ReplaceRequired $runner '$assembly=switch($phase){' '$assembly=switch($phase){''smoke''{''FileCat.Core.Tests.dll''};''preview''{''FileCat.Rendering.Tests.dll''};'
$runner=ReplaceRequired $runner '$filters=switch($phase){' ('$filters=switch($phase){''smoke''{@('+(Selectors $CoreMethods)+')};''preview''{@('+(Selectors $RenderMethods)+')};')
$check=@'
  if($phase -in @('smoke','preview')){
   $xml=[xml][IO.File]::ReadAllText((Join-Path $base ($phase+'-@REV@.xml')))
   $expected=if($phase-eq'smoke'){@CORE@}else{@RENDER@}
   $assemblyResult=$xml.assemblies.assembly
   if([int]$assemblyResult.total-ne$expected-or[int]$assemblyResult.passed-ne$expected-or[int]$assemblyResult.failed-ne0-or[int]$assemblyResult.errors-ne0){$result=1}
   if($result-ne0){('FINISHED exit=1 phase='+$phase)|Set-Content $state;exit 1}
  }
  if($result -ne 0){$overall=1}
'@
$check=$check.Replace('@REV@',$Revision).Replace('@CORE@',[string]$CoreCases).Replace('@RENDER@',[string]$RenderCases)
$runner=ReplaceRequired $runner '  if($result -ne 0){$overall=1}' $check
[IO.File]::WriteAllText($path,$runner.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
Write-Output ('Added short Windows previews: '+$CoreCases+' Core and '+$RenderCases+' rendering cases; broad suites run only after both pass.')
