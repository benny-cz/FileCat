# Original-push I362 and I363 CI

Both original GitHub Actions runs completed successfully in all four required lanes: Windows x64, Windows ARM64, Ubuntu and macOS. These are source-specific preliminary CI results, not release-candidate or stable publication approval.

| Source | Original run, attempt | Results | New controls | Reconciled artifacts |
|---|---|---|---|---|
| `a8f7159ecba9e7fc72ce478e482e118ff6a48029` (I362) | `38168382231`, attempt 1 | 32,851 passed / 1,077 exact skips / zero failures (33,928 total) | 112 passed | 27 digest-checked archives, 14 actual TRX inventories, 1,639 raw Git blobs, 92 locked restore graphs and four clean build receipts |
| `aeaea5cbd38c4dc22d853993ba733b6cb8578abf` (I363) | `38169832143`, attempt 1 | 32,859 passed / 1,085 exact skips / zero failures (33,944 total) | Eight Unix passes / eight Windows-only skips | 27 digest-checked archives, 14 actual TRX inventories, 1,642 raw Git blobs, 92 locked restore graphs and four clean build receipts |

Every 33,816 predecessor outcome/message/skip is retained at I362; all 33,928 I362 records remain at I363. The original historical adverse runs retain their original failures. I363's Windows skips are explicit Unix-only cases, not absent inventories. The I362 collector initially lacked an available gzip reader and stopped before output; the repaired version retains that reader failure. The I363 collector's first independent reader lacked a Python `re` import and stopped before a master proof; the corrected proof retains the first reader. Neither failure changes original Actions outcomes.

The exact I363 committed source has a separate no-overlay local/native seal: eight native root-exit controls pass, 214 native affected cases pass with six exact skips, 100 host affected cases pass with ten skips, 28 full pixel observations verify, and 742 distinct native files plus one host root/two files are checked or restored. The I363 product correction still leaves the owned test child alive until fixture release. Worker containment, wider I08/I06/I106, physical-source HOLD and candidate qualification remain open.

## Selected immutable receipts

Paths are relative to the private `FileCatReleaseEvidence` root. Each proof pins original job archives, source, commands, actual native outputs, skip messages, reader errors and cleanup as applicable.

| File | SHA-256 |
|---|---|
| `i362-ci-20261011-v1/independent-ci-final-v2.json` | `199efb6813165f8b1093803e6f5838bfa651542b35fc0f92f0bd8f9548ac658a` |
| `i363-exact-qualification-20261011-v1/independent-final-v1.json` | `dc486d091011cb11e6ffb07910f00741fdc1a44b9913db0aa12b9c49dae224be` |
| `i363-ci-20261011-v1/independent-ci-final-v1.json` | `1854f8c533ba7892026bb269add0ec562b7597bb0144bcf2e6f9683f58287346` |
