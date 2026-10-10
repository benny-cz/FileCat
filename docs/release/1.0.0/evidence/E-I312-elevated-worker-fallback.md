# E-I312 — failed low-integrity startup passed administrator rights to a worker

2026-10-10 CEST. **High; remediated for preliminary controlled scope.** I08, V10/V23, SEC-001/SEC-004 and ADR-06 require accurate Windows worker boundaries. Original exact product **e0bee1489209e0387cf565d858215c11201ba01f** tries a low-integrity token, then silently falls back to CreateProcessW with the parent's token when that attempt fails. Under a measured elevated parent, the fallback worker has integrity **12288**, rather than the documented ordinary/medium fallback. It can append to an owned administrator-protected file. Its job, one-process and 96 MiB limits still hold; those limits do not prevent that write. No UAC bypass, real hostile parser exploit or natural failure frequency is claimed.

The VM control duplicates a process token and verifies distinct native token IDs before modifying only the isolated subject token's DACL. Denying TOKEN_DUPLICATE produces actual Windows error five on the production low-token admission call. The production fallback then starts unchanged owned helpers. Every original/clone/subject DACL is restored and rechecked. No existing account, global policy, UAC dialog, workstation UI, VM console or physical source is operated.

The identical native regression observer records **six original elevated-fallback failures/seven healthy positives**. The correction admits ordinary fallback only after positively querying the actual process token at most medium integrity; elevated or unknown identity refuses before CreateProcessW. Normal low-integrity startup still works. All **39 corrected native controls pass**: thirteen each under measured medium Admin, high Admin and an actual temporary standard Users account absent from Administrators. The six elevated fallback attempts start no worker, change no sentinel, and retain the explicit refusal. Medium and standard-account fallback still complete exact 8193-byte exchanges, write their own ordinary sentinel, deny the administrator-protected append, preserve memory/child limits, and support kill/disposal. Low-integrity controls and post-DACL-restoration startup remain healthy.

Seven permanent native-token regression tests pass; **115 affected broker/Registry/Shell tests pass with one exact existing benchmark skip**. All **109 predecessor names/outcomes/messages/exact skip observations** remain. Native restoration independently rehashes **98 staged product/observer files and 193 private-runtime files**, verifies complete retained sentinel bytes, removes all seven owned fixture pairs, and confirms no owned process. The temporary standard account/profile are absent; its exact owned-root grant and deleted owner metadata are retired. Result/payload roots remain campaign evidence.

The first private observer compilation fails on local-name shadowing; a preparer then refuses a Python path concatenation before guest commands. The first completed subject capture proves the high fallback and restores its token, but the controller later fails querying ExitCode through a managed object it did not start. These raw records remain. A fresh observer retains the actual native process handle; independent complete original/fixed runs then supply the assertions above. No original failure is overwritten.

The product correction is a declared overlay on e0bee14 until committed follow-up is sealed. Existing [I310/I311 committed and hosted qualification](E-CI-broker-consent-bootstrap.md) remains at e0bee14. Broader I08 Unix policy, native permissions/network/lifetime, actual parser integrations and installed-candidate qualification remain open. Ordinary fallback retains ordinary-user capabilities and is not claimed as a filesystem sandbox. Physical-source HOLD, no freeze/candidate/publication and explicit human GO remain.

Committed follow-up: [4f9d500 exact/native qualification](E-I312-native-fallback-qualification.md) and [original hosted CI](E-CI-worker-fallback.md) are now sealed. The preceding overlay/original-failure identities remain unchanged; whole-I08/candidate scope remains open.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain actual source, overlays, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i08-native-fallback-20261010-v1/independent-final-v1.json` | `6dceaa010623fac4e5c5779018233d17088a58d2a9a09f890fcb6c8379037639` |
| `i08-native-fallback-20261010-v1/independent-restoration-v1.json` | `bda82495b03e72a1478c69dbfa6e849d90f00afa47d749e3bfcb580b6cc01d60` |
| `E:/FileCat/artifacts/release-evidence/i08-native-fallback-20261010-v1/fixed-v1/inputs.json` | `20a7aa1122fc7346b6ebf30fc7eab8cdf643f1a60eedabcc71ac16ebcf964cfb` |
| `E:/FileCat/artifacts/release-evidence/i08-native-fallback-20261010-v1/fixed-v1/controls/command.json` | `040bf9c319afdff8f1311e514cfb9b3301106f9c5426b970f56729de95c6806e` |
| `E:/FileCat/artifacts/release-evidence/i08-native-fallback-20261010-v1/fixed-v1/affected/command.json` | `379832473c40b6bfb8f4e33a6a54fc8f5ad724f813be118e7e5fdd2bbc7d5e81` |
| `E:/FileCat/artifacts/release-evidence/i08-native-fallback-20261010-v1/predecessor-v1/command.json` | `f6e306a95d60d35802cbcae47a1792045c28609f4dc843be9b6fd3a25eec9440` |
| `i08-native-fallback-20261010-v1/build-v1.json` | `43b78e6e7f00846a5c0e45e69adc9464d4c90b320436190e3b96448ab7f102e6` |
| `E:/FileCat/artifacts/release-evidence/i08-native-fallback-20261010-v1/prepare-v2.py` | `5cf0eda2244894ad5917770568aac133f8affe35b607615439fa5a3909d117f3` |
| `i08-native-fallback-20261010-v1/native-baseline-high-v1/transport-final-v1.json` | `3c869abf427dda61cde925b627be3a5e05067d01752412a031d32149665f1564` |
| `i08-native-fallback-20261010-v1/native-regression-original-high-v1/transport-final-v1.json` | `529e39dc0f2c06ba3697f54ba9f2ca59ab0635aba24460a6815f7bca1814de6e` |
| `i08-native-fallback-20261010-v1/native-regression-fixed-high-v1/transport-final-v1.json` | `d57aeebd746d276091023d71975cbb1c21a9ce4336b8be210fb5cb65ce1a194a` |
| `i08-native-fallback-20261010-v1/native-regression-fixed-medium-v1/transport-final-v1.json` | `21bee7a2611fa4cb6983f8923e784a574707db73d7b533c1433db82c3ed8c9e8` |
| `i08-native-fallback-20261010-v1/native-regression-fixed-standard-v1/transport-final-v1.json` | `d68d455662d9b44360694f9fa5e24e9d3b5db980617f392450640e5dd98676ce` |
| `E:/FileCat/artifacts/release-evidence/i08-native-fallback-20261010-v1/native-regression-fixed-standard-v1/setup-v1.json` | `46da671cc590458a9138a260dd9be38b7b4da787e30758c5eb923d2829a622fc` |
| `E:/FileCat/artifacts/release-evidence/i08-native-fallback-20261010-v1/native-regression-fixed-standard-v1/cleanup-standard-v1.json` | `bbbf5254aa207107d0970eed1889db5857b0dd60654a323a860073bc3fbfcd0f` |
| `E:/FileCat/artifacts/release-evidence/i08-native-fallback-20261010-v1/seal-v1.py` | `adeee2c5ba9174645b809ecf4311813ba5c367de92f9ceb57e6f7266c0de9de5` |
