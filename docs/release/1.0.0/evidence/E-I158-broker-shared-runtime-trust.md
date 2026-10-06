# E-I158 — FDD native helper accepts a user-writable shared CoreLib

2026-10-06. High pre-consent runtime code-trust defect under I17/V06/B04. Open; correction underway. No candidate/stable GO.

The exact committed **b4b6e1b281c24b86e2d66f5482bf3d0bfda0e140** native executables and published inputs remain unchanged, apart from the explicitly synthetic managed entry observer used to report CLR handoff/loaded CoreLib location. This does not run production plan/consent or any device. In the authorized disposable Windows VM, the runner records the original installed `.NET 10.0.9 System.Private.CoreLib.dll` content/owner/SDDL and adds only one explicit current-user SID Modify grant. File content and SYSTEM owner remain unchanged. The guard still trusts the selected hostfxr and its ancestors; it does not examine the subsequently selected shared runtime.

The actual FDD broker runas launch loads **C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.9\System.Private.CoreLib.dll** with that grant and reaches the synthetic entry in returned Admin PID **5724**, exiting 41 naturally. The distinct SC control reaches its synthetic entry in PID **1896** using its protected adjacent `.NET 10.0.12 CoreLib`, not the modified shared file. Original arguments/system environment/path and same-process administrative identity verify. The caller is already administrative; this proves acceptance of an explicit user-writable runtime component, not an observed byte substitution, limited-user escalation or UAC/consent bypass.

The runner restores the exact original installed runtime SDDL in finally before completing. Before/after/after-restoration CoreLib SHA-256 all equal **dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a**; original and restored full owner/group/DACL SDDL match exactly. Two independently positive native DLL controls, complete payload/output pins and owned process/protected-fixture cleanup verify. No runtime bytes, physical device, persistent security policy or outside-VM system change. This temporarily altered one installed VM runtime file's ACL, explicitly restored; it was not restricted to the owned Program Files helper fixture.

The correction must establish shared-runtime file/tree/ancestor trust before hostfxr can load CoreCLR or managed runtime code. Shared runtime servicing remains supported from the protected default architecture root. Complete resolution/races/token/UAC/consent and installed-candidate qualification remain in I17.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-loader-20261006`.

| Retained path | SHA-256 |
|---|---|
| shared-runtime-v1/native-baseline-v1/independent-native-baseline-v1.json | d976c5a3dc4d38082cf4718e5f29581da4b11b2049a79547e6ca73c12567ca4a |
| independent-i158-baseline-v1.json | d182a011fad958e73d6f9f9f02dc752af5c569ef63a5dbf900b1be9478dba6d6 |

Current 135/158 preliminary remediations, one Closed, 22 remaining issue remediations. All campaigns need final qualification; NO-GO.
