# E-I276 — comparison content cleanup replaces primary errors

2026-10-09. Preliminary I06/V13/V23 evidence at exact original **5dfd28ca48a25bb59fc270373eef286de011e047**, plus declared test/fix overlays. ComparisonIo's admission cleanup and nested content using scopes allow a failed Dispose to replace cancellation, scheduler shutdown, or the primary open/read error. When both standalone closes fail, the later left close also overwrites the first right close. This is controlled provider/owned-scheduler evidence, not an observed native storage incident.

The correction preserves the original admission/operation exception while logging secondary close failures. Both acquired contents retire in the original reverse acquisition order. Without an operation failure, the first standalone close error is retained with its original object/stack and subsequent failures are logged. Cancellation tokens and scheduler-stop meaning survive. Ordinary equal/different results, device-worker routing and existing unavailable-content semantics remain covered.

All **104 identical controls** cover thirteen scenarios, four close configurations and shared/distinct device keys. Scenarios include cancellation/shutdown after left/right content opens, cancellation/invalid-operation during left/right reads, three second-open errors, and healthy equal/different content. The original has **60 failures/44 passes**: 56 primary operation/cancellation meanings are masked and four both-close controls report the later error. All original raw errors remain. Original ownership cleanup already releases every recorded FileStream; no original holder leak is claimed.

The correction passes **104 controls**. Actual owned read-only FileStreams close once and admit immediate exclusive access, original operation objects or cancellation tokens/stop messages survive, first standalone close objects remain, and both file hashes are unchanged. Open/read calls run on the actual FileCat I/O workers. Unrelated worker calls complete when the scheduler remains running; stopped-scheduler cases explicitly record that no such call was made. Provider/cleanup faults are controlled, with no native fault incidence or desktop claim.

The **same unchanged corrected compiled payload**, without rebuilding, passes the complete Core suite: **2829 passed/64 exact skips**, 2893 actual records. All **2789 preceding logical outcome/message multiplicities and 64 exact skips** from the SMB fixture correction's full Core producer remain; only 104 passing cases are added. One PE-inspector test includes the actual compiled assembly location in its raw display label. The reader retains both actual labels, verifies the identical method and explicitly normalizes only that path-dependent label. Duplicate display-label multiplicities remain; **not all raw labels are identical**.

Source ZIPs contain 1316 canonical original Git blobs plus only the declared fixture and one production-file overlay. Exact committed and hosted correction checks follow below; this is not an installed candidate. The independent reader rehashes every source/blob/ZIP/overlay, command/raw stream, actual payload, TRX and original/fixed/full observation. Every recorded holder path and fixture directory is absent at seal.

All 10 owned temporary files are archived/rechecked and removed. Original/fixed/full temporary roots are absent, with zero retained locks. Older compiler locks retain separate qualifications; no global process termination or restoration claim.

No workstation UI, VM/Mac, persistent account/settings, physical source or stable publication changes. Wider provider/identity/resource/native/human/reference/candidate scope remains. I06, all twenty broader unresolved scopes, all 24 final-candidate campaigns, physical-source HOLD and explicit human stable GO remain.

## Exact committed and hosted follow-up

Canonical **3ef7b0a8e4c459a923eaa9a5515032e0faaff0cd** repeats all **104 controls successfully**, with 1318 exact Git blobs and no overlays. The independent reader verifies source ZIP/blob identities, commands, actual compiled payloads, raw TRX and complete semantic equality against the private corrected controls, except actual source paths and original stacks. All recorded holder paths/fixture directories are absent. The approved push receipt permits only the previously recorded C# CRLF-to-LF clean filter; no new refusal or product failure is claimed. This control repeat does not claim a complete Core-suite replay at the committed export.

Three owned temporary files are archived/rechecked; one is removed and two xUnit analyzer DLL locks remain in this exact committed test's compiler namespace. Its temporary root therefore remains present. The actual content holders are all absent. The earlier private original/fixed/full roots remain separately recorded as absent. No global compiler termination or restoration claim occurs.

[Original hosted attempt 37942271282](E-CI-content-open-comparison-retirement.md) passes all four required lanes, all 416 I276 executions and 26,756 actual records. Every 26,340 predecessor outcome/message multiplicity and exact skip remains. Native/provider/human/reference/candidate qualification remains.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute. Nested receipts retain complete inputs, commands, payloads, raw streams and restoration inventories.

| File | SHA256 |
|---|---|
| `comparison276-v1/preparation-v1.json` | `968b9f9588f039aafa74c0a55aa06557e9a95ece6327a089b384935e75cfe9e9` |
| `comparison276-v1/original-ComparisonIo.cs` | `6c5dae4c2d14228f86bcc88d969c43c16d348f919c26e7aaa5e6748cd2a2926d` |
| `comparison276-v1/ComparisonIo.cs` | `dffecd9f84e3dd1157949ec88ecaf5b2d70c0dc60ebd97f49bab923ad6584b77` |
| `comparison276-v1/ComparisonContentRetirementTests.cs` | `4224a2cfafac0f83ac4d16163072c0ae13d78f9551f71e3019304498e7fe2808` |
| `comparison276-v1/run-comparison-controls-v1.py` | `f1af0b8d1fc760e694dadbde997931f30a34203e8a6604887b79398e9c710435` |
| `comparison276-v1/run-full-comparison-core-v1.py` | `341e5ea7e7c62879e5b8bccfc6c6f64eb5155d96cb7ab94c50ab658bfa46f4fe` |
| `comparison276-v1/seal-comparison-retirement-v1.py` | `674e6fe3fde07b6fd560b9d19f98909820f851c5abd02de69df875ab2e959e9a` |
| `comparison276-v1/independent-comparison-retirement-final-v1.json` | `5e783be8801f7d7c38fff3e7efe601516fd56d12138089d341497aa51a9b78cd` |
| `E:/FileCat/artifacts/release-evidence/comparison276-v1/original/command.json` | `e3a0d210907559fe7eb6a231aed2f6b086397d8e37a26c90feb5ec0e5542a16b` |
| `E:/FileCat/artifacts/release-evidence/comparison276-v1/fixed/command.json` | `556f4f9c5983dc62558b118d72d54d0b1f3d2842238dd399b0b3989b45caa757` |
| `E:/FileCat/artifacts/release-evidence/comparison276-v1/fixed/full-remote-v1/command.json` | `66770d54d3655fe0102cf5226b14e808d9967295cf88277671a762de813b15ef` |
| `comparison276-v1/committed-source-clean-filter-v1.json` | `edfa17d6c48e27c4b34b29fc96ece89a81adddeadced02e760df83291ef4d98c` |
| `comparison276-v1/run-exact-committed-comparison-v1.py` | `941d69c26b422772bf64288d83e41c5c216eca9393334e082a67c488e1bf3184` |
| `comparison276-v1/seal-exact-committed-comparison-v1.py` | `8f51b4b5165fe3d41f312092b5e4c87a8c99c7ee38b71cae77da87ea5f04975f` |
| `comparison276-v1/independent-exact-committed-comparison-v1.json` | `0beefe65471fef64a38f1cf26602c06f5f0ce21d62825fae3f3505a3a8cdabf6` |
| `E:/FileCat/artifacts/release-evidence/comparison276-v1/committed/command.json` | `da2db4acae952dcfa3aaec6dcb29cdd4a38a25f78d34d4b27d13776b1739f576` |
| `comparison276-v1/comparison-retirement-main-push-v1.json` | `63c8f173d18f77a0dfa5eb3cd80f77ed6c15d5cb5f5906d80c8963eb942a1f5b` |
