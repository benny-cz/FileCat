# E-I322 — refused recovery images retain their open source until collection

2026-10-10 CEST. **Medium resource availability; remediated preliminarily.** ImageFileSource opens its read handle before checking the format. A dynamic/differencing VHD or VHDX refusal throws from the constructor without disposing that handle. The failed object cannot be disposed by the caller; its SafeFileHandle eventually finalizes, leaving a handle/file-sharing claim until collection.

Unchanged **1b2229c product plus five controls** reproduces **three failures and two supported-image positives** on Windows, Ubuntu UID1000 and Mac UID501. Each refused owned 1536-byte regular input still has one open source after refusal, measured before any collection. Windows uses exclusive-sharing refusal; Unix uses actual descriptor device/inode identity without duplicating descriptors. A bounded no-GC interval and unchanged generation counts distinguish immediate ownership from later finalization. Raw and fixed-VHD controls preserve exact 1536/1024-byte payload hashes and close normally. Complete fixture bytes remain unchanged; finalizer cleanup is retained separately and is not accepted as immediate release.

The constructor now disposes the acquired handle on any subsequent exception and rethrows the original error. Format support, read-only access, fixed-VHD footer exclusion and successful-reader ownership remain unchanged. All **five corrected controls pass per platform**, with zero retained handles after refusal. All **125 affected tests pass**, with the same **34 explicit skips**; every **159 predecessor outcome/message/skip** remains. All **1477 canonical raw blobs** and declared source/test overlays verify. Independent postchecks verify **172 staged and 920 reused runtime-file checks**, no owned payload processes/temp roots, and full fixture restoration.

Separately, failure-only raw query diagnostics were added to the existing Mac mount regression without changing its safety refusal or assertions. The compiled three-overlay producer passes all **six Mac controls**, including the unchanged real mount replacement, 64 KiB exact known bytes and current source/target overlap. Its independent 43-product/267-runtime/image/process/temp restoration verifies. This does not reproduce or resolve the separately retained hosted failure.

No actual device, physical source, UI, account, privilege or persistent system setting is changed. [Exact committed/native I322](E-I322-native-qualification.md) passes at 156b723. Original hosted, broader provider/resource/whole-process bounds and candidate qualification remain. I06/I106 remain open; physical-source HOLD, owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i322-recovery-image-open-20261010-v1/independent-remediation-v1.json` | `eb4997ec73a51a7d1cae042fb11b0105ef2a98db8c0c50e1a9f8f64b2c5b522e` |
| `V:/FileCat/artifacts/release-evidence/i322-recovery-image-open-20261010-v1/baseline-v1/inputs.json` | `b4f37a0002a22dec6d6bd8882cda85ddb8a49d383b9e6fe37266e0b0c6a63800` |
| `V:/FileCat/artifacts/release-evidence/i322-recovery-image-open-20261010-v1/fixed-v1/inputs.json` | `ea0328084d3086045da3566623d0416207dff53304f2843fd8981d0065d6826f` |
| `i322-recovery-image-open-20261010-v1/baseline-macos-v1/transport-final-v1.json` | `2278042fb74e4016a2e8cd8935bb8854d4691727ed2a6eed31cf41fe6b580cfc` |
| `i322-recovery-image-open-20261010-v1/fixed-macos-v1/transport-final-v1.json` | `237df51caf733473fc9c2267d0a24156037f2b13a2d6c6be7d7a1530cb11ab58` |
| `i322-image-open-native-baseline-20261010-v1/linux/transport-final-v1.json` | `b4873bf8f4464d41a2f699036c5c3f2731d9c0f65e12c19047b24d9bcaf3b472` |
| `i322-image-open-native-fixed-20261010-v1/linux/transport-final-v1.json` | `7f26454a3a3505c642544eadcce2b89ff1e04f8a166b0c78883c094db4acfefc` |
| `i322-public-20261010-v1/retained-tool-sources-v1.json` | `e89365e1520b1128171399113018ba79d2305998553325d091141342b3be489b` |
| `i322-recovery-image-open-20261010-v1/independent-diagnostic-final-v1.json` | `534c45ca11c188622f10f71606bea1e6177a6438f5812815ec48bb2d6a9109c9` |
