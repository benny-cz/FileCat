"""Reuse the uploaded Windows product; run its pending suites and one corrected fixture."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; revision = 'r1295'
def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()
def fresh(p):
    assert not p.exists(), str(p)
    return p

prior = json.loads((t / 'progressive-preview-proof-r1291.json').read_text())
old = next(p for p in prior['platforms'] if p['platform'] == 'windows')
changed = [x['path'] for x in prior['sources'] if sha(r / x['path']) != x['sha256']]
assert changed == ['tests/FileCat.App.Tests/StorageHistoryStartupUiTests.cs'], changed
build = (t / 'progressive-storage-ready-build-r1294.txt').read_text(encoding='utf-8-sig')
assert all(build.count(v) == 1 for v in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
prefix = 'tests/FileCat.App.Tests/bin/Debug/net10.0/'
allowed = {prefix + name for name in ('FileCat.App.Tests.dll', 'FileCat.App.Tests.pdb')}
differences = []
for row in old['inputs']:
    assert sha(t / 'batch-r1291-windows' / row['path']) == row['sha256']
    path = row['path']; current = r / ('tests/FileCat.Rendering.Tests/bin/Debug/net10.0/' + path[7:] if path.startswith('render/') else path)
    if current.is_file() and sha(current) != row['sha256']: differences.append(path)
unused_test_host = prefix + 'FileCat.App.Tests.exe'
assert set(differences) - {unused_test_host} == allowed and set(differences) <= allowed | {unused_test_host}, differences
stage = fresh(t / ('cache-input-' + revision)); stage.mkdir(); patches = []
for path in sorted(allowed):
    name = pathlib.Path(path).name; shutil.copy2(r / path, stage / name)
    patches.append(dict(path=path, patch=name, sha256=sha(r / path)))
plan = dict(baseline=r'C:\FileCatDsa20261104R2\batch-r1291\payload', inputs=old['inputs'], patches=patches,
    clonedBytes=sum((t / 'batch-r1291-windows' / x['path']).stat().st_size for x in old['inputs']))
(stage / 'plan.json').write_text(json.dumps(plan, indent=2))
archive = fresh(t / ('dsa-batch-windows-' + revision + '.tgz')); subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
runner = (t / 'batch-windows-r1291.ps1').read_text(encoding='utf-8-sig'); assert sha(t / 'batch-windows-r1291.ps1') == old['runnerSha256']
runner = runner.replace(old['archiveSha256'], sha(archive))
start = runner.index(" $payload=Join-Path $base 'payload'")
end = runner.index(" foreach($phase", start)
clone = r''' $driver=Join-Path $base 'driver';$payload=Join-Path $base 'payload'
 if((Test-Path -LiteralPath $driver)-or(Test-Path -LiteralPath $payload)){throw 'Fresh directories required'}
 New-Item -ItemType Directory -Path $driver|Out-Null
 & tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $driver;if($LASTEXITCODE){throw 'Extraction failed'}
 $plan=Get-Content -LiteralPath (Join-Path $driver 'plan.json') -Raw|ConvertFrom-Json
 $baseline=[IO.Path]::GetFullPath([string]$plan.baseline).TrimEnd('\')+'\'
 if($baseline-ne'C:\FileCatDsa20261104R2\batch-r1291\payload\'){throw 'Wrong baseline'}
 $disk=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
 $reserve=[long][Math]::Min(2GB,[Math]::Max(256MB,[Math]::Ceiling($disk.Size/100.0)))
 if($disk.FreeSpace-lt($reserve+[long]$plan.clonedBytes+256MB)){throw 'Insufficient clone space'}
 foreach($row in $plan.inputs){
  if([IO.Path]::IsPathRooted($row.path)-or$row.path.Contains(':')-or$row.path.Split('/')-contains'..'){throw 'Invalid input path'}
  $source=[IO.Path]::GetFullPath((Join-Path $baseline $row.path));$item=Get-Item -LiteralPath $source
  if(!$source.StartsWith($baseline,[StringComparison]::OrdinalIgnoreCase)-or$item.PSIsContainer-or($item.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0-or(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash-ne$row.sha256){throw ('Cached input changed: '+$row.path)}
 }
 foreach($row in $plan.inputs){
  $target=Join-Path $payload $row.path;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null
  Copy-Item -LiteralPath (Join-Path $baseline $row.path) -Destination $target
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash-ne$row.sha256){throw 'Clone changed'}
 }
 foreach($row in $plan.patches){
  if($row.patch-notin@('FileCat.App.Tests.dll','FileCat.App.Tests.pdb')-or$row.path-ne('tests/FileCat.App.Tests/bin/Debug/net10.0/'+$row.patch)){throw 'Only the test assembly may change'}
  $patch=Join-Path $driver $row.patch
  if((Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash-ne$row.sha256){throw 'Patch changed'}
  Copy-Item -LiteralPath $patch -Destination (Join-Path $payload $row.path)
 }
 [ordered]@{baseline=$baseline;verifiedFiles=@($plan.inputs).Count;changedProductFiles=0;testOnlyPatches=$plan.patches}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $base 'cache-proof.json')
'''
runner = runner[:start] + clone + runner[end:]
runner = runner.replace("foreach($phase in 'settings','epoch','app-rescan','render')", "foreach($phase in 'epoch','app-rescan','render','settings')")
lines = runner.splitlines()
at = next(i for i, s in enumerate(lines) if s.lstrip().startswith("'settings'{"))
lines[at] = "   'settings'{@('-method','*Private_Windows_history_is_acknowledged_through_UI_and_retained_summary_survives_restart_without_its_journal')}"
runner = '\n'.join(lines) + '\n'
runner = runner.replace("$phase-eq'settings'-and([int]$a.total-ne22-or[int]$a.passed-ne21-or[int]$a.skipped-ne1)", "$phase-eq'settings'-and([int]$a.total-ne1-or[int]$a.passed-ne1-or[int]$a.skipped-ne0)")
script = fresh(t / ('batch-windows-' + revision + '.ps1')); script.write_text(runner, encoding='utf-8', newline='\n')
sources = [dict(path=x['path'], sha256=sha(r / x['path'])) for x in prior['sources']]
snapshot = fresh(t / ('source-' + revision + '-progressive-preview.tgz'))
with tarfile.open(snapshot, 'w:gz') as out:
    for row in sources: out.add(r / row['path'], arcname=row['path'], recursive=False)
proof = dict(revision=revision, sources=sources, head='9a9837c6', priorProofSha256=sha(t / 'progressive-preview-proof-r1291.json'),
    sourceSnapshotSha256=sha(snapshot), productSourceChanged=False, changedSources=changed, patches=patches, inputs=old['inputs'],
    untransferredUnusedTestHost=dict(path=unused_test_host, sha256=sha(r / unused_test_host), reason='Tests run through dotnet DLL; refreshed test apphost resources are not used.'),
    archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(script), publicFeatureRegistered=False)
fresh(t / ('progressive-preview-proof-' + revision + '.json')).write_text(json.dumps(proof, indent=2))
print(json.dumps(dict(bytes=archive.stat().st_size, verifiedInputs=len(old['inputs']), changedProductFiles=0)))
