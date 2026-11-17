"""Retain the completed merged-product cohort, including its narrowly scoped helper reruns."""
import datetime
import importlib.util
import json
import pathlib
import tarfile
import xml.etree.ElementTree as ET

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1170-main-sync-input-capacity.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)

# Admission failures, syntax errors and original failed fixtures are retained,
# never converted into passing source runs. Verify actual terminal handles first.
for platform in ('windows', 'mac', 'ubuntu'):
    terminal = e.read(temp / f'{platform}-batch-r1170-original-run.json')
    assert terminal['terminalExit'] == terminal['controllerExit'] == 0
    assert terminal['actualOriginalController'] and terminal['sessionId']
    assert e.reports(temp / f'{platform}-batch-r1170-results.tgz')['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=0'
for platform, code in (('mac', 0), ('ubuntu', 1)):
    terminal = e.read(temp / f'{platform}-batch-r1169-original-run.json')
    assert terminal['terminalExit'] == terminal['controllerExit'] == code and terminal['actualOriginalController']
e.assert_clean_build(temp / 'input-case-capacity-build-r1168.txt', 4)
e.assert_clean_build(temp / 'input-case-capacity-render-helper-build-r1169.txt', 1)
e.assert_clean_build(temp / 'input-case-capacity-core-helper-build-r1170.txt', 1)

proofs = [e.read(temp / f'test-fix-proof-{r}.json') for r in ('r1169', 'r1170')]
allowed = [
    {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'},
    {'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.dll', 'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.pdb'},
]
for proof, previous, permitted in zip(proofs, ('r1168', 'r1169'), allowed):
    assert proof['baseline'] == previous and proof['scope'] == 'TestOnly'
    assert proof['producerSha256'] == e.sha(out / 'drivers/prepare-test-fix-batch.ps1')
    for platform in ('windows', 'mac', 'linux'):
        before = temp / f'batch-{previous}-{platform}'
        after = temp / f'batch-{proof["revision"]}-{platform}'
        a = {p.relative_to(before).as_posix(): e.sha(p) for p in before.rglob('*') if p.is_file()}
        b = {p.relative_to(after).as_posix(): e.sha(p) for p in after.rglob('*') if p.is_file()}
        row = next(p for p in proof['platforms'] if p['platform'] == platform)
        assert a.keys() == b.keys() and {p for p in a if a[p] != b[p]} == permitted == set(row['changed'])
        assert row['verifiedInputs'] == len(b)
        assert e.sha(temp / f'dsa-batch-{platform}-{proof["revision"]}.tgz') == row['sha256']
source = {r: {s['path']: s['sha256'] for s in e.read(temp / f'source-candidate-{r}.json')}
          for r in ('r1168', 'r1169', 'r1170')}
assert source['r1168'].keys() == source['r1169'].keys() == source['r1170'].keys()
assert {p for p in source['r1169'] if source['r1168'][p] != source['r1169'][p]} == {'tests/FileCat.Rendering.Tests/ColumnOwnershipBaselineTests.cs'}
assert {p for p in source['r1170'] if source['r1169'][p] != source['r1170'][p]} == {'tests/FileCat.Core.Tests/UnixOwnershipTests.cs'}

archives = [dict(platform=p['platform'], path=str(temp / f'dsa-batch-{p["platform"]}-r1170.tgz'), sha256=p['sha256'], bytes=p['archiveBytes'])
            for p in proofs[-1]['platforms']]
packing = dict(revision='r1170', mode='full-product-r1168-plus-verified-render-and-core-test-pairs',
               buildLog='.tmp/input-case-capacity-core-helper-build-r1170.txt',
               sourceManifest='.tmp/source-candidate-r1170.json', archiveCount=3, archives=archives)
settings = {p: [dict(path=f.relative_to(temp / f'batch-r1170-{p}').as_posix(), sha256=e.sha(f))
                for f in sorted((temp / f'batch-r1170-{p}/render').glob('FileCat.App.Tests.*'))]
            for p in ('windows', 'mac', 'linux')}
assert all(settings.values())
for path, data in ((temp / 'pack-timing-r1170.json', packing), (temp / 'app-settings-inputs-r1170.json', settings)):
    if path.exists():
        assert e.read(path) == data, 'A derived input receipt changed'
    else:
        path.write_text(json.dumps(data, indent=2) + '\n')

expected = {
    'windows': {'preview': (6, 0), 'core': (1992, 48), 'native': (75, 0), 'render': (779, 1), 'appsettings': (36, 1)},
    'mac': {'preview': (6, 0), 'core': (1974, 5), 'render': (779, 0), 'appsettings': (36, 0)},
    'ubuntu': {'preview': (6, 0), 'core': (1974, 5), 'render': (779, 0), 'appsettings': (36, 0)},
}
overlays = {p + '/' + phase: 'r1169' for p in ('mac', 'ubuntu') for phase in expected[p]}
skip = 'FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
record, files, runners, receipts = e.qualify(root, 'r1170', expected,
    ('NativeExternalInventorySuffixCase', 'NativeIndexedComparisonCapacity'), 6, build_projects=1,
    phase_revisions=overlays, source_snapshot='.tmp/source-r1170-input-case-capacity.tgz',
    source_expected_states={'mac/r1169': 'FINISHED exit=0', 'ubuntu/r1169': 'FINISHED exit=1'},
    additional_skips={p + '/core': [skip] for p in ('mac', 'ubuntu')})
record['productRevision'] = 'r1168'
record['renderFixtureRevision'] = 'r1169'
record['coreFixtureRevision'] = 'r1170'
record['testOnlyDeltas'] = proofs
record['runnerScopes'] = e.read(temp / 'runner-scopes-r1170.json')

# Counts and exact skip identities for the newly selected main/Owner families
# are audited from their native fixtures separately; no old baseline is invented.
extra_expected = e.read(temp / 'audited-extra-suites-r1170.json')
assert set(extra_expected) == {'windows/metacore', 'mac/metacore', 'ubuntu/metacore', 'windows/ownernative',
                               'windows/ownerapp', 'mac/ownerapp', 'ubuntu/ownerapp',
                               'windows/ownerrender', 'mac/ownerrender', 'ubuntu/ownerrender'}
for key, declared in extra_expected.items():
    platform, phase = key.split('/')
    revision = 'r1169' if platform != 'windows' and phase in ('ownerapp', 'ownerrender') else 'r1170'
    result = e.reports(temp / f'{platform}-batch-{revision}-results.tgz')
    a = e.assembly(result, phase, revision)
    skips = sorted(t.get('name') for t in a.findall('.//test') if t.get('result') == 'Skip')
    assert a.get('failed') == a.get('errors') == '0'
    assert int(a.get('total')) == declared['total'] and skips == sorted(declared['skippedNames'])
    assert int(a.get('passed')) + len(skips) == int(a.get('total'))
    record['matrix'][key] = dict(revision=revision, **{k: a.get(k) for k in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}, skippedNames=skips)
    if phase == 'ownerrender':
        assert int(a.get('total')) == (1 if platform == 'windows' else 4)
        assert all(t.get('result') == 'Pass' for t in a.findall('.//test') if t.get('method') == 'Capture_feature_off_render_and_metadata_sort_baselines')

for platform in ('windows', 'mac', 'ubuntu'):
    revision = 'r1170' if platform == 'windows' else 'r1169'
    result = e.reports(temp / f'{platform}-batch-{revision}-results.tgz')
    smoke = e.assembly(result, 'smoke', revision)
    assert smoke.get('total') == smoke.get('passed') == '12' and smoke.get('failed') == smoke.get('errors') == '0'
    names = {t.get('name') for t in smoke.findall('.//test')}
    assert len(names) == 12 and all(t.get('type') == 'FileCat.Core.Tests.AnalysisInventoryInputPathTests' for t in smoke.findall('.//test'))
    broad = {t.get('name') for t in e.assembly(result, 'core', revision).findall('.//test') if t.get('result') == 'Pass'}
    assert names <= broad
    record.setdefault('newCoreCases', {})[platform] = sorted(names)

old_windows = e.reports(temp / 'windows-batch-r1166b-results.tgz')
owner = e.assembly(old_windows, 'ownerrender', 'r1166b')
assert owner.get('total') == owner.get('passed') == '4' and owner.get('failed') == owner.get('errors') == '0'
record['reusedWindowsOwnerChecks'] = [dict(name=t.get('name'), result=t.get('result'), time=t.get('time'))
    for t in owner.findall('.//test') if t.get('method') != 'Capture_feature_off_render_and_metadata_sort_baselines']
assert len(record['reusedWindowsOwnerChecks']) == 3
record['reusedWindowsOwnerChecksMeaning'] = 'Original merged-product Owner enabled/listing and font checks passed. New product source changes affect only analyzer comparison admission and chosen input suffixes; the changed baseline fixture ran again. No binary identity across r1166/r1168 is claimed.'

for platform in ('windows', 'mac', 'ubuntu'):
    versions = {'column-baseline': 'r1170' if platform == 'windows' else 'r1169',
                'owner-performance': 'r1166b' if platform == 'windows' else 'r1169',
                'ownership-analysis': 'r1166b' if platform == 'windows' else 'r1169'}
    for prefix, revision in versions.items():
        result = e.reports(temp / f'{platform}-batch-{revision}-results.tgz')
        name = f'{prefix}-{revision}.json'
        data = result[name]
        assert len(data) < 16 * 1024 * 1024 and json.loads(data.decode('utf-8-sig'))['samples']
        record.setdefault('ownerBenchmarkReceipts', {})[platform + '/' + prefix] = dict(revision=revision, filename=name)

task = json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
environment = json.loads(receipts['windows']['environment.json'].decode('utf-8-sig'))
assert task['terminalExit'] == 0 and task['logonType'] == 'Interactive' and task['runLevel'] == 'Limited'
assert environment['session'] > 0 and environment['user'].lower().endswith('\\filecatdsa_r2')

# Ensure the reused Ubuntu batch failed only at the identified NSS fixture.
ubuntu_old = e.reports(temp / 'ubuntu-batch-r1169-results.tgz')
failures = [t.get('name') for name in ubuntu_old if name.endswith('.xml')
            for t in ET.fromstring(ubuntu_old[name]).findall('.//test') if t.get('result') == 'Fail']
assert failures == ['FileCat.Core.Tests.UnixOwnershipTests.Native_linux_owned_files_folders_and_broken_links_match_independent_stat']
record['nativeNssOracle'] = e.read(temp / 'ubuntu-native-nss-oracle-r1169.json')
assert record['nativeNssOracle'][0]['error'] == 0 and record['nativeNssOracle'][0]['found']
assert record['nativeNssOracle'][1] == dict(uid=4294967294, error=2, found=False)
record['qualification'] = dict(mergedMain='68a435dc', mergeCommit='b96dda68',
    newCoreCases=12, newNativeFilesystemHeadlessUiCases=6, exactChosenInputSpellingPreserved=True,
    capacityRefusedBeforeLoaderAndRecoveredAfterActualClose=True, mainOwnerAffectedSuitesPassed=True,
    ownerSyntheticBenchmarksAreNotDiskSpaceMillionEntryQualification=True,
    fullLargeResourcePhysicalInputA11yPackageMatrixQualified=False, publicFeatureRegistered=False,
    fullGoalComplete=False, finalIndependentReviewPerformed=False)

for revision in ('r1166', 'r1166b', 'r1169', 'r1170'):
    for platform in ('windows', 'mac', 'ubuntu'):
        for suffix in ('results.tgz', 'original-run.json', 'post.json'):
            path = temp / f'{platform}-batch-{revision}-{suffix}'
            if path.exists():
                files.add(path)
        path = temp / f'batch-{"linux" if platform == "ubuntu" else platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
        if path.exists():
            runners.add(path)
for name in ('source-candidate-r1166.json', 'source-r1166-main-sync.tgz', 'main-sync-build-r1166.txt',
             'source-candidate-r1167.json', 'source-r1167-input-case-capacity.tgz', 'input-case-capacity-build-r1167.txt',
             'source-candidate-r1168.json', 'source-r1168-input-case-capacity.tgz', 'input-case-capacity-build-r1168.txt',
             'source-candidate-r1169.json', 'source-r1169-input-case-capacity.tgz', 'input-case-capacity-render-helper-build-r1169.txt',
             'test-fix-proof-r1169.json', 'test-fix-proof-r1170.json', 'runner-scopes-r1170.json',
             'frozen-continuation-proof-r1166b.json', 'audited-extra-suites-r1170.json', 'ubuntu-native-nss-oracle-r1169.json',
             'windows-r1166b-observation-r1169-wait.json'):
    files.add(temp / name)
for name in ('collect-main-sync-r1166b.ps1', 'collect-main-sync-r1166b-zsh-original.ps1',
             'main-sync-windows-template-r1166b-as-used.ps1', 'main-sync-unix-template-r1166b-as-used.sh'):
    runners.add(temp / name)
e.retain(root, record, files, runners, ('native_batch_evidence.py', 'prepare-test-fix-batch.ps1',
    'deploy-batch.ps1', 'preflight-indexed-r732.ps1', 'prepare-main-sync-continuation-r1166b.py',
    'main-sync-windows-template-r1166.ps1', 'main-sync-unix-template-r1166.sh', 'finalize-main-sync-input-capacity-r1170.py'))
e.freeze_text(root, record, 'exact-receipt-text-r1170-main-sync-input-capacity.tgz')
destination.write_text(json.dumps(record, indent=2) + '\n')
print('Verified immutable main integration and input/capacity cohort:', destination)
