"""Freeze the combined memory/indexed capture and context cohort after actual guest completion."""
import json
import pathlib
import re
from native_batch_evidence import assembly, freeze_text, qualify, read, retain

root = pathlib.Path.cwd()
out = root / 'docs/validation/disk-space'
revision = 'r1088'
manifest = f'source-manifest-{revision}-memory-reports.json'
assert not (out / manifest).exists(), 'Use a fresh immutable qualification revision'
expected = {}
foreign = {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (50, 0), 'preview': (32, 0),
        'core': (1627, 48) if platform == 'windows' else (1609, 4),
        'render': (567, 1) if platform == 'windows' else (567, 0),
        'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows': expected[platform]['native'] = (75, 0)
    inventory = read(root / f'.tmp/{platform}-batch-{revision}-post.json')
    if inventory['filecatOrDotnet']:
        for process in inventory['filecatOrDotnet']:
            if platform == 'windows':
                assert re.search(r'owner=[^,]+\\fcarchive42829297, cmd=', process)
            else:
                assert '/FileCatValidation/archive-writing-42829297/' in process and '/dsa-batch-20261107-' not in process
        foreign[platform] = inventory['filecatOrDotnet']
record, files, runners, receipts = qualify(root, revision, expected, ('NativeMemoryCaptured',), 11,
    foreign_post_processes=foreign)
for platform, raw in receipts.items():
    for phase in ('smoke', 'core'):
        cases = [test for test in assembly(raw, phase, revision).findall('.//test')
            if test.get('type', '').endswith('.AnalysisMemoryReportTests') or test.get('method') == 'CapturedFilterAndProviderBasisContextEscapesLabelsAndOmitsAllRedactedSourceText']
        assert len(cases) == 26 and all(test.get('result') == 'Pass' for test in cases)
record['foreignPostRunProcesses'] = foreign
record['qualification'] = {
    'canonicalMemoryReportCaptureQualified': True,
    'sharedMemoryIndexedActualExportOwnershipQualified': True,
    'capturedFilterAndProviderBasisContextQualified': True,
    'identityUnverifiedUniqueScopedSummaryFixed': True,
    'nativeRootAndUnixBackslashReportLocationsFixed': True,
    'newCoreCasesPassedAllThree': 26,
    'newNativeCasesPassedAllThree': 11,
    'existingIndexedReportControlsRepeatedAllThree': 20,
    'broadSuitesPassedAllThree': True,
    'taskOwnedProcessesExited': True,
    'groupedFilteredReportsComplete': False,
    'chartExportsImplemented': False,
    'fullScaleResourceAndPhysicalDesktopQualificationComplete': False,
    'publicFeatureRegistered': False,
    'finalIndependentReviewPerformed': False,
}
retain(root, record, files, runners, ['finalize-memory-reports.py', 'native_batch_evidence.py',
    'prepare-history-workflow-batch.ps1', 'add-app-settings-suite.ps1', 'add-unix-previews.ps1',
    'add-windows-previews.ps1', 'pack-batch-direct.ps1', 'deploy-batch.ps1',
    'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'])
freeze_text(root, record, f'exact-text-{revision}-memory-reports.tgz')
(out / manifest).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['capturedMemoryReports'] = {'manifest': manifest, 'matrix': record['matrix'], **record['qualification']}
progress['completedBehaviors'].append('Memory report capture now uses retained immutable source data and the existing canonical tree fold, with a shared 64 MiB report admission and existing bounded table-order admission held through actual output completion. Filtered-folder, explicit marked paths and entire-result CSV/human reports share indexed output ownership and preserving review. Scoped identity-unverified unique-size amounts no longer disappear from memory summaries or enter chart area. Native roots and Unix trailing backslashes retain displayed path meaning. Reports include escaped/redactable captured filter details, allocation preset and bounded provider-basis labels taken from existing accounting state without another fact scan. Twenty-six new Core and eleven native cases, the twenty indexed report controls and broad suites pass all three (r1088). Grouped-filtered reports, charts, complete physical/resource/scale qualification and later work packets remain open.')
progress['nextWork'] = ['W3: complete grouped-filtered report scopes, SVG/PNG chart exports and remaining comparison replacement or evidenced unavailable behavior. Qualify full resource/scale, physical desktop and package behavior; include large full-result CSV lineage/read batching performance. Memory/indexed captured reports, canonical scoped amounts, root-path fidelity and captured filter/basis context have scoped native evidence (r1088).' if item.startswith('W3:') else item for item in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'MEMORY_REPORTS_QUALIFICATION.md').write_text('''# Memory reports and captured context

Memory reports now retain an independent immutable snapshot through preparation, output production, cancellation and actual release. They reuse the canonical projection and tree fold, retaining exact active/missing/logical statistics only for the report. A conservative shared 64 MiB report reservation precedes allocation; existing table-order admission remains separate and owned until close. Refusal preserves the analysis and offers complete inventory export. Filtered-folder rows, explicitly marked real paths and entire unfiltered CSV/human reports have the same declared scope/detail semantics and native protected create-new output as indexed reports. The memory view exposes the same report options and preserving destination review even when named save is unavailable.

Identity-unverified unique-size charges now retain their canonical attribution separately from chart area. Memory folder/root summaries can therefore show those amounts without falsely asserting uniqueness. Logical missing counts remain per matching namespace path, while missing allocation/identity cohorts remain charged once at their canonical owner. Reports preserve native root spelling and Unix filename backslashes. Source locations are inert displayed text.

Captured filter query, case handling, root-restriction presence, unknown-result cohort, allocation preset and bounded provider allocation-basis labels accompany the report. Basis labels come from the existing accounting pass; no extra fact scan or native source access is introduced. Scope-level labels do not claim per-row exclusive/reclaimable allocation. Nonfiltered exports clear the display predicate. Redaction omits all query and basis text in addition to names, locators and source identifiers, including hidden metadata. Escaping and formula safety remain in the existing formatter.

Twenty-six new Core cases and eleven new native cases pass Windows/macOS/Ubuntu. They cover every metric, coherent hard links, missing logical values, unverified identity, exact owner retention, refused oversized capture, native root and Unix-backslash paths, markup/redaction of query and basis context, all memory CSV scopes, human bounded detail, actual offline source/owner closure, preserving conflicts and blocked native-output cancellation. The twenty indexed report controls and memory fallback button check are repeated in the combined preflight. Applicable broad Core/rendering/Settings suites and Windows native controls pass. Exact hashes, counts, unchanged skip identities, source snapshots and final process inventories are in [r1088 provenance](source-manifest-r1088-memory-reports.json). All task-owned processes exited; any exact foreign process recorded after completion was preserved and still blocks subsequent guest testing.

This is scoped qualification, not full completion. Grouped-filtered report capture, chart exports, remaining comparison behavior, physical picker/input/accessibility/theme/DPI/package gates, full resource/scale/death qualification and later plan packets remain open. Large full-result CSV lineage and read batching still require performance qualification. Public registration remains off; the final independent UX/UI/functional/security review is pending.
''', encoding='utf-8')
print(json.dumps({'manifest': manifest, 'newCoreCases': 26, 'newNativeCases': 11, 'fullPlanComplete': False}))
