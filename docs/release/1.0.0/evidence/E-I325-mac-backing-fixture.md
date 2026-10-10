# E-I325 — native mount oracle respects unavailable backing

2026-10-10 CEST. **Medium validation reliability; CI fix, remediated preliminarily.** The original **38074627662 attempt1 at c0677d9329d278294ad2fc31249772691f24d1b9** Mac job fails its mount assertion. The digest-verified actual queries show its image disks as Disk Image, its APFS root's store as disk0s2, and backing disk0 with **empty BusProtocol /VirtualOrPhysical=Unknown**. The product correctly retains unknown; the test assumes complete backing classification. This observation explains that original job's null target. It does not retroactively establish the causes of earlier runs whose backing replies were unavailable. Whole-run CI acceptance remains separate.

With c0677d9 raw sources and declared query-only substitution matching those unavailable fields, the original real-Mac mount test reproduces **one failure/nineteen passing query controls**. The correction keeps the full known-backing assertions: old image membership, no source overlap before replacement, actual new parent, current source membership and overlap after replacement. When the containing backing is unavailable, the test explicitly requires unknown before/after and a recovery-destination refusal. Both branches still switch two owned images at the same mount, verify **65,536 exact bytes**, changed detached-image hash and complete cleanup. The direct known-byte fixture write is distinct from recovery admission; no recovery write is admitted.

The fixed producer without query substitution passes **twenty Mac controls** with actual known backing. The independently compiled fixed unknown-reply producer also passes **twenty**, explicitly retaining unknown and the cannot-tell refusal. The host passes **nineteen query controls/one native-only skip**, preserving every eighteen predecessor query outcome/message. All **1484 canonical raw blobs** and declared test/query overlays, **129 staged/801 reused runtime checks**, owned images, process/temp and fixture removal verify. The unknown-reply substitution is private test instrumentation and is absent from the proposed product. Production topology interpretation and source safety are unchanged; unknown never becomes proof of a separate disk.

Broader active-source identity/topology/races, I106/I110 physical-source attribution HOLD, installed-candidate and human acceptance remain. No physical source, workstation UI, persistent system setting, contract freeze or publication changes.

[Exact committed/native qualification](E-I324-I325-native-qualification.md) passes at 55deaa3, without source/test overlays. Original baseline and controlled unknown-query producers retain their distinct identities.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain exact inputs, compiled artifacts, commands, original failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i325-mac-backing-fixture-20261010-v1/independent-remediation-v1.json` | `cffb493464b2e5f9b2d81bf5bb3367b58f2bcfe95b5b55900259e8a4ea50d464` |
| `i323-ci-20261010-v1/mac-artifact-diagnostic-v1/actual-partial-mac-v1.json` | `bad651499dd1f7ee03fb145ace8e111bb7d22c321caf6367b3754f738adac468` |
| `V:/FileCat/artifacts/release-evidence/i325-mac-backing-fixture-20261010-v1/baseline-unknown-v1/inputs.json` | `1af7cf1f7c3a2d0e35d6a4abd80c3a047c5d2951d7fedffdf7ca22dff1e526a6` |
| `V:/FileCat/artifacts/release-evidence/i325-mac-backing-fixture-20261010-v1/fixed-known-v1/inputs.json` | `b0b85aa53c3c3030562b36453f3ec828847d19be71488558e76cb149c4e677d6` |
| `V:/FileCat/artifacts/release-evidence/i325-mac-backing-fixture-20261010-v1/fixed-unknown-v1/inputs.json` | `e60213c4da5753385768f3046776912c43423d36236238bce6c8ab794504ed93` |
| `i325-mac-backing-fixture-20261010-v1/baseline-unknown-macos-v1/transport-final-v1.json` | `dda83a8f45d6a325992266f13c9dd1c297bb08d9050677db111d263891cebdd1` |
| `i325-mac-backing-fixture-20261010-v1/fixed-known-macos-v1/transport-final-v1.json` | `a9e22da4207396b0039f70f94f7dd7b27ac7522c991ef67795db5fd10b3b36dd` |
| `i325-mac-backing-fixture-20261010-v1/fixed-unknown-macos-v1/transport-final-v1.json` | `f9368a42f5b7c205d408ae87b3e7fb2800135e92d8614587bd5cdb1feb6bf803` |
| `i325-mac-backing-fixture-20261010-v1/postcheck-v1/independent-final-v1.json` | `488dc245669d0f52815f7f8940b08e47cb4f0a8a2cc116e864103a8e8e309262` |
