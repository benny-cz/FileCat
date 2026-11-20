import hashlib
import json
import pathlib
import shutil
import subprocess

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'
revision = 'r1252'; previous = json.loads((t / 'runtime-capacity-health-proof-r1250.json').read_text())


def sha(p):
    with p.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


sources = {
    'windows': {'deep': r'C:\FileCatDsa20261104R2\batch-r1181\retained-fixtures\native-5e515a7094c640bfad6f56a33b2b1d34\source'},
    'mac': {
        'wide': '/Users/benny/FileCatValidation/dsa-batch-20261107-r1182/retained-fixtures/native-20737c85917441a4ac76f67a59c7596e/source',
        'deep': '/Users/benny/FileCatValidation/dsa-batch-20261107-r1189/retained-fixtures/native-2a600486c59f474daa481a9a8c439f52/source'},
    'linux': {
        'wide': '/home/benny/FileCatValidation/dsa-batch-20261107-r1181/retained-fixtures/native-82b33723670e41ab9697f94dc46c06cf/source',
        'deep': '/home/benny/FileCatValidation/dsa-batch-20261107-r1185/retained-fixtures/native-9d8e22d135a644e88ce7f0d550a248d5/source'}}
assert all(sha(r / row['path']) == row['sha256'] for row in previous['sources']), 'Freeze product and fixture before packing cached native work.'
counter = t / 'batch-r1222-mac/render/native-counter-layout.json'
assert json.loads(counter.read_text())['passed'] is True
marker = t / 'windows-retained-fixture-observed-r1251.json'; observed = json.loads(marker.read_text(encoding='utf-8-sig'))
assert observed['source'] == sources['windows']['deep'] and observed['shape'] == 'deep' and observed['sourceCreationComplete'] is True
proof = dict(revision=revision, baseline='r1250', baselineProofSha256=sha(t / 'runtime-capacity-health-proof-r1250.json'),
             productRebuilt=False, casesPerPlatform=['1000000-wide', '1000000-deep'], platforms=[])
for platform in ('windows', 'mac', 'linux'):
    prior = next(p for p in previous['platforms'] if p['platform'] == platform)
    inputs = [dict(path=row['path'][7:], sha256=row['sha256']) for row in prior['inputs'] if row['path'].startswith('render/')]
    assert inputs and all(sha(t / f'batch-r1250-{platform}/render' / row['path']) == row['sha256'] for row in inputs)
    stage = t / f'cached-native-{revision}-{platform}'; assert not stage.exists(); stage.mkdir()
    baseline = (r'C:\FileCatDsa20261104R2\batch-r1250\payload\render' if platform == 'windows' else
                ('/Users/benny' if platform == 'mac' else '/home/benny') + '/FileCatValidation/dsa-batch-20261107-r1250/payload/render')
    plan = dict(baseline=baseline, inputs=inputs, sources=sources[platform])
    if platform == 'windows':
        plan['deepMarkerSha256'] = sha(marker)
        shutil.copy2(d / 'flatten-retained-native-fixture-r1252.ps1', stage / 'flatten.ps1')
    else:
        shutil.copy2(d / 'cached-native-million-r1252.py', stage / 'run.py')
        shutil.copy2(d / 'validate-native-optimization-r1184.py', stage / 'validate.py')
        if platform == 'mac': shutil.copy2(counter, stage / 'native-counter-layout.json')
    (stage / 'plan.json').write_text(json.dumps(plan, indent=2), encoding='utf-8')
    archive = t / f'dsa-batch-{platform}-{revision}.tgz'; assert not archive.exists()
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    if platform == 'windows': template = (d / 'cached-native-million-template-r1252.ps1').read_text()
    else:
        template = '''set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1;then printf 'BUSY phase=verify\n' > state.txt;exit 75;fi
if [ "$(uname -s)" = Darwin ];then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1);else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1);fi
[ "$actual" = '@HASH@' ];umask 077;mkdir driver;tar -xzf bundle.tgz -C driver
python3 driver/run.py
'''
    runner = t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'; assert not runner.exists()
    runner.write_text(template.replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, verifiedInputCount=len(inputs), baselineInputProof=prior,
        archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner), planSha256=sha(stage / 'plan.json')))
marker_out = t / 'cache-input-r1252'; assert not marker_out.exists(); marker_out.write_text('Clone verified r1250 build; no product overlay.\n')
out = t / 'cached-native-million-proof-r1252.json'; assert not out.exists(); out.write_text(json.dumps(proof, indent=2))
print(json.dumps([{k:p[k] for k in ('platform', 'verifiedInputCount', 'archiveBytes')} for p in proof['platforms']]))
