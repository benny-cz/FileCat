"""Execute once, only after all original mixed-cohort controllers have ended."""
import datetime
import hashlib
import importlib.util
import json
import pathlib
import tarfile
import xml.etree.ElementTree as ET

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1190-native-scale.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
product = e.read(temp / 'native-optimization-proof-r1184.json')
fixture = e.read(temp / 'native-locality-fixture-fix-proof-r1185.json')
assert fixture['productCoreSha256'] == product['productCoreSha256'] and not fixture['productRebuilt']
assert fixture['sourceChanges'] == ['tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.Locality.cs']
assert all(e.sha(root / row['path']) == row['sha256'] for row in fixture['sources'])
assert e.sha(temp / 'source-r1184-native-optimization.tgz') == product['sourceSnapshotSha256']
assert e.sha(temp / 'source-r1185-locality-fixture-fix.tgz') == fixture['sourceSnapshotSha256']
e.assert_clean_build(temp / 'native-graph-locality-build-r1184.txt', 4)
e.assert_clean_build(temp / 'native-locality-fixture-fix-build-r1185.txt', 1)
files = {temp / name for name in ('native-optimization-proof-r1181.json', 'source-r1181-native-optimization.tgz',
    'native-optimization-proof-r1184.json', 'source-r1184-native-optimization.tgz',
    'native-locality-fixture-fix-proof-r1185.json', 'source-r1185-locality-fixture-fix.tgz',
    'native-graph-locality-build-r1184.txt', 'native-locality-fixture-fix-build-r1185.txt',
    'native-mac-scale-repeat-proof-r1189.json', 'native-deep-reuse-proof-r1187.json')}
runners = set()
matrix = {}
archives = {}


def decode(data):
    return data.decode('utf-16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')


def result(platform, revision, terminal_exit):
    key = platform + '/' + revision
    if key in archives:
        return archives[key]
    original = temp / f'{platform}-batch-{revision}-original-run.json'
    controller = e.read(original)
    assert controller['terminal'] and controller['exitCode'] == terminal_exit, controller
    path = temp / f'{platform}-batch-{revision}-results.tgz'
    values = {}
    with tarfile.open(path) as archive:
        members = archive.getmembers()
        assert len(members) <= 1000 and sum(m.size for m in members) <= 256 * 1024 * 1024
        for member in members:
            name = pathlib.PurePosixPath(member.name)
            assert not name.is_absolute() and '..' not in name.parts and ':' not in member.name
            assert member.isfile() or member.isdir()
            if member.isfile():
                assert member.name not in values
                values[member.name] = archive.extractfile(member).read()
    post = temp / f'{platform}-batch-{revision}-post.json'
    assert not e.read(post)['filecatOrDotnet']
    if terminal_exit == 0:
        assert decode(values['state.txt']).strip() == 'FINISHED exit=0'
    files.update((original, path, post))
    tag = 'linux' if platform == 'ubuntu' else platform
    runners.add(temp / f'batch-{tag}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    archives[key] = values
    return values


def counts(values, name):
    a = ET.fromstring(values[name]).find('assembly')
    assert a is not None
    return tuple(int(a.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped'))


def scale(platform, revision, count, shape, terminal_exit):
    values = result(platform, revision, terminal_exit)
    prefix = f'case-{count}-{shape}/'
    assert counts(values, prefix + 'tests.xml') == (1, 1, 0, 0, 0)
    r = json.loads(decode(values[prefix + 'receipt.json']))
    assert r['count'] == count and r['shape'] == shape and r['completed'] and r['cleanup']
    assert r['phase'] == 'closed' and r['failure'] is None
    assert r['sourceCloseFailures'] == r['remainingSharedQuotaBytes'] == 0
    assert r['paths'] == count + (33 if shape == 'deep' else 1)
    assert r['progressReports'] > 0 and r['firstObservationMilliseconds'] > 0
    assert {p['phase'] for p in r['phases']} == {'fixture', 'logical-baseline', 'native-acquisition',
        'verify-captured-paths', 'hierarchy-sort-charts', 'filtered-unique', 'extension-grouping', 'actual-close'}
    if platform == 'mac':
        m = r['nativePhysicalMemory']
        assert m['available'] and m['complete'] and m['withinPhysicalReferenceCeiling']
        assert not m['committedPrivateByteBudgetQualified'] and r['baselinePrivateBytes'] is None
    else:
        assert r['privateMemoryBudgetVerified'] and r['sampledPeakPrivateIncrementBytes'] <= 512 * 1024 * 1024
    if count >= 1_000_000:
        assert r['retainedSource']
    matrix[f'{platform}/{count}-{shape}'] = dict(revision=revision, **r)


for platform in ('windows', 'mac', 'ubuntu'):
    revision = 'r1185'
    values = result(platform, revision, 0 if platform == 'ubuntu' else 1)
    for phase, total, skipped in [('smoke', 6, 0), ('locality', 7, 0),
        ('native-control', 4, 4 if platform == 'windows' else 0),
        ('core', 2009, 52 if platform == 'windows' else 6),
        ('render', 779, 1 if platform == 'windows' else 0),
        ('settings', 36, 1 if platform == 'windows' else 0)]:
        assert counts(values, phase + '.xml') == (total, total - skipped, 0, 0, skipped)
        matrix[f'{platform}/{phase}'] = dict(revision=revision, counts=counts(values, phase + '.xml'))
    if platform == 'windows':
        assert counts(values, 'native.xml') == (75, 75, 0, 0, 0)
        assert 'case-1000000-deep/planned-phase-boundary.json' in values
        assert 'case-1000000-deep/tests.xml' not in values
    elif platform == 'mac':
        interrupted = json.loads(decode(values['case-1000000-wide/receipt.json']))
        assert not interrupted['completed'] and interrupted['cleanup'] and interrupted['remainingSharedQuotaBytes'] == 0
        assert 'OperationCanceledException' in interrupted['failure']
    for shape in ('wide', 'deep'):
        scale(platform, revision, 1000, shape, 0 if platform == 'ubuntu' else 1)
        large_revision = 'r1189' if platform == 'mac' else 'r1187' if platform == 'windows' and shape == 'deep' else revision
        scale(platform, large_revision, 1_000_000, shape, 1 if platform == 'windows' and shape == 'wide' else 0)

for platform in ('windows', 'mac', 'linux'):
    candidate = next(p for p in fixture['platforms'] if p['platform'] == platform)
    stage = temp / f'batch-r1185-{platform}'
    assert {p.relative_to(stage).as_posix(): e.sha(p) for p in stage.rglob('*') if p.is_file()} == {p['path']: p['sha256'] for p in candidate['inputs']}
    assert e.sha(temp / f'dsa-batch-{platform}-r1185.tgz') == candidate['archiveSha256']
    assert candidate['changedInputs'] == [f'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.{ext}' for ext in ('dll', 'pdb')]

record = dict(revision='r1190', observedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    productRevision='r1184', fixtureRevision='r1185', sources=fixture['sources'], inputs=fixture['platforms'],
    matrix=matrix, interruptedMacGroupingIsInconclusive=True,
    qualification=dict(nativeMillionWideDeep=True, completeTenMillion=False, globalMultiSessionMemory=False,
        macCommittedPrivateBytes=False, physicalInputA11yPackageMatrix=False, publicFeatureRegistered=False,
        fullGoalComplete=False, finalIndependentReviewPerformed=False))
deep_reuse = e.read(temp / 'native-deep-reuse-proof-r1187.json')
assert deep_reuse['productCoreSha256'] == fixture['productCoreSha256']
assert e.sha(temp / 'dsa-batch-windows-r1187.tgz') == deep_reuse['archiveSha256']
assert e.sha(temp / 'batch-windows-r1187.ps1') == deep_reuse['runnerSha256']
win_inputs = next(p for p in fixture['platforms'] if p['platform'] == 'windows')['inputs']
assert {p['path']: p['sha256'] for p in deep_reuse['reusedRenderInputs']} == {
    p['path'][7:]: p['sha256'] for p in win_inputs if p['path'].startswith('render/')}
win_deep = archives['windows/r1187']
reshape = json.loads(decode(win_deep['reshape.json']))
assert reshape['phase'] == 'complete'
assert reshape['priorReceiptSha256'].lower() == hashlib.sha256(archives['windows/r1185']['case-1000000-wide/receipt.json']).hexdigest()
assert 'Unknown owned batch' in json.loads(decode(win_deep['reshape-negative.json.error.json']))['error']
for revision in ('r1185', 'r1187'):
    values = archives['windows/' + revision]
    task = json.loads(decode(values['task-outcome.json']))
    environment = json.loads(decode(values['environment.json']))
    assert task['terminalExit'] == (1 if revision == 'r1185' else 0)
    assert task['logonType'] == 'Interactive' and task['runLevel'] == 'Limited'
    assert environment['session'] > 0 and environment['user'].lower().endswith('\\filecatdsa_r2')
repeat = e.read(temp / 'native-mac-scale-repeat-proof-r1189.json')
assert repeat['archiveByteIdentical'] and not repeat['productAndFixtureRebuilt']
assert repeat['archiveSha256'] == next(p for p in fixture['platforms'] if p['platform'] == 'mac')['archiveSha256']
assert e.sha(temp / 'batch-mac-r1189.sh') == repeat['runnerSha256']
assert e.sha(temp / 'dsa-batch-mac-r1189.tgz') == repeat['archiveSha256']
record['windowsDeepFixtureReuse'] = dict(proof=deep_reuse, actualReshape=reshape,
    originalWindowsBatchNonzeroIsPreserved=True, originalDeepCaseWasNotRun=True)
record['originalMacGroupingCancellation'] = dict(revision='r1185',
    reason='Cancelled during active external merge after 310 seconds; not evidence of a hang',
    receipt=json.loads(decode(archives['mac/r1185']['case-1000000-wide/receipt.json'])))

# Keep baseline successes, original failures, controls and their actual terminal records.
# None of the interrupted million-file baselines is promoted to a passing scale result.
baseline = {}
for platform in ('windows', 'mac', 'ubuntu'):
    values = e.reports(temp / f'{platform}-batch-r1184-results.tgz')
    assert counts(values, 'smoke.xml') == (6, 6, 0, 0, 0)
    assert counts(values, 'locality.xml') == (7, 3, 4, 0, 0)
    for revision in ('r1175', 'r1184'):
        for suffix in ('results.tgz', 'original-run.json', 'post.json'):
            files.add(temp / f'{platform}-batch-{revision}-{suffix}')
    base = temp / f'{platform}-batch-r1175-results'
    baseline[platform] = []
    for count in (1000, 100000):
        for shape in ('wide', 'deep'):
            receipt = e.read(base / f'case-{count}-{shape}/receipt.json')
            assert receipt['completed'] and receipt['cleanup'] and receipt['remainingSharedQuotaBytes'] == 0
            baseline[platform].append(receipt)
old_mac = e.reports(temp / 'mac-batch-r1182-results.tgz')
assert counts(old_mac, 'old-control.xml') == (5, 3, 2, 0, 0)
failed = ET.fromstring(old_mac['old-control.xml']).findall('.//test[@result="Fail"]')
assert {x.get('name').split('.')[-1] for x in failed} == {
    'NativeDeepSiblingEnumerationHasBoundedDescriptorChurn',
    'PermutedNativeIdentitiesProduceOrderedCanonicalObjectsWithExactAliasesAndBoundedReads'}
record['oldProductSmallNativeBaselines'] = baseline
record['oldProductNegativeControl'] = dict(revision='r1182', counts=(5, 3, 2, 0, 0),
    expectedFailures=[x.get('name') for x in failed], smallReadAmplificationWasNotEstablished=True)
for revision in ('r1181', 'r1182'):
    for suffix in ('results.tgz', 'original-run.json', 'post.json'):
        files.add(temp / f'mac-batch-{revision}-{suffix}')
files.update(temp / name for name in ('native-acquisition-proof-r1175.json',
    'native-optimization-validator-fix-r1182.json', 'old-relative-control-r1176-proof.json',
    'mac-footprint-counter-control-r1171b.json'))
helpers = ('native_batch_evidence.py', 'finalize-native-scale-r1190.py', 'collect-native-optimization-r1181.ps1',
    'prepare-native-optimization-r1181.py', 'prepare-native-optimization-r1184.py',
    'prepare-native-locality-fixture-r1185.py', 'prepare-native-deep-reuse-r1187.py',
    'reshape-native-fixture-windows-r1186.ps1', 'native-deep-reuse-windows-template-r1186.ps1')
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-receipt-text-r1190-native-scale.tgz')
destination.write_text(json.dumps(record, indent=2) + '\n')
print('Verified mixed native scale cohort:', destination)
