# E-I192 — changed-source status for cached structure reports

Recorded 2026-10-07. **Preliminary remediation; I06 and wider revision/native/candidate qualification remain Open.** Original producer dae3070c3608eb109e2c5a3eb1ef7c89268a63db; correction 4734e2e3e4631834ed05494be0911b0ac39b8446; parent/documentation discovery 69de171a70977f6b410d0704bf2b3f6305692ed5.

Info mode deliberately computes its structure report once. After PagedReader detects a source revision, the original viewer claims “showing its current content” while its cached report still describes the earlier file. Returning from Hex or recomputing status then removes the change indication. Actual shared FileContentSource, PagedReader, ViewerWindow and production static inspector reproduce this: an owned 12 × 8 teal PNG becomes a 16 × 10 orange PNG, current reader bytes change, and the existing report still states 12 × 8.

The correction records the detected-source generation when inspection starts and keeps “The file changed; reopen the viewer to refresh this structure report” whenever the report generation differs. It survives mode and status updates. First inspection after refresh uses the current generation without a false stale-report warning. Inspection remains computed once; the preceding cached-picture correction stays intact.

| Control | Original dae3070 | Corrected working and clean 4734e2e |
|---|---|---|
| Earlier report visible while source changes | Fails: current-content claim for the old report; status update loses the indication. | Pass: persistent reopen warning. |
| Earlier report hidden in Hex, then shown after source changes | Fails: old report returns without a warning. | Pass: byte view keeps its current-content indication; old report receives the warning. |
| First inspection requested after source change | Pass: current 16 × 10 report without stale warning. | Pass; unchanged. |
| Three corresponding unchanged-file controls | All pass: 12 × 8 report without stale warning. | All pass; unchanged. |

The original inventory has two failures/four positives. Working and fresh committed-source runs each pass all 64 affected cases without skips: six additions and every 58 preceding picture/Info/direct-content name/outcome, including close during an active inspection. The new cases start no picture worker. The independent seal verifies all 1,109 original and 1,111 clean Git blobs/modes/archive members, exactly the ViewerWindow/new test changes in the correction commit, all three 141-file actual payloads (423 references), forty retained local files and eighteen supported raw observations. It independently decodes both full PNGs and their chunk CRCs/pixels, reconstructs source hashes, lengths and modification times, and checks report dimensions and persistent status. Overlay text alone allows Windows CRLF/Git LF; canonical Git/archive bytes remain strict. No owned test executable remains at sealing.

The first CI status reader reused the preceding hardcoded run number. Its exact-source guard rejects that capture before accepting any result; raw API receipts and failed source remain. Fresh head-SHA discovery finds original [push CI 37616659442](https://github.com/benny-cz/FileCat/actions/runs/37616659442) attempt 1 at 4734e2e. It is pending, with all four test lanes running and the main producer-reference policy green in the latest retained capture. This preparation failure is not a product failure and no test/build/CI rerun replaces it. The preceding sealed CI remains 37613028189 at dae3070, with 585 App cases per lane.

These are actual owned-file/inspector executions with headless component observations on the elevated Windows Insider host. They do not qualify native frames/interaction/DPI, changes during inspection, same-size/time-restored undetectable mutations, descriptor/path replacement, cached Page rendering, complete source/checksum revisions, reference/human/assistive-technology workloads or the installed candidate. Fixtures close and remove their owned temporary roots. No guest, persistent setting, physical-source/USB, contract, candidate, GO or publication change occurs. Prior native/component records retain their exact producer rather than being promoted to this rebuilt source. Next autonomous work is original CI completion and remaining Page/source/checksum/worker boundaries.

Private `FileCatReleaseEvidence/ir192-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 9f953f57358a3f2f5639c212a91d5a445bff0fd2f5d56bf84f0154e778261087 |
| baseline-v1/results/display.trx | e809cedbaa72eb2ff462d57a1d3991ad1f17a65fc4fe9ce8c36249ed0b66248f |
| working-v2/command.json | 4b0084eda018907ad51e8d97b921859339dd512e89361cbfbd2f4ecaa09b41b4 |
| working-v2/results/display.trx | 53db49dbcdd3859f314e455257ed1eafda6ee9391e243351d12a0d8bf0b1fc87 |
| clean-v3/command.json | 731f3f2e88e7665f30cf75a51c9bd0b1a8bd6d121675165cdb4aeb8f8374922f |
| clean-v3/results/clean.trx | 72bac8b33c818007027aef3b798ec97f95a2f58328c82a65c6e63505893b19bb |
| InfoRevisionStatusTests-v1.cs | 0fa800321ff0972ef281ef31a7c834b6936772ff200cb9b0f0cb091b011db91f |
| ci-status-identity-guard-v5.json | b5ef78944548a0a9681a859eb8fe397b05b01e8c033a879735321246dcd476a7 |
| seal-info-revision-v6.py | c6a6394908ab4ee13410353a85eb7c0db5ea89f8e5f1100daaadeecf48aa5cf6 |
| independent-info-revision-v6.json | 612b868144548e95768a5e7bc94e0b6cbb63d3f6a7db5f7cc0f5c43a1948c419 |
