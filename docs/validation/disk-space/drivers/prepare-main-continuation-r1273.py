"""Resume missing Windows phases with cached product bytes and one fixture-only patch."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; revision = 'r1273'
def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()
def fresh(p):
    assert not p.exists(), str(p)
    return p

prior = json.loads((t / 'main-integration-proof-r1255.json').read_text())
old = next(p for p in prior['platforms'] if p['platform'] == 'windows')
changed = 'tests/FileCat.Platform.Windows.Tests/WindowsArchiveTargetProbeTests.cs'
assert all(sha(r / x['path']) == x['sha256'] for x in prior['sources'] if x['path'] != changed)
build = (t / 'main-probe-fixture-build-r1272.txt').read_text(encoding='utf-8-sig')
assert all(build.count(s) == 1 for s in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
stage = fresh(t / ('cache-input-' + revision)); stage.mkdir()
replacements = []
prefix = 'tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0/'
for name in ('FileCat.Platform.Windows.Tests.dll', 'FileCat.Platform.Windows.Tests.pdb'):
    source = r / prefix / name
    shutil.copy2(source, stage / name)
    replacements.append(dict(path=prefix + name, patch=name, sha256=sha(source)))
for row in old['inputs']:
    assert sha(t / 'batch-r1255-windows' / row['path']) == row['sha256']
plan = dict(baseline=r'C:\FileCatDsa20261104R2\batch-r1255\payload', inputs=old['inputs'], replacements=replacements,
    clonedBytes=sum((t / 'batch-r1255-windows' / x['path']).stat().st_size for x in old['inputs']),
    methods=['*NativeHistoryViewProtectsItsRollingCopyAndPinnedCopySurvivesLaterRetention',
             '*GroupOtherNativeWindowRetainsExactMembersThroughChartFocusEveryOrderHistoryAndCatalogRestore',
             '*GroupOtherNativeOverviewKeepsCategoriesBoundedAndFocusedCaptureInsideItsSavedScope'])
(stage / 'plan.json').write_text(json.dumps(plan, indent=2))
archive = fresh(t / f'dsa-batch-windows-{revision}.tgz')
subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
runner = r'''$ErrorActionPreference='Stop';$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
function Idle([string]$phase){if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){('BUSY phase='+$phase)|Set-Content $state;exit 75}}
try{
 if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use ordinary interactive R2'}
 Idle verify
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash-ne'@HASH@'){throw 'Bundle changed'}
 $driver=Join-Path $base 'driver';$payload=Join-Path $base 'payload'
 if((Test-Path -LiteralPath $driver)-or(Test-Path -LiteralPath $payload)){throw 'Fresh directories required'}
 New-Item -ItemType Directory -Path $driver|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $driver;if($LASTEXITCODE){throw 'Extraction failed'}
 $plan=Get-Content -LiteralPath (Join-Path $driver 'plan.json') -Raw|ConvertFrom-Json
 $baseline=[IO.Path]::GetFullPath([string]$plan.baseline).TrimEnd('\')+'\'
 if($baseline-ne'C:\FileCatDsa20261104R2\batch-r1255\payload\'){throw 'Wrong baseline'}
 $disk=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
 $reserve=[long][Math]::Min(2GB,[Math]::Max(256MB,[Math]::Ceiling($disk.Size/100.0)))
 if($disk.FreeSpace-lt($reserve+[long]$plan.clonedBytes+256MB)){throw 'Insufficient space before cloning cached inputs'}
 foreach($row in $plan.inputs){
  if([IO.Path]::IsPathRooted($row.path)-or$row.path.Contains(':')-or$row.path.Split('/')-contains'..'){throw 'Invalid input path'}
  $source=[IO.Path]::GetFullPath((Join-Path $baseline $row.path))
  if(!$source.StartsWith($baseline,[StringComparison]::OrdinalIgnoreCase)){throw 'Input escaped baseline'}
  $item=Get-Item -LiteralPath $source
  if($item.PSIsContainer-or($item.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0-or(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash-ne$row.sha256){throw ('Cached input changed: '+$row.path)}
 }
 foreach($row in $plan.inputs){
  $target=Join-Path $payload $row.path;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null
  Copy-Item -LiteralPath (Join-Path $baseline $row.path) -Destination $target
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash-ne$row.sha256){throw 'Clone changed'}
 }
 foreach($row in $plan.replacements){
  if($row.patch-notin@('FileCat.Platform.Windows.Tests.dll','FileCat.Platform.Windows.Tests.pdb')-or$row.path-ne('tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0/'+$row.patch)){throw 'Only the fixture assembly may be patched'}
  $patch=Join-Path $driver $row.patch
  if((Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash-ne$row.sha256){throw 'Fixture patch changed'}
  Copy-Item -LiteralPath $patch -Destination (Join-Path $payload $row.path)
 }
 Copy-Item -LiteralPath (Join-Path $payload 'tests\FileCat.Rendering.Tests\bin\Debug\net10.0') -Destination (Join-Path $payload 'render') -Recurse
 [ordered]@{verifiedFiles=@($plan.inputs).Count;changedProductFiles=0;fixtureOnlyReplacements=@($plan.replacements);baseline=$baseline;flatRenderingAssembly=$true}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $base 'cache-proof.json')
 foreach($phase in 'native','app-rescan','render','settings'){
  Idle $phase;('RUNNING phase='+$phase)|Set-Content $state
  $project=switch($phase){'native'{'FileCat.Platform.Windows.Tests'}'settings'{'FileCat.App.Tests'}default{'FileCat.Rendering.Tests'}}
  $bin=if($phase-in@('app-rescan','render')){Join-Path $payload 'render'}else{Join-Path $payload ('tests\'+$project+'\bin\Debug\net10.0')}
  $filters=switch($phase){'native'{@('-class','*WindowsArchiveTargetProbeTests')}'app-rescan'{$f=@();foreach($m in $plan.methods){$f+=@('-method',$m)};$f}'render'{@('-class','*AnalysisRenderingTests')}default{@('-class','*')}}
  Set-Location -LiteralPath $bin;$ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' ($project+'.dll') @filters -noLogo -noColor -maxThreads 2 -xml (Join-Path $base ($phase+'.xml')) *> (Join-Path $base ($phase+'.log'))
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop';[xml]$xml=[IO.File]::ReadAllText((Join-Path $base ($phase+'.xml')));$a=$xml.assemblies.assembly
  if($result-ne0-or[int]$a.total-le0-or[int]$a.failed-ne0-or[int]$a.errors-ne0){throw ('Actual test phase failed: '+$phase)}
  if($phase-eq'native'-and([int]$a.total-ne5-or[int]$a.passed-ne3-or[int]$a.skipped-ne2)){throw 'Native fixture discovery mismatch'}
  if($phase-eq'app-rescan'-and([int]$a.total-ne12-or[int]$a.passed-ne12-or[int]$a.skipped-ne0)){throw 'Focused discovery mismatch'}
  if($phase-eq'render'-and([int]$a.total-ne814-or[int]$a.passed-ne813-or[int]$a.skipped-ne1)){throw 'Rendering discovery mismatch'}
 }
 'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FINISHED exit=1'|Set-Content $state;exit 1}
'''
path = fresh(t / f'batch-windows-{revision}.ps1'); path.write_text(runner.replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
proof = dict(revision=revision, baseline='r1255', priorProofSha256=sha(t / 'main-integration-proof-r1255.json'),
    changedSource=dict(path=changed, sha256=sha(r / changed)), productSourceChanged=False, productRebuiltForContinuation=False,
    note='Host build refreshed commit metadata in dependencies; only test DLL/PDB transferred. All product inputs reuse exact r1255 bytes.',
    inputs=old['inputs'], replacements=replacements, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(path))
fresh(t / 'main-continuation-proof-r1273.json').write_text(json.dumps(proof, indent=2))
with tarfile.open(fresh(t / 'source-r1273-native-fixture.tgz'), 'w:gz') as out:
    out.add(r / changed, arcname=changed)
print(json.dumps(dict(revision=revision, archiveBytes=archive.stat().st_size, verifiedInputCount=len(old['inputs']), changedProductFiles=0)))
