"""Verify real guest comparison UI cohorts, immutable inputs and fixture-only reruns."""
import datetime
import hashlib
import importlib.util
import io
import json
import pathlib
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1138-indexed-comparison-ui.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('native_evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)

old = e.read(temp / 'source-candidate-r1136.json')
new = e.read(temp / 'source-candidate-r1138.json')
a = {r['path']: r['sha256'] for r in old}
b = {r['path']: r['sha256'] for r in new}
fixture = 'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.IndexedComparisonUi.cs'
assert a.keys() == b.keys() and [p for p in a if a[p] != b[p]] == [fixture]
current = (root / fixture).read_bytes()
addition = b'                await Until(() => ComparisonControl<ListBox>(window, "Captured comparison rows")?.ItemCount > 0);\n                Frame(window);\n'
assert current.count(addition) == 1
original = current.replace(addition, b'', 1)
assert hashlib.sha256(original).hexdigest() == a[fixture]

snapshot = temp / 'source-r1136-indexed-comparison-ui.tgz'
if not snapshot.exists():
    with tarfile.open(snapshot, 'w:gz') as archive:
        for row in old:
            data = original if row['path'] == fixture else (root / row['path']).read_bytes()
            assert hashlib.sha256(data).hexdigest() == row['sha256']
            member = tarfile.TarInfo(row['path']); member.size = len(data)
            archive.addfile(member, io.BytesIO(data))
with tarfile.open(snapshot) as archive:
    assert len(archive.getmembers()) == len(old)
    assert all(hashlib.sha256(archive.extractfile(p).read()).hexdigest() == digest for p, digest in a.items())

proof = e.read(temp / 'test-fix-proof-r1138.json')
assert proof['scope'] == 'TestOnly' and proof['baseline'] == 'r1137'
plans, archives = {}, []
for row in proof['platforms']:
    platform = row['platform']
    assert row['changed'] == ['render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb']
    assert row['guestReuse'] == (platform != 'windows')
    archive = temp / f'dsa-batch-{platform}-r1138.tgz'
    assert e.sha(archive) == row['sha256']
    archives.append({'platform': platform, 'path': str(archive), 'bytes': row['archiveBytes'], 'sha256': row['sha256']})
    if platform != 'windows':
        plan = e.read(temp / f'test-fix-delta-{platform}-r1138/plan.json')
        assert [p['path'] for p in plan['files'] if p['patch']] == row['changed']
        assert all(p['sha256'] == p['baselineSha256'] for p in plan['files'] if not p['patch'])
        plans[platform] = plan
    baseline = temp / f'batch-r1137-{platform}'
    stage = temp / f'batch-r1138-{platform}'
    for path in baseline.rglob('*'):
        if path.is_file() and path.relative_to(baseline).as_posix() not in row['changed']:
            assert e.sha(path) == e.sha(stage / path.relative_to(baseline))
packing = {'revision': 'r1138', 'renderRevision': 'r1138', 'sourceManifest': '.tmp/source-candidate-r1138.json',
           'buildLog': '.tmp/indexed-comparison-ui-fixture-build-r1138.txt', 'archiveCount': 3,
           'standaloneArchives': 0, 'elapsedSeconds': proof['elapsedSeconds'], 'archives': archives,
           'cachedProductBaseline': 'r1137', 'scope': 'TestOnly'}
(temp / 'pack-timing-r1138.json').write_text(json.dumps(packing, indent=2) + '\n', encoding='utf-8')
app_inputs = {p: [{'path': f.relative_to(temp / f'batch-r1138-{p}').as_posix(), 'sha256': e.sha(f)}
                  for f in sorted((temp / f'batch-r1138-{p}/render').glob('FileCat.App.Tests.*'))]
              for p in ('windows', 'mac', 'linux')}
assert all(app_inputs.values())
(temp / 'app-settings-inputs-r1138.json').write_text(json.dumps(app_inputs, indent=2) + '\n', encoding='utf-8')
expected = {
    'windows': {'preview': (21, 0), 'core': (1770, 48), 'native': (75, 0), 'render': (701, 1), 'appsettings': (36, 1)},
    'mac': {'preview': (21, 0), 'core': (1752, 5), 'render': (701, 0), 'appsettings': (36, 0)},
    'ubuntu': {'preview': (21, 0), 'core': (1752, 5), 'render': (701, 0), 'appsettings': (36, 0)}}
prefixes = ('NativeIndexedComparisonCatalogRoute', 'NativeIndexedComparisonMetricChanges',
            'NativeIndexedComparisonCanceledMapping', 'NativeIndexedComparisonAliasMembership',
            'NativeIndexedComparisonIncompatiblePolicies')
lock_case = 'FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
record, files, runners, receipts = e.qualify(root, 'r1138', expected, prefixes, 9, build_projects=1,
    phase_revisions={f'mac/{phase}': 'r1137' for phase in ('core', 'render', 'appsettings')},
    additional_skips={f'{platform}/core': [lock_case] for platform in ('mac', 'ubuntu')})
for platform in ('windows', 'mac', 'ubuntu'):
    revision = 'r1137' if platform == 'mac' else 'r1138'
    report = e.reports(temp / f'{platform}-batch-{revision}-results.tgz')
    smoke = e.assembly(report, 'smoke', revision)
    assert (smoke.get('total'), smoke.get('passed'), smoke.get('failed'), smoke.get('skipped'), smoke.get('errors')) == ('119', '119', '0', '0', '0')
    record['matrix'][f'{platform}/smoke'] = {'revision': revision, **{k: smoke.get(k) for k in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}}
    original_run = temp / f'{platform}-batch-r1138-original-run.json'
    outcome = e.read(original_run); assert outcome['terminalExit'] == 0 and outcome['sessionId']
    files.add(original_run)
    if platform == 'windows':
        task = json.loads(receipts[platform]['task-outcome.json'].decode('utf-8-sig'))
        assert task['terminalExit'] == 0 and task['logonType'] == 'Interactive' and task['runLevel'] == 'Limited'
    initial_path = temp / f'{platform}-batch-r1134-results.tgz'
    initial = e.reports(initial_path)
    assert initial['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=preview'
    preview = e.assembly(initial, 'preview', 'r1134')
    assert (preview.get('total'), preview.get('passed'), preview.get('failed')) == ('19', '18', '1')
    failed = [t for t in preview.findall('.//test') if t.get('result') == 'Fail']
    assert len(failed) == 1 and failed[0].get('method') == 'NativeIndexedComparisonCanceledMappingAndAcquisitionPublishNoPartialTab'
    assert 'reader limit' in failed[0].findtext('failure/message')
    initial_outcome = temp / f'{platform}-batch-r1134-original-run.json'
    assert e.read(initial_outcome)['terminalExit'] == 1
    files.update((initial_path, initial_outcome, temp / f'{platform}-batch-r1134-post.json'))

for platform, revision in (('windows', 'r1136'), ('ubuntu', 'r1137')):
    path = temp / f'{platform}-batch-{revision}-results.tgz'
    initial = e.reports(path)
    preview = e.assembly(initial, 'preview', revision)
    failed = [t for t in preview.findall('.//test') if t.get('result') == 'Fail']
    assert (preview.get('total'), preview.get('passed'), preview.get('failed')) == ('21', '20', '1')
    assert len(failed) == 1 and failed[0].get('method') == 'NativeIndexedComparisonIncompatiblePoliciesRequireExplicitLimitedReview'
    assert 'limited comparison' in failed[0].findtext('failure/message')
    outcome = temp / f'{platform}-batch-{revision}-original-run.json'
    assert e.read(outcome)['terminalExit'] == 1
    files.update((path, outcome, temp / f'{platform}-batch-{revision}-post.json'))

# Unix r1136 stopped in verification, before any test process. The collection
# inventory captured this task's later r1137 job, which has its own terminal receipt.
for platform in ('mac', 'ubuntu'):
    path = temp / f'{platform}-batch-r1136-results.tgz'
    report = e.reports(path)
    assert not any(name.endswith('.xml') for name in report)
    outcome = temp / f'{platform}-batch-r1136-original-run.json'
    assert e.read(outcome)['terminalExit'] == 1
    inventory = temp / f'{platform}-batch-r1136-post.json'
    later = e.read(inventory)['filecatOrDotnet']
    assert len(later) == 1 and 'r1137' in later[0] and 'dotnet FileCat.' in later[0]
    files.update((path, outcome, inventory))
with tarfile.open(temp / 'dsa-batch-mac-r1136.tgz') as archive:
    previous_verifier = archive.extractfile(next(m for m in archive.getmembers() if m.name.removeprefix('./') == 'reuse.py')).read()
assert b"if allowed != {'render/FileCat.dll'" in previous_verifier
previous_verifier_path = temp / 'cache-verifier-before-coverage-r1136.py'
previous_verifier_path.write_bytes(previous_verifier)
files.add(previous_verifier_path)
record['developmentFailures'] = {
    'r1134': '18/19 per platform; test retained the live explorer plus both same-graph inputs beyond the eight-reader limit. It now verifies preserving refusal and retries through an independent saved-inventory graph.',
    'r1136Unix': 'Cache verifier rejected explicitly declared generated coverage metadata before tests. Corrected verifier uses the same compiled inputs in r1137.',
    'r1136WindowsAndR1137Ubuntu': '20/21; fixture asserted visible comparison text before the new tab attached. r1138 waits for that view and renders a frame; only test DLL/PDB changed.'}
record['qualification'] = {
    'indexedExplorerCatalogComparisonRoute': True, 'wholeCapturedInputsIndependentOfDisplayFilter': True,
    'automaticAndReviewedStructuralRoots': True, 'explicitLimitedComparisonAndMetricReview': True,
    'fourMetricActualCloseAndRetainedInputLifecycle': True, 'pagedRowsAndIndependentAliasInspector': True,
    'cancellationReaderCapPreservingRefusalAndRetry': True, 'newNativeFilesystemHeadlessViewCases': 9,
    'broadSuitesPassedAllThree': True, 'macBroadReuseWithCorrectedFocusedFixtureRerun': True,
    'physicalPointerKeyboardAndAssistiveTechnologyQualified': False,
    'indexedComparisonPredicatesCohortsOrdersChartsReportsAndPersistenceComplete': False,
    'fullLargeComparisonAndResourceQualificationComplete': False,
    'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False, 'taskOwnedProcessesExited': True}
record['cachePlans'] = plans
files.update((snapshot, temp / 'source-r1134-indexed-comparison-ui.tgz', temp / 'source-candidate-r1134.json',
              temp / 'source-candidate-r1136.json', temp / 'source-candidate-r1137.json',
              temp / 'pack-timing-r1134.json', temp / 'test-fix-proof-r1136.json', temp / 'test-fix-proof-r1137.json',
              temp / 'test-fix-proof-r1138.json', temp / 'indexed-comparison-ui-build-r1134.txt',
              temp / 'indexed-comparison-ui-build-r1136.txt', temp / 'mac-batch-r1137-original-run.json'))
assert e.read(temp / 'mac-batch-r1137-original-run.json')['terminalExit'] == 0
helpers = ['native_batch_evidence.py', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py', 'pack-batch-direct.ps1',
           'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'finalize-indexed-comparison-ui-r1138.py']
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-text-r1138-indexed-comparison-ui.tgz')
destination.write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'qualification': record['qualification'], 'matrix': {k: {v: row[v] for v in ('total', 'passed', 'failed', 'skipped')} for k, row in record['matrix'].items()}}, indent=2))
