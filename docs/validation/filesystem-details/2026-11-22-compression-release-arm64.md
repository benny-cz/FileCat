# NTFS compression on Windows on Arm — 22 November 2026

The [ARM64 gates receipt](2026-11-21-compression-arm64-gates.md) ran the C fixtures natively on the hosted Windows on Arm runner, but compression stayed off there for two reasons. N-UI needed a keyboard and screen-reader review, and the hot-plug refusal needed a USB disk. Both now pass on a Windows 11 on Arm VM, and the [screen-reader receipt](2026-11-22-windows-arm64-screen-readers.md) covers the reviews. FileCat's release build now composes NTFS compression for a native Arm64 process.

## Release composition

`WindowsStorageTransforms.IsReleaseHost` accepts a native x64 or a native Arm64 Windows 11 client process. On native Arm64, the released adapter, evidence backend and journal factory are the Arm64 variants that the qualification fixtures and the hosted runner exercised. The release still needs normal integrity, and EFS stays unoffered: the released adapter refuses both EFS actions.

| Host | Reason code | Text |
|---|---|---|
| Administrator (high-integrity) process | `elevated-process` | Open FileCat as your usual Windows user to change NTFS compression; an administrator window does not change it. |
| An x64 copy running emulated on Windows on Arm | `arm64-emulated` | On Windows on Arm, NTFS compression changes need the Arm64 build of FileCat; this copy runs emulated. |
| Windows Server or Windows 10 | `windows-version` | NTFS compression changes in FileCat require Windows 11 (not Windows Server or Windows 10). |
| Any other host | `target-unqualified` | NTFS compression changes are not qualified on this system. |

The `arm64-unqualified` reason is gone. The two Windows tests that pinned the old decision now expect native Arm64 to be a release host, and the emulated case to get `arm64-emulated`. The App's private storage history test, which skipped everywhere but x64, now runs on any release host.

## Runs on the Windows on Arm VM

**Environment:** the guest from the screen-reader receipt: Windows 11 Pro 26H2 on Arm, build 26300.9457, under UTM on the Apple silicon test Mac. Every run was in the signed-in desktop at medium integrity.

**Build:** self-contained win-arm64 Release builds of this change: `FileCat.dll` `549A7D18…` and `FileCat.Platform.Windows.dll` `9F177ADF…` in the app and every test package.

| Run | Scope | Result | Evidence |
|---|---|---|---|
| Release build with NVDA (N-UI.C) | [`win-nvda-compression-release.ps1`](../../../tests/FileCat.NativeAcceptance/win-nvda-compression-release.ps1), as on x64: folder scope by keyboard, both actions, EFS absent, result text and focus return at a 640 × 420 DIP client and 96 DPI | Passed | [result](receipts/2026-11-22-compression-release-arm64/nvda-release-result.json), [NVDA speech](receipts/2026-11-22-compression-release-arm64/nvda-release-speech.log) |
| Hot-plug refusal (T-CAP.C, N-C2) | `A_hot_plug_disk_that_reports_fixed_media_is_refused_at_review` with the Arm64 opt-in, once for a folder and once for a file on the USB disk | 1 of 1 passed in each | [folder TRX](receipts/2026-11-22-compression-release-arm64/hotplug-folder.trx), [file TRX](receipts/2026-11-22-compression-release-arm64/hotplug-file.trx), [disk record](receipts/2026-11-22-compression-release-arm64/usb-disk.json) |
| Release composition and admission | `WindowsStorageAdmissionTests` and `WindowsStorageInspectionTests`, without the Arm64 opt-in | 38 total, 20 passed, 18 skipped: fixture cases that need prepared volumes or the opt-in | [TRX](receipts/2026-11-22-compression-release-arm64/platform-admission-inspection.trx), [log](receipts/2026-11-22-compression-release-arm64/platform-admission-inspection.log) |
| App storage commands | `StorageCommandIntegrationTests` and `StorageCompressionDialogTests`, with the default composition | 60 of 60 passed | [TRX](receipts/2026-11-22-compression-release-arm64/app-storage-commands.trx), [log](receipts/2026-11-22-compression-release-arm64/app-storage-commands.log) |
| Storage history and Operations | `StorageHistoryStartupUiTests`, which now runs on any release host, with `StorageOperationsUiTests`, `InterruptedOperationUiTests`, `OperationScopeTests` and `OperationReportTests`. Test DLL `9768FC00…` | 43 total, 42 passed, 1 EFS opt-in fixture skipped. All three history cases passed: Compress, Encrypt with EFS and Decrypt EFS history acknowledged, with its summary kept across a restart | [TRX](receipts/2026-11-22-compression-release-arm64/app-history-operations.trx), [log](receipts/2026-11-22-compression-release-arm64/app-history-operations.log) |

**Release build with NVDA:** the results match the x64 release run.

- **No EFS:** the main window and both reviews exposed no EFS control. Searching the command palette for "EFS protection" and "Back up EFS key" offered neither.
- **Compress:** "2 changed, 0 already satisfied, 1 skipped, 0 failed, 0 uncertain, 0 not processed. The requested scope was not fully completed." The folder and its writable file were compressed. The read-only file stayed uncompressed, and the sibling was untouched.
- **Remove compression:** "2 changed, 1 already satisfied, 0 skipped, 0 failed, 0 uncertain, 0 not processed. The requested scope was fully checked." The payload hashes and the named stream were unchanged, and the read-only attribute stayed set.
- **No progress figures:** neither result nor its operation text contained a percentage, rate or time estimate.
- **Focus return:** after each review FileCat returned focus to the reviewed folder by itself, and NVDA announced it.

**Hot-plug refusal:** UTM attached a 2 GiB disk image as QEMU's USB mass-storage device with fixed media. Windows listed it as "QEMU QEMU HARDDISK" on bus type USB. Its NTFS volume was a fixed drive with 4 KiB units. FileCat read the attachment as hot-plug (bus type 7, not removable media). Review refused both Compress and Remove compression with `hot-plug-unqualified` before any job or journal existed.

**Composition tests:** these tests compare against the host check, so they pass on any host. Here they ran in an Arm64 process at medium integrity ([integrity record](receipts/2026-11-22-compression-release-arm64/integrity.txt)), where the host check holds. So `The_release_composition_is_compression_only_and_needs_the_qualified_normal_process` took its qualified branch: the native adapter, both compression actions available, and both EFS actions refused with `efs-unqualified`. In the App, `The_default_composition_offers_compression_without_EFS_only_where_private_evidence_is_qualified` required the compression command to be enabled, and neither EFS command was advertised. The release-build NVDA run is the direct evidence that the shipped build offers and performs compression on Arm64.

## C gates on Windows on Arm

| Gate | Windows on Arm evidence |
|---|---|
| T-UI, N-UI | Native keyboard and NVDA reviews of the qualification host: default, High Contrast picture grid, two same-name roots, Operations recovery, already-satisfied, 150% text size and 144 DPI ([screen-reader receipt](2026-11-22-windows-arm64-screen-readers.md#storage-reviews-n-uic)). The release-build run above. The headless review fixtures in CI. |
| T-CAP | The hot-plug refusal above. The reason matrix, system-volume binding, the no-filter volume and the cross-process Cloud Files refusal on the hosted runner. |
| N-C2 | The hot-plug refusal above. Capacity, small clusters, mount points and the five device-loss phases on the hosted runner. |
| T-OUT | The held-postcondition outcomes, read-only, sparse and denied-folder cases, and removal on an EFS file without a native call ran natively in the [hosted gates run](2026-11-21-compression-arm64-gates.md). |
| T-TRAV, T-LIFE, T-REC, T-LIMIT, T-REFRESH, T-REG, N-ID | Unchanged: the [ARM64 gates receipt](2026-11-21-compression-arm64-gates.md#c-gates-on-windows-on-arm). |
| T-CONTRACT, N-C1, N-C3 | Already passed natively on ARM64; see their plan rows. |

## Limits

- The USB disk is QEMU's emulation, not a physical USB device. Windows' own USB storage stack reported it, so FileCat's refusal used the same query that a physical USB disk answers.
- One Windows on Arm guest, not physical Windows on Arm hardware.
- Compression on Windows on Arm needs the Arm64 build. An x64 copy running emulated gets `arm64-emulated`.
- EFS stays unoffered on every platform, and the optional categories in the [admission receipt](2026-11-21-storage-volume-admission.md#limits) stay refused, including removable and hot-plug drives.
