"""Run once after the actual main/preview batches have terminated and been collected."""
import fnmatch
import hashlib
import io
import json
import pathlib
import subprocess
import tarfile
import xml.etree.ElementTree as E

r = pathlib.Path.cwd(); d = r / 'docs/validation/disk-space'
candidate = r.parent / 'dsa-progressive-startup-20261117'
target = d / 'source-manifest-r1298-progressive-main.json'
outputs = [d / f'exact-{kind}-r1298-progressive-main.tgz' for kind in ('source-deltas', 'receipts', 'workflow')]
assert not target.exists() and not any(p.exists() for p in outputs), 'Immutable packet already exists'
def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))

final = read(candidate / '.tmp/progressive-preview-proof-r1295.json')
native = read(r / '.tmp/main-continuation-proof-r1273.json')
known = {x['path']: x['sha256'] for x in final['sources']}
known[native['changedSource']['path']] = native['changedSource']['sha256']
assert all(sha(r / path) == digest for path, digest in known.items()), 'Merged source differs from the tested source/fixture combination'
assert sha(candidate / '.tmp/source-r1295-progressive-preview.tgz') == final['sourceSnapshotSha256']
assert sha(candidate / '.tmp/batch-windows-r1295.ps1') == final['runnerSha256']
assert sha(candidate / '.tmp/dsa-batch-windows-r1295.tgz') == final['archiveSha256']
base = 'b39d7361fa7cb30d1adfca073189c07da803af8d'
code_changed = subprocess.check_output(['git', 'diff', '--name-only', base, '--', 'src', 'tests'], text=True).splitlines()
subprocess.run(['git','diff','--quiet','--','src','tests'],check=True)
assert not subprocess.check_output(['git','ls-files','--others','--exclude-standard','src','tests'],text=True).strip()
assert all(path in known for path in code_changed)
# Preserve exact compiler input bytes, including checkout CRLF and the few edited LF files.
# A raw git blob plus this delta archive reconstructs the source without EOL assumptions.
paths = sorted(known)
batch = subprocess.run(['git','cat-file','--batch'],input=''.join(base+':'+p+'\n' for p in paths).encode(),capture_output=True,check=True)
stream = io.BytesIO(batch.stdout); changed = []
for path in paths:
    header = stream.readline().rstrip(b'\n')
    if header.endswith(b' missing'): changed.append(path); continue
    fields = header.split(); assert len(fields) == 3 and fields[1] == b'blob'
    data = stream.read(int(fields[2])); assert stream.read(1) == b'\n'
    if hashlib.sha256(data).hexdigest() != known[path]: changed.append(path)
assert not stream.read()

runs = [('primary', rev, tag) for rev, tags in [('r1255', ['mac','ubuntu','windows']), ('r1266',['mac']),
    ('r1268',['mac']), ('r1269',['ubuntu']), ('r1273',['windows'])] for tag in tags]
runs += [('candidate', rev, tag) for rev, tags in [('r1281',['mac','ubuntu']), ('r1283',['mac','ubuntu']),
    ('r1285',['mac','ubuntu']), ('r1291',['mac','ubuntu','windows']), ('r1295',['windows'])] for tag in tags]
retained = {}; executions = {}; xmls = {}
for workspace, revision, tag in runs:
    root = r if workspace == 'primary' else candidate
    for suffix in ('results.tgz', 'post.json'):
        p = root / f'.tmp/{tag}-batch-{revision}-{suffix}'
        retained[f'{workspace}/{p.name}'] = p
    post = read(root / f'.tmp/{tag}-batch-{revision}-post.json')
    assert not post['filecatOrDotnet'], (revision, tag, 'post-run worker remains')
    archive = root / f'.tmp/{tag}-batch-{revision}-results.tgz'
    with tarfile.open(archive) as packed:
        state = packed.extractfile('state.txt').read().decode('utf-8-sig').strip()
        assert state.startswith('FINISHED exit='), (revision, tag, state)
        for member in packed.getmembers():
            if not member.name.endswith('.xml'): continue
            a = E.fromstring(packed.extractfile(member).read()).find('assembly')
            counts = tuple(int(a.get(k, '-1')) for k in ('total','passed','failed','errors','skipped'))
            assert counts[0] > 0 and counts[3] == 0
            key = f'{tag}/{revision}/{pathlib.PurePosixPath(member.name).stem}'
            xmls[key] = a
            executions[key] = dict(zip(('total','passed','failed','errors','skipped'), counts))
            executions[key].update(seconds=a.get('time'), state=state,
                failures=[dict(name=v.get('name'), message=v.findtext('failure/message'), stack=v.findtext('failure/stack-trace')) for v in a.iter('test') if v.get('result') == 'Fail'],
                skippedCases=[dict(name=v.get('name'), reason=''.join(v.find('reason').itertext())) for v in a.iter('test') if v.get('result') == 'Skip'])

expected = {'windows/r1295/epoch':(23,21,0,0,2), 'windows/r1295/app-rescan':(39,39,0,0,0),
 'windows/r1295/render':(825,824,0,0,1), 'windows/r1295/settings':(1,1,0,0,0),
 'windows/r1273/native':(5,3,0,0,2), 'windows/r1291/settings':(22,20,1,0,1),
 'mac/r1283/epoch':(23,23,0,0,0), 'ubuntu/r1283/epoch':(23,23,0,0,0),
 'mac/r1285/render':(825,825,0,0,0), 'ubuntu/r1285/render':(825,825,0,0,0),
 'mac/r1291/settings':(22,21,0,0,1), 'ubuntu/r1291/settings':(22,20,0,0,2),
 'mac/r1255/core':(11813,11517,0,0,296), 'ubuntu/r1255/core':(11813,11546,0,0,267),
 'windows/r1255/core':(11818,10672,0,0,1146)}
for key, counts in expected.items():
    assert tuple(executions[key][k] for k in ('total','passed','failed','errors','skipped')) == counts, key
assert [v['name'] for v in executions['windows/r1291/settings']['failures']] == [
 'FileCat.App.Tests.StorageHistoryStartupUiTests.Private_Windows_history_is_acknowledged_through_UI_and_retained_summary_survives_restart_without_its_journal']
patterns = ['*NativeRuntimeRescanFactory*','*NativeRescanView*','*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs',
 '*NativeOwnedRuntimeView*','*NativeIndexedComparisonCanceledMappingAndAcquisitionPublishNoPartialTab',
 '*NonrespondingPreparationFreezesBindingAndNeverStartsAScanAfterReturning','*NativeProgressivePreview*']
for tag in ('mac','ubuntu'):
    tests = [v for v in xmls[f'{tag}/r1285/render'].iter('test') if any(fnmatch.fnmatchcase(v.get('method'), p) for p in patterns)]
    assert len(tests) == 39 and all(v.get('result') == 'Pass' for v in tests)
    executions[f'{tag}/r1285/render']['focusedLifecycleCasesPassed'] = 39

workflow = {}
for root, label, proof_names in [(r,'primary',['main-integration-proof-r1255.json','main-continuation-proof-r1266.json','main-continuation-proof-r1268.json','main-continuation-proof-r1269.json','main-continuation-proof-r1273.json']),
 (candidate,'candidate',['progressive-preview-proof-r1281.json','progressive-preview-proof-r1283.json','progressive-preview-proof-r1285.json','progressive-preview-proof-r1291.json','progressive-preview-proof-r1291-unexecuted-draft.json','progressive-preview-proof-r1295.json','native-apphost-proof-r1280.json'])]:
    for name in proof_names: workflow[f'{label}/{name}'] = root / '.tmp' / name
for root, label, names in [(r,'primary',['main-integration-build-r1254-core.txt','main-integration-build-r1254-native.txt','main-integration-build-r1254-render.txt','main-integration-build-r1254-app.txt','main-probe-fixture-build-r1272.txt','main-app-fixture-final-build-r1288.txt']),
 (candidate,'candidate',['progressive-frozen-build-r1261-core.txt','progressive-accessibility-final-build-r1282.txt','progressive-app-final-build-r1282.txt','progressive-capacity-control-build-r1284.txt','progressive-app-fixtures-build-r1290.txt','progressive-storage-ready-build-r1294.txt','apphost-r1280-mac','apphost-r1280-linux'])]:
    for name in names: workflow[f'{label}/{name}'] = root / '.tmp' / name
for workspace, revision, tag in runs:
    root = r if workspace == 'primary' else candidate; platform = 'linux' if tag == 'ubuntu' else tag
    script = root / f'.tmp/batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
    workflow[f'{workspace}/{script.name}'] = script
for p in (d / 'drivers').iterdir():
    if p.name in {'deploy-batch.ps1','collect-rescan-ui-r1216.ps1','preflight-indexed-r732.ps1','finalize-progressive-main-r1298.py','clone-progressive-payload-r1283.py'} or p.name.startswith(('prepare-progressive-', 'prepare-main-continuation-r12', 'prepare-main-integration-r1255', 'cached-main-continuation-r12')):
        workflow['drivers/' + p.name] = p
assert all(p.is_file() for p in workflow.values())
manifest = dict(baseCommit=base, mergedSourceCommit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
 mergedMain='40480c8e', sources=[dict(path=p,sha256=known[p]) for p in sorted(changed)], codeChangedPaths=code_changed,
 sourceBytePolicy='Exact raw git blobs plus byte deltas; checkout line-ending differences are retained.', executions=executions,
 scope='Main integration regressions and internal progressive preview, full-recorded-result handoff, transactional ownership and dynamic live-region names. Failed originals remain visible; scoped reruns resolve their known findings.',
 publicFeatureRegistered=False, fullIndexedDeferredSelectionComplete=False, fullW1Complete=False,
 physicalDesktopAccessibilityQualified=False, fullGoalComplete=False, finalIndependentReviewPerformed=False,
 nativeFixtureOverride=native['changedSource'], receiptFiles=[dict(path=n,sha256=sha(p)) for n,p in sorted(retained.items())],
 workflowFiles=[dict(path=n,sha256=sha(p)) for n,p in sorted(workflow.items())])
# Everything above is validated before any immutable output is written.
with tarfile.open(outputs[0], 'w:gz') as out:
    for path in sorted(changed):
        data = (r / path).read_bytes(); member=tarfile.TarInfo(path);member.size=len(data);member.mode=0o600;out.addfile(member,io.BytesIO(data))
for output, files in ((outputs[1],retained),(outputs[2],workflow)):
    with tarfile.open(output,'w:gz') as out:
        for name,p in sorted(files.items()): out.add(p,arcname=name,recursive=False)
manifest['archives']=[dict(path=p.name,sha256=sha(p),bytes=p.stat().st_size) for p in outputs]
target.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(json.dumps(dict(executedPhases=len(executions),sourceDeltas=len(changed),retainedRuns=len(runs))))
