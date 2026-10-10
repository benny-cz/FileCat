# E-I306 — closed Find-window private data

2026-10-10 CEST. I06/V12/V13 preliminary scope. Unchanged baseline **af399bd7f245da9623bffed97610cb6f5d1f3359**; both isolated exports independently check all **1411 canonical raw Git blobs**. Original validation adds only the regression fixture; corrected validation declares one FindWindow product overlay and that fixture. Locks/dependencies are unchanged.

## Finding and correction

A deliberately held closed Find window retained its private within-results snapshot, refinement scratch and SearchSession, or duplicate-group index. Thirty controls exercise five real workflows — within-results scope, Intersect, Subtract, Append and duplicate groups — at 16, 128 and 4096 owned files, each with a live-window positive and closed-window case. The unchanged product yields **15 closure failures /15 live passes**; the correction yields **30 passes**, with **24 direct closed-owner retirements /24 live-owner positives**.

Closing now cancels work, disposes the existing tab and releases these private references. An unused within-results owner field is removed. StartSearch ignores calls after closure. The real search task retains its local session for its active lifetime; the existing cancellation and duplicate-completion barriers remain. Published result sets stay provider-owned and readable, including the empty Subtract result. No caller data, retention policy or arbitrary resource cap changes.

The same built payload passes all **99 affected Find controls** and **full App: 1562 passes /25 explicit skips /zero failures**, without rebuild/restore. Every **1557 predecessor outcome/message and all 25 exact skips** remains; only the thirty new controls differ. All synthetic fixture-file bytes remain unchanged. Observer boundaries are non-inlined, reflection only reads state, and private fields are not mutated by the tests.

## Native baseline and qualification limits

The exact unchanged baseline also runs in the signed-in Windows 11 VMware guest: **15 failures /15 live passes /60 compositor completions**, using the actual main/Find windows and the same five workflows. All **thirty owned fixture folders are removed**, the 193-file private runtime verifies before/after and no owned process remains. The first independent native reader incorrectly looked for Cleanup inside Cases and refused; the fresh reader uses the actual top-level Cleanup field, preserving the original refusal and unchanged output.

Exact corrected committed/hosted and native follow-up remains pending. These ownership controls do not establish whole-process peaks, native allocation ceilings, physical input, OS-present timing, reference/human or candidate acceptance. Broader I06 stays open; physical-source I106/I110 HOLD, owner/freeze/candidate decisions and explicit human GO are unchanged.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records retain raw source, commands, payloads, failures, skips and cleanup.

| File | SHA256 |
|---|---|
| `i306-find-retirement-20261010-v1/independent-final-v1.json` | `48c905b60012812dac233792b0770e6567d8a459c74ef81c6403c9f6c2c28981` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/seal-v1.py` | `ad2ea4aebccbe19ae5108c1402a871425dece82971ab7e5b61057cb0bd00ad71` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/run-batch-v1.py` | `d98a6212305ad84e94b67b096e792e845a1e36120786149b80d5a00b5f6a4761` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/run-wide-v1.py` | `40b97f714c70a5ed36cb7cec5c2e884e5457d11447e0a255c49ad475ddad0e20` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/original-v1/inputs.json` | `32b957b41cd494257c08d54c83b018d6b15fa67008e3fdc1a1d16341f0e730aa` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/original-v1/controls/command.json` | `63518731f8cac7a55844b9cdc8285a8ca068c5af1012da3c4e52e5fd51dbd096` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/fixed-v1/inputs.json` | `65068f905aa1af98ac64476dd37e9ef8ae1ccf527b01b710c72ce9ceed9a2d6a` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/fixed-v1/controls/command.json` | `883756e0ef3909d8a016c651c0581e5fd5fda66df1045b90e431ff378adef7b6` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/fixed-v1/affected/command.json` | `7d73f27ba1c0318a464146d8b5baf52e734092c8c87fdbdfae0255488f9bd891` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/fixed-v1/full-app/command.json` | `6dc040cfe5f2bcec99268f89b07f88fc8563e66378bbb40b63f1d44101b91ac9` |
| `i306-find-retirement-20261010-v1/independent-native-original-v2.json` | `0fb39a6af9fe30d553486b4cf1dc09fb143b1a257357a5ea293ffdb9d8482af9` |
| `i306-find-retirement-20261010-v1/native-original-reader-v1-refusal.json` | `fdceb0d7c8cdc8f750a0c0bacca93a27c232d755ecdccb0f29985ea18122e552` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/seal-native-original-v1.py` | `9da0e0ad077ad3c275cb88985236c3d0a3195d7f271838a60e0b8f1b43b45194` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/seal-native-original-v2.py` | `ef6981583c53f43b88f5a7d4efe94657dd34730fbcae5ceb100c22b9c9268870` |
| `i306-find-retirement-20261010-v1/native-original-v1/transport-final-v1.json` | `2bfa5769ad8810dbebfb349f0684a63898102964820666264c70b89bf91596c1` |
| `E:/FileCat/artifacts/release-evidence/i306-find-retirement-20261010-v1/native-original-v1/manifest.json` | `e44dbdfcac0b6321cfb380e55efb948f4ec1c3eec1880383f2816df90859b95a` |
