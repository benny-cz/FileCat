"""Record completed r868 evidence only after every actual runner and collector finishes."""
import datetime, hashlib, json, pathlib, shutil, subprocess, xml.etree.ElementTree as ET

r = pathlib.Path.cwd()
t = r / '.tmp'
out = r / 'docs/validation/disk-space'
revision = 'r869'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

sources = read(t / 'source-candidate-r868.json')
assert all(sha(r / item['path']) == item['sha256'] for item in sources)
matrix, controls, post = {}, {}, {}
files = set()
for platform in ('windows', 'mac', 'ubuntu'):
    phases = ('smoke', 'preview', 'core', 'native', 'render') if platform == 'windows' else ('smoke', 'preview', 'core', 'render')
    for phase in phases:
        stem = f'{platform}-batch-r868-{phase}-r868'
        a = ET.parse(t / (stem + '.xml')).getroot().find('assembly')
        expected = {'smoke': 18, 'preview': 7, 'core': 1418 if platform == 'windows' else 1400, 'native': 75, 'render': 288}[phase]
        assert int(a.get('total')) == expected and a.get('failed') == a.get('errors') == '0', (stem, a.attrib)
        if phase in ('smoke', 'preview'):
            assert int(a.get('passed')) == expected
            controls[f'{platform}/{phase}'] = [x.get('name') for x in a.findall('.//test')]
        matrix[f'{platform}/{phase}'] = {key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
        files.update((stem + '.xml', stem + '.log'))
    state = f'{platform}-batch-r868-state.txt'
    assert (t / state).read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=0'
    process_file = f'{platform}-batch-r868-post.json'
    post[platform] = read(t / process_file)['filecatOrDotnet']
    assert not post[platform], (platform, post[platform])
    files.update((state, process_file, f'{platform}-batch-r868-results.tgz', f'{platform}-batch-r868-observed.txt'))

failures = []
for platform, candidate, passed, failed in (('windows', 'r856', 0, 6), ('mac', 'r860', 5, 1), ('windows', 'r865', 3, 4)):
    stem = f'{platform}-batch-{candidate}'
    assert (t / (stem + '-state.txt')).read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=1 phase=preview'
    a = ET.parse(t / f'{stem}-preview-{candidate}.xml').getroot().find('assembly')
    assert int(a.get('passed')) == passed and int(a.get('failed')) == failed
    failures.append({'platform': platform, 'candidate': candidate, 'passed': passed, 'failed': failed})
    for path in t.glob(stem + '-*'):
        if path.is_file() and path.suffix in ('.txt', '.xml', '.log', '.tgz', '.json'):
            files.add(path.name)

for candidate in ('r854', 'r855', 'r858', 'r859', 'r862', 'r864', 'r867'):
    files.add(f'build-{candidate}.txt')
for candidate in ('r855', 'r859', 'r862', 'r864', 'r867'):
    log = (t / f'build-{candidate}.txt').read_text(encoding='utf-8-sig')
    assert all(value in log for value in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
for pattern in ('*-pre-r854.json', '*-pre-r856.json', '*-pre-r858.json', '*-pre-r860.json', '*-pre-r861.json', '*-pre-r865.json', '*-pre-r868.json'):
    files.update(path.name for path in t.glob(pattern))
files.update(f'source-candidate-{candidate}.json' for candidate in ('r856', 'r860', 'r863', 'r865', 'r868'))
files.update(('pack-timing-r856.json', 'pack-timing-r865.json', 'test-fix-proof-r860.json', 'test-fix-proof-r868.json', 'windows-batch-r868-cache-proof.txt'))
cache = (t / 'windows-batch-r868-cache-proof.txt').read_text(encoding='utf-8-sig').strip()
assert cache == 'VERIFIED cachedFiles=226 changedFiles=2'
drivers = ['pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1', 'add-windows-previews.ps1', 'add-unix-previews.ps1', 'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'finalize-root-query.py']
for platform in ('windows', 'mac', 'linux'):
    extension = 'ps1' if platform == 'windows' else 'sh'
    for candidate in ('r856', 'r860', 'r865', 'r868'):
        name = f'batch-{platform}-{candidate}.{extension}'
        shutil.copy2(t / name, out / 'drivers' / name)
        drivers.append(name)
for name in sorted(files):
    shutil.copy2(t / name, out / name)
production = {}
for name in ('FileCat.Core.dll', 'FileCat.dll', 'FileCat.Platform.Windows.dll'):
    baseline = t / 'batch-r865-windows/render' / name
    current = r / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0' / name
    assert sha(baseline) == sha(current)
    production[name] = sha(current)
archives = []
for candidate in ('r856', 'r860', 'r865', 'r868'):
    for platform in ('windows', 'mac', 'linux'):
        path = t / f'dsa-batch-{platform}-{candidate}.tgz'
        archives.append({'name': path.name, 'bytes': path.stat().st_size, 'sha256': sha(path)})
qualification = {'rootQueryPrivateMemoryAndIndexedSubsetPassedAllThree': True, 'unavailableStorageHandledWithoutAsyncUiEscapeAllThree': True,
                 'allApplicableBroadSuitesPassed': True, 'fullNativeDesktopQualified': False, 'fullResourceGateQualified': False,
                 'managedReplacementDeletionHistoryQualified': False, 'generalExportImportQualified': False,
                 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record = {'purpose': 'Ordinary private root-ID query restoration across memory/indexed views and repeated saves; visible identifier consistency; handled unavailable indexed storage',
          'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), 'testedWorkingTreeChanges': True,
          'sources': sources, 'artifacts': [{'path': (out / name).relative_to(r).as_posix(), 'sha256': sha(out / name)} for name in sorted(files)],
          'drivers': [{'path': (out / 'drivers' / name).relative_to(r).as_posix(), 'sha256': sha(out / 'drivers' / name)} for name in sorted(set(drivers))],
          'archives': archives, 'matrix': matrix, 'newCoreControls': 18, 'newRenderingCases': 7, 'preflightControls': controls,
          'supersededFailures': failures, 'verifiedCacheProof': cache, 'unchangedProductionHashesForTestFix': production,
          'postCollectionProcesses': post, 'qualification': qualification,
          'scope': 'Actual VM/Mac native captures and private stores plus headless UI workflows. Physical desktop, scale, complete W0-W8 and the final fresh independent review remain open.'}
(out / f'source-manifest-{revision}.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
summary = f'''Ordinary named analyses preserve root-ID predicates across inventory renumbering, memory/indexed restoration, grouping/reapply and a second save. Private catalog schema5 adds bounded immutable display identifiers; schemas1–4 remain readable and the portable inventory format is unchanged. Location selectors display the same IDs recognized by queries. The entry limit accounts for worst-case escaping of the existing 8KiB query and32 root IDs; the total2MiB catalog and shared memory reservations remain bounded. Unsupported private storage during indexed save now produces a handled failure and preserves the existing path.

18 new Core cases and7 native-backed headless UI cases pass on Windows, Mac and Ubuntu, with the applicable broad suites. [Evidence](source-manifest-{revision}.json) retains failed preflights and their fixes: an incorrect numeric-root fixture assumption, an unhandled unavailable-store exception exposed under resource pressure, and a single-root selector lookup in a fixture where that selector is intentionally hidden. The final fixture uses two actual captured roots and closes the prior indexed window before reopening to stay within the shared native-handle budget.

Reusable previews require exact passing counts before broad suites on each platform. The final test-only correction reuses226 verified Windows inputs with a388992-byte transfer, preserves the old payload and overlays only the test DLL/PDB. Actual result collection uses one archive/completed machine. Other testers' runs were postponed and left untouched. Full W0–W8 implementation, physical desktop/resource qualification and the final fresh independent UX/UI/functional/security review remain open.'''
for path in (out / 'README.md', r / 'docs/design/FILECAT_DISK_SPACE_EXPLORATION.md'):
    text = summary if path.parent == out else summary.replace(f'(source-manifest-{revision}.json)', f'(../validation/disk-space/source-manifest-{revision}.json)')
    previous = path.read_text(encoding='utf-8')
    if text not in previous:
        path.write_text(previous.rstrip() + '\n\n' + text + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
behavior = 'Private ordinary root-ID queries and visible identifiers survive memory/indexed restore, reapply/grouping and second saves; unsupported indexed private storage produces a handled preserving failure.18 new Core and7 native-backed UI cases plus applicable broad suites pass all three guest runtimes; whole-plan qualification remains open.'
if behavior not in progress['completedBehaviors']:
    progress['completedBehaviors'].append(behavior)
progress['validation']['ordinaryRootQueryPersistence'] = {'manifest': f'source-manifest-{revision}.json', 'matrix': matrix, 'newCoreControls': 18, 'newRenderingCases': 7, **qualification}
progress['validation']['testingWorkflow']['batchEvidence'] = f'source-manifest-{revision}.json'
for index, item in enumerate(progress['nextWork']):
    if item.startswith('W3:'):
        progress['nextWork'][index] = 'W3: complete managed catalog maintenance/replacement/deletion, optional capped history, general import/export and indexed-scale comparison/persistence, plus full native desktop qualification. Private saved ordinary and comparison root-ID query context is covered on all3 guest runtimes. The persistence gate and all remaining plan requirements remain unqualified.'
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'manifest': revision, 'sources': len(sources), 'artifacts': len(files), 'matrix': matrix}, indent=2))
