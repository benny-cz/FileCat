# E-I158 — FDD native helper accepts a user-writable shared CoreLib

2026-10-06. High pre-consent runtime code-trust defect under I17/V06/B04. Remediated preliminarily at 69603ec; broader I17 remains open. No candidate/stable GO.

The exact committed **b4b6e1b281c24b86e2d66f5482bf3d0bfda0e140** native executables and published inputs remain unchanged, apart from the explicitly synthetic managed entry observer used to report CLR handoff/loaded CoreLib location. This does not run production plan/consent or any device. In the authorized disposable Windows VM, the runner records the original installed `.NET 10.0.9 System.Private.CoreLib.dll` content/owner/SDDL and adds only one explicit current-user SID Modify grant. File content and SYSTEM owner remain unchanged. The guard still trusts the selected hostfxr and its ancestors; it does not examine the subsequently selected shared runtime.

The actual FDD broker runas launch loads **C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.9\System.Private.CoreLib.dll** with that grant and reaches the synthetic entry in returned Admin PID **5724**, exiting 41 naturally. The distinct SC control reaches its synthetic entry in PID **1896** using its protected adjacent `.NET 10.0.12 CoreLib`, not the modified shared file. Original arguments/system environment/path and same-process administrative identity verify. The caller is already administrative; this proves acceptance of an explicit user-writable runtime component, not an observed byte substitution, limited-user escalation or UAC/consent bypass.

The runner restores the exact original installed runtime SDDL in finally before completing. Before/after/after-restoration CoreLib SHA-256 all equal **dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a**; original and restored full owner/group/DACL SDDL match exactly. Two independently positive native DLL controls, complete payload/output pins and owned process/protected-fixture cleanup verify. No runtime bytes, physical device, persistent security policy or outside-VM system change. This temporarily altered one installed VM runtime file's ACL, explicitly restored; it was not restricted to the owned Program Files helper fixture.

The correction must establish shared-runtime file/tree/ancestor trust before hostfxr can load CoreCLR or managed runtime code. Shared runtime servicing remains supported from the protected default architecture root. Complete resolution/races/token/UAC/consent and installed-candidate qualification remain in I17.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-loader-20261006`.

| Retained path | SHA-256 |
|---|---|
| shared-runtime-v1/native-baseline-v1/independent-native-baseline-v1.json | d976c5a3dc4d38082cf4718e5f29581da4b11b2049a79547e6ca73c12567ca4a |
| independent-i158-baseline-v1.json | d182a011fad958e73d6f9f9f02dc752af5c569ef63a5dbf900b1be9478dba6d6 |

Baseline checkpoint: 135/158 preliminary remediations, one Closed, 22 remaining issue remediations. All campaigns need final qualification; NO-GO.

## Correction and clean native/CI seal

**69603ec10e6b1631ef897b68eafae4a118bc8d8d** checks the default architecture .NET installation's complete shared-framework tree and ancestors before loading hostfxr. The existing SYSTEM/Administrators/TrustedInstaller owner/writer rules, conservative unsupported grants, reparse refusal and depth/entry/time bounds apply. Self-contained startup continues using its independently protected application tree. Other resolution routes, concurrent replacement and limited-caller/consent qualification remain open.

All four working native publishes and receipts verify. The comparison changes only the native EXE against the original observer baseline: protected SC/FDD both naturally exit 41 after the same-process synthetic handoff. With the unchanged installed CoreLib's explicit user Modify ACE, SC remains compatible while FDD produces no entry witness in the bounded window. Its dialog is unobserved and its image-matched owned process is terminated after six seconds; no clean refusal exit or visible text is claimed. Before/after positive controls, full payload/output pins and cleanup pass. Exact original installed runtime owner/group/DACL SDDL and SHA-256 restore in finally.

Clean committed export independently verifies **924 raw Git blobs**, four native publish modes/PEs/compiler receipts and all input/output hashes. Fresh protected and writable-runtime VM repeats preserve the exact committed native executables and published files except one explicitly synthetic managed entry observer; it reports the actually loaded CoreLib and does not execute production plan/consent. Launcher/control source is unchanged; the original launcher/control binaries are explicitly reused, not claimed rebuilt from this commit. Protected SC/FDD naturally reach entry, unsafe-runtime SC reaches entry and unsafe FDD does not; positives/pins/process/protected cleanup and exact installed runtime SDDL/content restoration pass again. Caller is already administrative; no physical source, limited-user escalation or UAC bypass is tested.

[CI 37433725218 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37433725218) passes all four required lanes, with ten artifact server digests/four build receipts/fourteen complete TRX inventories and both downloaded Windows native byte/source/compiler output receipts independently verified. ARM64 package startup/drawing and installer compilation pass. Two gh complete-log retrievals failed on GitHub HTTPS connection timeouts. Both failed reads and the original collector remain. Native curl connectivity verifies; the official authenticated run-log archive and all ten artifacts are retrieved through native curl. The existing gh credential is used only in transient stdin, with no token in argv, disk or logs. Original archive entries/bytes are hashed; the search view prefixes each unchanged event line with its archive filename/job label and retains this explicit normalization in the proof. Setup/compiler bytes reported by receipts are not independently downloaded, and declared environment skips remain in the inventories. No candidate or stable human GO exists.

| Retained path under FileCatReleaseEvidence | SHA-256 |
|---|---|
| broker-loader-20261006/independent-i158-working-v1.json | d24b166d77dc9ce7d8761b870b2b7a69100cf29adbdd35112f1e7beab9f2b9d3 |
| broker-loader-20261006/independent-i158-committed-v1.json | 14649ab6770ce37ca5fcf02143f9b4e4480a3f0e3708d084f14b8b5c337a226b |
| broker-loader-20261006/shared-committed-v1/producer-native-bootstrap-v1.json | ef1a4985dafe943a0c8b18f6277fca84fc52d656d3e39f654366376b61daebc9 |
| ci-37433725218-attempt1/independent-ci.json | c1ae3ae38bfe6f84d506c22d7b977d2932973747d6c2f355eb2344040db4a671 |
| ci-37433725218-attempt1/independent-ci-native-bootstrap-v1.json | 92f900394606d248d27408383e708e2f8b92c4aa6be1c86a89eeb33df3700625 |
| broker-loader-20261006/independent-i158-seal-v1.json | 61ecb0fcefa292783a7499a61a1e4384c57af92f24cdfb571fbdb4368f8f0fc0 |

Current 136/159 preliminary remediations, one Closed, 22 remaining issue remediations after new I159; all 24 campaigns require final qualification. NO-GO.
