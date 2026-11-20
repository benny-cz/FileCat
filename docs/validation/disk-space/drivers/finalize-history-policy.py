"""Qualify finite history preferences/planning, not the unfinished capture/pruning feature."""
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

sources = read(temp / 'source-candidate-r986.json')
assert len(sources) == 8 and all(sha(root / s['path']) == s['sha256'] for s in sources)
files, drivers, matrix, post = set(), set(), {}, {}
specs = (
    ('windows', 'r982', ('smoke', 'preview', 'native', 'render')),
    ('mac', 'r982', ('smoke', 'preview', 'core', 'render')),
    ('ubuntu', 'r982', ('smoke', 'preview', 'render')),
    ('mac', 'r986', ('smoke', 'preview')),
    ('ubuntu', 'r986', ('smoke', 'preview', 'core')),
    ('windows', 'r987', ('smoke', 'preview', 'core')),
)
for platform, revision, phases in specs:
    for phase in phases:
        stem = f'{platform}-batch-{revision}-{phase}-{revision}'
        a = ET.parse(temp / (stem + '.xml')).getroot().find('assembly')
        expected = {'smoke': 25 if revision == 'r982' else 233, 'preview': 1,
                    'core': 1576 if platform == 'windows' else 1558, 'native': 75, 'render': 397}[phase]
        assert int(a.get('total')) == expected and a.get('failed') == a.get('errors') == '0', (stem, a.attrib)
        if phase in ('smoke', 'preview'):
            skips = 1 if platform == 'windows' and revision == 'r987' and phase == 'smoke' else 0
            assert int(a.get('passed')) == expected - skips and int(a.get('skipped')) == skips
            if skips:
                skipped = [t.get('name') for t in a.findall('.//test') if t.get('result') == 'Skip']
                assert skipped == ['FileCat.Core.Tests.AnalysisCatalogTests.NativeUnixCatalogCanBeReopenedAndNeverFollowsMetadataLinks']
        matrix[f'{platform}/{revision}/{phase}'] = {k: a.get(k) for k in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    stem = f'{platform}-batch-{revision}'
    state = (temp / (stem + '-state.txt')).read_text(encoding='utf-8-sig').strip()
    assert state == ('FINISHED exit=1' if revision == 'r982' and platform != 'mac' else 'FINISHED exit=0')
    post[f'{platform}/{revision}'] = read(temp / (stem + '-post.json'))['filecatOrDotnet']
    assert not post[f'{platform}/{revision}']
    files.update(p.name for p in temp.glob(stem + '-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))

failed = []
for platform, count in (('windows', 3), ('ubuntu', 7)):
    a = ET.parse(temp / f'{platform}-batch-r982-core-r982.xml').getroot().find('assembly')
    assert int(a.get('failed')) == count and a.get('errors') == '0'
    failures = [t for t in a.findall('.//test') if t.get('result') == 'Fail']
    assert all('shared private catalog memory budget' in ''.join(t.find('failure').itertext()) for t in failures)
    failed.append({'platform': platform, 'revision': 'r982', 'phase': 'core', 'failures': [t.get('name') for t in failures],
                   'cause': 'Concurrent tests competed for the two-result process-wide catalog reservation; catalog users now share the existing exclusive capacity-test collection.'})
a = ET.parse(temp / 'windows-batch-r986-smoke-r986.xml').getroot().find('assembly')
assert (a.get('total'), a.get('passed'), a.get('failed'), a.get('skipped'), a.get('errors')) == ('233', '232', '0', '1', '0')
files.update(p.name for p in temp.glob('windows-batch-r986-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))

proof = read(temp / 'test-fix-proof-r986.json')
assert all(p['guestReuse'] and set(p['changed']) == {'core/FileCat.Core.Tests.dll', 'core/FileCat.Core.Tests.pdb'} for p in proof['platforms'])
driver_only = read(temp / 'driver-only-proof-r987.json')
assert driver_only['changedFiles'] == 0 and driver_only['verifiedInputs'] == 231
for name in ('build-r980.txt', 'build-r981.txt', 'build-r983.txt', 'build-r985.txt',
             'source-candidate-r982.json', 'source-candidate-r984.json', 'source-candidate-r986.json',
             'pack-timing-r982.json', 'test-fix-proof-r984.json', 'test-fix-proof-r986.json', 'driver-only-proof-r987.json'):
    files.add(name)
for name in ('build-r981.txt', 'build-r983.txt', 'build-r985.txt'):
    text = (temp / name).read_text(encoding='utf-8-sig')
    assert all(s in text for s in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
for pattern in ('*-pre-r980.json', '*-pre-r982.json', '*-pre-r986.json', '*-pre-r987.json'):
    files.update(p.name for p in temp.glob(pattern))
for revision in ('r982', 'r986'):
    for platform in ('windows', 'mac', 'linux'):
        name = f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
        shutil.copy2(temp / name, out / 'drivers' / name)
        drivers.add(name)
name = 'batch-windows-r987.ps1'
shutil.copy2(temp / name, out / 'drivers' / name)
drivers.add(name)
drivers.update(('finalize-history-policy.py', 'pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1',
                'add-windows-previews.ps1', 'add-unix-previews.ps1', 'deploy-batch.ps1',
                'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'))
for name in files:
    shutil.copy2(temp / name, out / name)
qualification = {'newHistoryPlannerCasesPassedAllThree': 25, 'actualDiagnosticsZipControlPassedAllThree': True,
                 'applicableBroadSuitesPassedAllThree': True, 'historyCaptureAndPruningImplemented': False,
                 'settingsReviewAndHistoryBrowsingImplemented': False, 'fullPersistenceGateQualified': False,
                 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record = {'purpose': 'Finite configurable rolling-history preferences, data-only bounded retention proposals and actual diagnostics ZIP; complete history implementation remains open',
          'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
          'sources': sources, 'matrix': matrix, 'supersededFailures': failed, 'postCollectionProcesses': post,
          'artifacts': [{'path': (out / name).relative_to(root).as_posix(), 'sha256': sha(out / name)} for name in sorted(files)],
          'drivers': [{'path': (out / 'drivers' / name).relative_to(root).as_posix(), 'sha256': sha(out / 'drivers' / name)} for name in sorted(drivers)],
          'qualification': qualification,
          'scope': 'No automatic history files, pruning authority, public settings action or runtime approval is created by these data types. Capture, guarded deletion/leases, reviewed settings and native lifecycle/browsing still require implementation and qualification.'}
(out / 'source-manifest-r988.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['rollingHistoryPolicy'] = {'manifest': 'source-manifest-r988.json', 'matrix': matrix, **qualification}
behavior = 'Finite configurable rolling-history preferences default off (10 analyses/30 days/1 GiB); bounded age/count/byte proposals preserve complete oversized inputs, protected entries and uncertain clocks without granting mutation authority. 25 Core cases and an actual diagnostics-ZIP privacy control pass all three, as do applicable broad suites after catalog tests were isolated correctly. Automatic capture/pruning and reviewed history settings/browsing remain unimplemented.'
if behavior not in progress['completedBehaviors']:
    progress['completedBehaviors'].append(behavior)
progress['nextWork'] = [s.replace('optional capped rolling history,', 'optional capped rolling history (finite preferences/data-only planner and diagnostics control pass all three r988; implement native isolated capture/pruning, memory/indexed completion hooks, reviewed settings, deletion/browsing/pinning and full lifecycle),') if s.startswith('W3:') else s for s in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'manifest': 'r988', 'sources': len(sources), 'artifacts': len(files), 'drivers': len(drivers), 'historyFeatureComplete': False}))
