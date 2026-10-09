# E-I279 — recheck remote edit targets after uploading

2026-10-09. Preliminary I06/V08/V11/V23 evidence at original **0dc7380da0ffb0e09bcf73899aa7eb743cd680de**, plus the identical controlled fixture and, only in the corrected export, one production overlay. An edit upload checks ExpectedTarget before copying bytes but its publication step omits the fresh revision/type comparison. A destination changed during upload can therefore be replaced without a new user choice.

The correction checks the freshly listed entry against the approved length/modified time and requires an ordinary file, immediately before target mutation. A missing, changed, link or directory target fails with a retained-edit explanation, rolls back transferred progress and removes only this job's staged upload. RefusedOperationException prevents an ineffective retry question. Ordinary uploads and the non-overwriting rename used for an expected-absent target keep their existing behavior.

**56 original/fixed controls** use the actual upload JobManager/executor, an owned local working file and an instrumented in-memory channel. Four logical profile modes (SFTP, FTP, explicit TLS FTP and implicit TLS FTP), seven states (unchanged, length change, timestamp change, removal, link, directory and late appearance) and both atomic-rename capability settings exercise actual List/CreateNew/Stat/TryReplace/Delete/Rename calls. The fixture mutates the server only during the first owned upload write, after the initial destination check. These are logical protocol controls; they do not establish native server fault incidence.

The original **40 failures/16 passes** retain **36 changed destinations replaced or recreated**. Four atomic-capable directory controls already preserve the directory, but attempt replacement and ask a retry/skip question before refusing. The sixteen original successes are eight unchanged-file and eight late-appearance controls; the latter already preserve the newly appeared target through exclusive plain rename.

All **56 corrected controls pass**, and every adverse expected-present case preserves the complete observed target snapshot without a target mutation call or retry question. Every rejected upload has zero counted transfer bytes and no owned staging name. Unchanged uploads have exact working-file bytes; late-appearance controls retain the existing refusal behavior. Working-file hashes and the independent link-target sentinel remain unchanged.

The **same unchanged compiled Remote test payload** passes the full suite: **2332 passes/156 exact skips**, including all 56 controls again and every **2432 predecessor outcome/message multiplicity**. The App edit integration subset passes **287 cases**, preserving every predecessor outcome/message multiplicity. Its FileCat.Remote assembly is byte-identical to the corrected Remote-test assembly. No full App/Core replay is claimed for this correction.

Both exports contain **1324 canonical original Git blobs**, plus only the identical fixture and the declared fixed SftpJobs overlay. Actual commands, source ZIPs, payloads, raw original failures, exact skips and independent 56-case comparisons remain. Across original/fixed/full-Remote executions, all **168 recorded owned working-file and fixture paths are absent**. 13 owned temporary files are archived/rechecked, 4 removed and 9 compiler/analyzer locks retained. Root absence in original/fixed/App/Remote order is false, true, true, true.

This is a fresh metadata/type check, **not a server-side atomic compare-and-replace or full-byte identity guarantee**. Same-size/reverted changes, mutations after this check, server alias/parent races, wider native provider/account/disconnection/reference/human scope and exact candidate qualification remain under I06. I279 exact committed/hosted follow-ups follow below. Twenty broader unresolved entries, all 24 final-candidate campaigns, I106/I110 physical-source HOLD and owner/GO gates remain. No workstation UI, VM/Mac, account/policy, physical-source or stable-publication change occurs.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain the complete exports, raw results/payloads and owned cleanup inventory.

| File | SHA256 |
|---|---|
| `edit-target279-v1/preparation-v1.json` | `2c5b42ec51662eda3e73490950fc1ee596b64e5b1f8fb0c3dccfac92d4b9ef56` |
| `edit-target279-v1/original-SftpJobs.cs` | `f2f653522501357d09d0511680fe11fbe4eaeec41ab2f269888109b9d92ccb85` |
| `edit-target279-v1/SftpJobs.cs` | `a138c75db95d3375a21f1da7e53005cc7f6074381992c31f1f07a12be66e5bf1` |
| `edit-target279-v1/RemoteEditTargetPublicationTests.cs` | `65451296d941875463f15168faf88dd02807a7549eaf1cefdf2f991ae70fb44d` |
| `edit-target279-v1/run-controls-v1.py` | `4227e24bcf48211cda644762bae3baae437c1a4e542a0516abaca5d2323fb7ca` |
| `edit-target279-v1/run-regressions-v1.py` | `1f0c4eca409114420a927d88a432caf2854b341d5db78f7cd792d2c0e7e52898` |
| `edit-target279-v1/seal-controls-v1.py` | `8f2dafcb25679c00b0d91eeda9b1a64f017c10e0da2061d53cc7e028b84bad09` |
| `edit-target279-v1/independent-edit-target-final-v1.json` | `d3706864f701d9bb8576e9ec214e5c579860b94904f2c24f20a2fd36bd784651` |
| `E:/FileCat/artifacts/release-evidence/edit-target279-v1/original/command.json` | `3012f2a996806b60c075d462304c1de011d747c0463b16184b7506a877b27ce7` |
| `E:/FileCat/artifacts/release-evidence/edit-target279-v1/fixed/command.json` | `cba0536afafa3444ca990fbd9ee4c3a130d3aafdd50b53905015fe623bb2efb8` |
| `E:/FileCat/artifacts/release-evidence/edit-target279-v1/fixed/app-regression-v1/command.json` | `360a6007fe18b07cb214a5986ab2e5aed219c0d17aee93380dd350dc9c286b35` |
| `E:/FileCat/artifacts/release-evidence/edit-target279-v1/fixed/remote-regression-v1/command.json` | `82a0779aa672613201723d02f4211d2840850b521af413e26d8f9a1a99f49843` |

## Exact committed and hosted follow-up

Canonical **08399b2** repeats all **56 controls successfully**, with **1327 exact Git blobs and no overlays**. Both approved production/fixture bytes equal the pushed Git blobs. The reader compares all private semantic guards except actual owned paths/names and healthy uploaded timestamps. Every source/fixture root is absent; one owned temporary file is archived/rechecked/removed with no locks and that root absent. No full committed Core/Remote/App-suite replay is inferred.

[Original four-lane run 37953574730](E-CI-remote-edit-target-publication.md) passes all **224 I279 executions/27,460 actual records**, preserving every preceding outcome/message multiplicity and exact skip. These checks do not qualify I280's later local-sync correction or close atomic/full-byte/native/candidate scope.

| File | SHA256 |
|---|---|
| `edit-target279-v1/committed-source-comparison-v1.json` | `7fc8dfa450bfcd21a5e66b9b0b2cba430c2aa749a1f512b752790b49335615e6` |
| `edit-target279-v1/exact-preparation-v1.json` | `1fb10f2db54ccba577ab9262bf23a6ae3e203d076829428cc0b219febc18a541` |
| `edit-target279-v1/run-exact-v1.py` | `a565a547d9933b203844e6d2b18041c223a3115b108e92c0799f83ebc5170f3d` |
| `edit-target279-v1/seal-exact-v1.py` | `ab91908970c0e5d1ac7966480b26460d99ef4abda221b0075cbd39ae4952e5c0` |
| `edit-target279-v1/independent-exact-final-v1.json` | `2c9ea1ab3cc4ab36744e16f62ddbacd8b4ee3759d051e92ea37b52ba30780e8b` |
| `edit-target279-v1/main-push-v1.json` | `19ac85c32d84176ae691fbce232d143d95456584332e3e20057b22896f70c4c7` |
| `E:/FileCat/artifacts/release-evidence/edit-target279-v1/committed/command.json` | `bb42daa21bc022fdfe756885fd6e070c3d14799167eff435a26a3a8e7d7fd52d` |
