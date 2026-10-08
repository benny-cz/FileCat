# I210 — interrupted-operation review and demand ownership

**Preliminary remediation qualified at 7c708eec964e40060f9b14ed4ce2e206dba71523.** All 45 additions pass: 37 App controls and eight Core controls. Expanded working and exact Git-canonical clean runs each pass 517 targeted checks (96 Core, 90 Remote, 331 App), with eight explicit existing environment skips and no new skips. The full clean App suite separately passes 964 cases with 23 explicit skips; all targeted App names/outcomes are retained. This is component/headless owned-file evidence, not candidate or native desktop qualification.

| Confirmed gap | Result |
|---|---|
| Changed staged paths were deleted after an old confirmation | Complete registered-provider reads capture SHA-256/size/times. Deletion rechecks the reviewed version on its path's shared device worker. Changed, unreadable, partial, linked and over-budget files stay and are named. Same-length/mtime edits and replacements are exercised. |
| Late approval after shutdown/window closure still deleted, reconciled and submitted | One owner per journal spans preparation, actual confirmation, deletion and alerts. Ended demand suppresses subsequent actions; an active source remains owned until its call returns. |
| Clean-up button used the same blind staged deletion | The actual OperationsView handler now calls the same reviewed cleanup. Its changed-byte, cancellation and ended-demand controls run through the real headless overlay. |
| Repeat clicks and stale rows could prepare again | Pending actions coalesce; completed/live/unavailable journals are rejected before review. |
| The old journal closed before the kept-file alert/new-job admission | It remains open through confirmation and alerts; run-again submits before reconciliation. Failure/shutdown after submission can leave the old journal open, explicitly preserving it rather than claiming atomic admission and closure. |
| Failed journal append removed the durable source manifest | Reconciliation flushes the end record before removing the manifest. The original locked-journal control loses the manifest; the correction preserves it. |

## Actual validation and retained adverse evidence

The unchanged 58f2617 producer has 32 failing and five passing App controls. Seventeen failures exercise actual changed-byte deletion, ended actions, coalescing, stale prompts or manifest loss. Fifteen fail the provider-entry checkpoint because the original bypasses the registered content provider; their injected holds/errors did not run on that producer. The eight new Core API controls are qualified on corrected working/canonical source and are not presented as original executed defects.

All fourteen executed stages preserve their canonical source archives, overlays, actual private payload hashes, command receipts and available raw TRX. Baseline-v1/v5 and working-v3/v6 preserve compile diagnostics (internal reader visibility, event-type qualification and a missing formatter import). Baseline-v2/working-v4 preserve two alert-label timeouts; the alert's actual action is OK, not Close. Working-v8 retains its SFTP wait-predicate failure; its separate full suite passed 964 cases with 23 skips. Clean-v12 retains one targeted cleanup timeout, while its separate full suite also passed 964 cases with 23 skips: the observer repeatedly scanned the journal during its write. The corrected observer waits for the UI acknowledgement before opening the journal. Baseline-v13 is a post-product probe accidentally launched in baseline mode: its 37 tests passed; the runner then failed its expected-failure guard. Working-v14 correctly records the same 37 passes; clean-v15 qualifies the final producer. The original Single() could throw before asynchronous acknowledgement exposed the record; the corrected predicate waits for a present record, then checks Unchanged and still asserts exactly one persisted session. It does not prove every historical filesystem timing cause.

The private independent reader rechecks every retained source archive/Git blob/mode, overlay and payload reference, raw inventory definitions/counters/skips, all 37 emitted App observations, baseline provider-bypass distinction and canonical line-ending normalization. Native setup, physical USB sources and persistent VM/Mac settings are unchanged.

## Bounds and remaining work

Automatic staged review reads at most 64 MiB across up to 1,000 staged paths. Files outside that budget remain, with an explicit message. Copy review checks a finite number of directory entries and supports cancellation; incomplete deletion still compares its source prefix immediately before removal. Interrupted renames retain the existing algorithm, now worker-admitted and guarded by ended demand; native rename identity/replacement qualification is pending.

SHA-256 plus metadata is a conservative version check. It does not establish an atomic native identity or eliminate parent-link/read-to-delete races. Cross-device copy-source/destination admission, source disappearance/replacement while approval is open, multi-process journal races, larger workloads, remaining resource/provider cases and native/candidate reruns remain within I06/V03/V11/V12/V23. The [I211 save-copy follow-up](E-I211-edit-save-copy.md) qualifies that caller/lifetime subset. Broader identity/resource work and unrelated owner, physical-source and publication holds remain.

## Provenance

Private `FileCatReleaseEvidence/ir210-v1`:

| File | SHA-256 |
|---|---|
| independent-interrupted-clean-v2.json | 07bcadc1ded0aff27f2c6e711238bd63736378be6ea47dc6042f0f60eaf269ed |
| seal-interrupted-v2.py | 97157da3c2f49d6f2bcc7bd565c256d6311001f89e15ea9a7cfa526c534996c9 |
| update-documents-v2.py | 332fa62f08a0f5c13efa55b12b544ea4bbd82e734774e4238e90099df5cfbef8 |
| document-transitions-v1.json | c51fa7e69eaf65465c15c0e4163c56418e650dd2cb1c22a958ab368a353ad3bd |
| post-product-probe-guard-v1.json | d6dd16923ca76edd104e91a808c3e8e76486c441ad494e1c3181a465d80c47e6 |
| run-interrupted-v1.py | 339b387d802038e8b23ff9d1a8464b64b8276999d51add4ee5b48bb2588d79da |
| run-interrupted-v2.py | ea4fe3fc5c55641664b09f908659649283c3069bfe10e3f3cd4e85ae2b486da3 |
| run-interrupted-v3.py | 31ebcfae9e4cec3081cce6811fc618716589916c03a9a07e1bde3366436070a2 |
| run-interrupted-v4.py | 36c99b160a45fe08153e69d14f92414bf0924d4406b77dd0fc451cdcac7c5476 |
| run-interrupted-v5.py | 7501b53aa0bd49c04991e3c44188f292f3b657fceeed64c53c6af69e8d721c7f |
| run-interrupted-v6.py | 5c6e85df83eaf2499328e21a55f3ddb7f558f8b227fa893a2d1e584d852a4872 |
| run-interrupted-v7.py | 48bff9f6caf3f03b6c3e2317cc494cf56dae886ce4e710b18b6e8ed9aa50d080 |
| run-interrupted-v8.py | a1149a3d8cd93ffb552f768a7382dd8c69c74dc389b2d2097ac1dae4f3ea0f7b |
| run-interrupted-v9.py | cb93bc941c0272e062fa14151954f5d6d19256d45939322d18c663e5fb49bf3f |
| run-interrupted-v10.py | cb99257a0c7569004e54ed7b364cbfa16d473e5d1281f627978d647d15b2e4fb |
| run-interrupted-v11.py | 6e380a53a44abe52f1520fa4744023d0c7a5f2d5caa994f17f16c3654b7c5d4d |
| run-interrupted-v12.py | 3e25389ba69c0ba25cf5aee1b298f4d65f186088e36e8bd1de230f228792d6db |
| run-interrupted-v13.py | 76cd0dde6699fb6ccb446a36031cc439dd8b8413f4d3ff743a36b058db5eae6b |
| run-interrupted-v14.py | b06048ffb1992adeff1111fb9bc81a0cc56f6cb0db920199e0b977970033b3f0 |
| run-interrupted-v15.py | 110ec50aa415fe983f8b6ff245968931229f7c4b7e469da103faaa0548c189b1 |
| baseline-v9/command.json | 99c738555fecd4c01ebc7195a3688c6be884e99cf211eb9de353aa2de92c9583 |
| baseline-v9/source.zip | 9adacc8a0a2f0bc12ac2e61dfe6abdb31223d833487c992d55c0a6f63c538740 |
| baseline-v9/results/app.trx | c5fca086e1bd565ff116b523c8658db2566c9cdd44d8a96ce269291abefa33a4 |
| working-v11/command.json | ea64828a2316b822064e6b48f013aca148a91bda8edbc05ca0474794b7ab7fb2 |
| working-v11/source.zip | 3939cd0c3e05b145be55197f519958cca877672e68938189f9de2d47fa122db5 |
| working-v11/results/core.trx | a66b649020bc8f49c15c000b1545d5503c49a9d136e817755762570a39bdade3 |
| working-v11/results/remote.trx | f6e5a0654df96994de2688d5684206228ca988da4ba20f7914707deb6789d5f5 |
| working-v11/results/app.trx | 652b5aca72366ea01f1b8887943ba9897d86c67830373b3ace0859c9991dc477 |
| working-v11/results/app-full.trx | 4ccd59b92ae91e367fd601b500231ae1ee07a1bc92a3509eaaa6a860c807c83a |
| clean-v15/command.json | 76cbc7cdb95d13d0a76822b27410cb237dc1f11daabad9cff0e508fcaec44c46 |
| clean-v15/source.zip | f6aedf5412f42a900eeefceab4e2ea623e73c4688d87bf0f082db8dd4029f83a |
| clean-v15/results/core.trx | cf673bf68f653e209d8919a1a9624344fe6761eeefdacc3ac4ebef80af2b998f |
| clean-v15/results/remote.trx | f8443c93b0de68d2f9aaaaf5d61b198fd6dd7ad884a1ff9d1f41d82a6938596d |
| clean-v15/results/app.trx | 1b8cec01f371976f660c94a2bb2f1f8f238e00c3f651ccaca4464aee18588369 |
| clean-v15/results/app-full.trx | 81d4a7c22aa98638b4052d0819d362d170c24ac766b28c2b88c7e3dddfeb835a |
| baseline-v1/command.json | c374a7a5bdf1d14c8c11bfd0a98edc32cb3a6d0ce190c613a877573c9347b866 |
| baseline-v2/command.json | 1bbf449f359d6ce16c0064403ecc636b9a90cf966b810a639993b7daffb622a5 |
| working-v3/command.json | 1062e8e30da41b9eccbcab226f71e0c1eaae4ed2623f284b5aa9fa1c1f7d4f3e |
| working-v4/command.json | 323b6ffde624c62cb4365770c3bff990b8dac7c4a3df0805277bb7d80bb5c15b |
| baseline-v5/command.json | f29750ef1da8ed9513608007fd5bd1a1893ddbfa623b6b464f64fc9a364e0b03 |
| working-v6/command.json | 2d36f1dab22e4139563873b41a55ccfcb5f349f8e7035a48cea47df12a103535 |
| baseline-v7/command.json | 6a6c7b6921f92f0a71d142c20ccff014cf399b6e221748478159bda437d18c2a |
| working-v8/command.json | 80b923819ecbc4c8d7b52224ec9e9dcfde4851872f1fd7ea64d7cf0314fadacb |
| working-v8/results/app.trx | 1e7ca1f9645053f7bf61f3c9e7f074a223f481a745e19d35287c93c40fd643f5 |
| baseline-v1/app-stdout.txt | 7d05760a730514c60d9f0a090e0a0e5edaf6d45bbd2c6e25687be79362c08092 |
| working-v3/app-stdout.txt | 3c358555a320648b59392d44c93313ee259b0c356024d3fbb2ec3e300fc8c269 |
| baseline-v5/app-stdout.txt | 94398fcdbdd9481b7315f2df91bc041bcce19100c2b0263cffb93a417bd82354 |
| working-v6/app-stdout.txt | 63d43ac25237503e44a2bddaa9880d70b19dac9d4e63e9f7534e5832729b5f6d |
| clean-v12/command.json | 92f5f7ec56fbfdbce7600e4adc59fbad73a9b404b96c5d7de06b0e76566520be |
| clean-v12/source.zip | 8158a2e263ac12fa2b8584a76d84101de9cac1a61d547c27d769ba968940ec0d |
| clean-v12/results/app.trx | 6bcdbbc2fbe6fcb9d604b056be54228af9f93c6cbb8456dc9473d93851f073de |
| baseline-v13/command.json | abb042275ed024590b409433e37efa948d824093adc5ad3703fc5f074776ba87 |
| baseline-v13/source.zip | 217cdbd80a7d96574e5175464ffdf09a07adf77897bbf70e2c97e9ec8eb27fc3 |
| baseline-v13/results/app.trx | 29d4263343ae1346d21585754b0d3b950bb9001662a2c902dd05a8d68170b354 |
| working-v14/command.json | 24c5dc8ab2e756558238c639d9eb9583114c5960444be463d4e8ff3c974cc8c5 |
| working-v14/source.zip | a3177fca7565837a41a97072cfd326b5232f677cdd0fedfd766d876d744ad5be |
| working-v14/results/app.trx | 06b1a25fb00bfd8963e0b0e84bf375857ece7795ff386064a0c2e914086447f3 |

## Original Ubuntu refusal-control failure and fixture correction

CI 37713228605 attempt 1 at 7c708ee fails `A_failed_journal_close_preserves_the_durable_source_manifest` on Ubuntu. Its exact observation is Retained=false, Ended=true: POSIX sharing permitted a successful append, so manifest removal was correct. The fixture had not established the intended append failure. The original official job log and job snapshot are preserved; this is one retrieved failed job, not a complete four-lane artifact audit.

The fixture now verifies an actual sharing refusal on Windows. On POSIX it removes write permission from the owned journal, verifies an ordinary-user write refusal and restores the original mode in finally. All 37 affected Windows controls pass on the working overlay; 141 actual payload references and canonical unchanged production files verify independently. At this working checkpoint, the POSIX correction and the next exact fixture producer's native results were pending; the original-attempt follow-up below resolves that subset. This changes the test precondition, not the product's successful-close behavior. The full 517/964 qualification above remains pinned to 7c708ee.

Private `FileCatReleaseEvidence/ir210-v1`:

| File | SHA-256 |
|---|---|
| independent-refusal-fixture-working-v1.json | a5992f5d1b29f7041ae28e9c927a961a588f6f69a41afb87164031f07dba564e |
| seal-refusal-fixture-v1.py | 9cfd3b35693ab7af4cd3933a3548be36f57e98ea3d748c79b5f73e34c8751b23 |
| working-v16/command.json | 86bfec3f3a475f1e36423d646606bb3f704c452e6238fb9d70c91d1c894835f9 |
| working-v16/source.zip | 693aef764ba843075c53d0d64e81922cf1053e8297f92b311d98e83d6559836a |
| working-v16/results/app.trx | 04d8403e9b86eef2595b2118bc85b52b2c4b4fc33deacd0e329c02c699a4ebda |
| run-interrupted-v16.py | 99163f4d5e143e3937e57d3214d5819137d290cf9b0a8731e4c67b904337e15a |
| ubuntu-ci-failure-v1/job-log-stdout | 8860ca6693fa33bcad9387f92cf80ec76a0442e83241ca439de18f4d313a279f |
| ubuntu-ci-failure-v1/decoded-log.txt | 8860ca6693fa33bcad9387f92cf80ec76a0442e83241ca439de18f4d313a279f |
| interrupted-ci-query-v2/jobs-37713228605-stdout | 0c9192b1858a363c872e32e60307f45f8f8f94d226c6a70ae66fc54187344d4d |
| tracker-guard-v1.json | 843a60213a203d0a9aa92ab0b2b5075fc005eda55864265adf377c6ea04fb2ce |

## Original four-platform follow-up — 4966004

CI 37715108315 attempt 1 passes Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. All 45 additions pass on all four lanes (180 executions), with 148 reconstructed App observations and four actual append-refusal checks. The corrected POSIX test now establishes the refused write, preserves the durable manifest and restores its original mode; the Windows test establishes a sharing refusal. Every predecessor name/outcome/skip is retained. The earlier Ubuntu sharing fixture failure remains preserved.

The final official snapshot of predecessor CI 37713228605 shows Windows x64/ARM64 success and Ubuntu/macOS failure. The separately retrieved original Mac job log confirms the same ineffective sharing-refusal fixture and Retained=false/Ended=true observation. These are two retrieved failed job logs and final official job metadata, not a complete predecessor artifact audit; the corrected original attempt above is independently audited on all four lanes.

Independent capture/readers verify 20 server artifact digests and every ZIP member, 14 complete raw TRX inventories, four build receipts and 92 actual dependency graphs. Hosted owned mirror/launcher controls pass nine/five checks. Packages and draft publication are explicitly skipped on this normal main push; no native desktop/hardware/candidate publication is claimed. Exact clean Windows source4966004 also passes all 37 affected App controls; its runtime/test/engineering tree differs from the fully qualified 7c708ee tree only in the refusal fixture, with production unchanged.

The first collector used another observation's field names and stopped with KeyError after downloading/verifying 20 artifacts. Its corrected continuation rechecks the same original-attempt data; no CI/tests are rerun to replace a failure.

Private `FileCatReleaseEvidence/ci-37715108315-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 202c0e555171492fb81dd9d59138f08548ca3fa1af02d7817e614e07e225ad58 |
| independent-restore-ci-v1.json | c92171bdaea53aa6cb93e68b5861a1418bd44f60d6a3407db70bf67024af450e |
| independent-interrupted-ci-audit-v1.json | 0eaa8c195a95d98566a95f40e8f20a0a34fcdee6ac7f14583a85b2be27276fef |
| run-native-stdout | fa6fda8584a005c9b6614e48b5c6af258fb3d55756bcdb39fb004ac8fdda6b49 |
| jobs-native-stdout | 7441c337ad1a52e721006d9a8eb7f77b5e724edb451649a95bde018a0347fd88 |
| artifacts-stdout | 864c64df40dd479cfb6e79ff784af968c3e00ec5e7bb33bd7e2d8ece99082157 |

Private `FileCatReleaseEvidence/jr210-followup-v1`:

| File | SHA-256 |
|---|---|
| prepare-native-collector-v1.py | 4b927d1f64f1a020446de4cce9b612c1f8e067efea17bfcfe0ed43fcc6aaf621 |
| collect-interrupted-ci-v1.py | 1dac593f2eb8e58f921d567ada611ff0d97779dbff6bf838dcff85fb9dbb4cd2 |
| collect-interrupted-ci-v2.py | 25340ea72ee4b616e91d64e86179ed663540b8835b0e8a78c638c76f0280f0b6 |
| seal-interrupted-ci-v1.py | 5746ea6b0020d7245422c3b0d3eb318d6fbc854f9c2e0574ae02937424468622 |
| native-collector-guard-v1.json | 2798122799c0cef4e1d04953d3c563c43177b0698135583a527afdcd3b4d7d4e |
| query-final-ci-v3.py | 3cfb1a09811ccd6da6f713f2cdd3f8ee6d94b2f94c1ba1e7b3039a7d8b2e8575 |
| seal-refusal-clean-v1.py | ab0b2b6d56a3057e5767bd17e4eed1242cca1763d094f8aa0f196ab514ffe4b6 |
| independent-refusal-clean-v1.json | cd81d3189450e12efac1e958e9b5a5d2a62877ef0bba9cdc01db5aa4720be177 |
| run-refusal-clean-v1.py | 3b90e2fdd122e2d0c6d2f1022e030d021a213627f94fc99af8fad25890f00de6 |
| clean-v1/command.json | 100c662df414887dca1f331ceb1c985bab1758e4f87092e67d15654d99003f52 |
| clean-v1/source.zip | 6404d0d22ff1be93a1d7c6311d2cb7bef6be1348dc1b8ab78aca0d72c64bce5d |
| clean-v1/results/app.trx | 07a8cf2a3d31f5ce5ed695ecaa37cecf41953990c176bf47ae5981529ea63500 |
| query-original-final-v1.py | e8848e649b4eacb4bec21a8173b4a303fa3d7abe04e84e2efe6b60fafd075389 |
| original-ci-final-v1/run-stdout | d3bfc781599f53316044d5302311bb5c78ac009b26a9a1456e148c5bfd982b4f |
| original-ci-final-v1/jobs-stdout | 1ca3600d763defc15e9f64a36a0fbbee4841c93ef5a74f033ab9f03144832f57 |
| retrieve-original-mac-failure-v1.py | e52640da1c958e6ffceab4e970444a69d20730f6ee7acee318c328632496a45f |
| original-mac-failure-v1/job-log-stdout | 5604aece03d99b00f82c9ede6b6d50105e5b28b282a540ce90102de373a5408c |
| original-mac-failure-v1/decoded-log.txt | a2c170d9624da2a06f57c75e7d2d20b026899f4eca99f98619607ee07c71e09b |
| original-mac-failure-v1/job-observation.json | 1ff61225bfa9e29d0840458b1cc9d863149098710ea9f9feaba189ec0e1002cb |
