"""Five-file UI correction: cached Unix payloads, full Windows first deployment."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'; revision = 'r1283'
def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()
def fresh(p):
    assert not p.exists(), str(p)
    return p

for name in ('progressive-accessibility-final-build-r1282.txt', 'progressive-app-final-build-r1282.txt'):
    s = (t / name).read_text(encoding='utf-8-sig')
    assert all(s.count(v) == 1 for v in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
prior = json.loads((t / 'progressive-preview-proof-r1281.json').read_text())
changed = [x['path'] for x in prior['sources'] if sha(r / x['path']) != x['sha256']]
assert changed == ['src/FileCat.App/Views/SavedAnalysisInspectionPanel.cs'], changed
paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', 'src', 'tests', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'FileCat.slnx'], text=True).splitlines()
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(paths) if (r / p).is_file()]
proof = dict(revision=revision, head=prior['head'], sources=sources, priorProofSha256=sha(t / 'progressive-preview-proof-r1281.json'),
    priorFailedRevision='r1281', changedSources=changed, publicFeatureRegistered=False, prototypeDeferredIndexedReviewComplete=False, platforms=[])
allowed = {'render/FileCat.dll', 'render/FileCat.pdb', 'src/FileCat.App/Views/SavedAnalysisInspectionPanel.cs',
           'tests/FileCat.App.Tests/bin/Debug/net10.0/FileCat.dll', 'tests/FileCat.App.Tests/bin/Debug/net10.0/FileCat.pdb'}
for old in prior['platforms']:
    platform = old['platform']; baseline = t / ('batch-r1281-' + platform); patches = []
    for row in old['inputs']:
        assert sha(baseline / row['path']) == row['sha256']
        name = row['path']; source = r / ('tests/FileCat.Rendering.Tests/bin/Debug/net10.0/' + name[7:] if name.startswith('render/') else name)
        if source.is_file() and sha(source) != row['sha256']:
            assert name in allowed, name
            patches.append(dict(path=name, sha256=sha(source), source=source))
    assert {x['path'] for x in patches} == allowed
    if platform == 'windows':
        stage = fresh(t / ('batch-' + revision + '-windows')); shutil.copytree(baseline, stage)
        for row in patches: shutil.copy2(row['source'], stage / row['path'])
        runner = (t / 'batch-windows-r1281.ps1').read_text().replace(old['archiveSha256'], '@HASH@')
        inputs = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    else:
        stage = fresh(t / ('cached-' + revision + '-' + platform)); stage.mkdir()
        home = '/Users/benny' if platform == 'mac' else '/home/benny'
        plan = dict(baseline=home + '/FileCatValidation/dsa-batch-20261107-r1281/payload', inputs=old['inputs'],
            clonedBytes=sum((baseline / x['path']).stat().st_size for x in old['inputs']), patches=[dict(path=x['path'], sha256=x['sha256']) for x in patches])
        for row in patches:
            target = stage / 'patch' / row['path']; target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(row['source'], target)
        (stage / 'plan.json').write_text(json.dumps(plan, indent=2)); shutil.copy2(d / 'clone-progressive-payload-r1283.py', stage / 'clone.py')
        body = (t / ('batch-' + platform + '-r1281.sh')).read_text().split('bin="$base/payload/render"', 1)[1]
        runner = '''set -eu
cd "$(dirname "$0")";base=$(pwd)
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1;then printf 'BUSY phase=verify\\n' > state.txt;exit 75;fi
if [ "$(uname -s)" = Darwin ];then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1);else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1);fi
[ "$actual" = '@HASH@' ];umask 077;mkdir driver;tar -xzf bundle.tgz -C driver
python3 driver/clone.py
idle(){ if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1;then printf 'BUSY phase=next\\n' > state.txt;exit 75;fi; }
bin="$base/payload/render"''' + body
        inputs = old['inputs']
    archive = fresh(t / f'dsa-batch-{platform}-{revision}.tgz'); subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    script = fresh(t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'); script.write_text(runner.replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, cached=platform != 'windows', inputs=inputs,
        patches=[dict(path=x['path'], sha256=x['sha256']) for x in patches], archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(script)))
snapshot = fresh(t / 'source-r1283-progressive-preview.tgz')
with tarfile.open(snapshot, 'w:gz') as out:
    for row in sources: out.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / 'progressive-preview-proof-r1283.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({x['platform']:x['archiveBytes'] for x in proof['platforms']}))
