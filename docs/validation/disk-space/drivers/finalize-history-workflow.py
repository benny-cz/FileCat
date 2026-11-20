"""Verify the native/headless history workflow cohort; full history/resource/desktop gates remain open."""
import datetime
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

sources = read(temp / 'source-candidate-r1041.json')
assert len(sources) == 22 and all(sha(root / source['path']) == source['sha256'] for source in sources)
baseline = read(temp / 'source-candidate-r1036.json')
current = {source['path']: source['sha256'] for source in sources}
assert len(baseline) == 21 and all(current[source['path']] == source['sha256'] for source in baseline)
fix = read(temp / 'test-fix-proof-r1041.json')
assert fix['baseline'] == 'r1036' and fix['revision'] == 'r1041'
assert {item['platform'] for item in fix['platforms']} == {'windows', 'mac', 'linux'}
for item in fix['platforms']:
    assert item['guestReuse'] and set(item['changed']) == {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'}
files, drivers, matrix = set(), set(), {}
for platform in ('windows', 'mac', 'ubuntu'):
    for phase in ('smoke', 'preview', 'core', 'render', 'appsettings') + (('native',) if platform == 'windows' else ()):
        revision = 'r1041' if platform == 'ubuntu' and phase == 'render' else 'r1036'
        a = ET.parse(temp / f'{platform}-batch-{revision}-{phase}-{revision}.xml').getroot().find('assembly')
        expected = {'smoke': (27, 0), 'preview': (38, 0), 'core': (1577, 48) if platform == 'windows' else (1559, 4),
                    'render': (434, 1) if platform == 'windows' else (434, 0),
                    'appsettings': (35, 1) if platform == 'windows' else (35, 0), 'native': (75, 0)}
        total, skipped = expected[phase]
        assert a.get('failed') == a.get('errors') == '0'
        assert (int(a.get('total')), int(a.get('passed')), int(a.get('skipped'))) == (total, total-skipped, skipped)
        if skipped:
            old = ET.parse(temp / f'{platform}-batch-r1021-{phase}-r1021.xml').getroot().find('assembly')
            names = lambda assembly: {test.get('name') for test in assembly.findall('.//test') if test.get('result') == 'Skip'}
            assert names(a) == names(old)
        matrix[f'{platform}/{phase}'] = {'revision': revision, **{key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}}
    for phase in (('smoke', 'preview') if platform == 'ubuntu' else ()):
        a = ET.parse(temp / f'{platform}-batch-r1041-{phase}-r1041.xml').getroot().find('assembly')
        assert all(a.get(key) == '0' for key in ('failed', 'skipped', 'errors'))
        assert a.get('total') == a.get('passed') == '1'
        matrix[f'{platform}/testFix/{phase}'] = {'revision': 'r1041', **{key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}}
    stem = f'{platform}-batch-r1036'
    expected_exit = 1 if platform == 'ubuntu' else 0
    assert (temp / f'{stem}-state.txt').read_text(encoding='utf-8-sig').strip() == f'FINISHED exit={expected_exit}'
    assert not read(temp / f'{stem}-post.json')['filecatOrDotnet']
    files.update(path.name for path in temp.glob(stem+'-*') if path.is_file() and path.suffix in ('.xml', '.log', '.txt', '.json', '.tgz'))
    if platform == 'ubuntu':
        stem = f'{platform}-batch-r1041'
        assert (temp / f'{stem}-state.txt').read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=0'
        assert not read(temp / f'{stem}-post.json')['filecatOrDotnet']
        files.update(path.name for path in temp.glob(stem+'-*') if path.is_file() and path.suffix in ('.xml', '.log', '.txt', '.json', '.tgz'))
pointer_failure = ET.parse(temp / 'ubuntu-batch-r1036-render-r1036.xml').getroot().find('assembly')
assert (pointer_failure.get('total'), pointer_failure.get('passed'), pointer_failure.get('failed')) == ('434', '433', '1')
assert [test.get('method') for test in pointer_failure.findall('.//test') if test.get('result') == 'Fail'] == ['IndexedSelectionTablePressKeepsItsCapturedMarkTargetUntilThePendingQueryCanPublish']
with tarfile.open(temp / 'ubuntu-pointer-r1038-results.tgz') as archive:
    control = json.load(archive.extractfile('report.json'))
assert control['verifiedInputs'] == 113 and len(control['trials']) == 3
assert all(trial['exit'] == 0 and trial['total'] == trial['passed'] == '1' and trial['failed'] == trial['errors'] == '0' for trial in control['trials'])
assert not read(temp / 'ubuntu-pre-r1038post.json')['filecatOrDotnet']
failed = ET.parse(temp / 'ubuntu-batch-r1034-preview-r1034.xml').getroot().find('assembly')
assert (failed.get('total'), failed.get('passed'), failed.get('failed')) == ('38', '36', '2')
assert not read(temp / 'ubuntu-batch-r1034-post.json')['filecatOrDotnet']
files.update(path.name for path in temp.glob('ubuntu-batch-r1034-*') if path.is_file() and path.suffix in ('.xml', '.log', '.txt', '.json', '.tgz'))
for name in ('build-r1026.txt', 'build-r1027.txt', 'build-r1028.txt', 'build-r1029.txt', 'build-r1030.txt', 'build-r1031.txt',
             'build-r1032.txt', 'build-r1033.txt', 'build-r1035.txt', 'source-candidate-r1034.json', 'source-candidate-r1036.json',
             'pack-timing-r1034.json', 'pack-timing-r1036.json', 'app-settings-inputs-r1034.json', 'app-settings-inputs-r1036.json',
             'build-r1040.txt', 'source-candidate-r1041.json', 'test-fix-proof-r1041.json',
             'ubuntu-pointer-r1038-results.tgz', 'ubuntu-pre-r1038post.json'):
    files.add(name)
for name in ('build-r1027.txt', 'build-r1028.txt', 'build-r1031.txt', 'build-r1032.txt', 'build-r1033.txt', 'build-r1035.txt', 'build-r1040.txt'):
    data = (temp/name).read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in data and all(value in data for value in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
for pattern in ('*-pre-r1027.json', '*-pre-r1031.json', '*-pre-r1034.json', '*-pre-r1036*.json', '*-pre-r1041*.json', '*-pre-r1043.json'):
    files.update(path.name for path in temp.glob(pattern))
for revision in ('r1034', 'r1036', 'r1041'):
    for platform in ('windows', 'mac', 'linux'):
        if revision == 'r1041' and platform != 'linux':
            continue  # Prepared deltas are not execution evidence; passing product baselines are reused.
        name = f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
        shutil.copy2(temp/name, out/'drivers'/name); drivers.add(name)
drivers.update(('finalize-history-workflow.py', 'prepare-history-workflow-batch.ps1', 'pack-batch-direct.ps1',
                'add-windows-previews.ps1', 'add-unix-previews.ps1', 'add-app-settings-suite.ps1', 'deploy-batch.ps1',
                'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py'))
for name in files:
    shutil.copy2(temp/name, out/name)
qualified = {'historyBrowserWired': True, 'indexedOfflineOpenAndActiveProtectionImplemented': True,
             'pinNamedCopyAndPreparedReplacementImplemented': True, 'guardedSavedRemovalAndPendingRecoveryImplemented': True,
             'exactGraphValidatedUnindexedRecoveryImplemented': True, 'newNativeHeadlessCasesPassedAllThree': 12,
             'broadSuitesPassedAllThree': True, 'idleAgeRetentionImplemented': False,
             'opaqueCorruptUnindexedRemovalImplemented': False, 'fullHistoryFaultAndResourceMatrixQualified': False,
             'fullPersistenceGateQualified': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record = {'purpose': 'Bounded history browser, protected offline views, named pin/replacement, guarded deletion and exact-byte graph-validated recovery',
          'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'gitBase': subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
          'sources': sources, 'matrix': matrix, 'qualification': qualified,
          'testOnlyDelta': fix, 'unchangedPointerControl': control,
          'artifacts': [{'path':(out/name).relative_to(root).as_posix(),'sha256':sha(out/name)} for name in sorted(files)],
          'drivers': [{'path':(out/'drivers'/name).relative_to(root).as_posix(),'sha256':sha(out/'drivers'/name)} for name in sorted(drivers)],
          'scope': 'Actual guest native filesystem workflows and headless UI/regressions. Does not qualify physical input/desktop/AT, every native fault and resource boundary, idle retention or opaque corrupt-file removal. This is not full W3/W8 or the final independent review.'}
(out/'source-manifest-r1042.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
(out/'HISTORY_WORKFLOW_QUALIFICATION.md').write_text('''# History browsing, pinning and recovery

Settings and both analyzer views now open a paged history browser. Pages contain at most 128 inert summaries. Operations reselect the current catalog identity and entry before acquiring native ownership. Historical opens fully validate the record graph and stay offline; their runtime protection remains held until actual view/data closure. Automatic pruning excludes those protected entries. A refused removal decision cannot be replayed after another view closes; the user must obtain a fresh review.

Pinning creates a complete independent named copy outside rolling retention. Existing named targets use the existing prepared Keep existing / Replace review, with Keep existing default. Confirmed replacements invalidate the old presentation reference. New copies use a prepared named-store owner, without preparing a new scan or increasing the 128-handle limit.

Saved-entry removal, pending restore and completion of pending removal use existing native identity/checksum guards. Their review names the history scope and preserves named/source files. Unindexed complete copies require explicit recovery after both checksum/framing inspection and full graph/accounting validation. The validated import records the SHA256 of its complete source container; recovery requires that exact attestation and rechecks the original native identity and bytes before catalog publication. It uses default display settings and a fresh catalog time, states that retention applies again, and removes no other data. Invalid graphs or changed bytes remain unindexed.

Twelve new actual guest filesystem/headless cases pass on Windows, Mac and Ubuntu: memory/indexed history opened while original sources are offline; protection through real view closure; named pin survival after retention; refusal and fresh review for active work; changed-byte recovery refusal; graph corruption with a valid container checksum; exact digest binding of graph validation; preserving removal defaults at480x620/font20; Keep/Replace named pin decisions and presentation invalidation; and pending restore/finish. The batch also passes 26 earlier history controls, 27 Core preflight cases, applicable Settings regressions and broad Core/native/rendering suites. Exact counts, unchanged platform skip identities and source/build/input hashes are in [r1042 provenance](source-manifest-r1042.json).

Ubuntu initially passed36/38 preflight cases. Indexed pinning encountered unavailable private preparation, and the corruption fixture selected the wrong wire-record kind. Pinning now uses an owned named-store save capability; the fixed fixture proves framing inspection accepts the forged container and full graph validation refuses it. The failed receipt is retained. Machine use was postponed whenever another tester was active, and final task-process inventories are clear.

The corrected product batch passed all history cases and broad regressions except one Ubuntu table-pointer assertion. Three focused repeats with 113 verified unchanged cached inputs passed. The test now verifies the actual hit target and pressed checkbox, renders pointer hover before pressing, and records geometry/press/hover details on failure. Only the rendering test DLL/PDB changed. The focused check and entire 434-case renderer suite pass on Ubuntu. Passing unchanged Core/native/Settings and Windows/Mac broad renderer results are retained rather than repeated; the prepared Windows/Mac test deltas were not run while those guests were busy. The original failure and all three controls remain in the evidence. This strengthens the test preconditions; it does not establish the root cause of the intermittent original failure or independently qualify native physical pointer behavior.

This is native filesystem and headless UI qualification. Physical desktop input, AT/theme/DPI/package coverage and the full history fault/resource/scale matrix remain open. Idle/startup age retention and guarded removal of opaque corrupt/unindexed copies remain required. The complete persistence gate and public analyzer registration remain open; the final independent UX/UI/functional/security review has not been performed.
''',encoding='utf-8')
progress = read(out/'progress.json')
progress['validation']['historyWorkflow'] = {'manifest':'source-manifest-r1042.json','matrix':matrix,**qualified}
behavior = 'A paged history browser is wired to Settings and both analyzer views. Imported historical views stay offline and retain runtime protection through actual view closure. Pin creates an independent named copy and uses prepared preserving replacement for existing names. Scoped fresh one-use removal/pending restore and checksum-plus-full-graph-validated unindexed recovery pass native/headless checks on all three (r1042); recovery binds the graph attestation to the exact source-container SHA256. Twelve new cases and broad regressions pass. Idle age retention, corrupt opaque-file removal and full fault/resource/desktop qualification remain required.'
if behavior not in progress['completedBehaviors']:
    progress['completedBehaviors'].append(behavior)
progress['nextWork'] = ['W3: finish idle/startup rolling retention, guarded removal of corrupt/unindexed opaque copies, the full fault/resource/scale and native desktop history lifecycle; normal browsing/pinning, active-view protection, scoped removal/pending restore and exact-graph unindexed recovery pass all three r1042. Investigate the intermittent pointer case further during native input qualification. Finish comparison replacement or evidenced unavailable behavior, general import/export, multiple-active/indexed-scale comparison/persistence and power-loss/theme/DPI/AT/package controls. The complete persistence gate remains unqualified.' if line.startswith('W3:') else line for line in progress['nextWork']]
(out/'progress.json').write_text(json.dumps(progress,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
print(json.dumps({'manifest':'r1042','sources':len(sources),'artifacts':len(files),'matrix':matrix,'historyComplete':False}))
