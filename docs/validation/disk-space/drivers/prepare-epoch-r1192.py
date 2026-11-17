import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd()
t = r / '.tmp'
t.mkdir(exist_ok=True)
d = r / 'docs/validation/disk-space/drivers'


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(p):
    assert not p.exists(), str(p)
    return p


for name in ('.tmp-epoch-build-final-r1192.txt', '.tmp-epoch-build-windows-native-r1192.txt'):
    s = (r / name).read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in s and s.count('Build succeeded.') == s.count('    0 Warning(s)') == s.count('    0 Error(s)') == 1
paths = {row['path'] for row in json.loads((r.parent / 'disk-space-analysis/.tmp/source-candidate-r1184.json').read_text()) if (r / row['path']).is_file()}
paths.update(('src/FileCat.Core/Analysis/AnalysisCoordinator.Epoch.cs', 'tests/FileCat.Core.Tests/AnalysisScanningTests.Epoch.cs',
              'tests/FileCat.Platform.Windows.Tests/AnalysisWindowsTests.Epoch.cs'))
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(paths)]
core = r / 'tests/FileCat.Core.Tests/bin/Debug/net10.0'
native = r / 'tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0'
core_sha = sha(core / 'FileCat.Core.dll')
assert core_sha == sha(native / 'FileCat.Core.dll')
proof = dict(revision='r1192', worktree=str(r), baseCommit='378fab83', independentOfPendingPerformanceProduct=True,
             coreSha256=core_sha, sources=sources, platforms=[])
validator = """import sys,json,xml.etree.ElementTree as E
mode,path=sys.argv[1:];a=E.parse(path).getroot().find('assembly');c=tuple(int(a.get(k,'-1')) for k in ('total','passed','failed','errors','skipped'))
assert c[0]>0 and c[2:4]==(0,0),c
if mode=='epoch':assert c==(23,23,0,0,0),c
print(json.dumps(dict(mode=mode,counts=c)))
"""
for platform in ('mac', 'linux', 'windows'):
    stage = fresh(t / f'batch-r1192-{platform}')
    for source, target in [(core, stage / 'tests/FileCat.Core.Tests/bin/Debug/net10.0'),
                           (r / 'tests/FileCat.WorkerProbe/bin/Debug/net10.0', stage / 'tests/FileCat.WorkerProbe/bin/Debug/net10.0')] + ([(native, stage / 'native')] if platform == 'windows' else []):
        target.mkdir(parents=True)
        for p in source.iterdir():
            if p.is_file():
                shutil.copy2(p, target / p.name)
    shutil.copy2(r / 'FileCat.slnx', stage / 'FileCat.slnx')
    (stage / 'validate.py').write_text(validator)
    rows = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(t / f'dsa-batch-{platform}-r1192.tgz')
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    ext = 'ps1' if platform == 'windows' else 'sh'
    template = d / f'epoch-{"windows" if platform == "windows" else "unix"}-template-r1192.{ext}'
    runner = fresh(t / f'batch-{platform}-r1192.{ext}')
    runner.write_text(template.read_text().replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=rows, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
snapshot = fresh(t / 'source-r1192-epoch.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for row in sources:
        archive.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / 'epoch-proof-r1192.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']: p['archiveBytes'] for p in proof['platforms']}))
