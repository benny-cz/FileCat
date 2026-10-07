# E-I191 — changed-source status for cached pictures

Recorded 2026-10-07. **Preliminary remediation; I06 and wider native/candidate qualification remain Open.** Original producer 57204597f8839029aa9f8efa397e8fbb460ecc9c; correction dae3070c3608eb109e2c5a3eb1ef7c89268a63db; parent/documentation discovery dbc870aa348d84c813494790e1bfde0e25e920a9.

The viewer deliberately decodes its picture once. When its source changes, PagedReader.Refresh updates the byte views, but the original viewer labels the still-cached picture “showing its current content.” Returning from Hex or changing zoom then erases all change indication. Two original adverse controls reproduce this with an actual shared FileContentSource, PagedReader, ViewerWindow and the production decoder worker: the source changes from a 12 × 8 teal PNG to a 16 × 10 orange PNG, the byte reader returns the new content, and the picture stays 12 × 8.

The correction tracks the reader's detected change generation and the generation when the picture is requested. Picture status keeps “The file changed; reopen the viewer to refresh this picture” whenever those generations differ, including after mode/zoom changes. A picture first requested after the detected revision uses the current generation and does not receive a stale-picture warning. This preserves the existing decode-once behavior and tells users how to refresh it.

| Control | Original 5720459 | Corrected working and clean dae3070 |
|---|---|---|
| Source changes while the earlier picture is visible | Fails: falsely claims current content; zoom erases the indication. | Pass: persistent reopen warning before and after zoom. |
| Earlier picture is hidden in Hex while source changes, then shown again | Fails: byte view updates, but restored picture has no warning. | Pass: byte view retains its current-content indication; old picture gains the persistent warning. |
| Picture first opened after the source changes | Pass: 16 × 10 current picture, no stale warning. | Pass; unchanged. |
| Three corresponding unchanged-file controls | All pass: 12 × 8, no stale warning. | All pass; unchanged. |

Original corrected-checkpoint inventory is two failures/four positives. Working and fresh canonical committed-source runs each pass all 46 picture cases without skips: the six additions plus every preceding forty name/outcome. The independent reader reconstructs both complete source PNGs, all chunk CRCs/pixels and hashes, actual before/after lengths and modification-time evidence, current reader bytes, picture dimensions and all eighteen raw status observations. It verifies all 1,106 original and 1,109 clean Git blobs/modes/archive members, all four 141-file actual payloads (564 references), exactly the ViewerWindow change/new test in the correction commit and all 47 retained local files. Windows private CRLF versus canonical Git LF is checked as the same source text only for the overlays; canonical Git bytes remain verified raw. No owned test executable remains at final sealing.

The first fixture incorrectly waited for a constructor-started timer rather than completed PNG recognition. All six initial cases stop with a null picture checkpoint before status observations; these are retained fixture failures, not six product failures. Fresh v2 waits for actual picture recognition and establishes the two supported adverse results. The CI collector generator then looks for “Locked restore” instead of the existing “Restore” marker and fails before generating a collector or making any request; its failed source/receipt and dependent missing-file lookup are retained. Fresh v8 corrects only that preparation marker. Neither preflight failure is erased by a CI, product or build rerun.

Original [push CI 37613028189](https://github.com/benny-cz/FileCat/actions/runs/37613028189) attempt 1 is pending; its latest captured state has Ubuntu and Mac green while both Windows lanes continue. Earlier full green CI remains at 5720459. Changed/rebuilt artifacts keep their own producer; no prior loaded-module, native raster or candidate evidence is silently promoted.

These controls use actual owned local files and workers with headless window/state observation on the elevated Windows Insider host. They do not qualify native interaction/frames/DPI, same-size/time-restored undetectable mutations, descriptor/path replacement, changes during feeder reads, other cached Info/Page representations, complete source/checksum revision semantics, memory/reference/assistive-technology workloads or the installed candidate. Each fixture closes its viewer and removes its owned temporary root; no guest, persistent settings, physical-source/USB, contract, candidate, GO or publication change occurs. Next autonomous work is original CI completion and the remaining revision/worker/cache boundaries.

Private `FileCatReleaseEvidence/pr191-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | ffa37be6bdf8446e9222e8d0e9357903a5404ae39a304f77e9ea8b6295d3e571 |
| baseline-v1/results/display.trx | 70129c2712953295e2632be9fbcb20d8daa228b5cba7dba5ecaea62c81c8d710 |
| baseline-v2/command.json | b1afa292292a546d7a3154cf88668f80a696b05df936e1d4702343095f2d3329 |
| baseline-v2/results/display.trx | 5870c2fbcdf1c550aac163a55448f6ff90f178e60be9242fd170a5200b80af46 |
| working-v3/command.json | ab7e57e1f04bbb25efb612f4511a816eb76ec83f8d5660c10ad592787144069c |
| working-v3/results/display.trx | 3adc5337ea33d1ab9e096a017379c9e9aae9e694fad7f99985e89745627de850 |
| clean-v4/command.json | b1aeae58d847e87c4a7d580e437bcbd09af1ed40f2d1886f2e62ca8eb62b392d |
| clean-v4/results/clean.trx | f4b3aef18d9828a3305854a1c9ef6d3cf6c18190d6faffb04304d9c1bff68dd2 |
| owned-original.png | e432f276f2c80022bb7b246df5f22994d0f05e05519564b78697cd6f9991f398 |
| owned-replacement.png | 08ad419eb435e1f318a3d2b5d6e920b008aec64e5ef5a3a06c9323830fb16fc9 |
| recognition-checkpoint-failure-v2.json | bc0a3225e18d5ff11d133e3cce7a6ce08e4bde40bc9ee8745fdbf8873d6bf450 |
| collector-generation-marker-failure-v8.json | d495892875bff03acbf4382dd714c3348c69b0ca8bdde5de567a5510a0ef5925 |
| seal-revision-v6.py | 86b4bf2f9dc04872f9b476bc8a1f662568a7bae8e057e723e7ff76359768072a |
| seal-local-final-v9.py | 9ddc7502a8fd6ef878bd5a70fd60fdaa3b1767755ea00127a41c9ebad3ea211e |
| independent-local-final-v9.json | 50234efaa1295cd160f1946cb915c18893a13499ba13e2f87f6dc18d22eb01f7 |
