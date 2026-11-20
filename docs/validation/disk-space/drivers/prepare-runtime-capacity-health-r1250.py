import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'
revision = 'r1250'


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(p):
    assert not p.exists(), str(p)
    return p


build = (t / 'indexed-runtime-capacity-health-build-r1249.txt').read_text(encoding='utf-8-sig')
assert 'Build FAILED.' not in build and all(build.count(x) == 1 for x in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
render = r / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'
prior = json.loads((t / 'indexed-runtime-helpers-proof-r1233.json').read_text())
reused = [row for row in prior['sources'] if row['path'].startswith(('src/FileCat.Core/', 'tests/FileCat.Core.Tests/'))]
assert reused and all(sha(r / row['path']) == row['sha256'] for row in reused)
paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', 'src', 'tests', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'FileCat.slnx'], text=True).splitlines()
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(paths) if (r / p).is_file()]
proof = dict(revision=revision, head=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), sources=sources,
             reusedCoreSource=reused, previousProofSha256=sha(t / 'indexed-runtime-helpers-proof-r1233.json'), platforms=[])
focused = "-method '*NativeRuntimeRescanFactory*' -method '*NativeRescanView*' -method '*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs' -method '*NativeOwnedRuntimeView*' -method '*NativeIndexedComparisonCanceledMappingAndAcquisitionPublishNoPartialTab' -method '*NonrespondingPreparationFreezesBindingAndNeverStartsAScanAfterReturning'"
for platform in ('windows', 'mac', 'linux'):
    stage = fresh(t / f'batch-{revision}-{platform}'); target = stage / 'render'; target.mkdir(parents=True)
    for p in render.iterdir():
        if p.is_file() and not p.name.startswith('FileCat.App.Tests.'):
            shutil.copy2(p, target / p.name)
    for rid in {'windows': ('win', 'win-x64'), 'mac': ('osx', 'osx-arm64'), 'linux': ('linux-x64',)}[platform]:
        shutil.copytree(render / 'runtimes' / rid, target / 'runtimes' / rid)
    unix = (d / 'epoch-app-unix-template-r1194.sh').read_text()
    unix = unix.replace('for phase in app-rescan render settings;do', 'for phase in app-rescan render;do')
    unix = unix.replace("-method '*NativeRescanPreparation*'", focused)
    windows = (d / 'epoch-app-windows-template-r1195.ps1').read_text()
    windows = windows.replace("foreach($phase in 'epoch','native-epoch','app-rescan','core','native','render','settings')", "foreach($phase in 'app-rescan','render')")
    windows = windows.replace("'-method','*NativeRescanPreparation*'", ','.join("'" + part + "'" for part in focused.replace("'", '').split()))
    windows = windows.replace('[int]$a.total-ne7-or[int]$a.passed-ne7', '[int]$a.total-ne29-or[int]$a.passed-ne29')
    windows = windows.replace("  if($phase-eq'epoch'", "  if($phase-eq'render'-and([int]$a.total-ne814-or[int]$a.passed-ne813-or[int]$a.skipped-ne1)){throw 'Rendering discovery/platform skips mismatch'}\n  if($phase-eq'epoch'")
    (stage / 'validate.py').write_text("""import sys,json,xml.etree.ElementTree as E
phase,path=sys.argv[1:];a=E.parse(path).getroot().find('assembly');c=tuple(int(a.get(k,'-1'))for k in ('total','passed','failed','errors','skipped'))
assert c == ({'app-rescan':(29,29,0,0,0),'render':(814,814,0,0,0)}[phase]),c
print(json.dumps(dict(phase=phase,counts=c)))
""", encoding='utf-8', newline='\n')
    inputs = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(t / f'dsa-batch-{platform}-{revision}.tgz'); subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    runner = fresh(t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    runner.write_text((windows if platform == 'windows' else unix).replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=inputs, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
snapshot = fresh(t / f'source-{revision}-runtime-capacity-health.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for row in sources: archive.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / f'runtime-capacity-health-proof-{revision}.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']:p['archiveBytes'] for p in proof['platforms']}))
