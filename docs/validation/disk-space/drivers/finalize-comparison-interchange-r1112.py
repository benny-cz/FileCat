"""Verify and retain the actual comparison import/report cohorts, including their failed first preflight."""
import importlib.util
import json
import pathlib
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1112-comparison-interchange.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('native_evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
proof = e.read(temp / 'test-fix-proof-r1112.json')
assert proof['scope'] == 'TestOnly' and proof['baseline'] == 'r1111'
original = e.read(temp / 'source-candidate-r1111.json')
current = e.read(temp / 'source-candidate-r1112.json')
old_sources = {row['path']: row['sha256'] for row in original}
new_sources = {row['path']: row['sha256'] for row in current}
assert old_sources.keys() == new_sources.keys()
changed = [name for name in old_sources if old_sources[name] != new_sources[name]]
assert changed == ['tests/FileCat.Rendering.Tests/AnalysisRenderingTests.ComparisonInterchange.cs']
snapshot = temp / 'exact-text-r1111-comparison-interchange.tgz'
with tarfile.open(snapshot) as archive:
    members = archive.getmembers()
    assert len(members) == len(old_sources) and sum(m.size for m in members) < 256 * 1024 * 1024
    for member in members:
        assert member.isfile() and member.name in old_sources
        import hashlib
        assert hashlib.sha256(archive.extractfile(member).read()).hexdigest() == old_sources[member.name]
    old_text = archive.extractfile(changed[0]).read().decode('utf-8')
new_text = (root / changed[0]).read_text(encoding='utf-8')
assert old_text.replace('Assert.DoesNotContain("\\u202e", text);', 'Assert.DoesNotContain("\\u202e", text, StringComparison.Ordinal);').replace('\r\n', '\n') == new_text
e.assert_clean_build(temp / 'comparison-interchange-build-r1111.txt', 5)
archives = []
for item in proof['platforms']:
    platform = item['platform']
    assert item['guestReuse'] and item['changed'] == ['render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb']
    plan_path = temp / (f'test-fix-delta-{platform}-r1112' if platform != 'windows' else 'test-fix-delta-r1112') / 'plan.json'
    plan = e.read(plan_path)
    changes = [row for row in plan['files'] if row['patch']]
    assert [row['path'] for row in changes] == item['changed']
    assert all(row['sha256'] == row['baselineSha256'] for row in plan['files'] if not row['patch'])
    archive = temp / f'dsa-batch-{platform}-r1112.tgz'
    assert e.sha(archive) == item['sha256']
    archives.append({'platform': platform, 'path': str(archive), 'bytes': item['archiveBytes'], 'sha256': item['sha256']})
packing = {'revision': 'r1112', 'renderRevision': 'r1112', 'sourceManifest': '.tmp/source-candidate-r1112.json',
           'buildLog': '.tmp/comparison-interchange-test-fix-r1112.txt', 'initialBuildLog': '.tmp/comparison-interchange-build-r1111.txt',
           'archiveCount': 3, 'standaloneArchives': 0, 'elapsedSeconds': proof['elapsedSeconds'], 'archives': archives,
           'cachedProductBaseline': 'r1111', 'scope': 'TestOnly'}
(temp / 'pack-timing-r1112.json').write_text(json.dumps(packing, indent=2) + '\n', encoding='utf-8')
app_inputs = {platform: [{'path': p.relative_to(temp / f'batch-r1112-{platform}').as_posix(), 'sha256': e.sha(p)}
    for p in sorted((temp / f'batch-r1112-{platform}/render').glob('FileCat.App.Tests.*'))] for platform in ('windows', 'mac', 'linux')}
assert all(app_inputs.values())
(temp / 'app-settings-inputs-r1112.json').write_text(json.dumps(app_inputs, indent=2) + '\n', encoding='utf-8')
expected = {
    'windows': {'preview': (44, 0), 'core': (1645, 48), 'native': (75, 0), 'render': (647, 1), 'appsettings': (36, 1)},
    'mac': {'preview': (44, 0), 'core': (1627, 4), 'render': (647, 0), 'appsettings': (36, 0)},
    'ubuntu': {'preview': (44, 0), 'core': (1627, 4), 'render': (647, 0), 'appsettings': (36, 0)}
}
record, files, runners, receipts = e.qualify(root, 'r1112', expected,
    ('NativeExternalComparison', 'NativeComparisonReport', 'ComparisonImportAndReportReviews', 'NativeExternalInventoryInputEnforces'), 44, build_projects=1)
for platform in ('windows', 'mac', 'ubuntu'):
    first_path = temp / f'{platform}-batch-r1111-results.tgz'
    first = e.reports(first_path)
    assert first['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=preview'
    a = e.assembly(first, 'preview', 'r1111')
    assert (a.get('total'), a.get('passed'), a.get('failed'), a.get('skipped'), a.get('errors')) == ('44', '32', '12', '0', '0')
    failures = [test for test in a.findall('.//test') if test.get('result') == 'Fail']
    assert all(test.get('method') == 'NativeComparisonReportsFreezeBothInputsAndKeepScopeEscapingAndRedaction' for test in failures)
    assert e.read(temp / f'{platform}-batch-r1111-original-run.json')['terminalExit'] == 1
    assert e.read(temp / f'{platform}-batch-r1111-post.json')['filecatOrDotnet'] == []
    assert e.read(temp / f'{platform}-batch-r1112-original-run.json')['terminalExit'] == 0
    files.update((first_path, temp / f'{platform}-batch-r1111-original-run.json', temp / f'{platform}-batch-r1111-post.json',
                  temp / f'{platform}-batch-r1112-original-run.json'))
    assert 'changedFiles=2' in receipts[platform]['cache-proof.txt'].decode('utf-8-sig')
    tag = 'linux' if platform == 'ubuntu' else platform
    runners.add(temp / f'batch-{tag}-r1111.{"ps1" if platform == "windows" else "sh"}')
windows = json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
assert windows['terminalExit'] == 0 and windows['runLevel'] == 'Limited' and windows['logonType'] == 'Interactive'
files.update((snapshot, temp / 'source-candidate-r1111.json', temp / 'pack-timing-r1111.json',
              temp / 'comparison-interchange-build-r1111.txt', temp / 'test-fix-proof-r1112.json'))
helpers = ['native_batch_evidence.py', 'pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py',
           'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'finalize-comparison-interchange-r1112.py']
record['developmentPreflight'] = {'revision': 'r1111', 'total': 44, 'passed': 32, 'failed': 12,
    'correction': 'The Unicode-control assertion now uses ordinal comparison. Production sources and every cached production input are unchanged.'}
record['qualification'] = {'offlineExternalComparisonImport': True, 'comparisonMarkdownHtmlCsvReports': True,
    'privacyEscapingAndPreservingPublication': True, 'actualCancellationAndRelease': True,
    'newNativeAndReviewCases': 36, 'preflightCases': 44, 'broadSuitesPassedAllThree': True,
    'taskOwnedProcessesExited': True, 'largeIndexedComparisonQualified': False, 'comparisonChartExportComplete': False,
    'fullPhysicalAndResourceQualificationComplete': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
e.retain(root, record, files, runners, helpers)
e.freeze_text(root, record, 'exact-text-r1112-comparison-interchange.tgz')
destination.write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'qualified': record['qualification'], 'matrix': {key: {k: row[k] for k in ('total', 'passed', 'skipped', 'failed')} for key, row in record['matrix'].items()}}, indent=2))
