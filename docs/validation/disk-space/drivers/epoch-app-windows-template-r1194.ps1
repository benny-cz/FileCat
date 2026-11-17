$ErrorActionPreference='Stop';$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use the ordinary R2 interactive desktop'}
function Idle {if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){'BUSY'|Set-Content $state;exit 75}}
try{
 Idle;'RUNNING phase=verify'|Set-Content $state
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Bundle changed'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 New-Item -ItemType Directory -Path $payload|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload;if($LASTEXITCODE){throw 'Extraction failed'}
 foreach($phase in 'epoch','native-epoch','core','native','app-rescan','render','settings'){
  Idle;('RUNNING phase='+$phase)|Set-Content $state
  $bin=Join-Path $payload 'tests\FileCat.Core.Tests\bin\Debug\net10.0';$assembly='FileCat.Core.Tests.dll'
  $filters=switch($phase){
   'epoch'{@('-method','*RescanEpoch*')}
   'native-epoch'{@('-method','*RescanEpoch*')}
   'core'{@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests','-class','*ZipListingRevisionTests')}
   'app-rescan'{@('-method','*NativeRescanPreparation*')}
   'render'{@('-class','*AnalysisRenderingTests')}
   'settings'{@('-class','*SettingsDialogTests','-class','*SettingsPreservationTests')}
   'native'{@('-class','*AnalysisWindowsTests','-class','*WindowsFileOperationsTests','-class','*WindowsHiddenDataTests','-class','*BoundReplacementTests','-class','*BoundComparedDeleteTests','-class','*BoundExtractionTests')}
  }
  if($phase-in@('native-epoch','native')){$bin=Join-Path $payload 'native';$assembly='FileCat.Platform.Windows.Tests.dll'}
  if($phase-in@('app-rescan','render','settings')){$bin=Join-Path $payload 'render';$assembly=if($phase-eq'settings'){'FileCat.App.Tests.dll'}else{'FileCat.Rendering.Tests.dll'}}
  Set-Location -LiteralPath $bin;$ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' $assembly @filters -noLogo -noColor -maxThreads 2 -xml (Join-Path $base ($phase+'.xml')) *> (Join-Path $base ($phase+'.log'))
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
  [xml]$document=[IO.File]::ReadAllText((Join-Path $base ($phase+'.xml')));$a=$document.assemblies.assembly
  if($result-ne0-or[int]$a.total-le0-or[int]$a.failed-ne0-or[int]$a.errors-ne0){throw 'The actual test phase failed'}
  if($phase-eq'app-rescan'-and([int]$a.total-ne7-or[int]$a.passed-ne7-or[int]$a.skipped-ne0)){throw 'App rescan discovery mismatch'}
  if($phase-eq'epoch'-and([int]$a.total-ne23-or[int]$a.passed-ne21-or[int]$a.skipped-ne2)){throw 'Epoch discovery/platform skips mismatch'}
  if($phase-eq'native-epoch'-and([int]$a.total-ne2-or[int]$a.passed-ne2-or[int]$a.skipped-ne0)){throw 'Native epoch discovery mismatch'}
 }
 'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FINISHED exit=1'|Set-Content $state;exit 1}
