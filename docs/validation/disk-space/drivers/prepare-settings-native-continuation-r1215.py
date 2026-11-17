import hashlib
import json
import pathlib
import shutil
import subprocess

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'
prior = json.loads((t / 'rescan-ui-proof-r1214.json').read_text())
classes = sorted(set(prior['affectedMainTestClasses']['FileCat.App.Tests'] + ['SettingsDialogTests', 'SettingsPreservationTests',
    'AnalysisCurrentDemandTests', 'AnalysisLeaveTests', 'MetadataPreparationTests', 'MetadataOutcomeUiTests'] +
    [p.stem for p in (r / 'tests/FileCat.App.Tests').glob('OwnerColumn*Tests.cs')]))
assert all('*' not in c for c in classes)


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


revision = 'r1219'
proof = dict(revision=revision, productionRevision='r1214', productionRebuilt=False, classes=classes, platforms=[])
for platform in ('windows', 'mac', 'linux'):
    old = t / ('batch-r1214-' + platform)
    rows = [dict(path=row['path'][7:], sha256=row['sha256']) for row in next(p for p in prior['platforms'] if p['platform'] == platform)['inputs'] if row['path'].startswith('render/')]
    assert rows and all(sha(old / 'render' / row['path']) == row['sha256'] for row in rows)
    source = ('C:\\FileCatDsa20261104R2\\batch-r1214\\payload\\render' if platform == 'windows' else
              ('/Users/benny' if platform == 'mac' else '/home/benny') + '/FileCatValidation/dsa-batch-20261107-r1214/payload/render')
    stage = t / ('cached-driver-' + revision + '-' + platform); assert not stage.exists(); stage.mkdir()
    phases = ['settings', '1000-wide', '1000-deep'] if platform == 'windows' else ['1000-wide', '1000-deep']
    (stage / 'plan.json').write_text(json.dumps(dict(source=source, inputs=rows, classes=['*' + c for c in classes], phases=phases), indent=2))
    if platform != 'windows':
        shutil.copy2(d / 'cached-settings-native-r1215.py', stage / 'run.py')
    archive = t / f'dsa-batch-{platform}-{revision}.tgz'; assert not archive.exists()
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    if platform == 'windows':
        runner = (d / 'cached-settings-native-template-r1215.ps1').read_text().replace('@HASH@', sha(archive))
        # Used solely for the existing deployer's conservative incoming disk-space calculation.
        size_stage = t / f'batch-{revision}-windows'; assert not size_stage.exists(); shutil.copytree(stage, size_stage)
    else:
        runner = '''set -eu
cd "$(dirname "$0")"
if [ "$(uname -s)" = Darwin ];then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1);else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1);fi
[ "$actual" = '@HASH@' ];umask 077;mkdir input;tar -xzf bundle.tgz -C input
python3 input/run.py
'''.replace('@HASH@', sha(archive))
    ext = 'ps1' if platform == 'windows' else 'sh'
    path = t / f'batch-{platform}-{revision}.{ext}'; assert not path.exists(); path.write_text(runner, encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size,
        verifiedInputCount=len(rows), runnerSha256=sha(path), planSha256=sha(stage / 'plan.json')))
out = t / f'settings-native-continuation-proof-{revision}.json'; assert not out.exists(); out.write_text(json.dumps(proof, indent=2))
print(json.dumps(proof['platforms']))
