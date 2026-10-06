# E-I159 — broker caller accepts a user-writable executable before startup

2026-10-06. High executable trust defect under I17/V06/B04. Open; caller correction underway. No candidate or stable GO.

The exact **69603ec10e6b1631ef897b68eafae4a118bc8d8d** clean committed public launcher/caller files remain unchanged. In an owned disposable Windows VM Program Files fixture, only the helper EXE is deliberately replaced with an explicitly synthetic native marker. It validates its owned result path, records its PID/administrative membership and exits 73; it opens no device and runs no production plan/consent or CLR. All other published files remain exact, and the source/build/output hashes are retained. The native bootstrap's own trust check is absent from this explicit substitute; checking the original executable only after Windows already starts it cannot establish pre-execution trust.

The protected-path positive marker naturally exits 73 in returned PID **12180**. A separate same-byte synthetic executable with an explicit current-user SID Modify ACE also passes the actual caller's protected-location check and **ElevationBroker.Launch**. Its returned held-process PID and native marker PID match **10624**, Admin, with natural exit 73 and no termination. Before owner remains BUILTIN Administrators; executable bytes do not change during the ACL comparison. These are two native executable-permission controls, not evidence of self-contained versus framework-dependent production CLR startup despite their inherited fixture lane names.

The caller is already administrative. This proves the actual API accepts the ordinary-writable executable permissions; it does not demonstrate an unelevated account's replacement, UAC bypass, production FileCat consent or physical source access. The generic harness's HookMarkerPresent field here denotes the explicit native marker, not a managed/native startup hook. Two distinct DLL positive controls, all source/payload/output pins and owned process/protected-fixture cleanup pass. All permission changes are limited to the removed owned helper fixture. No installed runtime ACL change is made by this case.

Both discovery and launch must establish executable/ancestor ownership and ordinary write-grant trust before ShellExecute starts it. Path aliases, races, complete token/requester/IPC identity and limited-caller/candidate qualification remain under I17.

Private evidence root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-loader-20261006`.

| Retained path | SHA-256 |
|---|---|
| prelaunch-baseline-v1/native-baseline-v1/independent-native-baseline-v1.json | 13cc12dc44fcb500ecfeb55d18995f29fb2d7178234a4d0c842c8edd9793eb69 |
| independent-i159-baseline-v1.json | 0ec98dddd370eb9182641954a43ff01b9c666abf0bc5f3edab545f0911a364a4 |

136/159 preliminary remediations, one Closed, 22 remaining. All campaigns need final qualification; NO-GO.
