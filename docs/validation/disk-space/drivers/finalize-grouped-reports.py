"""Freeze the grouped/Other report cohort after original native handles actually finish."""
import json
import pathlib
import re
import hashlib
import io
import tarfile
from native_batch_evidence import assembly, freeze_text, qualify, read, reports, retain, sha

root = pathlib.Path.cwd()
out = root / 'docs/validation/disk-space'
revision = 'r1091'
manifest = f'source-manifest-{revision}-grouped-reports.json'
assert not (out / manifest).exists(), 'Use a fresh immutable qualification revision'
expected = {}
foreign = {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (64, 0), 'preview': (47, 0),
        'core': (1641, 48) if platform == 'windows' else (1623, 4),
        'render': (582, 1) if platform == 'windows' else (582, 0),
        'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows': expected[platform]['native'] = (75, 0)
    inventory = read(root / f'.tmp/{platform}-batch-{revision}-post.json')
    if inventory['filecatOrDotnet']:
        for process in inventory['filecatOrDotnet']:
            if platform == 'windows': assert re.search(r'owner=[^,]+\\fcarchive42829297, cmd=', process)
            else: assert '/FileCatValidation/archive-writing-42829297/' in process and '/dsa-batch-20261107-' not in process
        foreign[platform] = inventory['filecatOrDotnet']
record, files, runners, receipts = qualify(root, revision, expected, ('NativeGroupedReport', 'SupportedReportViewsReview'), 15,
    foreign_post_processes=foreign, source_snapshot='.tmp/source-text-r1091.tgz')
for platform, raw in receipts.items():
    for phase in ('smoke', 'core'):
        cases = [test for test in assembly(raw, phase, revision).findall('.//test') if test.get('type', '').endswith('.AnalysisGroupedReportTests')]
        assert len(cases) == 14 and all(test.get('result') == 'Pass' for test in cases)
record['initialParityFailure'] = {}
for platform in expected:
    path = root / f'.tmp/{platform}-batch-r1090-results.tgz'; raw = reports(path)
    assert raw['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=preview'
    original = assembly(raw, 'preview', 'r1090')
    assert (original.get('total'), original.get('passed'), original.get('failed'), original.get('errors')) == ('47', '46', '1', '0')
    failed = [test for test in original.findall('.//test') if test.get('result') == 'Fail']
    assert len(failed) == 1 and failed[0].get('method') == 'NativeGroupedReportsMatchMemoryAndIndexedScopesAfterAllOriginalOwnersCloseAndSourceMovesOffline'
    assert 'variant: 2' in failed[0].get('name')
    record['initialParityFailure'][platform] = {'name': failed[0].get('name'), 'message': failed[0].findtext('./failure/message')}
    files.add(path)
files.update({root / '.tmp/source-text-r1090.tgz', root / '.tmp/source-candidate-r1090.json', root / '.tmp/grouped-report-build-r1090.txt'})
record['initialParityFailure']['fix'] = 'Memory Other-group logical statistics now use the existing deduplicated matching membership, including shared references whose ordinary entry group lies outside the geometry suffix. Canonical per-path logical statistics are folded once; charges and rows are unchanged. An additional Core regression protects this exact case.'
record['foreignPostRunProcesses'] = foreign
record['qualification'] = {
    'groupedAndOtherCapturedReportsQualified': True,
    'memoryIndexedReportScopeParityQualified': True,
    'independentScopeActualLifetimeQualified': True,
    'allocationUncertaintyDistinctFromNamespaceGap': True,
    'reportReferenceAndDirectoryRolesExplicit': True,
    'humanEntireReportOrderExplicit': True,
    'capturedTimezoneContextRedacted': True,
    'newCoreCasesPassedAllThree': 14,
    'newNativeAndViewCasesPassedAllThree': 15,
    'broadSuitesPassedAllThree': True,
    'taskOwnedProcessesExited': True,
    'chartExportsImplemented': False,
    'largeCsvPerformanceQualified': False,
    'fullPhysicalAndResourceQualificationComplete': False,
    'publicFeatureRegistered': False,
    'finalIndependentReviewPerformed': False,
}
retain(root, record, files, runners, ['finalize-grouped-reports.py', 'native_batch_evidence.py',
    'prepare-history-workflow-batch.ps1', 'add-app-settings-suite.ps1', 'add-unix-previews.ps1',
    'add-windows-previews.ps1', 'pack-batch-direct.ps1', 'deploy-batch.ps1',
    'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'])
# The next implementation chunk may already be in the checkout. Preserve exact
# compiled bytes from the independently verified frozen source, without restoring it.
text_target = out / f'exact-text-{revision}-grouped-reports.tgz'
assert not text_target.exists()
entries = {item['path']: item['sha256'] for group in ('sources', 'artifacts', 'drivers') for item in record[group] if not item['path'].endswith('.tgz')}
with tarfile.open(root / '.tmp/source-text-r1091.tgz') as source_archive:
    source_bytes = {member.name: source_archive.extractfile(member).read() for member in source_archive.getmembers()}
with tarfile.open(text_target, 'w:gz') as archive:
    for name, digest in sorted(entries.items()):
        payload = source_bytes[name] if name in source_bytes else (root / name).read_bytes()
        assert hashlib.sha256(payload).hexdigest() == digest
        info = tarfile.TarInfo(name); info.size = len(payload); archive.addfile(info, io.BytesIO(payload))
record['artifacts'].append({'path': text_target.relative_to(root).as_posix(), 'sha256': sha(text_target)})
record['exactTextSnapshot'] = {'path': text_target.relative_to(root).as_posix(), 'entries': sorted(entries), 'purpose': 'Exact compiled source and producer receipt bytes, independent of the following chart implementation chunk.'}
(out / manifest).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['capturedGroupedReports'] = {'manifest': manifest, 'matrix': record['matrix'], **record['qualification']}
progress['completedBehaviors'].append('Reports now capture group overviews, real member paths and exact Other scopes in both indexed and memory views. Indexed reports retain independent existing explorer scopes rather than rebuilding source membership. Canonical grouped statistics remain separate from reference logical statistics and omitted allocation buckets. Report roles, not-applicable directory file statistics, typed displayed order and filter timezone context are explicit and safely redacted. Entire human reports select active-metric descending root detail; missing allocation no longer falsely marks memory namespace rows incomplete. Fourteen new Core and fifteen native/view cases plus broad suites pass Windows/Mac/Ubuntu (r1091), including memory/indexed parity after original owners close and generated sources move offline. Native parity exposed and fixed Other-overview logical membership outside an entry group. Charts, large-CSV performance, full physical/resource qualification, later packets and final independent review remain open.')
progress['nextWork'] = ['W3: implement SVG/PNG chart exports and remaining comparison export/replacement behavior or evidenced prescribed unavailable behavior. Qualify large CSV lineage/read batching at1M/10M and complete physical desktop/resource/package gates. Folder/group/Other report scopes, typed roles/order and timezone/basis/query context now have scoped three-platform evidence (r1091).' if item.startswith('W3:') else item for item in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'GROUPED_REPORTS_QUALIFICATION.md').write_text('''# Grouped and Other report scopes

The report scope chooser now offers the captured folder, accounting-group overview, real group member paths and exact Other scopes. Indexed exports fork the existing sealed explorer and independently retain its accounting and raw metadata owners; closing the original view cannot retarget or release an active report. Memory exports retain the same canonical grouped fold and exact geometry suffix. Table pagination does not limit CSV, and changing table sort does not change Other membership. Whole-result and marked-path choices remain explicitly unfiltered source scopes.

Accounting-group rows are labelled separately from real namespace paths. Shared reference file-size/count statistics are descriptive and are not extra allocation or an additive partition of the summary. Directory allocation retains its measured active amount while logical file statistics are Not applicable. Real member tables omit synthetic allocation buckets while keeping their charges in the canonical summary. A missing allocation measurement no longer alone marks a memory namespace row incomplete. Existing canonical statistics are folded; there is no per-row accounting reprojection.

Reports declare a typed table order. Entire-result human reports choose largest root-level rows by active metric descending; bounded detail wording no longer implies that every current sort is a largest-item ranking. Captured filter timezone is descriptive and the predicate is never rerun when opened. Query, basis, timezone, names and location text are omitted under redaction. Basis wording distinguishes labels admitted to the captured metric scope from other unqualified raw inventory facts. Existing HTML/Markdown escaping and formula-safe CSV remain in force.

Fourteen new Core and fifteen native/view cases pass Windows/macOS/Ubuntu. Seven native paired cases compare group overview, ordinary/shared member scopes and Other overviews/members/folders between memory and indexed reports. They close original owners, move generated source directories offline, compare exact scope totals/logical counts and all declared row values, preserve payload bytes and release private files. Other controls cover provider-basis retention, mismatched-source refusal, actual blocked-output cancellation and all six supported scope chooser labels/defaults. Applicable broad suites and Windows native controls pass. See [r1091 provenance](source-manifest-r1091-grouped-reports.json) for exact source/input hashes, counts, unchanged skip identities and post-run inventories. All task-owned processes exited; any exact foreign process seen afterward was preserved and still gates later guest testing.

The original preflight passed 63 Core and 46/47 rendering cases on every platform. Its Other-overview parity case found that the memory summary omitted a shared reference whose ordinary group lay outside the selected suffix. The fix uses the existing bounded deduplicated matching membership and canonical per-path logical accumulator, with an additional Core regression. The fixed product and rebuilt dependents were frozen and fully rerun on all three platforms; this was not a helper-only change. Original failing receipts and exact compiled-source bytes are retained.

Chart exports, large full-result CSV performance, remaining comparison export/replacement work, full physical picker/input/accessibility/theme/DPI/package/resource/scale/death qualification and later work packets remain open. Public registration remains off. The final independent UX/UI/functional/security review remains pending.
''', encoding='utf-8')
print(json.dumps({'manifest': manifest, 'newCoreCases': 14, 'newNativeViewCases': 15, 'fullPlanComplete': False}))
