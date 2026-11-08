"""Freeze the native indexed report capture/review cohort; broader report work remains open."""
import json
import pathlib
from native_batch_evidence import assembly, assert_clean_build, freeze_text, qualify, read, reports, retain, sha

root = pathlib.Path.cwd()
out = root / 'docs/validation/disk-space'
revision = 'r1084'
manifest = f'source-manifest-{revision}-captured-reports.json'
assert not (out / manifest).exists(), 'Use a fresh immutable qualification revision'
expected = {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (24, 0), 'preview': (20, 0),
        'core': (1601, 48) if platform == 'windows' else (1583, 4),
        'render': (556, 1) if platform == 'windows' else (556, 0),
        'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows': expected[platform]['native'] = (75, 0)
prefixes = ('NativeCapturedDatasetCsv', 'NativeCapturedReport', 'CapturedReportReview', 'CapturedReportFilename')
phases = {f'{platform}/{phase}': revision for platform in expected for phase in expected[platform]}
foreign = root / '.tmp/ubuntu-batch-r1084-post-foreign.json'
inventory = read(foreign)
assert inventory['filecatOrDotnet']
assert all(item.split(None, 2)[2].split(None, 1)[0].startswith('/home/benny/FileCatValidation/archive-writing-42829297/') for item in inventory['filecatOrDotnet'])
record, files, runners, receipts = qualify(root, 'r1083', expected, prefixes, 20, phase_revisions=phases,
    expected_states={platform: 'FINISHED exit=1 phase=preview' for platform in expected}, source_snapshot='.tmp/source-text-r1083.tgz',
    foreign_post_processes={'ubuntu/r1084': inventory['filecatOrDotnet']})
record['productRevision'] = record['revision']; record['revision'] = revision
updated = read(root / '.tmp/source-candidate-r1084.json')
before = {item['path']: item['sha256'] for item in record['sources']}
after = {item['path']: item['sha256'] for item in updated}
assert set(before) == set(after)
assert {name for name in before if before[name] != after[name]} == {'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.CapturedReports.cs'}
assert all(sha(root / item['path']) == item['sha256'] for item in updated)
record['productBatchSources'] = record['sources']; record['sources'] = updated
proof = read(root / '.tmp/test-fix-proof-r1084.json')
assert proof['baseline'] == 'r1083' and proof['scope'] == 'TestOnly' and proof['testPhases'] == ['render']
assert_clean_build(root / '.tmp/captured-report-helper-build-r1084.txt', 1)
record['helperInputOverlays'] = {}; record['initialFixtureFailures'] = {}; record['initialBusyRefusals'] = {}
for platform in expected:
    tag = 'linux' if platform == 'ubuntu' else platform
    original = assembly(receipts[platform], 'preview', 'r1083')
    assert (original.get('total'), original.get('passed'), original.get('failed'), original.get('errors')) == ('20', '17', '3', '0')
    failures = [test for test in original.findall('.//test') if test.get('result') == 'Fail']
    assert sorted(test.get('method') for test in failures) == ['CapturedReportFilenameReviewRequiresMatchingFormatAndPreservingDefault'] * 2 + ['NativeCapturedReportsUseReviewedAppOutputBoundaryAndKeepExistingFiles']
    record['initialFixtureFailures'][platform] = [{'name': test.get('name'), 'message': test.findtext('./failure/message')} for test in failures]
    row = next(item for item in proof['platforms'] if item['platform'] == tag)
    allowed = {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'}
    assert row['guestReuse'] and set(row['changed']) == allowed
    assert sha(root / f'.tmp/dsa-batch-{tag}-r1084.tgz') == row['sha256']
    delta = root / ('.tmp/test-fix-delta-r1084' if platform == 'windows' else f'.tmp/test-fix-delta-{tag}-r1084')
    plan = read(delta / 'plan.json'); baseline = {item['path']: item['sha256'] for item in record['inputs'][platform]}
    assert {item['path'] for item in plan['files']} == set(baseline)
    assert all(item['baselineSha256'] == baseline[item['path']] for item in plan['files'])
    assert all(sha(root / f'.tmp/batch-r1084-{tag}' / item['path']) == item['sha256'] for item in plan['files'])
    assert all(item['path'] in allowed or item['sha256'] == item['baselineSha256'] for item in plan['files'])
    record['helperInputOverlays'][platform] = {'proof': row, 'plan': plan}
    busy = root / f'.tmp/{platform}-batch-r1083-busy-results.tgz'
    raw = reports(busy); assert raw['state.txt'].decode('utf-8-sig').strip() == 'BUSY phase=verify'
    assert not any(name.endswith('.xml') for name in raw)
    inventory = root / f'.tmp/{platform}-batch-r1083-busy-post.json'
    assert read(inventory)['filecatOrDotnet']
    files.update({busy, inventory, root / f'.tmp/{platform}-batch-r1083-busy-state.txt'})
    record['initialBusyRefusals'][platform] = 'A foreign test suite began after the idle check. The runner postponed before payload extraction. Its original handle exited; the same uploaded runner and bundle were hash-verified and resumed after a fresh idle check.'
files.update({root / '.tmp/source-candidate-r1084.json', root / '.tmp/test-fix-proof-r1084.json', root / '.tmp/captured-report-helper-build-r1084.txt'})
files.add(foreign)
record['postRunForeignTester'] = 'The original cohort handle exited successfully. Ubuntu subsequently reported a foreign archive-workflow build whose executable is in that other tester\'s explicit checkout. Its exact process list is preserved and admitted only for this final receipt. No task-owned process remained, and no foreign process was stopped. A globally idle guest is still required before the next test run.'
record['qualification'] = {
    'indexedCanonicalReportCaptureQualified': True,
    'nativeMarkdownHtmlCsvOutputsQualified': True,
    'reportOptionsAndFilenameReviewQualified': True,
    'newNativeAndViewCasesPassedAllThree': 20,
    'broadSuitesPassedAllThree': True,
    'taskOwnedProcessesExited': True,
    'foreignPostRunProcessesPreserved': True,
    'physicalPickerAndDesktopInputQualified': False,
    'memoryAndGroupedFilteredReportCaptureComplete': False,
    'chartExportsImplemented': False,
    'publicFeatureRegistered': False,
    'finalIndependentReviewPerformed': False,
}
retain(root, record, files, runners, ['finalize-captured-reports.py', 'native_batch_evidence.py',
    'prepare-history-workflow-batch.ps1', 'add-app-settings-suite.ps1', 'add-unix-previews.ps1',
    'add-windows-previews.ps1', 'pack-batch-direct.ps1', 'deploy-batch.ps1',
    'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'resume-uploaded-batch.ps1',
    'prepare-test-fix-batch.ps1', 'run-batch-cache.py'])
freeze_text(root, record, f'exact-text-{revision}-captured-reports.tgz')
(out / manifest).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['capturedIndexedReports'] = {'manifest': manifest, 'matrix': record['matrix'], **record['qualification']}
progress['completedBehaviors'].append('Indexed report capture retains the exact offline dataset and canonical hierarchy through actual export and cancellation. Markdown/HTML/CSV support explicitly reviewed filtered-folder, marked-real-path, and entire-unfiltered scopes with declared detail/count limits. Default redaction, explicit folder/name review, protected native output boundaries and preserving existing files are tested. Twenty new native/view cases and applicable broad suites pass Windows/Mac/Ubuntu (r1084); only rendering-test DLL/PDB changed after correcting assertions for Markdown escaping and the existing dialog availability timer. Memory and grouped-filtered captures, basis/context details, charts and physical desktop qualification remain open.')
progress['nextWork'] = ['W3: complete memory/grouped report capture, basis/context details and SVG/PNG chart exports; complete comparison replacement or evidenced unavailable behavior and full resource/physical-desktop qualification. Indexed report capture/readers and report option/name reviews have scoped native evidence (r1084).' if item.startswith('W3:') else item for item in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'CAPTURED_REPORTS_QUALIFICATION.md').write_text('''# Captured indexed reports

An indexed report retains its immutable dataset and the existing canonical hierarchy. Filtered-folder reports include its immediate real rows in captured table order, independent of table pagination. Marked-path reports use explicit captured IDs; their summary is labelled as source context rather than an overlapping sum. Entire-result CSV walks every captured real namespace row. Entire-result human reports contain the canonical summary and bounded root-level detail, explicitly labelled limited. Synthetic accounting buckets remain in the summary and are excluded from the namespace table. Unmeasured evidence stays explicit even when exact missing-state counters are empty.

The indexed UI offers Markdown, HTML and spreadsheet-safe CSV with explicit scope and default path omission. Group and small-item views offer marked paths or the entire unfiltered result and explain how to return to folder hierarchy for a filtered-folder report. They never silently substitute a different filtered scope. Folder selection and filename/sensitivity review use Cancel as default. Existing files are preserved, and report format/filename extensions must agree. App output uses the same native protected-boundary and private-staging capability as full inventories. Named-save/history state is unchanged.

Twenty new native/view cases pass on Windows, macOS and Ubuntu. They cover all three scopes, all formats/redaction modes, original source/lease closure with the source physically offline, actual cancellation and retained graph ownership during blocked output, invalid capture refusal, protected app artifacts, existing-file preservation, report-option defaults and filename validation. Applicable broad Core/rendering/Settings suites and the Windows native suite pass. Exact counts, skip identities, source/input hashes and final process inventories are in [r1084 provenance](source-manifest-r1084-captured-reports.json). Every task-owned process exited. The Ubuntu inventory retained a later foreign build from its explicit separate checkout; it was left alone. No globally idle Ubuntu claim is made, and a fresh idle guard still gates every subsequent test.

The first preflight passed 17/20 on each platform. Two filename-review assertions did not wait for the existing 200 ms availability refresh, and a Markdown assertion expected an unescaped source path. The corrected helper changes only the rendering test DLL/PDB; every product input is hash-identical. Initial foreign-test busy refusals and their inventories are retained. Their runners resumed from the same hash-verified uploaded bundles after all foreign instances exited. Each original rerun handle reached actual terminal status, and results were collected once per final cohort.

This is scoped qualification. Memory and grouped-filtered report capture, basis/context details, chart exports, physical picker/input/accessibility/theme/DPI/package and resource/scale/death gates remain open. Public registration remains off. The final independent UX/UI/functional/security review is still pending.
''', encoding='utf-8')
print(json.dumps({'manifest': manifest, 'newNativeViewCases': 20, 'fullReportWorkflowComplete': False}))
