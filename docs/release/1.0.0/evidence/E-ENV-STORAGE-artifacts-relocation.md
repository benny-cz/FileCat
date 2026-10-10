# E-ENV-ARTIFACTS-V — verified artifact relocation

2026-10-10 CEST. Owner-authorized physical artifact storage is now **V:\FileCat\artifacts**. **E:\FileCat\artifacts is a junction to that directory**, preserving every historical absolute reference and existing repository-relative script path. Fresh qualification/CI artifacts use V: directly. Native directory handles and file IDs independently confirm both paths reach the same V: directory.

The original inventory contains **675,279 files and 206,385 child directories**, **295,196,238,177 logical bytes** and **196,783,053,110 recorded compressed/allocation bytes**. There are no reparse, encrypted or sparse entries. Independent native stat observations verify all 675,279 files have one link; the initial fast directory enumeration did not expose native IDs/link counts and is not used to establish that result.

A checked Robocopy copy retains data, attributes, timestamps and alternate streams; originals remain until independent full verification. Every original name, default/alternate stream set, stream length and SHA256 matches its V: copy; **675279 streams /295196238177 total stream bytes** verify. Source metadata/membership and source/destination identities are rechecked before retirement. Original owner/group/DACL descriptors and cross-volume identity/security differences remain in the complete immutable ledger; old native-identity evidence is not relabelled. New campaign writes can advance destination directory timestamps.

After complete verification, the original E: tree is renamed to one checked staging path under E:\FileCat\obj, the compatibility junction is created and independently resolved, then **675,279 original files and 206,386 directories including the root** are removed with per-file checked identities/size/time. No unverified recursive deletion is used. The staging tree is absent. Observed E: free space increases by **197,335,744,512 bytes (197.34 GB)** during retirement; concurrent workstation activity prevents attributing that whole volume delta exclusively to this operation. All verified artifact content remains on V:.

The first sandboxed copy returns exit zero despite ERROR 5 and zero copied files. Its raw log/receipt remain; qualification requires the later log to contain no errors and full independent content verification, not an exit-code-only check. Three reader guards refuse before qualification. The first compares enumeration size zero with native stat 4096; the second retains its full raw checkpoint without assigning a cause. The final full stream-hash run verifies all bytes but its raw name comparison refuses because inventory paths use forward slashes while native Windows enumeration uses backslashes. A fresh separator-only control establishes exact name equality without case folding or changing names; every source/destination identity, file size and timestamp is then checked again. Fresh native directory controls and all raw refusals remain. A fresh reader treats directory size separately while retaining source names, attributes, timestamps, identities and every regular-file/stream hash check. Private C: receipts still contain unique referenced evidence and are not discarded. DEC-10 owner-controlled read-only custody remains unresolved; relocation is not REP sealing, candidate qualification or publication. No host UI, VM configuration or physical-source validation is performed.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records preserve exact producers, raw commands/results, original failures/skips and restoration. The E: compatibility junction preserves historical paths; new artifact records use physical V: paths.

| File | SHA256 |
|---|---|
| `artifacts-relocation-20261010-v1/inspection-final-v1.json` | `ed0872e678c69e0e7786c04283e84f6b8ced93bed30af0a44a930b68051d0cd6` |
| `artifacts-relocation-20261010-v1/source-metadata-v1.jsonl` | `73a3afd10deec5e1638736d98e33bc735652efdc41b334f83363e3653acb8966` |
| `artifacts-relocation-20261010-v1/copy-final-v1.json` | `b0b98fdc0b56d7a663bc08289943b08c6ef77ba7ae5f44211408d9b631cb1822` |
| `artifacts-relocation-20261010-v1/robocopy-v1.log` | `2b963072b0c126acc1e425febc58e9a96ab45d9cde5a51fa81a59bd497287531` |
| `artifacts-relocation-20261010-v1/copy-final-v2.json` | `02a0d6accf58d947f1a72844455a27176b9b6ccdc4f5fc3172f41fe080cd74dc` |
| `artifacts-relocation-20261010-v1/robocopy-v2.log` | `1ba78ef9cd604b3859cc6471e072958cdca6bf40e808c242054ef7fcf06b59bd` |
| `artifacts-relocation-20261010-v1/verification-final-v5.json` | `036bcdb8cd0b943a7ed2609081c370685881fd4061f6f8cf214c9f749d436119` |
| `artifacts-relocation-20261010-v1/verified-content-security-v4.jsonl` | `d1f21ff33dd526b46a8fb2cc8855764448c622a0812a7edac5192801a6afe406` |
| `artifacts-relocation-20261010-v1/native-identity-stream-audit-v4.json` | `9927a656df4fd52758d1f86b8b9cae5e3d253825cf487665bf506c9c0f95412a` |
| `artifacts-relocation-20261010-v1/cutover-final-v4.json` | `f9ec21de29453174b53f5e9034424588483678ab1a7f3b85268ddbd0c38918d9` |
| `artifacts-relocation-20261010-v1/native-alias-predeletion-v5.json` | `b5ffd8e6c2dc583125f8ba6766d19417f2f63097c3bfa126f03a05c687b1bcc1` |
| `artifacts-relocation-20261010-v1/retirement-final-v5.json` | `a2f681856d99837375f7aaf35155656958ad635a1e7cf61d1886c43fd527bee2` |
| `artifacts-relocation-20261010-v1/retired-files-v5.jsonl` | `835d6802009020dc14792d5188730c6280cd36ce74bc4ec3765bd094cd93ea12` |
| `artifacts-relocation-20261010-v1/verify-copy-v4.py` | `b9687ff55e33fbe64836c0727778da992d92201873311d4d415a827a772fa971` |
| `artifacts-relocation-20261010-v1/retire-original-v5.py` | `12e27358b354b08a7e3cddc3151cad1e8f42ae877bb8127eda5c3816c04e12d1` |
