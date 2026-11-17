"""Immutable evidence finalizer. Run only after r1247 has ended and been collected on all OSes."""
import hashlib
import io
import json
import pathlib
import subprocess
import tarfile
import xml.etree.ElementTree as E

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space'
target = d / 'source-manifest-r1248-indexed-runtime-views.json'
outputs = [d / f'exact-{kind}-r1248-indexed-runtime-views.tgz' for kind in ('source-deltas', 'receipts', 'workflow')]
assert not target.exists() and not any(p.exists() for p in outputs), 'This immutable packet already exists.'


def sha(p):
    with p.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def load(p): return json.loads(p.read_text(encoding='utf-8-sig'))


product = load(t / 'owned-runtime-view-proof-r1241.json')
fixture = load(t / 'owned-fixture-proof-r1247.json')
fixture_path = 'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.OwnedRuntimeView.cs'
assert fixture['changedSources'] == [dict(path=fixture_path, sha256=sha(r / fixture_path))]
assert all(sha(r / row['path']) == (fixture['changedSources'][0]['sha256'] if row['path'] == fixture_path else row['sha256']) for row in product['sources'])
assert sha(t / 'exact-fixture-r1247-owned-runtime-view.tgz') == fixture['fixtureArchiveSha256']
base = product['head']; retained = set(); workflow = set(); matrix = {}
comparison = 'FileCat.Rendering.Tests.AnalysisRenderingTests.NativeIndexedComparisonCanceledMappingAndAcquisitionPublishNoPartialTab'
scoped = 'FileCat.Rendering.Tests.AnalysisRenderingTests.NativeOwnedRuntimeViewRescanRestoresScopedCrossPageFocusWithoutRetaggingReviewEntries'


def read_phase(packed, key, name, expected, failures=()):
    a = E.fromstring(packed.extractfile(name + '.xml').read()).find('assembly')
    actual = tuple(int(a.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped'))
    assert actual in expected, (key, name, actual, expected)
    failed = [v.get('name') for v in a.findall('.//test') if v.get('result') == 'Fail']
    assert all(v in failures for v in failed), (key, failed)
    matrix[key + '/' + name] = dict(zip(('total', 'passed', 'failed', 'errors', 'skipped'), actual))
    matrix[key + '/' + name].update(seconds=a.get('time'), failedNames=failed,
        skippedNames=[v.get('name') for v in a.findall('.//test') if v.get('result') == 'Skip'])


for tag in ('windows', 'mac', 'ubuntu'):
    win = tag == 'windows'
    for revision in ('r1233', 'r1241', 'r1243', 'r1247'):
        archive = t / f'{tag}-batch-{revision}-results.tgz'; post = t / f'{tag}-batch-{revision}-post.json'
        assert archive.is_file() and not load(post)['filecatOrDotnet'], (tag, revision, 'Missing receipt or non-idle final inventory')
        retained.update((archive, post))
        with tarfile.open(archive) as packed:
            state = packed.extractfile('state.txt').read().decode('utf-8-sig').strip()
            key = tag + '/' + revision
            if revision == 'r1233':
                assert state == ('FINISHED exit=1' if win else 'FINISHED exit=0')
                read_phase(packed, key, 'epoch', [(15, 15, 0, 0, 0)])
                read_phase(packed, key, 'app-rescan', [(14, 14, 0, 0, 0)])
                read_phase(packed, key, 'core', [(2078, 2022, 0, 0, 56) if win else (2078, 2072, 0, 0, 6)])
                read_phase(packed, key, 'render', [(801, 799, 1, 0, 1) if win else (801, 801, 0, 0, 0)], [comparison])
            elif revision == 'r1241':
                assert state == ('FINISHED exit=1' if win else 'FINISHED exit=0')
                read_phase(packed, key, 'app-rescan', [(24, 23, 1, 0, 0) if win else (24, 24, 0, 0, 0)], [scoped])
                if not win: read_phase(packed, key, 'render', [(810, 810, 0, 0, 0)])
            elif revision == 'r1243':
                if win:
                    assert state in ('FINISHED exit=0', 'FAILED')
                    read_phase(packed, key, 'render-r1243', [(9, 9, 0, 0, 0)])
                    read_phase(packed, key, 'render', [(810, 809, 0, 0, 1), (810, 808, 1, 0, 1)], [scoped])
                else:
                    assert state == 'FINISHED exit=1'
                    read_phase(packed, key, 'render-r1243', [(9, 8, 1, 0, 0)], [scoped])
            else:
                assert state == 'FINISHED exit=0'
                read_phase(packed, key, 'render-r1247', [(10, 10, 0, 0, 0)])
            if revision in ('r1243', 'r1247'):
                assert 'changedFiles=2' in packed.extractfile('cache-proof.txt').read().decode('utf-8-sig')

source_records = []
for revision, proof_name, snapshot_name in (
    ('r1233', 'indexed-runtime-helpers-proof-r1233.json', 'source-r1233-indexed-runtime-helpers.tgz'),
    ('r1241', 'owned-runtime-view-proof-r1241.json', 'source-r1241-owned-runtime-view.tgz')):
    proof = load(t / proof_name); snapshot = t / snapshot_name
    assert proof['head'] == base and sha(snapshot) == proof['sourceSnapshotSha256']
    for platform in proof['platforms']:
        suffix = 'ps1' if platform['platform'] == 'windows' else 'sh'
        runner = t / f"batch-{platform['platform']}-{revision}.{suffix}"
        assert sha(runner) == platform['runnerSha256']
        assert sha(t / f"dsa-batch-{platform['platform']}-{revision}.tgz") == platform['archiveSha256']
        workflow.add(runner)
    source_records.append((revision, proof, snapshot)); workflow.add(t / proof_name)

for revision in ('r1243', 'r1247'):
    delta = load(t / f'focused-delta-proof-{revision}.json')
    for row in delta:
        platform = row['platform']; plan_path = t / f'focused-delta-{platform}-{revision}/plan.json'; plan = load(plan_path)
        assert set(row['changed']) == {'FileCat.Rendering.Tests.dll', 'FileCat.Rendering.Tests.pdb'}
        assert sha(t / f'dsa-batch-{platform}-{revision}.tgz') == row['sha256']
        inputs = {v['path'][7:]: v['sha256'] for v in next(p for p in product['platforms'] if p['platform'] == platform)['inputs'] if v['path'].startswith('render/')}
        assert len(inputs) == len(plan['files'])
        for v in plan['files']:
            assert inputs[v['path']] == v['baselineSha256']
            assert v['patch'] == (v['path'] in row['changed'])
            if not v['patch']: assert v['sha256'] == v['baselineSha256']
        workflow.update((plan_path, t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'))
    workflow.add(t / f'focused-delta-proof-{revision}.json')
assert sha(t / 'owned-runtime-view-proof-r1241.json') == fixture['sourceBaselineProofSha256']
assert sha(t / 'focused-delta-proof-r1247.json') == fixture['deltaProofSha256']
for row in fixture['runners']:
    assert sha(t / f"batch-{row['platform']}-r1247.{'ps1' if row['platform'] == 'windows' else 'sh'}") == row['sha256']
continuation = load(t / 'owned-fixture-continuation-proof-r1243.json')
assert sha(t / 'batch-windows-r1243.ps1') == continuation['finalRunnerSha256']
assert sha(t / 'exact-fixture-r1243-owned-runtime-view.tgz') == continuation['fixtureArchiveSha256']
workflow.update((t / 'owned-fixture-proof-r1247.json', t / 'owned-fixture-continuation-proof-r1243.json'))
for name in ('prepare-indexed-runtime-helpers-r1233.py', 'prepare-owned-runtime-view-r1241.py', 'extend-owned-fixture-continuation-r1243.py',
    'prepare-focused-test-delta.ps1', 'run-focused-test-delta.py', 'run-focused-test-delta.ps1',
    'epoch-app-unix-template-r1194.sh', 'epoch-app-windows-template-r1195.ps1', 'collect-rescan-ui-r1216.ps1',
    'collect-app-fixture-native-r1227.ps1', 'deploy-batch.ps1', 'preflight-indexed-r732.ps1', 'finalize-indexed-runtime-views-r1248.py'):
    workflow.add(d / 'drivers' / name)
workflow.update(t / name for name in ('indexed-runtime-helpers-controls-build-r1232.txt', 'indexed-runtime-view-frozen-build-r1240.txt', 'indexed-runtime-resource-fixture-final-build-r1246.txt'))
assert all(p.is_file() for p in workflow)

changed = set(subprocess.check_output(['git', 'diff', '--name-only', base, '--', 'src', 'tests'], text=True).splitlines())
changed.update(subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', 'src', 'tests'], text=True).splitlines())
source_bytes = []
for revision, proof, snapshot in source_records:
    known = {v['path']: v['sha256'] for v in proof['sources']}
    with tarfile.open(snapshot) as incoming:
        for path in sorted(changed & known.keys()):
            data = incoming.extractfile(path).read(); assert hashlib.sha256(data).hexdigest() == known[path]
            source_bytes.append((revision + '/' + path, data, known[path]))
for revision in ('r1243', 'r1247'):
    with tarfile.open(t / f'exact-fixture-{revision}-owned-runtime-view.tgz') as incoming:
        data = incoming.extractfile(fixture_path).read()
        source_bytes.append((revision + '/' + fixture_path, data, hashlib.sha256(data).hexdigest()))

manifest = dict(baseCommit=base, productRevision='r1241', currentFixtureRevision='r1247', rawMatrix=matrix,
    derivedRenderingCoverage=dict(total=811, windowsPassed=810, windowsSkipped=1, macPassed=811, ubuntuPassed=811,
        explanation='Exact r1241 product; full 810-case suites plus the corrected ownership fixture and additional handle-budget recovery case in the test-only r1247 ten-case rerun. Raw failures are retained.'),
    publicFeatureRegistered=False, fullW1LifecycleComplete=False, physicalDesktopAccessibilityQualified=False,
    nativeMillionCurrentProductQualified=False, fullGoalComplete=False, finalIndependentReviewPerformed=False,
    sourceDeltas=[dict(path=p, sha256=s) for p, _, s in source_bytes],
    receiptFiles=[dict(path=p.relative_to(t).as_posix(), sha256=sha(p), bytes=p.stat().st_size) for p in sorted(retained)],
    workflowFiles=[dict(path=p.relative_to(r).as_posix(), sha256=sha(p)) for p in sorted(workflow)])
# Everything above is validation. Immutable output writes begin only after all receipts and inputs agree.
with tarfile.open(outputs[0], 'w:gz') as packed:
    for name, data, _ in source_bytes:
        member = tarfile.TarInfo(name); member.size = len(data); member.mode = 0o600; packed.addfile(member, io.BytesIO(data))
for output, files, root in ((outputs[1], retained, t), (outputs[2], workflow, r)):
    with tarfile.open(output, 'w:gz') as packed:
        for p in sorted(files): packed.add(p, arcname=p.relative_to(root).as_posix(), recursive=False)
manifest['archives'] = [dict(path=p.name, sha256=sha(p), bytes=p.stat().st_size) for p in outputs]
target.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps(dict(rawPhases=len(matrix), sourceDeltas=len(source_bytes), retainedFiles=len(retained))))
