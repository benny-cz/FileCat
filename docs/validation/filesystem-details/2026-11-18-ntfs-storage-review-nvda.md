# NTFS storage review with NVDA on Windows

Windows 11 x64 build 26340 ran the FileCat desktop interface as a standard user on a local NTFS volume. The isolated qualification host composed the Windows compression and EFS candidates. The app client measured 640 × 420 DIP at 96 DPI. NVDA 2026.3.1 used a fresh configuration. The fixture used one clear regular file in a disposable profile.

- **EFS review (N-UI.E).** The keyboard opened EFS protection from command search. The review opened without a chosen action. NVDA read the dialog and the unchecked pathname and all-stream decompression acknowledgments. Cancel remained visible. Accepting the pathname acknowledgment alone kept Encrypt and Queue disabled. Adding the all-stream approval enabled both. Switching to Decrypt cleared the pathname acknowledgment and replaced the compression acknowledgment with the unchecked warning about losing EFS protection. NVDA read the warning. Decrypt and Queue enabled only after the second required check. Escape returned focus and speech to the same file.
- **Compression review (N-UI.C).** The keyboard opened NTFS compression from command search. NVDA read the dialog and action choices. Selecting Compress and Remove compression exposed the matching enabled action and the Queue and Cancel buttons at the tested size. Escape again returned focus and speech to the file.

The fixture accepted the reviews up to the enabled footer, then dismissed them. It checked the file's SHA-256 and its clear compression and EFS attributes afterward. The broader native UI gate still includes high text scale, same-name captured roots, picture view, High Contrast, EFS key setup and Operations recovery.

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
