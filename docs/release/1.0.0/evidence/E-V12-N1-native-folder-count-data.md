# E-V12-N1 - native sparse and hard-linked folder-count data

**Classification:** preliminary native-filesystem and Core/headless App evidence, not final qualification.
**Plan:** V12, AI-03, FS-003; extends E-V12-C1/C2 and E-I127/I129/I130/I131.
**Source:** `fdb17b453d515df437bce34eaaaf8f3626f7cb09` (clean fdb17b4). Later main changes through 545d627 are documentation only.
**Result:** all six cases pass without skips on each of Windows, Ubuntu and macOS: eighteen executions.
No production defect was found in this slice; issue dispositions and the release gate counts remain unchanged.

## Scope and independent oracle

Each native Python controller creates six isolated cases: sparse, hard-linked and mixed data, each with
and without an explicit listing refresh. Each case has 32 marked folders; the 31 controls each contain
37 known bytes. All data is under a unique owned validation root. There are no raw-device or USB operations.

- Sparse file: **8,589,934,715 logical bytes** (8 GiB plus 123), native allocation below 1 MiB. Windows
  uses FSCTL_SET_SPARSE and SetFilePointerEx/SetEndOfFile; Unix uses truncate and native block accounting.
  Known 4,096-byte head/tail vectors and one 4,096-byte middle hole are read and SHA-256 checked.
- Hard links: two counted directory entries name the same 997-byte native file as an external control.
  All three have one device/inode identity and link count three; folder count is **1,994 logical bytes**.
- Mixed: **8,589,936,709 logical bytes**. Marks add exactly 1,147 control bytes to each folder's total.

The probe calls the actual production DirectorySizer, CountFolderSizes, listing reconciliation and
QuickViewPane debounce. It checks exact bytes/files/subfolders, zero inaccessible entries, monotonic
progress with exactly one completion, and early cancellation at a progress boundary in a separate
two-file control. The App checks computed flags, no lower-bound/incomplete label, all 32 marked names,
focus, native folder identity, completed/retry state and the actual quick-view/status captions. The
three refresh cases retain those values after refresh. This is headless App interaction, not native
desktop input or frame timing.

Per platform, independent before/after snapshots contain 416 rows: 198 control files, twelve linked
paths in four groups, four sparse files and 202 directory identities. Snapshots match exactly for
size, allocation, device/inode, link count, file modification time and recorded bytes/range hashes.
The final verifier independently checks these vectors, expected totals, case inventories and observations.
Whole sparse-file hashes are deliberately absent; only the stated known ranges were checked.

## Environments and outcomes

| Native lane | Environment | Adapter | Cases | Sparse allocations (bytes) |
|---|---|---|---:|---|
| windows / win-x64 | Windows 11 Pro Insider Preview 26300; VMware UUID 9D224D56-1161-A849-ABA7-2581A980895C | WindowsPlatform | 6 pass / 0 fail / 0 skip | 196608, 196608, 196608, 196608 |
| linux / linux-x64 | Ubuntu 26.04.1, kernel 7.0.0-38, x86_64, ext4; VMware | PortablePlatform (production Unix adapter) | 6 pass / 0 fail / 0 skip | 12288, 12288, 12288, 12288 |
| mac / osx-arm64 | macOS 27.0.1 build 26A434, arm64 MacBookPro17,1, 16 GiB; owner Mac | PortablePlatform (production Unix adapter) | 6 pass / 0 fail / 0 skip | 36864, 36864, 36864, 36864 |

This run's native `sw_vers` and Python platform identity both report macOS 27.0.1. The earlier
`mac-owner-access.json` checkpoint reports 26.6.2/25G83 and is retained separately. This record
qualifies only the environment observed by the actual run; the reason for the difference is unknown.

Windows and Ubuntu consume the seven exact production managed DLLs from the earlier pinned clean
fdb17b4 Windows producer; no production recompilation occurs for those consumers. Native filesystem
operations run on their actual Windows/ext4 fixtures. macOS consumes an actual osx-arm64 producer
from the same source export: all 826 exported source files independently match their Git blob bytes,
or canonical CRLF-to-LF for NUL-free text. All seven Mac production DLLs match that producer's pins.
The Mac DLL bytes differ from the Windows producer and are recorded separately. No global dependency
installation or system-setting change occurred on the Mac.

## Exact retained provenance

Raw root (authorized second workspace):

```text
C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\folder-count-native-data-20261004
```

The final independent proof `independent-v6-windows-linux-mac.json` has SHA-256
`854bbe2a2246df17913718166ef75a0c402a45b54274260eeb1324762d8affaa`; it pins the verifier, complete XML case names/IDs, input/output inventories,
native identities, observations and post-run process census. The proof was generated at `2026-10-04T21:37:44.007185+00:00`.

| Lane | Input ZIP SHA-256 | Input manifest SHA-256 | Payloads / ZIP members | Output ZIP SHA-256 |
|---|---|---|---:|---|
| windows | `588ba0305e4ec0d492904a383b5fda590c5682e723f7b60a3c0145b0830bfb50` | `f29cb9f93e565330624ae3f0b5fd6085faffce400f10b2fb189db63f72b6f583` | 871 / 872 | `787e27c7e33159c3e92fe8dbe3a0a9467282d3dceda8eee6fed4df2bb986dc03` |
| linux | `528249d18fb60c5ed5e193da8fb3f284f6774dd54c6bb1e37ca44cf26520bfa4` | `bbbfa072b5c381b579da1b989e04b34b8c059f7bb0a54ab985012cb5b118ec9d` | 299 / 300 | `091bbb917d10299d47c729996ac36ed0b593c2f4a387efb9dd926a6f7adf71a3` |
| mac | `fb9f4061ba2f81142b4b8e321506a62a2ed612dfec1d59695b401b7b62665876` | `080669ca5e7c6a35e4cd00ff221985937c06b3e26f779fb07b60822391a8a721` | 311 / 312 | `793d581cbe7df55477076dd79b7414ae1378738eaf0917e942d89e8c99c5968b` |

| Lane | Results XML SHA-256 | Output manifest SHA-256 | Native process census SHA-256 |
|---|---|---|---|
| windows | `9028a26b4003c77710ae8ea74f7f05943a4a9d91be37b187fc60ebd95c23f936` | `c878bb49c647039eb1baaf245f5714f03f61f476daca2d89a89fdf5d7f1937c3` | `69f8b815dba1bb116fdc63951e26fc6877ce897c16b4b77abc9eca8d3a8796d4` |
| linux | `169b39df8f7245177ddf8fa13aacef2e66b3569a6d71a559f8a1ff3b93430480` | `2ee83bb548b287cffe2e97d6f254bb929a22f068b323df10af600426d2fd0c56` | `8ab74bb09898eb9ce2c96576eaa47e38c7bd5a2b1acb8e6ba9005bf1198d4ce0` |
| mac | `95dfb3c148e1522a983159916988816df474979ae599f7f35c419b3adce22edb` | `0eb485939faab1800eab0c88443ee89600d439727d045ad9baa37d29dbdf3a45` | `7f293e415fc83640fb33c725f26ec3346b19a994c3d89bb41daf21b4aff3a640` |

Original Windows producer manifest: `9d4e1891d8f2f13908efe6791c01981f4642f1e7cccca824a7f4a341ea39dfc3`.
Mac source archive: `fe657cd9cffffaec1ac8a8dc9bee47e7e47e50026de420337060915a3ca33279`; Mac source record:
`f4566ea11949b96ac6894089ad899533f02fee34785c64dbcbc1873ad74a2c00`. Full per-file pins and producer commands/logs are retained in
`production-inputs.json`, `mac-producer-source.json`, `mac-production-inputs.json` and publish records.

Each `native-v6-<lane>` directory retains the launch request, bootstrap, remote receipt, output ZIP,
extracted raw outputs and process census. `bundle-v6-<rid>` and `<rid>-v6-input.zip` retain exact inputs,
probe/controller sources and native self-contained payloads. The verifier checks every payload size/hash,
ZIP multiplicity/path, complete output inventory, six XML passes and independently read observations.

## Cleanup and retained failed attempts

- windows: `C:\Users\Public\FileCat-countnative-validation-64f8bd8a637e41f2844812eafc1d6ef8`. Recorded PIDs 7204, 8220, 14112 and owned executable children are absent at `2026-10-04T21:28:04.933646+00:00`; owned fixtures are absent and process temp is empty. Inputs and evidence remain.
- linux: `/home/benny/FileCat-countnative-validation-4c3795f385a04afcbc9842891647f553`. Recorded PIDs 73870, 73873, 73879 and owned executable children are absent at `2026-10-04T21:28:29.676677+00:00`; owned fixtures are absent and process temp is empty. Inputs and evidence remain.
- mac: `/Users/benny/FileCatReleaseValidation/countnative-85d423dc793b45d09d3112b15b7aad95`. Recorded PIDs 2249, 2256, 2264 and owned executable children are absent at `2026-10-04T21:34:28.110661+00:00`; owned fixtures are absent and process temp is empty. Inputs and evidence remain.

Earlier attempts remain retained and failed. The initial compile/setup and allocated sparse-file
precondition failures are harness failures. Garbled non-ASCII expected-caption literals caused six
failed cases on each x64 lane; corrected ASCII C# Unicode escapes preserve the same production DLLs
and test assertions. A first Mac consumer could not load the Windows x64 App DLL, so it cannot prove
Core/App behavior. The actual Mac RID producer fixes that consumer architecture. Its initial source
comparison also rejected Git's text newline normalization before compilation; the corrected independent
comparison checks every source blob and does not normalize binary files. Three early independent
verifier sources are retained: erroneous production inventory and Unix adapter assumptions were corrected
against the actual pinned seven-DLL inventory and PlatformFactory's PortablePlatform route, without
changing the probe or any production bytes.

The final Mac SSH wait timed out after 270 seconds and a follow-up SSH connection timed out. After the
owner confirmed availability, the saved receipt/ZIP were retrieved and independently verified; the
remote controller completed successfully. Its original transport logs and `transport-disposition.json`
remain retained. The exact cause of that transport interruption is unavailable and is not attributed
to FileCat. No new run replaces the disconnected one.

## Limits and remaining work

This covers native sparse/hard-linked data and exact logical-count/mark/focus/caption reconciliation.
It does not qualify allocated-space accounting in folder totals, throughput, native Esc/input-to-frame
or accessibility, slow/cloud/hung devices, other filesystems, Ubuntu 24.04, minimum supported macOS,
GA Windows, installed packages or a candidate. Existing native UI automation startup failure remains.
Both VMs stay running; G: is untouched and its historical source-change gate remains held.
The 24 of 26 partly/fully open release steps and 109 of 131 remediated issue rows are unchanged.
No candidate, stable publication or human GO exists; overall **NO-GO**.
