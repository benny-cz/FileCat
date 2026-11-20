"""Verify all-platform indexed predicates/orders and preserve the diagnosed development cohorts."""
import hashlib
import importlib.util
import json
import pathlib
import tarfile

root = pathlib.Path.cwd(); temp = root / '.tmp'; out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1145-indexed-comparison-query.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('native_evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec); spec.loader.exec_module(e)

# Exact old bytes were frozen before testing, or reconstructed and checked against
# the independently frozen pre-build manifest. They are never substituted for new code.
source_sets = {}
for revision in ('r1141', 'r1142', 'r1143'):
    rows = e.read(temp / f'source-candidate-{revision}.json'); source_sets[revision] = {r['path']: r['sha256'] for r in rows}
    with tarfile.open(temp / f'source-{revision}-indexed-comparison-query.tgz') as archive:
        assert {m.name for m in archive.getmembers()} == set(source_sets[revision])
        assert all(m.isfile() and hashlib.sha256(archive.extractfile(m).read()).hexdigest() == source_sets[revision][m.name] for m in archive.getmembers())
assert source_sets['r1141'].keys() == source_sets['r1142'].keys() == source_sets['r1143'].keys()
fixture = 'tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.IndexedComparisonQuery.cs'
work = 'src/FileCat.Core/Analysis/AnalysisIndexedComparison.QueryWork.cs'
assert [p for p in source_sets['r1141'] if source_sets['r1141'][p] != source_sets['r1142'][p]] == [fixture]
assert [p for p in source_sets['r1142'] if source_sets['r1142'][p] != source_sets['r1143'][p]] == [work, fixture]
with tarfile.open(temp / 'source-r1142-indexed-comparison-query.tgz') as archive:
    original_work = archive.extractfile(work).read()
current_work = (root / work).read_bytes()
assert current_work.replace(b'AnalysisNames.DisplayName(node, int.MaxValue)', b'AnalysisNames.DisplayName(node, AnalysisLimits.RawNameBytes)', 1).replace(
    b'AnalysisNames.Display(binding.CanonicalLocator, int.MaxValue)', b'AnalysisNames.Display(binding.CanonicalLocator, AnalysisLimits.LocatorBytes)', 1) == original_work

app_inputs = {platform: [{'path': p.relative_to(temp / f'batch-r1143-{platform}').as_posix(), 'sha256': e.sha(p)}
                        for p in sorted((temp / f'batch-r1143-{platform}/render').glob('FileCat.App.Tests.*'))]
              for platform in ('windows', 'mac', 'linux')}
assert all(app_inputs.values())
(temp / 'app-settings-inputs-r1143.json').write_text(json.dumps(app_inputs, indent=2) + '\n', encoding='utf-8')
expected = {
    'windows': {'preview': (29, 0), 'core': (1899, 48), 'native': (75, 0), 'appsettings': (36, 1)},
    'mac': {'preview': (29, 0), 'core': (1881, 5), 'render': (709, 0), 'appsettings': (36, 0)},
    'ubuntu': {'preview': (29, 0), 'core': (1881, 5), 'render': (709, 0), 'appsettings': (36, 0)}}
prefixes = ('NativeIndexedComparisonFailedMetric', 'NativeIndexedComparisonAggregatedUnconfirmedClose', 'NativeIndexedComparisonQueryView')
lock_case = 'FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
record, files, runners, receipts = e.qualify(root, 'r1143', expected, prefixes, 8,
    expected_states={'windows': 'FINISHED exit=1'}, source_snapshot='.tmp/source-r1143-indexed-comparison-query.tgz',
    additional_skips={f'{platform}/core': [lock_case] for platform in ('mac', 'ubuntu')})
for platform in ('windows', 'mac', 'ubuntu'):
    smoke = e.assembly(receipts[platform], 'smoke', 'r1143')
    assert (smoke.get('total'), smoke.get('passed'), smoke.get('failed'), smoke.get('skipped'), smoke.get('errors')) == ('248', '248', '0', '0', '0')
    queries = [t for t in smoke.findall('.//test') if t.get('method', '').startswith('IndexedComparisonQuery') or t.get('method') == 'IndexedComparisonReaderCapAndRetainedReaderSurviveOwnerClosureWithoutEarlyReuse']
    assert len(queries) == 129 and all(t.get('result') == 'Pass' for t in queries)
    record['matrix'][f'{platform}/smoke'] = {k: smoke.get(k) for k in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    record['matrix'][f'{platform}/smoke']['newQueryAndReaderCases'] = sorted(t.get('name') for t in queries)
    actual = temp / f'{platform}-batch-r1143-original-run.json'
    assert e.read(actual)['terminalExit'] == (1 if platform == 'windows' else 0) and e.read(actual)['sessionId']
    files.add(actual)
    if platform == 'windows':
        task = json.loads(receipts[platform]['task-outcome.json'].decode('utf-8-sig'))
        assert task['terminalExit'] == 1 and task['logonType'] == 'Interactive' and task['runLevel'] == 'Limited'
    stopped = temp / f'{platform}-batch-r1141-original-run.json'; outcome = e.read(stopped)
    assert outcome['terminalExit'] == 1 and outcome['forcedStopAfterDiagnosedStall']
    old_path = temp / f'{platform}-batch-r1141-results.tgz'; old = e.reports(old_path)
    assert not any(n.endswith('.xml') for n in old), 'Interrupted preflight supplies no passing result claim'
    assert not e.read(temp / f'{platform}-batch-r1141-post.json')['filecatOrDotnet']
    files.update((stopped, old_path, temp / f'{platform}-batch-r1141-post.json'))
    first_path = temp / f'{platform}-batch-r1142-results.tgz'; first = e.reports(first_path)
    a = e.assembly(first, 'smoke', 'r1142'); assert a.get('total') == '248' and a.get('errors') == '0'
    failures = [t for t in a.findall('.//test') if t.get('result') == 'Fail']
    primary = [t for t in failures if 'memory budget is in use' not in t.findtext('failure/message', '')]
    assert len(primary) == 2
    assert {t.get('method') for t in primary} == {'IndexedComparisonQueryOversizedDisplayedPathRefusesExactOrderingAndKeepsTheNumericView', 'IndexedComparisonQueryMatchesMemoryPredicatesOnBothSidesAndAllOrders'}
    assert any('Unknown filter field' in t.findtext('failure/message', '') for t in primary)
    assert any('No exception was thrown' in t.findtext('failure/message', '') for t in primary)
    assert not e.read(temp / f'{platform}-batch-r1142-post.json')['filecatOrDotnet']
    actual = temp / f'{platform}-batch-r1142-original-run.json'; assert e.read(actual)['terminalExit'] == 1
    files.update((first_path, actual, temp / f'{platform}-batch-r1142-post.json'))

with tarfile.open(temp / 'mac-query-stall-diagnostics-r1141.tgz') as archive:
    fields = archive.extractfile('query-stall-fields.txt').read().decode('utf-8')
    exception = archive.extractfile('query-stall-exception.txt').read().decode('utf-8')
    assert 'IndexedComparisonQueryFailureCancellationAndSyntaxRefusalKeepAppliedIndexAndAllowRetry' in fields
    assert '_references' in fields and '_readers' in fields and '_disposed' in fields
    assert 'No exception was thrown' in exception and 'System.IO.IOException' in exception
    mac_stop = json.load(archive.extractfile('owned-stop-r1141.json')); assert mac_stop['pid'] == 24523 and 'r1141/smoke-r1141.xml' in mac_stop['command']
win_stop = e.read(temp / 'windows-owned-stop-r1141.json'); ubuntu_stop = e.read(temp / 'ubuntu-owned-stop-r1141.json')
assert win_stop['forced'] and win_stop['owner'] == 'filecatdsa_r2' and 'batch-r1141' in win_stop['command']
assert ubuntu_stop['pid'] == 196796 and 'r1141/smoke-r1141.xml' in ubuntu_stop['command']
record['developmentFailures'] = {
    'r1141': 'Mac managed heap identified an assertion expecting a read fault on a nonmatching literal path. The unexpected successful query was not disposed and kept comparison close pending. Exact owned children on all three machines were stopped; no XML/passing result is claimed. Corrected controls dispose unexpected results and inject the fault on a guaranteed match.',
    'r1142': 'The oversized-path control exposed ranking by clipped labels. Product correction builds the full escaped name/root before enforcing the exact-key bound. An unsupported mtime fixture field became modified; failed memory-oracle construction now returns its caller-owned lease before propagating the error. The other failures were the resulting oracle-budget cascade.'}
final_sources = e.read(temp / 'source-candidate-r1145.json')
final_hashes = {r['path']: r['sha256'] for r in final_sources}
preparation = 'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.Preparation.cs'
assert final_hashes.keys() == source_sets['r1143'].keys()
assert [p for p in final_hashes if final_hashes[p] != source_sets['r1143'][p]] == [preparation]
with tarfile.open(temp / 'source-r1143-indexed-comparison-query.tgz') as archive:
    original = archive.extractfile(preparation).read().decode('utf-8').replace('\r\n', '\n')
expected_preparation = original.replace('''        using var model = new DiskSpaceViewModel(fixture.Services, _ => source);
        await model.StartAsync([fixture.Root]);''', '''        var model = new DiskSpaceViewModel(fixture.Services, _ => source);
        try
        {
        await model.StartAsync([fixture.Root]);''', 1).replace('''        await model.RebuildAsync(true); Assert.Equal(NamespaceCoverage.Partial, model.Tree!.Snapshot.Coverage);
    }''', '''        await model.RebuildAsync(true); Assert.Equal(NamespaceCoverage.Partial, model.Tree!.Snapshot.Coverage);
        }
        finally
        {
            model.Dispose();
            var closed = await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => model.CloseCompletion);
            Assert.Equal("Captured analysis sources reported unconfirmed closure.", closed.InnerException?.Message);
        }
    }''', 1)
assert expected_preparation == (root / preparation).read_text(encoding='utf-8')
proof = e.read(temp / 'test-fix-proof-r1145.json'); assert proof['scope'] == 'TestOnly' and proof['baseline'] == 'r1143'
focused_cases = {}
for item in proof['platforms']:
    tag = item['platform']; platform = 'ubuntu' if tag == 'linux' else tag
    assert item['guestReuse'] and item['changed'] == ['render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb']
    plan_path = temp / (f'test-fix-delta-{tag}-r1145' if tag != 'windows' else 'test-fix-delta-r1145') / 'plan.json'
    plan = e.read(plan_path)
    retained_plan = temp / f'{platform}-cache-plan-r1145.json'
    retained_plan.write_bytes(plan_path.read_bytes())
    assert [p['path'] for p in plan['files'] if p['patch']] == item['changed']
    assert all(p['sha256'] == p['baselineSha256'] for p in plan['files'] if not p['patch'])
    path = temp / f'{platform}-batch-r1145-results.tgz'; focused = e.reports(path)
    assert focused['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=0'
    a = e.assembly(focused, 'preview', 'r1145')
    assert (a.get('total'), a.get('passed'), a.get('failed'), a.get('skipped'), a.get('errors')) == ('1', '1', '0', '0', '0')
    case = focused_cases[platform] = a.find('.//test')
    assert case.get('method') == 'SourceCloseFailureIsVisibleInTheAnalysisStatus' and case.get('result') == 'Pass'
    post = temp / f'{platform}-batch-r1145-post.json'; assert not e.read(post)['filecatOrDotnet']
    actual = temp / f'{platform}-batch-r1145-original-run.json'; assert e.read(actual)['terminalExit'] == 0
    first_path = temp / f'{platform}-batch-r1144-results.tgz'; first = e.reports(first_path)
    a = e.assembly(first, 'preview', 'r1144'); assert (a.get('total'), a.get('failed')) == ('1', '1')
    assert 'Captured analysis sources reported unconfirmed closure.' in a.findtext('.//failure/message', '')
    old_outcome = temp / f'{platform}-batch-r1144-original-run.json'; assert e.read(old_outcome)['terminalExit'] == 1
    files.update((path, post, actual, first_path, old_outcome, temp / f'{platform}-batch-r1144-post.json', retained_plan))
    runners.add(temp / f'batch-{tag}-r1145.{"ps1" if tag == "windows" else "sh"}')
    record['matrix'][f'{platform}/preparation-focused'] = {'revision': 'r1145', **{k: a for k, a in [('total', '1'), ('passed', '1'), ('failed', '0'), ('skipped', '0')]}}

win_render = e.assembly(receipts['windows'], 'render', 'r1143')
assert (win_render.get('total'), win_render.get('passed'), win_render.get('failed'), win_render.get('skipped')) == ('709', '707', '1', '1')
failed = [t for t in win_render.findall('.//test') if t.get('result') == 'Fail']; assert len(failed) == 1
assert failed[0].get('name') == focused_cases['windows'].get('name')
assert 'analysis-history' in failed[0].findtext('failure/message', '') and 'being used by another process' in failed[0].findtext('failure/message', '')
new_cases = [t for t in win_render.findall('.//test') if t.get('method', '').startswith(prefixes)]
assert len(new_cases) == 8 and all(t.get('result') == 'Pass' for t in new_cases)
skips = sorted(t.get('name') for t in win_render.findall('.//test') if t.get('result') == 'Skip')
baseline = e.ET.parse(out / 'windows-batch-r1036-render-r1036.xml').getroot().find('assembly')
assert skips == sorted(t.get('name') for t in baseline.findall('.//test') if t.get('result') == 'Skip')
record['matrix']['windows/render'] = {'revision': 'r1143 + focused r1145', 'total': '709', 'passed': '708', 'failed': '0', 'skipped': '1',
    'skippedNames': skips, 'original': {k: win_render.get(k) for k in ('total', 'passed', 'failed', 'skipped')},
    'correction': 'Only the preparation control now awaits actual close and asserts its deliberately injected unconfirmed source closure; every other source/product input is unchanged.'}
record['qualificationRevision'] = 'r1145'
record['broadSourceManifest'] = 'source-candidate-r1143.json'
record['sources'] = final_sources
record['finalFocusedInputProof'] = proof
record['developmentFailures']['r1143WindowsPreparation'] = '707 passed, one existing skip and one fixture deletion-sharing failure. The preparation control disposed its model without awaiting actual acquisition/history close. The corrected control awaits close, asserts the deliberately injected unconfirmed source error and passes on all three in focused r1145. r1144 retained the wait but initially omitted that expected-error assertion.'
record['qualification'] = {
    'twoSideThreeValuedPredicatesKeepWholeMetricEntities': True,
    'allFourOrdersAndAllElevenChangeCohorts': True,
    'strictCapturedCalendarAndRootDisplayIdContext': True,
    'exactSignedUInt128ThresholdsAndZeroBaselines': True,
    'fourteenThousandEntryPagingAndAliasLastMatch': True,
    'noWholeInputOrAliasMaterializationInIndexedQuery': True,
    'retainedComparisonReadersAndActualCloseAdmission': True,
    'failedCanceledInvalidQueriesKeepAppliedIndexAndRetry': True,
    'oversizedExactPathOrderRefusesWithoutClippedKeys': True,
    'aggregateCloseFailureLatchesViewAndKeepsAdmission': True,
    'failedMetricRestoresSelectorAndAllowsRetry': True,
    'appliedQuerySurvivesMetricChangesInHeadlessNativeView': True,
    'newCoreQueryAndReaderCases': 129, 'newNativeFilesystemHeadlessViewCases': 8,
    'broadCoverageWithExplicitFocusedPreparationCorrectionAllThree': True, 'taskOwnedProcessesExited': True,
    'physicalPointerKeyboardAndAssistiveTechnologyQualified': False,
    'indexedComparisonChartsReportsAndPersistenceComplete': False,
    'fullLargeComparisonResourceAndFaultMatrixQualified': False,
    'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
for revision in ('r1141', 'r1142', 'r1143'):
    files.update((temp / f'source-candidate-{revision}.json', temp / f'source-{revision}-indexed-comparison-query.tgz'))
files.update((temp / 'indexed-comparison-query-build-r1141.txt', temp / 'indexed-comparison-query-fixture-build-r1142.txt',
              temp / 'pack-timing-r1141.json', temp / 'test-fix-proof-r1142.json',
              temp / 'mac-query-stall-diagnostics-r1141.tgz', temp / 'windows-owned-stop-r1141.json', temp / 'ubuntu-owned-stop-r1141.json'))
files.update((temp / 'source-candidate-r1144.json', temp / 'source-candidate-r1145.json', temp / 'test-fix-proof-r1144.json', temp / 'test-fix-proof-r1145.json',
              temp / 'indexed-comparison-query-preparation-build-r1144.txt', temp / 'indexed-comparison-query-preparation-build-r1145.txt'))
for helper in ('stop-owned-query-r1141.ps1', 'stop-owned-query-r1141.py', 'freeze-query-r1142-sources.py'):
    target = out / 'drivers' / helper
    if not target.exists(): target.write_bytes((temp / helper).read_bytes())
helpers = ['native_batch_evidence.py', 'pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py', 'deploy-batch.ps1',
           'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'stop-owned-query-r1141.ps1', 'stop-owned-query-r1141.py',
           'freeze-query-r1142-sources.py', 'finalize-indexed-comparison-query-r1145.py']
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-text-r1145-indexed-comparison-query.tgz')
destination.write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'qualification': record['qualification'], 'matrix': {k: {v: row[v] for v in ('total', 'passed', 'failed', 'skipped')} for k, row in record['matrix'].items()}}, indent=2))
