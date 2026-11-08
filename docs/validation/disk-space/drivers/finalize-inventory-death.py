"""Verify the completed native-file packet before retaining its qualification evidence."""
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

sources = read(temp / 'source-candidate-r965.json')
assert all(sha(root / s['path']) == s['sha256'] for s in sources)
files, drivers, matrix, post = set(), set(), {}, {}
for platform, revision, phases in (
    ('windows', 'r965', ('smoke', 'preview', 'core', 'native', 'render')),
    ('mac', 'r963', ('smoke', 'preview', 'core', 'render')),
    ('ubuntu', 'r963', ('smoke', 'preview', 'core', 'render')),
    ('mac', 'r965', ('smoke', 'preview')),
    ('ubuntu', 'r965', ('smoke', 'preview')),
):
    for phase in phases:
        stem = f'{platform}-batch-{revision}-{phase}-{revision}'
        a = ET.parse(temp / (stem + '.xml')).getroot().find('assembly')
        expected = {'smoke': 1, 'preview': 2 if revision == 'r965' and platform != 'windows' else 27,
                    'core': 1551 if platform == 'windows' else 1533, 'native': 75, 'render': 396}[phase]
        assert int(a.get('total')) == expected and a.get('failed') == a.get('errors') == '0', (stem, a.attrib)
        if phase in ('smoke', 'preview'):
            assert int(a.get('passed')) == expected
        matrix[f'{platform}/{revision}/{phase}'] = {k: a.get(k) for k in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    stem = f'{platform}-batch-{revision}'
    assert (temp / (stem + '-state.txt')).read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=0'
    post[f'{platform}/{revision}'] = read(temp / (stem + '-post.json'))['filecatOrDotnet']
    assert not post[f'{platform}/{revision}']
    files.update(p.name for p in temp.glob(stem + '-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))

failed = []
for revision, expected in (('r959', (25, 19, 6)), ('r963', (27, 0, 27))):
    a = ET.parse(temp / f'windows-batch-{revision}-preview-{revision}.xml').getroot().find('assembly')
    assert tuple(int(a.get(k)) for k in ('total', 'passed', 'failed')) == expected
    failed.append({'platform': 'windows', 'revision': revision, **{k: a.get(k) for k in ('total', 'passed', 'failed', 'errors')}})
    files.update(p.name for p in temp.glob(f'windows-batch-{revision}-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))
for platform in ('mac', 'ubuntu'):
    a = ET.parse(temp / f'{platform}-batch-r959-preview-r959.xml').getroot().find('assembly')
    assert (a.get('total'), a.get('passed'), a.get('failed'), a.get('errors')) == ('25', '25', '0', '0')
    files.update(p.name for p in temp.glob(f'{platform}-batch-r959-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))
for revision in ('r958', 'r960', 'r961', 'r962', 'r964'):
    name = f'build-{revision}.txt'
    content = (temp / name).read_text(encoding='utf-8-sig')
    assert all(s in content for s in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
    files.add(name)
for pattern in ('source-candidate-r959.json', 'source-candidate-r963.json', 'source-candidate-r965.json',
                'pack-timing-r959.json', 'pack-timing-r963.json', 'test-fix-proof-r965.json',
                'windows-space-r963.json', 'windows-cleanup-r964*.json', '*-pre-r965*.json'):
    files.update(p.name for p in temp.glob(pattern) if p.is_file())
cleanup = read(temp / 'windows-cleanup-r964.json')
assert len(cleanup['removed']) == 42 and cleanup['available'] > 6_000_000_000
proof = read(temp / 'test-fix-proof-r965.json')
assert all(p['guestReuse'] and set(p['changed']) == {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'} for p in proof['platforms'])

for revision in ('r959', 'r963', 'r965'):
    for platform in ('windows', 'mac', 'linux'):
        name = f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
        shutil.copy2(temp / name, out / 'drivers' / name)
        drivers.add(name)
drivers.update(('finalize-inventory-death.py', 'pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1',
                'add-unix-previews.ps1', 'add-windows-previews.ps1', 'deploy-batch.ps1',
                'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'cleanup-windows-completed-batches.ps1'))
for name in files:
    shutil.copy2(temp / name, out / name)
record = {
    'purpose': 'Actual owned process death, Windows kernel staging cleanup and retained replacement close outcomes; full plan remains active',
    'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
    'testedWorkingTreeChanges': True, 'sources': sources, 'matrix': matrix,
    'supersededFailures': failed, 'postCollectionProcesses': post,
    'artifacts': [{'path': (out / n).relative_to(root).as_posix(), 'sha256': sha(out / n)} for n in sorted(files)],
    'drivers': [{'path': (out / 'drivers' / n).relative_to(root).as_posix(), 'sha256': sha(out / 'drivers' / n)} for n in sorted(drivers)],
    'qualification': {'processDeathCasesPassedAllThree': 25, 'replacementRootCloseControlsPassedAllThree': 2,
                      'applicableBroadSuitesPassedAllThree': True, 'fullPowerLossQualified': False,
                      'fullNativeDesktopQualified': False, 'fullPersistenceGateQualified': False,
                      'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False},
}
(out / 'source-manifest-r967.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['inventoryProcessDeath'] = {'manifest': 'source-manifest-r967.json', 'matrix': matrix, **record['qualification']}
progress['completedBehaviors'].append('25 actual process-death boundaries and two replacement root-close controls pass Windows/Mac/Ubuntu. Windows staging cleanup is armed atomically at native create; confirmed save receipts survive final close uncertainty. Applicable broad suites pass all three; power-loss, physical desktop and full persistence qualification remain open.')
progress['nextWork'] = [s.replace('Next qualify/fix actual process-death and power-loss/close boundaries, anonymous staging cleanup, physical desktop/theme/DPI/a11y/package controls;', 'Actual process-death/anonymous staging and root-close controls now pass all three (r967). Next qualify power-loss boundaries and physical desktop/theme/DPI/a11y/package controls;') for s in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'manifest': 'r967', 'sources': len(sources), 'artifacts': len(files), 'drivers': len(drivers), 'allApplicablePassed': True}))
