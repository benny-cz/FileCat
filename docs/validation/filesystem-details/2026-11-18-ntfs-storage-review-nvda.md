# NTFS storage review with NVDA on Windows

Windows 11 x64 build 26340 ran the FileCat desktop interface as a standard user on a local NTFS volume. The isolated qualification host composed the Windows compression and EFS candidates. The app client measured 640 × 420 DIP at 96 DPI. NVDA 2026.3.1 used a fresh configuration. The fixture used one clear regular file in a disposable profile.

- **EFS review (N-UI.E).** The keyboard opened EFS protection from command search. The review opened without a chosen action. NVDA read the dialog and the unchecked pathname and all-stream decompression acknowledgments. Cancel remained visible. Accepting the pathname acknowledgment alone kept Encrypt and Queue disabled. Adding the all-stream approval enabled both. Switching to Decrypt cleared the pathname acknowledgment and replaced the compression acknowledgment with the unchecked warning about losing EFS protection. NVDA read the warning. Decrypt and Queue enabled only after the second required check. Escape returned focus and speech to the same file.
- **Compression review (N-UI.C).** The keyboard opened NTFS compression from command search. NVDA read the dialog and action choices. Selecting Compress and Remove compression exposed the matching enabled action and the Queue and Cancel buttons at the tested size. Escape again returned focus and speech to the file.

The fixture accepted the reviews up to the enabled footer, then dismissed them. It checked the file's SHA-256 and its clear compression and EFS attributes afterward.

Run `tests/FileCat.NativeAcceptance/win-nvda-storage-review.ps1` with a fresh profile-local validation directory containing the built `FileCat.ArchiveQualificationHost` app and a signed portable NVDA in the same user's validation directory. The Windows VM passed twice. The final receipt is `.tmp/filecat-storage-review-nvda-r2-result.json`. The qualification host was built with SDK 10.0.101 for `net10.0` from the changes on top of `f820198a`. Tested `FileCat.dll` SHA-256 is `7E3F0EEA86113524CB4E8C97A15BA816D26C1D326DBDAAAE8288ADBD30D85192`.

## EFS backup wizard cancellation

The same Windows VM passed `-VerifyCanceledKeyBackup` at 640 × 420 DIP and 96 DPI. This mode requires a pre-existing default fixture certificate with its private key. NVDA announced the pending decision, and **Review decision…** opened setup with focus on **Cancel operation**. The fixture accepted setup for the existing key, verified the displayed certificate against that key, and opened the real Windows export wizard parented to FileCat.

Before the fix, canceling the wizard moved focus to the Windows desktop. FileCat now invokes the modal wizard on the owner window's UI thread while the worker retains the acquired certificate. The passing run verified that Escape returned foreground focus to FileCat. NVDA read the next recovery decision, and the dialog explained that Windows canceled the backup and offered a retry. Canceling the operation then produced a terminal result of zero changed and one not processed. The selected file retained its contents and its clear compression and EFS attributes.

The passing receipt is `.tmp/filecat-storage-review-keycancel-r4-result.json`. The tested `FileCat.dll` SHA-256 is `5DC34C5E32E75EA4FF3222520DE2FFC43A6E5F62F41DE732BC1E01C315AE9495`. The tested `FileCat.Platform.Windows.dll` SHA-256 is `BEE4A6541B6799A32CB63EA9E10F33143D511EB7A7AB415D26785A6C22D00D4A`. The [completed export and file round trip](2026-11-19-efs-key-export-nvda.md) covers the successful wizard path with a software key. Managed and hardware keys remain separate native checks.

## Picture view with the High Contrast preset

On Windows 11 x64, `-HighContrastPictures` supplied FileCat's High Contrast preset, Comfortable density, and 24 DIP file-list and content fonts through the isolated app settings. The 640 × 420 DIP client used 96 DPI. The keyboard switched from the source list to the picture grid using command search before opening either storage review.

NVDA 2026.3.1 announced both reviews, the unchecked EFS acknowledgments, and the decryption warning after switching actions. The fixture checked visible footer buttons and their enabled states for both compression actions and both EFS actions. Escape returned focus and speech to the same file in the picture grid after each review. The file's SHA-256 and clear compression and EFS attributes matched the initial state. Windows system text scaling still needs a separate fixture.

The tested app and Windows platform assemblies are from `26bfcec30aec2715cc35621a74a79cde59e339b1`. The SHA-256 of `FileCat.dll` is `1B1660119E742BFD0063ACF2DF8E9B9348CCA482BFBDED5C4022BD97FEFE5C65`. The receipt `.tmp/filecat-efs-live-166b6897-r1/native-storage-contrast-picture-r1.json` has SHA-256 `21C0CBD8B06FDEE40927A1EF9A8A40EB1328D53D1E10DEC3DEA924B0954DD7E2`.

## Two original paths with the same filename

On the same Windows x64 VM, `-VerifyDuplicateRoots` imported a list of two local files that share a name and have different paths and payloads. The keyboard marked both imported results after listing completion. From the resulting tab, it opened the EFS and compression reviews at 640 × 420 DIP and 96 DPI.

In each review, the keyboard expanded **Selected items (2)** and focused the read-only text that carries the original locations. NVDA announced the text control. UI Automation returned both complete paths in its value. After Escape, focus returned to the imported results. Both files retained their original hashes and clear compression and EFS attributes.

The list-view receipt `native-storage-duplicate-roots-r1.json` has SHA-256 `276C989FF846CC8E28FD699C89FA81C508F19D3291AED9697C7C8FB783CFB4D2`. The tested SHA-256 values are `542744CD5FFD77E66FE43C90222F81E9F3DA329B76EF162AE5347C23D487C4C7` for `FileCat.dll` and `0D4B9B81C5EF5B8FC3E55E948FFCF47EB050F31AEEC9833DA5521436F0E060FA` for `FileCat.Platform.Windows.dll`.

Combining `-VerifyDuplicateRoots` with `-HighContrastPictures` initially restored keyboard focus after EFS review while NVDA announced only the results grid. On Windows, FileCat now uses the existing polite restored-row announcement when the modal releases focus to a results picture grid. It describes the focused tile and its original location. The repeated fixture passed both EFS and compression reviews with both paths available and the same original tile announced on return. NVDA also reported the native focus event in that run. Both original file hashes and attributes matched the baseline.

The combined receipt `native-storage-duplicate-roots-r3.json` has SHA-256 `53D84C61AC284F850F4BCA5A18E6E0F3BCB253857B9094A9EBBE3BD566CBAE89`. The tested SHA-256 of `FileCat.dll` is `DE59120E93EA8C1F876FBA333EC321AB61F668B4B1D76B6F3954673A07D05E5B`. The Windows platform assembly matched the list run. The focused dialog, filter, picture and storage-command tests on Apple silicon macOS passed all 36 cases.

The x64 VMware path passed again on the revised runner that also supports hosted ARM64. It used the same FileCat assembly as the Operations Stop fixture below. NVDA 2026.3.1 and Windows UI Automation verified both paths in each review in interactive session 1 at 640 × 420 DIP and 96 DPI. The receipt `native-storage-hosted-guard-x64-r1.json` has SHA-256 `1F126964D83A60105745809A868DFD8BDE72AF8C35FA4559E13007A9F25C7A3C`.

## Stopping and reopening storage history in Operations

On the Windows 11 x64 VM, the `-VerifyOperationsRecovery` fixture used a 640 × 420 DIP client at 96 DPI with the High Contrast preset, Comfortable density and 24 DIP in-app content and file-list fonts. It ran two separately approved compression jobs over two owned files. The qualification host held each successful native SET at a test barrier and withheld the verification result after Stop. This produced a retained uncertain result through the real job and journal path.

While the first call owned the storage resource, the keyboard reached **Cancel preparation and show the blocking operation** in a second review. NVDA read the control, and activating it dismissed the review and focused the exact running job. NVDA announced that an admitted call finishes and completed changes remain when it read **Stop**. The job displayed the stopping state before the fixture released the barrier.

Ctrl+J from the file pane now focuses the selected pending storage review after any waiting job decision. In both stopped-operation cases, NVDA read the focused review action and the drawer displayed **Result not confirmed**. The fixture acknowledged the first history and restarted FileCat. It verified the old acknowledgment against its journal, submitted and stopped the second operation, and used Enter on the reviewed row to open the older retained uncertainty. The newer pending review stayed available. Both files kept their complete hashes and their observed compressed state.

The passing receipt `native-operations-stop-r14.json` has SHA-256 `1A90DE9DE963C17497C77C8450ADD092BDC32C2FE753B1C35DCEFFCF87CC1905`. The tested `FileCat.dll` SHA-256 is `24042895EE6888B59F0FB82CB3FD79C8171D6A51C9EA7BA53628D5A30F18EE56`. The focused Mac Operations and archive-recovery selection passed all 81 cases. The separate focused rerun with the keyboard routing regression passed all 27 cases.

## Already-satisfied outcomes in Operations

On the same Windows x64 VM, `-VerifyAlreadySatisfied` used Enter to submit Decrypt and Remove compression from their reviewed footers. Both jobs targeted the same clear NTFS file. At 640 × 420 DIP and 96 DPI, Operations showed a completed result for each action with **0 changed, 1 already satisfied** and every remaining count at zero. The main and named data streams kept their original bytes. Both journals ended in `completed` with a root and summary record and zero native mutation intents.

The run used the same FileCat assembly as the Operations Stop fixture. Under `.tmp/filecat-efs-live-166b6897-r1`, `native-storage-already-satisfied-r1.json` has SHA-256 `00636C39420F1E699F4D2FDC8E15CAFC3124971A5D5709CFBC5B6897DA455C7D` and the independent journal check `native-storage-already-satisfied-r1-journals.json` has SHA-256 `7A79E3A36DF3369A1D678AB37AA22A7B2DE595B68BEB71C586C7D0A8212FFC41`.

## Windows system text size at 150%

The Windows x64 VM repeated the High Contrast picture-grid and two-original-path fixtures with Windows Accessibility text size at 150%. The 640 × 420 DIP client at 96 monitor DPI exposed the action and scope controls, the EFS acknowledgments and the enabled footer. The keyboard reached the exact two captured paths in both reviews and focus returned to the original file on dismissal. The files retained their bytes and clear storage attributes. The Windows test controller measured a WinRT text scale factor of 1.5, then restored and measured the original factor of 1.0 after both UI fixtures.

The run used the same FileCat assembly as the Operations Stop fixture and the same Windows platform assembly as the list-view two-path fixture. Under `.tmp/filecat-efs-live-166b6897-r1`, `native-text-scale-storage-review-result.json` has SHA-256 `1B00071BC14A513C7A5477837E24B6E95F4507227DF4B0A7ACD49EDE2FB7A67B`. See the [details fixture and controller receipt](2026-11-18-windows-details-nvda.md#windows-system-text-size-at-150).

## Windows display scale at 150%

At 144 monitor DPI, NVDA passed the compression and EFS reviews in a 640 × 420 DIP client with the High Contrast preset and 24 DIP in-app content and list fonts. The keyboard reached the two complete original locations in each review and the appropriate action and consent controls. Dismissal restored speech and focus to the picture grid, and both originals retained their hashes and storage attributes. The display scale and resolution were restored after the journey. [Details fixture and restoration receipts](2026-11-18-windows-details-nvda.md#windows-display-scale-at-150)

The run used the same FileCat and Windows assemblies as the system text-size fixture. Under `.tmp/filecat-efs-live-166b6897-r1`, `native-display-scale-r6-storage-review-result.json` has SHA-256 `A71B8AEF825C2B9B9089FF4CCA9D42996786DC1CEE835E2A530E6016937FCA78`.
