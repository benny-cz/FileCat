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

Committed-source/native revalidation and successor CI are pending. Historical native
approval/refusal/replacement/removal captures retain their exact 8f75856 component
identity; their results are not relabelled as successor qualification. Actual drawn
workflow, broader helper/topology, final candidate and human GO remain unavailable.
No further human Mac dialog case is queued for this slice. **NO-GO** remains.
