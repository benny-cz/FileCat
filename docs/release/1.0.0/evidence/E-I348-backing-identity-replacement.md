# E-I348 — refuse after a source-container identity changes

2026-10-11 CEST. Baseline **0beaa10c8a7bc11c307c34a0506d3a77669e98a4 /1583 canonical raw Git blobs**, declared identical test overlays and a separate corrected product overlay. After the actual owned source moves into staging, an independent file replaces its old path. The path stays observable with a different identity; explicitly controlled unavailable staging-ID answers hide the retained actual original ID. Each baseline retains **six failures/six healthy cases: four complete original-source deletions and two publications of original-source bytes as extracted output**.

The correction refuses cleanup/publication when a previously captured backing identity changes or becomes unavailable. It preserves both the complete original staged bytes and replacement-file bytes, closes sources, refuses output and rolls failed-copy progress back. Healthy extraction remains usable. All **24 final local/48 native controls** pass through close, close I/O/unexpected failure, verification and publication retry/skip routes. Actual Unix device/inode and native Windows volume/file IDs remain behind the staging-query control; portable Windows Core identities are explicitly controlled.

Every **6242 predecessor outcome/message and 131 exact skips**, plus **347 affected headless App results: 335 passes/twelve exact skips**, remains. Independent checks verify **1449 native files**, absent owned processes/temporary roots and stopped Windows listeners. Four owned host scratch roots/34 files are archived, hash/exclusive-open checked and restored. Windows high RID 12288/session 0, Ubuntu UID 1000 and Mac UID 501 use pinned .NET 10.0.12.

The first proposed refusal changes wording required by one existing open-renamed assertion. Its local and Windows/Mac failures remain adverse; fixed-only v2 preserves “source image or container file” and every predecessor outcome. One Linux fixed preparation starts before the host affected TRX exists and stops before any guest command; that error and partial host preparation remain. Completed v1 baselines are reused with identical safety assertions.

[Exact I347](E-I347-native-qualification.md), [its original CI](E-CI-backing-visibility-loss.md) and [Mac diagnostics](E-I06-hex-retirement-investigation.md) have separate identities. [I350](E-I350-initial-backing-identity.md) separately refuses unavailable initial identities. Provider/handle provenance, other aliases/topology/atomic transitions and physical/candidate qualification remain. No host UI, physical source, package or persistent setting changes. [Exact committed/native qualification](E-I348-native-qualification.md) and [original four-lane CI](E-CI-backing-identity-replacement.md) now pass at 9710970. I106/I110 physical-source HOLD, owner decisions, candidate qualification and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence; nested records retain exact source, commands, raw failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `backing-identity-replacement-safety-20261011-v1/independent-final-v1.json` | `295500cbfe4d84312caeffb59c1ece88b0ed3f3e09cfef43fcfe1aff693b7b56` |
| `backing-identity-replacement-safety-20261011-v1/retained-tool-sources-v1.json` | `b934ed83eb73cfb0b12182d109ce63c2d138ecb5b935dccf8c9a9103919a8279` |
