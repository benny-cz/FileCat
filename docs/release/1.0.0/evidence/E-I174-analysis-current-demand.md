# E-I174 — folder-analysis updates retain current ownership

2026-10-07. Discovered against canonical product source cd6ba2e567807a527626d268bc2d746bc0eab80e, unchanged in the subsequent I173 documentation commits. Medium status/resource-demand defect; must fix under I06/V12. Remediated preliminarily at 2503ab0623fd05a98fd5781ebe7ccb4623adf06d. Broader provider, progress, fault, native and candidate scope remains open.

## Failure and correction

Starting a new Analyze folder operation cancels its predecessor. The predecessor's canceled task then clears AnalysisStatus and replaces the banner without checking that it still owns the tab's analysis. The replacement remains running. CancelAnalysis requires a non-null AnalysisStatus, so this stale update also removes the condition needed for Escape cancellation.

The correction checks current source identity before applying progress, successful completion/sorting or cancellation status. It preserves the existing folder/tab lifetime checks and source/provider behavior. Source disposal, all late-progress interleavings and other worker lifetimes are wider I06 scope.

## Controlled validation and provenance

Three durable headless controls use two owned files and the real AnalyzeAsync/MetadataService/scheduler paths. The original single-analysis completion control passes. Both replacement controls fail after proving that the old task ended successfully, the new task is pending and file bytes are unchanged: AnalysisStatus is null and the old cancellation banner replaces the current state. The held metadata producer is deliberate instrumentation, bounded and released during cleanup; no unavailable device or native metadata latency is simulated as an observed system fact.

Working and fresh committed locked builds each pass all six affected cases without skips. Both replacement controls retain the current status, then either complete normally or successfully cancel through CancelAnalysis. The single-analysis positive and the existing 20,000-file Escape/navigation/tab-closure controls also pass. Baseline assertions establish the stale status; the original cancellation consequence follows from the source guard, while the corrected controls invoke cancellation explicitly.

The independent seal verifies 25 retained local verification files, 423 actual test payload files, the original 1,070 raw-source overlays and all 1,072 committed Git blobs/modes with their archive. Working and committed module/test bytes are identical. Clean actual FileCat.dll SHA-256 fa80841eebac4d247f5084f6aa8e9a5be38a6c2eb1bd6638134e9d2a68dd104e. Owned files remain unchanged and fixture/state folders clean up. No persistent machine setting, Mac/VM setup, physical source, native desktop input, contract, candidate or publication changes occur.

Original push CI [37559419195](https://github.com/benny-cz/FileCat/actions/runs/37559419195), attempt 1 at 2503ab0, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server artifact digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 485-case App inventory equals the previous 482 names plus exactly these three additions. All twelve new executions pass with distinct IDs and no skips. Both replacement observations retain the current status with old work ended, new work pending and unchanged owned files. Core inventories retain 884 Windows/879 Unix cases, and I163–I173 subsets reconcile.

Full App outcomes: app-test-results-macos-26: 406 Passed/79 NotExecuted; app-test-results-ubuntu-24.04: 404 Passed/81 NotExecuted; test-results-windows: 468 Passed/17 NotExecuted; test-results-windows-arm64: 468 Passed/17 NotExecuted. Explicit overall skips remain unqualified. ARM64 package version startup, headless drawing and installer compilation pass. Tagged/manual package and draft jobs skip; no selected shipping artifact, installed native desktop, physical ARM64, participant or candidate qualification is implied. Official API preflight separately retains two original push-run responses for the same source: this run at 01:54:45 UTC and [37559428483](https://github.com/benny-cz/FileCat/actions/runs/37559428483) eight seconds later. The first run is selected for collection; no duplicate-event cause or replay is inferred. These metadata responses are separate from the 25 component-verification files.

Private `FileCatReleaseEvidence/analysis-current-demand-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | cfbfaf03429bbdb03cd157d73d1ac8cd8741eeb8c9952ddcaedaa99995edca48 |
| baseline-v1/results/baseline.trx | b4142a9294188ce4f99e2574fc2fab93845727ea73e1527b417b9a1b3961e047 |
| working-fixed-v2/command.json | 01645894bed15d1e77f648388df1bac8ef57300670427dfed4a2dcebc928533b |
| working-fixed-v2/results/fixed.trx | aa8da699def6a64104e9a64fe5ca69e29d949e3f6324361d56f0e38d5f06fae2 |
| independent-working-v3.json | d113abb0e36a124167677cc2bdf28a72f9b954cd10563e8cf8fbb5df73ad6afe |
| clean-committed-v5/command.json | ec26b4827f67ae0ee44140984e5c1ff6a2a32f997b8df2f0bbeff865d0345910 |
| clean-committed-v5/results/clean.trx | 48b6e325e555e6ed95285a9194a1c3a721f461bcd53e294c0080aa946d8d2c41 |
| independent-analysis-demand-v7.json | 78e91e99c3aa73ac4a60ac0dd0f616581bbe0ea2a7ba58f1f12170f562d34f0c |


The collector completes its artifact inventory and fifteen seals, then fails on a historical run path changed by a broad count replacement. Its first continuation preflight also rejects a partial-name filter. Both failed controller sources remain. Corrected continuation independently rechecks the nineteen original server digests and fourteen raw inventories, preserves all fifteen prior seal hashes and completes the remaining three seals from the same results. No CI/test failure, rerun or artifact substitution is inferred.

Private `FileCatReleaseEvidence/ci-37559419195-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 8399d1df65b9af9a3e7af38b4a37f4b5b6617e49f05ca215c5c5dadd96534479 |
| independent-fixture-ci-v1.json | 571975217d8c1f4cc57f8770760343d451f7373f6b5ab1b7c10dccba3c5d8bdb |
| independent-producer-policy-ci-v1.json | f8f980feb6ece38ea115053ece0233ba7f1c5307df038a0dcd2d54ac9d8676b2 |
| independent-draft-guard-ci-v1.json | a88c8636478dab0a4701eebfc2930c17a6a25bafb39518a8144750b172adf695 |
| independent-separation-ci-v1.json | a1b2728ab919293dd4ea668efce860a79528fd1b7bc20512d1a93dac0f368398 |
| independent-restore-ci-v1.json | 5b5d3a9294669e79ea0e522a81bc34cd11ac359d8b53b231c18dd2454bfa248c |
| independent-i163-ci-cases-v1.json | 88b7ad3819686fa7e086eb58b14babc2be02e34055b365b52e05e0b741572f48 |
| independent-i164-ci-cases-v1.json | f77dd7066cfb98bee2eb32889639306ce4c6f687b7d7785c157e514b27aef5b3 |
| independent-i165-ci-cases-v1.json | 96e53de02077f356b816e94e04a2342e68433fb105db2c6d117ac9fbf45faa6f |
| independent-i166-ci-cases-v1.json | 96ea8eca3dcd30a0f53057cd9f6fe30bbc35776b72b2a759df93fd19a06483e3 |
| independent-i167-ci-cases-v1.json | 2392ef316a2d0d3ce58b034623aa12fc05f95ffb655bfcdeaddecacff8cf81fc |
| independent-i168-ci-cases-v1.json | 10413e8d3a3ff2633356425f9d2c7edd1c4780e767a194383b6072c88fb7cb15 |
| independent-i169-ci-cases-v1.json | 9c0a749f64359f2f7d54c6d8a2c903002a7f54f198a81030925704ddd7e14d89 |
| independent-i170-ci-cases-v1.json | 29f4a3be10fbe60e07666ce87acc7703357691a62a73422b906782b101ffbee2 |
| independent-i171-ci-cases-v1.json | 22d8ce2a87cea9c263ffe6368f14aecfbc4f68643d9617cc55b3a0d9a092fac6 |
| independent-i172-ci-cases-v1.json | 000f3a4e0ddab9e54c2c0fe0ed430bcb9824ea82356c5980db2477852aa8f36f |
| independent-i173-ci-cases-v1.json | 622f3a7ab81bd175784bece8e3a74719fbfe17f2ce87f900f30d1f93c9d010c8 |
| independent-i174-ci-cases-v1.json | 05ecba05d591deda087d38df8263ed363764f4b86c4d720b4c210429b6e9731a |
| collector-continuation-v3.json | 4b5458936ac06c63a493b54bc6b8417ab3948a02aec06291773d42a16fb7ed41 |
