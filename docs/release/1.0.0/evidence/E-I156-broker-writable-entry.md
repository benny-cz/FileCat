# E-I156 — resolved Program Files location accepts a writable managed entry DLL

2026-10-06. High pre-consent code-trust defect under I17/V06/B04. Open; native ACL correction and revalidation in progress. No candidate/stable human GO.

The exact committed **7183268466ce24811aee7f59b3130b0b4e999b91** native self-contained/framework-dependent win-x64 executables and all surrounding content are unchanged from the separately pinned CLR handoff fixture. The sole managed entry DLL is an explicitly synthetic harmless control, not production plan/consent code. After copying each case into a fresh owned Program Files fixture, the native script records owner/SDDL and grants the ordinary account's user SID Modify on **only that DLL**. Before/after content hashes are identical. Both file/directory owners remain BUILTIN Administrators; inherited ordinary read/execute rules remain. An explicit non-inherited user SID write ACE is verified.

Both actual broker runas launches load this writable managed entry in their returned administrative PID: SC 10456, FDD 13920. The synthetic entry records exact argument handoff/system environment and exits 41 naturally. Two direct native DLL positives remain; full payload/retained output pins and owned process/protected-fixture cleanup independently verify. No outside-fixture permissions, persistent environment/policies, actual operation plans or source devices change. Caller already administrative; this proves acceptance of an ordinary-writable owned component, not a demonstrated unelevated escalation/UAC bypass. The loaded entry is deliberately synthetic; no production consent behavior is inferred.

Native resolved-location checks alone do not establish file/directory owner or DACL trust before CLR loads code. The correction must inspect the loaded file and its ancestor chain before host initialization and reject ordinary write/delete/ACL/owner permissions, while preserving legitimate administrator/system/installer-owned cases. Full dependency resolution, races, UAC/limited caller, pipe and installed-candidate scopes remain in I17.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-loader-20261006`.

| Retained path | SHA-256 |
|---|---|
| acl-loader-v1/native-baseline-v1/independent-native-baseline-v1.json | 00f852102bcc89eb991c970c6adcd33dcf2b2926a742e3c007091ae1d775c6b6 |

Current 133/156 preliminary remediations, one Closed, 22 remaining issue remediations; all campaigns still need final qualification.
