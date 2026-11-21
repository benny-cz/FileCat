# File-system details gap fixtures — 21 November 2026

This checkpoint adds fixtures for open items in the File-system details ledger (F-TYPE, F-PARSER, F-OPS, F-OBS, F-LIFE, F-UI, F-EXPORT, F-META, F-ENUM, N-LINUX, N-MAC, N-WIN). It also re-runs the new storage lifecycle tests on Linux and macOS. No product behavior changed, apart from three test-only seams.

## Deterministic fixtures

All are in source commits `b99a6f18f` and `31b473a30`; the wording fix for Linux is in `a8245c46e`.

- **F-OPS** (`ReparseOperationPolicyTests`): one corpus of Link-flagged and raw-reparse entries with tags 0, symlink, mount point, Cloud Files (with a subtag), WOF, app execution alias and unknown surrogate and non-surrogate tags. The corpus runs through:
  - TreeCompare recursion and one-sided folder contents
  - the Sync mirror and update plans
  - `ListingModel` unsized-folder marking
  - recursive file-list export
  - stream-transfer admission at root and child level (a real job)
  - Find descent

  In every consumer the decision follows the Link flag alone while the presentation differs.

  Not covered here: native edit snapshots, the main window's size requests, SFTP jobs, and native copy/delete. Each keeps its existing fixtures.
- **F-OBS / F-LIFE:**
  - Counting fakes show that the summary never calls the legacy reader, value reads or the supplementary inventory. This holds across opening, refresh and tier switches, including after the supplementary tab inventoried and refreshed.
  - A real reader never starts the legacy value acquisition on either tier.
  - A refresh storm (button, F5, Ctrl+R) keeps one read running and one pending, and never publishes a superseded result.
  - Closing the window, or disposing the reader, during a held read publishes nothing and returns the device permit once, after the native call returns.
- **F-PARSER:**
  - Reparse name offsets that are odd or out of range inside a full-length buffer are refused.
  - The 16 KiB payload cap holds for every decoder.
  - macOS packed results cut inside a 64-bit field or a time slot produce no value, and a known zero needs both the returned bit and the whole slot.
  - decmpfs values shorter than 16 bytes make no claim.
- **F-TYPE:**
  - `L` and `<LINK>` appear only for confirmed links; unclassified reparse folders show `P` and `<DIR>`.
  - WSL FIFO, character and block tags, and Cloud Files subtags, share one label across tiles, speech and letters.
  - A recorded render pass shows the shortcut arrow drawn only for symlink, mount, provider link and `.lnk` items, in list and picture modes.
- **F-UI:**
  - With other rows marked, details describe only the focused item.
  - The Find result menu and the panel menu each open their own item.
  - An archive member and a remote item show the accessible reason in Find and in the main menu, with no reader or provider call.
  - With no file-records adapter, details show the reason and open nothing; the Unix Properties fallback opens a summary-only window.
- **F-EXPORT:** a new read, a cached tier switch, or closing the window cancels a pending Save before it writes, and a pending copy of all text, the source or the displayed text before it is published. A real select-all-and-copy is refused or published exactly like the other copy variants.
- **F-META:** partial (`2+`) and not-inspected-zero values in the Extra data and Hard links columns sort last in both directions; exact values keep numeric order.

## Native fixtures

- **Missing, deleted and denied subjects** (source commit `33d10fa44`): Summary and Full on Linux, macOS and Windows.
  - A missing or deleted subject throws `FileNotFoundException`; a denied one throws `UnauthorizedAccessException`. No snapshot is produced.
  - The closed folder itself is still described.
  - On Windows the deny entry must also cover the folder's listing, because NTFS grants read-attributes on a child of a listable folder.
  - An elevated process read the Windows fixture's metadata despite the deny entries. The denied part therefore runs in a normal-integrity process; it passed there on the Windows 11 host.
- **Special objects:** `/dev/null` and `/dev/zero` are described as character devices from metadata, with no extent section, on Linux and macOS. A macOS FIFO is described without opening it or waiting for a writer.
- **F-ENUM parity** (source commit `04315d4a8`): on the Windows 11 x64 host, the native and managed listings agreed on container detection, the system drive root (names, kinds and attributes), case-distinct names (`a`, `A`, `ß`, `SS`) and a 255-unit name. The unlistable-folder, compatibility-junction and prepared-volume cases need a normal-integrity process or prepared volumes; they run in the Windows VM lane.
- **No per-entry opens** (source commit `b62a5122b`): a temporary Cloud Files provider counts open notifications for its 40 placeholders while both routes list the sync root. A metadata-only open is the positive control. This also runs in the Windows VM lane.

## Results

| Platform | Run | Result |
|---|---|---|
| Ubuntu 24.04.5 x64, .NET 10.0.14 | Core details, storage lifecycle and subject classes (source commit `3e4c1e4a9`) | 238 total, 0 failed, 22 skipped. [TRX](receipts/2026-11-21-details-gap-fixtures/core-storage-and-subjects-ubuntu.trx) |
| macOS 27.2.1 Apple silicon, .NET 10.0.12 | same | 238 total, 0 failed, 23 skipped. [TRX](receipts/2026-11-21-details-gap-fixtures/core-storage-and-subjects-mac.trx) |
| Ubuntu x64 | Core details classes with the agent fixtures (source commit `31b473a30`) | 164 total, 0 failed, 23 skipped. [TRX](receipts/2026-11-21-details-gap-fixtures/core-details-ubuntu.trx) |
| Apple silicon | same | 164 total, 0 failed, 24 skipped. [TRX](receipts/2026-11-21-details-gap-fixtures/core-details-mac.trx) |
| Ubuntu x64 | App details, presentation and storage UI classes (source commit `31b473a30`) | 206 total, 1 failed, 41 skipped. [TRX](receipts/2026-11-21-details-gap-fixtures/app-details-ubuntu.trx). The failure asserted the Windows and macOS wording of the omission notice; the Linux full record names "general supplementary-data values". Commit `a8245c46e` asserts the shared phrase. |
| Apple silicon | same | 206 total, 0 failed, 41 skipped. [TRX](receipts/2026-11-21-details-gap-fixtures/app-details-mac.trx) |

The skips are platform-specific (Windows-only fixtures on Unix, and the other Unix system's cases) or opt-in fixtures. On the Windows 11 x64 host, the agent's targeted runs passed:

- Core: 98 tests, 0 failed, 7 skipped.
- App, at medium integrity: 115 tests, 0 failed, 8 skipped.
