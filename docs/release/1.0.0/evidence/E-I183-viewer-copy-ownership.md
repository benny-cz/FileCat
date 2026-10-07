# E-I183 — viewer copy completion owns current live demand

2026-10-07. Original complete canonical product 576e55637b8f0a05bfe4ef963a9c13d2082d9afa. Medium invalidated-result and cancellation-lifetime defect; must fix under I06/V10/V11/V12. Remediated preliminarily at 25432ce5ac8c21cd069ad2e8a518bb2755e2280d; native/broader/candidate scope remains.

An old whole/range hex copy overwrites a newer 32-byte copy's clipboard and status. Closing while its real source read is held makes CopyAsync throw NullReferenceException when it later asks the closed headless window for its clipboard. Four original adverse controls fail; two ordinary copies pass. Actual native shutdown or desktop clipboard effects are not measured.

The correction snapshots the clipboard, mode and selected byte range while the window is live. Each copy owns a linked cancellation source, superseded by another copy and canceled by closure. Hex copying uses the actual PagedReader cancellation overload, keeping the synchronous source call alive until it returns and stopping before later pages. Hex conversion runs with background work; the existing 1 MiB copy limit and single-byte fallback remain. Only current live completion submits the clipboard operation or updates status; the cancellation source clears/disposes after completion. Text copying participates in the same completion ownership. Already submitted asynchronous platform clipboard operations are not recalled; native clipboard ordering remains wider qualification.

Six durable complete-viewer headless controls cover replace/close/ordinary completion for whole-file and 4096-byte selected copies. The actual FileContentSource/PagedReader stay in use, with one decorated provider boundary holding the read. A newer cached 32-byte copy finishes while the old call is held. Observations record clipboard string lengths and hashes, avoiding huge clipboard dumps; independent Python computes exact whole/range/new-copy hashes. Tests compare actual complete hex strings as well. Owned 262,144-byte sources stay unchanged, no source disposal occurs during reads, and canceled work starts no later source read. All corrected cases have no exception, dispose their cancellation source and clear current ownership.

Original: four adverse failures/two positives. Working and fresh locked committed builds each pass the same sixty-six affected viewer/picture/QuickView/admission cases without skips, exactly sixty prior cases plus six additions. All six additions pass. The independent seal checks 20 retained files, 423 actual payload file references, all 1,089 original canonical blobs/modes and all 1,091 clean committed blobs/modes plus every archive member. Working/committed differences are CRLF only. Clean FileCat.dll SHA-256 3f51c05e6860359cc3e7ddb926125807cbcc823a5b25f82a093d6b68fc360692. Owned fixtures clean up. No product/fixture preflight failure occurs in this slice.

Original push CI [37578155139](https://github.com/benny-cz/FileCat/actions/runs/37578155139), attempt 1 at 25432ce, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 542-case App inventory equals the prior 536 names plus exactly six viewer-copy additions; all 24 distinct new executions pass without skips, with actual file/page-reader held calls and headless clipboard observations. Core retains 890 Windows/885 Unix names; I163–I182 subsets retain exact outcomes and explicit skips. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No shipping artifact or candidate is selected. Headless clipboard mechanics do not qualify native desktop/shutdown, text/Info copy or single-byte/cap boundaries beyond this selected-range scope, source-revision races, provider errors, reference performance, physical sources, human UX or installed candidate behavior. No persistent machine, contract, candidate or publication changes.

Private `FileCatReleaseEvidence/cp183-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 78af81e319807b88805e1ab7d285796e4dd2f8b19fe14889af8af3c1a50f9e80 |
| baseline-v1/results/baseline.trx | 7b1fceccd4908e0741f20f157563d20a59da5e815ccd079cd9560e2ae8e2c8d4 |
| working-v2/command.json | e995930a868d953c5d764a7619e5d5bcb98d37c591b07729f9651bdc93706a64 |
| working-v2/results/baseline.trx | 9b897268a06e0d23e2f1238f58901a3336f62a3d95ce1f8946d5a7f9b21f4147 |
| independent-working-v3.json | 539cf4aa5b5cd2a308c63bd89657b861fbac490a6f28fe736fd6fda1a598a14c |
| clean-v4/command.json | 7a4b7440b21cf287870f5d7f6205e03be8391c8e2a515c63f97ae1a3b20775e2 |
| clean-v4/results/clean.trx | 47b80d879b3b6465dc29f8ee914053666402b6a5a07d9ffb419f7789d535cda2 |
| independent-copy-v5.json | 7264997d73e66dc49c83a8c2c38f63480b9982ebdad3b4f48e28a89a3127c9ac |

Private `FileCatReleaseEvidence/ci-37578155139-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 84d7462615434bb22e1a621d60dab64c0b80e19e19663c74568253ba1a080c02 |
| independent-draft-guard-ci-v1.json | 1da24871df2a6497a6e3ddb21ce5cca146459d50b4f4fe83c239b5bfed02b7b5 |
| independent-fixture-ci-v1.json | 3afc3f01e78c0d9f527100362c651ead8cb4683a4f24d50b7e00a5bd918cfe90 |
| independent-i163-ci-cases-v1.json | 471510087134844af6830361e9b9a0571fdd45c281af3d7f63d47f129642851b |
| independent-i164-ci-cases-v1.json | 156cbb315acca8ca82fa04c63dc7b3c3c3458065a8be010d84472192826dc804 |
| independent-i165-ci-cases-v1.json | 22a64eca5e995269d1c442a1b1df16ecffe6f97671a01f2f6a6f3e03679313ea |
| independent-i166-ci-cases-v1.json | 0fa6177629b0501d894660d4fcde6f1c03d0643eb6b23c170aca2a0461b9adf9 |
| independent-i167-ci-cases-v1.json | 42953407de518d78b1b942a39179438be0dc1a49dd76b0a5d5d93726b69ada79 |
| independent-i168-ci-cases-v1.json | 9f1e73042d1d7148a9b4f66e72a5c361540a35ba5ad42b3a6342495b28969f4c |
| independent-i169-ci-cases-v1.json | 57829d4c82e72dc746d0d0413efe538ef96c966ebe350f403f8bb353e291ab66 |
| independent-i170-ci-cases-v1.json | 2186d69ff656042a373853f227bfc07022a5189d531abec67b09f0f1621aa55e |
| independent-i171-ci-cases-v1.json | 2ab4370d4bc6eaabb9bdda78f57d906c04a8b4fea4ec0c56fe310fe38e2244c0 |
| independent-i172-ci-cases-v1.json | dd060543c6f4fc2e2455b120fc3d29426e9f6720712bbc99b90e8432811d0097 |
| independent-i173-ci-cases-v1.json | dc5b34da39ef2a0a7b7033ea0a60e18f4f8793a53b15f1f4a9fbfb2208cd9b9f |
| independent-i174-ci-cases-v1.json | dfc8ef288f89f5bd65778b4b9e74aff3524c1a60fd97382f3e4c180ebac3cd30 |
| independent-i175-ci-cases-v1.json | 37061a89f0e90c10c9fc75e1d6eaa49900c0f3cc2a10f48417eb4d6fcf2d3411 |
| independent-i176-ci-cases-v1.json | c9824af38e1fa428875bc29b92fd5eb696cae575a741c8c9e351b9ac7667e7df |
| independent-i177-ci-cases-v1.json | 50ca12d0566a372f3cd260dbf635474582cdf8086f4cda677bf15b39d9d37051 |
| independent-i178-ci-cases-v1.json | c5e060146ae119913e3aa40efbb93feedae3ed754c72d44b8e9c4c995d3ef909 |
| independent-i179-ci-cases-v1.json | be67a30e7a4831acd2036f5e5170ced2ef471adfe967ba2a0c84ec154a03aff0 |
| independent-i180-ci-cases-v1.json | c5fca007dd602cb28063ae18ed9ba62aeee458c479a9de4328e1aa900cbb861c |
| independent-i181-ci-cases-v1.json | e91cc5eaa031191eb9dcc9fe319545ac1537d587074728247f1246826788070c |
| independent-i182-ci-cases-v1.json | 63c1a9236f97dba632b757be1413881e573f634a7c9b4618863d2bfa25e4c98b |
| independent-i183-ci-cases-v1.json | bce7652fa2571fea7847d87db53eac6808ebd13b4f95f2dc7160a5e924c9d616 |
| independent-producer-policy-ci-v1.json | 0e3a0a981ea9b363e66300e7dbdcaae0846f994160d63b970a9374076223a959 |
| independent-restore-ci-v1.json | 27a34e52e052fc0976e4a42e9a2b30c02a7878635b593b1f31849ce5f5d506e5 |
| independent-separation-ci-v1.json | b0b3e73d26d7803396f4c95b8f39a80221e1cb87beab3ad2df03b8588697e213 |
