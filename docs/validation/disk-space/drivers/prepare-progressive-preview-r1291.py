"""Fixture-only stability correction. Reuse passed Unix audit and epoch phases."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'; revision = 'r1291'
def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()
def fresh(p):
    assert not p.exists(), str(p)
    return p

for name in ('progressive-app-fixtures-build-r1290.txt',):
    s = (t / name).read_text(encoding='utf-8-sig')
    assert all(s.count(v) == 1 for v in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
prior = json.loads((t / 'progressive-preview-proof-r1285.json').read_text())
older = json.loads((t / 'progressive-preview-proof-r1283.json').read_text())
changed = [x['path'] for x in prior['sources'] if sha(r / x['path']) != x['sha256']]
assert changed == ['tests/FileCat.App.Tests/OperationsDrawerLayoutTests.cs', 'tests/FileCat.App.Tests/StorageHistoryStartupUiTests.cs'], changed
paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', 'src', 'tests', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'FileCat.slnx'], text=True).splitlines()
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(paths) if (r / p).is_file()]
proof = dict(revision=revision, head=prior['head'], sources=sources, priorProofSha256=sha(t / 'progressive-preview-proof-r1285.json'),
    testFixtureOnlyCorrection=True, reusedUnixPhases=['epoch', 'render'], changedSources=changed, publicFeatureRegistered=False, prototypeDeferredIndexedReviewComplete=False, platforms=[])
allowed = {'tests/FileCat.App.Tests/bin/Debug/net10.0/FileCat.App.Tests.dll', 'tests/FileCat.App.Tests/bin/Debug/net10.0/FileCat.App.Tests.pdb'}
for old in prior['platforms']:
    platform = old['platform']; baseline = t / ('batch-r1285-windows' if platform == 'windows' else 'batch-r1281-' + platform); patches = []
    inputs_by_path = {x['path']: dict(x) for x in old['inputs']}
    if platform != 'windows':
        for x in old['patches']: inputs_by_path[x['path']]['sha256'] = x['sha256']
    baseline_inputs = list(inputs_by_path.values())
    old_patched = {x['path'] for x in old['patches']} if platform != 'windows' else set()
    older_patched = {x['path'] for p in older['platforms'] if p['platform'] == platform for x in p['patches']} if platform != 'windows' else set()
    for row in baseline_inputs:
        original = t / ('cached-r1285-' + platform) / 'patch' / row['path'] if row['path'] in old_patched else (
            t / ('cached-r1283-' + platform) / 'patch' / row['path'] if row['path'] in older_patched else baseline / row['path'])
        assert sha(original) == row['sha256']
        name = row['path']; source = r / ('tests/FileCat.Rendering.Tests/bin/Debug/net10.0/' + name[7:] if name.startswith('render/') else name)
        if source.is_file() and sha(source) != row['sha256']:
            assert name in allowed, name
            patches.append(dict(path=name, sha256=sha(source), source=source))
    assert {x['path'] for x in patches} == allowed
    validation = (baseline / 'validate.py').read_text().replace("'settings':(3,3,0,0,0) if sys.platform=='darwin' else (3,2,0,0,1)",
        "'settings':(22,21,0,0,1) if sys.platform=='darwin' else (22,20,0,0,2)")
    validation_path = fresh(t / f'validation-{revision}-{platform}.py'); validation_path.write_text(validation)
    patches.append(dict(path='validate.py', sha256=sha(validation_path), source=validation_path))
    if platform == 'windows':
        stage = fresh(t / ('batch-' + revision + '-windows')); shutil.copytree(baseline, stage)
        for row in patches: shutil.copy2(row['source'], stage / row['path'])
        runner = (t / 'batch-windows-r1285.ps1').read_text().replace(old['archiveSha256'], '@HASH@')
        runner = runner.replace("'-method','*No_control_that_can_be_live_has_a_fixed_accessible_name'", "'-method','*No_control_that_can_be_live_has_a_fixed_accessible_name','-method','*OperationsDrawerLayoutTests.*','-method','*StorageHistoryStartupUiTests.*'")
        runner = runner.replace("$phase-eq'settings'-and([int]$a.total-ne3-or[int]$a.passed-ne2-or[int]$a.skipped-ne1)", "$phase-eq'settings'-and([int]$a.total-ne22-or[int]$a.passed-ne21-or[int]$a.skipped-ne1)")
        inputs = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    else:
        stage = fresh(t / ('cached-' + revision + '-' + platform)); stage.mkdir()
        home = '/Users/benny' if platform == 'mac' else '/home/benny'
        plan = dict(baseline=home + '/FileCatValidation/dsa-batch-20261107-r1285/payload', inputs=baseline_inputs,
            clonedBytes=sum((baseline / x['path']).stat().st_size for x in baseline_inputs), patches=[dict(path=x['path'], sha256=x['sha256']) for x in patches])
        for row in patches:
            target = stage / 'patch' / row['path']; target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(row['source'], target)
        (stage / 'plan.json').write_text(json.dumps(plan, indent=2)); shutil.copy2(d / 'clone-progressive-payload-r1283.py', stage / 'clone.py')
        body = (t / ('batch-' + platform + '-r1285.sh')).read_text().split('bin="$base/payload/render"', 1)[1]
        body = body.replace('for phase in render;do', 'for phase in settings;do')
        body = body.replace("-method '*No_control_that_can_be_live_has_a_fixed_accessible_name'", "-method '*No_control_that_can_be_live_has_a_fixed_accessible_name' -method '*OperationsDrawerLayoutTests.*' -method '*StorageHistoryStartupUiTests.*'")
        runner = '''set -eu
cd "$(dirname "$0")";base=$(pwd)
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1;then printf 'BUSY phase=verify\\n' > state.txt;exit 75;fi
if [ "$(uname -s)" = Darwin ];then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1);else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1);fi
[ "$actual" = '@HASH@' ];umask 077;mkdir driver;tar -xzf bundle.tgz -C driver
python3 driver/clone.py
idle(){ if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1;then printf 'BUSY phase=next\\n' > state.txt;exit 75;fi; }
bin="$base/payload/render"''' + body
        inputs = baseline_inputs
    archive = fresh(t / f'dsa-batch-{platform}-{revision}.tgz'); subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    script = fresh(t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'); script.write_text(runner.replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, cached=platform != 'windows', inputs=inputs,
        patches=[dict(path=x['path'], sha256=x['sha256']) for x in patches], archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(script)))
snapshot = fresh(t / 'source-r1291-progressive-preview.tgz')
with tarfile.open(snapshot, 'w:gz') as out:
    for row in sources: out.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / 'progressive-preview-proof-r1291.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({x['platform']:x['archiveBytes'] for x in proof['platforms']}))
