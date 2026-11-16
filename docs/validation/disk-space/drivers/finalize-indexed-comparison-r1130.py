"""Verify actual all-platform indexed comparison batches and preserve failed fixture preflights."""
import hashlib
import importlib.util
import io
import json
import pathlib
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1130-indexed-comparison.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('native_evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
e.assert_clean_build(temp / 'indexed-comparison-build-r1129.txt', 4)
e.assert_clean_build(temp / 'indexed-comparison-fixture-fix-r1130.txt', 1)
old_sources = {row['path']: row['sha256'] for row in e.read(temp / 'source-candidate-r1129.json')}
new_sources = {row['path']: row['sha256'] for row in e.read(temp / 'source-candidate-r1130.json')}
assert old_sources.keys() == new_sources.keys()
changed = [path for path in old_sources if old_sources[path] != new_sources[path]]
test = 'tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.IndexedComparison.cs'
assert changed == [test]
text = (root / test).read_text(encoding='utf-8')
original = text.replace('enumeration: ComparisonEnumeration(old), arbitraryPathIds: true);', 'enumeration: ComparisonEnumeration(old));', 1)
original = original.replace('enumeration: ComparisonEnumeration(current), arbitraryPathIds: true);', 'enumeration: ComparisonEnumeration(current));', 1)
original = original.replace('''        var old = ComparisonSample();
        // One alias keeps every captured metric within UInt128; two MaxValue aliases
        // would intentionally overflow the independent reference File-size total.
        old = old with { Nodes = old.Nodes.Where(n => n.Id != 5).ToImmutableArray(), Objects = old.Objects.SetItem(0, old.Objects[0] with { LinkCount = AnalysisMeasurement.Known(1) }) };
        var current = old;
''', '''        var old = ComparisonSample(); var current = old;
''')
original = original.replace('''        Assert.Equal(AnalysisLimits.RawNameBytes, member.RawName.IsEmpty ? AnalysisRawNames.Encode(member.NameEncoding, member.Name).Length : member.RawName.Length);
        Assert.Equal(s.Nodes[1].Name, member.Name);
''', '''        Assert.Equal(AnalysisLimits.RawNameBytes, member.RawName.Length); Assert.Equal(s.Nodes[1].Name, member.Name);
''')
original_bytes = original.encode('utf-8')
assert hashlib.sha256(original_bytes).hexdigest() == old_sources[test], 'Initial fixture must reconstruct exactly'
proof = e.read(temp / 'test-fix-proof-r1130.json')
assert proof['scope'] == 'TestOnly' and proof['baseline'] == 'r1129'
archives, plans = [], {}
for item in proof['platforms']:
    platform = item['platform']
    assert item['guestReuse'] and item['changed'] == ['tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.dll', 'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.pdb']
    plan = e.read(temp / (f'test-fix-delta-{platform}-r1130' if platform != 'windows' else 'test-fix-delta-r1130') / 'plan.json')
    assert [row['path'] for row in plan['files'] if row['patch']] == item['changed']
    assert all(row['sha256'] == row['baselineSha256'] for row in plan['files'] if not row['patch'])
    plans[platform] = plan
    archive = temp / f'dsa-batch-{platform}-r1130.tgz'; assert e.sha(archive) == item['sha256']
    archives.append({'platform': platform, 'path': str(archive), 'bytes': item['archiveBytes'], 'sha256': item['sha256']})
packing = {'revision': 'r1130', 'renderRevision': 'r1130', 'sourceManifest': '.tmp/source-candidate-r1130.json',
    'buildLog': '.tmp/indexed-comparison-fixture-fix-r1130.txt', 'initialBuildLog': '.tmp/indexed-comparison-build-r1129.txt',
    'archiveCount': 3, 'standaloneArchives': 0, 'elapsedSeconds': proof['elapsedSeconds'], 'archives': archives,
    'cachedProductBaseline': 'r1129', 'scope': 'TestOnly'}
(temp / 'pack-timing-r1130.json').write_text(json.dumps(packing, indent=2) + '\n', encoding='utf-8')
app_inputs = {platform: [{'path': p.relative_to(temp / f'batch-r1130-{platform}').as_posix(), 'sha256': e.sha(p)}
    for p in sorted((temp / f'batch-r1130-{platform}/render').glob('FileCat.App.Tests.*'))] for platform in ('windows', 'mac', 'linux')}
assert all(app_inputs.values())
(temp / 'app-settings-inputs-r1130.json').write_text(json.dumps(app_inputs, indent=2) + '\n', encoding='utf-8')
expected = {
    'windows': {'preview': (12, 0), 'core': (1770, 48), 'native': (75, 0), 'render': (692, 1), 'appsettings': (36, 1)},
    'mac': {'preview': (12, 0), 'core': (1752, 5), 'render': (692, 0), 'appsettings': (36, 0)},
    'ubuntu': {'preview': (12, 0), 'core': (1752, 5), 'render': (692, 0), 'appsettings': (36, 0)}}
lock_case = 'FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
record, files, runners, receipts = e.qualify(root, 'r1130', expected, ('NativeIndexedComparisonKeeps',), 4, build_projects=1,
    additional_skips={f'{platform}/core': [lock_case] for platform in ('mac', 'ubuntu')})
for platform in ('windows', 'mac', 'ubuntu'):
    smoke = e.assembly(receipts[platform], 'smoke', 'r1130')
    assert (smoke.get('total'), smoke.get('passed'), smoke.get('failed'), smoke.get('skipped'), smoke.get('errors')) == ('119', '119', '0', '0', '0')
    cases = [case for case in smoke.findall('.//test') if case.get('type') == 'FileCat.Core.Tests.AnalysisDatasetSummaryTests' and case.get('method', '').startswith('IndexedComparison') and not case.get('method', '').startswith(('IndexedComparisonEntities', 'IndexedComparisonPaths'))]
    assert len(cases) == 70 and all(case.get('result') == 'Pass' for case in cases)
    compact = [case for case in smoke.findall('.//test') if case.get('method', '').startswith('ExactKeyCompaction')]
    assert len(compact) == 2 and all(case.get('result') == 'Pass' for case in compact)
    record['matrix'][f'{platform}/smoke'] = {key: smoke.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    first_path = temp / f'{platform}-batch-r1129-results.tgz'
    first = e.reports(first_path)
    assert first['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=smoke'
    a = e.assembly(first, 'smoke', 'r1129')
    assert (a.get('total'), a.get('passed'), a.get('failed'), a.get('skipped'), a.get('errors')) == ('119', '106', '13', '0', '0')
    failures = [case for case in a.findall('.//test') if case.get('result') == 'Fail']
    assert len(failures) == 13 and all(case.get('method') in {
        'IndexedComparisonKeepsFullUnsigned128BitSignedDeltasAndKnownZero',
        'IndexedComparisonMatchesAllMetricChangesCoverageAndAliasAmbiguity',
        'IndexedComparisonDefersLargeRawNameDetailsAndKeepsScalarPagesWithinTheirPayloadBound',
        'IndexedComparisonIdentityVersionMoveAndAttributionRequireQualifiedContext'} for case in failures)
    for revision, terminal in (('r1129', 1), ('r1130', 0)):
        assert e.read(temp / f'{platform}-batch-{revision}-original-run.json')['terminalExit'] == terminal
        assert e.read(temp / f'{platform}-batch-{revision}-post.json')['filecatOrDotnet'] == []
        files.update((temp / f'{platform}-batch-{revision}-original-run.json', temp / f'{platform}-batch-{revision}-post.json'))
    assert 'changedFiles=2' in receipts[platform]['cache-proof.txt'].decode('utf-8-sig')
    files.add(first_path); tag = 'linux' if platform == 'ubuntu' else platform
    runners.add(temp / f'batch-{tag}-r1129.{"ps1" if platform == "windows" else "sh"}')
task = json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
assert task['terminalExit'] == 0 and task['runLevel'] == 'Limited' and task['logonType'] == 'Interactive'
snapshot = temp / 'exact-text-r1129-indexed-comparison.tgz'; assert not snapshot.exists()
with tarfile.open(snapshot, 'w:gz') as archive:
    for path, digest in old_sources.items():
        data = original_bytes if path == test else (root / path).read_bytes()
        assert hashlib.sha256(data).hexdigest() == digest
        info = tarfile.TarInfo(path); info.size = len(data); archive.addfile(info, io.BytesIO(data))
cache_path = temp / 'cache-plans-r1130-indexed-comparison.json'
cache_path.write_text(json.dumps(plans, indent=2) + '\n', encoding='utf-8')
record['qualification'] = {'internalPagedAllMetricTwoInputComparisonQualified': True,
    'exactPathsBeforeQualifiedIdentityVersionAndMetadataFallback': True,
    'aliasSplitMergeAmbiguityWithoutZeroBaseline': True, 'fullPolicyScopeBasisFreshnessCoverageChecks': True,
    'exactMetadataSetsAndBasisEqualityAfterHashLookup': True, 'compactionUsesTrustedExactEquality': True,
    'fullUInt128DeltasAndFourteenThousandEntryFixtures': True, 'deferredLargeRawNameDetails': True,
    'nativeBothInputLifetimeAndMembershipAllMetrics': True, 'newCoreComparisonCases': 70, 'newCompactionCases': 2, 'newNativeCases': 4,
    'broadSuitesPassedAllThree': True, 'taskOwnedProcessesExited': True,
    'publicIndexedComparisonUiQueryChartPersistenceIntegrated': False, 'fullLargeIndexedComparisonParityQualified': False,
    'fullPhysicalAndResourceQualificationComplete': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record['developmentPreflight'] = {'revision': 'r1129', 'passed': 106, 'failed': 13,
    'correction': 'Fixtures now declare sparse IDs after removals, keep the independent File-size reference total within UInt128 while testing UInt128-max active values, and validate reversible compact raw names. Product inputs are byte-identical.'}
record['windowsCoordination'] = {'otherChat': '01a1e460-2711-7dd1-a5cc-4e7e2efd4446',
    'scope': 'Postponed Windows for the explicitly user-authorized Owner NVDA check. Other tester returned the R2 session and closed owned processes/task; fresh inventory was empty before deployment. No account/session or display changes were made by this task.'}
files.update((snapshot, cache_path, temp / 'source-candidate-r1129.json', temp / 'pack-timing-r1129.json',
              temp / 'indexed-comparison-build-r1129.txt', temp / 'indexed-comparison-build-r1126.txt', temp / 'test-fix-proof-r1130.json'))
helpers = ['native_batch_evidence.py', 'pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py',
    'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'finalize-indexed-comparison-r1130.py']
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-text-r1130-indexed-comparison.tgz')
destination.write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'qualification': record['qualification'], 'matrix': {key: {k: row[k] for k in ('total', 'passed', 'failed', 'skipped')} for key, row in record['matrix'].items()}}, indent=2))
