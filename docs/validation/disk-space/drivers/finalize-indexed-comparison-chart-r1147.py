"""Verify actual chart batches and preserve both the failed and corrected source cohorts."""
import hashlib
import importlib.util
import json
import pathlib
import struct
import subprocess
import tarfile

root = pathlib.Path.cwd(); temp = root / '.tmp'; out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1147-indexed-comparison-chart.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('native_evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec); spec.loader.exec_module(e)

source_sets = {}
for revision in ('r1146', 'r1147'):
    rows = e.read(temp / f'source-candidate-{revision}.json'); source_sets[revision] = {row['path']: row['sha256'] for row in rows}
    with tarfile.open(temp / f'source-{revision}-indexed-comparison-chart.tgz') as archive:
        members = archive.getmembers(); assert len(members) == len(source_sets[revision])
        assert sum(member.size for member in members) <= 64 * 1024 * 1024
        assert {member.name for member in members} == set(source_sets[revision])
        assert all(member.isfile() and hashlib.sha256(archive.extractfile(member).read()).hexdigest() == source_sets[revision][member.name] for member in members)
assert source_sets['r1146'].keys() == source_sets['r1147'].keys()
assert {path for path in source_sets['r1146'] if source_sets['r1146'][path] != source_sets['r1147'][path]} == {
    'src/FileCat.Core/Analysis/AnalysisIndexedComparison.Chart.cs',
    'src/FileCat.App/Views/DiskSpaceWindow.IndexedComparison.Charts.cs',
    'tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.IndexedComparisonChart.cs'}
current = set(subprocess.check_output(['git', 'ls-files', '--', 'src', 'tests', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json'], text=True).splitlines())
current.update(subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '--', 'src', 'tests'], text=True).splitlines())
assert current == set(source_sets['r1147'])
assert all(e.sha(root / path) == digest for path, digest in source_sets['r1147'].items())
app_inputs = {platform: [{'path': path.relative_to(temp / f'batch-r1147-{platform}').as_posix(), 'sha256': e.sha(path)}
                        for path in sorted((temp / f'batch-r1147-{platform}/render').glob('FileCat.App.Tests.*'))]
              for platform in ('windows', 'mac', 'linux')}
assert all(app_inputs.values())
(temp / 'app-settings-inputs-r1147.json').write_text(json.dumps(app_inputs, indent=2) + '\n', encoding='utf-8')
expected = {
    'windows': {'preview': (33, 0), 'core': (1915, 48), 'native': (75, 0), 'render': (713, 1), 'appsettings': (36, 1)},
    'mac': {'preview': (33, 0), 'core': (1897, 5), 'render': (713, 0), 'appsettings': (36, 0)},
    'ubuntu': {'preview': (33, 0), 'core': (1897, 5), 'render': (713, 0), 'appsettings': (36, 0)}}
lock_case = 'FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
prefix = ('NativeIndexedComparisonChartsKeepWholeCapturedSidesAcrossQueryNavigationAndMetricRetirement',)
record, files, runners, receipts = e.qualify(root, 'r1147', expected, prefix, 4,
    source_snapshot='.tmp/source-r1147-indexed-comparison-chart.tgz',
    additional_skips={f'{platform}/core': [lock_case] for platform in ('mac', 'ubuntu')})
themes = ('Classic', 'ClassicDark', 'HighContrast', 'Cyberpunk', 'Psychedelic', 'Steampunk', 'DosCommander')
gallery = {}
for platform in ('windows', 'mac', 'ubuntu'):
    smoke = e.assembly(receipts[platform], 'smoke', 'r1147')
    assert (smoke.get('total'), smoke.get('passed'), smoke.get('failed'), smoke.get('errors'), smoke.get('skipped')) == ('264', '264', '0', '0', '0')
    cases = [case for case in smoke.findall('.//test') if case.get('method', '').startswith('IndexedComparisonChart')]
    assert len(cases) == 16 and all(case.get('result') == 'Pass' for case in cases)
    record['matrix'][f'{platform}/smoke'] = {key: smoke.get(key) for key in ('total', 'passed', 'failed', 'errors', 'skipped', 'time')}
    record['matrix'][f'{platform}/smoke']['newChartCases'] = sorted(case.get('name') for case in cases)
    actual = temp / f'{platform}-batch-r1147-original-run.json'
    assert e.read(actual)['terminalExit'] == 0 and e.read(actual)['sessionId']; files.add(actual)
    failed = e.reports(temp / f'{platform}-batch-r1146-results.tgz')
    assert failed['state.txt'].decode('utf-8-sig').strip().startswith('FINISHED exit=1')
    old = e.assembly(failed, 'smoke', 'r1146')
    assert (old.get('total'), old.get('passed'), old.get('failed'), old.get('errors'), old.get('skipped')) == ('264', '257', '7', '0', '0')
    failures = [case for case in old.findall('.//test') if case.get('result') == 'Fail']
    assert {case.get('method') for case in failures} == {
        'IndexedComparisonChartIncompatibleBasesKeepRecordedRootsSeparateWithoutACombinedArea',
        'IndexedComparisonChartBoundsLargeGeometryAndOpensExactRemainders',
        'IndexedComparisonChartUsesTheFullPositiveSideHierarchyAndExactEntityEvidence'}
    assert not any(name.startswith(('core-', 'render-', 'preview-')) and name.endswith('.xml') for name in failed)
    assert e.read(temp / f'{platform}-batch-r1146-original-run.json')['terminalExit'] == 1
    assert not e.read(temp / f'{platform}-batch-r1146-post.json')['filecatOrDotnet']
    files.update(temp / f'{platform}-batch-r1146-{suffix}' for suffix in ('results.tgz', 'original-run.json', 'post.json'))
    gallery[platform] = []
    with tarfile.open(temp / f'{platform}-batch-r1147-results.tgz') as archive:
        by_name = {member.name: member for member in archive.getmembers()}
        for theme in themes:
            for width in (1180, 380):
                for mode in (1, 2):
                    suffix = f'indexed-comparison-growth-renders/{theme}-{width}-{mode}.png'
                    name = next(name for name in by_name if name.endswith(suffix))
                    data = archive.extractfile(by_name[name]).read()
                    assert data[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II', data[16:24]) == (width, 430 if width == 380 else 780)
                    gallery[platform].append({'path': name, 'sha256': hashlib.sha256(data).hexdigest()})
    tag = 'linux' if platform == 'ubuntu' else platform
    runners.add(temp / f'batch-{tag}-r1146.{"ps1" if platform == "windows" else "sh"}')
task = json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
environment = json.loads(receipts['windows']['environment.json'].decode('utf-8-sig'))
assert task['terminalExit'] == 0 and task['logonType'] == 'Interactive' and task['runLevel'] == 'Limited'
assert environment['session'] > 0 and environment['user'].lower().endswith('\\filecatdsa_r2')
record['developmentFailures'] = {'r1146': 'All three strict preflights reproduced seven failures. A real zero-area shortcut bypassed incompatible combined-area refusal; the corrected chart rejects that scope before geometry construction. Fixture assumptions now distinguish zero/missing table entries from positive geometry and synthetic Other from ordinary files classified as other. No broad phases ran for this failed cohort. Construction now uses the exact retained comparison graphs, eliminating a caller-supplied dataset/identity join.'}
record['gallery'] = gallery
record['qualification'] = {
    'exactRetainedSideHierarchyWithPositiveObservedArea': True,
    'currentAndPreviousSidesKeepRemovedAreaSeparate': True,
    'growthShrinkageAttributionAndUnknownColorsPreserveAccounting': True,
    'privateIndexedJoinsAndBoundedDepthFoldWithoutWholeAliasArrays': True,
    'fourteenThousandEntryGeometryAndNestedExactRemainders': True,
    'incompatibleBasesRefuseCombinedAreaAndExposeSeparateRoots': True,
    'actualReaderClosureCancellationFailureAndRetry': True,
    'queryDoesNotSilentlyChangeFullHierarchyChartScope': True,
    'newCoreCases': 16, 'newNativeFilesystemHeadlessViewCases': 4,
    'sevenThemesBothChartModesWideCompactLargeTextGallery': True,
    'taskOwnedProcessesExited': True,
    'physicalPointerKeyboardAndAssistiveTechnologyQualified': False,
    'automaticCrossPageChartToTableLocationQualified': False,
    'indexedComparisonReportsAndPersistenceComplete': False,
    'fullLargeComparisonResourceAndFaultMatrixQualified': False,
    'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
files.update((temp / 'source-candidate-r1146.json', temp / 'source-r1146-indexed-comparison-chart.tgz',
              temp / 'indexed-comparison-chart-build-r1146.txt', temp / 'pack-timing-r1146.json'))
runners.add(temp / 'collect-comparison-chart-r1147.ps1')
helpers = ('native_batch_evidence.py', 'pack-batch-direct.ps1', 'deploy-batch.ps1', 'preflight-indexed-r732.ps1',
           'collect-batch-archive.ps1', 'finalize-indexed-comparison-chart-r1147.py')
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-text-r1147-indexed-comparison-chart.tgz')
destination.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
print('Verified immutable all-three indexed comparison chart qualification:', destination)
