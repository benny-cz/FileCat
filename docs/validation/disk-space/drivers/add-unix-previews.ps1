param(
 [Parameter(Mandatory=$true)][ValidateSet('mac','linux')][string]$Platform,
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][string[]]$CoreMethods,
 [Parameter(Mandatory=$true)][ValidateRange(1,10000)][int]$CoreCases,
 [Parameter(Mandatory=$true)][string[]]$RenderMethods,
 [Parameter(Mandatory=$true)][ValidateRange(1,10000)][int]$RenderCases,
 [switch]$PreviewOnly
)
$ErrorActionPreference='Stop'
foreach($method in @($CoreMethods)+@($RenderMethods)){if($method-notmatch'^[A-Za-z0-9_.*]+$'){throw 'Use fully qualified method patterns'}}
function Selectors([string[]]$methods){(@($methods|ForEach-Object{"-method '"+$_+"'"})-join' ')}
$path=Join-Path $PWD ('.tmp/batch-'+$Platform+'-'+$Revision+'.sh')
$runner=[IO.File]::ReadAllText($path);$boundary='for phase in core render; do'
if(!$runner.Contains($boundary)-or$runner.Contains('for check in smoke preview')){throw 'Unexpected runner shape or previews already present'}
$preview=@'
for check in smoke preview; do
  if busy; then printf 'BUSY phase=%s\n' "$check" > "$base/state.txt"; exit 75; fi
  printf 'RUNNING phase=%s runnerPid=%s\n' "$check" "$$" > "$base/state.txt"
  set +e
  if [ "$check" = smoke ]; then
    cd "$base/payload/core"
    "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll @COREMETHODS@ -noLogo -noColor -maxThreads 2 -xml "$base/smoke-@REV@.xml" > "$base/smoke-@REV@.log" 2>&1
    code=$?; expected=@CORE@
  else
    cd "$base/payload/render"
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll @RENDERMETHODS@ -noLogo -noColor -maxThreads 2 -xml "$base/preview-@REV@.xml" > "$base/preview-@REV@.log" 2>&1
    code=$?; expected=@RENDER@
  fi
  set -e
  if [ "$code" -ne 0 ]; then printf 'FINISHED exit=1 phase=%s\n' "$check" > "$base/state.txt"; exit 1; fi
  if ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");n=int(sys.argv[2]);assert int(a.get("total"))==int(a.get("passed"))==n and a.get("failed")==a.get("errors")=="0"' "$base/$check-@REV@.xml" "$expected"; then
    printf 'FINISHED exit=1 phase=%s\n' "$check" > "$base/state.txt"; exit 1
  fi
done
'@
$preview=$preview.Replace('@REV@',$Revision).Replace('@COREMETHODS@',(Selectors $CoreMethods)).Replace('@RENDERMETHODS@',(Selectors $RenderMethods)).Replace('@CORE@',[string]$CoreCases).Replace('@RENDER@',[string]$RenderCases)
if($PreviewOnly){$preview+="`nprintf 'FINISHED exit=0\n' > `"`$base/state.txt`"`nexit 0`n"}
[IO.File]::WriteAllText($path,$runner.Replace($boundary,$preview+"`n"+$boundary).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
Write-Output ('Added '+$Platform+' previews: '+$CoreCases+' Core and '+$RenderCases+' rendering cases; previewOnly='+[bool]$PreviewOnly+'.')
