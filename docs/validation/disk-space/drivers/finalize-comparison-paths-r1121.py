"""Retain exact indexed-path correspondence evidence, including failed preflights and cached reruns."""
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
destination = out / 'source-manifest-r1121-comparison-paths.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('native_evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
initial = e.read(temp / 'source-candidate-r1118.json')
current = e.read(temp / 'source-candidate-r1121.json')
old_sources = {row['path']: row['sha256'] for row in initial}
new_sources = {row['path']: row['sha256'] for row in current}
assert old_sources.keys() == new_sources.keys()
changed = [name for name in old_sources if old_sources[name] != new_sources[name]]
test_path = 'tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.ComparisonPaths.cs'
assert changed == [test_path]
assert all(e.sha(root / path) == digest for path, digest in new_sources.items())
text = (root / test_path).read_text(encoding='utf-8')
original_test = text.replace('[InlineData("unpaired:D800", "unpaired:D801", "utf16le", "utf16le")]',
                             '[InlineData("bad\\ud800", "bad\\ud801", "utf16le", "utf16le")]')
original_test = original_test.replace('''        // Attribute/discovery transport uses UTF-8 and cannot carry an unpaired code unit.
        // Construct it in the test so the two actual native raw inputs remain distinct.
        static string NativeName(string text) => text.StartsWith("unpaired:", StringComparison.Ordinal)
            ? "bad" + (char)Convert.ToUInt16(text[9..], 16) : text;
        oldName = NativeName(oldName); newName = NativeName(newName);
''', '')
original_test = original_test.replace('''        var old = One(oldName, oldEncoding); var nowSnapshot = One(newName, newEncoding);
        Assert.False(oldEncoding == newEncoding && old.Nodes[1].RawName.AsSpan().SequenceEqual(nowSnapshot.Nodes[1].RawName.AsSpan()));
        await using var a = await Build(old, io); await using var b = await Build(nowSnapshot, io);
''', '''        await using var a = await Build(One(oldName, oldEncoding), io); await using var b = await Build(One(newName, newEncoding), io);
''')
original_bytes = original_test.encode('utf-8')
assert hashlib.sha256(original_bytes).hexdigest() == old_sources[test_path], 'Original fixture reconstruction must match the frozen compiled source'
e.assert_clean_build(temp / 'comparison-paths-build-r1118.txt', 5)
e.assert_clean_build(temp / 'comparison-paths-test-fix-r1120.txt', 1)
proof = e.read(temp / 'test-fix-proof-r1121.json')
runner_only = e.read(temp / 'test-fix-proof-r1119.json')
assert proof['scope'] == 'TestOnly' and proof['baseline'] == 'r1118'
assert runner_only['scope'] == 'RunnerOnly' and runner_only['baseline'] == 'r1118'
archives = []
cache_plans = {}
for item in proof['platforms']:
    platform = item['platform']
    assert item['guestReuse'] and item['changed'] == ['tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.dll', 'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.pdb']
    old_item = next(row for row in runner_only['platforms'] if row['platform'] == platform)
    assert old_item['guestReuse'] and not old_item['changed']
    def plan(revision):
        return e.read(temp / (f'test-fix-delta-{platform}-{revision}' if platform != 'windows' else f'test-fix-delta-{revision}') / 'plan.json')
    previous_plan, current_plan = plan('r1119'), plan('r1121')
    assert all(not row['patch'] and row['sha256'] == row['baselineSha256'] for row in previous_plan['files'])
    assert [row['path'] for row in current_plan['files'] if row['patch']] == item['changed']
    assert all(row['sha256'] == row['baselineSha256'] for row in current_plan['files'] if not row['patch'])
    cache_plans[platform] = {'r1119': previous_plan, 'r1121': current_plan}
    archive = temp / f'dsa-batch-{platform}-r1121.tgz'
    assert e.sha(archive) == item['sha256']
    archives.append({'platform': platform, 'path': str(archive), 'bytes': item['archiveBytes'], 'sha256': item['sha256']})
packing = {'revision': 'r1121', 'renderRevision': 'r1121', 'sourceManifest': '.tmp/source-candidate-r1121.json',
           'buildLog': '.tmp/comparison-paths-test-fix-r1120.txt', 'initialBuildLog': '.tmp/comparison-paths-build-r1118.txt',
           'archiveCount': 3, 'standaloneArchives': 0, 'elapsedSeconds': proof['elapsedSeconds'], 'archives': archives,
           'cachedProductBaseline': 'r1118', 'scope': 'TestOnly'}
(temp / 'pack-timing-r1121.json').write_text(json.dumps(packing, indent=2) + '\n', encoding='utf-8')
app_inputs = {platform: [{'path': p.relative_to(temp / f'batch-r1121-{platform}').as_posix(), 'sha256': e.sha(p)}
    for p in sorted((temp / f'batch-r1121-{platform}/render').glob('FileCat.App.Tests.*'))] for platform in ('windows', 'mac', 'linux')}
assert all(app_inputs.values())
(temp / 'app-settings-inputs-r1121.json').write_text(json.dumps(app_inputs, indent=2) + '\n', encoding='utf-8')
expected = {
    'windows': {'preview': (1, 0), 'core': (1665, 48), 'native': (75, 0), 'render': (681, 1), 'appsettings': (36, 1)},
    'mac': {'preview': (1, 0), 'core': (1647, 5), 'render': (681, 0), 'appsettings': (36, 0)},
    'ubuntu': {'preview': (1, 0), 'core': (1647, 5), 'render': (681, 0), 'appsettings': (36, 0)}
}
lock_case = 'FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
record, files, runners, receipts = e.qualify(root, 'r1121', expected, ('NativeIndexedComparisonPaths',), 1, build_projects=1,
    additional_skips={f'{platform}/core': [lock_case] for platform in ('mac', 'ubuntu')})
for platform in ('windows', 'mac', 'ubuntu'):
    result = receipts[platform]
    smoke = e.assembly(result, 'smoke', 'r1121')
    assert (smoke.get('total'), smoke.get('passed'), smoke.get('failed'), smoke.get('skipped'), smoke.get('errors')) == ('14', '14', '0', '0', '0')
    core = e.assembly(result, 'core', 'r1121')
    zip_cases = [case for case in core.findall('.//test') if case.get('type') == 'FileCat.Core.Tests.ZipListingRevisionTests']
    assert len(zip_cases) == 6
    if platform == 'windows':
        assert all(case.get('result') == 'Pass' for case in zip_cases)
    else:
        assert len([case for case in zip_cases if case.get('result') == 'Pass']) == 5
        skipped_case = next(case for case in zip_cases if case.get('result') == 'Skip')
        assert skipped_case.get('name') == lock_case and skipped_case.findtext('reason') == 'Mandatory byte-range locks are a Windows facility.'
    assert 'changedFiles=2' in result['cache-proof.txt'].decode('utf-8-sig')
    record['matrix'][f'{platform}/smoke'] = {key: smoke.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    for revision, total, passed in (('r1118', '0', '0'), ('r1119', '14', '13')):
        path = temp / f'{platform}-batch-{revision}-results.tgz'
        first = e.reports(path)
        assert first['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=smoke'
        a = e.assembly(first, 'smoke', revision)
        assert a.get('total') == total and a.get('passed') == passed and a.get('skipped') == a.get('errors') == '0'
        if revision == 'r1119':
            failures = [case for case in a.findall('.//test') if case.get('result') == 'Fail']
            assert len(failures) == 1 and failures[0].get('method') == 'IndexedComparisonPathsNeverNormalizeCaseUnicodeEncodingOrRawNames'
            assert 'changedFiles=0' in first['cache-proof.txt'].decode('utf-8-sig')
        assert e.read(temp / f'{platform}-batch-{revision}-original-run.json')['terminalExit'] == 1
        assert e.read(temp / f'{platform}-batch-{revision}-post.json')['filecatOrDotnet'] == []
        files.update((path, temp / f'{platform}-batch-{revision}-original-run.json', temp / f'{platform}-batch-{revision}-post.json'))
        tag = 'linux' if platform == 'ubuntu' else platform
        runners.add(temp / f'batch-{tag}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    assert e.read(temp / f'{platform}-batch-r1121-original-run.json')['terminalExit'] == 0
    files.add(temp / f'{platform}-batch-r1121-original-run.json')
task = json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
assert task['terminalExit'] == 0 and task['runLevel'] == 'Limited' and task['logonType'] == 'Interactive'
snapshot = temp / 'exact-text-r1118-comparison-paths.tgz'
assert not snapshot.exists()
with tarfile.open(snapshot, 'w:gz') as archive:
    for name, digest in old_sources.items():
        data = original_bytes if name == test_path else (root / name).read_bytes()
        assert hashlib.sha256(data).hexdigest() == digest
        info = tarfile.TarInfo(name); info.size = len(data); archive.addfile(info, io.BytesIO(data))
cache_path = temp / 'cache-plans-r1121-comparison-paths.json'
cache_path.write_text(json.dumps(cache_plans, indent=2) + '\n', encoding='utf-8')
files.update((snapshot, cache_path, temp / 'source-candidate-r1118.json', temp / 'source-candidate-r1119.json',
              temp / 'pack-timing-r1118.json', temp / 'comparison-paths-build-r1118.txt',
              temp / 'test-fix-proof-r1119.json', temp / 'test-fix-proof-r1121.json'))
record['qualification'] = {'exactIndexedPathCorrespondenceInternalStageQualified': True,
    'fourteenCoreAndOneNativeCasesPassedAllThree': True, 'fourteenThousandRowsAndDeepPrefixBounds': True,
    'rawNameTagsAndBytesPreserved': True, 'retainedBothSourcesAndActualCancellationCleanup': True,
    'allApplicableNewMainZipListingCasesPassedAllThree': True, 'broadSuitesPassedAllThree': True, 'taskOwnedProcessesExited': True,
    'metricEntityAndCoverageComparisonIntegrated': False, 'largeIndexedComparisonQualified': False,
    'fullPhysicalAndResourceQualificationComplete': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record['developmentPreflights'] = {'r1118': 'Zero tests: class and method filters intersected. Runner-only correction selected the intended cases.',
    'r1119': '13/14 passed. Unpaired UTF-16 InlineData was replaced during discovery transport. Constructed actual distinct raw code units inside the test, with a precondition assertion.',
    'r1120': 'Host-only staging refused checkout-layout Core test paths. No native run launched. Declared complete test-pair allowlists now support that existing layout.'}
helpers = ['native_batch_evidence.py', 'pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py',
           'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'finalize-comparison-paths-r1121.py']
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-text-r1121-comparison-paths.tgz')
destination.write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'qualified': record['qualification'], 'matrix': {key: {k: row[k] for k in ('total', 'passed', 'skipped', 'failed')} for key, row in record['matrix'].items()}}, indent=2))
