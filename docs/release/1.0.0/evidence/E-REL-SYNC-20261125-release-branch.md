# E-REL-SYNC-20261125 — `release/1.0` synced with main twice: at `d449095f` (PR #56, the rc.3 fixes) and at `5137040b` (PR #59, I672 to I674)

2026-11-25 (UTC). **Release-branch maintenance before `v1.0.0-rc.3`.** The owner's standing instruction is to sync `release/1.0` with main once every open PR has merged. After the fifth sync of [E-REL-SYNC-20261124](E-REL-SYNC-20261124-release-branch.md) (PR #24), main took the rc.3 fixes through integration [PR #44](https://github.com/benny-cz/FileCat/pull/44) and their records. When this sync started, PRs #25 to #55 had all merged and no PR was open.

## Source and procedure

- **Source:** `d449095f`, main's tip: the rc.3 ledger records on top of `a2f983e9`, the merge of PR #44.
  - PR #44's head `9956448b` passed [run 45570809782](https://github.com/benny-cz/FileCat/actions/runs/45570809782): all 19 jobs that ran.
  - `a2f983e9` differs from `9956448b` only in release records. Main's I671 record and the integration branch's I640 amendment had changed adjacent rows of the blockers file (DEC-05 and DEC-04); the merge kept both.
  - Main's own CI run of `a2f983e9` (45574829200) was cancelled as a repeat of the tested source. The Release records workflow passed on `a2f983e9` and on `d449095f`; since PR #48, a records-only push to main starts no CI run.
- **What came in** (149 commits from `9e310e95`):
  - every rc.3 fix: PRs #25 to #43 and #45 to #55, through integration PRs #35, #37 and #44, and the CI changes of PR #48. They fix I635 to I637, I641 to I671 and I42, carry the I638 to I640 contract amendments, and give L7's apphost and WebView2 files their disposition;
  - the records of I665 to I671, DEC-14, the owner's rc.3 walkthroughs and the ledger update.

  Outside `docs/`: product source in `src/` (Core, App, Remote, the Windows platform and `ThirdParty`), tests, `eng/` (the installer and the privileged bootstrap), `.github/workflows/ci.yml`, `licenses/`, `THIRD-PARTY-NOTICES.md`, `README.md` and `SECURITY.md`.
- **Sync branch:** `sync/release-1.0-d449095f6` was pushed at `1f5feb6c`, with parents `d449095f` and `9e310e95` and `d449095f`'s tree. A first push of the branch name pointed at `d449095f` itself, because the merge step had failed; it was replaced by `1f5feb6c` before the pull request opened.
- **CI:** [PR run 45575294166](https://github.com/benny-cz/FileCat/actions/runs/45575294166) passed all 19 jobs that ran, in its first attempt. The 5 package and draft jobs skip on a pull request by design.
- **Merge:** [PR #56](https://github.com/benny-cz/FileCat/pull/56) was merged as `60fe1731` at 2026-11-25T08:51:02Z, pinned to the tested head `1f5feb6c`; the branch was deleted.

## Result

- **Tip:** `release/1.0` is at `60fe1731`, with parents `9e310e95` and `1f5feb6c`.
- **Content:** its tree equals `d449095f`'s.
- **Tag:** with the owner's standing authorization of 2026-11-24 ("Yes, tag it", for rc.3 once merged, synced and green), annotated tag `v1.0.0-rc.3` (object `c551d41b`) was pushed at 2026-11-25T08:51:18Z on `60fe1731`. Its record follows once the tagged run is done.

## Quick issue scan

Covering `9e310e95` to `d449095f`:
- No secrets, tokens or email addresses were added.
- The 83 added lines that skip a test are all conditions in new or extended tests: the platform; tools that must be installed (OpenSSH's sshd, Git, the WebView2 runtime, the FTP fixture, `findmnt`); the token's integrity level; the volume's support for streams or punched holes; the test machine's state (the clipboard held by another program, a folder that cannot be watched, the apphost or the sources not beside the test build); and opt-in measurements. No test gained an unconditional skip, and no skip was removed.

## Second sync: main at `5137040b` (PR #59)

- **Source:** `5137040b`, main's tip, after PR #57 (I672, merged as `3934b843`) and PR #58 (I673 and I674, merged as `32d4dc2b`). Each had passed its own full-matrix run in a second attempt that reran jobs failed by the hosted runners (a .NET SDK download on Windows ARM64; macOS jobs no runner took) or by the two races PR #58 fixes. No PR was open.
- **What came in** (11 commits from `60fe1731`): the three fixes, in `MainViewModel.Commands.cs`, `CompareWindow.cs` and `ReportWindow.StorageCalls.cs`, with two test files; and the records of rc.3's tag and qualification and of I672 to I674. No secrets, tokens or email addresses; no skips added.
- **Sync branch:** `sync/release-1.0-5137040b4` at `1b1c6a11`, with parents `5137040b` and `60fe1731` and `5137040b`'s tree.
- **CI:** [PR run 45634525115](https://github.com/benny-cz/FileCat/actions/runs/45634525115) passed all 19 jobs that ran, in its first attempt: the three fixes' first run together.
- **Merge:** [PR #59](https://github.com/benny-cz/FileCat/pull/59) was merged as `16f9379a` at 2026-11-25T15:08:25Z, pinned to the tested head; the branch was deleted.
- **Result:** `release/1.0` is at `16f9379a`, with parents `60fe1731` and `1b1c6a11`; its tree equals `5137040b`'s.
- **Tag:** with the owner's authorization of 2026-11-25 ("Yes, tag rc.4"), annotated tag `v1.0.0-rc.4` (object `6cdaeb55`) was pushed at 2026-11-25T15:08:40Z on `16f9379a`. The `v1.0.0-rc.3` tag stays at `60fe1731`.

## Not shown

- The records after `5137040b`, which stay on main until a later sync.
