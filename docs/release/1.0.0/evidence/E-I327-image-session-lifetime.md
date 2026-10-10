# E-I327 — cached recovery-image retirement closes live readers

2026-10-10 CEST. **High: recovery content availability/ownership, I06/V09/V12/V23.** Actual original **3fc9dcaf3a6d3a3b3b06b8892fb4a0fb41194040 /1496 canonical raw Git blobs**, with a declared test overlay, reproduces **eight failures/three passing device controls** on the host and each Windows/Ubuntu/Mac guest. Opening enough other images, forgetting an image, clearing cached sessions or replacing its path closes the original native source while one/two live `IContentSource` readers still hold its content. Their next read throws `ObjectDisposedException`. This directly tests content reads; no end-to-end job or physical-device result is inferred.

The fix gives **image-file windows** a shared source lifetime independent of their cached scan. Retiring the cache drops its ownership; active image readers keep the opened source until the last reader closes. `RecoveryContent` releases only the image window it owns, outside its read/close lock; the existing public shared-source constructor keeps its original ownership contract. Reader acquisition and cache retirement are serialized. The lifetime owner captures the source/window, not the scanned session/tree.

All **eleven controls** pass on the host and each native platform (**33 native fixed passes**). Exact **4096-byte** payloads remain readable through cache eviction, refresh, cache clearing and path replacement. Replacement controls read the old open image and new current image separately. Actual Windows exclusive-sharing observations and Unix inode/descriptor counts show the retired source remains open with live readers, survives the first/double close when another reader remains, and closes immediately after the last. Acceptance forces no garbage collection. Every owned source remains byte-identical except the explicit fixture-owned replacement, which is independently compared with its expected old/new bytes; all temporary images and handles are closed and removed.

**Device lifetimes are not extended.** Three owned-regular-image adapters, carrying a synthetic device label, retain immediate device-reader retirement on eviction, cache clear and explicit reselection. No real device is opened. Binding physical-destination admission to held native source identity, broader source/session races and **I106/I110 physical-source HOLD** remain separate. These image controls do not resume physical testing.

Both original and fixed sources preserve all **109 affected Core passes/19 exact skips**, plus **25 affected App passes/ten exact skips**: every **163 predecessor outcome/message**, all 29 explicit skips and every three device-positive outcomes remain unchanged. These are finite affected subsets, not a full-suite/candidate claim. The fourteen prior recovery-reader graph/concurrent/reentrant controls remain among the affected passes. Exact committed and original hosted qualification follow after the product push.

Independent qualification verifies every canonical blob and declared overlay, actual host/native observations, **262 staged/1306 runtime checks**, Windows parent/child **RID8192/session1**, Ubuntu **UID1000**, Mac **UID501**, native before/after pins and separate owned process/temp postchecks. The original native test failures survive with their raw output and restoration. No workstation UI, guest setting change or aggregate/native process-peak acceptance is used.

An initial private `git archive` export is refused before compilation because **878 exported files do not match raw Git blob identities**. Its archive, partial extraction, actual mismatch inventory and failed controller survive; no cause is assumed. The accepted source package is built from `git cat-file --batch` and every raw blob hash is verified before use. This failed export is not product evidence and is never relabelled as canonical. All baseline/fix producers retain their own declared overlays; no exact-commit artifact is fabricated.

Broader I06 aggregate/native/reference/human, physical-source, contract/candidate and publication gates remain open. I327 is **Remediated preliminarily** for this finite source-lifetime defect; no wider issue closes.

[Exact committed/native follow-up passes at 767adc4 with no source/test overlays.](E-I327-native-qualification.md)

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested receipts retain raw source/output/command/native input hashes, complete failures/skips and independent restoration.

| File | SHA256 |
|---|---|
| `i327-image-session-lifetime-20261010-v1/independent-remediation-v1.json` | `35bc7cd2c6d660d3be1fbf28aa0bd4d72361930e5c07bab1d9ebfdada996c6aa` |
| `i327-image-session-lifetime-20261010-v1/canonical-export-failure-v1.json` | `8c55ad335923ed1e9341bf793664c56cb9d5da8752255201e6765a6b8dca5572` |
| `i327-public-20261010-v1/retained-tool-sources-v1.json` | `d1465d55a64f10ef73c5f639465bf50a38e1d8f674b120958ca53d2e11ebdd54` |
