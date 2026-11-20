# Acknowledgment-aware accounting and retention — 2026-11-17

This continues the [private review checkpoint](2026-11-17-storage-review.md) on `main`, HEAD `68a435dc2a1d6122f5961f867bcb653b0e6968aa`, with uncommitted changes. WP3 remains partial. Windows App composition now gives the process budget the native evidence backend. WindowsPlatform still supplies NoStorageTransforms and the executor still refuses execution. EFS remains required by the active goal.

## Implemented

Standalone retained pairs can be loaded after journal pruning. The Core verifier reopens the exact acknowledgment and report, checks their private identities/digests and preserved historical facts, and separately checks whether a journal still exists. A retained report never becomes filesystem certainty. Private deletion rechecks journal absence while holding the artifact; it detaches/reopens the directory owner to stay within three handles. A stale snapshot or reappearing journal cannot authorize deletion. Report removal precedes acknowledgment removal, so failed report deletion leaves the pair intact.

Windows accounting uses native metadata and exact owned review status for each journal. A verified acknowledgment releases the pending-review reservation; active foreign owners remain pending and charged. Associated physical bytes are counted once. Retained pairs remain recognized after journal removal rather than becoming unassociated quota failures. Every native read checks the captured namespace identity. Unknown/unprovable artifacts remain retained and can keep accounting incomplete.

The process budget remembers the backend for each observed directory. Before admission it applies ordinary age/count retention and, when necessary, byte-pressure pruning. Candidate selection retains one exact snapshot, reacquires ownership and rechecks it before deletion. Only clean or exactly acknowledged inactive history is eligible. The budget recomputes a complete census after actual removal; it never credits assumed freed bytes. Native worker ownership is not held while waiting for evidence operations.

The default policy is 200 entries per history category/30 days, with journal-first pruning followed by standalone pairs. Core protocol tests preserve the dependency when a report deletion fails. Real Windows fixtures cover acknowledgment reservation release, post-journal accounting, byte-pressure admission, ordinary age/count retirement, and a blocked live writer whose metadata remains accountably pending.

## Verification

Builds completed with zero warnings/errors. Final focused Core selection: **235 discovered**, **234 passed/one missing-mount fixture skip on Windows**, **233 passed/two platform or fixture skips on Ubuntu and macOS**, zero failures. It includes the expanded retained-pair protocol tests and the previous lifecycle/journal/ownership selection.

The normal-user Windows 11 x64 NTFS run passed **27 native tests**, zero failures/skips. Its log confirms medium integrity. The new retained-pair case exercises the complete reservation lifecycle and private deletion after journal pruning. Byte-pressure and age/count fixtures preserve unreviewed history. The live writer case also verifies pending accounting during a blocked flush. Earlier compression/traversal/cursor/private evidence cases remain green.

The focused App Operations/Undo selection discovered 75 cases: **74 passed/one skip on Windows**, **73 passed/two skips on Ubuntu**, **72 passed/three skips on macOS**, zero failures.

Transport archive SHA-256 values, retained in `.tmp`:

| Artifact | SHA-256 |
|---|---|
| filecat-storage-retention-source.tar.gz | `7BD3669DFD09446F7D76B8337A2D363F89D98B749ECF98C51E6B6AB0C6FE281F` |
| filecat-storage-retention-native-patch.tar.gz | `30E00CF290CE2FBB270E49966ADEE593CB08807F72364BB093C1D70DA76C7488` |
| filecat-storage-retention-core-binaries.tar.gz | `51654A6C72994E6221FE5BD99FD41CB10BB970214A6A1293CE68B5B18403111C` |
| filecat-storage-retention-ui-patch.tar.gz | `92A68AD0A034210D67735CECBB7F5B1F4F0012C05EA718BEA9AF68DDCA9A5545` |

Markers: FILECAT_STORAGE_RETENTION_FINISHED on Ubuntu/Mac, FILECAT_STORAGE_REVIEW_NATIVE_FINISHED on Windows native, FILECAT_STORAGE_CORE_FINISHED and FILECAT_STORAGE_UI_FINISHED on Windows Core/App.

## Remaining acceptance

Directory discovery still uses managed enumeration around native per-entry verification. Finish the fully bound cursor/census route, large-history cost, dirty cross-instance observations, inherited malformed artifacts, partial-prune failures, and actual disk/device/crash faults. Do not treat this scoped accounting proof as the full 256+188+1+3 qualification. Startup/report loading and every recovery query still need to use this backend consistently; the standalone pair UI is not connected.

Complete the review/Operations UI and same-session publication, then the full bounded traversal executor and its overlap/root accounting. Remaining C native/provider/performance/UI gates, EFS identity/key/output work and the companion filesystem plan remain required. T-REC.C and T-LIMIT.C are still partial.
