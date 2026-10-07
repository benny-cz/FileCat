# E-I182 — viewer checksum completion owns current live demand

2026-10-07. Discovered against complete canonical product fa13957ac8a34ac4075bfc7ae1da4d750f43e96c. Medium invalidated-result and worker-lifetime defect; must fix under I06/V10/V12/V15. Remediated preliminarily at 576e55637b8f0a05bfe4ef963a9c13d2082d9afa. Wider/native/candidate scope remains.

## Failure and correction

An older viewer checksum can overwrite a completed newer range checksum's status and clipboard content. Closing during an active real source read also permits late status updates; the closed headless window exposes no clipboard, so its clipboard stays unchanged, even though the old status claims a copy. The whole-file closure control hashes only the bytes returned before the disposed reader stops later pages. Four original adverse controls fail and two ordinary whole/range positives pass. Native desktop clipboard, shutdown and human behavior are not inferred.

Each calculation now owns a linked cancellation source. Another checksum supersedes it; closure cancels its linked demand. The actual PagedReader receives the token and checks it before/after every page call, retaining the active synchronous read until that call returns. A canceled or superseded completion publishes neither status nor clipboard. The source clears/disposes after the task returns. Clipboard completion appends the copy suffix only while the same live demand still owns it, and no suffix is claimed without a clipboard. Hash algorithms, range selection and early-content warning remain.

## Controlled validation and provenance

Six durable complete-viewer headless controls cover replacement, closure and ordinary completion for whole-file and selected-range calculations. The viewer uses its real FileContentSource/PagedReader; one page read is decorated to hold the boundary. A newer 32-byte range checksum completes on a cached page while the old read is held. Independent Python SHA-256/CRC controls fix exact whole-file, 4096-byte range and new 32-byte range values. Owned 262,144-byte inputs stay unchanged; no source disposal occurs during an active read. Canceled demand adds no source read after the held call returns. Current whole/range completion yields the exact expected hashes. All six corrected cases demonstrate disposed cancellation sources and cleared current ownership.

Original canonical baseline: four failures/two positives. Working overlay: twenty selected passes without skips, exactly fourteen prior cases plus six additions. Fresh locked committed build: sixty affected passes without skips, exactly 54 prior viewer/picture/QuickView/admission cases plus six additions. Only the tested viewer module and new test differ from baseline product source. Canonical working/committed differences are CRLF only.

The independent seal checks 22 retained files, all 423 primary actual payload file references, all 1,086 original raw Git blobs/modes and all 1,089 clean committed blobs/modes plus every archive member. Clean FileCat.dll SHA-256 52351301ad394b23f454c65ea7b1e14314d0605ef13dba3b389b2eb787592bda. Owned fixtures clean up. The initial working runner's outer assertion assumed sixty cases although its narrower filter ran twenty actual passes/child exit zero; exact names independently reconcile without a rerun. A clean-run preparer initially applied case-sensitive matching to VSTest's case-insensitive filter and stopped before export/build. Both preflights remain retained. The seal's console summary prints an obsolete 1088 count; its verified canonical tree and persisted proof both correctly contain 1089.

Original push CI [37576460051](https://github.com/benny-cz/FileCat/actions/runs/37576460051), attempt 1 at 576e556, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 536-case App inventory equals the prior 530 names plus exactly six viewer-checksum additions; all 24 distinct new executions pass without skips, with actual file/page-reader held calls and headless clipboard observations. Core retains 890 Windows/885 Unix names; I163–I181 subsets retain exact outcomes and explicit skips. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No shipping artifact or candidate is selected. Headless clipboard mechanics do not qualify the native desktop clipboard, installed candidate, source-revision races, provider errors, reference performance, physical sources or human UX. Clipboard operations already submitted to an asynchronous platform API are not recalled; this held-read scope verifies ownership before submission and after its completion. No machine, contract, candidate or publication changes.

Private `FileCatReleaseEvidence/ck182-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | b0012e6cf0871cf7829f1a6c8bccdc35e959a635cf4e20b89b4741729fc577e5 |
| baseline-v1/results/baseline.trx | 142b4c746aa5765e95cb4cbf35f98fb9ada4c7201e102390bcf8760f0cd556c1 |
| working-v2/command.json | 8887193886fd6563d795350edaf19101d798161ffac7ca76a215266909642dde |
| working-v2/results/baseline.trx | f19b47a4df60bd25f0e7b256d48835f152c2628f5927aa57f49e738ab0d497ad |
| independent-working-v3.json | fbf3043a7d36821ab4f17a0cbb2d7ce3337c9eb284c037fc7e189dc3670ab61b |
| clean-v5/command.json | 65bf01817efc8627c234f25663b9a9e0a877ac98ab819d99aaefdbde464e067a |
| clean-v5/results/clean.trx | c79f15576d167a2ae0255f2cbecf7835d9af49f9f145e6253aae0cf588544d91 |
| independent-checksum-v6.json | f0cb48c746c46f7572a6ce1424b8c87759f081cc44a74b5f46d7f0572284b0ff |

Private `FileCatReleaseEvidence/ci-37576460051-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 4eaed235a4ac3bcc9de74cf4794a89df5eaf3f849f26d5618f84f184cebfaf14 |
| independent-draft-guard-ci-v1.json | 2e22d831ef0ed9d71312559f8c591aa70acc4eed8b067f71c32719c8d7febaf9 |
| independent-fixture-ci-v1.json | 32adc36adb680fece91c16027aa03980e0455cccb5f13f7b62c6fbc9bfabfe8e |
| independent-i163-ci-cases-v1.json | 7e8f8e95e40878ab0b1201a7a0445b29b6694f226d71c0a93064ed86a2eb07d8 |
| independent-i164-ci-cases-v1.json | 4538434d2a9aaa1e97312c778e69aa828989d476ee946153c22be02fb53728da |
| independent-i165-ci-cases-v1.json | 046b9409118d55a498fbcb1753d878b7a02b804a9000246f25a48714041879ca |
| independent-i166-ci-cases-v1.json | 494fd7fa7f4d450dc54e1b925e21582c808a146c322a6cb3ca24fc2dd9d285ea |
| independent-i167-ci-cases-v1.json | a6fc52b683cbde7b10bc1b2baf7ff07406b03b8a3c9b98c0253b8bf878e8acb8 |
| independent-i168-ci-cases-v1.json | dd2c52aa296967830f66b73ade66ae4114a183cf8e8535639f87da5cc217f818 |
| independent-i169-ci-cases-v1.json | e5397cf57daf6de017e80864b7a3491f4f7cdb0238e113728446b7f6dfbb5c7a |
| independent-i170-ci-cases-v1.json | 20fb1d40ea76471c09a525071e70e7b0b5aef6ae0fc5f736276724e11fdc1160 |
| independent-i171-ci-cases-v1.json | 5d346c4148f85ea062ca20817ab097b9ef1e1a5f9c0a41f306bd449c8c6bd658 |
| independent-i172-ci-cases-v1.json | 7dc517b4c49a29efe5b8f85f5bcc625dc539ad61679baedaa2303a4abba92ead |
| independent-i173-ci-cases-v1.json | 345f215ad3057e0e07091ae4bb771e92d447db773e24a2f396d5154e0397ba2e |
| independent-i174-ci-cases-v1.json | 54b23f16268bfa61b1cb90c452dd7e1467ef9e19505f5c1f7004bfb5d22c29bf |
| independent-i175-ci-cases-v1.json | b6418f60e5bb46a7be7ddf7dde9e8bb93ff05090008a401a91947900f5aeb60d |
| independent-i176-ci-cases-v1.json | 0ac8583c5f9f8b9abb19a161635409d6c037ff37f2602d12b6ab8195dd0750f4 |
| independent-i177-ci-cases-v1.json | 4bffd21c0c41bc118dd1db077407f1d0ad2d174dd7a971363dc631eefb50a40b |
| independent-i178-ci-cases-v1.json | 542b6c50c62d7e0cc47f59c55e455ccafa62ae24386a959cc7a7e48a08b20050 |
| independent-i179-ci-cases-v1.json | c8d2c6a6ffeb9133c431cedd88a251cb88bce03302650c2a79a79d480688f4d0 |
| independent-i180-ci-cases-v1.json | b552d4b373cb1be7cc782dce4f2accab27fe2e524ac4121ccc67b31850361724 |
| independent-i181-ci-cases-v1.json | af697343ec649309c16e04a39436eac2034c04e7e7bfcda8e5a90191aa8b3ae4 |
| independent-i182-ci-cases-v1.json | aa6a0a5dac58e4b44591c0ea0acc1b94090ef121d9b3c8a0b1dcd27197a1c340 |
| independent-producer-policy-ci-v1.json | 749dd3309701fcccc5e1b3f9c2d076ba787348d903b4227e4fdd6776e2a7500e |
| independent-restore-ci-v1.json | cfe3e65e6105b666780f661f92b94db68c092272ab52dad6b4ebbf754a435eae |
| independent-separation-ci-v1.json | 72243007fe43d3e74bcb1c2d64452c55aae4ac1f323e4c4cc05cda37d23b1b48 |
