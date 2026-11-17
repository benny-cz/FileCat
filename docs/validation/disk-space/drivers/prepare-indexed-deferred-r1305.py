"""Freeze the complete deferred-review slice for three queued guest suites."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'; revision = 'r1305'
candidate = r.parent / 'dsa-progressive-startup-20261117'
def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()
def fresh(p):
    assert not p.exists(), str(p)
    return p

build = (t / 'indexed-deferred-publication-build-r1304.txt').read_text(encoding='utf-8-sig')
assert all(build.count(s) == 1 for s in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
assert not subprocess.check_output(['git','diff','--name-only','026a1272','--','src/FileCat.Core','src/FileCat.Platform.Windows'],text=True).strip()
paths = subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','src','tests','FileCat.slnx','global.json','Directory.Build.props','Directory.Build.targets','Directory.Packages.props'],text=True).splitlines()
sources = [dict(path=p,sha256=sha(r/p)) for p in sorted(paths) if (r/p).is_file()]
proof = dict(revision=revision,head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),sources=sources,
 publicFeatureRegistered=False,fullGoalComplete=False,unchangedCoreAndWindowsSourceComparedWith='026a1272',platforms=[])
focus = "-method '*NativeRuntimeRescanFactory*' -method '*NativeRescanView*' -method '*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs' -method '*NativeOwnedRuntimeView*' -method '*NativeIndexedComparisonCanceledMappingAndAcquisitionPublishNoPartialTab' -method '*NonrespondingPreparationFreezesBindingAndNeverStartsAScanAfterReturning' -method '*NativeProgressivePreview*' -method '*NativeDeferredIndexed*'"
audit = "-method '*No_control_that_can_be_live_has_a_fixed_accessible_name'"
unix = (d / 'epoch-app-unix-template-r1194.sh').read_text()
unix = unix.replace('for phase in app-rescan render settings;do','for phase in settings app-rescan render;do')
unix = unix.replace('assembly=FileCat.Rendering.Tests.dll','assembly=FileCat.Rendering.Tests.dll;bin="$base/payload/render"')
unix = unix.replace("-method '*NativeRescanPreparation*'",focus)
unix = unix.replace("settings) assembly=FileCat.App.Tests.dll;set -- -class '*SettingsDialogTests' -class '*SettingsPreservationTests';;",'settings) bin="$base/payload/tests/FileCat.App.Tests/bin/Debug/net10.0";assembly=FileCat.App.Tests.dll;set -- '+audit+';;')
windows = (d / 'epoch-app-windows-template-r1195.ps1').read_text()
windows = windows.replace("foreach($phase in 'epoch','native-epoch','app-rescan','core','native','render','settings')","foreach($phase in 'settings','app-rescan','render')")
windows = windows.replace("'-method','*NativeRescanPreparation*'",','.join("'"+s+"'" for s in focus.replace("'",'').split()))
windows = windows.replace("'-class','*SettingsDialogTests','-class','*SettingsPreservationTests'",','.join("'"+s+"'" for s in audit.replace("'",'').split()))
windows = windows.replace("  Set-Location -LiteralPath $bin","  if($phase-eq'settings'){$bin=Join-Path $payload 'tests\\FileCat.App.Tests\\bin\\Debug\\net10.0'}\n  Set-Location -LiteralPath $bin")
windows = windows.replace('[int]$a.total-ne7-or[int]$a.passed-ne7','[int]$a.total-ne54-or[int]$a.passed-ne54')
windows = windows.replace("  if($phase-eq'epoch'","  if($phase-eq'settings'-and([int]$a.total-ne1-or[int]$a.passed-ne1-or[int]$a.skipped-ne0)){throw 'Source audit discovery mismatch'}\n  if($phase-eq'render'-and([int]$a.total-ne840-or[int]$a.passed-ne839-or[int]$a.skipped-ne1)){throw 'Rendering discovery mismatch'}\n  if($phase-eq'epoch'")
validator = '''import sys,json,xml.etree.ElementTree as E
phase,path=sys.argv[1:];a=E.parse(path).getroot().find('assembly');c=tuple(int(a.get(k,'-1'))for k in ('total','passed','failed','errors','skipped'))
assert c=={'settings':(1,1,0,0,0),'app-rescan':(54,54,0,0,0),'render':(840,840,0,0,0)}[phase],c
print(json.dumps(dict(phase=phase,counts=c)))
'''
render = r / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'
prior = json.loads((candidate / '.tmp/progressive-preview-proof-r1291.json').read_text())
for platform in ('windows','mac','linux'):
    stage = fresh(t / ('batch-'+revision+'-'+platform)); (stage/'render').mkdir(parents=True)
    for p in render.iterdir():
        if p.is_file(): shutil.copy2(p,stage/'render'/p.name)
        elif p.name=='TestData': shutil.copytree(p,stage/'render'/p.name)
    for rid in {'windows':('win','win-x64'),'mac':('osx','osx-arm64'),'linux':('linux-x64',)}[platform]:
        if (render/'runtimes'/rid).exists(): shutil.copytree(render/'runtimes'/rid,stage/'render/runtimes'/rid)
    # The unchanged source-audit method is reused; all product dependencies come from this build.
    app = stage / 'tests/FileCat.App.Tests/bin/Debug/net10.0'
    baseline = candidate / ('.tmp/batch-r1291-'+platform) / 'tests/FileCat.App.Tests/bin/Debug/net10.0'
    if platform!='windows': baseline = candidate / ('.tmp/batch-r1281-'+platform) / 'tests/FileCat.App.Tests/bin/Debug/net10.0'
    shutil.copytree(baseline,app)
    for p in render.iterdir():
        if p.is_file() and p.name.startswith('FileCat') and p.suffix in ('.dll','.pdb') and (app/p.name).exists(): shutil.copy2(p,app/p.name)
    for name in paths:
        if name.startswith('src/FileCat.App/') and pathlib.Path(name).suffix in ('.cs','.axaml','.csproj'):
            target=stage/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(r/name,target)
    shutil.copy2(r/'FileCat.slnx',stage/'FileCat.slnx');(stage/'validate.py').write_text(validator,encoding='utf-8',newline='\n')
    inputs=[dict(path=p.relative_to(stage).as_posix(),sha256=sha(p))for p in sorted(stage.rglob('*'))if p.is_file()]
    archive=fresh(t/f'dsa-batch-{platform}-{revision}.tgz');subprocess.run(['tar.exe','-czf',str(archive),'-C',str(stage),'.'],check=True)
    runner=fresh(t/f'batch-{platform}-{revision}.{"ps1"if platform=='windows'else'sh'}');runner.write_text((windows if platform=='windows'else unix).replace('@HASH@',sha(archive)),encoding='utf-8',newline='\n')
    proof['platforms'].append(dict(platform=platform,inputs=inputs,archiveSha256=sha(archive),archiveBytes=archive.stat().st_size,runnerSha256=sha(runner),sourceAuditAssemblyReused=True))
snapshot=fresh(t/'source-r1305-indexed-deferred.tgz')
with tarfile.open(snapshot,'w:gz')as out:
    for row in sources:out.add(r/row['path'],arcname=row['path'],recursive=False)
proof['sourceSnapshotSha256']=sha(snapshot);fresh(t/'indexed-deferred-proof-r1305.json').write_text(json.dumps(proof,indent=2))
print(json.dumps({p['platform']:p['archiveBytes']for p in proof['platforms']}))
