# E-I304 — closed report-window ownership

2026-10-10 CEST. I06/V12/V13 preliminary scope. Baseline **555d6dad8065ef892378a18095b436b5beecac79**, all **1406 canonical raw Git blobs** independently checked in six isolated exports. Final validation declares one product overlay and four regression-fixture overlays; dependencies and locks are unchanged. I305 below concerns a test readiness signal, with no icon product change.

## Finding and correction

A deliberately held closed report window retained its producer delegate and captured owner, materialized text and disposed reader. Three completed-report closure controls fail while three live-window positives pass. With a real producer deliberately ignoring cancellation, three more cases retain its owner/text after it finishes. The unchanged product gives **six ownership failures /thirteen positive passes** across the expanded nineteen-case affected suite.

Closure now cancels work, detaches the view reader, clears materialized text, the completed Reading task and producer delegate, and disposes the prior reader. A running read captures its producer before dispatch; it keeps that producer safe until completion even if its window closes. Existing cancellation and read-disposal barriers remain. No caller data, retention policy or arbitrary resource cap changes.

Nine new report controls use 16 KiB, 128 KiB and 1 MiB synthetic reports: live/closed completed cases plus delayed producers that ignore cancellation. They retain the closed window while weakly observing producer/text/reader owners, with non-inlined observer boundaries and positive live/active checks. All **nineteen report/search/window controls pass**, including old held-read and stale-search barriers. Closed owners retire in **fifteen direct weak-reference observations**; late completion never republishes.

## I305 — icon-worker readiness and retained full-suite failures

The first full App run retains **1522 passes /25 explicit skips /four failures**: three older report-read fixtures dereference a reader after closure, and one asynchronous icon worker has not reached its post-publication idle point when the fixture observes it. The report fixtures now assert the stronger close contract: the prior reader is disposed, the window reader is null, text is empty and late completion cannot republish. Existing source/token, replacement, normal-read and refusal assertions remain.

The icon fixture previously accepted its initial empty-channel wait as proof of being idle after publication. Three deterministic controls start with an empty channel: the original readiness gate signals idle before any publication and fails all three. The corrected fixture only signals idle after a completed publication; all three pass along with all 24 existing icon-retirement cases. Published borrowers and retained-cache positives remain unchanged. No icon product code changes.

The combined **51 affected controls pass**. Its runner initially expected 75 rather than 51 and stopped before the full suite; that runner assertion is retained. A separate continuation runs the same unchanged built payload, without rebuild/restore: **full App 1532 passes /25 explicit skips /zero failures**, including all nine new report and three new idle controls. Every **1545 predecessor outcome/message and all 25 exact skips** remain; only those twelve controls are added. The earlier nineteen-case and full-suite product bytes match the final report product overlay.

## Limits and next work

These controls use actual headless report windows and managed ownership, with synthetic data. They do not establish native-input, process peaks or total memory ceilings. [Exact committed/native follow-up](E-I304-native-report-retirement.md) and [original hosted CI](E-CI-report-retirement.md) are sealed: 51 exact passes, 27 three-platform native passes and all 48 added hosted records pass. The default-headless pixel flags remain explicitly unqualified. Broader I06 qualification remains open. Physical-source I106/I110 HOLD, owner/freeze/candidate decisions and explicit human GO are unchanged.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records retain raw source, commands, payloads, failures, skips and cleanup.

| File | SHA256 |
|---|---|
| `i304-report-retirement-20261010-v1/independent-final-v1.json` | `cffd116fef77a5978e644c7063268fdb12798380afc0e498b7282b84214aff7b` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/seal-v1.py` | `9a49da568cc5679db2df1b3152be5a8b1488647f0ae950162f012627b5e37c2a` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/run-batch-v1.py` | `a771f5bcdc6998e7323e926e83768aa7a3dbf2f3e61f1f6fe12356da7ff3ffa4` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/run-batch-v2.py` | `c30afcfe6aaa1f4327883a499f82b3e634ac64331ddba5e1fed5e9b6e078f47a` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/run-batch-v3.py` | `69c9e3346223b45298aa2483690beb363678b81a102d3b4b278fe58f9bdf7167` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/run-batch-v4.py` | `84a4eaa2c7bc89125033a4ab8f52f265a4300c5ca620b5755c4a3369dbc8d801` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/run-full-v1.py` | `422731ed6c0f26966c0ee79b905faa376b5f5077b17b4f13882588f92aaa406e` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/original-v1/inputs.json` | `9efb00fda95802468da4a344957f31cdece5c80588c7946653de168256cb367f` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/original-v1/controls/command.json` | `f726fca49348d2e85eba058bbae4f4181699210d08171001a6dd72c298da0319` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/original-v3/inputs.json` | `8504567b76d0458bb1c7ae9746920465eb7914ca5d85cbc9e971f7555c39e1ac` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/original-v3/controls/command.json` | `b37691d0f4f012760c64cd7650cc1e93bb142a487cd572211f35c61f26ae23d4` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/fixed-v2/inputs.json` | `a3142814162336d94b873fc8516d7b187f373a40244e3b9a9a2ecd892da392f8` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/fixed-v2/controls/command.json` | `214e190402dceb51a48d74bfd08090f4058790a4fa972d72aa9e03e7a202d937` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/fixed-v3/inputs.json` | `d08d4cd4ceaed5bd2bc928d917272f3d53148bfced07dffe95f709b1007b6059` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/fixed-v3/controls/command.json` | `b0e3c625693226c81ebb71107853cc3923d9a03087df2abec3e90ce6d6e2dc3e` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/icon-original-v1/inputs.json` | `e1593448a8273ac5d00c767145c642a2e5e13ec0d5804cabcbc0e263e9c6d4b8` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/icon-original-v1/controls/command.json` | `3662c32efee068d2f85672f6ce34c6e300d91b5242ac47b20b2c6fe0ce283ba5` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/fixed-v4/inputs.json` | `dbcab90369cf8f69d41b22d652097944761f79df884df1ca8e775255806f892b` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/fixed-v4/controls/command.json` | `4f31a40facdf89313ef2e0907d43136abd6a5f8bc34e638eeb2a968ead97e4a8` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/fixed-v2/full-app/command.json` | `21c40ba4d4bb2c36ecb112f46f0289aca93b6bc8cb611eed013377ade120e34e` |
| `E:/FileCat/artifacts/release-evidence/i304-report-retirement-20261010-v1/fixed-v4/full-app/command.json` | `2fc19a2d073fc0ca6e55d94f499b6ad7fe9912a68f3d05951550ab62c3a7797e` |

## I305 — subsequent asynchronous retirement checkpoint

The I307 test-only working batch retains a full-App failure in the asynchronous clear/no-borrower/64-pixel icon control: its first collection reports the image alive, while the second reports it gone, with an empty cache/queue and a live idle worker. The notification can race publishing-stack unwind; that specific root is not measured. It is a transient collection-check failure, not evidence of an enduring product owner. The original source, full TRX and exact observation are preserved.

The fixture now gives cleared/unborrowed images a bounded ten-second asynchronous collection opportunity instead of treating the empty-read signal as complete GC quiescence. Three new actual-cache borrower controls hold usable images through the bounded probe, then explicitly release them and require collection. All thirty icon controls and the full App suite pass in the final combined batch. Default-headless pixels remain unqualified; published bitmap disposal and icon product code are unchanged. Exact committed/hosted successor qualification remains pending; earlier I305 exact/native/hosted producer records keep their original scope. See [the combined immutable controls](E-I307-git-fixture-command-lifetime.md).

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records retain exact source, commands, payloads, original failures/skips, independent observations and restoration.

| File | SHA256 |
|---|---|
| `i307-git-fixture-20261010-v1/independent-final-v1.json` | `3b20f63ed7bd602150287b12ba35fbeb29e9ac04e7ff082ebf69d7a6e6df0bfe` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/fixed-v2/full-app/command.json` | `6e6b1b48f37cff1472dc052bdbf49bbff9fe84ea9ae3ec32afa508971d572383` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/fixed-v3/affected/command.json` | `2cc51776ff8ec0097e0d760c2db7d5dff1d687b37e88c943880db7a3249049cf` |
