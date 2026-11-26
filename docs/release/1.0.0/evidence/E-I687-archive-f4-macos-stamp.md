# E-I687 — An archive UI test pressed F4 once while hosted macOS stamped the freshly written archive

**Found:** 2026-11-26, by `v1.0.0-rc.9`'s tagged run 45897912070 (macOS App tests, focus: 1,477 passed, 1 failed). **Severity:** Low: CI qualification, no product defect. **Disposition:** fixed on main ([PR #78](https://github.com/benny-cz/FileCat/pull/78)). By the owner's decision ("Keep rc.9 (Recommended)") rc.9 stays the candidate: nothing shipped changes, its source keeps the old test, and its failed job was run again.

## What failed

`ArchiveStandaloneUiTests.Actual_Unix_workers_compress_replace_and_commit_one_file(format: Zstd)` on macOS. After F4 on the replaced archive's member, no edit session existed (`Assert.Single()`: the collection was empty). The test recorded no notice, and the run's TRX holds no output for it.

## Why (most likely)

- **The same tree passed:** the test passed on rc.9's tree in PR #75's run 45873958869 and PR #76's run 45890542893, and rc.9 changed neither archive writing nor edit sessions.
- **The stamp:** hosted macOS stamps a freshly written file as used (`com.apple.lastuseddate#PS`) a few seconds later, which changes its metadata. F4 refuses an archive that changed after the listing read it, as it must.
- **Not covered at F4:** the test already allows that stamp once, at the replacement (runs 43792885074 and 43792906622), but not at F4.

## The fix

[PR #78](https://github.com/benny-cz/FileCat/pull/78) (`c5946507`, merged into main as `7618b498` after its full-matrix run 45900683071):
- When F4 opens no edit session on macOS and a file in the folder carries that stamp, the test writes the refusal's notice to its output, reads the listing again and presses F4 once more.
- Any other outcome fails with "F4 opened N edit sessions, not one", the notice and the window's text, so a next failure says why.
