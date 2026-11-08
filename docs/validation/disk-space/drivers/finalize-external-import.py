"""Retain the bounded native external-import cohort after actual guest completion."""
import json
import pathlib
from native_batch_evidence import assembly, assert_clean_build, freeze_text, qualify, read, reports, retain, sha

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
manifest = 'source-manifest-r1072-external-import.json'
assert not (out / manifest).exists(), 'Use a fresh immutable qualification revision'
expected = {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (1, 0), 'preview': (22, 0),
                          'core': (1577, 48) if platform == 'windows' else (1559, 4),
                          'render': (504, 1) if platform == 'windows' else (504, 0),
                          'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows':
        expected[platform]['native'] = (75, 0)
prefixes = ('NativeExternalInventory', 'ExternalInventoryReview')
record, files, runners, original = qualify(root, 'r1071', expected, prefixes, 22,
    phase_revisions={f'windows/{phase}': 'r1072' for phase in expected['windows']},
    expected_states={'windows': 'FINISHED exit=1 phase=preview'}, source_snapshot='.tmp/source-text-r1071.tgz')
initial = assembly(original['windows'], 'preview', 'r1071')
assert (initial.get('total'), initial.get('passed'), initial.get('failed'), initial.get('errors')) == ('22', '21', '1', '0')
failure = [test for test in initial.findall('.//test') if test.get('result') == 'Fail']
assert len(failure) == 1 and failure[0].get('method') == 'NativeExternalInventoryInputEnforcesReadOnlyStableBindingsAndReleasesHandles'
assert 'directory' in failure[0].get('name')
assert 'UnauthorizedAccessException' in failure[0].findtext('./failure/message')
updated_sources = read(temp / 'source-candidate-r1072.json')
updated = {source['path']: source['sha256'] for source in updated_sources}
before = {source['path']: source['sha256'] for source in record['sources']}
assert set(updated) == set(before)
assert {path for path in before if before[path] != updated[path]} == {'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.ExternalInventory.cs'}
assert all(sha(root / source['path']) == source['sha256'] for source in updated_sources)
record['productBatchSources'] = record['sources']
record['sources'] = updated_sources
record['productRevision'] = record['revision']
record['revision'] = 'r1072'
record['initialWindowsFixtureFailure'] = {'name': failure[0].get('name'), 'message': failure[0].findtext('./failure/message')}
proof = read(temp / 'test-fix-proof-r1072.json')
assert proof['scope'] == 'TestOnly' and proof['baseline'] == 'r1071'
assert_clean_build(temp / 'external-import-build-r1072.txt', 1)
record['helperInputOverlays'] = {}
for platform in ('windows', 'mac', 'ubuntu'):
    tag = 'linux' if platform == 'ubuntu' else platform
    row = next(item for item in proof['platforms'] if item['platform'] == tag)
    assert row['guestReuse'] and set(row['changed']) == {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'}
    assert sha(temp / f'dsa-batch-{tag}-r1072.tgz') == row['sha256']
    delta = temp / ('test-fix-delta-r1072' if tag == 'windows' else f'test-fix-delta-{tag}-r1072')
    plan = read(delta / 'plan.json')
    old = {item['path']: item['sha256'] for item in record['inputs'][platform]}
    assert {item['path'] for item in plan['files']} == set(old)
    assert all(item['baselineSha256'] == old[item['path']] for item in plan['files'])
    assert all(sha(temp / f'batch-r1072-{tag}' / item['path']) == item['sha256'] for item in plan['files'])
    assert all(item['path'] in row['changed'] or item['sha256'] == item['baselineSha256'] for item in plan['files'])
    result = reports(temp / f'{platform}-batch-r1072-results.tgz')
    assert result['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=0'
    post = read(temp / f'{platform}-batch-r1072-post.json')
    assert not post['filecatOrDotnet']
    record['postRunProcesses'][f'{platform}/r1072'] = post
    for phase, count in (('smoke', 1), ('preview', 22)):
        a = assembly(result, phase, 'r1072')
        assert a.get('total') == a.get('passed') == str(count) and a.get('failed') == a.get('errors') == a.get('skipped') == '0'
        record['matrix'][f'{platform}/helper/{phase}'] = {'revision': 'r1072', **{key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}}
    record['helperInputOverlays'][platform] = plan
    files.add(temp / f'{platform}-batch-r1072-results.tgz')
    runners.add(temp / f'batch-{tag}-r1072.{"ps1" if tag == "windows" else "sh"}')
files.update({temp / 'source-candidate-r1072.json', temp / 'test-fix-proof-r1072.json', temp / 'external-import-build-r1072.txt'})
record['testOnlyHelperOverlay'] = proof
flags = {'externalReadOnlyImportWorkflowQualified': True, 'newNativeAndReviewCasesPassedAllThree': 22,
         'broadSuitesPassedAllThree': True, 'nativePickerAndFullImportResourceMatrixQualified': False,
         'generalExportsImplemented': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record.update({'purpose': 'Explicit read-only native external inventory import into bounded private scratch', 'qualification': flags,
               'scope': 'Actual native input and private scratch, full graph/accounting validation, inert tab adoption and fault ownership. Physical picker, AT/DPI/package, maximum-size and all power-loss qualification remain open.'})
retain(root, record, files, runners, ['finalize-external-import.py', 'native_batch_evidence.py', 'prepare-history-workflow-batch.ps1',
       'prepare-test-fix-batch.ps1', 'run-batch-cache.py', 'add-app-settings-suite.ps1', 'add-unix-previews.ps1',
       'add-windows-previews.ps1', 'pack-batch-direct.ps1', 'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'])
freeze_text(root, record, 'exact-text-r1072-external-import.tgz')
(out / manifest).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['externalInventoryImport'] = {'manifest': manifest, 'matrix': record['matrix'], **flags}
progress['completedBehaviors'].append('Explicit external .fcdsa imports now use an ordinary-process, native read-only input owner separate from private storage. Public source ACL/mode is accepted without weakening private scratch; source and ancestor links, unstable bindings, conflicting writers and handle exhaustion are refused. Full bounded graph/checksum/accounting validation precedes inert unsaved tab adoption. Twenty-two native/review checks and applicable broad regressions pass all three platforms (r1072); helper-only correction reused exact product binaries. Physical picker and complete import resource qualification remain open; general exports and final independent review remain unfinished.')
progress['nextWork'] = ['W3: implement general exports and comparison replacement or evidenced unavailable behavior; finish physical external picker and grouped full import/history resource/scale/desktop qualification. External read-only import now has scoped native evidence on all three (r1072). Full persistence gate remains unqualified.' if line.startswith('W3:') else line for line in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'EXTERNAL_IMPORT_QUALIFICATION.md').write_text('''# External inventory import

An explicitly chosen .fcdsa file now opens through a native read-only input owner, separately from private storage. Public read permissions on the chosen input do not relax owner-private scratch, catalog or quota requirements. Native handles retain and validate the chosen ancestry without following links. Windows holds the source against writes/deletion; Unix uses a shared reader lease and checks native identity, size and change time around reads. The input is bounded to 8 GiB and reads are bounded. All handles use the shared native-handle admission budget.

The existing staged decoder, full graph and accounting validator are reused. Invalid, truncated, future, authority-bearing, checksum-mismatched and graph/accounting-poisoned files cannot publish a tab. Decoded locators remain offline observations with ImportedUnverified taint; they do not create connections or native source authority. A successful external import opens an unsaved indexed tab, without claiming a named save or making an automatic history capture. Native input closure must succeed before the staged result is accepted.

Each platform passes 22 new checks covering actual publicly readable inputs from memory/indexed inventories, reversible native names, inert tab adoption, source preservation, malformed containers, parent-link refusal, source mutation/replacement or Windows sharing refusal, competing writers, exhausted handle admission, cancellation with actual ownership, input verify/close faults and a narrow large-text Cancel-default review. Windows initially passed 21/22: its correct UnauthorizedAccessException for a directory differed from the fixture's IOException assertion. Only that assertion changed; every product binary remained identical. All 22 final checks pass on all three. The raw original failure and exact source/input hashes are retained in [r1072 provenance](source-manifest-r1072-external-import.json).

Broad results: Windows Core 1529 passed plus 48 existing skips, native filesystem 75 passed, rendering 503 passed plus one existing Unix skip, Settings 34 passed plus one existing Unix skip. Mac and Ubuntu each pass Core 1555 plus four existing skips, rendering 504, and Settings 35. Skip identities match the retained reference. Host activity was compilation and receipt verification; suites ran only on the Windows/Ubuntu VMs and Mac. Each original process handle reached terminal status, each guest's reports were collected once per cohort, and every final task-process inventory is clear.

This is scoped native/headless evidence. Physical file-picker input, complete screen-reader/theme/DPI/package behavior, maximum-size/scale/resource and all process-death/power-loss boundaries remain open. General external publication and report/chart exports remain unfinished. Public feature registration remains off, and the final independent UX/UI/functional/security review has not been performed.
''', encoding='utf-8')
print(json.dumps({'manifest': manifest, 'newCasesPerPlatform': 22, 'renderPassed': {platform: record['matrix'][platform + '/render']['passed'] for platform in expected}, 'fullPlanComplete': False}))
