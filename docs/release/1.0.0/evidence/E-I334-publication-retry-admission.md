# E-I334 — publication retries recheck backing sources

2026-10-11 CEST. Baseline **9c8e6cf75085db2ea8dfe699b047ab2bb5e64318 /1529 canonical raw Git blobs**; declared test/product overlays have separate pinned identities. This is a High/Critical controlled source-safety gap within I106, not a claim of native kernel alias replacement. No physical source is opened.

## Finding and correction

The stream executor checks the target before publication, then its I/O helper can retry the rename without repeating that check. A controlled adapter changes the backing identity or final-path answer after the first failed publication. Both explicit Retry and automatic sharing-error retry then replace the owned backing file with the recovered output. Four adverse controls reproduce that bypass with real staged bytes/renames; a fifth demonstrates a skipped publication retaining 24 copied bytes despite no output. Two ordinary retry controls remain healthy.

The executor now checks the backing target inside **every publication attempt**, including automatic and user-requested retries. A refused retry cannot reach Move, even when the decision responder tries once more. Failed publication removes its copied-byte progress along with its staged file. Ordinary publication, successful retries, Skip/Keep Both and prior recovery/archive controls retain their behavior.

## Validation and retained failures

[Exact committed/native follow-up](E-I334-native-qualification.md) qualifies 5ec6968 /1532 raw blobs/no overlays: all seven local/21 native controls, every 3448 predecessor outcome/message/107 exact skips, complete observed bytes and 907 independent native file checks.

All **seven final local controls and 21 fixed native controls** pass across high-token Windows, Ubuntu UID 1000 and Mac UID 501. All **3448 affected predecessor outcomes/messages and 107 exact skips** remain: local 674 passes/17 skips; Windows 901 passes/18 skips; Ubuntu and Mac each 883 passes/36 skips. The native class selection is wider than the local method selection; each compares its own full baseline/fixed inventory. Independent postchecks verify **1161 native files** and no owned process/temp tree. Complete source, old destination, incoming and final output bytes, publication counts, content closure and failed progress are inspected independently.

The initial fixture omits GetRevision and fails compilation before tests. Its corrected producer reproduces five failures/two positives. The first fixed producer preserves the source and clears progress, but two assertions incorrectly expect one prompt: its responder intentionally retries the first safety refusal, so two prompts are correct. That original two-failure/five-pass producer remains adverse. The final fixture changes only that strict prompt-count expectation; no source/progress/closure assertion is weakened. Independent-reader setup failures from treating a historical Before digest as a current overwritten source path are preserved in their original tool observations; the final reader checks those historical bytes against the immutable canonical Git archive.

The backing-path/identity transitions are recorded adapter answers, with real owned-file publication; they are **not native kernel alias-transition or atomic rename evidence**. Windows copies exercise PortableFileOperations and the shared executor. Hosted/native kernel-transition/installed-candidate repeats, broader mount/path races and physical-source HOLD remain. No package, persistent setting, workstation UI, physical-source resumption, contract freeze, candidate or stable publication is implied.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact inputs, commands, original outcomes/skips, whole bytes and independent cleanup.

| File | SHA256 |
|---|---|
| `i334-publication-retry-safety-20261011-v1/independent-remediation-v1.json` | `0edabc9cc95739cd8da24c288a357f136b495b52c40317f77214b123de163022` |
| `i334-publication-retry-safety-20261011-v1/retained-tool-sources-v1.json` | `34af50fa7c829cc4c36c8eb3d33fadef479f716e19537403f904f223cd17aaa5` |
