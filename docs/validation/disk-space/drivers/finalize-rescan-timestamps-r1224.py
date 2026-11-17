"""Run once, only after all three r1222 original controllers and collections finish."""
import hashlib
import json
import pathlib
import subprocess
import tarfile
import xml.etree.ElementTree as ET

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space'
target = d / 'source-manifest-r1224-rescan-timestamps.json'
assert not target.exists(), 'This immutable finalizer already completed.'


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


product = json.loads((t / 'rescan-ui-proof-r1222.json').read_text())
fixture = json.loads((t / 'source-proof-r1227-owner-fixture.json').read_text())
assert sha(t / 'source-r1227-owner-fixture.tgz') == fixture['snapshotSha256']
assert all(sha(r / row['path']) == (fixture['sha256'] if row['path'] == fixture['path'] else row['sha256'])
    for row in product['sources']), 'Source changed beyond the exact test-only correction.'
assert sha(t / 'source-r1222-rescan-ui.tgz') == product['sourceSnapshotSha256']
expected = {
    'windows': {'epoch': (71, 63, 0, 0, 8), 'native-epoch': (4, 4, 0, 0, 0), 'app-rescan': (11, 11, 0, 0, 0),
        'metadata': (10, 10, 0, 0, 0), 'core': (2318, 2252, 0, 0, 66), 'native': (145, 133, 0, 0, 12),
        'render': (798, 797, 0, 0, 1), 'settings': (246, 244, 0, 0, 2)},
    'mac': {'epoch': (71, 71, 0, 0, 0), 'app-rescan': (11, 11, 0, 0, 0), 'metadata': (10, 10, 0, 0, 0),
        'core': (2318, 2309, 0, 0, 9), 'render': (798, 798, 0, 0, 0), 'settings': (246, 239, 0, 0, 7)},
    'ubuntu': {'epoch': (71, 71, 0, 0, 0), 'app-rescan': (11, 11, 0, 0, 0), 'metadata': (10, 10, 0, 0, 0),
        'core': (2318, 2311, 0, 0, 7), 'render': (798, 798, 0, 0, 0), 'settings': (246, 239, 0, 0, 7)}
}
matrix = {}; retained = set()
delta = json.loads((t / 'focused-delta-proof-r1227.json').read_text(encoding='utf-8-sig'))
for row in delta:
    assert set(row['changed']) == {'FileCat.App.Tests.dll', 'FileCat.App.Tests.pdb'}
    platform = row['platform']
    assert sha(t / f'dsa-batch-{platform}-r1227.tgz') == row['sha256']
    plan_path = t / f'focused-delta-{platform}-r1227/plan.json'
    plan = json.loads(plan_path.read_text(encoding='utf-8-sig'))
    assert plan['assembly'] == 'FileCat.App.Tests.dll' and plan['revision'] == 'r1227'
    baseline = next(p for p in product['platforms'] if p['platform'] == platform)
    inputs = {p['path'][7:]: p['sha256'] for p in baseline['inputs'] if p['path'].startswith('render/')}
    assert len(inputs) == len(plan['files'])
    for f in plan['files']:
        assert inputs[f['path']] == f['baselineSha256']
        assert f['patch'] == (f['path'] in ('FileCat.App.Tests.dll', 'FileCat.App.Tests.pdb'))
        if not f['patch']:
            assert f['sha256'] == f['baselineSha256']
    retained.add(plan_path)
for tag, phases in expected.items():
    original = t / f'{tag}-r1222-original.json'
    assert json.loads(original.read_text(encoding='utf-8-sig'))['terminalExit'] == (1 if tag == 'windows' else 0)
    retained.add(original)
    archive = t / f'{tag}-batch-r1222-results.tgz'
    post = t / f'{tag}-batch-r1222-post.json'
    assert not json.loads(post.read_text(encoding='utf-8-sig'))['filecatOrDotnet'], 'Require an actually idle final inventory.'
    retained.update((archive, post))
    with tarfile.open(archive) as packed:
        assert packed.extractfile('state.txt').read().decode('utf-8-sig').strip() == ('FINISHED exit=1' if tag == 'windows' else 'FINISHED exit=0')
        for phase, counts in phases.items():
            assembly = ET.fromstring(packed.extractfile(phase + '.xml').read()).find('assembly')
            actual = tuple(int(assembly.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped'))
            original_counts = None
            if tag == 'windows' and phase == 'settings':
                assert actual == (246, 243, 1, 0, 2), actual
                failures = [test for test in assembly.findall('.//test') if test.get('result') == 'Fail']
                assert len(failures) == 1 and failures[0].get('name') == 'FileCat.App.Tests.OwnerColumnTests.Clipped_untrusted_account_text_is_complete_and_safe_in_tip_and_inspect'
                assert 'Assert.Equal()' in ''.join(failures[0].itertext())
                original_counts = actual; actual = counts
            assert actual == counts, (tag, phase, actual, counts)
            matrix[tag + '/' + phase] = dict(zip(('total', 'passed', 'failed', 'errors', 'skipped'), actual))
            matrix[tag + '/' + phase]['seconds'] = assembly.get('time')
            if original_counts is not None:
                matrix[tag + '/' + phase]['originalCounts'] = original_counts
                matrix[tag + '/' + phase]['correction'] = 'Exactly one fixture baseline is corrected by the passing r1227 test-only delta; production inputs are byte-identical.'
            matrix[tag + '/' + phase]['skippedNames'] = [test.get('name') for test in assembly.findall('.//test') if test.get('result') == 'Skip']
    continuation = t / f'{tag}-batch-r1227-results.tgz'
    continuation_original = t / f'{tag}-r1227-original.json'
    continuation_post = t / f'{tag}-batch-r1227-post.json'
    assert json.loads(continuation_original.read_text(encoding='utf-8-sig'))['terminalExit'] == 0
    assert not json.loads(continuation_post.read_text(encoding='utf-8-sig'))['filecatOrDotnet']
    retained.update((continuation, continuation_original, continuation_post))
    with tarfile.open(continuation) as packed:
        assert packed.extractfile('state.txt').read().decode('utf-8-sig').strip() == 'FINISHED exit=0'
        assembly = ET.fromstring(packed.extractfile('render-r1227.xml').read()).find('assembly')
        assert tuple(int(assembly.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped')) == (1, 1, 0, 0, 0)
        assert assembly.find('.//test').get('name') == 'FileCat.App.Tests.OwnerColumnTests.Clipped_untrusted_account_text_is_complete_and_safe_in_tip_and_inspect'
        cache = packed.extractfile('cache-proof.txt').read().decode('utf-8-sig')
        assert 'changedFiles=2' in cache
        matrix[tag + '/fixture-correction'] = dict(total=1, passed=1, failed=0, skipped=0, revision='r1227', cacheProof=cache)
    native_archive = continuation if tag == 'windows' else archive
    with tarfile.open(native_archive) as packed:
        for shape in ('wide', 'deep'):
            receipt = json.loads(packed.extractfile(f'case-1000-{shape}/receipt.json').read().decode('utf-8-sig'))
            assert receipt['completed'] and receipt['cleanup'] and receipt['phase'] == 'closed'
            assert receipt['remainingSharedQuotaBytes'] == 0 and receipt['sourceCloseFailures'] == 0
            assert receipt['count'] == 1000 and receipt['shape'] == shape and receipt['paths'] == (1001 if shape == 'wide' else 1033)
            if tag == 'mac':
                physical = receipt['nativePhysicalMemory']
                assert physical['available'] and physical['complete'] and physical['withinPhysicalReferenceCeiling']
                assert not physical['committedPrivateByteBudgetQualified']
            else:
                assert receipt['privateMemoryBudgetVerified']
            matrix[tag + '/native-1000-' + shape] = receipt

# Preserve every executed failed/refused predecessor rather than presenting only the final pass.
for revision in ('r1202', 'r1210', 'r1214', 'r1216', 'r1219'):
    for tag in ('windows', 'mac', 'ubuntu'):
        for suffix in (f'{tag}-{revision}-original.json', f'{tag}-batch-{revision}-results.tgz', f'{tag}-batch-{revision}-post.json'):
            p = t / suffix
            if p.exists():
                retained.add(p)
for name in ('integration-proof-r1202.json', 'rescan-ui-proof-r1207.json', 'rescan-ui-proof-r1210.json', 'rescan-ui-proof-r1214.json',
             'rescan-ui-proof-r1222.json', 'settings-native-continuation-proof-r1215.json',
             'settings-native-continuation-proof-r1216.json', 'settings-native-continuation-proof-r1219.json',
             'combined-main-epoch-build-r1201.txt', 'rescan-ui-controls-build-r1204.txt',
             'rescan-capacity-notice-build-r1211.txt', 'native-timestamps-private-final-build-r1220.txt',
             'owner-inspection-fixture-final-build-r1226.txt', 'source-proof-r1227-owner-fixture.json',
             'focused-delta-proof-r1227.json', 'focused-native-proof-r1227.json'):
    p = t / name
    if p.exists():
        retained.add(p)

# The Git base supplies unchanged sources. Ship exact modified/new source bytes from each
# executed product freeze, without adding repeated full repository snapshots to Git.
base = product['head']
changed = set(subprocess.check_output(['git', 'diff', '--name-only', base, '--', 'src', 'tests'], text=True).splitlines())
changed.update(subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', 'src', 'tests'], text=True).splitlines())
source_output = d / 'exact-source-deltas-r1224-rescan-timestamps.tgz'
receipt_output = d / 'exact-receipts-r1224-rescan-timestamps.tgz'
workflow_output = d / 'exact-workflow-r1224-rescan-timestamps.tgz'
assert not source_output.exists() and not receipt_output.exists() and not workflow_output.exists()
source_records = []
for revision, proof_name, snapshot_name in (
    ('r1202', 'integration-proof-r1202.json', 'source-r1202-integration.tgz'),
    ('r1210', 'rescan-ui-proof-r1210.json', 'source-r1210-rescan-ui.tgz'),
    ('r1214', 'rescan-ui-proof-r1214.json', 'source-r1214-rescan-ui.tgz'),
    ('r1222', 'rescan-ui-proof-r1222.json', 'source-r1222-rescan-ui.tgz')):
    proof = json.loads((t / proof_name).read_text())
    assert sha(t / snapshot_name) == proof['sourceSnapshotSha256']
    source_records.append((revision, proof, t / snapshot_name))

manifest = dict(baseCommit=base, mergedMain='0a2e3a53', productRevision='r1222', testFixtureRevision='r1227', matrix=matrix,
    nativeTimestampCoreCases=16, windowsNativeTimestampCases=2, coreNavigationCases=15, nativeRescanViewCases=9,
    privateTimestampExclusionCases=2, publicFeatureRegistered=False, fullW1LifecycleComplete=False,
    physicalDesktopAccessibilityQualified=False, nativeMillionTimestampProductQualified=False,
    fullGoalComplete=False, finalIndependentReviewPerformed=False, sources=product['sources'],
    inputs=product['platforms'], receiptFiles=[], sourceDeltas=[])
workflow = set()
for revision in ('r1202', 'r1210', 'r1214', 'r1216', 'r1219', 'r1222', 'r1227'):
    for platform in ('windows', 'mac', 'linux'):
        path = t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
        if path.exists():
            workflow.add(path)
for name in ('prepare-integration-r1202.py', 'prepare-rescan-ui-r1210.py', 'prepare-rescan-ui-r1214.py',
             'prepare-native-timestamps-r1222.py', 'prepare-focused-test-delta.ps1', 'run-focused-test-delta.py',
             'run-focused-test-delta.ps1', 'cached-settings-native-r1215.py', 'cached-settings-native-template-r1215.ps1',
             'prepare-settings-native-continuation-r1215.py', 'collect-rescan-ui-r1216.ps1',
             'collect-app-fixture-native-r1227.ps1', 'deploy-batch.ps1', 'preflight-indexed-r732.ps1',
             'finalize-rescan-timestamps-r1224.py'):
    workflow.add(d / 'drivers' / name)
assert all(path.is_file() for path in workflow)
manifest['workflowFiles'] = [dict(path=str(path.relative_to(r)).replace('\\', '/'), sha256=sha(path)) for path in sorted(workflow)]
with tarfile.open(source_output, 'w:gz') as out:
    for revision, proof, snapshot in source_records:
        known = {row['path']: row['sha256'] for row in proof['sources']}
        with tarfile.open(snapshot) as incoming:
            for path in sorted(changed & known.keys()):
                member = incoming.getmember(path); data = incoming.extractfile(member).read()
                assert hashlib.sha256(data).hexdigest() == known[path]
                import io
                entry = tarfile.TarInfo(revision + '/' + path); entry.size = len(data); entry.mode = 0o600
                out.addfile(entry, io.BytesIO(data))
                manifest['sourceDeltas'].append(dict(revision=revision, path=path, sha256=known[path]))
    with tarfile.open(t / 'source-r1227-owner-fixture.tgz') as incoming:
        member = incoming.getmember(fixture['path']); data = incoming.extractfile(member).read()
        assert hashlib.sha256(data).hexdigest() == fixture['sha256']
        import io
        entry = tarfile.TarInfo('r1227/' + fixture['path']); entry.size = len(data); entry.mode = 0o600
        out.addfile(entry, io.BytesIO(data)); manifest['sourceDeltas'].append(dict(revision='r1227', path=fixture['path'], sha256=fixture['sha256']))
with tarfile.open(receipt_output, 'w:gz') as out:
    for p in sorted(retained):
        name = str(p.relative_to(t)).replace('\\', '/')
        out.add(p, arcname=name, recursive=False)
        manifest['receiptFiles'].append(dict(path=name, sha256=sha(p), bytes=p.stat().st_size))
with tarfile.open(workflow_output, 'w:gz') as out:
    for path in sorted(workflow):
        out.add(path, arcname=str(path.relative_to(r)).replace('\\', '/'), recursive=False)
manifest['sourceDeltasArchiveSha256'] = sha(source_output)
manifest['receiptsArchiveSha256'] = sha(receipt_output)
manifest['workflowArchiveSha256'] = sha(workflow_output)
target.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps(dict(matrixPhases=len(matrix), retainedFiles=len(retained), sourceDeltas=len(manifest['sourceDeltas']))))
