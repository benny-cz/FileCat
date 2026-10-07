# E-I182 — viewer checksum completion owns current live demand

2026-10-07. Discovered against complete canonical product fa13957ac8a34ac4075bfc7ae1da4d750f43e96c. Medium invalidated-result and worker-lifetime defect; must fix under I06/V10/V12/V15. Remediated preliminarily at 576e55637b8f0a05bfe4ef963a9c13d2082d9afa. Wider/native/candidate scope remains.

## Failure and correction

An older viewer checksum can overwrite a completed newer range checksum's status and clipboard content. Closing during an active real source read also permits late status updates; the closed headless window exposes no clipboard, so its clipboard stays unchanged, even though the old status claims a copy. The whole-file closure control hashes only the bytes returned before the disposed reader stops later pages. Four original adverse controls fail and two ordinary whole/range positives pass. Native desktop clipboard, shutdown and human behavior are not inferred.

Each calculation now owns a linked cancellation source. Another checksum supersedes it; closure cancels its linked demand. The actual PagedReader receives the token and checks it before/after every page call, retaining the active synchronous read until that call returns. A canceled or superseded completion publishes neither status nor clipboard. The source clears/disposes after the task returns. Clipboard completion appends the copy suffix only while the same live demand still owns it, and no suffix is claimed without a clipboard. Hash algorithms, range selection and early-content warning remain.

## Controlled validation and provenance

Six durable complete-viewer headless controls cover replacement, closure and ordinary completion for whole-file and selected-range calculations. The viewer uses its real FileContentSource/PagedReader; one page read is decorated to hold the boundary. A newer 32-byte range checksum completes on a cached page while the old read is held. Independent Python SHA-256/CRC controls fix exact whole-file, 4096-byte range and new 32-byte range values. Owned 262,144-byte inputs stay unchanged; no source disposal occurs during an active read. Canceled demand adds no source read after the held call returns. Current whole/range completion yields the exact expected hashes. All six corrected cases demonstrate disposed cancellation sources and cleared current ownership.

Original canonical baseline: four failures/two positives. Working overlay: twenty selected passes without skips, exactly fourteen prior cases plus six additions. Fresh locked committed build: sixty affected passes without skips, exactly 54 prior viewer/picture/QuickView/admission cases plus six additions. Only the tested viewer module and new test differ from baseline product source. Canonical working/committed differences are CRLF only.

The independent seal checks 22 retained files, all 423 primary actual payload file references, all 1,086 original raw Git blobs/modes and all 1,089 clean committed blobs/modes plus every archive member. Clean FileCat.dll SHA-256 52351301ad394b23f454c65ea7b1e14314d0605ef13dba3b389b2eb787592bda. Owned fixtures clean up. The initial working runner's outer assertion assumed sixty cases although its narrower filter ran twenty actual passes/child exit zero; exact names independently reconcile without a rerun. A clean-run preparer initially applied case-sensitive matching to VSTest's case-insensitive filter and stopped before export/build. Both preflights remain retained. The seal's console summary prints an obsolete 1088 count; its verified canonical tree and persisted proof both correctly contain 1089.

Original push CI [37576460051](https://github.com/benny-cz/FileCat/actions/runs/37576460051), attempt 1 at 576e556 remains pending at this local seal. Headless clipboard mechanics do not qualify the native desktop clipboard, installed candidate, source-revision races, provider errors, reference performance, physical sources or human UX. Clipboard operations already submitted to an asynchronous platform API are not recalled; this held-read scope verifies ownership before submission and after its completion. No machine, contract, candidate or publication changes.

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
