"""Record the isolated rolling store cohort; this does not close the feature/release gates."""
import datetime
import hashlib
import json
import pathlib
import shutil
import subprocess
import xml.etree.ElementTree as ET

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

sources = read(temp / 'source-candidate-r1006.json')
assert all(sha(root / s['path']) == s['sha256'] for s in sources)
matrix, artifacts, drivers = {}, set(), set()
for platform, revision in (('windows', 'r1009'), ('mac', 'r1006'), ('ubuntu', 'r1006')):
    phases = ('smoke', 'preview', 'core', 'native', 'render') if platform == 'windows' else ('smoke', 'preview', 'core', 'render')
    for phase in phases:
        path = temp / f'{platform}-batch-{revision}-{phase}-{revision}.xml'
        a = ET.parse(path).getroot().find('assembly')
        assert a.get('failed') == a.get('errors') == '0'
        if phase in ('smoke', 'preview'):
            assert int(a.get('total')) == int(a.get('passed')) == (27 if phase == 'smoke' else 14)
        else:
            expected = {'core': (1577, 48), 'native': (75, 0), 'render': (410, 1)} if platform == 'windows' else {'core': (1559, 4), 'render': (410, 0)}
            total, skipped = expected[phase]
            assert (int(a.get('total')), int(a.get('passed')), int(a.get('skipped'))) == (total, total - skipped, skipped)
        matrix[f'{platform}/{phase}'] = {k: a.get(k) for k in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    stem = f'{platform}-batch-{revision}'
    assert (temp / f'{stem}-state.txt').read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=0'
    assert not read(temp / f'{stem}-post.json')['filecatOrDotnet']
    artifacts.update(p.name for p in temp.glob(stem + '-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))

# Preserve the environmental failure, not just its successful rerun.
failed = ET.parse(temp / 'windows-batch-r1006-preview-r1006.xml').getroot().find('assembly')
assert (failed.get('passed'), failed.get('failed')) == ('9', '5')
assert not read(temp / 'windows-batch-r1006-post.json')['filecatOrDotnet']
artifacts.update(p.name for p in temp.glob('windows-batch-r1006-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))
proof = read(temp / 'cache-proof-r1009.json')
assert (proof['verifiedInputs'], proof['changedInputs'], proof['archiveBytes']) == (231, 0, 7661)
assert 'cachedFiles=231 changedFiles=0' in (temp / 'windows-batch-r1009-cache-proof.txt').read_text(encoding='utf-8-sig')
for name in ('build-r1001.txt', 'build-r1002.txt', 'build-r1003.txt', 'build-r1004.txt', 'build-r1005.txt', 'source-candidate-r1006.json', 'pack-timing-r1006.json', 'cache-proof-r1009.json',
             'windows-cleanup-r1007-preview.json', 'windows-cleanup-r1007.json', 'windows-cleanup-r1008-preview.json', 'windows-cleanup-r1008.json'):
    artifacts.add(name)
for name in ('build-r1004.txt', 'build-r1005.txt'):
    log = (temp / name).read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in log and all(s in log for s in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
for platform in ('windows', 'mac', 'linux'):
    revision = 'r1006'
    name = f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
    shutil.copy2(temp / name, out / 'drivers' / name)
    drivers.add(name)
shutil.copy2(temp / 'batch-windows-r1009.ps1', out / 'drivers/batch-windows-r1009.ps1')
drivers.add('batch-windows-r1009.ps1')
shutil.copy2(temp / 'deploy-batch.ps1', out / 'drivers/deploy-batch-r1006.ps1')
drivers.add('deploy-batch-r1006.ps1')
drivers.update(('finalize-history-store.py', 'prepare-cached-windows-batch.ps1', 'pack-batch-direct.ps1', 'add-windows-previews.ps1',
                'add-unix-previews.ps1', 'deploy-batch.ps1', 'collect-batch-archive.ps1', 'cleanup-windows-completed-batches.ps1', 'preflight-indexed-r732.ps1'))
for name in artifacts:
    shutil.copy2(temp / name, out / name)
qualification = {'isolatedNativeStoreImplemented': True, 'memoryAndIndexedCaptureCasesPassedAllThree': True,
                 'guardedCountPruningCasesPassedAllThree': True, 'oversizeAndProtectionPreserveFilesAllThree': True,
                 'broadSuitesPassedAllThree': True, 'historySettingsWired': False, 'automaticCompletionHooksWired': False,
                 'historyBrowsingAndPinningImplemented': False, 'fullHistoryFaultAndResourceMatrixQualified': False,
                 'fullPersistenceGateQualified': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record = {'purpose': 'Isolated rolling capture/pruning engine and verified Windows cache-only rerun',
          'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
          'sources': sources, 'matrix': matrix, 'qualification': qualification,
          'artifacts': [{'path': (out / n).relative_to(root).as_posix(), 'sha256': sha(out / n)} for n in sorted(artifacts)],
          'drivers': [{'path': (out / 'drivers' / n).relative_to(root).as_posix(), 'sha256': sha(out / 'drivers' / n)} for n in sorted(drivers)],
          'scope': 'Actual native filesystem captures and guarded count retention from memory/indexed revisions under guest headless tests. No automatic runtime, physical desktop, full fault/resource or final independent review qualification. The pre-transfer space check was added after the executed deployments; its syntax was checked, but it is not claimed as live-qualified here.'}
(out / 'source-manifest-r1010.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['historyNativeStore'] = {'manifest': 'source-manifest-r1010.json', 'matrix': matrix, **qualification}
behavior = 'An internal isolated native history store now saves and verifies complete memory/indexed inventories before guarded pruning; count retention, finite-byte oversize refusal, protected/disabled preservation, named-store isolation and unrelated-file refusal pass actual Windows/Mac/Ubuntu filesystem tests. Shared primitives pass all three broad suites. Runtime completion, Settings, browsing/pinning and full fault/resource qualification remain open.'
if behavior not in progress['completedBehaviors']:
    progress['completedBehaviors'].append(behavior)
progress['nextWork'] = [s.replace('implement native isolated capture/pruning,', 'native isolated capture/count-pruning and broad regressions also pass all three r1010; finish the history fault/resource matrix,') if s.startswith('W3:') else s for s in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'manifest': 'r1010', 'sources': len(sources), 'artifacts': len(artifacts), 'historyComplete': False, 'matrix': matrix}))
