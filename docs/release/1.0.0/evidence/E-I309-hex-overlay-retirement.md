# E-I309 — retired hex-editor edit storage

2026-10-10 CEST. Medium resource-retention defect within [I06](E-I06-resource-progress.md). Canonical parent **99ec926a9c8460e0512cf8ac7b1d01febcc4d108** plus the declared test/Core overlays is the actual producer; these preliminary assemblies are not relabelled as a committed or candidate build.

Closing an edited editor, or replacing its file through the actual private SwitchTo path, disposes the protected baseline but retains the overlay's original/patch dictionary arrays and undo/redo payloads. The held-owner regression records **26 failures/six live positives**. An in-flight controlled baseline read can also republish edits after disposal. The unchanged native observer independently reproduces twelve failures/six live positives in real Windows guest editor windows; every owned file remains unchanged.

Disposal now retires dictionaries without backing arrays, clears undo/redo and event subscribers, and closes the baseline once. Mutations check disposal under the same gate before publication, including writes whose baseline read began earlier. Cleanup precedes a baseline close error and preserves its original exception. Caller-owned SnapshotRanges copies remain unchanged; live undo/redo and replacement editors stay usable. No edit/save limits or file mutation policy change.

All **32 targeted controls, 497 affected passes/two existing skips, full App 1,619 passes/25 exact existing skips and full Core 3,482 passes/64 explicit skips** pass. Every 1,612 prior App outcome/message/exact skip remains plus 32 new passes. Controls cover two sizes, undo/redo/mixed histories, healthy/failing close, actual held closed/replaced editors, live positives and controlled late publication. The first attempted correction releases original storage but TrimExcess leaves capacity three; all 24 strict empty-capacity assertions fail while eight other controls pass. The final correction replaces the dictionaries. The initial runner's temporary-directory collision occurred before build and remains retained. The first independent reader refuses the partial producer's now-updated workspace input; the fresh reader verifies its immutable compiled copy against the original input hash, and rechecks every canonical Git blob/ZIP member. No original, partial or reader failure is relabelled.

The same compiled observer passes **54 final native controls** across Windows 11 Insider26300/x64, Ubuntu26.04.1/GNOME Xwayland/x64 and macOS27.0.1/Aqua/M1 ARM64. All **144 final compositor completions** and **17,694,720 complete overlay bytes** verify independently against known patterns; leased snapshots and both original/replacement file hashes remain unchanged. Baseline Windows adds its own 48 completions and twelve failures/six positives. All **72 native fixture roots** are independently absent; product/observer/runtime hashes and no owned observers verify. The owned Mac job/awake process are removed; Mac PrivateBytes zero remains unavailable. Payload/results/private scratch roots stay in campaign custody.

Native windows are observed through guest/background routes; no workstation UI or VM console is operated. SwitchTo controls do not qualify the full Save As dialog/journal workflow. Controlled late reads do not claim physical-device or native fault incidence. Compositor commits are not OS presentation/physical-input evidence; retired editor controls are deliberately held. This finite storage correction does not establish whole-process/native peaks, all I06 consumers, reference/human or exact-candidate acceptance. [Committed/native follow-up](E-I309-native-hex-overlay-retirement.md) now passes 497 affected/two exact skips and 54 native controls; [original CI](E-CI-hex-overlay-retirement.md) passes all 128 added records at 1da71e7. Those successor identities remain separate from this declared producer. I309 is remediated preliminarily; I06 stays open and physical-source HOLD/owner decisions/GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain exact commands, declared overlays, original failures/skips, complete inventories and owned restoration.

| File | SHA256 |
|---|---|
| `i309-hex-overlay-retirement-20261010-v1/independent-final-v2.json` | `5dbae14d7a156caae7b4d6e435995ac42b86919fc4fca104a277e2d5cd37223d` |
| `i309-hex-overlay-retirement-20261010-v1/finding-v1.json` | `6199b137615a77094ceb7611741c48d85849b3658a1a5c372678de41811be77c` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/baseline-v2/inputs.json` | `5f9bb1e30c44d1bda16f796ce565129577131db3c84951ec24802635290fc156` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/baseline-v2/controls/command.json` | `da1564d20e50249833f1d73a637f64449a0096f29e7380c3576967cc9df9c5d1` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/fixed-v2/inputs.json` | `efe5d10ced8c967a641a570f979156524726df4aa35c1a22395fd1bd9af5b42b` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/fixed-v2/controls/command.json` | `cadd062b96a49aef9cf2eaefbbc1b0a480f8106f17b3c6bed9fd360c17fb1c0e` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/fixed-v3/inputs.json` | `44f9bbd07aa5b92029baabbf71a104222cfd2588605647b27dffdc016eb6b108` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/fixed-v3/controls/command.json` | `7fb7f53feca86e4030b841cb99dc1dcde9cb22d1c662400f4b265668addbd607` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/fixed-v3/affected/command.json` | `bd36e43b5d5593701e9036285eaca99d60025a344a7077f9731f8d58ea5f564a` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/fixed-v3/full-app/command.json` | `5748de47d8f0932a95977bc48d904e2eaba6b988d6c251917d5057a8b0d4526a` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/fixed-v3/full-core/command.json` | `f888e3be188931d882670755a78e4831d6fe90ca8f7c58a68369de5b299ac88f` |
| `i309-hex-overlay-retirement-20261010-v1/native-original-v1/transport-final-v1.json` | `4009eb8893c1082b8507ec9aa6199c0e52c09ced6576eb608787fedaedc1995c` |
| `i309-hex-overlay-retirement-20261010-v1/native-fixed-v1/transport-final-v1.json` | `5e0ee805006925162b7cebfd15174ab0622c62e3ae21841a8e33f255e7e7313b` |
| `i309-native-linux-20261010-v1/linux/transport-final-v1.json` | `246e29683c8bf3e119b3a28d6ebb475416c9318860e7630a37b082a96e2cffce` |
| `i309-native-macos-20261010-v1/transport-final-v1.json` | `acd89fb119aa2961f131745f057664f8184516e1c5d47e8778ac07404f08d738` |
| `i309-hex-overlay-retirement-20261010-v1/independent-restoration-v1.json` | `a1d828915943ffbe4b65bfe438a699c47885a4765c5ec93d70d315ed0979cd9b` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/native-observer-v1.cs` | `af6fa984efa54fc44efd75fe9303b242beb805144e44cd4b85d0fc0c636fb113` |
| `i309-hex-overlay-retirement-20261010-v1/native-build-v1.json` | `35cb23d331f8d870db747bf1b156929c8b68b287fcb945a74580845b2841e87a` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/seal-v1.py` | `fdbd34fb9019055503855b953267ed240bfce2f5d8da1616cf509cc76a5a5d63` |
| `E:/FileCat/artifacts/release-evidence/i309-hex-overlay-retirement-20261010-v1/seal-v2.py` | `c394d98bf853ca6f799ee6f4eaf03c4e94476535858be8c37c3e4d1e249cadd7` |
