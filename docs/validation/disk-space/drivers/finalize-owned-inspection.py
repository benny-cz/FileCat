"""Finalize r893 only after all original guest runners and collectors are terminal."""
import datetime, hashlib, json, pathlib, shutil, subprocess, xml.etree.ElementTree as ET

r = pathlib.Path.cwd()
t = r / '.tmp'
out = r / 'docs/validation/disk-space'
revision = 'r895'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

sources = read(t / 'source-candidate-r893.json')
assert all(sha(r / x['path']) == x['sha256'] for x in sources)
files, drivers = set(), set()
matrix, post, controls = {}, {}, {}
for platform in ('windows', 'mac', 'ubuntu'):
    phases = ('smoke', 'preview', 'core', 'native', 'render') if platform == 'windows' else ('smoke', 'preview', 'core', 'render')
    for phase in phases:
        stem = f'{platform}-batch-r893-{phase}-r893'
        a = ET.parse(t / (stem + '.xml')).getroot().find('assembly')
        expected = {'smoke': 14, 'preview': 4, 'core': 1432 if platform == 'windows' else 1414, 'native': 75, 'render': 292}[phase]
        assert int(a.get('total')) == expected and a.get('failed') == a.get('errors') == '0', (stem, a.attrib)
        if phase in ('smoke', 'preview'):
            assert int(a.get('passed')) == expected
            controls[f'{platform}/{phase}'] = [x.get('name') for x in a.findall('.//test')]
        matrix[f'{platform}/{phase}'] = {key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
        files.update((stem + '.xml', stem + '.log'))
    state = f'{platform}-batch-r893-state.txt'
    assert (t / state).read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=0'
    process_file = f'{platform}-batch-r893-post.json'
    post[platform] = read(t / process_file)['filecatOrDotnet']
    assert not post[platform], (platform, post[platform])
    files.update((state, process_file, f'{platform}-batch-r893-results.tgz', f'{platform}-batch-r893-observed.txt'))

failures = []
for platform, candidate in (('windows', 'r873'), ('mac', 'r877'), ('ubuntu', 'r877')):
    stem = f'{platform}-batch-{candidate}'
    assert (t / (stem + '-state.txt')).read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=1 phase=preview'
    a = ET.parse(t / f'{stem}-preview-{candidate}.xml').getroot().find('assembly')
    expected = (4, 0, 4, 0) if platform == 'windows' else (2, 2, 0, 1)
    assert tuple(int(a.get(k)) for k in ('total', 'passed', 'failed', 'errors')) == expected
    failures.append({'platform': platform, 'candidate': candidate, **{k: a.get(k) for k in ('total', 'passed', 'failed', 'errors')}})
    files.update(p.name for p in t.glob(stem + '-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))
for platform, candidate, phase, expected in (('mac', 'r887', 'preview', (4, 2, 2, 0)), ('ubuntu', 'r887', 'preview', (4, 2, 2, 0)), ('mac', 'r891', 'render', (2, 1, 1, 0))):
    a = ET.parse(t / f'{platform}-batch-{candidate}-{phase}-{candidate}.xml').getroot().find('assembly')
    assert tuple(int(a.get(k)) for k in ('total', 'passed', 'failed', 'errors')) == expected
    failures.append({'platform': platform, 'candidate': candidate, 'phase': phase, **{k: a.get(k) for k in ('total', 'passed', 'failed', 'errors')}})
for candidate in ('r870', 'r872', 'r876', 'r880', 'r882', 'r886', 'r890', 'r892'):
    name = f'build-{candidate}.txt'
    files.add(name)
    if candidate != 'r870':
        text = (t / name).read_text(encoding='utf-8-sig')
        assert all(x in text for x in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
for pattern in ('*-pre-r870.json', '*-pre-r873.json', '*-pre-r877.json', '*-pre-r883.json', '*-pre-r887.json', '*-pre-r891.json', '*-pre-r891check.json', '*-pre-r893.json'):
    files.update(p.name for p in t.glob(pattern))
files.update(('windows-space-r875.json', 'windows-cleanup-r875.json', 'focused-delta-proof-r891.json'))
for platform, candidate in (('mac', 'r887'), ('ubuntu', 'r887'), ('mac', 'r891')):
    stem = f'{platform}-batch-{candidate}'
    files.update(p.name for p in t.glob(stem + '-*') if p.is_file() and p.suffix in ('.txt', '.log', '.xml', '.json', '.tgz'))
cleanup = read(t / 'windows-cleanup-r875.json')
assert cleanup['available'] > 10 * 1024**3 and set(cleanup['keep']) == {'batch-r868', 'batch-r873'}
assert all(pathlib.PureWindowsPath(x['path']).parent.parent == pathlib.PureWindowsPath('C:/FileCatDsa20261104R2') and pathlib.PureWindowsPath(x['path']).parent.name.startswith('batch-r') and pathlib.PureWindowsPath(x['path']).name in ('payload', 'bundle.tgz') for x in cleanup['removed'])
files.update(f'source-candidate-{candidate}.json' for candidate in ('r871', 'r873', 'r877', 'r883', 'r887', 'r891', 'r893'))
files.update(f'pack-timing-{candidate}.json' for candidate in ('r873', 'r877', 'r883', 'r887', 'r893'))
drivers.update(('prepare-focused-test-delta.ps1', 'run-focused-test-delta.ps1', 'run-focused-test-delta.py', 'pack-batch-direct.ps1', 'add-windows-previews.ps1', 'add-unix-previews.ps1', 'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'cleanup-windows-completed-batches.ps1', 'finalize-owned-inspection.py'))
for candidate in ('r873', 'r877', 'r883', 'r887', 'r893'):
    for platform in ('windows', 'mac', 'linux'):
        name = f'batch-{platform}-{candidate}.{"ps1" if platform == "windows" else "sh"}'
        shutil.copy2(t / name, out / 'drivers' / name)
        drivers.add(name)
for platform in ('windows', 'mac', 'linux'):
    name = f'batch-{platform}-r891.{"ps1" if platform == "windows" else "sh"}'
    shutil.copy2(t / name, out / 'drivers' / name)
    drivers.add(name)
for name in sorted(files):
    shutil.copy2(t / name, out / name)
archives = []
for candidate in ('r873', 'r877', 'r883', 'r887', 'r893'):
    for platform in ('windows', 'mac', 'linux'):
        path = t / f'dsa-batch-{platform}-{candidate}.tgz'
        archives.append({'name': path.name, 'bytes': path.stat().st_size, 'sha256': sha(path)})
qualification = {'ownedInspectionMemoryAndIndexedSubsetPassedAllThree': True, 'corruptInspectionAndOpenHandledAllThree': True,
                 'nativeReaderLeaseSubsetPassedAllThree': True, 'allApplicableBroadSuitesPassed': True,
                 'fullNativeDesktopQualified': False, 'fullResourceGateQualified': False,
                 'managedReplacementDeletionHistoryQualified': False, 'generalExportImportQualified': False,
                 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record = {'purpose': 'Generated-ID owned-store inspection, bounded container verification, private size/location UI and native reader leases; preserving create-error and malformed-input handling',
          'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), 'testedWorkingTreeChanges': True,
          'sources': sources, 'artifacts': [{'path': (out / name).relative_to(r).as_posix(), 'sha256': sha(out / name)} for name in sorted(files)],
          'drivers': [{'path': (out / 'drivers' / name).relative_to(r).as_posix(), 'sha256': sha(out / 'drivers' / name)} for name in sorted(drivers)],
          'archives': archives, 'matrix': matrix, 'newCoreControls': 14, 'newRenderingCases': 4, 'preflightControls': controls,
          'supersededFailures': failures, 'postCollectionProcesses': post, 'qualification': qualification,
          'scope': 'Actual VM/Mac native captures/stores/reader locks and headless controls. Inspection verifies the container, not graph semantics or authority. Complete managed maintenance/history, physical desktop, full W0-W8 and final fresh independent review remain open.'}
(out / f'source-manifest-{revision}.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
summary = f'''`OwnedAnalysisStore` inspects a named inventory by generated catalog ID through an application-prepared private-directory capability. It rechecks profile/catalog identity and provenance, verifies the entire bounded record container and checksum in admitted I/O batches, and retains native input ownership through cancellation and actual close. It does not validate the complete captured graph or grant maintenance/source authority. Saved-analysis browsers in both view types show verified size, location, observation interval, coverage and privileged-data warning on explicit inspection. Native inventory/comparison reads hold shared reader locks until actual close, providing the reader side of subsequent managed maintenance.

14 Core cases and 4 native-backed headless UI cases plus applicable broad suites pass Windows, Mac and Ubuntu. Corrupt inspection and corrupt open retain the existing tab and file; redundant selection notifications do not cancel work, stale results are suppressed and cancellation/close wait for actual work. Low-space creation failures preserve their original cause unless a real concurrent winner can be read. [Evidence](source-manifest-{revision}.json) preserves the first Windows reserve failure and Mac/Ubuntu unhandled-`InvalidDataException` failures before the fixes. Cleanup restored about 18GB of Windows free space by removing only completed task-owned payloads/bundles beneath a verified root, keeping current/reusable candidates and result evidence. Future/unsupported private catalogs are refused before profile/control creation.

These controls qualify the inspection and reader-lease subset. Named replacement/deletion, retention/history, complete resource/physical desktop qualification, the remaining W0–W8 implementation and final independent UX/UI/functional/security review remain open; public and unqualified capability gates stay off.'''
for path in (out / 'README.md', r / 'docs/design/FILECAT_DISK_SPACE_EXPLORATION.md'):
    text = summary if path.parent == out else summary.replace(f'(source-manifest-{revision}.json)', f'(../validation/disk-space/source-manifest-{revision}.json)')
    previous = path.read_text(encoding='utf-8')
    if text not in previous:
        path.write_text(previous.rstrip() + '\n\n' + text + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
behavior = 'Generated-ID owned-store inspection verifies bounded saved containers and displays private size/location/dates; native readers retain shared leases through actual close. Malformed inspection/open and missing-winner creation failures are handled without modifying saved data. 14 Core and 4 native/UI cases plus applicable broad suites pass all three guest runtimes; complete managed maintenance and whole-plan qualification remain open.'
if behavior not in progress['completedBehaviors']:
    progress['completedBehaviors'].append(behavior)
progress['validation']['ownedStoreInspection'] = {'manifest': f'source-manifest-{revision}.json', 'matrix': matrix, 'newCoreControls': 14, 'newRenderingCases': 4, **qualification}
progress['validation']['testingWorkflow']['batchEvidence'] = f'source-manifest-{revision}.json'
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'manifest': revision, 'sources': len(sources), 'artifacts': len(files), 'matrix': matrix}, indent=2))
