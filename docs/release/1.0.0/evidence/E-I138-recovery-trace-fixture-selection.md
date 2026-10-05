# E-I138 — whole-file trace control selects an intentionally partial recovery fixture

Preliminary validation repair, 2026-10-05, extending [E-I137](E-I137-macos-process-census.md).
Production is unchanged from I137's clean
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

## Clean committed successor

Clean **`593583e585d4a79cbb7ff961770a2d826858e14d`** is pushed to main. Only the driver and
release records change from f623297; production/build/workflow source is unchanged. A fresh
verified full export produces the Release/self-contained **osx-arm64 App** payload. The earlier
51 native census/five component checks retain their f623297 provenance; they are not relabeled
as new executions.

The complete recovery control passes **1/1**, zero skips, as ordinary benny/UID 501 on the
physical Mac. It confirms, opens the isolated raw source once, scans, selects **tiny.txt**, reads
its content through the viewer's reader, copies with **Completed**, waits **70 seconds** for timed
saves and closes/saves the workspace. This is headless MainViewModel/window/dialog execution;
the native desktop and F3 viewer window are not drawn or qualified.

The recovered 60 bytes exactly match independently generated fixture text, SHA-256
**`2c9aed8755e52ee19e225e615a4cb8a11fbc509343a3e0e93618d2cfe089fda3`**. All **844 Git exports**,
**1,196 payloads/1,197 input ZIP members**, output pins, case XML and independent tracked/root-path
process/temp/device cleanup verify. The unmounted source is attached writable; its complete
before/after image SHA-256 remains `19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`.
It is detached. This establishes an unchanged-byte preliminary session, not absence of transient
source writes; whole-process tracing remains mandatory.

All four required jobs pass in [CI run 37287555326](https://github.com/benny-cz/FileCat/actions/runs/37287555326);
three tag/manual package jobs skip. Four server artifact digests and six complete TRX inventories
verify. Full App: Windows **347/17 skips**, Ubuntu **319/45 skips**, Mac **321/43 skips** (364 each).
All 51 census/admission cases match the sealed host inventory, including every declared skip;
all 19 Mac census cases pass on Mac. ARM64's App 347/17 skips and package-start/installer steps
pass, with log totals rather than per-case TRX. The device opt-in driver is not run by ordinary
CI; its actual success is the separately pinned native successor above. I138 is verified
preliminarily, not Closed.

## Next administrator gate — calibration staged, not executed

Apple's [fs_usage manual](https://raw.githubusercontent.com/apple-oss-distributions/system_cmds/main/fs_usage/fs_usage.1)
states that wide output appends the thread ID. The installed manual agrees and is retained with
SHA-256 `516b37f33b209156bb6ebffef63aa8f14c7c5b5c21535e09340ebb499dc8f97a`.
Thus the first pilot's suffix is not a direct PID field; its specific native thread ID was not
recorded. Apple's [formatter source](https://raw.githubusercontent.com/apple-oss-distributions/system_cmds/main/fs_usage/fs_usage.c)
reassembles positional offsets from event arguments. Neither document proves correct offset
reporting on this running kernel; no ad-hoc high-bit removal is accepted as calibration.

The agent stages **`~/FileCatReleaseValidation/TraceCalibration-20261005-v2.command`** on the Mac.
Its **14-second** capture is limited to **two owned ordinary-user control PIDs**, with three
recorded native thread IDs and **18 known read/write events** at different sizes and offsets,
including offsets beyond 4 GiB. Three synthetic sparse files have about 4 GiB logical length
each, capped at **2 MiB allocated space each**; controlled low/high byte ranges and readbacks
are verified separately. No device or FileCat process is opened; no system settings change.
All workers have bounded termination and the results are retained. This tests field interpretation
and explicitly selected child coverage, not automatic discovery of future children or zero loss.

- Native root: `/Users/benny/FileCatReleaseValidation/trace-calibration-4d87888ac95a4073983b37e52a8ac4c8`.
- Controller SHA-256 `4a82b12748388af3577d0d78e79a8a959a43a987cfab3ff6f60bde9ecfe6dc64`.
- Launcher SHA-256 `2ddd4e0efac558495357c4f3f143d7d6589ffe5dfab32cdc9a770bb50f011e87`.
- Installed fs_usage SHA-256 `a0312eb9b2e93bccaa1ae3e09da6ecdf2502e5f7e29dcfcf053713e4d41d4b66`.

Controller/probe syntax and native staged-file hashes/permissions verify; **results do not yet
exist and it has not executed**. SSH `sudo -n` still returns password required. The owner must
launch this file in the Mac's Terminal and authenticate there; the agent stops at that credential
interaction gate after committing these results. Subsequent full tracing/authopen still require
their own qualified capture and native approval/refusal evidence.

## Evidence and limits

Private root: the authorized second workspace's `FileCatReleaseEvidence/mac-resume-20261005`.

- `i137-native-baseline-independent-v1.json` SHA-256
  **`eb62953c52c676aaa540f1e10d93d7a038cc4a6a97793bd8b011142c406da36d`**:
  exact committed producer, all input/output/case pins, actual admission, original failed session,
  known partial-file bytes, unchanged image and independent process/temp/device cleanup.
- `i138-host-independent-v1.json` SHA-256
  **`8ddaa276da1d304c1cd968bb2e4bd2558b954897485c7429b5cfbcbcd9b5e6a5`**:
  both host inventories, declared skips and working test-source hash.
- `i138-native-independent-v1.json` SHA-256
  **`ac8adc8f5ba7628b40efd696712683494e46470759e3fc5daa45ae4ff6b6070e`**:
  exact committed App producer, all source/payload/output pins, actual complete session,
  independent intact bytes, unchanged image and independent process/temp/device cleanup.
- `../ci-37287555326/independent-ci.json` SHA-256
  **`b83731a2763a6d41b4f257b61964223ffc63a21ed82a860beda6a7540b891fd8`**:
  four required jobs, four server digests and six full TRX/case/declared-skip inventories.
- `trace-calibration-request-v2.json` SHA-256
  **`7f0dc6e2dd622b381836e129a4d0cb83e2a578980b7410bbd0cf47b284644aae`**:
  staged script/launcher/native receipt, syntax checks and explicit not-executed state.

Whole-process source-write tracing, authopen approval/refusal, native desktop and candidate
qualification remain pending. The original failure and the verifier's expected-success assertion
failure are both retained; a separate baseline verifier seals the failed observation accurately.
Both VMs remain running, G: remains untouched/HOLD. Progress: **116/138 issue rows remediated**,
one separately Closed; **24/26 checklist steps partly or fully open**. No candidate or human GO;
overall **NO-GO**.
