# E-I324 — revalidate broker admission when consent returns

2026-10-10 CEST. **High, consent freshness/requester lifetime; remediated preliminarily.** The broker validates a plan's fifteen-minute age, requester and nonce before its dialog, then previously entered either execution path without rechecking after approval. A requester can exit, the plan can expire, or cancellation can arrive while the dialog is open.

The controlled baseline retains **six failures/twelve positives** on the Windows host and **six failures/44 positives** in the signed-in Windows guest. Four expired/requester-exit cases actually create an owned directory or enter the owned read callback. Consent-time cancellation enters the read callback; the write runner already honors its stop marker, but reports consent/execution rather than declining before admission. No source device opens. These are actual one-plan orchestration and secure file exchanges, with declared clock/requester/nonce/consent/read callbacks. The initial baseline includes only boundary adapters, not a safety correction. This does not establish native UAC input, an actual requester-token transition, a spoofing exploit or an installed-candidate result.

After approval the broker checks the exchange stop marker, validates the same in-memory plan at the current time and rechecks the requester **before reporting consent or entering either execution path**. Cancellation declines without effects; expired/unavailable-requester plans report refusal without effects. The nonce is claimed once, and ordinary declines, initial expiry/requester refusal, replay, changed digest and fresh write/read positives retain their behavior. The installed entry supplies the real clock, requester/nonce checks, native consent and device-read implementations; adapters are internal and have no runtime/environment selection.

All **eighteen corrected controls and 32 affected tests pass** on the host; the medium-integrity guest passes all **fifty combined controls**. Parent and child integrity are measured as **8192**, session1. All twelve earlier positive and 32 affected host outcomes/messages remain; all 32 native affected outcomes remain, including their original theory-display escaping. Every **1484 canonical raw blob**, six declared source/test/project/lock overlays, **102 staged/386 reused runtime checks**, secure exchanges and owned process/temp/Registry-fixture restoration verify independently. The first affected test failed because its missing-helper assumption no longer covered the untrusted test-output apphost introduced by the project reference. A fresh baseline/fix pair explicitly verifies the actual unavailability reason and unchanged Registry value; the original failure stays retained. The lock change adds only the existing PrivilegedHost project reference, no new package version.

Broader I17 still requires actual installed UAC/consent/token/path/loader/pipe/requester lifetime and human/candidate qualification. A recheck does not eliminate subsequent races or establish PID reuse resistance. No physical-source HOLD, owner decision, contract or publication gate changes.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain exact inputs, compiled artifacts, commands, original failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i324-broker-consent-admission-20261010-v1/independent-remediation-v1.json` | `c8f8906aa2e72b44658ed9d19ed6cdecba8233b1109cf6a62166fa6840114598` |
| `V:/FileCat/artifacts/release-evidence/i324-broker-consent-admission-20261010-v1/baseline-v2/inputs.json` | `9d6eacf78ca21d87810dfff1de99a201adec9711d3d67f84c406dc680f6d1a6c` |
| `V:/FileCat/artifacts/release-evidence/i324-broker-consent-admission-20261010-v1/fixed-v1/inputs.json` | `0f30cb8935ee8326a0a27b5fe0b4eae25adbe475d89b3ffc351e039612e88763` |
| `i324-broker-consent-native-baseline-20261010-v1/native-medium-v1/transport-final-v1.json` | `4592a9a97fba907168c1156e73a04d972bb56da55768b4314dce0d226d653b25` |
| `i324-broker-consent-native-fixed-20261010-v1/native-medium-v1/transport-final-v1.json` | `e66874838dd2a6b691663492bba9a017b46e5c61c651252f7be3f1e52c802a21` |
| `i324-broker-consent-native-baseline-20261010-v1/postcheck-windows-v2/independent-final-v1.json` | `8784fe8c5a19e14143decf4bf2ebfddd4ab9bb419c19b0c641617c01fd989b07` |
| `i324-broker-consent-native-fixed-20261010-v1/postcheck-windows-v2/independent-final-v1.json` | `7acf625c093877c188a91dfa1c320338ee1029ffbeeafe9a33b755e8ee22d5ad` |
| `i324-i325-public-20261010-v1/retained-tool-sources-v3.json` | `741e0e6f0a2c24f48c4f7afa85b141ed8a09ea592954491bd70c810880f9c55d` |
