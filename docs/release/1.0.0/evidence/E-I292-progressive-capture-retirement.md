# E-I292 — retire disposed progressive-member callbacks

2026-10-09. Exact correction base **adc54053de097ef88dd86851bcbc8edb5cfdf7e2**, plus the declared fixture/fix overlays in two complete 1363-blob exports. I06/V07/V12 preliminary scope.

ProgressiveContent closed its native source/spool and returned its archive lease, but its readonly open/closed delegates continued referring to the archive index and captured payloads. A caller holding the disposed wrapper therefore retained that managed object graph even after provider cache release. This is a managed ownership defect; the original native closure controls already pass. No ordinary UI incidence or whole-process peak is inferred.

Disposal now clears the open callback and takes/clears the completion callback under the existing archive gate, then attempts source, spool and callback cleanup once. Live calls retain their owners until the safe boundary. The first cleanup error, repeated disposal and post-disposal read refusal retain their behavior. There is no new memory limit, cache policy or relaxed lease guard.

Eighteen actual ZIP/TAR cases cover 64/4096/16384 empty members plus one **33 MiB + 17-byte** patterned member, before reading, after reading every known byte, and with a live lease after cache release. Reflection observes only the actual cached index; opening/listing/reading/releasing use public provider APIs. Four further controls use owned 32 KiB bytes and source/callback/both/no cleanup faults. Bounded collection checks reachability while disposed wrappers/providers are deliberately held. Before collection, independent Windows sharing already confirms native closure; Unix records that native sharing oracle as unavailable. Roots and complete archive timestamps/hashes retain their per-run identity.

Original **16 failures/six live-lease positives** become **22 passes**. All eighteen original disposed wrappers retain their evicted index; all corrected ones release it. All four corrected callback owners collect after cleanup, including the fault paths. Live leases keep their index and read every known member byte after cache release. Native sources close, original error objects/callback counts remain, and complete input hashes remain unchanged in both runs. The controlled index estimates and weak references are not aggregate managed/native/mapping/frame measurements.

The unchanged compiled payloads pass **full Core 3446/64 exact skips** and **full App 1398/25 exact skips**, actually overlapping in time. All **3488** preceding Core outcome/message records and all **1423** preceding App records/exact skips remain, with one verified PE assembly-path display adaptation. All **358** selected archive/edit/direct-viewer App results, including two exact skips, agree with that preceding full App. Exact committed checks pass below; original hosted and aggregate/reference/native/frame/human/candidate scope remain. I06 stays open; I106/I110 HOLD and human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete source, commands, payloads, raw failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i06-progressive-capture-retirement-20261009-v2/ProgressiveCaptureRetirementTests.cs` | `68ac96c790c76512a87436249132cdae7978d55856b540a75f3b62e24e785807` |
| `i06-progressive-capture-retirement-20261009-v2/ProgressiveContent.cs` | `0d6a2bd053e648c5875f34c3abaabbb56c89a62f455fb547b002dc4c056fd261` |
| `E:/FileCat/artifacts/release-evidence/i06-progressive-capture-retirement-20261009-v1/original/core-controls/command.json` | `b94d1e9959c3b99b3fbdb591946afbdb65ce9158bd342c2902de52843c96241e` |
| `E:/FileCat/artifacts/release-evidence/i06-progressive-capture-retirement-20261009-v2/fixed/core-controls/command.json` | `b6c932a5954051bf94f816f87a5fb037a38305353e3b5ecd22e0a4f9971fcfab` |
| `E:/FileCat/artifacts/release-evidence/i06-progressive-capture-retirement-20261009-v2/fixed/archive-app-controls/command.json` | `ea9a5b221e203b5a6ae7f09ee9ac4fa776e3df43ca3519372aafd1c432172cd6` |
| `E:/FileCat/artifacts/release-evidence/i06-progressive-capture-retirement-20261009-v2/fixed/full-core/command.json` | `4a665b482d0fd131a4800d1068200fda2a402cc6cc47801c778495504911c22a` |
| `E:/FileCat/artifacts/release-evidence/i06-progressive-capture-retirement-20261009-v2/fixed/full-app/command.json` | `d1e91177bb077d1b865a4f9256f2a5dce7645b7153d05a537d7278e374e173b8` |
| `i06-progressive-capture-retirement-20261009-v2/seal-progressive-batch-v1.py` | `3945374ac006ab4c2fe315610e199057868a38c16f6af6689d3527641adb3da2` |
| `i06-progressive-capture-retirement-20261009-v2/independent-progressive-batch-final-v1.json` | `e797d74898363277c5e58dce58123cdd04eeffd4daf20f4a4ed85a6827b2f4f8` |
| `E:/FileCat/artifacts/release-evidence/i06-progressive-capture-retirement-20261009-v2/owned-temporary-files-v1.zip` | `211c26268359ddea55e9c2c4286f7cf1f32ec49e69e85bbd0bb16af52fce5632` |


## Exact committed follow-up

No-overlay **f56f9de148ed65653f815a4f42194afcfcf94b8f** verifies all **1366 canonical Git blobs** and passes all **22 controls**. Every private/exact outcome, message and retirement/live-lease/cleanup-error observation agrees apart from owned roots and archive creation hashes. The reader rechecks every canonical source byte and archives three owned temporary files; one is removed and two compiler locks remain. This is not a full-suite or whole-process/native frame/candidate repeat.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete commands, raw outputs and source identities.

| File | SHA256 |
|---|---|
| `i292-committed-20261009-v1/seal-exact-v1.py` | `e79283406973a1f3363acc2b740fb47375ba45cde7e664945029b0e3def19d84` |
| `i292-committed-20261009-v1/independent-exact-final-v1.json` | `6e3e065ebd260883b2af3c0670816b46b7a7f4a9d87979a760a0b2978dda2e01` |
| `E:/FileCat/artifacts/release-evidence/i292-committed-20261009-v1/committed/core-controls/command.json` | `54bea163e1ea0165c3997768ebd601879c35d73aa915d19a361593117c1ed650` |
| `E:/FileCat/artifacts/release-evidence/i292-committed-20261009-v1/committed/inputs.json` | `33b10c02b8858bab55519d36d6caced288071fd692dfdd2937c560dc1806af33` |


## Original hosted follow-up

[37994261915 attempt 1](E-CI-progressive-capture-retirement.md) at exact f56f9de passes four lanes/30,172 results, including 88 new controls; all preceding outcomes/messages/exact skips remain. Windows sharing and unavailable Unix oracles stay separately qualified. Aggregate/native/frame/reference/human/candidate scope remains.
