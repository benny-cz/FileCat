import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
drivers = root / 'docs/validation/disk-space/drivers'


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(p):
    assert not p.exists(), 'Fresh destination required: ' + str(p)
    return p


sources = json.loads((temp / 'source-candidate-r1184.json').read_text())
assert all(sha(root / row['path']) == row['sha256'] for row in sources)
build = (temp / 'native-graph-locality-build-r1184.txt').read_text(encoding='utf-8-sig')
assert 'Build FAILED.' not in build
assert all(build.count(x) == 4 for x in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
core_sha = sha(root / 'src/FileCat.Core/bin/Debug/net10.0/FileCat.Core.dll')
# The complete four-project build is recorded above; all packaged Core copies must match it.
render = root / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'
core = root / 'tests/FileCat.Core.Tests/bin/Debug/net10.0'
native = root / 'tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0'
app = root / 'tests/FileCat.App.Tests/bin/Debug/net10.0'
probe = root / 'tests/FileCat.WorkerProbe/bin/Debug/net10.0'
old = temp / 'old-relative-control-r1176'
old_proof = json.loads((temp / 'old-relative-control-r1176-proof.json').read_text())
assert {p.relative_to(old).as_posix(): sha(p) for p in old.rglob('*') if p.is_file()} == {r['path']: r['sha256'] for r in old_proof['inputs']}


def files(source, destination):
    destination.mkdir(parents=True)
    for path in source.iterdir():
        if path.is_file():
            shutil.copy2(path, destination / path.name)


proof = dict(revision='r1184', productCoreSha256=core_sha, productCoreBuiltWithLocalityChanges=True,
             sources=sources, priorProductQualification='source-manifest-r1170-main-sync-input-capacity.json',
             oldControlCoreSha256=old_proof['qualifiedCoreSha256'], platforms=[])
for platform in ('windows', 'mac', 'linux'):
    stage = fresh(temp / f'batch-r1184-{platform}')
    stage.mkdir()
    files(core, stage / 'tests/FileCat.Core.Tests/bin/Debug/net10.0')
    files(probe, stage / 'tests/FileCat.WorkerProbe/bin/Debug/net10.0')
    shutil.copy2(root / 'FileCat.slnx', stage / 'FileCat.slnx')
    files(render, stage / 'render')
    for path in app.glob('FileCat.App.Tests.*'):
        if path.is_file():
            shutil.copy2(path, stage / 'render' / path.name)
    (stage / 'render/runtimes').mkdir()
    for rid in {'windows': ('win', 'win-x64'), 'mac': ('osx', 'osx-arm64'), 'linux': ('linux-x64',)}[platform]:
        shutil.copytree(render / 'runtimes' / rid, stage / 'render/runtimes' / rid)
    shutil.copy2(temp / 'mac-footprint-counter-control-r1171b.json', stage / 'render/native-counter-layout.json')
    shutil.copy2(drivers / 'validate-native-optimization-r1184.py', stage / 'render/validate-native-optimization.py')
    if platform == 'windows':
        files(native, stage / 'native')
    # The already-completed old-product negative control is retained from r1182.
    for folder in ('tests/FileCat.Core.Tests/bin/Debug/net10.0', 'render') + (('native',) if platform == 'windows' else ()):
        assert sha(stage / folder / 'FileCat.Core.dll') == core_sha
    rows = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p), bytes=p.stat().st_size)
            for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(temp / f'dsa-batch-{platform}-r1184.tgz')
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    ext = 'ps1' if platform == 'windows' else 'sh'
    template = drivers / f'native-optimization-{"windows" if platform == "windows" else "unix"}-template-r1184.{ext}'
    runner = fresh(temp / f'batch-{platform}-r1184.{ext}')
    retained_source = json.loads((temp / f'{"ubuntu" if platform == "linux" else platform}-batch-{"r1182" if platform == "mac" else "r1181"}-results/case-1000000-wide/retained-source-request.json').read_text(encoding='utf-8-sig'))['source']
    runner.write_text(template.read_text(encoding='utf-8-sig').replace('@HASH@', sha(archive)).replace('@CORE@', 'tests/FileCat.Core.Tests/bin/Debug/net10.0').replace('@SOURCE@', retained_source), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=rows, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
with tarfile.open(fresh(temp / 'source-r1184-native-optimization.tgz'), 'w:gz') as archive:
    for row in sources:
        archive.add(root / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(temp / 'source-r1184-native-optimization.tgz')
fresh(temp / 'native-optimization-proof-r1184.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']: dict(bytes=p['archiveBytes'], files=len(p['inputs'])) for p in proof['platforms']}))
