"""Package a new instrumentation-only harness against immutable qualified product bytes."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
drivers = root / 'docs/validation/disk-space/drivers'
revision = 'r1172'
project = temp / 'csv-physical-pinned-r1172'
binary = project / 'bin/Debug/net10.0'


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(path):
    if path.exists():
        raise RuntimeError('Fresh destination required: ' + str(path))
    return path


log = (temp / 'csv-physical-pinned-build-r1172.txt').read_text(encoding='utf-8-sig')
assert 'Build FAILED.' not in log and all(s in log for s in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
source = json.loads((temp / 'csv-physical-source-r1172.json').read_text())
assert all(sha(root / s['path']) == s['sha256'] for s in source['sources'])
assert sha(binary / 'FileCat.Core.dll') == source['qualifiedCoreSha256']
qualified = json.loads((root / 'docs/validation/disk-space/source-manifest-r1170-main-sync-input-capacity.json').read_text())
inputs = {p: {r['path']: r['sha256'] for r in qualified['inputs'][p]} for p in ('windows', 'mac', 'ubuntu')}
control = temp / 'mac-footprint-counter-control-r1171b.json'
native = json.loads(control.read_text(encoding='utf-8-sig'))
assert native['passed'] and native['structBytes'] == 296 and native['footprintOffset'] == 72 and native['lifetimePeakOffset'] == 240
proof = dict(revision=revision, productQualification=source['productQualification'], productRebuilt=False,
             qualifiedCoreSha256=source['qualifiedCoreSha256'], buildLog='.tmp/csv-physical-pinned-build-r1172.txt',
             layoutControlSha256=sha(control), producerSha256=sha(pathlib.Path(__file__)), platforms=[])
for platform in ('windows', 'mac', 'linux'):
    stage = fresh(temp / f'batch-{revision}-{platform}')
    stage.mkdir()
    for path in binary.iterdir():
        if path.is_file():
            shutil.copy2(path, stage / path.name)
    if platform == 'windows':
        # Extra adapters are exact frozen product inputs, never rebuilt here.
        for name in ('FileCat.Platform.Windows.dll', 'FileCat.Recovery.dll', 'System.Management.dll',
                     'Microsoft.Web.WebView2.Core.dll'):
            path = temp / 'batch-r1170-windows/native' / name
            assert sha(path) == inputs['windows']['native/' + name]
            shutil.copy2(path, stage / name)
    assert sha(stage / 'FileCat.Core.dll') == source['qualifiedCoreSha256']
    shutil.copy2(control, stage / 'native-counter-layout.json')
    invalid = dict(native, structBytes=295)
    (stage / 'invalid-counter-layout.json').write_text(json.dumps(invalid))
    shutil.copy2(drivers / 'validate-csv-physical-r1172.py', stage / 'validate.py')
    rows = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p), bytes=p.stat().st_size)
            for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(temp / f'dsa-batch-{platform}-{revision}.tgz')
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    template = drivers / ('csv-physical-windows-template-r1172.ps1' if platform == 'windows' else 'csv-physical-unix-template-r1172.sh')
    text = template.read_text(encoding='utf-8-sig').replace('@HASH@', sha(archive))
    extension = 'ps1' if platform == 'windows' else 'sh'
    runner = fresh(temp / f'batch-{platform}-{revision}.{extension}')
    runner.write_text(text, encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=rows, sha256=sha(archive),
                                   archiveBytes=archive.stat().st_size, runnerSha256=sha(runner), templateSha256=sha(template)))
files = [root / s['path'] for s in source['sources']]
files += [project / 'packages.lock.json', control, temp / 'mac-footprint-counter-control-r1171.c']
files += [drivers / n for n in ('prepare-csv-physical-r1172.py', 'csv-physical-windows-template-r1172.ps1',
                              'csv-physical-unix-template-r1172.sh', 'validate-csv-physical-r1172.py')]
proof['sources'] = [dict(path=p.relative_to(root).as_posix(), sha256=sha(p)) for p in files]
snapshot = fresh(temp / 'source-r1172-csv-physical-instrumentation.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for path in files:
        archive.add(path, arcname=path.relative_to(root).as_posix(), recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(temp / 'csv-physical-proof-r1172.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']: dict(bytes=p['archiveBytes'], files=len(p['inputs'])) for p in proof['platforms']}))
