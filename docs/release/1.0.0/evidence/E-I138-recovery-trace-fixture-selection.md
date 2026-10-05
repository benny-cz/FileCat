# E-I138 — whole-file trace control selects an intentionally partial recovery fixture

Preliminary validation repair, 2026-10-05. Production is unchanged from I137's clean
`f62329788630514251a87d2524f256f978bef031`; no candidate exists.

## Retained failure and independent bytes

After I137, the Mac full recovery session reaches confirmation, opens the owned raw FAT16
device once and lists its deleted files. The driver selects the first readable file,
`frag-a.bin`, although this fixture is intentionally **Partly lost**. FileCat correctly copies
it with **CompletedWithIssues**, reporting 24 KiB lost at bytes 16,384–40,959. The test omits that
finished state from its wait predicate, waits roughly two minutes, then incorrectly demands
Completed. This is a validation-driver defect, not a demonstrated recovery defect.

The independently retrieved 40,960-byte file exactly matches the generator's first 16,384
bytes and 24,576 zero bytes for the known lost range. SHA-256:
`29ff7e7780767d6f2b74db8ad9b22150ca36d64eb884e4dc9364e089888ca50f`.
Core's existing partial-recovery tests independently require this warning/result/zero-fill behavior.
The original native session remains failed; successful admission does not relabel it a pass.

All **843 Git exports**, **1,516 payloads/1,517 input ZIP members**, 51 admission/census cases,
five native control XMLs, output pins and owned cleanup independently verify. Mac admission
checks pass **44/51**, with seven declared Windows-only skips; **all 19 census controls pass**.
The writable source image remains SHA-256
`19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`, and is detached.

## Correction and verification checkpoint

The whole-file driver now chooses a nonempty file explicitly reported **Recoverable**. Partial,
uncertain and unavailable entries are ineligible for this positive whole-file control. The job
wait uses the shared finished-state predicate and still requires **Completed**; partial success
is not accepted as a whole-file pass. Existing partial/adverse controls remain separate.

Affected working host App: **40 passes, 12 declared skips**, including the actual-device trace
case's explicit opt-in skip. All **ten existing Core recovery-job controls pass**, zero skips;
these include whole-file and correctly partial copies. Complete TRX inventories and working
test-source pins verify. Clean committed native successor and CI are pending at this checkpoint.
Prior full host/CI evidence belongs to the unchanged production source and retains its own pins.

## Evidence and limits

Private root: the authorized second workspace's `FileCatReleaseEvidence/mac-resume-20261005`.

- `i137-native-baseline-independent-v1.json` SHA-256
  **`eb62953c52c676aaa540f1e10d93d7a038cc4a6a97793bd8b011142c406da36d`**:
  exact committed producer, all input/output/case pins, actual admission, original failed session,
  known partial-file bytes, unchanged image and independent process/temp/device cleanup.
- `i138-host-independent-v1.json` SHA-256
  **`8ddaa276da1d304c1cd968bb2e4bd2558b954897485c7429b5cfbcbcd9b5e6a5`**:
  both host inventories, declared skips and working test-source hash.

Whole-process source-write tracing, authopen approval/refusal, native desktop and candidate
qualification remain pending. The original failure and the verifier's expected-success assertion
failure are both retained; a separate baseline verifier seals the failed observation accurately.
Both VMs remain running, G: remains untouched/HOLD. Progress: **116/138 issue rows remediated**,
one separately Closed; **24/26 checklist steps partly or fully open**. No candidate or human GO;
overall **NO-GO**.
