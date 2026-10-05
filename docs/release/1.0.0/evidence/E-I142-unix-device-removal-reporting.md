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

Committed-source/native revalidation passes below. Successor CI is pending; its first
attempt is retained as a hosted-runner acquisition failure, and the exact same source
is being retried. Historical native
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
separate. Attempt two reruns all lanes at the same source; completion and complete
inventories remain pending. This is not a test failure or a qualified candidate.
No further Mac interaction is queued. Broader/native workflow/candidate gates remain.
