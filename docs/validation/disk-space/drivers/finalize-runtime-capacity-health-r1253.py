"""Run once after all r1250 tests and collections have actually completed."""
import hashlib
import io
import json
import pathlib
import subprocess
import tarfile
import xml.etree.ElementTree as E

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space'
target = d / 'source-manifest-r1253-runtime-capacity-health.json'
outputs = [d / f'exact-{kind}-r1253-runtime-capacity-health.tgz' for kind in ('source-deltas', 'receipts', 'workflow')]
assert not target.exists() and not any(p.exists() for p in outputs), 'Immutable packet already exists.'


def sha(p):
    with p.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


proof = json.loads((t / 'runtime-capacity-health-proof-r1250.json').read_text())
assert all(sha(r / row['path']) == row['sha256'] for row in proof['sources']), 'Current product differs from its tested freeze.'
assert all(sha(r / row['path']) == row['sha256'] for row in proof['reusedCoreSource'])
snapshot = t / 'source-r1250-runtime-capacity-health.tgz'; assert sha(snapshot) == proof['sourceSnapshotSha256']
retained = set(); workflow = {t / 'runtime-capacity-health-proof-r1250.json', t / 'indexed-runtime-capacity-health-build-r1249.txt'}; matrix = {}
for platform in proof['platforms']:
    name = platform['platform']; tag = 'ubuntu' if name == 'linux' else name
    runner = t / f'batch-{name}-r1250.{"ps1" if name == "windows" else "sh"}'
    assert sha(runner) == platform['runnerSha256'] and sha(t / f'dsa-batch-{name}-r1250.tgz') == platform['archiveSha256']
    workflow.add(runner)
    archive = t / f'{tag}-batch-r1250-results.tgz'; post = t / f'{tag}-batch-r1250-post.json'
    assert not json.loads(post.read_text(encoding='utf-8-sig'))['filecatOrDotnet']
    retained.update((archive, post))
    with tarfile.open(archive) as packed:
        assert packed.extractfile('state.txt').read().decode('utf-8-sig').strip() == 'FINISHED exit=0'
        for phase, expected in (('app-rescan', (29, 29, 0, 0, 0)), ('render', (814, 813, 0, 0, 1) if name == 'windows' else (814, 814, 0, 0, 0))):
            a = E.fromstring(packed.extractfile(phase + '.xml').read()).find('assembly')
            counts = tuple(int(a.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped'))
            assert counts == expected, (name, phase, counts)
            matrix[tag + '/' + phase] = dict(zip(('total', 'passed', 'failed', 'errors', 'skipped'), counts))
            matrix[tag + '/' + phase].update(seconds=a.get('time'), skippedNames=[v.get('name') for v in a.findall('.//test') if v.get('result') == 'Skip'])

for name in ('prepare-runtime-capacity-health-r1250.py', 'epoch-app-unix-template-r1194.sh', 'epoch-app-windows-template-r1195.ps1',
             'collect-rescan-ui-r1216.ps1', 'preflight-indexed-r732.ps1', 'deploy-batch.ps1', 'finalize-runtime-capacity-health-r1253.py'):
    workflow.add(d / 'drivers' / name)
assert all(p.is_file() for p in workflow)
base = proof['head']; changed = set(subprocess.check_output(['git', 'diff', '--name-only', base, '--', 'src', 'tests'], text=True).splitlines())
changed.update(subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', 'src', 'tests'], text=True).splitlines())
known = {row['path']: row['sha256'] for row in proof['sources']}; source_bytes = []
with tarfile.open(snapshot) as incoming:
    for path in sorted(changed):
        data = incoming.extractfile(path).read(); assert hashlib.sha256(data).hexdigest() == known[path]
        source_bytes.append((path, data))
manifest = dict(baseCommit=base, productRevision='r1250', matrix=matrix, unchangedCoreSourceProof='Exact source hashes match the previously qualified r1233/r1248 Core inputs.',
    crossWindowCapacityUpdatesQualified=True, observerFailureCannotStrandAdmission=True, memoryAdoptionStatusQualified=True, rescanBindingWatchdogQualified=True,
    publicFeatureRegistered=False, fullW1LifecycleComplete=False, physicalDesktopAccessibilityQualified=False, fullGoalComplete=False, finalIndependentReviewPerformed=False,
    sourceDeltas=[dict(path=p, sha256=hashlib.sha256(data).hexdigest()) for p, data in source_bytes],
    receiptFiles=[dict(path=p.relative_to(t).as_posix(), sha256=sha(p)) for p in sorted(retained)],
    workflowFiles=[dict(path=p.relative_to(r).as_posix(), sha256=sha(p)) for p in sorted(workflow)])
# Validate every test, source and process receipt before writing the immutable outputs.
with tarfile.open(outputs[0], 'w:gz') as out:
    for path, data in source_bytes:
        member = tarfile.TarInfo(path); member.size = len(data); member.mode = 0o600; out.addfile(member, io.BytesIO(data))
for output, files, root in ((outputs[1], retained, t), (outputs[2], workflow, r)):
    with tarfile.open(output, 'w:gz') as out:
        for path in sorted(files): out.add(path, arcname=path.relative_to(root).as_posix(), recursive=False)
manifest['archives'] = [dict(path=p.name, sha256=sha(p), bytes=p.stat().st_size) for p in outputs]
target.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps(dict(phases=len(matrix), changedSources=len(source_bytes))))
