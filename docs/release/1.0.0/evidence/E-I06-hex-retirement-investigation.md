# E-I06 — unchanged Mac hex-retirement diagnostics

Exact **0beaa10c8a7bc11c307c34a0506d3a77669e98a4 /1583 raw blobs**, no overlays. The [original ec5868e Mac failure](E-CI-memory-content-retirement.md) reported eight old weak targets alive after close; its cause remains unproven. Product code, GC/weak assertions, iteration thresholds and timing allowances are unchanged.

All **32 host controls** pass. Thirty isolated Mac repetitions plus the same cases in a full App run give **992 native hex passes**: 744 retired-storage observations with zero retained arrays/capacities/undo/redo, 186 live-editor storage observations and 62 late-write refusals. Source bytes, caller snapshots, active undo/redo and close-error assertions remain. [Later original hosted CI](E-CI-backing-visibility-loss.md) passes all four lanes; that does not establish the historical cause.

The native full-App run stays **adverse: 1652 results/1534 passes/117 explicit skips/one failure**. RecoverySafetyTests.Renamed_apphosts_are_identified_by_their_bound_entry_assembly_and_incomplete_reads_are_unknown cannot find native payload/FileCat: the diagnostic was built on Windows and excludes .exe files. Every raw failure/skip remains; no complete native Mac App-suite qualification is claimed.

Independent postchecks verify **408 staged/runtime/root files**, absent owned workers and removal of 31 empty scratch directories. One host scratch file is archived/hash/exclusive-open checked and restored. Mac UID 501/ARM64 uses pinned .NET 10.0.12. No system settings/packages, host UI or physical source changes. Wider I06 native allocation/input/reference/platform/human/candidate scope remains.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence; nested records retain exact source, commands, raw failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `hex-retirement-mac-investigation-20261011-v1/independent-final-v1.json` | `d61a21d6f7d67b7475e2a739a66b2c2f4ae5a57eac6d0e72bb502aeb1e8732af` |
| `hex-retirement-mac-investigation-20261011-v1/retained-tool-sources-v1.json` | `20a9383d07ee2efda302ab7486ea4381a1c4dafe54ec315984ce384b06499b3c` |
