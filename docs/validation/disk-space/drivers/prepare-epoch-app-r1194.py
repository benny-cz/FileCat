import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd()
t = r / '.tmp'
d = r / 'docs/validation/disk-space/drivers'


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(p):
    assert not p.exists(), str(p)
    return p


for name in ('.tmp-epoch-app-build-final-r1194.txt', '.tmp-epoch-settings-build-r1194.txt'):
    build = (r / name).read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in build and build.count('Build succeeded.') == build.count('    0 Warning(s)') == build.count('    0 Error(s)') == 1
prior = json.loads((t / 'epoch-proof-r1192.json').read_text())
paths = {row['path'] for row in prior['sources']}
changed = [row['path'] for row in prior['sources'] if sha(r / row['path']) != row['sha256']]
assert changed == ['src/FileCat.App/Services/AnalysisIndexedAcquisition.cs'], changed
paths.update(('src/FileCat.App/Services/AnalysisIndexedAcquisition.Rescan.cs', 'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.Rescan.cs'))
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(paths)]
render = r / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'
settings = r / 'tests/FileCat.App.Tests/bin/Debug/net10.0'
assert sha(render / 'FileCat.Core.dll') == sha(settings / 'FileCat.Core.dll') == prior['coreSha256']
proof = dict(revision='r1194', coreQualificationRevision='r1192', coreUnchanged=True, coreSha256=prior['coreSha256'],
             newAppCases=7, sources=sources, appChangedSourcePaths=changed + sorted(paths - {row['path'] for row in prior['sources']}), platforms=[])
for platform in ('mac', 'linux', 'windows'):
    old = t / f'batch-r1192-{platform}'
    expected = next(p for p in prior['platforms'] if p['platform'] == platform)
    assert {p.relative_to(old).as_posix(): sha(p) for p in old.rglob('*') if p.is_file()} == {p['path']: p['sha256'] for p in expected['inputs']}
    stage = fresh(t / f'batch-r1194-{platform}')
    shutil.copytree(old, stage)
    target = stage / 'render'
    target.mkdir()
    for source in render.iterdir():
        if source.is_file():
            shutil.copy2(source, target / source.name)
    for source in settings.glob('FileCat.App.Tests.*'):
        if source.is_file():
            shutil.copy2(source, target / source.name)
    (target / 'runtimes').mkdir()
    for rid in {'windows': ('win', 'win-x64'), 'mac': ('osx', 'osx-arm64'), 'linux': ('linux-x64',)}[platform]:
        shutil.copytree(render / 'runtimes' / rid, target / 'runtimes' / rid)
    assert sha(target / 'FileCat.Core.dll') == prior['coreSha256']
    (stage / 'validate.py').write_text("""import sys,json,xml.etree.ElementTree as E
mode,path=sys.argv[1:];a=E.parse(path).getroot().find('assembly');c=tuple(int(a.get(k,'-1')) for k in ('total','passed','failed','errors','skipped'))
assert c[0]>0 and c[2:4]==(0,0),c
if mode=='app-rescan':assert c==(7,7,0,0,0),c
if mode=='render':assert c==(786,786,0,0,0),c
if mode=='settings':assert c==(36,36,0,0,0),c
print(json.dumps(dict(mode=mode,counts=c)))
""")
    rows = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(t / f'dsa-batch-{platform}-r1194.tgz')
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    ext = 'ps1' if platform == 'windows' else 'sh'
    template = d / f'epoch-app-{"windows" if platform == "windows" else "unix"}-template-r1194.{ext}'
    runner = fresh(t / f'batch-{platform}-r1194.{ext}')
    runner.write_text(template.read_text().replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=rows, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
snapshot = fresh(t / 'source-r1194-epoch-app.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for row in sources:
        archive.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / 'epoch-app-proof-r1194.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']: p['archiveBytes'] for p in proof['platforms']}))
