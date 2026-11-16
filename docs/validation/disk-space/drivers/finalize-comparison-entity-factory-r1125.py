"""Qualify a factory-only ownership correction with focused all-platform reruns and exact broad-source linkage."""
import hashlib
import importlib.util
import json
import pathlib
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1125-comparison-entity-factory.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('native_evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
broad_path = out / 'source-manifest-r1124-comparison-entities.json'
broad = e.read(broad_path)
old_sources = {row['path']: row['sha256'] for row in broad['sources']}
new_sources = {row['path']: row['sha256'] for row in e.read(temp / 'source-candidate-r1125.json')}
assert old_sources.keys() == new_sources.keys()
production = 'src/FileCat.Core/Analysis/AnalysisComparisonEntities.cs'
tests = 'tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.ComparisonEntities.cs'
assert {name for name in old_sources if old_sources[name] != new_sources[name]} == {production, tests}
with tarfile.open(out / 'exact-text-r1124-comparison-entities.tgz') as archive:
    previous = {path: archive.extractfile(path).read().decode('utf-8') for path in (production, tests)}
text = (root / production).read_text(encoding='utf-8')
start = text.index('    // A return inside await using can lose an already-created result')
end = text.index('    internal static async Task<AnalysisComparisonEntities> BuildAsync(', start)
original = (text[:start] + text[end:]).replace('''            var charges = await AnalysisObjectChargeIndex.BuildAsync(source, Descriptor, metric, _io, _device, cancellation).ConfigureAwait(false);
            return await AnalysisComparisonEntities.BuildAfterProducerCloseAsync(charges,
                () => charges.IndexComparisonEntitiesAsync(cancellation)).ConfigureAwait(false);
''', '''            await using var charges = await AnalysisObjectChargeIndex.BuildAsync(source, Descriptor, metric, _io, _device, cancellation).ConfigureAwait(false);
            return await charges.IndexComparisonEntitiesAsync(cancellation).ConfigureAwait(false);
''')
assert original == previous[production]
test_text = (root / tests).read_text(encoding='utf-8')
marker = '    private static async Task CheckComparisonEntities('
assert test_text[test_text.index(marker):] == previous[tests][previous[tests].index(marker):]
expected = {platform: {'preview': (8, 0)} for platform in ('windows', 'mac', 'ubuntu')}
record, files, runners, receipts = e.qualify(root, 'r1125', expected,
    ('NativeIndexedComparisonEntities', 'NativeComparisonEntitiesLimitedBasis'), 7, build_projects=4)
for platform in ('windows', 'mac', 'ubuntu'):
    smoke = e.assembly(receipts[platform], 'smoke', 'r1125')
    assert (smoke.get('total'), smoke.get('passed'), smoke.get('failed'), smoke.get('skipped'), smoke.get('errors')) == ('47', '47', '0', '0', '0')
    cases = smoke.findall('.//test')
    entity = [case for case in cases if case.get('method', '').startswith('IndexedComparisonEntities')]
    factory = [case for case in cases if case.get('method', '').startswith('IndexedComparisonEntitiesFactory')]
    assert len(entity) == 33 and len(factory) == 4 and all(case.get('result') == 'Pass' for case in cases)
    record['matrix'][f'{platform}/smoke'] = {key: smoke.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    assert e.read(temp / f'{platform}-batch-r1125-original-run.json')['terminalExit'] == 0
    files.update((temp / f'{platform}-batch-r1125-original-run.json', temp / f'{platform}-batch-r1125-post.json'))
task = json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
environment = json.loads(receipts['windows']['environment.json'].decode('utf-8-sig'))
assert task['terminalExit'] == 0 and task['runLevel'] == 'Limited' and task['logonType'] == 'Interactive'
assert environment['session'] > 0 and environment['user'].lower().endswith('\\filecatdsa_r2')
record['qualifiedBroadAccountingCohort'] = {'revision': 'r1124', 'path': broad_path.relative_to(root).as_posix(),
    'sha256': e.sha(broad_path), 'matrix': broad['matrix'],
    'scope': 'All accounting, comparison, chart/report and native fixture sources remain identical. Only the entity factory release barrier and four focused ownership cases changed. Full broad suites were not repeated on r1125.'}
record['qualification'] = {'factoryPublicationWaitsForActualProducerClose': True, 'unpublishedResultReleasedAfterProducerCloseFailure': True,
    'actualReleaseCompletionAndBothFailuresRetained': True, 'eachOwnerCloseAttemptedOnce': True,
    'allThirtyThreeEntityCoreAndSevenNativeCasesPassedAllThree': True, 'allFourteenPriorPathCasesPassedAllThree': True,
    'accountingBroadEvidenceLinkedToExactUnchangedSources': True, 'taskOwnedProcessesExited': True,
    'twoInputIndexedMatchingAndCompatibilityIntegrated': False, 'largeIndexedComparisonQualified': False,
    'fullPhysicalAndResourceQualificationComplete': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
helpers = ['native_batch_evidence.py', 'pack-batch-direct.ps1', 'deploy-batch.ps1', 'collect-batch-archive.ps1',
           'preflight-indexed-r732.ps1', 'finalize-comparison-entity-factory-r1125.py']
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-text-r1125-comparison-entity-factory.tgz')
destination.write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'qualification': record['qualification'], 'focusedMatrix': record['matrix']}, indent=2))
