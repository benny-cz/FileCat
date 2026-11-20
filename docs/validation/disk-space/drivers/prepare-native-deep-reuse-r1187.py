import hashlib
import json
import pathlib
import shutil
import subprocess

r = pathlib.Path.cwd()
t = r / '.tmp'
d = r / 'docs/validation/disk-space/drivers'


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


stage = t / 'batch-r1187-windows'
assert not stage.exists()
stage.mkdir()
prior = json.loads((t / 'native-locality-fixture-fix-proof-r1185.json').read_text())
win = next(p for p in prior['platforms'] if p['platform'] == 'windows')
inputs = [dict(path=row['path'][7:], sha256=row['sha256']) for row in win['inputs'] if row['path'].startswith('render/')]
assert len(inputs) > 100
for row in inputs:
    assert sha(t / 'batch-r1185-windows/render' / row['path']) == row['sha256']
(stage / 'inputs.json').write_text(json.dumps(inputs, indent=2))
shutil.copy2(d / 'reshape-native-fixture-windows-r1186.ps1', stage / 'reshape.ps1')
archive = t / 'dsa-batch-windows-r1187.tgz'
assert not archive.exists()
subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
runner = t / 'batch-windows-r1187.ps1'
assert not runner.exists()
runner.write_text((d / 'native-deep-reuse-windows-template-r1186.ps1').read_text().replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
proof = dict(revision='r1187', purpose='Focused deep fixture reuse; no FileCat product or C# fixture rebuild',
             reusedRevision='r1185', productCoreSha256=prior['productCoreSha256'],
             reusedRenderInputs=inputs, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size,
             runnerSha256=sha(runner), reshapeSha256=sha(stage / 'reshape.ps1'))
path = t / 'native-deep-reuse-proof-r1187.json'
assert not path.exists()
path.write_text(json.dumps(proof, indent=2))
print(json.dumps(dict(reusedFiles=len(inputs), bytes=archive.stat().st_size, productRebuilt=False)))
