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

## Committed correction; qualification in progress

Correction **ce8189e2fb3b14053aeecdd09063fba9812782e7** checks owners and ordinary write/delete/ACL/owner grants on the resolved helper, every ancestor through Program Files, the bounded complete adjacent application tree, and selected hostfxr/ancestors before loading CLR. SYSTEM, BUILTIN Administrators and TrustedInstaller owners/writers remain supported; inherited read/execute and inherit-only creator-owner grants remain compatible. Reparse points and unverifiable/unsupported grants fail closed. Native linking includes advapi32. Managed plan/consent source is unchanged. Shared-runtime dependencies and replacement races remain in I17.

The executable-only working comparison preserves every other input. Both protected SC/FDD cases hand off exact arguments/environment to the synthetic managed entry and exit 41 naturally; both ordinary-writable entry cases have no hook or managed witness during the bounded window. Separate writable-directory and writable-Core-DLL cases also block startup. The owner-change control blocks startup, but Windows additionally materialized an inherited creator-owner FullControl ACE: this is **not** an isolated owner-only proof. The first independent audit incorrectly interpreted the retained before-owner field as the after owner and failed; v2 uses the explicit after-owner/SDDL and preserves that limitation. Every negative process is bounded and terminated by its returned PID after image verification; no refusal dialog was observed and a clean refusal exit is not claimed. Each native case keeps two independently positive native DLL controls and verified payload/output/process/protected-fixture cleanup.

All four working win-x64/win-arm64 SC/FDD publishes are native PE executables with verified compiler receipts and disabled managed startup hooks. Host affected elevation tests pass 15/15. Clean exact committed export independently verifies all **918** raw Git blobs, source archive, four actual publishes and compiler receipts; fresh native repeats and CI 37426875741 are running. No installed candidate, limited-caller consent or UI qualification is inferred.

| Retained path | SHA-256 |
|---|---|
| independent-acl-working-v2.json | 863b9226b2b84a9f019c766b1ebc1fbb031924ab29cf5753d3c4530d53269088 |
| acl-comparison-v1/readonly/native-baseline-v1/independent-native-baseline-v1.json | 71b9ee3310dfa898365fca3782cbd784fc6f3cf899c37587ba42dfb6b2fca67d |
| acl-comparison-v1/writable/native-baseline-v1/independent-native-baseline-v1.json | 1a116f642684931243a827e7290ec320bf766800b0e6c06819fd10585e2a690a |
| acl-adverse-v1/directory-writable/native-baseline-v1/independent-native-baseline-v1.json | 69ab718b962d183534c14d76505386b6e16a80a1d48f9319cf121feb38a97f3e |
| acl-adverse-v1/dependency-writable/native-baseline-v1/independent-native-baseline-v1.json | f5bd89d56409f9e707c148509f53d6b202c81f75b121bb3aebdeadb1b9cf4c6f |
| acl-adverse-v1/entry-owner/native-baseline-v1/independent-native-baseline-v1.json | 307f7c31cd40475ad858ff4ea3424da4bed61446a9f20a699621f2dfef3e0940 |
| acl-committed-v1/source.zip | 6de0e5fa8c9725f25238c7ee3ceb84b0a04b34204c00b32e90a78843f594f97f |
| acl-committed-v1/producer.json | 4c21d0339dcd011079d76ca900fa85001e8ffa49a35058ad18281be2a7c37e7f |
