# E-I200 — comparison provider-open admission and shutdown

Recorded 2026-10-07. **Preliminary remediation; broader I06 qualification remains open.**
Original product 419a358c54db4ed50705d6cfdfac00b95f199e26; discovery documentation 701e1053c0c05cd51a0ba4b55a97bc14973786f3 /discovery record commit 68741dd5aea3e28d8600c4bd255d29816499fc17; corrected producer d82397dc8adb3521d83dffe2bd46b2a4dc53f61d.

Main-window file comparison opened both providers through thread-pool work. Seven comparisons all entered a held left or right provider concurrently; after scheduler shutdown they opened seven comparison windows and read the files. A comparison requested after shutdown also opened both providers, showed a window and read content. The comparison's repeat factory likewise bypassed the device scheduler. This path is separate from the corrected F3 viewer admission.

Nine headless controls exercise the actual MainViewModel comparison method and CompareWindow repeat/close paths. An owned provider exposes two real 32768-byte FileContentSources with separate synthetic device keys. Left bytes are `(i*17+23)%251`; one positive control changes exactly the right byte at offset 16123. Independent known-input hashes and the exact one-byte difference agree. The original producer has three admission/shutdown failures and six positives: equal/different byte comparisons, null/I/O/denied second-source cleanup, and closing during a held repeat. Active-open sources remain alive until callbacks return in the retained original controls; no use-after-dispose is claimed.

Each main-window comparison provider now opens through its own interactive device queue. The asynchronous pair factory retains the first source until the second call returns, disposes abandoned/failed pairs and refuses source/window admission after scheduler shutdown. CompareWindow accepts this asynchronous repeat factory while preserving existing synchronous callers; its repeat affordance and change advice remain available. Closing a repeat lets the active open return before both fresh sources are released. These controls qualify provider opening, not comparison reading/alignment, every other synchronous factory, all hang/quarantine/queue/worker or native/candidate scope.

Working and exact clean producer pass all 176 selected App cases with no skips: all 147 preceding affected names/outcomes, nine additions and twenty existing comparison regressions. Every selected existing outcome also agrees with the original producer CI inventory. Independent v6 verifies original 1126 and corrected 1128 canonical Git blobs/modes/archive members, exactly three changed product/test paths, original and working overlays, 423 actual payload references, 25 retained private files and 27 raw admission/known-input/byte-difference/thread/shutdown/ownership observations. No owned stage executable remains at the seal observation. Original three failures and six positives remain unchanged. Original [producer CI 37659496946](https://github.com/benny-cz/FileCat/actions/runs/37659496946), attempt 1 at d82397d, **failed on Windows**; ARM64, Ubuntu and macOS passed. All 36 new comparison controls pass without skips. The only prior outcome change is Windows `GitAlternateTests.Owned_shared_objects_keep_badges_but_a_junction_to_them_is_refused` (Modified expected, null observed); its cause is not established by the raw result. All prior names/skips and other outcomes remain. This failure is retained for diagnostic fixture work; no CI rerun or green substitution occurred.

The first seal reader still referenced two older borrowed canonical receipt/archive paths and failed its guard before writing a proof. Its source and a diagnostic guard are retained. The fresh reader verifies the actual original producer archive, every overlay and raw result; no source, payload or test result was changed or rerun.

Private `FileCatReleaseEvidence/co200-v1`:

| Retained path | SHA-256 |
|---|---|
| I200-discovery-v2.json | 18bec02dbbb27dc2e1d2729d55d3b47e2d000a11ce9c66a67b88ec4af79e9fcf |
| baseline-v1/command.json | e38d8ea7a8992c04f396afd7ecb40805bd0277fb6227d2fd27e07d78ec994c61 |
| baseline-v1/results/app.trx | 83acbee801a40f0a2d6b41e2d4e6b5be9f3226f58ee32f14374c2b066601cd21 |
| working-v3/command.json | 7c905ed0657df0c1e43b51a12078394eecd358d55ea9dac6f8348e65d76c2195 |
| working-v3/results/app.trx | eff700937245c85b00a1fe42473763ecb2ea7c2b82835cd874e7fe30fddc73bf |
| clean-v4/command.json | 0e021c8df917f332955b993393a4333ec99434ccd7369d15604c78d8b25ab08f |
| clean-v4/results/app.trx | debccbcc5d434dcb52939c9e4b850c89943c4c605d1b52294581589808e46e69 |
| ComparisonProviderAdmissionTests-v1.cs | 09f48d5171921aca72574d7dd2c585c0035a085dd0941a2ba416bb3fc8355300 |
| MainViewModel.Tools-working-v3.cs | b9646004d2f25ac3c0c2f5971a880415d5cafe65d5b32a75fa4d62a019996724 |
| CompareWindow-working-v3.cs | 667bbf61fbeab5dc23a85681021e63fad9c8f80519839cfd80f5823f4a1452b0 |
| independent-comparison-observation-reader-v2.py | 90ba0d57f49700303b9d65c334b476196b7fc02773da5bbfa5432ca3551f72ba |
| run-comparison-baseline-v1.py | 56b2103b27247e9cae72efd40d5dc8ac8d850625ee3eec3185b1af007c28a2db |
| run-comparison-working-v3.py | 48756dd61b1ecd11492a5fae4778ce67dc38469cc0670c47523b2f1219fff8df |
| run-comparison-clean-v4.py | d50d4ee606e45d77709bf2c8f4770413da9069e35fb8e8c8f21296e3d50832b2 |
| seal-comparison-admission-v5.py | 9ad5cc7566f47f49d5edace11f7da58ffa7b986a2e6563a223b7f73862b0f620 |
| seal-comparison-admission-v6.py | 494e4df007dcd5313b158f444b218ac6d1deb5079af52777f0cdfd2b7987d19b |
| seal-canonical-reference-guard-v6.json | 0574829c6660b84a4b0c9ba2e4907436265a9691f4c7583f41b89b7f2f2546dd |
| owned-process-absence-v6.json | 06a020b4c1186d4ef3410c3127ef87dad70443f64531fde133b15865371597de |
| independent-comparison-admission-v6.json | d258b842f5c02b3ff919e1c765dbe6a9f86ec8b8af827991ae12b8560eaadf61 |


## Original producer CI

Independent failure audit v2 verifies nineteen selected server digests/every member, fourteen complete TRX inventories, four compiler receipts, 92 locked restore graphs and all seven CI seals. Each App lane contains 651 cases: Windows 633 pass/17 explicit skips/one failure; ARM64 634 pass/17 skips; Ubuntu 565 pass/86 skips; macOS 567 pass/84 skips. All 36 comparison admission/ownership/known-byte observations are independently reconstructed. Producer policy passes; package/draft jobs are skipped. Hosted component evidence does not qualify a candidate.

The first failure collector sealed six records before its Windows-path guard failed. A fresh continuation corrects that reader guard and rechecks the one adverse outcome and all comparison observations; source, raw results, artifacts and CI are unchanged. The failed reader, guard and unexecuted prepared success readers remain. A document-writer preflight used the wrong separation-record filename and stopped before editing any public file; the continuation selects all eight actual records.

Private `FileCatReleaseEvidence/ci-37659496946-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 0286b431ae9cc7df59315151875cb80accfae07df46ffe8d1f01b9acc785c784 |
| independent-draft-guard-ci-v1.json | 026044e475e53e26b8f145dd02d05ce20970dfa7cd739768fd20b69e0b5a1c26 |
| independent-fixture-ci-v1.json | a2a7cf7edb05e142ac6e471ee105bbfa25b0e74cf43b24c66504fdb72cc1cefa |
| independent-i200-ci-cases-v1.json | fa9988186af35fecffad1b09806872f37213baf875482c54d96703cd11dad5a7 |
| independent-i200-ci-failure-audit-v2.json | c84f04d3e157eef82c7dfd000015021f40f1a958d0f5192cfa3f3aa9f6143898 |
| independent-producer-policy-ci-v1.json | 699551aa7a781e6b5c72b890f22d1949b7dbaf4a1718b40a4af21bf9ce2faef4 |
| independent-restore-ci-v1.json | e66129a4ef66a13645b71120de36c43367913a88cce26c6e0c579d4960863e7e |
| independent-separation-ci-v1.json | 64cb673e35e7f135019ec6909ac7a52e20d33fc37eab82bbed1deb208813b372 |

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained path | SHA-256 |
|---|---|
| collect-i200-ci-v1.py | cc0f59e81cfb4581b4d25969a726a00ded28f3a6f5f39e87246f8e8de15d387a |
| verify-i200-ci-v1.py | a52bd8f5fffbd4e87f61ba2767342db1709fd68ecb8a4fcc06edb91f4d5910ef |
| collect-i200-ci-failure-v2.py | 41e7f6e402b00a7b149f658c9377e2d59925ef6b1c127f8341b6ab502970c6eb |
| continue-i200-failure-cases-v3.py | 7ed847fe725cc1df0612e3dbe54ac4866a536f444bc6cc331bd698b32d668140 |
| verify-i200-ci-failure-v2.py | dc747a6870ccaaac1688dee7cb2b799f902dfdc9f4d61017cbe448553c118911 |

Private `FileCatReleaseEvidence/co200-v1`:

| Retained path | SHA-256 |
|---|---|
| ci-reader-path-guard-v12.json | 948eed06e06763fcb7cf87056e12706c6b5f35abcbf4f68e768177fb5d86a5ae |

No native desktop/input, actual remote/archive device, human, physical/reference or candidate qualification is claimed. Physical-source/USB hold, contract freeze, candidate formation and explicit human GO/publication gates remain. No persistent machine setup or physical source changed.
