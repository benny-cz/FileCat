# E-REL-SYNC-20261123 — `release/1.0` synced with main five times, from `e5cf68f2` to `48d3de08`

2026-11-23 (UTC). **Release-branch maintenance; no candidate, tag or publication.** On the owner's request, `release/1.0` was synced with main through PR #3, as [E-REL-SYNC-20261120](E-REL-SYNC-20261120-release-branch.md) had planned. Main's newest work then settled and passed CI, and the owner had it synced again through PR #5. Backport PR #4, opened between the two syncs, was closed because the second sync contains its commits.

Three more syncs followed, each at the owner's request:
- PR #6 brought a Windows program-start fix and two test cleanups.
- PR #8 brought the release-candidate signing pipeline.
- PR #14 brought two changes:
  - the fix for [I631](E-I631-ftps-refused-data-final-reply.md), which the native runs of the PR #6 tree found ([E-REL-NATIVE-20261123](E-REL-NATIVE-20261123-release-tree.md));
  - the move of unlinked validation receipts to the evidence store (PR #11).

  It superseded sync PR #10, which had stopped at `1ec1207f`.

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

## Third sync: main at `8a1e6763` (PR #6)

### Source choice

- **Source:** `8a1e6763`, main's tip when the owner asked for the sync.
- **What came in (4 commits, 13 files):**
  - `94f20963`, `2bf15ce8` and `8a1e6763` came from an old worktree branch. At the owner's request, the branch was rebased onto main and deleted.
    - `94f20963` fixes [I632](E-I632-unrunnable-program-start-wait.md): starting a tool or an Apply command that names a file 64-bit Windows takes for a 16-bit program no longer waits behind Windows' modal "Unsupported 16-Bit Application" box.
    - `2bf15ce8` keeps the junction accounting fixture uncompressed. `8a1e6763` clears the storage tests' last analyzer warnings.
  - `5cb7702a` recorded the first two syncs.
- **Gate:** the PR's own run. Main's run of the same tree, 45236898547, started at the same time; see [CI failures seen on the way](#ci-failures-seen-on-the-way).

### Procedure and result

1. **Sync branch.** `sync/release-1.0-8a1e6763d` was pushed at `49644213`, with parents `8a1e6763` and `6611a72d` and `8a1e6763`'s tree.
2. **CI.** [PR run 45237015331](https://github.com/benny-cz/FileCat/actions/runs/45237015331) passed all 19 jobs that ran, the five required ones included, in its second attempt. In the first, GitHub could not get hosted macOS runners for two macOS jobs.
3. **Merge.** [PR #6](https://github.com/benny-cz/FileCat/pull/6) was merged as `57bd5e8f` at 2026-11-23T19:47:24Z. The merge was pinned to the tested head, and the branch was deleted.

`release/1.0`'s tree then equalled `8a1e6763`'s.

### Quick issue scan

Covering `59abfde1` to `8a1e6763`:
- No secrets, key or keystore files, email addresses, files over 2 MB or new test skips.
- Workflows, packages and build configuration are unchanged.

## Fourth sync: main at `3069448f` (PR #8)

### Source choice

- **Source:** `3069448f`, the merge of [PR #7](https://github.com/benny-cz/FileCat/pull/7), which brings signing into the tagged candidate pipeline. The owner asked for the sync once PR #7 merged.
- **Main's CI:** [run 45252307188](https://github.com/benny-cz/FileCat/actions/runs/45252307188) passed.
- **What came in (3 commits, 8 files):** workflows and `eng` scripts only; see [E-SIGN-DRYRUN-20261120](E-SIGN-DRYRUN-20261120.md#candidate-signing-in-the-tagged-pipeline).

### Procedure and result

1. **Sync branch.** `sync/release-1.0-3069448f8` was pushed at `4a2f2a78`, with parents `3069448f` and `57bd5e8f`.
2. **CI.** [PR run 45252685726](https://github.com/benny-cz/FileCat/actions/runs/45252685726) passed all 19 jobs that ran in its first attempt. The new candidate signing job runs only for RC tags, so it was skipped with the other tag-only jobs.
3. **Merge.** [PR #8](https://github.com/benny-cz/FileCat/pull/8) was merged as `08e03db2` at 2026-11-23T21:08:33Z. The merge was pinned to the tested head, and the branch was deleted.

`release/1.0`'s tree then equalled `3069448f`'s.

### Quick issue scan

Covering `8a1e6763` to `3069448f`:
- No secrets, key or keystore files, email addresses, files over 2 MB or new test skips.
- Source, packages and build configuration are unchanged.
- **Workflows.** `ci.yml` gains the `sign-candidate` job for `v1.0.0-rc.N` tags:
  - It runs in the protected `signing` environment and holds `contents: write` for the candidate's private drafts.
  - It is the only job given the eSigner secrets, and they reach only its two signing steps.
  - On RC tags, the unsigned Windows draft steps are skipped.
- **Manual workflows.** The two manual signing workflows gain a runtimes input.
- **Pinning.** Every new action is pinned to a commit, and no workflow uses `pull_request_target`.

## Sync PR #10 at `1ec1207f`, superseded

- **Source:** `1ec1207f`, the merge of [PR #9](https://github.com/benny-cz/FileCat/pull/9), the I631 fix. The owner had asked for a sync once all open PRs had merged, and PR #9 was then the last of them.
- **Main's CI:** run 45267782131 lost two macOS jobs to runner capacity.
- **What it carried (2 commits, 2 files):** `FluentFtp.cs` and `FtpIntegrationTests.cs`.
- **Sync branch:** `sync/release-1.0-1ec1207f5` at `d34cac82`, with parents `1ec1207f` and `08e03db2`.
- **First attempt of [PR run 45267952326](https://github.com/benny-cz/FileCat/actions/runs/45267952326):**
  - Every job passed except one non-required macOS Rendering test; see below.
  - GitHub could not get a hosted runner for the macOS archive Core job.
- **Superseded.** The re-run of those two jobs was still waiting for macOS runners when PR #11 merged into main. The owner then asked for a sync with main at once. [PR #10](https://github.com/benny-cz/FileCat/pull/10) was closed, its re-run cancelled and its branch deleted. Its commits came in through PR #14.

## Fifth sync: main at `48d3de08` (PR #14)

### Source choice

- **Source:** `48d3de08`, main's tip and the merge of [PR #11](https://github.com/benny-cz/FileCat/pull/11), on the owner's request to sync with main at once.
- **What came in (4 commits):**
  - From PR #9 and its merge: `FluentFtp.cs` and `FtpIntegrationTests.cs`.
  - From PR #11 and its merge: 2,246 unlinked validation receipts moved to the evidence store. With them came `.gitignore` rules that keep such receipts out, `eng/evidence-retention.policy.json` and `docs/validation/disk-space/README.md`.
- **Tree size:** the tree shrank from 1,384 MiB to 193 MiB.

### Procedure and result

1. **Sync branch.** `sync/release-1.0-48d3de08d` was pushed at `939d0a8e`, with parents `48d3de08` and `08e03db2` and `48d3de08`'s tree.
2. **CI.** [PR run 45286933633](https://github.com/benny-cz/FileCat/actions/runs/45286933633) passed all 19 jobs that ran, the five required ones included, in its first attempt.
3. **Merge.** [PR #14](https://github.com/benny-cz/FileCat/pull/14) was merged as `6b2404f3` at 2026-11-24T00:18:34Z. The merge was pinned to the tested head, and the branch was deleted.

- **Tip:** `release/1.0` is at `6b2404f3`, with parents `08e03db2` and `939d0a8e`.
- **Content:** its tree equals `48d3de08`'s.
- **Gap:** while PR #14's checks ran, main merged PR #13 and PR #12. Those two only lengthen test waits, and they and these records follow in the next sync.

### Quick issue scan

Covering `3069448f` to `48d3de08`:
- No secrets, key or keystore files, email addresses, files over 2 MB or new test skips. PR #11 adds or changes only `.gitignore`, the retention policy and one README; the rest of it is deletions.
- Workflows, packages and build configuration are unchanged.

## CI failures seen on the way

Both failures were on hosted macOS, in jobs that are not required. Neither was investigated further.

- **`MetadataPreparationTests.Ended_preparation_retains_active_calls_until_return_and_never_publishes_late_UI(operation: "final", action: "navigate")`.**
  - It failed in main's run 45236898547 of `8a1e6763`, in the macOS App focus job, with "Assert.NotEmpty() Failure: Collection was empty" after 0.5 s.
  - The same job passed on the same tree in PR #6's run.
  - Earlier that day, in main's run 45163923255 of `e34011a8`, three cases of another test in the class, `An_explicitly_approved_attribute_job_keeps_the_selection_shown_in_its_dialog`, timed out in the same job.
- **`AnalysisRenderingTests.ClipboardPathFromCompactInspectorRemainsBoundToItsDisplayedEntry`.**
  - It failed in the first attempt of PR #10's run, in the macOS Remote and Rendering job, after 61 ms.
  - The clipboard held nothing where `/fixture/Photos/picture.png` was expected.
  - PR #10's re-run was cancelled when PR #14 superseded it. The same job passed in PR #14's run, whose Rendering tests are the same.

## Not shown

- Native runs of the final tree. The PR #6 tree ran natively on the Mac, Ubuntu and Windows VMs: [E-REL-NATIVE-20261123](E-REL-NATIVE-20261123-release-tree.md).
- The cause of the two macOS failures above.
