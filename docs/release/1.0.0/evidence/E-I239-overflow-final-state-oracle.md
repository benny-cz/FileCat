# I239 — observe overflow reconciliation against the actual final state

Updated 2026-10-09. **Remediated preliminarily.** Exact final test/runtime source `e4cf5924edcfed362c43857349aa4dfc74c8c963`; original CI `37864532583`, attempt 1. This is a V12/I06 validation-oracle correction. Runtime `ChangeMonitor.cs` is unchanged. Twenty broader unresolved statuses and all twenty-four final-candidate campaigns remain.

## Original failure and observed defect

The original `c412bd0` CI run `37861720364`, attempt 1, failed one older Windows x64 overflow test; the other three required lanes passed. The original raw output records 20,000 churn changes, 24 native overflows and thirteen rereads, followed by “No reread was asked for after the churn and its overflow.” All 224 I236 and 100 I237 additions passed. The original output does not contain final namespace/byte observations or the relevant timestamps; the historical scheduling cause is not reconstructed.

The test required a callback UTC sample after a writer UTC sample taken when the writer resumed following its last deletion. A callback can already have reconciled the completed directory before that writer resumes. An independent observer using the exact existing `c412bd0` Core DLL, without rebuilding it or injecting notifications, exercises ordinary and deliberately held writers. Each has four workers performing 20,000 actual create/delete changes, followed by four positive completion markers. The native watcher reports one and 103 overflows respectively, with one reread each. Both callbacks see the exact completed namespace and decoded marker text; the old timestamp condition passes for the ordinary case and fails for the held case. These observations disprove that condition as a universal reconciliation oracle, without assigning a cause to the earlier CI failure.

## Correction and controlled validation

The existing fact name is retained and a held-writer fact added. The callback must observe exactly the four expected final markers, with no churn files, and the final source uses `ReadAllBytes` against the exact UTF-8 bytes of `done`. The held writer waits for that positive observation, then delays fifty milliseconds; `Stopwatch` samples verify that the observation precedes its resumed sample. UTC is diagnostic only. Real native overflow remains mandatory, with the existing maximum of five churn rounds and five-second completion waits. Production notification handling and deadlines are unchanged.

The separate native observer holds its writer for 500 milliseconds. The durable held control uses fifty milliseconds; these are distinct retained inputs. Private v1 passes four cases (two overflow controls and two existing cadence cases); fresh monotonic v2 passes the two overflow cases. The v2 held case takes two actual rounds: 40,000 changes/eight markers and eighteen native overflows. The original reader's assumption of one round is retained as a refusal; the fresh reader accepts only the configured one-to-five actual rounds. Native and private v1/v2 `AllFinalBytesExact` fields originally derive from `ReadAllText`, so they qualify decoded text, not raw bytes. The final v3 strengthens both predicates to byte comparisons and is qualified by its own committed and hosted results, rather than relabelling earlier observations.

## Exact committed and hosted qualification

All 1239 direct Git-blob LF inputs and all 82/41/141 Core/Remote/App payload members verify. Full committed Core is **2396 passed/61 explicit skips**, Remote **1828/156**, and App **1217/25**. All previous `c412bd0` local case-name/outcome/exact-skip multiplicities remain. All 163 affected raw observations are independently inspected: eighty I238 active-use error/cleanup cases, two native overflow cases, twenty-five I237 format-reader cases and fifty-six I236 listing cases. The committed overflow cases each show actual positive native overflow, actual rereads, final names and exact UTF-8 bytes; the held case verifies monotonic ordering.

Original CI `37864532583`, attempt 1, passes all four required lanes. Its four actual Windows x64/ARM64 overflow observations verify the final byte predicates and held-writer ordering. The new held fact passes on both Windows lanes and retains the exact native-platform skip on Ubuntu and macOS. All 320 I238 cases pass. The fourteen inventories contain 23,204 cases; all 22,880 predecessor identities and exact skip messages remain, with the original Windows overflow failure explicitly changing to pass. Earlier 1028 error/byte/cleanup records are independently rechecked. The 21 original server-digest archives, 2731 extracted members, four toolchains, 92 locked graphs and 25 native API command/receipt sets retain their actual producer. The original failed predecessor, all its 324 new passes and the separate native CRLF export qualification remain sealed.

This qualifies the observed component and test behavior. Native desktop frames, physical-source safety, native parser/protocol fault incidence, the full resource/permission/lifetime matrix, reference acceptance and final-candidate/stable qualification remain outside this correction.

## Owned restoration

The two private control roots `E:/FileCat/obj/oc239` and `E:/FileCat/obj/oc239v2` are absent after their two files are archived/rehashed/removed and four recorded command PIDs observed absent. The standalone native observer's owned fixture root is gone; its inherited system-temp SDK probe was not inventoried or removed, so no global temp restoration is claimed. The committed `E:/FileCat/obj/k239` root is absent after eleven files are archived/rehashed/removed and three recorded command PIDs observed absent. I238 separately retains its two private analyzer locks. Earlier cleanup inventories keep their actual qualifications. No unrelated process termination, machine setting, VM/Mac/USB/account/network change or physical-source test occurs.

## Selected private evidence

Paths are relative to private `FileCatReleaseEvidence`; each retained command/collector seals its full source, payload and raw output inventory.

| Record | SHA-256 |
|---|---|
| Original failed c412 hosted run (`listing-reader236-ci-v1/independent-list-reader-ci-final-v2.json`) | dd19dbcb0cc640fe95162a194ff4dcce5a05310d3d4287184aa58fc1d4857b02 |
| Original overflow test input (`overflow-observer239-v1/ChangeMonitorOverflowTests-original-c412.cs`) | 6a2c8c0d3c457ec744e9b88f8fc1312a14141fd6c145abfc71530c03ff42f3b6 |
| First final-state control (`overflow-observer239-v1/ChangeMonitorOverflowTests-fixed-v1.cs`) | 5d48404e9c2edb84ce7937c4d0df8958badcdfdcf66862bd1a83d797def81833 |
| Monotonic held-writer control (`overflow-observer239-v1/ChangeMonitorOverflowTests-fixed-v2.cs`) | 260f2c0d5673f41165ec2c301be0951d575098374ac664415c5548ca420b7378 |
| Final exact-byte control (`overflow-observer239-v1/ChangeMonitorOverflowTests-fixed-v3.cs`) | 40a42581bf6e318d0d0f3c1de62532c84514c2bb29e3dd1c58963d9886a606f7 |
| Existing-DLL native observer command (`overflow-observer239-v1/native-overflow-observer-command-v1.json`) | af59c6c18623b8815386d30e50d483390c1df4e5018a092a5e393db89428d39b |
| Original native observer output (`overflow-observer239-v1/native-stdout-v1.txt`) | 025e4ed48856cb69d085166b6e7344940f8ac229e35657083cdc394d6e33a1d5 |
| Independent native timestamp disproof (`overflow-observer239-v1/independent-native-overflow-order-v1.json`) | eb740e478b4822470835b15e2f4bd94046c5d575682d4645d0838d1c003f34d1 |
| First private command (`overflow-observer239-v1/fixed-v1/command.json`) | a0e2aa81e2093d7f00af2ada1f1b4f2eae4de3672e5d2011b5370a99d386c05f |
| First private TRX (`overflow-observer239-v1/fixed-v1/results/watcher.trx`) | 24883642f3403a34e3e4ebfb297cbb59ce29c3f02347e1dda6f910930490eebf |
| Monotonic private command (`overflow-observer239-v1/fixed-v2/command.json`) | 6a9932ac97f542a8a16efaee1e1fe5170c60003ef8f4d1910ca8d051b3cc4bc9 |
| Monotonic private TRX (`overflow-observer239-v1/fixed-v2/results/watcher.trx`) | 3d02d628fb83904aff657bbdfdf553c93e1c8c3b012e88c5451be84e6f2ce77d |
| Original one-round reader refusal (`overflow-observer239-v1/original-reader-one-round-refusal-v1.json`) | 748f7c80f0521d658dbcd0198599d03d14e2a73e1d9e482ece75f63663b9ef3d |
| Independent private actual rounds (`overflow-observer239-v1/independent-private-overflow-controls-v2.json`) | a2728a55d1621d3e54e8539466cf4c4166fd51bb5824f8449e5657c5e7fcdf83 |
| Exclusive application (`overflow-observer239-v1/applied-exclusive-overflow-oracle-v1.json`) | ae37873bd602896066f6ef5992b16388ae6047637484e6076784da2a751cb5f6 |
| Combined runtime commit and push (`overflow-observer239-v1/combined-runtime-main-push-v1.json`) | dc4e610e136508345826b8a5feb6c456b469ed52460f61dd46df617fb0684a83 |
| All three exact-source commands (`overflow-observer239-v1/canonical-v1/command.json`) | 04640f9792e858c00850f801a1771549abd49934056b0f3946aa46c786841853 |
| Exact-source Core TRX (`overflow-observer239-v1/canonical-v1/results/core.trx`) | fb6fe2c4c014e30ba927224a73f09f37d6ed99dc470a0a3a309f58ee245aadb6 |
| Exact-source Remote TRX (`overflow-observer239-v1/canonical-v1/results/remote.trx`) | 6418c72fc3877435949f93626efc0e4c80f2283c9fe7837e0d92bfac44939f69 |
| Exact-source App TRX (`overflow-observer239-v1/canonical-v1/results/app-full.trx`) | d627f36e2200e84711a264ca1993f764304d3b0b5f4af53b02948e1180f12056 |
| Independent committed inventory and actual bytes (`overflow-observer239-v1/independent-canonical-paged-overflow-v1.json`) | c96e13cbcd5f4cc343fe83208a21ad769e4686ec4eb0ea231f6ec632cea67107 |
| Original successful four-platform run (`paged-overflow238-ci-v2/independent-paged-overflow-ci-final-v1.json`) | 9fed29cfaa636106c66c0cee0763490c33b6135a76ca137e61806b3523027586 |
| Private test temp restoration (`overflow-observer239-v1/owned-overflow-private-restoration-v1.json`) | 4ed4c1d3773c6f263e42d0de2887e6ea653e707979683ffa65de0ea5f8dec0e5 |
| Committed test temp restoration (`overflow-observer239-v1/owned-paged-overflow-canonical-restoration-v1.json`) | b3320a3b1afc68f98dd0a5195d4d25616a2b5e98290871be46942c2749272bab |
