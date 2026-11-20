"""Retain acquisition-completion, Settings and private-boundary evidence without closing W3/W8."""
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

sources = read(temp / 'source-candidate-r1021.json')
assert all(sha(root / source['path']) == source['sha256'] for source in sources)
matrix, files, drivers = {}, set(), set()
for platform in ('windows', 'mac', 'ubuntu'):
    for phase in ('smoke', 'preview', 'core', 'render', 'appsettings') + (('native',) if platform == 'windows' else ()):
        a = ET.parse(temp / f'{platform}-batch-r1021-{phase}-r1021.xml').getroot().find('assembly')
        assert a.get('failed') == a.get('errors') == '0'
        expected = {'smoke': (27, 0), 'preview': (26, 0), 'appsettings': (35, 1) if platform == 'windows' else (35, 0),
                    'core': (1577, 48) if platform == 'windows' else (1559, 4),
                    'render': (422, 1) if platform == 'windows' else (422, 0), 'native': (75, 0)}
        total, skipped = expected[phase]
        assert (int(a.get('total')), int(a.get('passed')), int(a.get('skipped'))) == (total, total - skipped, skipped)
        skipped_tests = [test for test in a.findall('.//test') if test.get('result') == 'Skip']
        if phase == 'appsettings' and platform == 'windows':
            assert len(skipped_tests) == 1 and skipped_tests[0].get('name').startswith('FileCat.App.Tests.SettingsPreservationTests.Protected_profiles_remain_inspectable_with_the_correct_reason_and_no_edit_actions(status: UnavailableReadOnly,')
            assert 'Unix non-regular settings input fixture' in ''.join(skipped_tests[0].itertext())
        elif phase in ('core', 'render'):
            baseline = 'windows-batch-r1009' if platform == 'windows' else f'{platform}-batch-r1006'
            old_revision = 'r1009' if platform == 'windows' else 'r1006'
            old = ET.parse(temp / f'{baseline}-{phase}-{old_revision}.xml').getroot().find('assembly')
            assert {test.get('name') for test in skipped_tests} == {test.get('name') for test in old.findall('.//test') if test.get('result') == 'Skip'}
        matrix[f'{platform}/{phase}'] = {key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
    stem = f'{platform}-batch-r1021'
    assert (temp / f'{stem}-state.txt').read_text(encoding='utf-8-sig').strip() == 'FINISHED exit=0'
    assert not read(temp / f'{stem}-post.json')['filecatOrDotnet']
    files.update(path.name for path in temp.glob(stem + '-*') if path.is_file() and path.suffix in ('.xml', '.log', '.txt', '.json', '.tgz'))
for platform in ('mac', 'ubuntu'):
    a = ET.parse(temp / f'{platform}-batch-r1015-preview-r1015.xml').getroot().find('assembly')
    assert (a.get('total'), a.get('passed'), a.get('failed')) == ('26', '22', '4')
    assert all('MainViewModel.ApplySettings' in ''.join(test.find('failure').itertext()) for test in a.findall('.//test') if test.get('result') == 'Fail')
    assert not read(temp / f'{platform}-batch-r1015-post.json')['filecatOrDotnet']
    files.update(path.name for path in temp.glob(f'{platform}-batch-r1015-*') if path.is_file() and path.suffix in ('.xml', '.log', '.txt', '.json', '.tgz'))
for name in ('build-r1011.txt', 'build-r1012.txt', 'build-r1013.txt', 'build-r1014.txt', 'build-r1018.txt', 'build-r1020.txt',
             'source-candidate-r1015.json', 'source-candidate-r1021.json', 'pack-timing-r1015.json', 'test-fix-proof-r1021.json', 'app-settings-inputs-r1015.json',
             'windows-cleanup-r1014-preview.json', 'windows-space-inventory-r1014.json', 'windows-root-list-r1017.txt',
             'windows-cleanup-legacy-r1014-error.txt', 'windows-cleanup-legacy-r1014-error2.txt', 'windows-quota-r1017.txt',
             'windows-legacy-archives-r1017-preview.json', 'windows-legacy-archives-r1017.json', 'windows-process-detail-r1024.json'):
    files.add(name)
for name in ('build-r1011.txt', 'build-r1012.txt', 'build-r1014.txt', 'build-r1018.txt', 'build-r1020.txt'):
    data = (temp / name).read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in data and all(value in data for value in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
assert read(temp / 'app-settings-inputs-r1015.json')['sharedDllsVerified'] == 78
proof = read(temp / 'test-fix-proof-r1021.json')
assert all(len(row['changed']) == 2 for row in proof['platforms'])
for pattern in ('*-pre-r1012.json', '*-pre-r1015.json', '*-pre-r1016.json', '*-pre-r1021.json', 'windows-pre-r102*.json'):
    files.update(path.name for path in temp.glob(pattern))
for revision in ('r1015', 'r1021'):
    for platform in ('windows', 'mac', 'linux'):
        name = f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}'
        shutil.copy2(temp / name, out / 'drivers' / name)
        drivers.add(name)
drivers.update(('finalize-history-runtime.py', 'add-app-settings-suite.ps1', 'add-windows-previews.ps1', 'add-unix-previews.ps1',
                'pack-batch-direct.ps1', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py', 'deploy-batch.ps1',
                'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'cleanup-windows-legacy-archives.ps1'))
for name in files:
    shutil.copy2(temp / name, out / name)
qualification = {'completionHooksWired': True, 'settingsPolicyReviewWired': True, 'historyPrivateBoundaryQualifiedAllThree': True,
                 'nativeCompletionAndSettingsCasesPassedAllThree': 12, 'settingsRegressionCasesPassedAllThree': 34,
                 'settingsRegressionCasesTotal': 35, 'windowsUnixNonRegularInputFixtureSkipped': True,
                 'broadSuitesPassedAllThree': True, 'historyBrowsingAndPinningImplemented': False,
                 'idleAgeRetentionAndRecoveryUiImplemented': False, 'fullHistoryFaultAndResourceMatrixQualified': False,
                 'fullPersistenceGateQualified': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record = {'purpose': 'Once-per-acquisition history capture, three private exclusions and separately reviewed finite Settings policy',
          'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), 'sources': sources,
          'matrix': matrix, 'qualification': qualification,
          'artifacts': [{'path': (out / name).relative_to(root).as_posix(), 'sha256': sha(out / name)} for name in sorted(files)],
          'drivers': [{'path': (out / 'drivers' / name).relative_to(root).as_posix(), 'sha256': sha(out / 'drivers' / name)} for name in sorted(drivers)],
          'scope': 'Actual native filesystem acquisition/completion plus guest headless UI and broad regression suites. This does not qualify physical desktop input, complete history browsing/age/recovery lifecycle, full fault/resource/scale behavior or the final independent review.'}
(out / 'source-manifest-r1025.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
(out / 'HISTORY_RUNTIME_QUALIFICATION.md').write_text('''# History completion and Settings

The application now registers one cached history task per live acquisition. It waits for actual scan/source cleanup, then captures a successful enabled memory or indexed revision under bounded serialized history work. Imported opens do not start this path. Stopped sessions and unconfirmed source closure are refused. Closing a completed acquisition waits for its history work and native cleanup; it does not abandon a published payload. Views display the outcome without treating a prunable history copy as a named saved analysis.

Both acquisition modes now prepare and verify native exclusions for named inventories, scratch and rolling history before capturing source roots. Ordinary analysis with history off creates no inventory. Settings shows finite count/age/byte limits, the private location, inclusion/privacy rules and pruning scope. OK opens a separate preserving review before changing policy. A policy changed during review is refused; disabling waits for preceding history work and keeps existing bytes. The normal Settings writer persists accepted changes.

Twelve new native filesystem/headless cases pass on Windows, Mac and Ubuntu: memory/indexed completion and count pruning, exact private-root exclusion, duplicate-capture prevention, ordinary memory-view status, off policy, stopped/source-close-failure refusal, accepted/canceled enable/disable through the real Settings save path, byte-cap precision/invalid values and changed-policy refusal. Mac/Ubuntu also pass all 35 existing Settings regressions; Windows passes 34 and skips the Unix non-regular settings-input fixture. All three pass the earlier 14 history/diagnostics renderer controls and 27 Core preflight controls. Broad suites pass: Mac/Ubuntu each 1,555 Core passes plus four platform skips and 422 rendering passes; Windows 1,529 Core passes plus 48 skips, 75 native passes and 421 rendering passes plus one Unix-only skip. Exact reports, skip identities and source/input hashes are in [r1025 provenance](source-manifest-r1025.json).

The initial four Settings cases lacked the main-window view adapter and failed during normal Settings application. The corrected fixture uses the real main window. Both failed guest reports are retained. Hash-verified Mac/Ubuntu reuse changed only the test DLL/PDB; each update transferred about 475 KB. Windows testing was postponed while foreign test processes were present. A guarded cleanup of 145 obsolete task-owned transfer archives recovered 4.0 GB while preserving reports and expanded data; disk quotas were checked and were disabled. Final task-process inventories are clear.

This qualifies actual guest filesystem work and headless UI/regressions. Physical input and desktop accessibility are not qualified here. History browsing/pinning, idle age retention, recovery/deletion UI, full fault/resource/scale qualification and the complete persistence gate remain open. The public analyzer command stays unregistered. The final independent UX/UI/functional/security review remains required after the full implementation loop.
''', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['historyRuntime'] = {'manifest': 'source-manifest-r1025.json', 'matrix': matrix, **qualification}
behavior = 'Acquisition completion now captures enabled successful live sessions once after confirmed source cleanup; memory/indexed paths exclude all three native private roots and display history outcomes. Stopped/source-close-failure sessions and off policy do not capture. Separate preserving Settings reviews gate finite policy changes; disabling keeps all history bytes. Twelve new native/headless cases, applicable existing Settings regressions (35 Mac/Ubuntu, 34 Windows plus one Unix-fixture skip) and broad suites pass all three platforms (r1025). History browsing/pinning, idle age retention, recovery and full qualification remain required.'
if behavior not in progress['completedBehaviors']:
    progress['completedBehaviors'].append(behavior)
progress['nextWork'] = ['W3: finish rolling-history browsing/pinning, separate deletion/recovery UI, retention while idle/on startup and full fault/resource/scale lifecycle; capture hooks, private exclusions and reviewed finite Settings pass all three r1025. Finish comparison replacement or its evidenced unavailable path, general import/export, multiple-active/indexed-scale comparison/persistence, power-loss boundaries and physical desktop/theme/DPI/a11y/package controls. The complete persistence gate remains unqualified.' if line.startswith('W3:') else line for line in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'manifest': 'r1025', 'sources': len(sources), 'artifacts': len(files), 'matrix': matrix, 'historyComplete': False}))
