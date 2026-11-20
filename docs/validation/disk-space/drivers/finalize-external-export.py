"""Retain the native inventory-export cohort only after all three original runners finish."""
import json
import pathlib
from native_batch_evidence import freeze_text, qualify, read, retain

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
manifest = 'source-manifest-r1075-external-export.json'
assert not (out / manifest).exists(), 'Use a fresh immutable qualification revision'
expected = {}
for platform in ('windows', 'mac', 'ubuntu'):
    expected[platform] = {'smoke': (1, 0), 'preview': (43, 0),
                          'core': (1577, 48) if platform == 'windows' else (1559, 4),
                          'render': (525, 1) if platform == 'windows' else (525, 0),
                          'appsettings': (35, 1) if platform == 'windows' else (35, 0)}
    if platform == 'windows':
        expected[platform]['native'] = (75, 0)
prefixes = ('NativeExternalExport', 'ExternalExportReviews', 'ExternalPickerOwnership')
record, files, runners, receipts = qualify(root, 'r1075', expected, prefixes, 21)
files.add(temp / 'source-text-r1075.tgz')
for name in ('windows-space-r1075-refused.json', 'windows-cache-retire-r1075.json', 'guest-cache-retire-r1075.ps1',
             'windows-cache-inventory-r1075b.json', 'windows-layout-inventory-r1075.json', 'windows-cache-retire-r1075b.json',
             'guest-cache-retire-r1075b.ps1', 'windows-render-cache-retire-r1075c.json', 'guest-render-cache-retire-r1075c.ps1',
             'windows-batch-r1075-post-foreign.json', 'windows-pre-r1075postidle2.json'):
    files.add(temp / name)
flags = {'externalInventoryExportWorkflowQualified': True, 'newNativeAndReviewCasesPassedAllThree': 21,
         'broadSuitesPassedAllThree': True, 'physicalPickerAndFullExportResourceMatrixQualified': False,
         'humanCsvChartReportsImplemented': False, 'publicFeatureRegistered': False, 'finalIndependentReviewPerformed': False}
record.update({'purpose': 'Stream an entire captured inventory to a separately reviewed external create-new destination',
               'qualification': flags,
               'scope': 'Actual NTFS/APFS/ext4 files, bounded immutable inventory streams, native protection and failure ownership; physical picker, all scale/resource/process-death/power-loss and all human/CSV/chart report formats remain open.',
               'publicationCapabilities': {'windows': 'Local NTFS, private delete-on-close staging, native source-handle create-new rename',
                                           'ubuntu': 'Live-tested ext4, anonymous O_TMPFILE staging and held-descriptor create-new link; XFS is recognized by the native guard but not exercised by this cohort',
                                           'mac': 'Qualified APFS, anonymous private staging on the selected output volume and source-descriptor clone; cross-volume staging unavailable'},
               'replacement': 'Existing external output is preserved; a separate themed preserving review offers choosing a new name.'})
retain(root, record, files, runners, ['finalize-external-export.py', 'native_batch_evidence.py', 'prepare-history-workflow-batch.ps1',
       'add-app-settings-suite.ps1', 'add-unix-previews.ps1', 'add-windows-previews.ps1', 'pack-batch-direct.ps1',
       'deploy-batch.ps1', 'collect-batch-archive.ps1', 'preflight-indexed-r732.ps1'])
freeze_text(root, record, 'exact-text-r1075-external-export.tgz')
(out / manifest).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
progress = read(out / 'progress.json')
progress['validation']['externalInventoryExport'] = {'manifest': manifest, 'matrix': record['matrix'], **flags}
progress['completedBehaviors'].append('Whole-inventory external export now shares the bounded staged snapshot/indexed writer. Separate native output owners keep unpublished bytes private, publish create-new, preserve existing output and distinguish uncertain publication/release. Trusted native application-root identities block output into profile/scratch/history/journal/install artifacts. Folder picking, filename/sensitivity review and preserving existing-name review keep external exports separate from named/history receipts. Twenty-one new native/review controls and broad suites pass all three (r1075). Picker ownership now attempts every selected item release. Physical picker, all export resource/scale/death gates and human/CSV/chart formats remain open.')
progress['nextWork'] = ['W3: finish human reports, selected/full CSV, redaction/formula/markup and chart exports through the native create-new output boundary; complete comparison replacement or evidenced unavailable behavior. Whole-inventory external export now has scoped native evidence on all three (r1075). Full persistence/export/resource/physical-desktop qualification remains open.' if line.startswith('W3:') else line for line in progress['nextWork']]
(out / 'progress.json').write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
(out / 'EXTERNAL_EXPORT_QUALIFICATION.md').write_text('''# External whole-inventory export

The entire captured .fcdsa inventory streams through the existing bounded memory/indexed writer to a distinct native output capability. It ignores display filters explicitly and preserves partial coverage and privacy classifications. It grants no source authority. Export has its own receipt and never changes named-save or rolling-history state. This cohort covers the available-profile workflow; independent memory-fallback export eligibility remains a follow-up.

Windows uses a create-time private ACL and exclusive delete-on-close staging followed by a source-handle create-new rename on NTFS. Ubuntu uses private anonymous O_TMPFILE staging with a qualified descriptor publication route on ext4. XFS is recognized by the guard but was not exercised here. macOS clones anonymous private staging through a held source descriptor to a create-new APFS name; staging must be on the output volume. Filesystem/ownership/capacity and source/parent bindings are checked through native handles. Output bytes remain bounded to 8 GiB and the existing capacity reserve. Complete output must flush before publication. Post-publication binding/durability or release failures report uncertainty, and no failure authorizes replacement.

The application captures current native protected directory identities independently of inventory data. Every selected output ancestor is compared before staging against FileCat's state, scratch, named/history, journal, installation and configured write boundaries. Unsupported protection is refused. Existing output remains unchanged and gets a themed Keep existing / Choose new name review. The folder picker is separate from FileCat's filename and sensitivity review, with Cancel default, so no platform overwrite prompt is involved. Import and export release all returned picker items, including cancellation and a failed item release.

Twenty-one new controls pass on actual Windows/Mac/Ubuntu files: memory/indexed raw-name round trip; existing-byte preservation; five protected roots; before-flush/before-publish/after-publish/close/capacity/cancellation faults; redirected folders; partial/privileged-derived labels; narrow large-text preserving reviews; and all-item picker release. Each product preflight also reruns the preceding 22 import controls. Broad rendering totals 525, with one existing Unix case skipped on Windows. Core, native Windows filesystem and Settings counts/unchanged skips, sources, binary hashes and once-collected raw receipts are retained in [r1075 provenance](source-manifest-r1075-external-export.json). Every task-owned process has exited.

Windows initially postponed for another tester and refused transfer below its disk reserve. Completed older batches and explicitly enumerated old renderer payloads under this task's guest root were retired after checked containment, no-reparse inspection and an idle process check. Cleanup stopped when another tester started. The final renderer cleanup restored more than 8 GB available space and verified the current import baseline product hashes before and after. Current baselines and other testers' roots were retained. Another tester began after our completed runner and before its first post-inventory; that snapshot is retained, followed by a later clear inventory. No foreign process was stopped. That VM maintenance is separate from product qualification.

Synchronization is a best-effort native durability request, not a whole-machine power-loss guarantee; see Apple's [fsync contract](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/fsync.2.html). Physical picker/input/AT/theme/DPI/package, all maximum-scale/resource and death/power-loss gates remain open. APFS cross-volume staging is unavailable. Human, CSV and chart report formats remain unfinished. Public registration stays off and the final independent UX/UI/functional/security review has not been performed.
''', encoding='utf-8')
print(json.dumps({'manifest': manifest, 'newCasesPerPlatform': 21, 'fullPlanComplete': False}))
