# E-I194 — coalesced comparison revision checks and unavailable-state status

Recorded 2026-10-07. **Preliminary remediation; I06 and wider native/device/candidate scope remain Open.** Original producer c073192d26b063e0086e6740b9ad1010ab4175c1; correction d51182ca038aeb9ac1b9ce95723fb1e1436527e8; parent/discovery 7531c264690988c88bdd3b06dd80b7bc036b72ea.

Each activation starts another comparison-source revision check while the earlier call is still held. Six controls use actual FileContentSource/ViewSource/CompareWindow on two identical owned 31-byte files: keep the window live, close it or compare again, and release metadata normally or with IOException. One initial call plus 25 explicit checks produce 26 simultaneously held calls in five original controls, and 27 in one control that also receives activation. Each source remains alive safely, but demand grows with repeated activation. The live error also wrongly labels unchanged bytes as a confirmed file change because an unavailable revision is represented by null.

The correction admits one active revision check per comparison window and clears the guard in finally. Existing close/reopen source-identity and deferred-disposal guards remain. A previously available revision becoming unavailable receives “The files' current state could not be checked: what is shown is the earlier comparison”; confirmed changed revisions keep their existing message and F5 behavior.

| Control, normal and failed metadata | Original | Final working and committed source |
|---|---|---|
| Live window, 25 repeated checks during hold | 26 active calls; IOException falsely says the file changed | One active call; normal input stays unwarned, unavailable metadata gets truthful warning |
| Close during hold | 26 or 27 calls; deferred source disposal remains safe | One active call; single deferred disposal, no late banner |
| Compare again during hold | 26 calls; old completions cannot change replacement state | One active old call; replacement comparison stays intact, old sources dispose once |

All six original concurrency controls fail. The intermediate guard-only build passes 51 cases but its raw live-error observation still contains the wrong changed-file label; this partial result is retained. Strengthened final working and clean committed-source runs each pass all 51 affected cases without skips: six additions and every 45 preceding selected name/outcome, including the actual changed-file/F5 control, comparison close/reopen/search/conflict lifetimes, viewer polling and picture/Info revision warnings. The new controls check unavailable-state wording directly.

Two evidence-reader mistakes remain: the first parses two JSON lines as one; the second incorrectly assumes exactly 26 calls in every original case. Fresh read-only interpretation retains the six original raw results and all source/build receipts; no original test/build/CI rerun substitutes for them. The independent seal verifies 1,113 original and 1,115 clean Git blobs/modes/archive members, exactly the comparison window/new test changes, four actual 141-file payloads (564 references), forty-three retained files and 24 raw observations. It reconstructs input hashes/length, held/total calls, UI availability, off-UI metadata, close/reopen ownership, single deferred disposal, unchanged bytes, banner/summary and actual pool-minimum restoration.

The fixture temporarily reserves forty minimum pool workers so all original held calls can start without starving its controller. Every case restores both original worker and completion-port minima and removes its owned temporary root. This is bounded headless resource-demand evidence, not native latency/reference performance. No owned test executable remains at sealing.

Original [push CI 37625381893](https://github.com/benny-cz/FileCat/actions/runs/37625381893) attempt 1 at d51182c is pending in the latest exact-head capture; producer-reference policy passes. Prior original four-platform CI remains sealed at c073192 with 599 App cases per lane.

The correction does not qualify real unresponsive network/removable sources, shared scheduling across all comparison windows, byte-view page/worker bounds, initial/reopen metadata admission, same-size/time-restored undetectable changes, native GUI frames/input, reference/human/assistive-technology tests or an installed candidate. A held earlier poll suppresses activation checks until it returns; reopening still captures the replacement sources' revisions during comparison. Earlier source/native/adverse records retain their exact producers. No persistent guest/host setting, physical-source/USB, frozen contract, candidate, GO or stable publication changes.

Private `FileCatReleaseEvidence/cr194-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | abb643484a12b8eaa6951d2192cbd21b1820a31409a2d221dbfa26892b40f02e |
| baseline-v1/results/display.trx | 1c247d64a7438e729fa8696cadf6cf66f4d752c732cb48b78f8dfeab54845004 |
| working-v4/command.json | 68226bf157a2dd5bbf53f473e52dd0e3245b94d50ea8e88852bd6e3772a721ea |
| working-v4/results/display.trx | f874fe64763bb6661f98712c4c6ee7b9290b30cc1092f41f1e4e0398e6cc5c89 |
| working-v5/command.json | 11258ab8e28e56270590e83b968aa17b8fbd09d947fcd36eeb7683db13a2f9b0 |
| working-v5/results/display.trx | 9effdc406dae62e9e3dadbf91ec43d5f708615f36a9f99e75237a1ce96e65066 |
| clean-v6/command.json | 29f5511b36c4d256d05c593b5962fa0d0f6fb44ee0cdd3c0ada1217fc87df930 |
| clean-v6/results/clean.trx | 611aa76b800f7e4078ed02127583c01f16d3ea93b22fca2009d153e9df181b03 |
| CompareRevisionPollingTests-v1.cs | 073881fd683a282caf2ff2330bf26769bc232e6cc753623ac816e48755a2a307 |
| CompareRevisionPollingTests-v5.cs | 5c3df9db5a0ca9909561afa0188e1bdfa0f9340ab762ee64d233a548ffcaf085 |
| baseline-reader-guard-v3.json | 8f996e9e60c7760756779c6ba1f34eaba1feea566eaa4d8be85246772561b18b |
| seal-comparison-polling-v8.py | a8056b09ac6c436abf0e0f2e43307d4f340bf1c1a720642a296612c754d20cf8 |
| independent-comparison-polling-v8.json | da694032e4c38748f28e1058874f41c827969b7614ec0d2ceff1201f250fa131 |
| owned-process-absence-v8.json | 3b705fc6c1e1a07fad4cf600dc5d0e5cb7444867470d281e92484a3056dcb63b |
