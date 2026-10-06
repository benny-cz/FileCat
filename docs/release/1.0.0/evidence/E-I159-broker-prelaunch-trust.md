# E-I159 — broker caller accepts a user-writable executable before startup

2026-10-06. High executable trust defect under I17/V06/B04. Remediated preliminarily at 8b4be9d; broader I17 remains open. No candidate or stable GO.

The exact **69603ec10e6b1631ef897b68eafae4a118bc8d8d** clean committed public launcher/caller files remain unchanged. In an owned disposable Windows VM Program Files fixture, only the helper EXE is deliberately replaced with an explicitly synthetic native marker. It validates its owned result path, records its PID/administrative membership and exits 73; it opens no device and runs no production plan/consent or CLR. All other published files remain exact, and the source/build/output hashes are retained. The native bootstrap's own trust check is absent from this explicit substitute; checking the original executable only after Windows already starts it cannot establish pre-execution trust.

The protected-path positive marker naturally exits 73 in returned PID **12180**. A separate same-byte synthetic executable with an explicit current-user SID Modify ACE also passes the actual caller's protected-location check and **ElevationBroker.Launch**. Its returned held-process PID and native marker PID match **10624**, Admin, with natural exit 73 and no termination. Before owner remains BUILTIN Administrators; executable bytes do not change during the ACL comparison. These are two native executable-permission controls, not evidence of self-contained versus framework-dependent production CLR startup despite their inherited fixture lane names.

The caller is already administrative. This proves the actual API accepts the ordinary-writable executable permissions; it does not demonstrate an unelevated account's replacement, UAC bypass, production FileCat consent or physical source access. The generic harness's HookMarkerPresent field here denotes the explicit native marker, not a managed/native startup hook. Two distinct DLL positive controls, all source/payload/output pins and owned process/protected-fixture cleanup pass. All permission changes are limited to the removed owned helper fixture. No installed runtime ACL change is made by this case.

Both discovery and launch must establish executable/ancestor ownership and ordinary write-grant trust before ShellExecute starts it. Path aliases, races, complete token/requester/IPC identity and limited-caller/candidate qualification remain under I17.

Private evidence root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-loader-20261006`.

| Retained path | SHA-256 |
|---|---|
| prelaunch-baseline-v1/native-baseline-v1/independent-native-baseline-v1.json | 13cc12dc44fcb500ecfeb55d18995f29fb2d7178234a4d0c842c8edd9793eb69 |
| independent-i159-baseline-v1.json | 0ec98dddd370eb9182641954a43ff01b9c666abf0bc5f3edab545f0911a364a4 |

Baseline checkpoint: 136/159 preliminary remediations, one Closed, 22 remaining. All campaigns need final qualification; NO-GO.

## Correction, isolated native repeat and committed seal

**8b4be9d9a4be824614cdc421f34cbc5981d685aa** makes both discovery and Launch verify the actual file and each ancestor through Program Files before ShellExecute. SYSTEM/Administrators/TrustedInstaller ownership and write grants are accepted; ordinary write/delete/permission/owner-change grants, unsupported allow forms, reparse points, missing files and names resolving to a different path are refused. This does not eliminate concurrent changes after inspection or qualify all token/path identities. Refusal is a clear IOException before elevation is requested.

Affected host tests pass **12/0**, including the actual native API's refusal of owned malformed/missing noninstalled files before elevation. Four actual working native publish modes, all sources/published files and compiler receipts verify. The final native observer adds only an explicit pre-launch IOException receipt, preserving the actual Launch call and success path. **Both before and after run this identical observer** and identical native inputs; the only loaded-file difference is FileCat.Platform.Windows.dll. Baseline still runs the ordinary-writable synthetic native entry; the correction preserves protected native natural exit 73 and rejects the writable file with no returned process, no marker, no timeout or owned termination. Two DLL positives/all pins/process/protected cleanup pass. This final isolated repeat supplements the original observer baseline; it does not claim the original and final observer binaries are identical.

Clean committed export verifies **927 raw Git blobs**, all four native publish modes/PEs/compiler receipts and complete input/output pins. Fresh native controls preserve all committed caller files except the explicitly reused error observer DLL in the two adverse-permission cases; only the helper EXE is replaced by the same harmless native marker. One case adds an explicit user Modify grant to the file. A separate case grants only its parent directory while the executable's full SDDL remains unchanged. Both reject with the pre-launch error/no returned process/no native marker; each retains a protected-file positive with natural exit 73 and two DLL controls. The parent case is an independent boundary control, not a claimed old-caller parent-grant reproduction.

A distinct healthy control preserves the exact committed native executables and public caller output while replacing only the managed entry with the declared synthetic observer. SC and FDD naturally reach it in the held runas PID and exit 41, preserving original arguments/clean environment/system cwd/path and actually loaded bundled/installed CoreLib. This proves normal native/CLR handoff compatibility, not production plan/consent or raw-device access. All retained bytes and owned process/protected-fixture cleanup verify in all three cases. Only owned fixture permissions change; no installed runtime or persistent system setting changes. Caller remains already administrative and no GUI interaction is observed.

[CI 37437361505 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37437361505) passes all four required lanes, ten artifact server digests/four build receipts/fourteen full TRX inventories and both downloaded Windows native byte/source/compiler receipts. The new actual API regression passes in separately retained execution IDs on x64 and ARM64. Platform counts are 168/33 declared skips/201 and 167/34/201 respectively. ARM64 startup/drawing/installer compilation pass. Official log archive retrieval uses the verified native-curl route and explicitly retains filename/job-prefix normalization; this run has no invented gh timeout. Compiler/setup bytes are reported rather than downloaded. Final candidate, limited-user consent, races, full token/path/IPC identity and all remaining release gates stay open.

| Retained path under FileCatReleaseEvidence | SHA-256 |
|---|---|
| broker-loader-20261006/independent-i159-working-v1.json | ec22f53ef50149bf148e97387cb20319e529509ec887c01ff860012adaae9e1d |
| broker-loader-20261006/independent-i159-committed-v1.json | cddd272a6e4b769094c892ad2d027d7885698bf2f9d5967771a79757fee6188c |
| broker-loader-20261006/prelaunch-committed-v1/producer-native-bootstrap-v1.json | 6f2371f27aca1ad9923bca78a9c83fff377996d896f459618ad78c2c244a7beb |
| ci-37437361505-attempt1/independent-ci.json | 1bb333766c74fae5e12eaaffa2e4b3b24b996c88510c8046c927b617d2cca0a8 |
| ci-37437361505-attempt1/independent-ci-native-bootstrap-v1.json | 9ae1929969976352f0fb76cd75bb4e61dbf96147c51600c3ac493a2c758627b5 |
| broker-loader-20261006/independent-i159-seal-v1.json | 1c295d3eea5b2347767889a04c0a848b6cdc02ee59aa59e4f5250038050f3d94 |

137/159 preliminary remediations, one Closed, 21 remaining issue remediations. All 24 campaigns require final qualification; NO-GO.
