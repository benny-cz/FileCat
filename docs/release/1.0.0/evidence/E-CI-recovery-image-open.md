# E-CI-I322 — original image-ownership CI retains a Mac failure

2026-10-10 CEST. Original **38072748133 attempt1 at 156b723cd652081bcf64cd5b56638b3283ee489d** fails the Mac lane; the other three required lanes pass. **24 actual digest archives/12 TRX/27,478 records** retain **26,603 passes, 874 explicit skips and one failure**. All **120 current added topology/reselection/image ownership controls pass**, including twenty image controls. All **1481 raw source blobs, 92 locked restore graphs and four clean SDK10.0.401 receipts** verify.

The Mac mount target is unknown and its first diagnostic collector then fails on a non-mount query. Exactly that predecessor pass becomes a failure; all other available predecessor outcomes/messages/skips remain, plus 120 added passes. Two downstream Mac inventories, covering **4168 records from the previous complete green run**, remain unavailable, not passed or skipped. The adverse reader rejects qualification. No rerun or workflow mutation.

[Exact I322](E-I322-native-qualification.md) remains separately successful. I323 corrects the diagnostic path; [I325](E-I325-mac-backing-fixture.md) retains the subsequent c0677d9 job's actual unavailable backing replies and corrected finite oracle results. Those later replies do not prove this earlier run's hidden cause. Wider hosted/platform/candidate/source safety qualification remains.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain exact inputs, compiled artifacts, commands, original failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i322-ci-20261010-v1/independent-adverse-ci-final-v1.json` | `e8718b0c7596d2788fb46f62595c492722d7d29066b82daf2589dd18950f5d45` |
| `i322-ci-20261010-v1/actual-available-observations-v1.json` | `fefbdec9775a1142d6454fb04967aa011b8287431c875c7d83c892b307e98d2c` |
| `i322-ci-20261010-v1/watch-final-v1.json` | `d871e292f07d91b6424faf1fb9974d134ab5aabeb6572f63515959cb0462507c` |
