# E-I315 — closed remote consumers retain former owners and buffers

2026-10-10 CEST. **Medium memory retention; remediated preliminarily.** I06/V08/V10/V12 requires retired consumers to release their resource graphs. A held disposed `SftpContentSource` retains its stream and lease; a held returned `SftpLease` retains its channel, pool and connection owner. Closing native handles does not retire these managed references. Controlled SFTP/FTP/explicit-TLS/implicit-TLS cases retain a known 32 KiB payload after shutdown or failed cleanup.

Unchanged **e0bdade product with only the new test overlay** produces **28 retirement failures and four live-reader positives**. All owned file handles close, bytes remain unchanged, original close failures and idempotence remain correct; the failure is managed retention. The correction atomically removes a lease's return state and clears a closed reader's stream/lease fields before cleanup. Returned leases reject further channel access. Live readers, connection reuse, one-time slot return and original cleanup exceptions remain supported.

Declared source/test overlays pass **32 targeted controls**, the full preceding Remote suite (**2332 passes/156 exact skips**) and **156 affected App tests**. Every **2644 predecessor outcome/message/exact skip** remains. The same compiled 32 controls pass in both Windows and Ubuntu guests: **64 native passes**. Windows parent/child integrity 8192 and interactive guest session 1, Ubuntu UID 1000, all **78 staged file pins and 386 reused runtime pins** verify. Fresh independent queries find no owned payload processes or fixture/temp roots. Known bytes, native handle closure, failure identity and live exact reads are checked in every case.

The first postcheck reader wrongly expected VMware Tools to forward guest stdout; its empty output and refusal remain. Fresh checks write and retrieve their own guest results. A separate byte-equality reader refuses working CRLF differences; after proving only newline normalization to the already qualified source, its fresh successor passes. All owned probe/controller sources are additionally preserved on V:.

These are controlled in-memory protocol/owned-file ownership checks, not native-server incidence, credential disclosure, whole-process peaks or a new aggregate budget. I06 remains open for wider consumers/native accounting/reference/input/human/candidate scope. [Exact committed/native follow-up](E-I315-I316-native-qualification.md) now passes at 17035d5; original hosted collection remains separate. Physical-source HOLD and human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact source, commands, original failures, skips and native restoration.

| File | SHA256 |
|---|---|
| `i06-remote-consumer-20261010-v1/independent-remediation-v2.json` | `199a19b21abd85a2c339305ce19156f49bb594561fb7fd6c7bb9bd9ab44b1a2c` |
| `i06-remote-consumer-20261010-v1/i315-discovery-v1.json` | `2b9f11cc98be4b13815a634bef025b5882a1268beb18dbfdf8914e71af681495` |
| `V:/FileCat/artifacts/release-evidence/i06-remote-consumer-20261010-v1/baseline-v1/inputs.json` | `d21ec81a7edff9dbc18e82d78d396b28744ad2665279709c6555c44023d04f9c` |
| `V:/FileCat/artifacts/release-evidence/i06-remote-consumer-20261010-v1/fixed-v1/inputs.json` | `2d0d20244287455b297891e3f89402cfb118b5cd8aea6e466e71149d8c03125d` |
| `i315-remote-consumer-native-20261010-v1/native-medium-v1/transport-final-v1.json` | `b72fe3ddb1af9b4086bc3f16900096b3eba5ec8043610b5090fb3ff852cb79c2` |
| `i315-remote-consumer-native-20261010-v1/linux/transport-final-v1.json` | `f0b8896846a189c65bd7aed86abccee0a19903092741548ec3d3f586c36876ba` |
| `i315-remote-consumer-native-20261010-v1/postcheck-windows-v2/independent-final-v1.json` | `18cd4ba95c63d5914faeb8c2594aca5c2f727849b8a9d0b35d0388dec2b9554a` |
| `i315-remote-consumer-native-20261010-v1/postcheck-linux-v2/independent-final-v1.json` | `b404251d5f4b4b9c5dcba4fe5652419d37dff05f879d4f105b7ddcad6558b26b` |
| `i315-i316-public-20261010-v1/retained-tool-sources-v1.json` | `ef6a6d7b0cb54af02dd11b5e4b634edec89e51ad769c6643791158b5c8c9eb92` |
