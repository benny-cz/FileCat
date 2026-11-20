"""Retain all-metric entity and limited-basis report evidence from actual terminal native runs."""
import hashlib
import importlib.util
import json
import pathlib
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1124-comparison-entities.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('native_evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
e.assert_clean_build(temp / 'comparison-entities-build-r1123.txt', 4)
old_sources = {row['path']: row['sha256'] for row in e.read(temp / 'source-candidate-r1123.json')}
new_sources = {row['path']: row['sha256'] for row in e.read(temp / 'source-candidate-r1124.json')}
assert old_sources.keys() == new_sources.keys()
assert {name for name in old_sources if old_sources[name] != new_sources[name]} == {
    'src/FileCat.Core/Analysis/AnalysisAggregate.cs', 'src/FileCat.Core/Analysis/AnalysisComparison.cs',
    'src/FileCat.Core/Analysis/AnalysisComparisonEntities.cs', 'src/FileCat.Core/Analysis/AnalysisComparisonEntities.Fold.cs',
    'src/FileCat.Core/Analysis/AnalysisModel.cs',
    'tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.ComparisonEntities.cs',
    'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.ComparisonEntities.cs'}
snapshot = temp / 'exact-text-r1123-comparison-entities.tgz'
with tarfile.open(snapshot) as archive:
    members = archive.getmembers()
    assert len(members) == len(old_sources) and sum(member.size for member in members) < 128 * 1024 * 1024
    assert {member.name for member in members} == set(old_sources)
    for member in members:
        assert member.isfile() and hashlib.sha256(archive.extractfile(member).read()).hexdigest() == old_sources[member.name]
expected = {
    'windows': {'preview': (8, 0), 'core': (1694, 48), 'native': (75, 0), 'render': (688, 1), 'appsettings': (36, 1)},
    'mac': {'preview': (8, 0), 'core': (1676, 5), 'render': (688, 0), 'appsettings': (36, 0)},
    'ubuntu': {'preview': (8, 0), 'core': (1676, 5), 'render': (688, 0), 'appsettings': (36, 0)}}
lock_case = 'FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
record, files, runners, receipts = e.qualify(root, 'r1124', expected,
    ('NativeIndexedComparisonEntities', 'NativeComparisonEntitiesLimitedBasis'), 7, build_projects=5,
    additional_skips={f'{platform}/core': [lock_case] for platform in ('mac', 'ubuntu')})
initial_expected = {
    'windows': {'smoke': (42, 0), 'preview': (5, 0), 'core': (1693, 48), 'native': (75, 0), 'render': (685, 1), 'appsettings': (36, 1)},
    'mac': {'smoke': (42, 0), 'preview': (5, 0), 'core': (1675, 5), 'render': (685, 0), 'appsettings': (36, 0)},
    'ubuntu': {'smoke': (42, 0), 'preview': (5, 0), 'core': (1675, 5), 'render': (685, 0), 'appsettings': (36, 0)}}
initial_matrix = {}
for platform in ('windows', 'mac', 'ubuntu'):
    smoke = e.assembly(receipts[platform], 'smoke', 'r1124')
    assert (smoke.get('total'), smoke.get('passed'), smoke.get('failed'), smoke.get('skipped'), smoke.get('errors')) == ('43', '43', '0', '0', '0')
    entity_cases = [case for case in smoke.findall('.//test') if case.get('method', '').startswith('IndexedComparisonEntities')]
    assert len(entity_cases) == 29 and all(case.get('result') == 'Pass' for case in entity_cases)
    record['matrix'][f'{platform}/smoke'] = {key: smoke.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    initial_path = temp / f'{platform}-batch-r1123-results.tgz'
    initial = e.reports(initial_path)
    assert initial['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=0'
    for phase, (total, skipped) in initial_expected[platform].items():
        a = e.assembly(initial, phase, 'r1123')
        assert (int(a.get('total')), int(a.get('passed')), int(a.get('skipped'))) == (total, total - skipped, skipped)
        assert a.get('failed') == a.get('errors') == '0'
        initial_matrix[f'{platform}/{phase}'] = {key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    for revision in ('r1123', 'r1124'):
        assert e.read(temp / f'{platform}-batch-{revision}-original-run.json')['terminalExit'] == 0
        assert e.read(temp / f'{platform}-batch-{revision}-post.json')['filecatOrDotnet'] == []
        files.update((temp / f'{platform}-batch-{revision}-original-run.json', temp / f'{platform}-batch-{revision}-post.json'))
    files.add(initial_path)
    tag = 'linux' if platform == 'ubuntu' else platform
    runners.add(temp / f'batch-{tag}-r1123.{"ps1" if platform == "windows" else "sh"}')
task = json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
environment = json.loads(receipts['windows']['environment.json'].decode('utf-8-sig'))
assert task['terminalExit'] == 0 and task['runLevel'] == 'Limited' and task['logonType'] == 'Interactive'
assert environment['session'] > 0 and environment['user'].lower().endswith('\\filecatdsa_r2')
record['qualification'] = {'allMetricComparisonEntityAccountingInternalStageQualified': True,
    'sharedChargesAndLcaReusedWithoutAliasArrays': True, 'fourteenThousandAliasPagingAllMetrics': True,
    'knownLimitedBasisSideValuesPreservedWithoutCombinedAreaOrDelta': True,
    'nativeLimitedBasisMarkdownHtmlCsvReports': True, 'retainedSourceAndActualFailureCancellationCleanup': True,
    'newCoreCases': 29, 'newNativeCases': 7, 'broadSuitesPassedAllThree': True, 'taskOwnedProcessesExited': True,
    'twoInputIndexedMatchingAndCompatibilityIntegrated': False, 'largeIndexedComparisonQualified': False,
    'fullPhysicalAndResourceQualificationComplete': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record['initialCohort'] = {'revision': 'r1123', 'matrix': initial_matrix,
    'purpose': 'Initial all-metric entity stage. Review then found that inherited global basis suppression hid known per-entity values; r1124 corrects that behavior and adds explicit data/export regressions.'}
record['hostBuildCorrection'] = {'revision': 'r1122', 'nativeRunLaunched': False,
    'reason': 'A test fixture used the MetadataState init property as a constructor argument. Corrected before the first native cohort; the failed host build log remains retained.'}
files.update((snapshot, temp / 'source-candidate-r1123.json', temp / 'pack-timing-r1123.json', temp / 'app-settings-inputs-r1123.json',
              temp / 'comparison-entities-build-r1123.txt', temp / 'comparison-entities-build-r1122.txt'))
helpers = ['native_batch_evidence.py', 'pack-batch-direct.ps1', 'deploy-batch.ps1', 'collect-batch-archive.ps1',
           'preflight-indexed-r732.ps1', 'finalize-comparison-entities-r1124.py']
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-text-r1124-comparison-entities.tgz')
destination.write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'qualification': record['qualification'], 'matrix': {key: {k: row[k] for k in ('total', 'passed', 'failed', 'skipped')} for key, row in record['matrix'].items()}}, indent=2))
