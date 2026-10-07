# E-I174 — folder-analysis updates retain current ownership

2026-10-07. Discovered against canonical product source cd6ba2e567807a527626d268bc2d746bc0eab80e, unchanged in the subsequent I173 documentation commits. Medium status/resource-demand defect; must fix under I06/V12. Remediated preliminarily at 2503ab0623fd05a98fd5781ebe7ccb4623adf06d. Broader provider, progress, fault, native and candidate scope remains open.

## Failure and correction

Starting a new Analyze folder operation cancels its predecessor. The predecessor's canceled task then clears AnalysisStatus and replaces the banner without checking that it still owns the tab's analysis. The replacement remains running. CancelAnalysis requires a non-null AnalysisStatus, so this stale update also removes the condition needed for Escape cancellation.

The correction checks current source identity before applying progress, successful completion/sorting or cancellation status. It preserves the existing folder/tab lifetime checks and source/provider behavior. Source disposal, all late-progress interleavings and other worker lifetimes are wider I06 scope.

## Controlled validation and provenance

Three durable headless controls use two owned files and the real AnalyzeAsync/MetadataService/scheduler paths. The original single-analysis completion control passes. Both replacement controls fail after proving that the old task ended successfully, the new task is pending and file bytes are unchanged: AnalysisStatus is null and the old cancellation banner replaces the current state. The held metadata producer is deliberate instrumentation, bounded and released during cleanup; no unavailable device or native metadata latency is simulated as an observed system fact.

Working and fresh committed locked builds each pass all six affected cases without skips. Both replacement controls retain the current status, then either complete normally or successfully cancel through CancelAnalysis. The single-analysis positive and the existing 20,000-file Escape/navigation/tab-closure controls also pass. Baseline assertions establish the stale status; the original cancellation consequence follows from the source guard, while the corrected controls invoke cancellation explicitly.

The independent seal verifies 25 retained local verification files, 423 actual test payload files, the original 1,070 raw-source overlays and all 1,072 committed Git blobs/modes with their archive. Working and committed module/test bytes are identical. Clean actual FileCat.dll SHA-256 fa80841eebac4d247f5084f6aa8e9a5be38a6c2eb1bd6638134e9d2a68dd104e. Owned files remain unchanged and fixture/state folders clean up. No persistent machine setting, Mac/VM setup, physical source, native desktop input, contract, candidate or publication changes occur.

Original push CI [37559419195](https://github.com/benny-cz/FileCat/actions/runs/37559419195), attempt 1 at 2503ab0, is pending at this local seal. Official API preflight separately retains two original push-run responses for the same source: this run at 01:54:45 UTC and [37559428483](https://github.com/benny-cz/FileCat/actions/runs/37559428483) eight seconds later. The first run is selected for collection; no duplicate-event cause or replay is inferred. These metadata responses are separate from the 25 component-verification files.

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
