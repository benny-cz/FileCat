# Windows native details fixtures — 21 November 2026

These fixtures close Windows-native cases in four File-system details ledger items: F-EXTRA, F-SCOPE, F-FIND and F-META. They add tests only.

**Source commits:**

- `7f6936a45` F-EXTRA
- `f19757295` F-SCOPE
- `0378c5a51` F-FIND
- `da3735d74` F-META
- `fa397e273` comment wording

They are cherry-picks of the commits that were built and run.

**Follow-up commit:** adds the Find help note, the deterministic column-sort fixture and the cleanup fix described under the findings.

## Fixtures

**F-EXTRA** (`WindowsHiddenDataTests`)

- Streams-only actions and Find issue no EA query. A deny entry for reading EAs is the native proof; the EA-reading request is the control and must fail.
- Find and stream lookup use only the names-only inventory. A counting wrapper would catch a fallback to the old combined list.
- Empty streams on files and folders are listed, read, found and deleted.
- Native names round-trip exactly, or the whole category refuses (for example, a stream name with an unpaired surrogate).
- The stream inventory grows its buffer from 64 KiB to 256 KiB and stops with a typed result at the entry limit.

**F-SCOPE** (`FileDetailsWindowsNativeTests`, App, 11 cases)

"Open target location" is offered and acts only on explicit activation, after a fresh read confirms the stored target and the source identity. The cases:

- a junction to a folder;
- a file symlink whose target is held with `FileShare.None` while it opens;
- a junction retargeted in place (same identity) and one replaced by a new object with the same stored target;
- a missing target, and a target that is itself a link;
- a relative link under a junction parent;
- a forged print name;
- network, device and pipe targets, labelled and not opened;
- WSL, app-execution-alias and unknown reparse tags, which offer neither Open nor Copy.

**F-FIND** (`NativeSearchAttributeTests`)

- Compressed and EFS predicates match listed files that refuse every open, so they read the listing and open no file.
- A change made through one hard link is found exactly as each folder's listing reports it.
- A folder that vanishes after its parent was listed, or that refuses listing, is logged, offered for retry where it applies, and leaves the attribute search incomplete.

**F-META** (`WindowsDetailsNativeGapTests` and `FileDetailsColumnTests`)

- The native Allocated, Hard links and Reparse type columns are checked for files, hard links, symlinks, junctions, sparse files and folders.
- A new name with the same size and time stays cached until expiry, after which Get and Compute read again.
- An Allocated analysis of 2,000 files retains every row. It took 256 ms at medium integrity and 327 ms elevated, on NTFS on the Windows 11 x64 workstation.

## Results

All runs were on the Windows 11 x64 workstation:

- **Medium:** unelevated, started through `explorer.exe`, with `FILECAT_TEST_EFS=1` and TEMP inside the user profile.
- **Elevated:** started from PowerShell, where the backup and restore privileges exist but are disabled.
- **Backup privilege:** elevated and started from Git Bash, which enables those privileges.

| Run | Classes | Result | TRX |
|---|---|---|---|
| Medium | Windows hidden-data, search-attribute and native-gap classes | 24 of 24 passed | [windows-native-medium.trx](receipts/2026-11-21-details-windows-native/windows-native-medium.trx) `12c73692…` |
| Elevated | same | 21 passed, 3 EFS-output tests skipped by design | [windows-native-elevated.trx](receipts/2026-11-21-details-windows-native/windows-native-elevated.trx) `4bf967f4…` |
| Backup privilege | same | 19 passed, 5 skipped (the two deny tests detect the bypass and skip) | [windows-native-elevated-backup-privilege.trx](receipts/2026-11-21-details-windows-native/windows-native-elevated-backup-privilege.trx) `73601045…` |
| Medium and elevated | `FileDetailsWindowsNativeTests` | 11 of 11 passed in each | [medium](receipts/2026-11-21-details-windows-native/app-scope-medium.trx) `87575ddb…`, [elevated](receipts/2026-11-21-details-windows-native/app-scope-elevated.trx) `e64c44e3…` |
| Medium and elevated | the new `FileDetailsColumnTests` case | 1 of 1 passed in each | [medium](receipts/2026-11-21-details-windows-native/app-meta-medium.trx) `e24604f1…`, [elevated](receipts/2026-11-21-details-windows-native/app-meta-elevated.trx) `064ef931…` |

The EFS halves of the two F-FIND predicate tests ran only in the medium run; the elevated runs checked compression only. The TRX files had the workstation's account name replaced by `user` before they were committed. That changed the hashes above, not the results.

## Findings

- **Hard links can make attribute Find miss a file.** After a compression or EFS change through one name, NTFS kept listing the other name with its old attribute until something opened the file through that name. Find reads attributes from folder listings by design (`SearchEngine`), so it missed the file in the other folder. This happened at both integrity levels. The fixture asserts that Find agrees with each listing and records the stale entry. The Compressed and EFS predicate help now says that each folder's listing supplies the bit and can show an earlier state for a file with more than one name.
- **Deny entries are bypassed by Git Bash, not by elevation.** Git Bash enables the backup and restore privileges for the processes it starts. A high-integrity process started from PowerShell obeyed the same deny entries. This corrects the earlier explanation in the [gap fixtures receipt](2026-11-21-details-gap-fixtures.md#native-fixtures).
- **A flaky column test.** The existing `Partial_and_unknown_zero_counts_sort_last_in_both_directions_and_never_as_exact_numbers` failed 2 of 10 class runs. It wrote its files after the window opened, so a late folder-watcher refresh could clear the analysed values after the sort. The test now creates its files before the first listing. It then passed 10 of 10 single runs and 5 of 5 class runs.
- **Test cleanup and junctions.** At normal integrity, .NET's recursive delete can fail on a junction with `UnauthorizedAccessException`. The shared App cleanup caught only `IOException`, so that error failed the test. It now catches both.
- **TEMP for unelevated runs.** Processes started through `explorer.exe` on the workstation got the system TEMP, where EFS is refused. The EFS-output tests need TEMP inside the user profile.
- **NTFS behaviour recorded by the fixtures:**
  - A relative symlink under a junction parent resolves against the path it was opened by, as FileCat's target calculation expects.
  - EA names are stored upper-cased.
  - A stream name with an unpaired surrogate is accepted by NTFS and refused by FileCat for the whole category.
  - NTFS refused a 606th stream with a 200-character name, so the inventory's 1 MiB buffer step is barely reachable.
  - A one-byte sparse write reported 0 bytes allocated while cached and 64 KiB after a flush.

## Still uncovered

- **F-SCOPE:**
  - actually opening a network target;
  - `\\?\Volume{…}\` targets;
  - replacing the path between the summary and full reads, beyond the existing fixture;
  - missing adapters for the optional mutation links, and I-DETAILS-C;
  - the per-adapter permissions and attributes cases;
  - ARM64.
- **F-EXTRA:**
  - App flows for same-name streams and EAs: refresh order, marks, focus, restored selection and a disappeared category;
  - the 1 MiB buffer step;
  - native errors other than denial and an unreadable name;
  - the macOS items.
- **F-FIND:**
  - saved and refined search sets, and clearing masks;
  - removing or replacing an unsupported criterion;
  - archive members against negative predicates;
  - replacing the old extra-data criterion, and download marks other than `Zone.Identifier`;
  - unknown exact tags;
  - live result forwarding.
- **F-META:**
  - invalidation races, provider restrictions, cache saturation and eviction;
  - natively, the rules against synthetic device dispatch and against compare, hover or speech access;
  - read counting for Get;
  - timing beyond NTFS on this host.
