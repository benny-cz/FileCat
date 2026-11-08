"""Retain the completed native-window subset without publishing unrelated desktop pixels."""
import datetime
import hashlib
import json
import pathlib
import re
import shutil
import subprocess
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
dest = out / 'native-replacement-r978'
dest.mkdir(exist_ok=True)

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

sources = read(temp / 'source-native-r975.json')
prior = read(out / 'source-manifest-r967.json')
assert all(sha(root / s['path']) == s['sha256'] for s in sources + prior['sources'])
matrix, artifacts, local_captures, actual_drivers = {}, set(), [], []
for platform, revision in (('windows', 'r975'), ('mac', 'r975'), ('ubuntu', 'r977')):
    folder = temp / f'{platform}-native-{revision}-results'
    data = read(folder / 'result.json')
    closed = data['closed']
    probe = closed['replacementUiProbe']
    assert data['passed'] and data['exitCode'] == 0 and not data['postProcesses'] and not data.get('forcedOwnedTermination')
    assert closed['state'] == 'closed' and closed['sourceUnchanged'] and not closed['deadlineClosed'] and not closed.get('indexedProbeError')
    assert probe['passed'] and probe['finished'] and probe['sessions'] == 8 and probe['sourceCloseFailures'] == 0
    assert {(c['indexed'], c['replace']) for c in probe['cases']} == {(i, r) for i in (False, True) for r in (False, True)}
    for case in probe['cases']:
        assert case['preservedDefaultAndFocus'] and case['dangerClass']
        assert case['bytesChanged'] == case['savedReferenceInvalidated'] == case['replace']
        assert case['sessionCount'] == (8 if case['indexed'] else 2)
        for capture in case['captures']:
            assert capture['width'] in (480, 1100) and capture['height'] == 620 and capture['windowState'] == 'Normal'
    assert closed['harnessSha256'] == sha(temp / 'desktop-r975-windows/DiskSpaceHarness.dll')
    if platform == 'mac':
        assert len(data['compositorCaptures']) == 8
        for capture in data['compositorCaptures']:
            path = folder / ('compositor-' + capture['frame'])
            assert sha(path) == capture['sha256']
            local_captures.append({'path': path.relative_to(root).as_posix(), 'sha256': sha(path),
                                   'scope': capture['scope'], 'reasonNotCheckedIn': 'Full display includes unrelated background pixels; reviewed locally, unchanged.'})
    matrix[platform] = {'revision': revision, 'cases': len(probe['cases']), 'sessionsAtIndexedReview': 8,
                        'sourceUnchanged': closed['sourceUnchanged'], 'postProcesses': data['postProcesses'],
                        'nativeWindowVisible': data['finished']['nativeWindowVisible'],
                        'appSha256': closed['appSha256'], 'harnessSha256': closed['harnessSha256'],
                        'captures': [c for case in probe['cases'] for c in case['captures']],
                        'launch': data.get('launch'), 'linuxRestore': data.get('linuxRestore')}

# Preserve failed attempts and passing predecessors. Mac compositor captures stay local;
# the retained native-window bitmap frames are explicitly excluded from its visual finding.
for platform, revisions in (('mac', ('r970', 'r972', 'r975')),
                            ('ubuntu', ('r970', 'r972', 'r975', 'r976', 'r977')),
                            ('windows', ('r972', 'r975'))):
    for revision in revisions:
        folder = temp / f'{platform}-native-{revision}-results'
        target = dest / f'{platform}-{revision}'
        target.mkdir(exist_ok=True)
        for source in folder.iterdir():
            if source.is_file() and not source.name.startswith('compositor-') and (source.stat().st_size < 1024**2 or source.suffix == '.png'):
                shutil.copy2(source, target / source.name)
                artifacts.add(target / source.name)
        archive = temp / f'{platform}-native-{revision}-results.tgz'
        retained = dest / archive.name
        if platform == 'mac' and revision == 'r975':
            with tarfile.open(archive) as original, tarfile.open(retained, 'w:gz') as filtered:
                for member in original.getmembers():
                    if not member.name.startswith('compositor-'):
                        filtered.addfile(member, original.extractfile(member))
        else:
            shutil.copy2(archive, retained)
        artifacts.add(retained)

for platform in ('mac', 'ubuntu', 'windows'):
    path = temp / f'{platform}-native-drivers-r978.tgz'
    shutil.copy2(path, dest / path.name)
    artifacts.add(dest / path.name)
    with tarfile.open(path) as archive:
        for member in archive.getmembers():
            assert member.isfile() and member.size < 128 * 1024
            match = re.fullmatch(r'(?:dsa-)?native-(r[0-9]+)/run[.](py|ps1)', member.name)
            assert match, member.name
            name = f'native-r978-{platform}-{match[1]}.{match[2]}'
            executed = out / 'drivers' / name
            executed.write_bytes(archive.extractfile(member).read())
            actual_drivers.append({'path': executed.relative_to(root).as_posix(), 'sha256': sha(executed)})

for name in ('build-desktop-r966.txt', 'build-desktop-r968.txt', 'build-desktop-r969.txt', 'build-desktop-r971.txt', 'build-desktop-r974.txt',
             'pack-native-r970.json', 'pack-native-r972.json', 'pack-native-r975.json', 'pack-native-r976.json', 'pack-native-r977.json',
             'source-native-r970.json', 'source-native-r975.json'):
    shutil.copy2(temp / name, dest / name)
    artifacts.add(dest / name)
for pattern in ('*-pre-r968.json', '*-pre-r970check.json', '*-pre-r972*.json', '*-pre-r973.json', '*-pre-r975check.json', '*-pre-r976check.json', '*-pre-r977check.json'):
    for path in temp.glob(pattern):
        shutil.copy2(path, dest / path.name)
        artifacts.add(dest / path.name)
drivers = ['collect-native-replacement.ps1', 'deploy-native-replacement-windows.ps1', 'deploy-native-replacement.ps1',
           'native-replacement-ui.ps1', 'native-replacement-ui.py', 'pack-native-replacement.ps1',
           'prepare-native-harness-fix.py', 'finalize-native-replacement.py']
record = {
    'purpose': 'Native window control-event, named replacement, actual widths/text scale/theme and native-file subset; complete plan remains active',
    'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
    'sources': sources, 'unchangedProductSourceEvidence': 'source-manifest-r967.json', 'matrix': matrix,
    'artifacts': [{'path': p.relative_to(root).as_posix(), 'sha256': sha(p)} for p in sorted(artifacts)],
    'executedGuestDrivers': actual_drivers,
    'drivers': [{'path': (out / 'drivers' / n).relative_to(root).as_posix(), 'sha256': sha(out / 'drivers' / n)} for n in drivers],
    'localOnlyCompositorCaptures': local_captures,
    'qualification': {'nativeReplacementControlEventsPassedAllThree': True, 'memoryAndIndexedAtCapacity': True,
                      'threeThemesAndLargeTextAt480And1100Widths': True, 'macVisualFindingUsesCompositorOnly': True,
                      'fullPhysicalInputQualified': False, 'fullAccessibilityQualified': False,
                      'fullDpiDisplayPackageMatrixQualified': False, 'fullPersistenceGateQualified': False,
                      'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False},
}
(out / 'source-manifest-r978.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['nativeNamedReplacement'] = {'manifest': 'source-manifest-r978.json', 'matrix': matrix, **record['qualification']}
behavior = 'Native Keep/Replace control-event cases pass Windows/Mac/Ubuntu for memory and indexed views, including saved-reference invalidation at eight tabs. Actual480/1100 widths, font20 and Classic/Dark/HighContrast are checked; Mac visuals use its authorized compositor helper because native bitmap overlay capture is inaccurate. Sources remain unchanged and all task apps exit. Full physical input, a11y, display/package and persistence gates remain open.'
if behavior not in progress['completedBehaviors']:
    progress['completedBehaviors'].append(behavior)
progress['nextWork'] = [s.replace('Ubuntu dynamic restore/resize remains unqualified;', 'Ubuntu normal native resize at480/1100 now passes (r978); maximize/restore and display changes remain unqualified;') for s in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'manifest': 'r978', 'nativeCases': {p: m['cases'] for p, m in matrix.items()}, 'artifacts': len(artifacts), 'exactGuestDrivers': len(actual_drivers), 'rawCompositorFramesRetainedLocally': len(local_captures)}))
