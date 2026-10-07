# E-I201 — broker report consistency and uncertain effects

Recorded 2026-10-07. **Preliminary working remediation; exact committed and CI checks pending.**
Original product: 05ba1e77ea4d31e2d260ad17096ea53d31b3bd31. Scope: I17/V06/V23 broker result consumption, V03 durable outcome truth, and I06 bounded result input.

A two-step report containing two committed records for index zero was counted as two completed items and journaled Committed, while only root zero was marked completed. A report marked Finished with only the first step bypassed the unknown-effect warning. Null steps and negative indices raised runtime exceptions after report admission. A genuinely unfinished prefix emitted an uncertainty issue but durably recorded Failed or PartiallyApplied for the unreported operation. Skipped was counted as Failed. The standalone JSON parser also accepted a report beyond the existing exchange byte limit.

Thirty-one controls exercise the production consumer, actual CRC journal, JSON parser and owned Windows exchange. No plan is executed and no UAC/installed-helper/consent dialog is launched. The baseline is canonical original source with two explicitly pinned overlays: an internal method extraction whose entire original report-interpretation body is byte-identical, and the final tests. It is not an unmodified original binary or native installed-helper qualification. The independent reader verifies this equivalence, exact wire bytes/SHA-256, every journal CRC and actual counters/root positions/issues/outcomes. Baseline: 21 adverse failures and ten positive passes; 32 raw observations.

The correction validates the complete report before applying counters: exact nonce, ordered contiguous indices, complete Finished/Stopped reports, consistent consent/refusal flags, defined outcomes, non-null steps/messages and nonnegative item counts. Malformed reports yield a durable Uncertain outcome with zero counted roots. A valid unfinished prefix keeps known completed roots and journals the unreported effect as Uncertain. Valid skipped/stopped/mixed/refused and complete progress/final reports preserve their distinct outcomes. Report consistency does not authenticate the user-writable exchange or establish a new privilege boundary.

The parser enforces the same 32 MiB input limit. The exchange opens one reader-shared handle, checks its length, allocates at most that limit and reads exactly from that handle; a concurrent writer cannot grow that open file. The finite limit/exchange controls and code inspection support this mechanism; no deterministic size-race or native hostile IPC capture is claimed.

Working overlay: all 54 broker/consent/platform controls pass, preserving the 23 preceding platform outcomes. Core: 49 pass and one existing explicit mounted-filesystem fixture skip (`FILECAT_TEST_MOUNT_INSIDE` unavailable). The corrected tests are byte-identical to the valid baseline tests. Independent working seal verifies 1128 canonical original blobs, all four working overlays, 314 actual payload references, 32 retained files and 64 wire/journal observations across baseline and correction.

The first fixture run is retained: its journal reader did not share the live writer, and its runner mode variable was shadowed by a Git mode. Those fixture failures do not establish product defects. V2 closes the journal before inspection and uses a separate Git-mode variable. The fresh baseline then reproduces the 21 product failures. Original files, commands, stdout/stderr, TRX, source snapshots, extraction and failure guard are pinned below.

Private `FileCatReleaseEvidence/br201-v1`:

| Retained path | SHA-256 |
|---|---|
| ElevatedJobExecutor-original.cs | c10a6a33f6f96d24542ce2e7de95261a750143d6deed53a0f9e980c605f75634 |
| ElevatedJobExecutor-seam.cs | baa0b6e4151529f47a237f7b7317da54e2fc794af2ed8f6f40b7cbac1349d57b |
| ElevatedJobExecutor-working-v2.cs | 622d8968d13d8c35f7a639991eda7d7dd6d658ee29768a22e658d11520ac59a3 |
| ElevationExchange-working-v2.cs | 38cb7df9a42ca796b0451238579087e3dd149b21f3a1d44b67da34253819587c |
| ElevationPlanCodec-working-v2.cs | 80de94fca8ad408ea9c4729005db9c0e1c2d486b971c5fc8968faf8082c23178 |
| ElevationReportTests-v1.cs | cc13e0cc5a27975e702c363706bcece5a810ddc1eba83b43a12584f4ef6235ad |
| ElevationReportTests-v2.cs | 751c4b129db2218f8230535cb16b8e41db66a8f3f0c28078830f46a0f316960b |
| fixture-failure-v1.json | 19b4a98e834e51ff3bc62ebd32e083d728385f355bf36d1affc55a219a11da3c |
| I201-discovery-v2.json | fc019f0a994dd8d7aa2d4931dd886c16b2c35f6762ed6e00eae8bca6e39aa079 |
| independent-broker-observation-reader-v1.py | 8090758b332e2b56f31792782509b4b64f54d8a38cbfa03a55a0f96fa46df530 |
| run-broker-reports-v1.py | 7e49d39ee5f1906952eff878ffe8d830f2920239e6f061d07a96d75bfd13a77b |
| run-broker-reports-v2.py | eca0260b7e75c2e2dc10d9bb6e6c23282a0078484861b75db5a4b75f3e994fd4 |
| seal-broker-working-v3.py | 55efcfc4ad22e117b69d9e6d47189985e69e3dffef21deedb6008385c9978af9 |
| seam-extraction-v1.json | 63fbb14a47cde12cb517b1c33ea339d7a54ac41e42ae85c2a3d3d020610d604f |
| baseline-v1/command.json | 3db37d3315b29102b9f45ade5f202651deaca2b160cba911fcf13df060f660e5 |
| baseline-v1/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| baseline-v1/core-stdout.txt | 2bf3390ff356f511c2e8ccfa5c0d5cca302104fddd88519ffc34ea5c7f4fee5a |
| baseline-v1/platform-stderr.txt | 0f9230d14913c1ec4a43261c5175461db546b11045624f58e81f19e4a31b4da4 |
| baseline-v1/platform-stdout.txt | 5c41b56a463ce80907ae11dd8c23cba41953c0250670ba0ed503b980abfac8a6 |
| baseline-v1/results/core.trx | 5720ca091f4631322bceb04054782dccd8e9b94f92fe2973988f60e110123b0e |
| baseline-v1/results/platform.trx | 71d3c8bf920d871664ec481f784317235d42481cdc4329dd2445e9fb25f1618c |
| baseline-v2/command.json | b61ea659ff3b0bd968777b6f5e833f51ee37681ae528077aa7967085547931a7 |
| baseline-v2/platform-stderr.txt | 69fc9630aef316dc608c696de90650d5a383ece49ca10b66b0f0df7dabadc38e |
| baseline-v2/platform-stdout.txt | 2a5073d80a72ddc1511d684953a354dd73ae4154667de5b19f1bd8cfcc41b32e |
| baseline-v2/results/platform.trx | 5f9d311b6645388742e6035c52dd6646ac31d6eaaed4dd7a2288a19f8ff33ac3 |
| working-v2/command.json | dd4fc36f8a37b765ad5217e53cd0118c1adbc9d833f1d48717b4edb308c415cd |
| working-v2/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/core-stdout.txt | b2cc480344bc30446e9a29992e2355f2ce2c870d809ef3b468b0ec103acfd071 |
| working-v2/platform-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/platform-stdout.txt | 96737b0b38f8da7ada9dc94acd97d84a296d0c5c9555ac833af4b32420b00a8b |
| working-v2/results/core.trx | 723f385343eac586a5e7a8ea63bd0c2d68fcf55b40b2cbbeb300a97f17b23d94 |
| working-v2/results/platform.trx | bb3f5b15facd1a9e180d5a9f4e4e7c9f608003a5c089e1e8989aa6f29c3a7818 |
| independent-broker-working-v3.json | 7c016e9157320f1641f610f14e8f20ba8f6285951e3f99ff41b8d498fccfdc6f |

The broader I17 limited-account, installed-helper, requester/consent/lifetime and exact-candidate matrix remains open. All 24 final-candidate campaigns, physical-source/USB hold, contract freeze, custody/signing/human GO gates remain. No persistent Mac/VM setup, physical source, release candidate or publication changed.
