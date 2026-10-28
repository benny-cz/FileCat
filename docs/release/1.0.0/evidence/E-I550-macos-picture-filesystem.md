# E-I550 — macOS picture-worker filesystem boundary

## Identity and defect

- Baseline/source archive: `d1757bcac9044299b3bc322280c74fff3acf8513`, SHA-256 `8fddf305e95a59917640eeb5e7c3d046f273f28f6c72bd4ffc2a67d8804ac4c0`.
- Accepted implementation: `79bea6e592211c26e22805cffa897e4bed506a5b`.
- Accepted-source Git bundle SHA-256: `4577153b99f71262ed60f34cec4844cc110cb24a3a28bcc431d783fea781464f`.
- Raw evidence: `V:\FileCat\artifacts\release-evidence\i550-mac-picture-filesystem-20261028-v1`.
- Evidence manifest: 42 entries, zero rehash mismatches, manifest SHA-256 `1c9a4e9d46d1b3ce62454fc66e3176fed382b4c1664bf5629f37ba0e29b3fc71`.

The pre-change macOS policy denied process creation and network operations but left the picture worker with the ordinary user's filesystem rights. An exact old-profile control read an owned secret and created an owned file.

## Correction

`MacPictureSandbox` now removes all filesystem reads, restores Apple's installed system baseline and reviewed system/application/.NET runtime content roots, grants metadata-only traversal on exact ancestors of those roots, then puts filesystem-write, process-fork and network denials last. This ordering prevents the imported baseline from widening the final denied operations.

The first path-only version made Apple arm64e utilities abort in dyld. Importing installed `system.sb` after the global read denial restored the non-path-filtered OS baseline while the secret-read and file-write controls still failed as required. The first complete picture run then exposed eight `EPERM` `getattrlist /Users` events in a bounded root `fs_usage` trace. Exact ancestor-metadata grants corrected apphost traversal without granting content under `/Users`; the full picture suite then passed. All failed setup/diagnostic attempts and the losslessly compressed raw trace remain in the evidence package.

## Accepted results

On the physical Apple-silicon Mac (macOS 27.0.1, ordinary user, .NET SDK 10.0.101/runtime 10.0.12):

- focused policy controls: **3 passed / 0 failed / 0 skipped**;
- unlisted owned secret read refused;
- owned-file creation refused and no file left behind;
- reviewed application assembly read succeeded;
- actual worker denied process creation plus inbound/outbound network operations; and
- complete `FullyQualifiedName~Picture` App slice: **94 passed / 0 failed / 5 explicit non-Mac skips**.

Windows x64 passes the platform-neutral structure case with **1 pass / 2 explicit Mac-native skips**. The exact patched source builds the complete Release solution with zero warnings/errors. Its structured receipt pins the source archive, patch, TRX and MSBuild binlog hashes. Temporary Mac and Windows build stages were removed after retrieval; the Mac cleanup query found no remaining I550 path.

## Disposition

`I550-MAC-PICTURE-FILESYSTEM` is **Accepted** for preliminary macOS picture-worker filesystem containment. `REL-I08-CONTAINMENT` remains Open for Linux/other parser filesystem authority, whole-process memory, remaining parser/Shell routes and installed-candidate qualification. `GATE-CI-FINAL` is reopened for exact `79bea6e5` run 40766185355. Frozen-candidate qualification and stable publication remain Open/NO-GO.
