# E-I281 — preserve compared targets when copying file links

2026-10-09. Preliminary I06/V02/V13/V23 evidence at original **cb52c13ffd062bc59fefbac17313bdbdce319dc7**, plus the declared fixture and production overlay. I280's ordinary-file guard does not reach literal file-link publication. A link may therefore replace a target changed while the link was materialized or a failed publication waits for Retry. If the target disappears just after the initial check, link creation can also recreate it directly.

The correction carries the compared target into CopyLink and repeats the existing ordinary-file metadata/type check before each PublishLink attempt, including retries. Compared link copies always stage, even if a later stat finds the target absent. A mismatch removes only the owned staged link, journals canceled-before-change and skips the item; it cannot delete a moved source. Ordinary un-compared link behavior is retained.

**160 actual symbolic-link controls** execute TreeCompare, SyncPlanner.BuildRequests, JobManager and PortableFileOperations.TryCopyLink/Move against owned paths. Update/Mirror, both directions, root/nested paths and four timing boundaries cover unchanged/length/time/removal outcomes; link/directory substitutions are added at materialization and retry boundaries. These are actual file links to an owned absolute referent, not virtualized link operations. Account capability is explicit: unavailable symbolic-link creation is a skip, never an invented pass.

Original results are **104 failures/56 passes**. Eighty-eight changed targets are replaced or recreated as links; sixteen directory targets retain their contents but lack the comparison refusal and take the error/question route. The final correction passes **all 160 controls**, preserving changed targets, independent referents, source links, directory contents and sentinels, without another conflict question. Healthy copies still publish a link.

The unchanged compiled control payload passes full Core: **3117 passes/64 exact skips**. All **3021 preceding logical outcome/message multiplicities** remain, with one explicitly verified PE-inspector assembly-path display-label adaptation. **300 headless App cases** pass: all 287 preceding edit cases retain outcomes/messages and thirteen comparison/sync cases match the original hosted Windows outcomes. One existing headless sync case registers the Windows adapter on Windows; this is component coverage, not a native desktop qualification. No new full Remote or full App replay is claimed.

Both exports contain **1330 canonical original Git blobs**, plus the identical fixture and only the declared final production overlay. Full commands, source ZIPs, payloads and every original failure remain. All **480 recorded fixture/source/target/referent/other path sets are absent**. 11 owned temporary files are archived/rechecked, 11 removed and 0 exact locks retained. Root absence in original/final/App/Core order is true, true, true, true.

The executed v1 reader exits at line 65 because it requires injected link/directory creation timestamps to match between separate runs. A first v2 preparer then stops at line 22 because it allows only directory rows; independent inspection finds sixteen injected-link and sixteen directory rows, differing only in During/After timestamps. Both refusals, all 32 raw pairs and the diagnostic remain; no product test or CI is rerun. Versioned reader v2 compares all private/full-Core semantic observations, adapting owned paths/staged names, a healthy newly created link's actual timestamp/link-text length, and injected link/directory creation timestamps (plus platform-dependent injected link-text length). Every actual injected target must first equal its own During snapshot exactly. Raw values remain; link identity/type, referent bytes, calls, questions, warnings and cleanup remain required. The other 128 observations already match under the original reader.

This correction does not establish atomic filesystem compare-and-replace, full-byte/same-size-reverted identity, parent aliases or post-check race protection. I281 exact committed and original hosted checks pass as detailed below. I280 original CI retains its qualified native time-change skip. I282 separately addresses file Follow link; directory links/junctions and broader native/provider/human/candidate scope remain; no cause is assigned to the native skip. Twenty broader unresolved entries, 24 final-candidate campaigns, physical-source HOLD and all owner/GO gates remain. No workstation UI, VM/Mac, device, account/policy or publication occurs.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain full inputs, failures, payloads, predecessor comparisons and cleanup inventories.

| File | SHA256 |
|---|---|
| `sync-link281-v1/preparation-v1.json` | `1d42adff5c0eff21bdca7fa631383e50a3b421f58490e7668af7842cb4968ce2` |
| `sync-link281-v1/regression-preparation-v1.json` | `17b9c46f620fae38ae21b7a4752e9bb614d9e44eb242033ead5cec336ec1ed51` |
| `sync-link281-v1/original-Executors.cs` | `041ae73bfa2df91a1488534f5caa3b470d3607a9cfb762531b6d6db780b2a28f` |
| `sync-link281-v1/Executors.cs` | `a2b7ef79afa59d7e82c6081d943322c64120f6551d8c045f468acdac42de082e` |
| `sync-link281-v1/SyncLinkPublicationTests.cs` | `2ee6c1c8f975f65c43b0ff295944e85f1287b23ecdeec509c0faca9c8ab70ee9` |
| `sync-link281-v1/run-controls-v1.py` | `964410cf21c0470cea3f47dd6856631ecf8e5ba4178ea125503b8a5293de5d24` |
| `sync-link281-v1/run-regressions-v1.py` | `ad2724d0a65004ec59c60dc42ca8a392badb88ed737ddc52c9936546418f73ef` |
| `sync-link281-v1/seal-controls-v1.py` | `7c5b3472a538e854bc15d77e755229a9670d8d95e03515481dbb96f847a4106e` |
| `sync-link281-v1/seal-controls-v2.py` | `8ac65308bbec35b65472eecf1ddc64d4bc94c16d5916342a813b6dbf38590e76` |
| `sync-link281-v1/reader-refusal-preparation-v3.json` | `b9641e7ea720670e65afa126b57571872e4d0f6279f6f5b9eae6ca7c030f5220` |
| `sync-link281-v1/independent-sync-link-final-v1.json` | `d0c87a53624e0a7a957f33887949206ad3bfbbeb12e2a9bbe8775107d8e6b9a0` |
| `E:/FileCat/artifacts/release-evidence/sync-link281-v1/original/command.json` | `a62dc1ecdaf1e20932efe7f0d63a8eff327b67d8e6a48409ef8b826f2a76e6a2` |
| `E:/FileCat/artifacts/release-evidence/sync-link281-v1/fixed/command.json` | `be8a9c6008621936b0cfd2b18ac42ab07d4c50bcc6032c0549fb838d3ad1be91` |
| `E:/FileCat/artifacts/release-evidence/sync-link281-v1/fixed/app-regression-v1/command.json` | `8f9b5d0b9b2d98daf825ef080e37823c94f3916d77d16fed64457786b00bc57d` |
| `E:/FileCat/artifacts/release-evidence/sync-link281-v1/fixed/core-regression-v1/command.json` | `8d7ef939a69589630e1ac0952cb1669bdd6545f6a640b4440847c5ce46d70dbe` |

## Exact committed and hosted follow-ups

Canonical **b3edd919/1332 Git blobs/no overlays** repeats all **160 controls successfully**, requiring every private semantic guard. All recorded fixture/source/target/referent/other paths are absent. The first exact receipt retains three archived files/one removed/two compiler locks and its retained root. A later hash-checked follow-up removes both naturally released files and the root; no process is terminated and no original receipt is rewritten. [Original four-lane CI](E-CI-sync-link-publication.md) subsequently verifies 640 new passes/28,612 actual records, explicitly preserving the native I280 skip-to-pass transition and all other predecessor outcomes.

[I280 original CI](E-CI-sync-target-publication.md) is independently audited with its native time-change skip qualified. [I282](E-I282-follow-link-publication.md) addresses the separately unqualified file Follow link fallback. Directory links, atomic/full-byte/post-check/native/human/candidate scope remains.


## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute; nested receipts retain source, raw commands, payloads, failures and cleanup.

| File | SHA256 |
|---|---|
| `sync-link281-v1/committed-source-comparison-v1.json` | `6d6a07d1ee1d50d1aab411b61ec902ec4efa699b40998bb6d71067a1b32202bb` |
| `sync-link281-v1/exact-preparation-v1.json` | `7bdc3899e587a11d23764a618b080ed218682261b6df3ba2ac92ad774cc72182` |
| `sync-link281-v1/run-exact-v1.py` | `2f5609f3a4d28436f6b13c7888c9ed93dd60997bca6252a1dc17466bd9a6aa98` |
| `sync-link281-v1/seal-exact-v1.py` | `1f80a83682eeff18357a50da5672c5150a65949bfc99407c6f152cd0adfc28d2` |
| `sync-link281-v1/independent-exact-final-v1.json` | `53207182dc3054b5192b46ed9017de12d8165da6663a73656287fe0ea6cf4d80` |
| `sync-link281-v1/owned-restoration-followup-v1.json` | `4c2cef836b13682a6450e1ed57b4ce08389d76a36f1e41ca6b8163a93ddab3a3` |
| `sync-link281-v1/main-push-v1.json` | `b34ff0aa0969e151c3de959c50967d3842f684bf0ed2e5b98c0b754701987ced` |
| `E:/FileCat/artifacts/release-evidence/sync-link281-v1/committed/command.json` | `86bbceca5d98bc42df0fd58c82b97bf073f7a38f4126ec2d24dca3e9f62b39bb` |
