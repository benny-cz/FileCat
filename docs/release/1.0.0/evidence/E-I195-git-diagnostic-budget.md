# E-I195 — bounded automatic Git diagnostics

Recorded 2026-10-07. **Preliminary remediation; I16/I06 and wider native/candidate qualification remain unresolved.**
Original product d51182ca038aeb9ac1b9ce95723fb1e1436527e8; discovery 26e207478b1df2953e374fb87b3f24bc56498208; correction introduced in 8aa64cc138c91a52645367a289a4193f8567b4a8. Final validated producer b0da4ff600d5dee39e8224e477521ac4a4240c34 removes only one trailing test blank line.

Automatic badge reads capped Git's status output at four million decoded characters but retained its entire diagnostic stream. Actual installed Git produces 4,468,894 diagnostic bytes from 80,000 invalid-attribute lines in a 480,000-byte owned attributes file, while status stays at 33 bytes and Git exits zero. A committed tracked file is changed to same-length bytes to force real attribute evaluation; the earlier added-file/size-change preflight produced no diagnostics and remains separately retained.

Six integration controls cover direct repository-file badges and parent-folder repository badges, with zero, twenty or 80,000 warning lines. The original product passes four ordinary controls but fails both oversized-diagnostic refusals: it still returns Modified badges after retaining more than the existing output budget. Independent native controls hash/count diagnostics without retaining their text. Automatic reads preserve tracked, attributes, configuration and index hashes; subsequent ordinary reads return Modified badges.

| Warning lines, each badge path | Original | Final working / committed source |
|---|---|---|
| 0 and 20 | Normal Modified badges | Normal Modified badges; diagnostics drained without retaining text |
| 80,000 | Badge returned despite 4.47 MB diagnostics | Optional badge unavailable; follow-up ordinary read recovers |

The correction caps each redirected pipe at four million decoded characters and discards diagnostic text. It observes the first pipe completion/failure, cancels remaining reads on exit, kills an unfinished child on failure and observes both read tasks. This per-pipe budget is not an aggregate native-worker memory limit. The first correction caught the limit in its main path but rethrew InvalidDataException while draining in finally because the cleanup filter omitted that distinct exception type. Both intermediate runtime failures and their original 111-case results remain. The final cleanup explicitly handles that type.

Final working and clean committed-source runs each contain 111 affected Git cases: 110 Passed, one existing packet-capture NotExecuted. All six additions pass without skips, and all preceding 105 names/outcomes are preserved. The full preceding configuration, alternate-store, lazy-fetch, home-environment, worktree, reparse/tree and shared-directory controls remain. The independent seal verifies 1,115 original and 1,117 clean Git blobs/modes/archive members, exact source/test overlays, four actual 141-file payloads (564 references), 149 retained paths and 22 raw native-diagnostic/admission/input-hash/recovery observations. It independently reconstructs every diagnostic byte/hash from the numbered warning lines, including the original failed controls. No owned stage executable or Git process remains at the sealing observation; each control removes its owned temporary root.

Original push CI [37630947723](https://github.com/benny-cz/FileCat/actions/runs/37630947723), attempt 1 at 8aa64cc, and final-producer CI [37631087514](https://github.com/benny-cz/FileCat/actions/runs/37631087514), attempt 1 at b0da4ff, are pending. Neither is rerun or replaced by the local result.

This does not qualify all Git versions, mutable-path swaps, other indirect reads, sustained native resource limits, unavailable network/removable sources, native GUI/input/reference/human tests or a release candidate. No persistent machine setting, physical-source/USB, contract freeze, candidate, GO or publication changes.

Private `FileCatReleaseEvidence/go195-v1`:

| Retained path | SHA-256 |
|---|---|
| native-output-probe-v1.json | 810cb14250e9df28386a4312756d242b5be0b1328148f0c6754277a661ffc37c |
| native-output-probe-v2.json | 9f534452a5660e82c146295b7dd8a3047b5c5d3c092f2555c0b0b90637ba329b |
| native-output-budget-v3.json | afe4c5910b1f9506697aeb03ea7b5151ee1522011b8c929e182c5208ddb486c8 |
| baseline-v4/command.json | 790a8a8590e597399df5f28a50c083109653172d6b6662dca38a95875ae18caf |
| baseline-v4/results/display.trx | 9cd9dfeda5420653247bc515ff3f76fddee184dc18936a564f7397e35571af28 |
| working-v5/command.json | 833bf0b7ad74b18fe5be9e9cb4a63ca2aa167ef76841d7cb0e4f5d91898a64d6 |
| working-v5/results/display.trx | 0d60aebc416775411cf583b0be5e6816926ee92eb40b959d5308b9fb78317b33 |
| working-v6/command.json | 7d00a2136f8823de4d8f60e7151888c39606fa1f4fadff110daae902b3099b9b |
| working-v6/results/display.trx | 2d737710fc48decfac07a92b64a879198bc363f9924477ffd88de2ab66ddd503 |
| clean-v7/command.json | bf82f48be73752bb7a876d5b88bf7044f51d345a87a89b1ef88e9651fd06e4e7 |
| clean-v7/results/clean.trx | c243bbbda4b4b406c02e90ceefd9a0be976b54e5469b3c5acc382bd8c5c63ce8 |
| seal-git-output-v10.py | 5b0d4c1f32aafb83f2a7445c2ac9bdea5468d80b27af1a7479b9b580a59da97c |
| independent-git-output-v10.json | 4d379bd9336c1e88c8652c395d2c6f94aad9365022cdafe830fbe2d554d10355 |
| owned-process-absence-v10.json | aa8abfa0ad3fd1ca42f89ee3d5b254e7eb674ec61750798217d28baf22e9edc7 |
