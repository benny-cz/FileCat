"""Retain focused history lookup/review evidence; automatic history remains unimplemented."""
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

sources = read(temp / 'source-candidate-r998.json')
assert len(sources) == 4 and all(sha(root / s['path']) == s['sha256'] for s in sources)
files, drivers, matrix = set(), set(), {}
for platform in ('windows', 'mac', 'ubuntu'):
    for phase, expected in (('smoke', 27), ('preview', 9)):
        stem = f'{platform}-batch-r998-{phase}-r998'
        a = ET.parse(temp / (stem + '.xml')).getroot().find('assembly')
        assert int(a.get('total')) == int(a.get('passed')) == expected and a.get('failed') == a.get('errors') == a.get('skipped') == '0'
        matrix[f'{platform}/{phase}'] = {k: a.get(k) for k in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    stem = f'{platform}-batch-r998'
    assert (temp / (stem + '-state.txt')).read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=0'
    assert not read(temp / (stem + '-post.json'))['filecatOrDotnet']
    files.update(p.name for p in temp.glob(stem + '-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))
for platform in ('mac', 'ubuntu'):
    a = ET.parse(temp / f'{platform}-batch-r996-preview-r996.xml').getroot().find('assembly')
    assert (a.get('total'), a.get('passed'), a.get('failed'), a.get('errors')) == ('9', '1', '8', '0')
    assert all('Sub-string found' in ''.join(t.find('failure').itertext()) for t in a.findall('.//test') if t.get('result') == 'Fail')
    for revision in ('r990', 'r996'):
        files.update(p.name for p in temp.glob(f'{platform}-batch-{revision}-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))
for name in ('build-r989.txt', 'build-r992.txt', 'build-r993.txt', 'build-r995.txt', 'build-r997.txt',
             'pack-timing-r990.json', 'pack-timing-r996.json', 'test-fix-proof-r998.json',
             'source-candidate-r990.json', 'source-candidate-r996.json', 'source-candidate-r998.json'):
    files.add(name)
for name in ('build-r989.txt', 'build-r992.txt', 'build-r995.txt', 'build-r997.txt'):
    data = (temp / name).read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in data and all(s in data for s in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
for pattern in ('*-pre-r990*.json', '*-pre-r994check.json', '*-pre-r996.json', '*-pre-r998.json'):
    files.update(p.name for p in temp.glob(pattern))
for revision in ('r990', 'r996', 'r998'):
    for platform in ('windows', 'mac', 'linux'):
        name = f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
        shutil.copy2(temp / name, out / 'drivers' / name)
        drivers.add(name)
drivers.update(('finalize-history-review.py', 'pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1',
                'add-unix-previews.ps1', 'add-windows-previews.ps1', 'deploy-batch.ps1',
                'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'))
for name in files:
    shutil.copy2(temp / name, out / name)
qualification = {'historyPlannerCasesPassedAllThree': 26, 'catalogCapacityControlPassedAllThree': True,
                 'preservingHistoryReviewCasesPassedAllThree': 8, 'diagnosticsControlPassedAllThree': True,
                 'historyCaptureAndPruningImplemented': False, 'historySettingsWired': False,
                 'fullNativeDesktopQualified': False, 'fullPersistenceGateQualified': False,
                 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record = {'purpose': 'Ordered bounded history IDs and preserving retention-review component; runtime history capture/pruning still required',
          'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
          'sources': sources, 'matrix': matrix, 'unchangedSourceBaseline': 'source-manifest-r988.json',
          'artifacts': [{'path': (out / name).relative_to(root).as_posix(), 'sha256': sha(out / name)} for name in sorted(files)],
          'drivers': [{'path': (out / 'drivers' / name).relative_to(root).as_posix(), 'sha256': sha(out / 'drivers' / name)} for name in sorted(drivers)],
          'qualification': qualification,
          'scope': 'Focused actual guest Core/headless UI checks for the changed/new components. Existing logic retains the r988 broad baseline. No physical input, actual history files/pruning, settings enable path or final independent review is qualified here.'}
(out / 'source-manifest-r1000.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['historyRetentionReview'] = {'manifest': 'source-manifest-r1000.json', 'matrix': matrix, **qualification}
behavior = 'History planning uses ordered GUID sets and a copied bounded protection set; a4096-colliding-GUID control passes all three runtimes. Eight preserving retention-review variants pass headless on Windows/Mac/Ubuntu, including default focus, danger semantics, narrow-window footer geometry and escaped bidi path display. Diagnostics still passes. The dialog is not wired to Settings and automatic capture/pruning remains unimplemented.'
if behavior not in progress['completedBehaviors']:
    progress['completedBehaviors'].append(behavior)
progress['nextWork'] = [s.replace('implement native isolated capture/pruning,', 'ordered lookup/collision and headless preserving-review component also pass all three r1000; implement native isolated capture/pruning,') if s.startswith('W3:') else s for s in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'manifest': 'r1000', 'sources': len(sources), 'artifacts': len(files), 'changedComponentCasesPerPlatform': 36, 'historyComplete': False}))
