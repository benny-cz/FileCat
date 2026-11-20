"""Qualify chart output only after all original guest runners actually finish."""
import json
import pathlib
import re
from native_batch_evidence import assembly, freeze_text, qualify, read, reports, retain, sha

root = pathlib.Path.cwd()
out = root / 'docs/validation/disk-space'
revision = 'r1096'
manifest = f'source-manifest-{revision}-chart-exports.json'
assert not (out / manifest).exists(), 'Use a fresh immutable qualification revision'
expected = {}
foreign = {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (64, 0), 'preview': (71, 0),
        'core': (1641, 48) if platform == 'windows' else (1623, 4),
        'render': (606, 1) if platform == 'windows' else (606, 0),
        'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows': expected[platform]['native'] = (75, 0)
    inventory = read(root / f'.tmp/{platform}-batch-{revision}-post.json')
    if inventory['filecatOrDotnet']:
        for process in inventory['filecatOrDotnet']:
            if platform == 'windows': assert re.search(r'owner=[^,]+\\fcarchive42829297, cmd=', process)
            else: assert '/FileCatValidation/archive-writing-42829297/' in process and '/dsa-batch-20261107-' not in process
        foreign[platform] = inventory['filecatOrDotnet']
record, files, runners, receipts = qualify(root, revision, expected, ('NativeChartExport', 'ChartExport', 'NativeCsvBatch', 'NativeCsvLineage'), 24, foreign_post_processes=foreign)
for platform in expected:
    terminal = root / f'.tmp/{platform}-batch-{revision}-original-run.json'
    outcome = read(terminal)
    assert outcome['state'] == 'original-deployment-terminal' and outcome['exitCode'] == 0
    files.add(terminal)
proof = read(root / '.tmp/test-fix-proof-r1094.json')
assert proof['baseline'] == 'r1093' and proof['scope'] == 'AppUi'
record['initialMissingWatermark'] = {}
record['intermediateXmlFailure'] = {}
for original_revision, snapshot in [('r1093','source-text-r1093-original.tgz'),('r1094','source-text-r1094.tgz')]:
    original_sources = read(root / f'.tmp/source-candidate-{original_revision}.json')
    import hashlib, tarfile
    with tarfile.open(root / '.tmp' / snapshot) as archive:
        members = archive.getmembers()
        assert {member.name for member in members} == {item['path'] for item in original_sources}
        expected_hashes = {item['path']:item['sha256'] for item in original_sources}
        assert all(hashlib.sha256(archive.extractfile(member).read()).hexdigest() == expected_hashes[member.name] for member in members)
    files.update({root / '.tmp' / snapshot, root / f'.tmp/source-candidate-{original_revision}.json'})
for platform in expected:
    tag = 'linux' if platform == 'ubuntu' else platform
    delta = root / ('.tmp/test-fix-delta-r1094' if platform == 'windows' else f'.tmp/test-fix-delta-{tag}-r1094')
    plan = read(delta / 'plan.json')
    assert plan['scope'] == 'AppUi'
    allowed = {'render/FileCat.dll', 'render/FileCat.pdb'}
    assert {item['path'] for item in plan['files'] if item['patch']} == allowed
    assert all(item['path'] in allowed or item['sha256'] == item['baselineSha256'] for item in plan['files'])
    assert all(sha(root / f'.tmp/batch-r1093-{tag}' / item['path']) == item['baselineSha256'] for item in plan['files'])
    assert all(sha(root / f'.tmp/batch-r1094-{tag}' / item['path']) == item['sha256'] for item in plan['files'])
    path = root / f'.tmp/{platform}-batch-r1093-results.tgz'; raw = reports(path)
    assert raw['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=preview'
    preview = assembly(raw, 'preview', 'r1093')
    assert (preview.get('total'), preview.get('passed'), preview.get('failed'), preview.get('errors')) == (('65','60','5','0') if platform == 'windows' else ('65','61','4','0'))
    failures = [test for test in preview.findall('.//test') if test.get('result') == 'Fail']
    watermark_failures = [test for test in failures if test.get('method') == 'NativeChartExportUsesIndexedFolderAndGroupedGeometryAfterOriginalOwnersClose']
    assert len(watermark_failures) == 4
    other = [test for test in failures if test not in watermark_failures]
    assert len(other) == (1 if platform == 'windows' else 0)
    if other: assert other[0].get('method') == 'NativeChartExportKeepsCapturedScopeOfflineAndPreservesExistingFiles' and 'invalid character' in other[0].findtext('./failure/message')
    record['initialMissingWatermark'][platform] = {'failures':[test.get('name') for test in failures], 'overlay':plan, 'xmlFailureAlsoPresentInOriginalWindowsCohort': platform == 'windows'}
    files.add(path)
    intermediate = root / f'.tmp/{platform}-batch-r1094-results.tgz'; raw = reports(intermediate)
    preview = assembly(raw, 'preview', 'r1094')
    if platform == 'windows':
        assert raw['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=1 phase=preview'
        assert (preview.get('total'), preview.get('passed'), preview.get('failed')) == ('65','64','1')
        failures = [test for test in preview.findall('.//test') if test.get('result') == 'Fail']
        assert len(failures) == 1 and failures[0].get('method') == 'NativeChartExportKeepsCapturedScopeOfflineAndPreservesExistingFiles'
        assert 'invalid character' in failures[0].findtext('./failure/message')
        record['intermediateXmlFailure'][platform] = {'name':failures[0].get('name'),'message':failures[0].findtext('./failure/message')}
    else:
        assert raw['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=0'
        assert (preview.get('total'), preview.get('passed'), preview.get('failed')) == ('65','65','0')
        assert assembly(raw,'render','r1094').get('passed') == '600'
        record['intermediateXmlFailure'][platform] = 'Focused and broad intermediate cohort passed; retained rather than misreported as Windows qualification.'
    files.add(intermediate)
files.update({root / '.tmp/test-fix-proof-r1094.json', root / '.tmp/app-ui-delta-proof-r1094.json', root / '.tmp/chart-export-build-r1093.txt', root / '.tmp/chart-watermark-build-r1094.txt', root / '.tmp/pack-timing-r1093.json'})
record['initialMissingWatermark']['fix'] = 'The existing partial notice was present but the prominent standalone PARTIAL stamp was missing. The corrected app adds that stamp and reserves title space. The intermediate app-only patch reused every other binary under exact hash checks.'
record['intermediateXmlFailure']['fix'] = 'A legal Windows filename contained XML 1.0-forbidden U+FFFF. Captured display text now visibly escapes invalid XML code units before encoding either image format; valid supplementary characters remain intact. A dedicated portable XML-scalar regression was added. The correction and the independent Core CSV batching chunk were rebuilt and fully rerun as one coherent cohort.'
record['foreignPostRunProcesses'] = foreign
record['qualification'] = {
    'pngAndSvgCapturedChartsImplemented': True,
    'sharedExistingGeometryAndGuardedOutputQualified': True,
    'completeSourceTextRedactionAndInertSvgQualified': True,
    'boundedImageAdmissionAndActualOutputLifetimeQualified': True,
    'nativePngDecodeAndNonblankPixelReadbackQualified': True,
    'indexedFolderAndGroupCaptureAfterOriginalOwnersCloseQualified': True,
    'comparisonPrimarySnapshotSubstitutionDisabled': True,
    'newChartNativeAndViewCasesPassedAllThree': 19,
    'newCsvBatchAndLineageCasesPassedAllThree': 5,
    'canonicalCsvBatchReadsAndBoundedLineageImplemented': True,
    'broadSuitesPassedAllThree': True,
    'taskOwnedProcessesExited': True,
    'comparisonExportsComplete': False,
    'largeCsvPerformanceQualified': False,
    'fullPhysicalAndResourceQualificationComplete': False,
    'publicFeatureRegistered': False,
    'finalIndependentReviewPerformed': False,
}
retain(root, record, files, runners, ['finalize-chart-exports.py', 'native_batch_evidence.py',
    'prepare-history-workflow-batch.ps1', 'add-app-settings-suite.ps1', 'add-unix-previews.ps1',
    'add-windows-previews.ps1', 'pack-batch-direct.ps1', 'deploy-batch.ps1',
    'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1', 'wait-deploy-batch.ps1', 'prepare-cached-app-ui-batch.ps1', 'prepare-test-fix-batch.ps1', 'run-batch-cache.py'])
freeze_text(root, record, f'exact-text-{revision}-chart-exports.tgz')
(out / manifest).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['capturedChartExports'] = {'manifest': manifest, 'matrix': record['matrix'], **record['qualification']}
progress['completedBehaviors'].append('PNG and inert SVG exports capture the existing treemap/sunburst geometry with metric, scope, observation interval, legend, freshness, partial watermark and privacy/privileged notices. Default redaction removes all source names, locators and identifiers before scene encoding. Full-result CSV now reads existing canonical namespace summaries in bounded batches and reuses directory prefixes through a per-producer 2 MiB/2,048-entry LRU cache over captured metadata. Cancellation clears that cache after the actual producer stops. Shared 64 MiB chart admission covers scene, encoded buffer and conservative PNG working memory; dimensions also have 16,384/side and 64 MP hard bounds. The shared actual-completion output lifetime retains captured owners and memory through native write/publication/close and cancellation. Both memory and indexed UIs use the existing reviewed protected create-new route, keep existing outputs and explicit scope/size choices. Comparison views no longer silently export the primary snapshot. Nineteen new chart native/view and five new CSV batch/lineage checks plus broad suites pass all three platforms (r1096). Full comparison export, large CSV performance, physical/resource qualification and later packets/final independent review remain open.')
progress['nextWork'] = ['W3: complete comparison-specific export/replacement behavior or evidenced prescribed unavailable routes; qualify large CSV lineage/read batching at1M/10M and physical desktop/resource/package gates. Human and chart exports now have scoped three-platform evidence.' if item.startswith('W3:') else item for item in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'CHART_EXPORTS_QUALIFICATION.md').write_text('''# Captured chart exports

Both report flows offer the current treemap or sunburst as PNG or SVG, with explicit image dimensions and names/locations redacted by default. The current chart and retained report scope are captured before publication; changing or closing original owners cannot retarget encoded output. The existing chart layout supplies geometry without a second accounting implementation. Captured metric, known area value, scope, observation interval, legend, freshness, partial-state watermark and privileged/privacy caveats are visible. Zero/unknown/shared-only results retain explanatory text.

SVG contains generated geometry, normalized colors, escaped display text and generated local clip references only. It has no scripts, event handlers, remote resources, links, foreign objects, source keys or hidden source payload. Redaction is applied before creating the immutable scene. PNG encodes that same scene and contains no source text metadata. Shared 64 MiB admission occurs before allocating scene/image buffers; absolute limits are 16,384 pixels per dimension and 64 megapixels. Conservative working-memory estimates can refuse a smaller image too. The current chooser offers 960×720, 1280×960 and 1600×1200. Captured data, buffers and admission remain owned through actual native output completion and cleanup.

The native create-new path, protected application-state boundary, folder/name review, default Cancel and Keep existing behavior are reused. Existing outputs remain untouched on conflicts; replacement stays explicitly unavailable. Comparison report/chart exports are visibly unavailable instead of silently substituting the primary analysis.

Nineteen new chart native/view and five new CSV batch/lineage cases pass Windows/macOS/Ubuntu, with prior report controls and applicable broad Core/rendering/Settings and Windows native regressions. Checks include both chart modes and formats, default/reviewed redaction, decodable nonblank PNG pixels, PNG metadata chunks, SVG escaping/inert element set, indexed folder/group geometry after original owners close, offline memory sources, preserving destination conflicts, dimension/memory refusal before output opens, review defaults and actual blocked-write cancellation while memory stays reserved. See [r1096 provenance](source-manifest-r1096-chart-exports.json) for exact sources/inputs, counts, unchanged skip identities and final process inventories.

Full-result CSV reads existing canonical namespace rows in bounded I/O turns, preserving raw captured order and every canonical row field. A per-producer directory-prefix cache is capped at 2 MiB and 2,048 entries, reads captured metadata only, preserves root/name separator semantics and clears when the producer ends. Five additional native checks compare all four metrics with canonical single reads and verify offline prefix fidelity, fewer parent lookups and cancellation. Full 1M/10M throughput/resource qualification remains pending.

This is scoped qualification. Full comparison export, large full-result CSV performance, the physical picker/input/accessibility/theme/DPI/package/resource/scale/death matrix, later packets and final independent UX/UI/functional/security review remain open. Public registration remains off.
''', encoding='utf-8')
print(json.dumps({'manifest': manifest, 'newChartCases': 19, 'newCsvCases': 5, 'fullPlanComplete': False}))
