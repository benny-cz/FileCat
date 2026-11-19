# Selected supplementary value reports

The Windows supplementary inventory offers **Report selected value…** for one focused, bound ADS or EA. The report acquires an excerpt of up to 4,096 bytes from that exact category and name. It checks the actual opened file against the inventory identity before acquiring values and verifies that the observed source revision still agrees afterward. Provenance and text stay together after the native handles close. The native report checks use the same byte ordering for the retained Windows file ID and the inventory identity.

EFS or unknown input protection refuses acquisition through this report route. An unverified or protected report revision also refuses file and clipboard output, including whole, selected, visible and displayed variants. The report window cancels a pending picker or clipboard publication when its captured revision is retired. Clear reports can be saved to a new local file. An existing target is preserved, including a target using EFS. If a save is interrupted after creating the file, the report identifies the possible incomplete output. The ordinary file-system details summary and full record keep the metadata-only acquisition path.

The Windows 11 x64 VM passed 14 focused native tests and 15 focused Avalonia tests at integrity 8192. Native ADS and EA fixtures generated reports from the reviewed file, replaced the pathname and verified that a fresh read using the previous identity refused. The captured report retained its original contents. The application activated the supplementary header action for an ordinary stream and for an EFS-protected stream. The clear report displayed its excerpt. The protected case showed the EFS reason before exposing the value and left the copy action disabled.

The application tests also exercised encrypted and unknown report receipts, cancellation after opening a picker and before writing, and refusal to replace a genuine EFS destination. The existing file retained its bytes and encryption. A subsequent new-file save and clipboard copy contained the new report revision. See `.tmp/filecat-supplementary-value-reports-native-tests.xml` and `.tmp/filecat-supplementary-value-reports-app-tests.xml`. This is selected-value evidence for E-OUT15 and F5. Broader native destinations, actual interruption after a write has begun, and independently bound adapters on other platforms remain separate qualification work.

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

The Windows writer checks cancellation between 64 KiB chunks. A follow-up used a private, 66,973,696-byte NTFS volume with 1,044,480 bytes available. A larger metadata-only report saved to a new name left the byte-order mark and an exact prefix of the captured report. FileCat showed that the file could be incomplete. Saving the same report over an existing clear destination failed and preserved its original bytes and last-write time. The prepared EFS destination also passed its independent preservation test. The focused run passed 16 tests with four Unix-only skips. See `.tmp/filecat-report-space-app-de609ce9-r2.xml` and `.tmp/filecat-report-space-prepare-de609ce9-r2.log`. The private image was detached. Other destination types and interruption by device loss have separate qualification.
