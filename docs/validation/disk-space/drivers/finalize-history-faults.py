"""Record the deletion/retention failure-boundary cohort after actual three-platform completion."""
import json
import pathlib
from collections import Counter
from native_batch_evidence import assembly, assert_clean_build, qualify, read, reports, retain, sha, freeze_text

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
revision = 'r1063'
manifest = 'source-manifest-r1068-faults.json'
assert not (out / manifest).exists(), 'Use a fresh immutable qualification revision'
expected = {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (27, 0), 'preview': (96, 0),
                          'core': (1577, 48) if platform == 'windows' else (1559, 4),
                          'render': (482, 1) if platform == 'windows' else (482, 0),
                          'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows':
        expected[platform]['native'] = (75, 0)
prefixes = ('NativeHistoryAndNamedRootCloseFault', 'NativeRollingFailureBoundaries', 'NativeOpaqueHistoryFailureBoundary')
record, files, runners, original_receipts = qualify(root, revision, expected, prefixes, 26,
    phase_revisions={'windows/render': 'r1068'}, expected_states={'windows': 'FINISHED exit=1'})
initial = assembly(original_receipts['windows'], 'render', 'r1063')
assert (initial.get('total'), initial.get('passed'), initial.get('failed'), initial.get('skipped')) == ('482', '480', '1', '1')
initial_failure = [test for test in initial.findall('.//test') if test.get('result') == 'Fail']
assert len(initial_failure) == 1 and initial_failure[0].get('method') == 'AbruptOwnedInventoryDeathPreservesNamedBytesAndAllowsFreshExplicitRecovery'
assert 'marker-removed' in initial_failure[0].get('name') and 'FileNotFoundException' in initial_failure[0].findtext('./failure/message')
updated_sources = read(temp / 'source-candidate-r1068.json')
updated = {source['path']: source['sha256'] for source in updated_sources}
assert all(updated[source['path']] == source['sha256'] for source in record['sources'])
assert set(updated) - {source['path'] for source in record['sources']} == {'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.InventoryDeath.cs'}
assert all(sha(root / source['path']) == source['sha256'] for source in updated_sources)
record['productBatchSources'] = record['sources']
record['sources'] = updated_sources
record['productRevision'] = record['revision']
record['revision'] = 'r1068'
record['helperInputOverlays'] = {}
helper_proof = read(temp / 'test-fix-proof-r1068.json')
assert helper_proof['scope'] == 'TestOnly' and helper_proof['baseline'] == 'r1063'
for platform in ('windows', 'mac', 'ubuntu'):
    tag = 'linux' if platform == 'ubuntu' else platform
    row = next(row for row in helper_proof['platforms'] if row['platform'] == tag)
    assert row['guestReuse'] and set(row['changed']) == {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'}
    assert sha(temp / f'dsa-batch-{tag}-r1068.tgz') == row['sha256']
    delta = temp / ('test-fix-delta-r1068' if tag == 'windows' else f'test-fix-delta-{tag}-r1068')
    plan = read(delta / 'plan.json')
    old_hashes = {item['path']: item['sha256'] for item in record['inputs'][platform]}
    assert {item['path'] for item in plan['files']} == set(old_hashes)
    assert all(item['baselineSha256'] == old_hashes[item['path']] for item in plan['files'])
    assert all(sha(temp / f'batch-r1068-{tag}' / item['path']) == item['sha256'] for item in plan['files'])
    assert all(item['path'] in row['changed'] or item['baselineSha256'] == item['sha256'] for item in plan['files'])
    record['helperInputOverlays'][platform] = plan
    result = reports(temp / f'{platform}-batch-r1068-results.tgz')
    assert result['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=0'
    post = read(temp / f'{platform}-batch-r1068-post.json')
    assert not post['filecatOrDotnet']
    record['postRunProcesses'][f'{platform}/r1068'] = post
    for phase, count in (('smoke', 1), ('preview', 25)):
        a = assembly(result, phase, 'r1068')
        assert a.get('total') == a.get('passed') == str(count) and a.get('failed') == a.get('errors') == a.get('skipped') == '0'
        record['matrix'][f'{platform}/helper/{phase}'] = {'revision': 'r1068', **{key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}}
    files.add(temp / f'{platform}-batch-r1068-results.tgz')
    runners.add(temp / f'batch-{tag}-r1068.{"ps1" if tag == "windows" else "sh"}')
assert_clean_build(temp / 'build-r1067.txt', 1)
files.update({temp / 'build-r1067.txt', temp / 'source-candidate-r1068.json', temp / 'test-fix-proof-r1068.json'})
record['testOnlyHelperOverlay'] = helper_proof
record['initialWindowsRegressionFailure'] = {'name': initial_failure[0].get('name'), 'message': initial_failure[0].findtext('./failure/message')}
control_path = temp / 'mac-batch-r1061-results.tgz'
control = reports(control_path)
a = assembly(control, 'preview', 'r1061')
assert (a.get('total'), a.get('passed'), a.get('failed'), a.get('errors')) == ('26', '17', '9', '0')
assert control['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=preview'
assert not read(temp / 'mac-batch-r1061-post.json')['filecatOrDotnet']
failures = [test for test in a.findall('.//test') if test.get('result') == 'Fail']
assert Counter(test.get('method') for test in failures) == {
    'NativeHistoryAndNamedRootCloseFaultPreservesConfirmedFactsAndNeverReportsCleanSuccess': 6,
    'NativeRollingFailureBoundariesPreserveOtherCopiesAndRequireFreshRecovery': 2,
    'NativeOpaqueHistoryFailureBoundaryDistinguishesKnownKeptBytesFromUncertainUnlink': 1}
assert sorted(test.get('name') for test in a.findall('.//test')) == record['matrix']['mac/preview']['newCases']
control_sources = read(temp / 'source-candidate-r1061.json')
current = {source['path']: source['sha256'] for source in record['sources']}
assert all(current[source['path']] == source['sha256'] for source in control_sources)
proof = read(temp / 'app-ui-delta-proof-r1061.json')
assert proof['scope'] == 'OldProductControl' and proof['baseline'] == proof['productSourceBaseline'] == 'r1058'
assert all(row['guestReuse'] and set(row['changed']) == {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'} for row in proof['platforms'])
assert_clean_build(temp / 'build-r1059.txt', 1)
files.update({control_path, temp / 'source-candidate-r1061.json', temp / 'app-ui-delta-proof-r1061.json', temp / 'build-r1059.txt'})
runners.add(temp / 'batch-mac-r1061.sh')
flags = {'scopedDeletionRetentionFaultsQualified': True, 'newNativeFaultCasesPassedAllThree': 26,
         'broadSuitesPassedAllThree': True, 'fullHistoryFaultResourceDesktopMatrixQualified': False,
         'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record.update({'purpose': 'Preserve deletion and rolling outcomes through close/unlink/cancellation failures', 'qualification': flags,
               'negativeControl': {'platform': 'mac', 'proof': proof, 'failed': [{'name': test.get('name'), 'message': test.findtext('./failure/message')} for test in failures]},
               'scope': 'Fault hooks wrap actual native private-directory operations. Does not prove every kernel close failure, power loss, resource/scale boundary, or physical desktop behavior.'})
record['negativeControl']['inputs'] = read(temp / 'test-fix-delta-mac-r1061/plan.json')
retain(root, record, files, runners, ['finalize-history-faults.py', 'native_batch_evidence.py', 'prepare-history-workflow-batch.ps1',
       'prepare-cached-app-ui-batch.ps1', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py', 'add-app-settings-suite.ps1',
       'add-unix-previews.ps1', 'add-windows-previews.ps1', 'pack-batch-direct.ps1', 'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'])
freeze_text(root, record, 'exact-text-r1068-faults.tgz')
(out / manifest).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['historyFailureBoundaries'] = {'manifest': manifest, 'matrix': record['matrix'], **flags}
progress['completedBehaviors'].append('Deletion now observes a completed concurrent store-close failure and preserves known removed/restored facts in an Uncertain outcome. Automatic capture/idle retention propagate an uncertain nested unlink instead of downgrading it, and opaque removal distinguishes a known pre-unlink refusal from uncertain deletion. An unchanged-product native control reproduced nine failures; all 26 fault cases and broad regressions pass Windows, Mac and Ubuntu (r1068), with exact fault-test sources unchanged. The process-death harness now observes exact native marker absence at its requested-removal boundary; 25 focused helper cases pass all three with unchanged product inputs. Full history fault/resource/physical-desktop qualification remains open.')
progress['nextWork'] = ['W3: finish general import/export and comparison replacement or evidenced unavailable behavior, then group the remaining full history fault/resource/scale and physical desktop lifecycle qualification. Concurrent root-close deletion outcomes and nested uncertainty propagation now pass scoped native fault checks on all three (r1068). Multiple-active/indexed-scale comparison/persistence and power-loss/theme/DPI/AT/package gates remain open; the complete persistence gate is unqualified.' if line.startswith('W3:') else line for line in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'HISTORY_FAILURE_BOUNDARIES_QUALIFICATION.md').write_text('''# Deletion and retention failure boundaries

Deletion now observes a completed concurrent root-close failure before returning its outcome. It preserves confirmed payload removal and restoration facts while reporting Uncertain cleanup. It observes completed closure only; waiting for owner disposal inside a nested rolling transaction would deadlock. Named deletion/restoration, pending cleanup and indexed/unindexed opaque history removal share this completion guard.

Automatic capture and idle retention preserve an uncertain nested deletion outcome. A post-unlink durability failure remains Uncertain, including when it is wrapped in another operation or aggregate exception. An opaque removal refused before its native removal request now reports Kept; an actual removal with unconfirmed durability remains Uncertain. Every retry still requires fresh inspection and intent.

The unchanged-product control used the complete verified r1058 guest inputs with only the test DLL/PDB replaced. It reproduced nine failures: six final-close outcome failures, two downgraded rolling unlink outcomes and one known-refusal classification error. After the fixes, all 26 native fault cases pass on Windows, Mac and Ubuntu with unchanged test source. Hooks surround actual private native operations: named and rolling concurrent close; before/after metadata movement and payload unlink; rollback refusal; absent-payload metadata cleanup; cancellation; replay; and fresh pending restore/completion. Neighbor bytes, complete incoming capture and confirmed removal facts are checked.

Each product batch passes 27 Core and 96 workflow/fault preflight cases. The final matrix passes the applicable broad Core/native/rendering and Settings regressions. Rendering totals 482 cases on each platform, with the existing Unix-only case skipped on Windows. Exact counts, unchanged skip identities, source/input hashes and the original failures are in [r1068 provenance](source-manifest-r1068-faults.json). Raw reports are retained once in each result archive. All final task-process inventories are clear.

Windows initially had one existing process-death failure after the child reported marker removal: enumeration observed a marker which was absent when opened. The child signals after its removal request and before its handle closes. The harness now observes the exact generated marker's absence through the same native backend before opening the fresh catalog, with a two-second bound and no catalog repair. The retained failure identifies the race observed; it does not establish every kernel-level cause. Microsoft's [file disposition contract](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_disposition_information_ex) distinguishes requesting POSIX removal from closure of its delete handle. The harness also continues to [await the exact terminated process](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-terminateprocess). All 25 process-death helper cases pass on each guest. Only the test DLL/PDB changed; Windows reruns its affected broad renderer suite, while the passing unchanged product results are retained.

These hooks qualify error propagation over actual native files, not every possible kernel close failure or power loss. Full history fault/resource/scale and physical desktop startup/input/theme/DPI/AT/package gates remain open. Public registration stays off. The final independent UX/UI/functional/security review has not been performed.
''', encoding='utf-8')
print(json.dumps({'manifest': manifest, 'newFaultCasesPerPlatform': 26, 'baselineFailuresReproduced': 9,
                  'renderPassed': {platform: record['matrix'][platform + '/render']['passed'] for platform in ('windows', 'mac', 'ubuntu')},
                  'historyFullyQualified': False}))
