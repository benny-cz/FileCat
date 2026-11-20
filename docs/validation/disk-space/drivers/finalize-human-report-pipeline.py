"""Retain formatter/output/fallback evidence; it does not qualify report UI or dataset scope capture."""
import json
import pathlib
import shutil
from native_batch_evidence import assembly, assert_clean_build, freeze_text, qualify, read, reports, retain, sha

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
manifest = 'source-manifest-r1079-human-report-pipeline.json'
assert not (out / manifest).exists(), 'Use a fresh immutable qualification revision'
expected = {}
overrides = {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (24, 0), 'preview': (11, 0),
                          'core': (1601, 48) if platform == 'windows' else (1583, 4),
                          'render': (536, 1) if platform == 'windows' else (536, 0),
                          'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows': expected[platform]['native'] = (75, 0)
    for phase in expected[platform]: overrides[f'{platform}/{phase}'] = 'r1078' if platform == 'windows' else 'r1079'
prefixes = ('NativeHumanReport', 'NativeExternalExportWithCorruptProfile', 'MemoryFallbackCanExport')
record, files, runners, originals = qualify(root, 'r1077', expected, prefixes, 11, phase_revisions=overrides,
    expected_states={platform: 'FINISHED exit=1 phase=smoke' for platform in expected}, source_snapshot='.tmp/source-text-r1077.tgz')
updated = read(temp / 'source-candidate-r1079.json')
current = {source['path']: source['sha256'] for source in updated}
old = {source['path']: source['sha256'] for source in record['sources']}
assert set(current) == set(old)
assert {name for name in old if current[name] != old[name]} == {
    'tests/FileCat.Core.Tests/AnalysisHumanReportTests.cs', 'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.HumanReports.cs'}
assert all(sha(root / source['path']) == source['sha256'] for source in updated)
record['productBatchSources'] = record['sources']; record['sources'] = updated
record['productRevision'] = record['revision']; record['revision'] = 'r1079'
record['initialFixtureFailures'] = {}
record['helperInputOverlays'] = {}
assert_clean_build(temp / 'human-report-helper-build-r1078.txt', 2)
for platform in ('windows', 'mac', 'ubuntu'):
    a = assembly(originals[platform], 'smoke', 'r1077')
    assert (a.get('total'), a.get('passed'), a.get('failed'), a.get('errors')) == ('24', '23', '1', '0')
    failures = [test for test in a.findall('.//test') if test.get('result') == 'Fail']
    assert len(failures) == 1 and failures[0].get('method') == 'HumanFormatsEscapePoisonedLabelsKeepExactNumbersAndOmitEveryRedactedLocator'
    assert 'Markdown' in failures[0].get('name') and 'PRIVATE_SCOPE' in failures[0].findtext('./failure/message')
    record['initialFixtureFailures'][platform] = {'name': failures[0].get('name'), 'message': failures[0].findtext('./failure/message')}
    tag = 'linux' if platform == 'ubuntu' else platform
    revision = 'r1078' if platform == 'windows' else 'r1079'
    proof = read(temp / f'test-fix-proof-{revision}.json')
    assert proof['scope'] == 'TestOnly' and proof['baseline'] == 'r1077' and set(proof['testPhases']) == {'core', 'render'}
    row = next(item for item in proof['platforms'] if item['platform'] == tag)
    allowed = {'core/FileCat.Core.Tests.dll', 'core/FileCat.Core.Tests.pdb', 'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'}
    assert row['guestReuse'] and set(row['changed']) == allowed and sha(temp / f'dsa-batch-{tag}-{revision}.tgz') == row['sha256']
    delta = temp / (f'test-fix-delta-{revision}' if tag == 'windows' else f'test-fix-delta-{tag}-{revision}')
    plan = read(delta / 'plan.json'); baseline = {item['path']: item['sha256'] for item in record['inputs'][platform]}
    assert {item['path'] for item in plan['files']} == set(baseline)
    assert all(item['baselineSha256'] == baseline[item['path']] for item in plan['files'])
    assert all(sha(temp / f'batch-{revision}-{tag}' / item['path']) == item['sha256'] for item in plan['files'])
    assert all(item['path'] in allowed or item['sha256'] == item['baselineSha256'] for item in plan['files'])
    result = reports(temp / f'{platform}-batch-{revision}-results.tgz')
    for phase in ('smoke', 'core'):
        cases = [test for test in assembly(result, phase, revision).findall('.//test') if test.get('type', '').endswith('.AnalysisHumanReportTests')]
        assert len(cases) == 24 and all(test.get('result') == 'Pass' for test in cases)
    record['helperInputOverlays'][platform] = {'proof': proof, 'plan': plan}
    files.add(temp / f'test-fix-proof-{revision}.json')
for platform in ('mac', 'ubuntu'):
    rejected = reports(temp / f'{platform}-batch-r1078-results.tgz')
    assert not any(name.endswith('.xml') for name in rejected)
    assert not read(temp / f'{platform}-batch-r1078-post.json')['filecatOrDotnet']
    files.add(temp / f'{platform}-batch-r1078-results.tgz')
old_guard = temp / 'rejected-unix-cache-guard-r1078.py'
shutil.copy2(temp / 'test-fix-delta-mac-r1078/reuse.py', old_guard)
assert 'Only one test DLL and PDB may be patched' in old_guard.read_text()
files.update({old_guard, temp / 'source-candidate-r1078.json', temp / 'source-candidate-r1079.json', temp / 'human-report-helper-build-r1078.txt'})
record['unixHelperRejection'] = 'The original Unix cache validator rejected two complete test pairs before any test launched; a fresh helper bundle accepts only exact Core/render test pairs.'
flags = {'humanFormatterAndNativeOutputPipelineQualified': True, 'memoryFallbackExplicitExportQualified': True,
         'newPureCasesPassedAllThree': 24, 'newNativeAndViewCasesPassedAllThree': 11, 'broadSuitesPassedAllThree': True,
         'reportUiAndCanonicalScopeCaptureImplemented': False, 'chartExportsImplemented': False,
         'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record.update({'purpose': 'Inert streamed Markdown/HTML/CSV formatter and explicit export during profile fallback', 'qualification': flags,
               'scope': 'Supplied immutable report DTOs and actual native output. Does not qualify UI report capture, full dataset CSV readers, basis/context completeness, chart output or physical picker behavior.'})
retain(root, record, files, runners, ['finalize-human-report-pipeline.py', 'native_batch_evidence.py', 'prepare-history-workflow-batch.ps1',
       'prepare-test-fix-batch.ps1', 'run-batch-cache.py', 'add-app-settings-suite.ps1', 'add-unix-previews.ps1',
       'add-windows-previews.ps1', 'pack-batch-direct.ps1', 'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'])
freeze_text(root, record, 'exact-text-r1079-human-report-pipeline.tgz')
(out / manifest).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['humanReportPipeline'] = {'manifest': manifest, 'matrix': record['matrix'], **flags}
progress['completedBehaviors'].append('Markdown/HTML/spreadsheet-safe CSV now have a bounded inert formatter and native create-new output pipeline for explicitly supplied captured report DTOs. Text/markup/formula, complete path/identity redaction, UInt128 values, declared row count, partial coverage, non-file boundary labels, empty CSV metadata and actual producer cancellation are checked. External inventory export now has separate memory-fallback eligibility; corrupt profile bytes remain unchanged, and APFS independently qualifies fresh private scratch. Twenty-four pure and eleven native/view cases plus broad suites pass all three (r1079) with unchanged product inputs after fixture-only canary correction. Report UI, canonical memory/indexed scope capture, full dataset CSV readers, basis/context details and chart exports remain unfinished.')
progress['nextWork'] = ['W3: wire report UI to retained canonical memory/indexed captures, selected/full CSV readers, basis/context details and SVG/PNG chart export. Formatter/native-output foundation and memory-fallback export now have scoped three-platform evidence (r1079). Complete comparison replacement or evidenced unavailable behavior and full resource/physical-desktop qualification remain open.' if line.startswith('W3:') else line for line in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'HUMAN_REPORT_PIPELINE_QUALIFICATION.md').write_text('''# Human report formatter, output pipeline and fallback eligibility

Markdown, HTML and CSV now have an inert streamed formatter for explicitly supplied immutable report DTOs. The output declares scope, observation interval, metric definition, known/unverified/missing values, coverage, freshness, privacy and caveats. Folder rollups are labelled non-additive. Non-file namespace boundaries remain Not measured / Not counted; real zero files retain zero. UInt128 values use exact invariant decimal text. Human detail is bounded to 5,000 rows and CSV to the observation limit; declared count mismatches cannot finish as a successful report. Empty CSV still contains a metadata record and declared columns.

HTML contains no script, external resource or source link. Markdown markup and display controls are escaped. Spreadsheet CSV quotes all fields and neutralizes formula-leading display text with an apostrophe; it does not alter stored raw names. Redaction substitutes generated item labels and omits all source names, locators and identity fields, including hidden metadata. Exact raw data remains separate in .fcdsa inventories. The bounded two-buffer producer writes through the existing native create-new output capability, owns cancellation through actual producer completion, and never publishes a failed partial stream.

Explicit inventory export now has eligibility separate from named save. A corrupt private profile does not prevent an otherwise safe explicit output on Windows/Linux. macOS independently qualifies fresh owner-private scratch and its current native identity for same-volume APFS staging. The corrupt profile bytes remain untouched and named/history state is unchanged. Themed reviews now explain existing-name preservation in plain language.

All three platforms pass 24 new formatter controls and 11 native/view controls. Those cover inert markup/formulas, complete canary redaction, exact numbers, row bounds/completeness, empty CSV, boundary labels and actual row-producer cancellation; actual native report outputs, failed-stream cleanup and corrupt-profile export; and the memory-fallback export button without named-save eligibility. Broad Core/rendering/Settings suites pass, together with the Windows native filesystem suite. Rendering totals 536 with the same existing Unix-only skip on Windows. Exact counts, unchanged skip identities and source/input hashes are in [r1079 provenance](source-manifest-r1079-human-report-pipeline.json).

The original short preflight passed 23/24 on each system. The Markdown fixture expected its underscore-bearing canary without Markdown escaping. Alphanumeric canaries now check both literal and redacted output without that ambiguity; only Core/render test DLL/PDB pairs changed. Every product input hash remained identical. An old Unix cache validator separately rejected the two-pair helper before any test launched; its rejected receipts and original guard are retained. Fresh Unix helpers use an exact pair whitelist. Each original runner reached terminal status, reports were collected once per cohort, and final process inventories are clear.

This qualifies supplied report DTO formatting and native output, not a complete report product workflow. The report UI, retained canonical memory/indexed scope captures, selected/full dataset CSV readers, basis/context details and SVG/PNG charts remain unfinished. Full physical picker/input/AT/theme/DPI/package and scale/resource/death qualification remain open. Public registration remains off and the final independent UX/UI/functional/security review has not been performed.
''', encoding='utf-8')
print(json.dumps({'manifest': manifest, 'newCoreCases': 24, 'newNativeViewCases': 11, 'reportWorkflowComplete': False}))
