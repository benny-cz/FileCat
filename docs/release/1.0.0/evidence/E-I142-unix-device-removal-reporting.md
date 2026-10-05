# E-I142 — Unix recovery reports a changed source as an approval refusal

Confirmed and corrected preliminarily on 2026-10-05. In the actual native
[held-approval removal](E-V09-M10-macos-guard-device-removal.md), the owner approves
after both owned device paths disappear. Authopen's subsequent read-only open fails
with ENOENT, but committed 8f75856 returns OperationCanceledException saying reading
was "not approved." No source was constructed: this is a reporting defect, not a
wrong-device read or source-write observation.

## Remedy and controlled validation

UnixDeviceSource now rechecks the selected path's native identity when opening fails
with OperationCanceledException. If the cancellation token is not cancelled and the
path no longer identifies the selected entry, it reports IOException explaining that
the device changed or was removed and must be selected again. The original failure
is retained as the inner exception. Unchanged-device refusal and explicit caller
cancellation preserve the original exception. Successful descriptor identity checks
and disposal remain unchanged.

Four new native cases cover removal, equal-size replacement, unchanged refusal and
explicit cancellation after removal. The unchanged production baseline reproduces
both reporting failures; both cancellation controls pass. Identical corrected cases
and existing UnixDevice/UnixFiles controls pass on the physical Mac as ordinary UID
501, using only owned files and the internal opener boundary; no native dialog is
opened and no root source access is substituted for consent.

| Lane | Result | Limits |
|---|---|---|
| Native baseline v2 | 21 pass, 2 expected reporting failures, 4 declared skips; worker exit 1 | Baseline production DLLs, new regressions; not a passing suite |
| Native corrected working v2 | 23 pass, 0 fail, 4 declared skips; worker exit 0 | Four new cases pass; working producer, no real dialog |
| Windows affected Core | 7 pass, 20 declared native skips; exit 0 | Windows cannot prove Unix identities |
| Windows affected App recovery | 25 pass, 9 declared native skips; exit 0 | Recovery admission/UI models, no drawn native workflow |

Each native run verifies all 321 payload pins before and after, six retained files,
the test process's absence and empty owned temporary roots. The first baseline v1
observer incorrectly required no temporary entries at all: TempDir left an empty
`filecat-tests` container after all owned case folders were removed. The test results
were already 21/2/4, but that observer failed and is retained. Fresh v2 verifies that
the sole remaining entry is the exact empty nonsymlink directory, removes it using
rmdir, and then verifies the empty root. No test or failure assertion is relaxed.

## Exact provenance

Private base is the authorized second workspace's
`FileCatReleaseEvidence/mac-resume-20261005/i142-working-v1`.

| Item | SHA-256 |
|---|---|
| Baseline v2 `baseline-v2/independent-native-v1.json` | `30ea24b378a0cea2d42ba710c0bec41cd4bb29b1aeadb3475382e7d04d2b59c0` |
| Corrected working v2 `corrected-v2/independent-native-v1.json` | `692bbca18f97c55d1b9dc918ac9fea6100e987ecbd2303e792d14cab6fc2ab83` |
| Actual native removal baseline | See exact raw/result/owner hashes in [E-V09-M10](E-V09-M10-macos-guard-device-removal.md) |

Committed-source/native and successor CI revalidation pass below. The first CI
attempt is retained as a hosted-runner acquisition failure, separately from the
successful retry of the exact same source. Historical native
approval/refusal/replacement/removal captures retain their exact 8f75856 component
identity; their results are not relabelled as successor qualification. Actual drawn
workflow, broader helper/topology, final candidate and human GO remain unavailable.
No further human Mac dialog case is queued for this slice. **NO-GO** remains.

## Committed clean-source and native revalidation

Production is `348cbc703140b1b4d84a05b75185d268ae5fc8f6`. The producer exports all 858 raw Git
blobs and verifies each Git blob ID, path and SHA-256 before publication with the
exact SourceRevisionId. Two earlier producers stop before tests because Windows Git
archive converts line endings; those partial attempts remain retained. No archive
bytes or test outcomes are silently rewritten. The raw source archive SHA-256 is
`952348a5f9477f0597a694c28c08df90cab97e845b4277b95468891e04a89b2d`;
producer receipt is `44d9e20649539090179dfa0753bef76b0ea68e55c75142b189db710bc63009f6`.

| Lane | Actual result | Evidence scope |
|---|---|---|
| Clean physical Mac arm64, ordinary UID 501 | 23 pass, 4 declared skips, exit 0 | All four new cases pass; 321 payload pins before/after, six retained pins, process absence and empty owned temporary root |
| Clean Ubuntu 26.04.1 VMware x64, ordinary UID 1000 | 25 pass, 2 declared skips, exit 0 | All four new cases pass; 322 payload pins before/after, six retained pins, process absence and empty owned temporary root; exact VMX route and actual OS recorded |
| Final actual Mac held authorization/removal | Safety and correct reporting pass; no source, no timeout | Byte-identical clean production components, owner approval after independently verified removal, native ENOENT and path recheck, source/cleanup and trace limits in [E-V09-M10](E-V09-M10-macos-guard-device-removal.md) |

| Private item under `mac-resume-20261005` | SHA-256 |
|---|---|
| `i142-clean-v3/executed/independent-native-v1.json` | `ad08c1554ade263d9943a25f2fe8f5de9051d3b5e989bc312f30b4c4ce1be594` |
| `i142-linux-guest-v1/independent-guest-v1.json` | `f39917f7e4863365c31edc9dd55dd0bb753e4e6dda7ab3b11ad2c7b7046f30c9` |
| `mac-removal-executed-v2/independent-executed-v1.json` | `b990dff20015d290b00c945851b4c810e9d466e65f9f8db4f6cf081786e38692` |
| `mac-removal-executed-v2/independent-path-recheck-v1.json` | `9f3449ca282a8f71b0d134b59658cc69cb013dabcb68baa30e1f4249d90b8425` |

The supplementary host observer first selected an assumed test name and found only
two of the four cases. Its failed script/exit description remain retained; the fresh
observer selects the exact committed method names and verifies the original results.
No native suite is rerun or outcome altered for that selector correction.

CI 37373490704 attempt one passes Mac and ARM64, while Windows and Ubuntu are
cancelled with the primary annotation "The job was not acquired by Runner of type
hosted even after multiple attempts." Attempt-one metadata/annotations remain
separate. Attempt two reruns all lanes at the same source and passes all four required
lanes, with independently verified logs/inventories below. The acquisition failure
is not a test failure; the passing retry does not qualify a release candidate.
No further Mac interaction is queued. Broader/native workflow/candidate gates remain.

## Exact-source CI retry verified

[CI 37373490704 attempt two](https://github.com/benny-cz/FileCat/actions/runs/37373490704/attempts/2)
passes Windows x64, Windows ARM64, Ubuntu and macOS at exact production
`348cbc703140b1b4d84a05b75185d268ae5fc8f6`. All three package publication jobs are
skipped. Four downloaded server ZIP digests and six complete TRX inventories verify;
each case has a matching definition/unique execution ID, zero failures, and retained
messages/stdout for each declared skip. The complete 368-name App inventory matches
the independently retained host inventory on all three TRX-bearing App lanes.

| Complete TRX inventory | Passed | Declared skips |
|---|---|---|
| app-test-results-macos-latest/FileCat.App.Tests | 325 | 43 |
| app-test-results-ubuntu-latest/FileCat.App.Tests | 323 | 45 |
| test-results-windows/FileCat.App.Tests | 351 | 17 |
| test-results-windows/FileCat.Core.Tests | 787 | 57 |
| test-results-windows/FileCat.Platform.Windows.Tests | 166 | 33 |
| test-results-windows/FileCat.Remote.Tests | 88 | 28 |

The 34 affected App recovery cases are 31 pass/3 declared skips on Windows and
26 pass/8 declared skips each on Ubuntu/macOS. The Windows UnixDevice/UnixFiles
scope is 7 pass/20 declared native skips; Unix CI has successful Core log totals,
but no per-case Core TRX artifact. The physical Mac and Ubuntu guest inventories
above independently prove actual execution of all four native I142 cases.

ARM64 App logs retain 351 pass/17 declared skips/368 total, zero failures, successful
package start/drawing and installer compilation. Per-case ARM64 TRX and physical
ARM64/final artifact qualification remain unavailable.

| Independently verified server archive | Artifact ID | SHA-256 |
|---|---|---|
| test-results-windows | 11371442043 | `faba8c74ec6ac5e6d300fba364ef52cc491ac8056f596c5cf762648c2ff1ab03` |
| app-test-results-ubuntu-latest | 11371447067 | `b0c0f18bf8f4eabc84174d58cd8178c8e268442ab4217f03bd72fba1847b19a0` |
| app-test-results-macos-latest | 11371807170 | `1ddb80461a71648be02fab96071255ae0aa49612db338642a1380ee503e5a911` |
| windows-arm64-screenshot | 11372161729 | `6de6fc3240a8fe5c015cd3e5e0f3f983b23732e4729087a9f31c8c929d2a5495` |

Private retry root `FileCatReleaseEvidence/ci-37373490704-attempt2` contains
`independent-ci.json`, SHA-256 `a861defc5c65aa21d2b4a2f60602b21a232127e35f3769c3e0f7c9ac45b72755`. The separate
attempt-one provider proof in `ci-37373490704-attempt1/independent-provider-failure-v1.json`
is `787bae39097a185b0228753e1f0380c71e246bb593e107cfae346fd491be908b`;
its primary metadata, both exact failure annotations, notices and complete 38-member
log archive are retained. Both cancelled lanes have zero test steps. No failed
attempt is relabelled as passing or merged into the retry's results.

I142 is remediated preliminarily on committed/native/CI evidence; broader recovery,
drawn workflow, final candidate and human GO remain. **NO-GO** remains.
