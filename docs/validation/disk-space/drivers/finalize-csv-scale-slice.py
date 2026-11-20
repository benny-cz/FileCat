"""Preserve measured 1M exports and 10M guarded refusals without advertising 10M export support."""
import hashlib
import json
import pathlib
import shutil
from native_batch_evidence import read, reports, sha

root = pathlib.Path.cwd()
out = root / 'docs/validation/disk-space'
name = 'source-manifest-r1102-csv-scale.json'
assert not (out / name).exists()


def document(raw):
    return json.loads(raw.decode('utf-16' if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig'))


files = set()
loaded = {}
for platform, revisions in {'windows': ('r1098', 'r1101', 'r1102'), 'mac': ('r1098', 'r1101'), 'ubuntu': ('r1098', 'r1101')}.items():
    for revision in revisions:
        original = root / f'.tmp/{platform}-csv-scale-{revision}-original.json'
        status = read(original)
        assert status['state'] == 'original-deployment-terminal'
        assert status['exitCode'] == (1 if revision == 'r1098' else 0)
        archive = root / f'.tmp/{platform}-csv-scale-{revision}-results.tgz'
        raw = reports(archive)
        loaded[platform, revision] = document(raw['result.json'])
        post = root / f'.tmp/{platform}-csv-scale-{revision}-post.json'
        inventory = read(post)
        assert inventory['exitCode'] == 0
        for process in inventory['filecatOrDotnet']:
            known_admin = platform == 'windows' and revision == 'r1102' and process == 'pid=9716, owner=DESKTOP-A60F1NE\\Admin, cmd=dotnet.exe'
            if known_admin:
                probe = read(root / '.tmp/windows-foreign-process-r1104.json')
                assert probe['processes'] == []  # Exited before the read-only follow-up; own tests ran only as R2.
                files.add(root / '.tmp/windows-foreign-process-r1104.json')
            else:
                assert 'archive-writing-42829297' in process or 'fcarchive42829297' in process
        files.update((original, archive, post))

record = {'purpose': 'Measured bounded CSV store/export slice; no source traversal or complete 10M export support claim', 'matrix': {}, 'originalFailures': {}}
for platform in ('windows', 'mac', 'ubuntu'):
    original = loaded[platform, 'r1098']
    for shape in ('wide', 'deep'):
        revision = 'r1101' if platform == 'windows' and shape == 'deep' else 'r1098'
        candidates = loaded[platform, revision]['runs']
        run = next(item for item in candidates if item['records'] == 1_000_000 and item['shape'] == shape)
        m = run['measurement']
        assert run['exitCode'] == 0 and run['ownedProcessExited'] and m['passed'] and not m.get('limitRefusal')
        assert m['ownedArtifactsRemoved'] and m['foreignNeighborPreserved'] and m['observedSourceWasNeverCreated']
        assert m['remainingQuotaBytes'] == m['remainingProfileQuotaBytes'] == 0
        assert len(m['reports']) == 2 and {item['redact'] for item in m['reports']} == {False, True}
        assert all(item['lines'] == 1_000_002 and item['verifiedEveryDeclaredRow'] and len(item['sha256']) == 64 for item in m['reports'])
        if platform != 'mac': assert m['privateMemoryBudgetVerified'] and m['sampledPeakPrivateIncrement'] <= 512 * 1024 * 1024
        record['matrix'][f'{platform}/1M/{shape}'] = {'revision': revision, 'measurement': m}
        revision = 'r1102' if platform == 'windows' else 'r1101'
        run = next(item for item in loaded[platform, revision]['runs'] if item['records'] == 10_000_000 and item['shape'] == shape)
        m = run['measurement']
        assert run['exitCode'] == 0 and run['ownedProcessExited'] and m['passed'] and m['limitRefusal']
        assert not m['fullCsvExportQualified'] and m['retainedSourceUnchanged'] and m['destinationWasNeverOpened']
        assert (m['sourcePaths'], m['firstSourcePath'], m['lastSourcePath']) == (10_000_000, 1, 10_000_000)
        assert m['remainingQuotaBytes'] == m['remainingProfileQuotaBytes'] == 0 and m['ownedArtifactsRemoved'] and m['foreignNeighborPreserved']
        record['matrix'][f'{platform}/10M/{shape}'] = {'revision': revision, 'measurement': m}
    failures = [item for item in original['runs'] if item.get('error') or item.get('exitCode') not in (0,)]
    assert failures
    record['originalFailures'][platform] = failures

record['qualification'] = {'oneMillionFileSizeCsvWideDeepBothPrivacyModesAllThree': True,
    'tenMillionScratchRefusalRetainsCompleteSourceAllThree': True, 'taskOwnedProcessesExited': True,
    'tenMillionFullCsvExportQualified': False, 'macProcessPrivateMemoryQualified': False,
    'fullSourceTraversalAndPhysicalQualificationComplete': False, 'fullPlanComplete': False}
for revision in ('r1098', 'r1101', 'r1102'):
    path = root / f'.tmp/csv-scale-manifest-{revision}.json'
    manifest = read(path)
    assert sha(pathlib.Path(manifest['archive'])) == manifest['sha256']
    qualified = read(out / 'source-manifest-r1096-chart-exports.json')
    expected_core = next(item['sha256'] for item in qualified['inputs']['windows'] if item['path'] == 'core/FileCat.Core.dll')
    assert next(item['sha256'] for item in manifest['inputs'] if item['name'] == 'FileCat.Core.dll') == expected_core
    files.add(path)
files.update(root / '.tmp' / value for value in ('csv-scale-source-r1098.tgz', 'csv-scale-drivers-r1101.tgz',
    'csv-scale-build-r1098.txt', 'csv-pinned-build-r1100.txt', 'csv-pinned-build-r1101.txt',
    'windows-retired-payload-cleanup-r1101.json', 'windows-retired-payloads-r1101.json', 'windows-csv-space-probe-r1100.json'))
for path in files:
    destination = out / path.name
    if destination.exists(): assert sha(destination) == sha(path)
    else: shutil.copy2(path, destination)
record['artifacts'] = [{'path': (out / path.name).relative_to(root).as_posix(), 'sha256': sha(path)} for path in sorted(files)]
(out / name).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['csvScaleSlice'] = {'manifest': name, **record['qualification']}
progress['completedBehaviors'].append('Production File-size CSV export over inert indexed fixtures completed 1M-wide/deep datasets with both privacy modes on all three platforms, exact declared row counts and output hashes, bounded scratch, actual cleanup and preserved neighbors. The 10M-wide/deep report captures reached the independent 4GiB scratch ceiling; targeted native checks prove the complete captured source remains readable, no destination opens and derived/source quota returns to zero. This is safe-refusal evidence, not complete 10M CSV export support. Windows output-space refusal was preserved and its focused 1M-deep rerun passed after removing only hash-verified retired task payloads. Mac .NET private bytes remain unavailable; working set is not substituted. Full traversal/physical/private-memory qualification and final independent review remain open.')
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'CSV_SCALE_SLICE_QUALIFICATION.md').write_text('''# CSV scale slice

All three platforms completed production File-size CSV exports for one-million-entry wide and 64-directory-deep inert indexed datasets, with both redaction choices. The harness used the exact previously qualified Core DLL/PDB, native guarded scratch and actual create-new output. It verified declared row counts, output SHA-256, header totals, cleanup and neighboring-file preservation. Sources were generated observations; no live filesystem traversal is claimed. The source distributions, capture/export timings, bytes, scratch and memory samples are retained in [the scale provenance](source-manifest-r1102-csv-scale.json).

Ten-million-entry report capture reached the independent 4 GiB scratch ceiling. Targeted wide/deep controls on every platform confirm that the complete captured source still exposes its first/last records, failed derived work releases its scratch, no destination opens and final source/profile quota returns to zero. Complete ten-million-row CSV export is **not qualified**. This follows the plan's independent limits and narrowed-support requirements; it is not a throughput result for a complete 10M export.

Windows initially reached its real free-space reserve during the 1M deep output. Only old task-owned batch payloads with completed host receipts and matching runner hashes were removed after idle, containment and reparse checks. The focused deep rerun passed; successful earlier cases were retained. The original native failures and helper error handling remain in the evidence. A pinned minimal helper build avoids rebuilding or changing the qualified product while implementation continues.

Windows/Linux sampled private increments are recorded against the 512 MiB reference budget. macOS .NET private bytes were unavailable; working-set samples are reported separately and do not qualify the private-memory budget. Full source-traversal, all-metric/resource/physical qualification and final independent review remain open.
''', encoding='utf-8')
print(json.dumps({'manifest': name, **record['qualification']}))
