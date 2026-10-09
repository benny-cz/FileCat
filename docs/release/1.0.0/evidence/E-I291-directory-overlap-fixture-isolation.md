# E-I291 — isolate the owned overlap-policy fixture from watcher churn

2026-10-09. Exact correction base **0adbad02e766b55614d881b509e8fa7ef8ad44c5**. Both complete 1361-blob exports overlay only the existing DirectoryDiffTests fixture; no production source changes. The original [About CI](E-CI-about-windows-job-failure.md) retains its actual predecessor pass-to-failure.

The I94 overlap-policy test awaited the actual comparison command but still assumed it would publish a window. Production correctly cancels that command if either listing generation changes while its options or native checks are pending. A controlled original fixture writes one owned witness file while the options are open. Its actual native watcher advances the left generation from 3 to 4; the command completes without a window and produces the same empty Assert.Single message as original CI. Two other directory controls pass. This proves a reachable invalid test assumption; it does not establish that the historical runner received this exact notification or prove its scheduler/watcher cause.

The corrected fixture makes its two owned tabs inactive for watching before navigating to the policy-test folders. Selected panels and comparison paths stay in place. It explicitly checks watcher absence and stable generations after the same owned witness write, then still awaits the actual command and asserts that the resulting comparison offers no synchronization and explains the folder overlap. Production Current/generation/cancellation and synchronization guards are unchanged. The fixture qualifies overlap policy; native watcher/current-demand behavior retains its own separate controls and acceptance limits. There is no retry, increased delay, suppressed assertion or promotion of the historical failure to a pass.

All **three directory controls** and the same compiled **full App 1398/25 exact skips** pass. All **1423 preceding App outcome/message records and exact skip reasons** remain. Full Core 3424/64 remains separately at I290; this fixture-only batch does not rerun it. The controlled original failure/two positives, actual witness bytes, generations, command state, raw logs, source/payload pins and bounded owned restoration are retained. Exact committed/original hosted repair checks, historical CI cause, native watcher/platform/reference/human/candidate scope remain. I106/I110 HOLD and required owner decisions/human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain complete source, commands, payloads, raw failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i108-directory-overlap-fixture-20261009-v2/DirectoryDiffTests.cs` | `82d16cd635c0df0d042abe160d32ff52d90b51fb03a8f2bd1dc788d0433f6b02` |
| `E:/FileCat/artifacts/release-evidence/i108-directory-overlap-fixture-20261009-v1/original/app-controls/command.json` | `65942c7ce35768a254afd7a2f9b1889f79d13c6995a723a18a88401dc4cae8a4` |
| `E:/FileCat/artifacts/release-evidence/i108-directory-overlap-fixture-20261009-v2/fixed/app-controls/command.json` | `5f6a789b5b2500cd194e15d19a8afe86d7f5c1f34033d5e069bb06274c0cf551` |
| `E:/FileCat/artifacts/release-evidence/i108-directory-overlap-fixture-20261009-v2/fixed/full-app/command.json` | `c32cdbbd140ddef5a6ec11ed3f133e5b0fa963e8272386fc80b4045d7b42a475` |
| `i108-directory-overlap-fixture-20261009-v2/seal-overlap-fixture-v1.py` | `447e308ca6cb3aad6461c136dbd771828e0754119e248d1249b01833946c6a4f` |
| `i108-directory-overlap-fixture-20261009-v2/independent-overlap-fixture-final-v1.json` | `3d562481eef111a1dcdb7dde641d9c555456da26a0fa6d346561990281f26092` |
| `E:/FileCat/artifacts/release-evidence/i108-directory-overlap-fixture-20261009-v2/owned-temporary-files-v1.zip` | `4d1a0ead09cad37ea2f8b2df865b96c3d25fcb8ab36c2b0f61ea1616ecf9fd6d` |
