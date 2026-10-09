# E-I280 — preserve compared synchronization targets through publication

2026-10-09. Preliminary I06/V02/V13/V23 evidence at original **08399b2d71ff5a6f72cecc7e34c609950b765212**, plus explicitly declared fixture/production overlays. Synchronization checks compared target metadata before copying, but an absent target passes that check and staged publication does not repeat it. A target changed during a copy or while a publication failure waits for Retry can therefore be replaced without a fresh comparison.

The correction requires the target to remain an ordinary file with its compared length/time before copying and before every ordinary-file publication attempt, including a user-approved retry. Compared ordinary-file copies always use staging: an item removed just after the initial stat cannot take the small-file direct-copy route. A mismatch preserves the target, removes only the staged copy, records the existing comparison warning/canceled-before-change journal outcome, and skips the item without another conflict question. Ordinary copies without an expected comparison retain their direct-copy policy. Existing copied-byte accounting is retained rather than claiming all discarded I/O is rolled back.

**128 identical original/first-correction/final controls** execute actual TreeCompare, SyncPlanner.BuildRequests, JobManager and PortableFileOperations.CopyFile/Move against owned files. Update/Mirror, both directions, root/nested relative paths, unchanged/length/time/removal outcomes and four mutation boundaries are covered: before execution, just after the initial target stat, after the actual staged copy and while the first publication error waits for Retry. The retry failure and mutation timing are controlled; these are actual portable file operations, not native Windows copy-engine or desktop controls.

Original results are **80 failures/48 passes**, retaining eighty changed targets replaced or recreated. The first private correction passes the original **96 controls**, full Core **2925/64 exact skips**, full Remote **2332/156 exact skips** and **287 edit cases**. Review then adds 32 initial-stat boundary controls; all 128 against that first correction retain **120 passes/eight failures**, demonstrating the direct-copy recreation gap. Nothing from either earlier producer is overwritten or hidden. The final correction passes **all 128 controls**, including the eight reproductions.

The unchanged final compiled Core-control payload passes the complete Core suite: **2957 passes/64 exact skips**. All **2893 preceding logical outcome/message multiplicities** remain, with one explicitly verified PE-inspector display-label adaptation for its actual isolated assembly path. Full Remote **2332 passes/156 exact skips** preserves all **2488 preceding records**, and all **287 headless edit cases** preserve predecessor outcomes/messages. These are full Core/Remote and a defined App subset, not a full App/native desktop qualification.

All three expanded exports contain **1327 canonical original Git blobs**, plus the identical 128-case fixture and only the declared first/final production overlay. Full commands, source ZIPs, payloads, original failures, retry traces and raw comparisons remain. Across original/final/full-Core controls, all **384 recorded owned source/target/fixture roots are absent**; all 128 first-correction fixture roots are also absent. 13 owned temporary files are archived/rechecked, 13 removed and 0 exact locks retained. Root absence in original/first-correction/final/App/Core/Remote order is true, true, true, true, true, true.

The earlier 96-case batch separately archives/rechecks/removes all twelve owned temporary files with no locks and all five roots absent. Existing older restoration limits remain separately qualified. This fresh metadata/type check **does not provide atomic filesystem compare-and-replace, full-byte/same-size-reverted identity, parent-alias or post-check race protection**. Literal source-link copying and other transfer branches remain unqualified. Those wider I06 scopes, native Windows/SMB/account/reference/human/candidate evidence and I280 exact committed follow-up follows below; its hosted follow-up remains pending. Twenty broader unresolved entries, 24 final-candidate campaigns, physical-source HOLD and owner/GO gates remain. No workstation UI, VM/Mac, account/policy, physical-source or stable publication occurs.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records retain every preceding producer and the full source/result/cleanup inventories.

| File | SHA256 |
|---|---|
| `sync-target280-v2/expanded-preparation-v1.json` | `fc3f2c265c9d018b9501b35fecd09c3a10e22908561ee92b4a8bc20e9559920e` |
| `sync-target280-v2/reader-preparation-v1.json` | `8caced78afe70ebca0f0c1cfd9eb68c35c74e9db795fe7b98641bf938052b9c5` |
| `sync-target280-v2/original-Executors.cs` | `73e516b92e5130c4792bbe910416d0414de6ea8d287e9093e7a22200931708f9` |
| `sync-target280-v2/first-correction-Executors.cs` | `e9c9df597e3662deed4bd0044f42dfad9faf3b8bbe680dea6cbf31d3efd7bcb3` |
| `sync-target280-v2/Executors.cs` | `041ae73bfa2df91a1488534f5caa3b470d3607a9cfb762531b6d6db780b2a28f` |
| `sync-target280-v2/SyncTargetPublicationTests.cs` | `065b4c84ca4f0372364ac439f90f83ed3e2657bd8bce90dd95f0b60578761841` |
| `sync-target280-v2/run-controls-v1.py` | `0ef62a5677f4919b9d10eb0781be39b6b6a8a197703856106e2beca83c40b49d` |
| `sync-target280-v2/run-regressions-v1.py` | `730f30079acdb77d07f07b532014d5c7b12d7a0880d1470bab26298aff7b6b18` |
| `sync-target280-v2/seal-controls-v1.py` | `9e8438648f9dd73e3768b1cc66e7b66063d9b0dfa2ad77d843e780a1ab859e0f` |
| `sync-target280-v2/independent-sync-target-final-v1.json` | `2ed290623ad811f54b9766ffbd2feb79469c9fc394e26251621d33bbd99fbf7b` |
| `sync-target280-v1/independent-sync-target-final-v1.json` | `222a5b44f990db083e73fefaf7e599732a699e5046d186a3bc78374aca94716a` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-v2/original/command.json` | `a8a7873db2d218d41510296b81fd18f3d8381c332b2da947f04cb3da41ccb97f` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-v2/first-correction/command.json` | `2825b9b36c2fe4a166448a6e14f7ba700f3705dd64096d155b4e02426c33d49c` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-v2/fixed/command.json` | `a117f79ad4b93c52bcff95313a172d785a5ce876c2c10a120656b9352862202b` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-v2/fixed/app-regression-v1/command.json` | `7c568d775084cae810a53d8d8f6a2d43a5b2680ab06111e3e5e21845775c3e6e` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-v2/fixed/core-regression-v1/command.json` | `4f1046e58bb1a83d1d405b6a1dfcc52b9f78d374cbccc354f3875533b91b4cb2` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-v2/fixed/remote-regression-v1/command.json` | `beab20c781378854d96069e3ad1607a301de27fdefe2928da4719855fd814a83` |

## Exact committed follow-up

Canonical **cb52c13** repeats all **128 controls successfully**, with **1330 exact Git blobs and no overlays**. Both approved production/fixture bytes equal the pushed Git blobs. The reader requires every private semantic guard, adapting only actual owned paths/staged names. Every recorded source/target/fixture root is absent; one temporary file is archived/rechecked/removed with no locks and its root absent. This is a defined exact-control repeat, not a full committed Core/Remote/App-suite replay.

[Original run 37957430524](https://github.com/benny-cz/FileCat/actions/runs/37957430524), attempt 1 at cb52c13, awaits independent artifact audit in this update. The prepared reader expects 512 new I280 passes and 27,972 actual records only if the original run supplies that evidence; no result is inferred. [I281](E-I281-sync-link-publication.md) subsequently addresses the separately unqualified literal file-link branch. Follow-link, atomic/full-byte/native/candidate scopes remain.

| File | SHA256 |
|---|---|
| `sync-target280-v2/committed-source-comparison-v1.json` | `b105168bc5e54e377ca3dcdcc7f2e8048092ca2e284ae37f6726824012153da7` |
| `sync-target280-v2/exact-preparation-v1.json` | `9a1c88f9ee0d20979160daa314510bca900a3b2aadd38619d69d5d847490aa25` |
| `sync-target280-v2/run-exact-v1.py` | `a160b38a10eab10d5c30d1b3cdbd78d69c3780edd4394c5ff021a879de99d8b9` |
| `sync-target280-v2/seal-exact-v1.py` | `b98b4126969fe275f744dc3a4c3ed2ea558e4f08abe7ebbd05faceb732263b92` |
| `sync-target280-v2/independent-exact-final-v1.json` | `e8a21a583d0c288d7b7b9c9eb076ffdff8de6016c5ff6422f0f90b9d699f5265` |
| `sync-target280-v2/main-push-v1.json` | `64900ab00e41135437d3fb11ab8b5a1edd5c8c556fbf43a1eb73faccd503e068` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-v2/committed/command.json` | `577edf85414f676c29233f637de082e39a6e0f6bf1fbda54ff4182290da70a67` |
