# E-I201 — broker report consistency and uncertain effects

Recorded 2026-10-07. **Preliminary remediation; exact committed controls sealed; original CI partial sealed; Ubuntu setup timeout retained.**
Original product: 05ba1e77ea4d31e2d260ad17096ea53d31b3bd31; corrected producer: f59343d01d9e6b6a33474700175eb446b9ff1178. Scope: I17/V06/V23 broker result consumption, V03 durable outcome truth, and I06 bounded result input.

A two-step report containing two committed records for index zero was counted as two completed items and journaled Committed, while only root zero was marked completed. A report marked Finished with only the first step bypassed the unknown-effect warning. Null steps and negative indices raised runtime exceptions after report admission. A genuinely unfinished prefix emitted an uncertainty issue but durably recorded Failed or PartiallyApplied for the unreported operation. Skipped was counted as Failed. The standalone JSON parser also accepted a report beyond the existing exchange byte limit.

Thirty-one controls exercise the production consumer, actual CRC journal, JSON parser and owned Windows exchange. No plan is executed and no UAC/installed-helper/consent dialog is launched. The baseline is canonical original source with two explicitly pinned overlays: an internal method extraction whose entire original report-interpretation body is byte-identical, and the final tests. It is not an unmodified original binary or native installed-helper qualification. The independent reader verifies this equivalence, exact wire bytes/SHA-256, every journal CRC and actual counters/root positions/issues/outcomes. Baseline: 21 adverse failures and ten positive passes; 32 raw observations.

The correction validates the complete report before applying counters: exact nonce, ordered contiguous indices, complete Finished/Stopped reports, consistent consent/refusal flags, defined outcomes, non-null steps/messages and nonnegative item counts. Malformed reports yield a durable Uncertain outcome with zero counted roots. A valid unfinished prefix keeps known completed roots and journals the unreported effect as Uncertain. Valid skipped/stopped/mixed/refused and complete progress/final reports preserve their distinct outcomes. Report consistency does not authenticate the user-writable exchange or establish a new privilege boundary.

The parser enforces the same 32 MiB input limit. The exchange opens one reader-shared handle, checks its length, allocates at most that limit and reads exactly from that handle; a concurrent writer cannot grow that open file. The finite limit/exchange controls and code inspection support this mechanism; no deterministic size-race or native hostile IPC capture is claimed.

Working overlay: all 54 broker/consent/platform controls pass, preserving the 23 preceding platform outcomes. Core: 49 pass and one existing explicit mounted-filesystem fixture skip (`FILECAT_TEST_MOUNT_INSIDE` unavailable). The corrected tests are byte-identical to the valid baseline tests. Independent working seal verifies 1128 canonical original blobs, all four working overlays, 314 actual payload references, 32 retained files and 64 wire/journal observations across baseline and correction.

Exact committed qualification exports all 1130 raw Git blobs/modes and verifies the archive and unchanged source tree. The four committed source/test paths equal the passing working overlays. Clean f59343d passes the same 54 platform and 49 Core cases/one existing mount skip, preserving all 23 earlier platform and 50 Core names/outcomes. Independent v4 seals 446 payload references, 46 retained files and 96 raw report observations; no owned stage process remains. Original CI 37673555486 attempt 1 completed: Windows x64/ARM64 and macOS passed; Ubuntu reached its 30-minute job limit before Remote/App tests. The workflow executes Windows platform tests on x64/ARM64; its Unix lanes execute Core/Remote/App and do not run this Windows broker suite.

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
| i17-working-row-v4.json | d9315c4802423ad702c4f0458cbbaba744d0c5710be5f230110b673024c235e2 |
| independent-broker-observation-reader-v2.py | 3bdba805aeb4b3c1fd7078468ac68ec874f71077e034d6e88a5ed2bc41d26e58 |
| owned-process-absence-v4.json | 9d60a83dbea54171485205ae6f6e9ee97f933e7f413491f7a05413e81c39c0e5 |
| seal-broker-clean-v4.py | 591682e6f108303ec292c84ab44d38a7b62d36a2c30ea3f5356b086b03d75648 |
| write-broker-working-record-v4.py | 01fd99260b8bc81167d02c068cc878e0f0ca13ba00fc04ed60bbb94f59c65809 |
| write-broker-working-record-v5.py | 1580d60cfcd824fcd5363936bbd4704565087af6440e6b9080c3ceb073cfd5c5 |
| clean-v2/command.json | 349aa33e5c09c1b1c85c1827a28975e1439e42c3688182958809f82b93c503ee |
| clean-v2/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v2/core-stdout.txt | cde21aa96e2e7bd84e6e3f5e20bea5ecb0af202132b5ca74985219827ebff697 |
| clean-v2/platform-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v2/platform-stdout.txt | 37be15d8e4b4dbe8f0cc66f32e7fa24c237b34bae856d38fb1d65cb0691f778c |
| clean-v2/results/core.trx | 2a6c603e8d09f71e791e8892b9ac79f3094dcbde31a9074078d1c84d4ff72038 |
| clean-v2/results/platform.trx | be6e853d297777694919eb905e15f29b82eb175f79050e319f8e6d13d10032eb |
| independent-broker-clean-v4.json | 042ebf946d04b8cba49e837919811b13dda1dd004be63d365ebc9ad4f1da4799 |

The broader I17 limited-account, installed-helper, requester/consent/lifetime and exact-candidate matrix remains open. All 24 final-candidate campaigns, physical-source/USB hold, contract freeze, custody/signing/human GO gates remain. No persistent Mac/VM setup, physical source, release candidate or publication changed.

## Original CI attempt and setup timeout

Original f59343d run 37673555486 attempt 1 retains 19 selected server digests/every ZIP member and 12 available raw TRX inventories. Its two Windows lanes pass all 62 new broker executions, with 64 independently decoded wire/CRC observations; every available preceding case name/outcome is preserved. Ubuntu build/Core passed, then the combined package/keyring/test step timed out. The log has no intermediate progress and does not establish which subcommand stalled. Ubuntu Remote/App and four-platform qualification are unavailable in this run; no rerun replaced it. Later documentation producer 5957b80 has a separate green API status, not attributed to f59343d here. Current 6e9dadf CI 37681607535 also retains three passed platform jobs and an Ubuntu package-download timeout after the [I202 batch](E-I202-directory-comparison-lifetimes.md).

Private `FileCatReleaseEvidence/ci-37673555486-cancelled-assets-attempt1-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| independent-cancelled-broker-ci-v1.json | 88433670a79c1773ea168c50d8273cbf0fa6723dec04d7ae58bb62451336e98a |
| independent-cancelled-broker-ci-audit-v1.json | bc69135b34c69a603c94bcf8b8f0a62bbdf9861f2d64d7a0d8b91663304ae82e |

Private `FileCatReleaseEvidence/br201-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| verify-cancelled-broker-ci-v1.py | 1f568cb252c0e335c586c7343020b8189093f0121e7390e5bd152b115ebdd202 |
| ci-cancelled-capture-v1/jobs-stdout | 9dfe393697c1b4e3c2607f91f50a9516b92ab8a0d723501734b05f9ff415e5c6 |
| ci-cancelled-capture-v1/annotations-stdout | 93bf01c07e1677c0a85db3bbf4fbdb3124fa5991dad6ee8e54113e3968047a0a |
| ci-cancelled-capture-v1/ubuntu-log-stdout | 87094f29bf221f7ffc9d0dcfda9981930c1c930abf43dc873ceb143ddedf6166 |

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Selected receipt or reader | SHA-256 |
|---|---|
| collect-i201-cancelled-ci-v3.py | f9bb25e9099a64815929c6bde6064cf7eff1e7b6aabfc47670cab040c3e9eb3d |
