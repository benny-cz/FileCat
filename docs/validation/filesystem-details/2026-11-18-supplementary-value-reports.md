# Selected supplementary value reports

The Windows supplementary inventory offers **Report selected value…** for one focused, bound ADS or EA. The report acquires an excerpt of up to 4,096 bytes from that exact category and name. It checks the actual opened file against the inventory identity before acquiring values and verifies that the observed source revision still agrees afterward. Provenance and text stay together after the native handles close. The native report checks use the same byte ordering for the retained Windows file ID and the inventory identity.

EFS or unknown input protection refuses acquisition through this report route. An unverified or protected report revision also refuses file and clipboard output, including whole, selected, visible and displayed variants. The report window cancels a pending picker or clipboard publication when its captured revision is retired. Clear reports can be saved to a new local file. An existing target is preserved, including a target using EFS. If a save is interrupted after creating the file, the report identifies the possible incomplete output. The ordinary file-system details summary and full record keep the metadata-only acquisition path.

The Windows 11 x64 VM passed 14 focused native tests and 15 focused Avalonia tests at integrity 8192. Native ADS and EA fixtures generated reports from the reviewed file, replaced the pathname and verified that a fresh read using the previous identity refused. The captured report retained its original contents. The application activated the supplementary header action for an ordinary stream and for an EFS-protected stream. The clear report displayed its excerpt. The protected case showed the EFS reason before exposing the value and left the copy action disabled.

The application tests also exercised encrypted and unknown report receipts, cancellation after opening a picker and before writing, and refusal to replace a genuine EFS destination. The existing file retained its bytes and encryption. A subsequent new-file save and clipboard copy contained the new report revision. See `.tmp/filecat-supplementary-value-reports-native-tests.xml` and `.tmp/filecat-supplementary-value-reports-app-tests.xml`. This is selected-value evidence for E-OUT15 and F5. Broader native destinations, actual interruption after a write has begun, and independently bound adapters on other platforms remain separate qualification work.

In the [16-case native x64 and ARM64 follow-up](2026-11-18-windows-enumeration-scale.md#cancellation-at-a-published-batch), an ADS and an EA each produced a bound report on the clear file. After encryption, the same reviewed identity refused both report acquisition and streamed extraction, and the ordinary viewer still read the exact value. A separate fixture changed EFS protection after the first streamed read and verified refusal before another byte was returned.

## Linux selected xattr

The same focused-row action is available for Linux xattrs. FileCat opens the inode through the existing checked procfs descriptor route, compares it with the inventory identity and samples the opened inode revision around `fgetxattr`. A report retains one identity and revision and includes up to 4,096 bytes of value preview. File output uses a new local path.

On the Ubuntu 24.04 x64 VM, nine focused Core tests passed with one Darwin-only skip, and eight App tests passed with one Windows-only EFS skip. The native replacement fixture wrote the same xattr on a different inode at the original path. A request using the previous inventory identity refused, and the report already acquired from the original inode retained its original value. The application opened the report from the supplementary header for a real xattr. The VM receipts are `linux-value-reports-core.xml` and `linux-value-reports-app.xml` under `~/FileCatValidation/details-92d52654.zR6HolZy/`. Darwin value reports still require a qualified bound adapter.

The Apple silicon Mac passed the corresponding eight App tests, with the Windows-only EFS case skipped. The Mac result is `~/FileCatValidation/ci-fix-3877f7ef/tests/FileCat.App.Tests/TestResults/mac-value-reports-fallback.trx`.

## Metadata-only report save on a full Linux volume

Ubuntu x64 and Apple silicon each passed seven focused report and details window tests. These covered saving metadata to a new file, replacing a clear file, and the existing selected-value output controls. The encrypted-destination and full-volume opt-ins were skipped in the ordinary runs.

A separate run mounted a private 64 KiB `tmpfs` and saved a metadata report larger than the mount. The application reported incomplete output. The test verified that a nonempty partial file remained on disk. The volume and all report data belonged to the fixture.

The Ubuntu x64 follow-up used 64 KiB writes on the same volume size and verified the UTF-8 byte-order mark and an exact prefix of the captured report. The focused run passed 18 tests with two Windows-only skips, including an explicit pass for the full-volume test. Its receipt is `.tmp/filecat-linux-report-space-de609ce9-r2-app.trx`. The fixture unmounted the volume.

See `app.trx` and `full-volume.trx` under `~/filecat-metadata-report-4f7257ee` on the Ubuntu VM, and `app.trx` under `~/FileCatValidation/filecat-metadata-report-4f7257ee` on the Mac.

The newer Ubuntu x64 and Apple silicon runs covered an open save picker in the metadata-only report. Refresh retired the previous observation and canceled its pending save. Returning a destination from that picker created no file. A fresh picker wrote the refreshed report in UTF-8 with a byte-order mark, and replacing a longer clear file preserved only the new report text. Each focused App run passed 24 tests with two environment-specific skips. The receipts are `~/filecat-details-output-d2ed94ec-r2/app.trx` on Ubuntu and `~/FileCatValidation/filecat-details-output-d2ed94ec-mac-r1/app.trx` on the Mac.

A later Apple silicon run supplied full observations classified as payload and unknown to the canonical metadata-only route. Both requests retained the prior summary and showed the refusal. The displayed, copied and saved text stayed free of the fixture's payload canary. The selection passed 12 tests with two environment-specific skips. Its receipt is `~/FileCatValidation/filecat-unqualified-details-8eeeca7e-r1/report-content.trx`.

The Ubuntu x64 follow-up included the same two refusal cases, the stale picker check and the portable EFS completion and Operations fixtures. It passed 55 tests with two environment-specific skips. Its receipt is `~/filecat-details-publish-8eeeca7e-r1/app.trx`.

## Metadata-only report save on a full APFS volume

An Apple silicon fixture used a private 201,285,632-byte APFS image with 5 MiB available. Its Unicode metadata report exceeded the remaining space while fitting the viewer's character limit. The previous large write left only the UTF-8 byte-order mark. The 64 KiB writes left the mark and an exact prefix containing report text, and FileCat identified the file as possibly incomplete.

The focused run passed 18 tests with two Windows-only skips. See `.tmp/filecat-apfs-report-space-de609ce9-r5-app.trx` and `~/FileCatValidation/filecat-apfs-report-space-de609ce9-r5/results.log`. The private image was ejected.

## NTFS metadata-only report save

On Windows 11 x64 at medium integrity, the metadata-only details window replaced an existing clear NTFS file with exactly the current report in UTF-8 with a byte-order mark. The replacement used the held file identity and a native file transaction. A separate fixture chose an actual EFS destination and verified its encryption and original bytes survived refusal. The report suite also passed the captured-revision, unverified-payload and selected-value cases. The individual passes are in `.tmp/filecat-metadata-report-output-8eeeca7e-r6.xml`.

The Windows writer checks cancellation between 64 KiB chunks. A follow-up used a private, 66,973,696-byte NTFS volume with 1,044,480 bytes available. A larger metadata-only report saved to a new name left the byte-order mark and an exact prefix of the captured report. FileCat showed that the file could be incomplete. Saving the same report over an existing clear destination failed and preserved its original bytes and last-write time. The prepared EFS destination also passed its independent preservation test. The focused run passed 16 tests with four Unix-only skips. See `.tmp/filecat-report-space-app-de609ce9-r2.xml` and `.tmp/filecat-report-space-prepare-de609ce9-r2.log`. The private image was detached.

### Volume loss after an NTFS report replacement

After a successful Windows commit, the writer reopens the current name for metadata, confirms its original file identity and checks its protection. The details window shows a verification warning if that final check fails, including guidance to inspect the destination before retrying.

On Windows 11 x64 at medium integrity, the controller detached a private 256 MiB NTFS volume after the report replacement committed and before the application could verify the destination. The production writer returned the committed-but-unverified warning. After remount the same file identity held the complete captured UTF-8 report and its byte-order mark. The file remained clear and the folder contained only the fixture canary and the report. The controller detached the image after verification. See `.tmp/filecat-device-loss-4cb1239bb08d448aad225c6e2c56aee2-*`.

### Volume loss before an NTFS report replacement commits

The Windows 11 x64 VM detached a fresh 256 MiB 4 KiB NTFS volume after the production writer wrote and flushed the replacement inside the Windows transaction. It detached before the commit call. The native commit returned an error, and FileCat reported that it could not confirm the replacement. After remount the original clear file matched its prior identity and exact bytes. The folder contained only the original report and the fixture canary. This checks the precommit boundary. Loss inside the native call and other destination types require separate qualification.

Both the precommit case and the postcommit regression passed without skips on build 26340 with .NET 10.0.15 at integrity RID 8192. Each test confirmed loss of access after detachment and verified the result after remount. Both images were detached at teardown. The source was `59da8ae5` plus the report test seam and fixture changes. See `.tmp/filecat-report-commit-boundaries-59da8ae5-r1-{BeforeCommit,AfterCommit}-test.xml` and the matching controller logs. The test bundle SHA-256 was `E532D632DC50FA532E5F471EF40669CBA6C96F7B28657258D278335D1F80C39D`.

The focused details and report-window selection passed 10 cases on Windows at medium integrity, including refusal to overwrite the prepared EFS destination. It passed 12 cases on Ubuntu. Five and three platform or fixture cases were skipped, respectively. See `.tmp/filecat-report-device-loss-f7e1253c-r1-app.xml` and `~/FileCatValidation/filecat-report-device-loss-f7e1253c-r1/ubuntu-app.xml`.

### Native ARM64 report publication

On Windows 11 Enterprise ARM64 build 26200, the eight App fixtures executed at medium integrity on 4 KiB NTFS and passed without skips. The metadata-only details report saved to a new path, replaced a clear destination, preserved an actual EFS target, and wrote nothing from a retired picker. Protected and unknown report revisions reached neither file output nor the clipboard. The canonical details route retained the accepted observation when the full result carried unqualified content. The `native-efs-report-output.trx` SHA-256 was `600BF957E213663CC423F92CBF8C0DB70F83CA3925AD895342DA404944E4161A`. [Native ARM64 qualification run](https://github.com/benny-cz/FileCat/actions/runs/44582510202)

## Other report-text destinations, 21 November 2026

The generic File list and other non-supplementary `ReportWindow` Save route now uses the same Windows held-destination guard as metadata-only Details. `AdvancedTextFileExchange` uses that guard for File list and settings text exports without adding a UTF-8 byte-order mark. A new text export remains atomic; replacement on an EFS-capable NTFS volume uses the native transaction. The selected-value report still has its separate source-provenance refusal and new-file-only rule. The focused App selection passed 11 tests with five prepared-fixture skips and no failures on the Windows x64 workstation. The newly added actual-EFS destination assertions were among the skipped opt-in cases, so this is source and ordinary-output verification, not native EFS qualification.

`OperationReport.SaveAtomic` also publishes through a Windows destination guard. It still streams retained issues and command tails into a sibling temporary file, then copies into a held clear NTFS destination inside a transaction or atomically renames on a volume without EFS support. A postcommit verification failure reaches the Operations status message. Its focused App selection passed seven tests, with one opt-in actual-EFS target case skipped. The general and operation-report guards preserve an existing protected destination; they do not establish the protection provenance of arbitrary command standard output/error included in a report. The actual-EFS replacement, interruption and broader destination cases remain open.

**Custody note (2026-11-26):** the raw files this record cites on the Ubuntu VM were removed by the release session's cleanup on 2026-11-26, and no snapshot of the VM holds them. The results, counts and hashes above are as they were recorded. See [E-CUSTODY-20261126-ubuntu-cleanup](../../release/1.0.0/evidence/E-CUSTODY-20261126-ubuntu-cleanup.md).
