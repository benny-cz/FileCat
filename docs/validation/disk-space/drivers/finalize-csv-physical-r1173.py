"""Retain actual resource runs only after every original controller has ended."""
import datetime
import hashlib
import importlib.util
import json
import pathlib

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1173-csv-physical.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
proof = e.read(temp / 'csv-physical-proof-r1172.json')
assert proof['revision'] == 'r1172' and proof['productRebuilt'] is False
e.assert_clean_build(temp / 'csv-physical-pinned-build-r1172.txt', 1)
assert e.sha(temp / 'source-r1172-csv-physical-instrumentation.tgz') == proof['sourceSnapshotSha256']
fix = e.read(temp / 'csv-physical-mac-fix-r1173.json')
assert fix['baseline'] == 'r1172' and fix['productRebuilt'] is False
assert set(fix['changedInputs']) == {'FileCat.CoreCrashHarness.dll', 'FileCat.CoreCrashHarness.pdb', 'validate.py'}
assert fix['changedSourcePaths'] == ['tests/FileCat.CoreCrashHarness/AnalysisPhysicalMemory.cs']
assert fix['qualifiedCoreSha256'] == proof['qualifiedCoreSha256']
e.assert_clean_build(temp / 'csv-physical-pinned-build-r1173.txt', 1)
e.assert_clean_build(temp / 'csv-physical-original-harness-build-r1173.txt', 1)
assert e.sha(temp / 'source-r1173-csv-physical-counter-fix.tgz') == fix['sourceSnapshotSha256']
source = e.read(temp / 'csv-physical-source-r1172.json')
assert source['qualifiedCoreSha256'] == proof['qualifiedCoreSha256']
qualified = e.read(out / proof['productQualification'])
for platform in ('windows', 'mac', 'ubuntu'):
    path = 'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.dll'
    assert next(p['sha256'] for p in qualified['inputs'][platform] if p['path'] == path) == proof['qualifiedCoreSha256']


def last_json(receipts, name):
    data = receipts[name]
    assert 0 < len(data) <= 1024 * 1024
    encoding = 'utf-16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig'
    return json.loads(data.decode(encoding).splitlines()[-1])


matrix, processes, inputs = {}, {}, {}
files = {temp / 'csv-physical-proof-r1172.json', temp / 'csv-physical-source-r1172.json',
         temp / 'source-r1172-csv-physical-instrumentation.tgz', temp / 'csv-physical-pinned-build-r1172.txt',
         temp / 'csv-physical-mac-fix-r1173.json', temp / 'source-r1173-csv-physical-counter-fix.tgz',
         temp / 'csv-physical-pinned-build-r1173.txt', temp / 'mac-batch-r1172-results.tgz',
         temp / 'csv-physical-original-harness-build-r1173.txt',
         temp / 'mac-batch-r1172-original-run.json', temp / 'mac-batch-r1172-post.json',
         temp / 'mac-footprint-counter-r1171-state.json', temp / 'mac-footprint-counter-control-r1171b.json',
         temp / 'mac-footprint-counter-control-r1171.c',
         temp / 'mac-pre-r1171counterlaunch.json', temp / 'mac-pre-r1171counterpost.json'}
runners = set()
for platform in ('windows', 'mac', 'ubuntu'):
    tag = 'linux' if platform == 'ubuntu' else platform
    revision = 'r1173' if platform == 'mac' else 'r1172'
    terminal_path = temp / f'{platform}-batch-{revision}-original-run.json'
    terminal = e.read(terminal_path)
    assert terminal['terminalExit'] == terminal['controllerExit'] == 0 and terminal['actualOriginalController'] and terminal['sessionId']
    archive = temp / f'{platform}-batch-{revision}-results.tgz'
    receipts = e.reports(archive)
    assert receipts['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=0'
    inventory_path = temp / f'{platform}-batch-{revision}-post.json'
    inventory = e.read(inventory_path)
    assert not inventory['filecatOrDotnet']
    processes[platform] = inventory
    candidate = fix if platform == 'mac' else next(p for p in proof['platforms'] if p['platform'] == tag)
    assert e.sha(temp / f'dsa-batch-{tag}-{revision}.tgz') == candidate['sha256']
    stage = temp / f'batch-{revision}-{tag}'
    actual = {p.relative_to(stage).as_posix(): e.sha(p) for p in stage.rglob('*') if p.is_file()}
    assert actual == {p['path']: p['sha256'] for p in candidate['inputs']}
    assert actual['FileCat.Core.dll'] == proof['qualifiedCoreSha256']
    inputs[platform] = candidate['inputs']
    if platform == 'mac':
        old_stage = temp / 'batch-r1172-mac'
        old_inputs = {p.relative_to(old_stage).as_posix(): e.sha(p) for p in old_stage.rglob('*') if p.is_file()}
        assert old_inputs.keys() == actual.keys()
        assert {p for p in actual if old_inputs[p] != actual[p]} == set(fix['changedInputs'])
    runner = temp / f'batch-{tag}-{revision}.{"ps1" if platform == "windows" else "sh"}'
    assert e.sha(runner) == candidate['runnerSha256']
    runners.add(runner)
    control = last_json(receipts, 'counter.log')
    assert control['passed'] and control['allocationAttempted'] and control['touchedBytes'] == 67108864
    assert control['touched'] >= control['before'] + 56 * 1024 * 1024 and control['released'] <= control['before'] + 12 * 1024 * 1024
    if platform == 'mac':
        assert control['nativeLayoutControlSha256'] == proof['layoutControlSha256'] and control['nativeReads'] >= 4
        assert control['nonAtomicAccountingControlPassed']
        negative = last_json(receipts, 'negative.log')
        assert negative['passed'] is False and negative['allocationAttempted'] is False and negative['nativeReads'] == 0
        matrix['mac/negative-layout'] = negative
    matrix[platform + '/counter'] = control
    for phase, count, shape in [('small-wide', 1000, 'wide'), ('small-deep', 1000, 'deep'),
                                ('wide', 1000000, 'wide'), ('deep', 1000000, 'deep'),
                                ('huge-wide', 10000000, 'wide'), ('huge-deep', 10000000, 'deep')]:
        phase_receipts = e.reports(temp / 'mac-batch-r1172-results.tgz') if platform == 'mac' and phase in ('wide', 'deep') else receipts
        row = last_json(phase_receipts, phase + '.log')
        row['qualificationSourceRevision'] = 'r1172' if platform == 'mac' and phase in ('wide', 'deep') else revision
        assert row['passed'] and row['records'] == count and row['shape'] == shape
        assert row['remainingQuotaBytes'] == row['remainingProfileQuotaBytes'] == 0
        assert row['ownedArtifactsRemoved'] and row['foreignNeighborPreserved'] and row['observedSourceWasNeverCreated']
        if row.get('limitRefusal'):
            assert count == 10000000 and row['fullCsvExportQualified'] is False
            assert row['retainedSourceUnchanged'] and row['destinationWasNeverOpened']
            assert row['firstSourcePath'] == 1 and row['lastSourcePath'] == count
        else:
            assert len(row['reports']) == 2 and {r['redact'] for r in row['reports']} == {True, False}
            assert all(r['verifiedEveryDeclaredRow'] and r['lines'] == count + 2 and len(r['sha256']) == 64 for r in row['reports'])
        if platform == 'mac':
            memory = row['nativePhysicalMemory']
            assert memory['available'] and memory['complete'] and memory['withinPhysicalReferenceCeiling']
            assert memory['nativeLayoutControlSha256'] == proof['layoutControlSha256']
            assert memory['committedPrivateByteBudgetQualified'] is False
            if phase not in ('wide', 'deep'):
                assert memory['observedPeakBytes'] >= max(memory['lifetimePeakBytes'], memory['sampledPeakCurrentBytes'])
            assert row['baselinePrivateBytes'] is None and row['privateMemoryBudgetVerified'] is False
        else:
            assert row['privateMemoryBudgetVerified'] and row['sampledPeakPrivateIncrement'] <= 512 * 1024 * 1024
        matrix[platform + '/' + phase] = row
    if platform == 'windows':
        task = json.loads(receipts['task-outcome.json'].decode('utf-8-sig'))
        environment = json.loads(receipts['environment.json'].decode('utf-8-sig'))
        assert task['terminalExit'] == 0 and task['logonType'] == 'Interactive' and task['runLevel'] == 'Limited'
        assert environment['session'] > 0 and environment['user'].lower().endswith('\\filecatdsa_r2')
    files.update({terminal_path, archive, inventory_path})

native = e.read(temp / 'mac-footprint-counter-control-r1171b.json')
assert native['passed'] and (native['structBytes'], native['footprintOffset'], native['lifetimePeakOffset']) == (296, 72, 240)
assert proof['layoutControlSha256'] == e.sha(temp / 'mac-footprint-counter-control-r1171b.json')
record = dict(revision='r1173', observedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    productQualification=proof['productQualification'], productRebuilt=False, qualifiedCoreSha256=proof['qualifiedCoreSha256'],
    sources=fix['sources'], inputs=inputs, matrix=matrix, postRunProcesses=processes, nativeCounterControl=native,
    memoryMethods=dict(windowsLinux='50 ms sampled .NET private-byte increment; not an absolute peak',
                      mac='Maximum of separate native current samples and kernel lifetime peaks minus pre-workload footprint; distinct from committed private bytes',
                      appleReference='https://developer.apple.com/documentation/xcode/analyzing-the-memory-usage-of-your-metal-app'),
    qualification=dict(generatedCsvOnly=True, sourceTraversalQualified=False, globalMultiSessionMemoryQualified=False,
                       committedPrivateBytesOnMacQualified=False, physicalInputA11yPackageMatrixQualified=False,
                       publicFeatureRegistered=False, fullGoalComplete=False, finalIndependentReviewPerformed=False))
old_mac = e.reports(temp / 'mac-batch-r1172-results.tgz')
assert old_mac['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=huge-wide validation=failed'
assert e.read(temp / 'mac-batch-r1172-original-run.json')['terminalExit'] == 1
old_memory = last_json(old_mac, 'huge-wide.log')['nativePhysicalMemory']
assert old_memory['available'] and not old_memory['complete']
record['originalMeasurementFailure'] = dict(revision='r1172', state='FINISHED exit=1 phase=huge-wide validation=failed', reason=old_memory['reason'])
record['helperOnlyCorrection'] = fix
for item in fix['sources']:
    assert e.sha(root / item['path']) == item['sha256']
runners.add(temp / 'batch-mac-r1172.sh')
runners.add(out / 'drivers/collect-csv-physical-r1172.ps1')
e.retain(root, record, files, runners, ('native_batch_evidence.py', 'prepare-csv-physical-r1172.py',
    'csv-physical-unix-template-r1172.sh', 'csv-physical-windows-template-r1172.ps1',
    'validate-csv-physical-r1172.py', 'collect-csv-physical-r1172.ps1', 'finalize-csv-physical-r1173.py'))
e.freeze_text(root, record, 'exact-receipt-text-r1173-csv-physical.tgz')
destination.write_text(json.dumps(record, indent=2) + '\n')
print('Verified immutable pinned-product resource cohort:', destination)
