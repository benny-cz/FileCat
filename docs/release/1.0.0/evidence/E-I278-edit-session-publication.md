# E-I278 — close owned edit sources before session publication

2026-10-09. Preliminary I06/V07/V08/V11/V23 evidence at exact original **a8cee66ebe690752da3a67676b84d9ac960a8690**, plus declared test/fix overlays. The actual headless F4 preparation flow proves two related defects: closing archive/server content can replace a read/revision/invalid-count error, and server preparation saves a session before source closure. With a standalone close failure, the original remote flow reports failure but leaves a saved session/working copy. This does not establish native provider fault incidence.

The correction gives server F4 preparation an owned-content CreateRemoteFrom path in EditSessionStore. It copies and closes content within the unpublished session transaction, then writes the origin mark and record. The existing borrowed-source CreateRemote signature and ownership remain unchanged. Archive preparation uses the same close discipline. On a copy failure, closure is attempted once, a secondary close exception is logged and the original error is rethrown. A standalone close failure prevents publication and the transaction removes its owned working directory. Admission, revision/length/EOF/CRC checks, worker routing, explicit commit and external editor policy retain their prior scope.

**96 identical original/fixed controls** execute real headless F4 commands: ZIP/server schemes, healthy/read-IO/read-denied/invalid-count/initial-revision/late-revision outcomes, four close configurations and seekable/non-seekable sources. Each source is an actual owned read-only FileStream behind a controlled provider; ZIP catalog/CRC/baseline checks use an owned real archive. No network, editor process or workstation UI is used.

Original results are **68 failures/28 passes**: sixty primary read/revision/error notifications are replaced or escape as a secondary close error; six healthy remote copies leave a published session after close failure; two otherwise successful remote copies additionally demonstrate closure after publication. Those last two fail the ordering oracle, not a failed user edit. All original streams already close and permit exclusive access; no original handle leak is claimed.

All **96 corrected controls pass**. Closure observes no published record; only the four fully healthy copies create a session with exact bytes. Every failed copy/close leaves no session or session directory. Expected primary failure notifications remain, standalone InvalidOperationException close objects/stacks remain observable, every source closes once, source/archive hashes remain unchanged, actual I/O calls run off the headless UI thread and an unrelated worker returns 42.

The **same unchanged compiled App payload** passes **287 edit cases**, preserving all **191 preceding edit outcome/message multiplicities** including admission, preparation, demand, commit and save-copy controls. This is not a full App-suite replay. Complete Core and Remote regressions pass **2829/64 exact skips** and **2276/156 exact skips** respectively; every prior 2893 Core logical and 2432 Remote outcome/message multiplicity remains. One Core PE-inspector display label changes with its actual isolated assembly path; the identical defined method is verified and only that label is adapted. All actual labels and duplicate multiplicities remain, rather than claiming byte-identical display names.

Both source exports contain **1321 canonical original Git blobs**, plus only the identical new fixture and, in the corrected producer, two production overlays. Complete commands, locked restore inputs, actual payload hashes, raw TRXs, original failures and semantic comparisons remain. Exact committed/four-lane qualification of I278 follows below; [I277's separate completed audit](E-CI-edit-admission-preservation.md) applies only to its own earlier source.

All **288 recorded source paths and fixture directories are absent**. 22 owned temporary files are archived/rechecked, 13 removed and 9 exact compiler/analyzer locks retained. Root absence in original/fixed/edit/Core/Remote order is false, true, true, true, true. Earlier retained locks keep their separate qualifications; no global compiler termination or restoration claim occurs.

Twenty broader unresolved entries, all 24 final-candidate campaigns, the I106/I110 physical-source HOLD and owner/GO gates remain. No workstation UI, VM/Mac, account/settings, physical-source or stable-publication change occurs.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute. Nested receipts preserve full source exports, overlays, commands, actual payloads, failures/skips and restoration inventories.

| File | SHA256 |
|---|---|
| `edit-publication278-v1/preparation-v1.json` | `4a8906beafb2d5d16920bf452bae0d64ae94a33d1957a07abb8ca12c9bd70117` |
| `edit-publication278-v1/correction-preparation-v1.json` | `92b8e29976d611a9bbc117f1c7b330444c2ae6beaf2965971d31be0c77072986` |
| `edit-publication278-v1/regression-preparation-v1.json` | `c2685b1135c2a468cd7d93f4c82e605c2b708226f098a40cd9a83fa59ae5dfec` |
| `edit-publication278-v1/original-Core-EditSessions.cs` | `0c2218068e41312e5044ff29c2fdb7917679914c0d215781c153570c4f8b9751` |
| `edit-publication278-v1/original-MainViewModel.EditSessions.cs` | `2cb2eb8e364aca91130c861f86505a659d763645a3a5bf96a920b0d6cc523a79` |
| `edit-publication278-v1/Core-EditSessions.cs` | `fb39496b125bdb0b189f9da7d066399ce4293c2592ff32cf43771ca2a442073c` |
| `edit-publication278-v1/MainViewModel.EditSessions.cs` | `f28317339b8c81b1a2749c90493b9849d007b1af471be43369c2a80b76c2ff93` |
| `edit-publication278-v1/EditSessionPublicationTests.cs` | `e908c92d506dd343e9d8ffdbadcc0110d7ef820c99f574c2cb53f02a24345648` |
| `edit-publication278-v1/run-edit-publication-controls-v1.py` | `b48166134c1b7e2c21acfeabf2b6110789610b7829a42e3dd54150ac9a10f721` |
| `edit-publication278-v1/run-edit-regression-v1.py` | `405099068b347e362e77b59767358b3bc8e1591456e7b1b7b8d97d185a7f8e67` |
| `edit-publication278-v1/run-core-remote-regression-v1.py` | `f75b035c31d2eaf1593ac8f0245c36afb4108ad6bdd2d35087afe4a3849a1e3b` |
| `edit-publication278-v1/seal-edit-publication-v1.py` | `bb836e02f3a14e737f7211fbcf69fd03bcf3c22e5b8dec2ce8ac1b1ef5ac7491` |
| `edit-publication278-v1/independent-edit-publication-final-v1.json` | `887c53551e89d0d2081927ab2c8d084d569d97801db60d0bd1acc442c77b7f54` |
| `E:/FileCat/artifacts/release-evidence/edit-publication278-v1/original/command.json` | `8ba2286ca42d964a377a6cf1546010d67cc484570f55d62ae9835a71d14c0c7e` |
| `E:/FileCat/artifacts/release-evidence/edit-publication278-v1/fixed/command.json` | `07c956e8a1b94a61124aa5e30931c912b2b81a0e1093f01d3c5d389e45820a41` |
| `E:/FileCat/artifacts/release-evidence/edit-publication278-v1/fixed/edit-regression-v1/command.json` | `316ee93435c07cfbbfd1ef4626e32d7861bea7560c4a4bdcf2954c47cf1f73a9` |
| `E:/FileCat/artifacts/release-evidence/edit-publication278-v1/fixed/core-regression-v1/command.json` | `d297a9da8990d8c37d52b2a0feef4170617001fabc43f8fd12645158e752c51a` |
| `E:/FileCat/artifacts/release-evidence/edit-publication278-v1/fixed/remote-regression-v1/command.json` | `77e542825a4ef16ff29531841e7ea36e45e6d3dd3f41785f54a28d1874a28716` |

## Exact committed and hosted follow-up

Canonical **0dc7380** repeats all **96 controls successfully**, with **1324 exact Git blobs and no overlays**. The committed production and fixture bytes exactly match the privately tested bytes. Independent reading compares all semantic observations except actual paths, ZIP timestamp-derived archive hashes and actual stacks; every individual archive remains unchanged. All source/fixture paths are absent. One owned temporary file is archived/rechecked and removed, with no locks and that root absent. No full committed App/Core/Remote-suite replay is inferred.

[Original four-lane run 37950046170](E-CI-edit-session-publication.md) passes all **384 I278 executions and 27,236 actual records**, preserving every predecessor multiplicity and exact skip. A prior GitHub discovery call retains its actual TCP connection error; the combined shell's final exit status is not misrepresented as the unavailable individual gh exit status. The fresh bounded discovery succeeds with separately captured stdout/stderr. There is no product/CI failure or retry inference.

These checks apply to I278's exact producer and do not qualify I279's later upload-target correction or close native/candidate scope.

| File | SHA256 |
|---|---|
| `edit-publication278-v1/committed-source-comparison-v1.json` | `1ab9517d96a3b5a989654740a45ce29d7ea384db3b712dfdc18d7dc03ae9162d` |
| `edit-publication278-v1/exact-repeat-preparation-v1.json` | `4e3500fe944fcb72eeb7899211f98f4a77ed0b15ef85ba4db48ae8e9555200c9` |
| `edit-publication278-v1/run-exact-committed-edit-v1.py` | `0f47c7d33bb9885c1754d7ed447da7ba5e192a0dfc351a1d14dfe91d193285bf` |
| `edit-publication278-v1/seal-exact-committed-edit-v1.py` | `2306be47f51759b2435c8a7da2d54ccb15ee78d3a6e5c3a1e44bebd792b2cdc6` |
| `edit-publication278-v1/independent-exact-committed-edit-v1.json` | `5d9440a8f6eb3dcf0dadff0cae9cc4bc1a86b818f0f5ca1f0c8387b728c502e9` |
| `edit-publication278-v1/edit-publication-main-push-v1.json` | `770f7090329fe49d35e274bc9faed0052dd0f578c0c55b7d9e22043acaf90e7f` |
| `edit-publication278-v1/ci-discovery-connectivity-v1.json` | `6c266311ab5f154856054a953b308a0d0d77cba9f9f6b39bafcc69060385b6fe` |
| `edit-publication278-v1/ci-discovery-v2-command.json` | `ca92b803039ccdfcad41dab2b3880b75474e50598108dfcfff46178f0e7df40a` |
