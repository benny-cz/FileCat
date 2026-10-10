# E-I316 — panel-retirement fixture can observe a pending preview too early

2026-10-10 CEST. **Medium validation reliability; remediated preliminarily.** [Original ARM64 CI](E-CI-incomplete-linux-topology.md) observes a null reader in one panel-retirement case. The fixture stops future debounce ticks, invokes `LoadAsync` and immediately assumes a reader exists. If the debounce already started that same-key request, another loader invocation returns before the original request publishes its reader.

A gated owned provider proves the readiness defect with unchanged **e0bdade product and fixture plus only new tests**: both text and binary controls return before a reader exists. Releasing the gate then yields one open and complete known bytes. Both original controls fail specifically on early fixture completion. This proves a fixture gap, not the historical hosted cause.

The correction waits for the actual `PagedReader` positive checkpoint. The original **five-second bound** and every retirement/reuse assertion remain; no retries, skip broadening or production viewer change occurs. Both controlled tests pass, and **35 affected tests pass**, including all fourteen existing panel-retirement/reuse controls and QuickView lifetime/folder-demand controls. Every predecessor name/outcome/message/skip remains. The first private harness compilation refusal caused by an ambiguous provider type is retained separately; the fresh probe adds the explicit type alias before any product/fixture observation.

Source qualification uses declared test-only overlays, preserving exact artifact identity. Committed/ARM64 hosted follow-up remains; no live desktop/input, installed-candidate or whole-I06/I108 acceptance is inferred. Physical-source HOLD and human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact source, commands, original failures, skips and native restoration.

| File | SHA256 |
|---|---|
| `i316-panel-preview-readiness-20261010-v1/independent-remediation-v1.json` | `a8c8102af5faf71ece19b00aab9841e3690297b7bece772afde2eeb8303013ca` |
| `i316-panel-preview-readiness-20261010-v1/i316-discovery-v1.json` | `d44b6335b9684d2ca2fbcb1991acb3f40aa427e659d67d9b544b53a210d7bdd0` |
| `V:/FileCat/artifacts/release-evidence/i316-panel-preview-readiness-20261010-v1/baseline-v2/inputs.json` | `e319388ac4661121d797ac580d974a1e49ca2a47e98866eb6a0a2c4bdab34546` |
| `V:/FileCat/artifacts/release-evidence/i316-panel-preview-readiness-20261010-v1/fixed-v2/inputs.json` | `bac0e538f87caa72d6785f44f366b8b5d20abcb331c88fa9acb8e2435fc20746` |
| `V:/FileCat/artifacts/release-evidence/i316-panel-preview-readiness-20261010-v1/baseline-v1/controls/command.json` | `2a3f91ddb946c6e1669e4479269eabd840e12f53fed3e8d5c4ce9921a52adeba` |
