# E-REL-SYNC-20261123 — `release/1.0` synced with main twice: at `e5cf68f2`, then at `59abfde1`

2026-11-23 (UTC). **Release-branch maintenance; no candidate, tag or publication.** On the owner's request, `release/1.0` was synced with main through PR #3, as [E-REL-SYNC-20261120](E-REL-SYNC-20261120-release-branch.md) had planned. Main's newest work then settled and passed CI, and the owner had it synced again through PR #5. Backport PR #4, opened between the two syncs, was closed because the second sync contains its commits.

## First sync: main at `e5cf68f2` (PR #3)

### Source choice

- **Starting point:** `release/1.0` stood at `1401e15f`, the merge of PR #2 on 2026-11-20.
- **Source:** `e5cf68f2` was the newest main commit whose full CI had passed. [Run 45178038597](https://github.com/benny-cz/FileCat/actions/runs/45178038597) passed every job, including the five required ones.
- **Why not later commits:** main's tip, `073e6a66`, was 7 commits ahead, and its CI had not finished.

### Procedure

1. **Sync branch.** `sync/release-1.0-e5cf68f2e` was pushed at `e896c03f`, a merge commit made with `git commit-tree`. Its parents are `e5cf68f2` and the release tip `1401e15f`, and its tree is `e5cf68f2`'s. The branch already contained the release tip, so the PR started up to date and needed no update step for the ruleset's up-to-date rule.
2. **PR.** [PR #3](https://github.com/benny-cz/FileCat/pull/3) was opened into `release/1.0`.
3. **First attempt.** [PR run 45203742824](https://github.com/benny-cz/FileCat/actions/runs/45203742824) passed four of the five required checks: producer policy, Windows, Windows ARM64 and Ubuntu 24.04.
   - The fifth, macOS 26, was cancelled after 28 minutes in the queue. GitHub reported that the job "was not acquired by Runner of type hosted even after multiple attempts", with notices about limited macOS arm64 capacity.
   - The non-required `ubuntu-24.04 (App tests)` job failed one test; see below.
4. **Second attempt.** It re-ran only the failed and cancelled jobs on the same commit. Both passed, macOS 26 included.
5. **Merge.** Repository auto-merge is disabled, so the PR was merged manually with a merge commit, `f25a2d6a`, at 2026-11-23T16:35:00Z. The merge was pinned to the tested head, and the temporary branch was deleted.

### Result

- **Tip:** `release/1.0` was at `f25a2d6a`, with parents `1401e15f` and `e896c03f`.
- **Content:** its tree equalled `e5cf68f2`'s. 431 commits came in.

### The Ubuntu App test failure

`PermissionRouteTests.Find_results_inspect_the_original_and_closing_Find_retires_the_inspector(closeFind: True)` waited 10 s for the permissions inspector and timed out. The same tree passed that job on main in run 45178038597 and in the second attempt here. Main's next tip, `073e6a66`, passed it too, in run 45201558553.

The cause is in the test, not in FileCat:
- Find refuses "Permissions and ownership" until its search has finished and its last results are published (`StorageResultsSettled`).
- The test ran the command as soon as `a.txt` was listed, and a timer tick can list it while the search is still walking.
- On a loaded runner, the command was refused with "Wait for the results to finish before inspecting permissions.", and no inspector opened.

A throwaway probe confirmed this, run at medium integrity on the development host. It made the search slow by adding 40,000 folders after `a.txt`:
- With the old wait, the command ran after about 300 ms, while the search was still running. It was refused in 5 of 5 runs.
- Waiting for `StorageResultsSettled` instead, the command ran after about 3.9 s, and the inspector opened in 5 of 5 runs.

Main commit `59abfde1` makes the test wait that way, as `OpenSettledFind` already does for the storage tests. After the change, 30 runs of the class passed at medium integrity (7 tests each, none skipped).

### Quick issue scan of the 431 incoming commits

Covering `1401e15f` to `e5cf68f2`:

1. **No secrets.** The incoming changes add no private keys, tokens or account keys, and no key or keystore files.
2. **Repository size.**
   - The tree grew from 1,366 MB to 1,383 MB.
   - `docs/validation` grew from 1,293 MB to 1,304 MB.
   - No incoming file is over 2 MB.
3. **SSH.NET is now vendored.**
   - The SSH.NET 2026.0.0 package was removed. Its source now lives in `src/ThirdParty/SshNet`, added in `385a59e8`: 349 files, built as `FileCat.SshNet.csproj` for `FileCat.Remote`, with `licenses/sshnet/LICENSE` and `LICENSE-bcrypt`.
   - Microsoft.Extensions.Logging.Abstractions 8.0.3 came in with it.
   - Upstream SSH.NET security fixes no longer arrive as package updates or GitHub security alerts. They have to be tracked and ported by hand.
4. **Workflows.**
   - New manual workflows: `details-arm64-qualification.yml`, `sparse-copy-device-loss.yml` and the two Windows signing workflows. `ntfs-arm64-qualification.yml` changed and stays manual.
   - None uses `pull_request_target`, and every action is pinned to a commit.
   - Apart from `ci.yml`'s existing `GITHUB_TOKEN` use, only the signing workflows use secrets: the dry run's eSigner sandbox secrets, and the pilot's eSigner secrets. The pilot's signing job runs in the protected `signing` environment and holds the only new write permission (contents, for its private draft release).
   - `ci.yml` splits its work from 10 jobs into 18 and gains a concurrency limit. Its permissions and token use are unchanged.
5. **Build configuration.** `FileCat.slnx` gains `FileCat.SshNet.csproj`.
6. **Personal data.** No new files with personal data.
7. **The 7z reader's breakpoints.** The two `Debugger.Break()` calls in the bundled 7z reader were unchanged by this sync. No archive can reach them; see the correction in E-REL-SYNC-20261120. Main removed them in `7230e412`, which the second sync brings.

## Backport PR #4, superseded

[PR #4](https://github.com/benny-cz/FileCat/pull/4) carried two main commits to `release/1.0`:
- `7230e412`, as `03275ed7`.
- `59abfde1`, as `475aa52a`.

Main's tip then passed its full CI, and the owner chose a second sync instead. That sync contains both commits, so the PR was closed, its CI run 45218271885 was cancelled, and its branch was deleted.

## Second sync: main at `59abfde1` (PR #5)

### Source choice

- **Source:** `59abfde1`, main's tip. [Run 45213402537](https://github.com/benny-cz/FileCat/actions/runs/45213402537) passed all 19 of its jobs that ran, the five required ones included; 4 tag-only jobs were skipped.
- **Why now:** the 10 commits since `e5cf68f2` include 7 that complete the permissions inspector redesign, whose first part came in with PR #3, and add remembered dialog sizes. That work looked finished:
  - Its last commit was more than two hours old.
  - No branch held unmerged work.
  - No checkout had source edits in the last hour.
  - No FileCat build or test was running.

### Procedure

1. **Sync branch.** `sync/release-1.0-59abfde1b` was pushed at `3ebfbc49`, made like `e896c03f`: parents `59abfde1` and `f25a2d6a`, with `59abfde1`'s tree.
2. **PR.** [PR #5](https://github.com/benny-cz/FileCat/pull/5) was opened into `release/1.0`.
3. **CI.** [PR run 45223218317](https://github.com/benny-cz/FileCat/actions/runs/45223218317) passed all five required checks on `3ebfbc49` in its second attempt; in the first, GitHub could not get hosted macOS runners for four macOS jobs, the required one included.
4. **Merge.** The PR was merged manually with a merge commit, `6611a72d`, at 2026-11-23T17:58:46Z. The merge was pinned to the tested head, and the temporary branch was deleted.

### Result

- **Tip:** `release/1.0` is at `6611a72d`, with parents `f25a2d6a` and `3ebfbc49`.
- **Content:** its tree equals `59abfde1`'s, which is an ancestor. 10 commits came in.
- **Gap:** none. At the merge, `release/1.0` contained every main commit.

### Quick issue scan of the 10 incoming commits

Covering `e5cf68f2` to `59abfde1` (71 files):
- No secrets, key or keystore files, files over 2 MB, or email addresses in the added lines.
- Workflows, packages and build configuration are unchanged, and no test gains a skip.
- The tree grew from 1,383 MB to 1,384 MB.

## Not shown

- Native runs on the Mac, Ubuntu and Windows VMs for this state.
