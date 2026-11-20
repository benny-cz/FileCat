"""Retain the three-platform r1252 native million-file evidence once, without re-running tests."""
import hashlib
import json
import pathlib
import tarfile
import xml.etree.ElementTree as E

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space'
target = d / 'source-manifest-r1265-native-million-timestamps.json'
receipts = d / 'exact-receipts-r1265-native-million-timestamps.tgz'; workflow = d / 'exact-workflow-r1265-native-million-timestamps.tgz'
assert not any(p.exists() for p in (target, receipts, workflow)), 'Immutable evidence already exists.'


def sha(p):
    with p.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


proof = json.loads((t / 'cached-native-million-proof-r1252.json').read_text())
product_path = t / 'runtime-capacity-health-proof-r1250.json'; product = json.loads(product_path.read_text())
assert sha(product_path) == proof['baselineProofSha256'] and proof['productRebuilt'] is False
matrix = {}; retained = set(); workflows = {t / 'cached-native-million-proof-r1252.json', product_path, t / 'windows-retained-fixture-observed-r1251.json'}
for platform in proof['platforms']:
    name = platform['platform']; tag = 'ubuntu' if name == 'linux' else name
    baseline = next(v for v in product['platforms'] if v['platform'] == name)
    assert platform['baselineInputProof'] == baseline
    inputs = {v['path'][7:]: v['sha256'] for v in baseline['inputs'] if v['path'].startswith('render/')}
    plan_path = t / f'cached-native-r1252-{name}/plan.json'; plan = json.loads(plan_path.read_text())
    assert inputs == {v['path']: v['sha256'] for v in plan['inputs']} and len(inputs) == platform['verifiedInputCount']
    archive = t / f'dsa-batch-{name}-r1252.tgz'; runner = t / f'batch-{name}-r1252.{"ps1" if name == "windows" else "sh"}'
    assert sha(archive) == platform['archiveSha256'] and sha(runner) == platform['runnerSha256'] and sha(plan_path) == platform['planSha256']
    workflows.update((plan_path, runner))
    source = t / f'{tag}-batch-r1252-results.tgz'; post = t / f'{tag}-batch-r1252-post.json'
    assert not json.loads(post.read_text(encoding='utf-8-sig'))['filecatOrDotnet']
    retained.update((source, post))
    with tarfile.open(source) as packed:
        assert packed.extractfile('state.txt').read().decode('utf-8-sig').strip() == 'FINISHED exit=0'
        cache = json.loads(packed.extractfile('cache-proof.json').read().decode('utf-8-sig'))
        assert cache['changedProductFiles'] == 0 and cache['verifiedFiles'] == len(inputs)
        for shape in ('wide', 'deep'):
            prefix = 'case-1000000-' + shape + '/'
            a = E.fromstring(packed.extractfile(prefix + 'tests.xml').read()).find('assembly')
            assert tuple(int(a.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped')) == (1, 1, 0, 0, 0)
            receipt_data = packed.extractfile(prefix + 'receipt.json').read(); receipt = json.loads(receipt_data.decode('utf-8-sig'))
            assert receipt['completed'] and receipt['cleanup'] and receipt['phase'] == 'closed' and receipt['failure'] is None
            assert receipt['count'] == 1000000 and receipt['shape'] == shape and receipt['paths'] == (1000033 if shape == 'deep' else 1000001)
            assert receipt['sourceCloseFailures'] == 0 and receipt['remainingSharedQuotaBytes'] == 0 and receipt['reusedSource']
            assert receipt['progressReports'] > 0
            if name == 'mac':
                physical = receipt['nativePhysicalMemory']; assert physical['available'] and physical['complete'] and physical['withinPhysicalReferenceCeiling']
                assert not physical['committedPrivateByteBudgetQualified']
            else: assert receipt['privateMemoryBudgetVerified'] and receipt['sampledPeakPrivateIncrementBytes'] <= 512 * 1024 * 1024
            matrix[tag + '/' + shape] = receipt
            if name == 'windows' and shape == 'deep':
                flattened = json.loads(packed.extractfile('flatten.json').read().decode('utf-8-sig'))
                assert flattened['phase'] == 'complete' and flattened['priorReceiptSha256'].lower() == hashlib.sha256(receipt_data).hexdigest()
                negative = json.loads(packed.extractfile('flatten-negative.json.error.json').read().decode('utf-8-sig'))
                assert 'Unknown completed deep case' in negative['error']

for name in ('prepare-cached-native-million-r1252.py', 'cached-native-million-r1252.py', 'cached-native-million-template-r1252.ps1',
    'flatten-retained-native-fixture-r1252.ps1', 'collect-native-million-r1252.ps1', 'validate-native-optimization-r1184.py',
    'deploy-batch.ps1', 'preflight-indexed-r732.ps1', 'finalize-native-million-timestamps-r1265.py'):
    workflows.add(d / 'drivers' / name)
workflows.add(t / 'cached-native-r1252-mac/native-counter-layout.json')
assert all(p.is_file() for p in workflows)
manifest = dict(productRevision='r1250', nativeFixtureRevision='r1252', sourcePacket='source-manifest-r1253-runtime-capacity-health.json',
    sourcePacketSha256=sha(d / 'source-manifest-r1253-runtime-capacity-health.json'), productSourceSnapshotSha256=product['sourceSnapshotSha256'],
    mergedMainIntegrationIncluded=False, matrix=matrix, allThreeNativeMillionWideDeepPassed=True, allSourcePathsVerified=True,
    actualOwnerClosureAndZeroQuota=True, macCommittedPrivateBudgetQualified=False, strict256MiBReleaseBudgetQualified=False,
    tenMillionQualified=False, globalMultiSessionBudgetQualified=False, firstVisibleReleaseBudgetQualified=False,
    physicalDesktopAccessibilityQualified=False, publicFeatureRegistered=False, fullGoalComplete=False, finalIndependentReviewPerformed=False,
    receiptFiles=[dict(path=p.relative_to(t).as_posix(), sha256=sha(p)) for p in sorted(retained)],
    workflowFiles=[dict(path=p.relative_to(r).as_posix(), sha256=sha(p)) for p in sorted(workflows)])
for output, files, root in ((receipts, retained, t), (workflow, workflows, r)):
    with tarfile.open(output, 'w:gz') as packed:
        for path in sorted(files): packed.add(path, arcname=path.relative_to(root).as_posix(), recursive=False)
manifest['archives'] = [dict(path=p.name, sha256=sha(p), bytes=p.stat().st_size) for p in (receipts, workflow)]
target.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps(dict(cases=len(matrix), retainedFiles=len(retained), windowsRetainedFixtureNow='wide')))
