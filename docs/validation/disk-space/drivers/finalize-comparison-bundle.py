"""Freeze only the completed comparison-bundle export slice, not full-plan completion."""
import json
import pathlib
import re
from native_batch_evidence import assembly, freeze_text, qualify, read, reports, retain

root = pathlib.Path.cwd()
out = root / 'docs/validation/disk-space'
revision = 'r1104'
name = f'source-manifest-{revision}-comparison-bundle.json'
assert not (out / name).exists()
expected, foreign = {}, {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (64, 0), 'preview': (76, 0),
        'core': (1641, 48) if platform == 'windows' else (1623, 4),
        'render': (611, 1) if platform == 'windows' else (611, 0),
        'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows': expected[platform]['native'] = (75, 0)
    post = read(root / f'.tmp/{platform}-batch-{revision}-post.json')
    if post['filecatOrDotnet']:
        for process in post['filecatOrDotnet']:
            if platform == 'windows': assert re.search(r'owner=[^,]+\\fcarchive42829297, cmd=', process)
            else: assert '/FileCatValidation/archive-writing-42829297/' in process
        foreign[platform] = post['filecatOrDotnet']
record, files, runners, receipts = qualify(root, revision, expected, ('NativeComparisonBundle', 'ComparisonBundleExportReview'), 5, foreign_post_processes=foreign)
for platform in expected:
    path = root / f'.tmp/{platform}-batch-{revision}-original-run.json'
    status = read(path)
    if platform == 'windows':
        assert status['state'] == 'postponed-no-deployment' and status['exitCode'] == 75
        stopped = root / '.tmp/windows-batch-r1104-direct-original-run.json'
        assert read(stopped)['state'] == 'postponed-before-direct-deployment' and read(stopped)['exitCode'] == 75
        postponed = root / '.tmp/windows-batch-r1104-final-original-run.json'
        assert read(postponed)['state'] == 'postponed-before-deployment' and read(postponed)['exitCode'] == 75
        actual = root / '.tmp/windows-batch-r1104-waited-original-run.json'
        assert read(actual)['state'] == 'waited-continuation-terminal' and read(actual)['exitCode'] == 0
        files.update((stopped, postponed, actual))
    else:
        assert status['state'] == 'original-deployment-terminal' and status['exitCode'] == 0
    files.add(path)
original_path = root / '.tmp/mac-batch-r1103-results.tgz'
original = assembly(reports(original_path), 'preview', 'r1103')
assert (original.get('total'), original.get('passed'), original.get('failed'), original.get('errors')) == ('76','72','4','0')
record['initialIntegrationFailure'] = {'platform':'mac', 'failedNames':[test.get('name') for test in original.findall('.//test') if test.get('result') == 'Fail'],
    'fix':'The shared output suffix guard now explicitly accepts .fcdsc. The preserving destination validation and strict comparison-specific name/privacy checks remain in force.'}
files.update({original_path,root / '.tmp/source-text-r1103-comparison.tgz',root / '.tmp/source-candidate-r1103.json',root / '.tmp/comparison-bundle-build-r1103.txt'})
record['qualification'] = {
    'guardedComparisonBundleExportQualified': True,
    'bothImmutableInputOwnersRetainedThroughActualOutput': True,
    'existingFileAndProtectedStatePreserved': True,
    'rawMachinePrivacyReviewAndCancelDefaultQualified': True,
    'newNativeAndReviewCasesPassedAllThree': 5,
    'broadSuitesPassedAllThree': True,
    'taskOwnedProcessesExited': True,
    'externalComparisonImportUiComplete': False,
    'comparisonHumanReportsAndChartsComplete': False,
    'fullPhysicalAndResourceQualificationComplete': False,
    'publicFeatureRegistered': False,
    'finalIndependentReviewPerformed': False,
}
retain(root, record, files, runners, ['finalize-comparison-bundle.py', 'native_batch_evidence.py',
    'prepare-history-workflow-batch.ps1', 'add-app-settings-suite.ps1', 'add-unix-previews.ps1',
    'add-windows-previews.ps1', 'pack-batch-direct.ps1', 'deploy-batch.ps1', 'wait-deploy-batch.ps1',
    'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'])
freeze_text(root, record, f'exact-text-{revision}-comparison-bundle.tgz')
(out / name).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['comparisonBundleExport'] = {'manifest': name, 'matrix': record['matrix'], **record['qualification']}
progress['completedBehaviors'].append('The comparison footer can export both complete immutable inputs and applied comparison settings as a self-contained .fcdsc machine bundle through the protected reviewed create-new route. Default Cancel and Keep existing remain in force; exact names/IDs are explicitly disclosed, and a path-omission claim is rejected before opening output. Both owners survive original-view closure/offline sources and actual blocked-output cancellation. Five new native/review cases plus affected broad suites pass Windows/Mac/Ubuntu (r1104). External comparison import UI and human/comparison-chart reports remain separate open work; this is not full interoperability or full-plan qualification.')
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'COMPARISON_BUNDLE_EXPORT_QUALIFICATION.md').write_text('''# Comparison bundle export

The comparison footer offers an explicit .fcdsc machine export of both complete captured inventories and applied comparison view settings. Display filters do not reduce the inventories. The review names the sensitive raw names, locations and identifiers included; path omission is unavailable for this format. Folder selection, filename review, default Cancel, protected application-state boundaries and preserving create-new publication use the existing guarded output route. Named-save/history state does not change.

The captured output independently retains both immutable snapshot owners through actual encoding, native writing/publication, cancellation and final release. The existing strict comparison format carries inert data and no source connections or action approvals. The bounded two-buffer prepared-output pipeline is shared with human reports. Five new native/review cases verify offline ownership and strict format round-trip, destination conflicts, protected-state refusal, suffix/privacy mismatch before destination creation, review defaults and actual blocked-output cancellation. Applicable broad suites pass all three platforms; exact sources, inputs, unchanged skips and post-run inventories are in [r1104 provenance](source-manifest-r1104-comparison-bundle.json).

This is a scoped export checkpoint. External comparison import UI, human/CSV/comparison-chart exports, full physical/resource qualification, later plan packets and the final independent UX/UI/functional/security review remain open. Public registration remains off.
''', encoding='utf-8')
print(json.dumps({'manifest': name, 'newCases': 5, 'fullPlanComplete': False}))
