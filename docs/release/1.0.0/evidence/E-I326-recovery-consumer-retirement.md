# E-I326 — retire a closed recovery reader's private payloads

2026-10-10 CEST. **Potential High, aggregate resource ownership; remediated preliminarily.** `RecoveryContent.Dispose()` previously did nothing. A retained closed reader consequently held its volume window, recovered item and parent scan tree, resident payloads, missing-range list and decompressed unit. Session-cache bounds do not retire those references from a closed consumer.

The final baseline uses **55deaa3a6a0c103b000c56a66874512623b928f9 /1489 canonical raw blobs** with only declared tests. It reproduces **ten failures/four live positives** on the Windows host and independently on Windows, Ubuntu and Mac. Four closed reader cases retain their private graphs; three concurrent-close cases return from disposal while the read is still active; three source callbacks close the reader reentrantly. These are actual recovery readers and owned regular 64 KiB files, with controlled read gates/callbacks. No physical source opens. Managed reachability is measured while the closed wrapper remains held; this is not a process-memory peak or an aggregate acceptance result.

The correction serializes reads with disposal, detaches the reader's volume/item references and clears decoded/missing storage. Metadata snapshots remain available. It leaves the session-owned shared source open and preserves caller-owned metadata and missing-range snapshots. The read and missing/decode publication paths also recognize reentrant closure: an initial correction passed the eleven-case subset but the expanded matrix retains **two further failures/twelve positives** when a callback republishes decoded or missing data. The final guards prevent that publication. A storage-capacity extension then retains two more first-correction failures: clearing missing ranges leaves their allocated list storage behind after ordinary/concurrent closure. Final disposal trims that storage to zero capacity while preserving caller snapshots. Earlier native templates inherited a contradictory exactness flag; those raw transports remain retained, and final manifests explicitly declare overlays and deny exact-artifact qualification. Repeated disposal is harmless, and subsequent reads refuse a closed reader.

All **fourteen final controls** and **95 affected passes/19 exact predecessor skips** pass on the host. Every **114 affected outcome/message** and all four original live-positive outcomes remain. Windows, Ubuntu and Mac each pass the fourteen final controls (**42 native passes**); all fourteen actual observation records equal their host counterpart. Exact known bytes and hashes, live-reader reachability, retired item/tree/payload/unit references, source non-ownership, caller snapshots and the two close timing routes verify independently. All **262 staged/1306 reused runtime checks**, measured Windows parent/child RID8192/session1, Ubuntu UID1000, Mac UID501 and owned process/temp restoration verify. Original fixture compile errors and an attempted no-build run without a valid fixture assembly remain failures; they are excluded from qualification. The earlier eleven-case native subset remains separately retained.

This closes one reader-retirement defect under I06. It does not establish whole-process/native peaks, aggregate active recovery scan limits, device identity or session-eviction leases. Active-source topology/races, I106/I110 physical-source HOLD, required reference/human and exact-candidate qualification remain. No workstation UI, persistent guest setting, freeze, candidate or publication change.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain exact source, overlays, payload/runtime bytes, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i326-recovery-consumer-retirement-20261010-v1/independent-remediation-v2.json` | `f84a2964205c233ff6adb1d6e05d8f0b3f5be8c4f73acdb2fd83e29703cf191d` |
| `V:/FileCat/artifacts/release-evidence/i326-recovery-consumer-retirement-20261010-v1/baseline-v4/inputs.json` | `c52dc765e0a670bbb8dda4b7b87a51895a5e0b6b52ff73204d43d914fefb1f84` |
| `V:/FileCat/artifacts/release-evidence/i326-recovery-consumer-retirement-20261010-v1/first-fix-v1/inputs.json` | `efd1cd45fe587c21710c2747184a7b74222994e6db20b6cccb5161b70691cc84` |
| `V:/FileCat/artifacts/release-evidence/i326-recovery-consumer-retirement-20261010-v1/fixed-v4/inputs.json` | `34388479f4537200f1c16ae0283de9d41ca8eb6f64e14cca9af501e5b87155d6` |
| `i326-recovery-native-baseline-windows-20261010-v3/native-medium-v1/transport-final-v1.json` | `886d8dfd5b2bc8d98f465ec9757069582e8b36c9758573a67779f0d029b06ef0` |
| `i326-recovery-native-fixed-windows-20261010-v3/native-medium-v1/transport-final-v1.json` | `86fc09f2c8ec7af50d7852dc029b2d323067a641afed9d6466db33c97dd9ac0e` |
| `i326-recovery-native-baseline-linux-20261010-v3/linux/transport-final-v1.json` | `b4f8f33a8ade83710a7a80395741d701b3f89642ebc022118b07141ca9da3916` |
| `i326-recovery-native-fixed-linux-20261010-v3/linux/transport-final-v1.json` | `9d38d601174c6f6688b71f12a0056c1f62627003bd3d1a801ba8070f92dbb5a0` |
| `i326-recovery-consumer-retirement-20261010-v1/baseline-macos-v3/transport-final-v1.json` | `e264d6377da1ff69d169e6e530a64e182a54f51b7637220ad42f4b793eecccf5` |
| `i326-recovery-consumer-retirement-20261010-v1/fixed-macos-v3/transport-final-v1.json` | `0c26b4f24a26f7b5453a894169499c2cf393afb4b4ec8a480bc3182eb28c068e` |
| `i326-recovery-native-baseline-windows-20261010-v3/postcheck-windows-v2/independent-final-v1.json` | `644cb8de42051f5f458667f73c159fd86c49b7181e1c236ec75766ea09f6e99b` |
| `i326-recovery-native-fixed-windows-20261010-v3/postcheck-windows-v2/independent-final-v1.json` | `504a3072f6d640142a7432e2e2b51436014116dc76d7ced06f90777a3e6bd242` |
| `i326-recovery-native-baseline-linux-20261010-v3/postcheck-linux-v2/independent-final-v1.json` | `8ebe7929ce1a5d5d59b394d26f1c32c041a6c2b477bd1944ab0c55778a56ba64` |
| `i326-recovery-native-fixed-linux-20261010-v3/postcheck-linux-v2/independent-final-v1.json` | `d1d43923637bfd8e92d05dd46d6949b20f7d437afdbc6c935f007c476ff443c5` |
| `i326-recovery-consumer-retirement-20261010-v1/postcheck-v2/independent-final-v1.json` | `d1d16ed93ec09e0b04068dd0d9056e687a61320ba3bcf6262ed786fb8820dc5f` |
| `i326-public-20261010-v1/retained-tool-sources-v3.json` | `17c540822a29d503802c798e1f22a58aedfdca433412e5894e6362c6ccf0919a` |
