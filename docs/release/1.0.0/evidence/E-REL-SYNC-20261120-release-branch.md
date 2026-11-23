# E-REL-SYNC-20261120 — `release/1.0` synced with main at `e0d16207`

2026-11-20 (UTC). **Release-branch maintenance; no candidate, tag or publication.** On the owner's request, `release/1.0` was brought up to the newest green state of main while main was still under heavy load. A second sync follows when the current work settles.

## Source choice

- **Starting point:** `release/1.0` stood at `783f63e7`, the merge of PR #1 on 2026-10-30. Main was 1,524 commits ahead.
- **Source:** `e0d16207` was the newest main commit whose full CI passed. [Run 44783690622](https://github.com/benny-cz/FileCat/actions/runs/44783690622) passed all five required jobs and the macOS App tests.
- **Why not later commits:** the completed runs after it failed two macOS Core tests, starting at `dd990be3`. At `bb3ec12f` a different macOS test failed ([run 44828715427](https://github.com/benny-cz/FileCat/actions/runs/44828715427), later cancelled). Newer runs were cancelled as superseded. The owner chose to sync at `e0d16207` instead of waiting.

## Procedure

1. A fixed branch, `sync/release-1.0-e0d16207c`, was pushed at `e0d16207`, so pushes to main could not restart the checks.
2. [PR #2](https://github.com/benny-cz/FileCat/pull/2) was opened into `release/1.0`.
3. The branch ruleset (26209930) requires an up-to-date head (`strict_required_status_checks_policy`). Merging the release tip `783f63e7` into the sync branch produced `ce1bcb99`, whose tree equals `e0d16207`'s. The obsolete PR run on `e0d16207` was cancelled.
4. [PR run 44832029084](https://github.com/benny-cz/FileCat/actions/runs/44832029084) on `ce1bcb99` passed all five required checks (producer policy, Windows, Windows ARM64, Ubuntu 24.04 and macOS 26) and the macOS App tests.
5. Auto-merge could not be enabled, because the repository does not allow it. The owner chose a manual merge. The PR merged with a merge commit, `1401e15f`, at 2026-11-20T23:47:24Z, and the temporary branch was deleted.

## Result

- **Tip:** `release/1.0` is at `1401e15f`, with parents `783f63e7` and `ce1bcb99`.
- **Content:** its tree equals `e0d16207`'s, which is an ancestor; 1,481 commits came in.
- **Gap:** main is 52 commits ahead afterwards.

## Quick issue scan of the incoming commits

Covering `release/1.0` to `e0d16207`, the whole tree where noted:

1. **No secrets.** No private keys, GitHub, AWS or Slack tokens, or Azure account keys appear anywhere in the tree, and no key or keystore files were added.
2. **Personal data** appears only in deliberate authorship notices: author and copyright metadata, the About dialog, SBOM copyright text, the macOS bundle copyright and the RAR 1.5 port headers. This matches DEC-13 and was already public on main.
3. **Repository size.** The tree grew from 87 MB to 1,366 MB. `docs/validation` holds 1,293 MB, including 448 archives of 936 MB in total (`.tgz` receipts and sources of 16–19 MB each under `docs/validation/disk-space/`). No `export-ignore` or LFS rule covers them, so clones and CI checkouts carry them, and so would GitHub's generated source archives for a release tag.
4. **Latent crash, older than this sync.** `src/ThirdParty/SharpCompress/Common/SevenZip/ArchiveReader.cs` calls `Debugger.Break()` unguarded at lines 547 and 1278. A 7z archive whose digest counts disagree reaches the first one; without a debugger, the breakpoint trap ends the process that parses it. Both calls were already on `release/1.0` before this sync.
5. **New dependency.** PDFtoImage 5.2.1 brings native pdfium; the dependency-notice gate passed.
6. **Workflows.** Two new manual, read-only NTFS/EFS ARM64 workflows run on GitHub-hosted runners, and the main CI queue is now bounded. No action is unpinned, and no workflow uses secrets, `pull_request_target` or write permissions.
7. **Build configuration.** `global.json` is unchanged. `Directory.Build.props` changes the author and copyright lines, `FileCat.slnx` gains three projects, and no test is skipped by attribute.

## Not shown

- The 52 newer main commits, and main's macOS failures, which belong to the in-flight extended-attribute work.
- Native runs on the Mac, Ubuntu and Windows VMs for this state. They follow with the second sync, when the workers are done with those machines.
