import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'; revision = 'r1270'


def sha(p):
    with p.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(p):
    assert not p.exists(), str(p)
    return p


for name in ('progressive-frozen-build-r1261-core.txt', 'progressive-controls-final-build-r1263.txt'):
    build = (t / name).read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in build and all(build.count(v) == 1 for v in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
core = r / 'tests/FileCat.Core.Tests/bin/Debug/net10.0'; render = r / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'
assert sha(core / 'FileCat.Core.dll') == sha(render / 'FileCat.Core.dll')
paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', 'src', 'tests', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'FileCat.slnx'], text=True).splitlines()
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(paths) if (r / p).is_file()]
proof = dict(revision=revision, head=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), sources=sources,
    publicFeatureRegistered=False, prototypeDeferredIndexedReviewComplete=False, platforms=[])
focused = "-method '*NativeRuntimeRescanFactory*' -method '*NativeRescanView*' -method '*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs' -method '*NativeOwnedRuntimeView*' -method '*NativeIndexedComparisonCanceledMappingAndAcquisitionPublishNoPartialTab' -method '*NonrespondingPreparationFreezesBindingAndNeverStartsAScanAfterReturning' -method '*NativeProgressivePreview*'"
unix = (d / 'epoch-app-unix-template-r1194.sh').read_text()
unix = unix.replace('for phase in app-rescan render settings;do', 'for phase in epoch app-rescan render;do')
unix = unix.replace('assembly=FileCat.Rendering.Tests.dll', 'assembly=FileCat.Rendering.Tests.dll;bin="$base/payload/render"')
unix = unix.replace('  app-rescan)', '  epoch) bin="$base/payload/tests/FileCat.Core.Tests/bin/Debug/net10.0";assembly=FileCat.Core.Tests.dll;set -- -method \'*RescanEpoch*\';;\n  app-rescan)')
unix = unix.replace("-method '*NativeRescanPreparation*'", focused)
windows = (d / 'epoch-app-windows-template-r1195.ps1').read_text()
windows = windows.replace("foreach($phase in 'epoch','native-epoch','app-rescan','core','native','render','settings')", "foreach($phase in 'epoch','app-rescan','render')")
windows = windows.replace("'-method','*NativeRescanPreparation*'", ','.join("'" + part + "'" for part in focused.replace("'", '').split()))
windows = windows.replace('[int]$a.total-ne7-or[int]$a.passed-ne7', '[int]$a.total-ne36-or[int]$a.passed-ne36')
windows = windows.replace("$bin=Join-Path $payload 'render'", "$bin=Join-Path $payload 'render'")
windows = windows.replace("  if($phase-eq'epoch'", "  if($phase-eq'render'-and([int]$a.total-ne821-or[int]$a.passed-ne820-or[int]$a.skipped-ne1)){throw 'Rendering discovery mismatch'}\n  if($phase-eq'epoch'")
validator = '''import sys,json,xml.etree.ElementTree as E
phase,path=sys.argv[1:];a=E.parse(path).getroot().find('assembly');c=tuple(int(a.get(k,'-1'))for k in ('total','passed','failed','errors','skipped'))
assert c == {'epoch':(23,23,0,0,0),'app-rescan':(36,36,0,0,0),'render':(821,821,0,0,0)}[phase],c
print(json.dumps(dict(phase=phase,counts=c)))
'''
for platform in ('windows', 'mac', 'linux'):
    stage = fresh(t / f'batch-{revision}-{platform}')
    for project in ('FileCat.Core.Tests', 'FileCat.Rendering.Tests', 'FileCat.WorkerProbe', 'FileCat.CoreCrashHarness'):
        source = r / f'tests/{project}/bin/Debug/net10.0'; target = stage / ('render' if project == 'FileCat.Rendering.Tests' else f'tests/{project}/bin/Debug/net10.0'); target.mkdir(parents=True)
        for p in source.iterdir():
            if p.is_file(): shutil.copy2(p, target / p.name)
            elif p.name == 'TestData': shutil.copytree(p, target / p.name)
        for rid in {'windows': ('win', 'win-x64'), 'mac': ('osx', 'osx-arm64'), 'linux': ('linux-x64',)}[platform]:
            if (source / 'runtimes' / rid).exists(): shutil.copytree(source / 'runtimes' / rid, target / 'runtimes' / rid)
    shutil.copy2(r / 'FileCat.slnx', stage / 'FileCat.slnx')
    (stage / 'validate.py').write_text(validator, encoding='utf-8', newline='\n')
    inputs = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(t / f'dsa-batch-{platform}-{revision}.tgz'); subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    runner = fresh(t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    runner.write_text((windows if platform == 'windows' else unix).replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=inputs, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
snapshot = fresh(t / 'source-r1270-progressive-preview.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for row in sources: archive.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / 'progressive-preview-proof-r1270.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']:p['archiveBytes']for p in proof['platforms']}))
