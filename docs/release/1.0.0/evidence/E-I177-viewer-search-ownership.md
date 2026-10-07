# E-I177 — viewer search completion owns its current demand

2026-10-07. Discovered against complete canonical product fcae7632a4cf13f089243e28065df2b140d613de; intervening documentation commits leave this implementation unchanged. Medium cursor/status and resource-lifetime defect; must fix under I06/V12/V13. Remediated preliminarily at 9f5f135c511b6a2bf5c70a24f11ae1fd2446047b. Wider consumer/worker/frame lifetimes, native interaction and candidate qualification remain open.

## Failure and correction

A complete ViewerWindow starts a search over an owned real file while one source read is held. A replacement search finds fresh at byte 64, with the selection cursor at its inclusive end, byte 68. Releasing the old read lets its prior match move the cursor to byte 131,118 and replace the current status; an absent old pattern instead replaces the current result with a not-found message. Closing the viewer still allows the old match or cancellation to alter its cursor/status. Explicit cancellation also accepts a match returned by the held read. Completed searches retain their cancellation source; invalid hexadecimal input allocates an unused source and leaves it active.

The correction links each valid search to viewer closure, caches its token, checks current ownership and cancellation before applying results, and disposes the source after its task returns. Only the current live search may publish cancellation status; an older completion cannot clear a newer source. Invalid hexadecimal input retires prior demand without allocating an unused source. This does not change the search algorithm or claim that synchronous source calls can be interrupted mid-call.

## Controlled validation and provenance

Eight durable headless complete-viewer controls use owned real files: replacement with/without an old match, closure with/without a match, explicit cancellation after a held read, completed-search cleanup, invalid hexadecimal input and an ordinary live-search positive. Original source records seven failures/one positive pass. Working and fresh locked committed runs each pass all 54 affected viewer/picture/admission/lifetime controls without skips. All six held-read observations retain unchanged file hashes, no source disposal during a read, current ownership and disposed completed cancellation sources after correction. Ordinary content remains searchable.

The independent seal verifies 44 current retained files/eight earlier-root files, 423 primary actual test payload files plus 423 earlier failed-stage payload files, original 1,076 raw-source overlays and all 1,078 clean committed Git blobs/modes plus their archive. Working module/test bytes differ from canonical bytes only by Git CRLF normalization. Clean actual FileCat.dll SHA-256 8052fdfb712633f14094b006c64d14090d52ab68f63c2627cb65cb890689e4bd.

Earlier fixture/controller failures remain separate. The first private path exceeds the native HarfBuzz loader's path limit, so its six failures do not establish the product bug. A shorter fixture initially compares a selected cursor against the match's first byte; the corrected independent oracle uses its inclusive end. Corrected seven-case then expanded eight-case original runs establish the actual defect. Six owned temporary directories left by the loader failure are captured with byte hashes, checked against exact names/parents/creation times and removed. An initial cleanup UTC guard fails before capture/deletion; the fresh controller parses UTC explicitly. An initial seal rejects raw source/test equality; the fresh seal proves CRLF-only normalization. Their original sources and earlier raw test outputs remain retained.

Original push CI [37566601056](https://github.com/benny-cz/FileCat/actions/runs/37566601056), attempt 1 at 9f5f135, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 509-case App inventory equals the prior 501 names plus exactly eight viewer-search additions; all 32 distinct new executions pass without skips, including 24 structured held-read observations. Core retains 884 Windows/879 Unix names; I163–I176 subsets retain exact outcomes and explicit skips. Full App outcomes: macOS 425 Passed/84 NotExecuted; Ubuntu 423 Passed/86 NotExecuted; both Windows lanes 492 Passed/17 NotExecuted. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No selected shipping artifact or candidate is produced. These are component mechanics, not native desktop, human UX, physical-source or installed-candidate qualification. Prior rebuilt-artifact evidence keeps its original identity; current viewer-search behavior requires affected revalidation. No Mac/VM settings, physical-source, contract, candidate or publication changes occur.

Private `FileCatReleaseEvidence/vs177-v2`:

| Retained path | SHA-256 |
|---|---|
| baseline-v4/command.json | 51a42483d4817b44ec685e69549ab09709d7684e414a285dc3b94eea4fb1017f |
| baseline-v4/results/baseline.trx | 2bb9095d9edc4caefccf5f64e0539f6dc67c0019c5337d012ad5bb7c7aa8c203 |
| working-v5/command.json | b3d26779a8afba3be883f53190e44742004956d945f2c03dc15df173acc810f5 |
| working-v5/results/baseline.trx | 6f943389466ab39ebfb7e901865aacdbd252c4403befd2a262265560cd018e26 |
| independent-working-v6.json | 6b515248d07a2608da767fa9453d36cb824499f8b6c4f75e5874cd8b365ee85f |
| clean-v7/command.json | 176117522909e67a236c303982d54aceadefc5a1111f97b34a6c06571468c929 |
| clean-v7/results/clean.trx | 9a558ab08bff294acb031ee3958d1a91e36960c67c521bc2aaadd261675a3cf9 |
| independent-viewer-search-v11.json | fa3635bf49a856990c77be57e38f944ff70026fa26a3b89fc5fe0e1ba7e3dd5a |

Private `FileCatReleaseEvidence/ci-37566601056-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 9a2452197ed4d2397bca50b237738f4764bd105a4bd3e70146df26c868724171 |
| independent-draft-guard-ci-v1.json | e23ad563733e21327945ab3eed2831e8f47733413d510478f3b0e1783e246a10 |
| independent-fixture-ci-v1.json | b1da93ece558bd8c6c66fee65b3852a1a9ecf719346707f7e662a2dada72d364 |
| independent-i163-ci-cases-v1.json | ee607f81840dde3924ddf41fef5f0381d3d6fea5e22041434890e6e5b4da0d44 |
| independent-i164-ci-cases-v1.json | 7b4bd509319058c8873938c83100375187d6e7053b2de2f0682a4bbbb086b5e5 |
| independent-i165-ci-cases-v1.json | e9761b222fb9b6c03ba28f76003cb1ae7ff34d530f6dbf829479f5a2644c45fa |
| independent-i166-ci-cases-v1.json | 92032b87df0d584be59b6deebd722ebc5cad80b05a266c6fe5059665863843b3 |
| independent-i167-ci-cases-v1.json | c08dd3fc463a068993cc35103c3b382a7a1a2ff84bfc34e6ea75624bb0994684 |
| independent-i168-ci-cases-v1.json | 00746a0d5483ba3985bbc32ea56a90fb808ff62c3b1dc9868a5ce1e20dbbcb2b |
| independent-i169-ci-cases-v1.json | a7b2e0189a1dd624c07a75ed39476fc22c7363747de6e95f4e906eaff0aab3ab |
| independent-i170-ci-cases-v1.json | 83bed8bf83d892e20c9112ae8258b7e0c89ed694a4bc1c462403251ccbf5ed15 |
| independent-i171-ci-cases-v1.json | 24e7ff6c672a3c9c82f235e12ddbe4e149836ce6e5be8084e8ce12f012e695dc |
| independent-i172-ci-cases-v1.json | 8377877b6499c73c36be4428a93584fb5eea9733e52495a56bee1d6148e9f024 |
| independent-i173-ci-cases-v1.json | ec7616debb517cc2d088b6456300da7750414fbe667396ac633974f6dac11769 |
| independent-i174-ci-cases-v1.json | dd9d45dab4e4c643ac555e1210ed4d1e6f9423f5bc3a291cf5f34279f97d8358 |
| independent-i175-ci-cases-v1.json | f51886fa61dabf726188cf1dd87e9e0dc8156b03ee529419752706e6df1a6b87 |
| independent-i176-ci-cases-v1.json | cf05372b2e88addc6b8d50d19492b8a411c592af59efd5271b8b8794d10de4a2 |
| independent-i177-ci-cases-v1.json | f46321fee3c81e807b42e2ed7529151e9360374242da4b68271ed61cde4fe29a |
| independent-producer-policy-ci-v1.json | 85642f6df14c65f0b5a9f2ee91efc1433b2ebd1bf11f3e43e40bb6b33b6e1edc |
| independent-restore-ci-v1.json | 7f74e97f5aa168bf85242b2389a484cb3544431d6d6201478f1f9f99c1df5e0e |
| independent-separation-ci-v1.json | 00059f2a3857d3157414e11b67c3d5328502745ab56c5aa6434df07f730f3680 |
