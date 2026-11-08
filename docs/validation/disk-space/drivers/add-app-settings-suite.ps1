param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [switch]$SkipWindows, [switch]$ReuseCoreResultsOnUnix)
$ErrorActionPreference='Stop'
function ReplaceRequired([string]$text,[string]$old,[string]$next){if(!$text.Contains($old)){throw 'Unexpected runner shape'};return $text.Replace($old,$next)}
if(!$SkipWindows){
$path=Join-Path $PWD ('.tmp/batch-windows-'+$Revision+'.ps1')
$runner=[IO.File]::ReadAllText($path)
$runner=ReplaceRequired $runner "foreach(`$phase in 'smoke','preview','core','native','render')" "foreach(`$phase in 'smoke','preview','core','native','render','appsettings')"
$runner=ReplaceRequired $runner "@{smoke='core';preview='render';" "@{appsettings='render';smoke='core';preview='render';"
$runner=ReplaceRequired $runner '$assembly=switch($phase){' '$assembly=switch($phase){''appsettings''{''FileCat.App.Tests.dll''};'
$runner=ReplaceRequired $runner '$filters=switch($phase){' '$filters=switch($phase){''appsettings''{@(''-class'',''*SettingsDialogTests'',''-class'',''*SettingsPreservationTests'')};'
[IO.File]::WriteAllText($path,$runner.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
}
foreach($platform in 'mac','linux'){
 $path=Join-Path $PWD ('.tmp/batch-'+$platform+'-'+$Revision+'.sh')
 $runner=[IO.File]::ReadAllText($path)
 if($ReuseCoreResultsOnUnix){$runner=ReplaceRequired $runner 'for phase in render; do' 'for phase in render appsettings; do'}
 else{$runner=ReplaceRequired $runner 'for phase in core render; do' 'for phase in core render appsettings; do'}
 $old=@'
  else
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class
'@
 $next=@'
  elif [ "$phase" = appsettings ]; then
    cd "$base/payload/render"
    "$HOME/.dotnet/dotnet" FileCat.App.Tests.dll -class '*SettingsDialogTests' -class '*SettingsPreservationTests' -noLogo -noColor -maxThreads 2 -xml "$base/appsettings-@REV@.xml" > "$base/appsettings-@REV@.log" 2>&1
  else
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class
'@
 $runner=ReplaceRequired $runner $old ($next.Replace('@REV@',$Revision))
 # The phase directory for this assembly is the already verified renderer payload.
 $runner=ReplaceRequired $runner '  cd "$base/payload/$phase"' '  if [ "$phase" = appsettings ]; then cd "$base/payload/render"; else cd "$base/payload/$phase"; fi'
 [IO.File]::WriteAllText($path,$runner.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Added existing Settings dialog/preservation regressions on all three platforms; app-test inputs share the verified renderer payload.'
