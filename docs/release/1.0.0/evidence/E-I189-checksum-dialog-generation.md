# E-I189 — checksum generation completion belongs to its current dialog

2026-10-07. Medium obsolete-error/worker-lifetime defect under I06/V12/V15; remediated preliminarily at 3b84bc1081ca2de6423715ef055e432ef6b5557b. Remaining queued-frame/revision/native/race and final-candidate scope stays open.

At original 4c86041, changing algorithms while an owned file has FileShare.None creates an actual asynchronous read error. After another algorithm finishes or the actual dialog closes, the old continuation appends “Error: The item is in use…” to the newer checksum output or detached controls. Four SHA-512/MD5 replacement/closure controls fail; two ordinary selected-algorithm controls pass. The actual complete MainWindow/OverlayDialog and real Task.Run, file open and hashing are used on an owned materialized 65,536-byte file. A delegated synchronization context holds only the actual UI continuation, leaving worker/file operations intact; the real async selection handler is fully drained before owned fixtures are removed. No held native syscall or desktop input is asserted.

Each computation now owns cancellation and a current-dialog/demand guard. Replacement cancels the prior demand; late errors, hash completion and queued progress require the current live demand. Cancellation is checked before the worker remembers a completed hash. The worker clears its own slot and disposes its source after completion; closing cancels outstanding work in finally without disposing its source beneath a read. A separate started flag prevents a completed clipboard-triggered computation from causing duplicate initial work after its slot clears. These implementation guards are reviewed; the controls specifically reproduce and verify late read-error completion, preserving existing progress/result behavior.

Working and fresh locked committed Windows builds pass all 34 affected cases without skips, retaining the exact 28 prior checksum/overlay/escape/Find/conflict names and outcomes plus six additions. Replacement keeps only the current SHA-256 line with usable Save and hidden progress; closed output remains unchanged. Ordinary SHA-512/MD5 results, current input errors and prior lifetime controls pass. Independent Python SHA-256/SHA-512/MD5 and all actual owned bytes agree; all six actual async handlers complete and fixtures clean up. No product/test/controller preflight failure occurs.

Independent seal fcd10393bdec5096a60ec3335ededea2f4e7596cab6b9cde3afeeccc87f21203 verifies 23 retained files, 423 actual payload references, all 1,100 original raw Git blobs/modes/archive members and all 1,102 clean committed members. Clean FileCat.dll SHA-256 8b132044e2451a53063be5ff4ed5bc1defed829bec09f03364d63316226d34e3. Only the dialog demand-lifetime implementation and six controls change relative to parent 1cf0273; baseline remains at its own exact producer. No Mac/VM/USB or persistent settings change. Original push CI [37593773630](https://github.com/benny-cz/FileCat/actions/runs/37593773630), attempt 1 at 3b84bc1, is sealed green on policy/all four required lanes. Full App inventories retain all prior 559 names/outcomes/skips plus six, giving 565; all 24 new distinct executions pass without skips and retain exact current/closed output, hashes, byte counts and completed handlers. Core retains 898 Windows/893 Unix names/outcomes/skips, including the existing explicit Windows Unix-filename skips; Remote/Platform retain their earlier outcomes. Nineteen selected server digests/every member, fourteen raw inventories, four compiler receipts and 92 locked graphs reconcile. Native installed-candidate qualification remains open. The actual owned-file/headless controls do not qualify native GUI, queued native frames, synchronous I/O latency/throughput, source revisions, cache identity races, human/reference/physical-source or installed candidate behavior. No contract freeze, candidate or stable GO is claimed.

Private `FileCatReleaseEvidence/cd189-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 8d2ea08e2c4fd6619c0074ce91f86d043458f312dab0d99524517fb004de8600 |
| baseline-v1/results/baseline.trx | 6fd8c4a3d79f06af782a3eac70bb1e7a0a669005a79d6dc5a0a7928cedc64ebf |
| working-v2/command.json | f520d12184c8023de6e48ef5bf68a458af0eb088c0f5be22297685ffc25d7357 |
| working-v2/results/baseline.trx | 5e060eaf4151d928533865c49ff5a085017972e7014cdb1a692e94d658f50c72 |
| independent-working-v3.json | 8ff8f68be659afcad0499de75c1691ba4c20d266db95cbc19fd962860e68efec |
| clean-v4/command.json | 5b369bfa53987c228d674010ca7735b8e798d060a5d8899c9064a7ebacda4da9 |
| clean-v4/results/clean.trx | a08236f55334c781237d457c61e50f867783cca283c1888ac5237ef90b9a045c |
| independent-checksum-dialog-v5.json | fcd10393bdec5096a60ec3335ededea2f4e7596cab6b9cde3afeeccc87f21203 |

The original collector succeeds and preserves all 33 proofs. Independent reader v2 verifies saved artifacts/graphs/cases, then retains a staged count guard copied from before the last proof writer (32 versus 33 existing proofs). Its exact source/assertion remain in verifier-staged-count-failure-v3.json. Fresh v3 checks the current stage without requests, tests, builds or CI reruns. Independent CI seal SHA-256 34f7d55b50609b484eb9213ca6168a618da7b33a2d394fab3cc3cd901b65d5ca verifies all 33 proof pins and complete raw data; the helper failure is separate from the passing product CI.

Private `FileCatReleaseEvidence/ci-37593773630-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 5611acf464a21ef939aaa43fe58d48105ed8a0050057f4210010353ca41629be |
| independent-draft-guard-ci-v1.json | f443781d8d70db631dc58534b95a0d95862203f614b9aeee72335a33dd9da918 |
| independent-fixture-ci-v1.json | 70e6144cb33b45151a8ae50365726e1c9bb5d83f5e72bbd54c35c9af5689274d |
| independent-i163-ci-cases-v1.json | c3a6a54088b4075a07116db2f369fe1737c4a6f9d28fd0fb0dcbb0b3e8c31927 |
| independent-i164-ci-cases-v1.json | bd4bada200a220aa1bd736a4ff3652339a30fa80f4f40ce22e642dc794d456b4 |
| independent-i165-ci-cases-v1.json | 969f3e3610f5d5aa7cd627707a50563c4b249dbd07f4761c4fc2236745c5aeb6 |
| independent-i166-ci-cases-v1.json | 8db3859ceb0e42b15c23cfe4a4a20b99d0cc99eaa9a4b18023f7b0b2a118744c |
| independent-i167-ci-cases-v1.json | 41dba9850a2119636d0c10951b641990357a5b00fe181ba8a90a2024776851fb |
| independent-i168-ci-cases-v1.json | fc16776776226359c06706ed4979ade184343bf9380009eb66dfac4d768cb522 |
| independent-i169-ci-cases-v1.json | 2b13da5bb2100295ad044adf29751fb82c109a3b7731721db2b28b247fd0005c |
| independent-i170-ci-cases-v1.json | a085da6b6d9417298c75e3b40a80d32f6e7a7ee7756c9186553061b8369c8b1c |
| independent-i171-ci-cases-v1.json | 039aca5fd0005368bbb39455a6467aee43abfa1bf5d49535df0f7c8895541064 |
| independent-i172-ci-cases-v1.json | f771c91dd40dc6a704b9551c0c4ddd5c6bc01cfd7dbee6f6e9a6f2dbdfb6e563 |
| independent-i173-ci-cases-v1.json | 1f15f5255277cf07a63a30d15130b887aba5d73104960c4e5aca76248033c973 |
| independent-i174-ci-cases-v1.json | ac4ed898647733fef17d98bcdeeff062b9f5c8580749743fd749e79a71bee753 |
| independent-i175-ci-cases-v1.json | 143e51f31d08e07f4afe41dbb5b509ccc6f972ad0c8263e57d0442f92b088355 |
| independent-i176-ci-cases-v1.json | c84400e0b855145f5adb4a4d3b87c1e9fd296e6d262a2d7c6f1d2b2c9d7c725c |
| independent-i177-ci-cases-v1.json | 1201e338d43a52c2804f6499d3a6d7b4a4258267008ae30e70cf25a05b430774 |
| independent-i178-ci-cases-v1.json | 263932dd0d6dc3682f6cec0f8d6516dfaae3896e16c058540563de413cb018d7 |
| independent-i179-ci-cases-v1.json | d2b7f9149f4c0278b4ad327009a557d9acc46ec02e29cdcd7bdead7a5b1bc74f |
| independent-i180-ci-cases-v1.json | 8500c8a6f0613d89bee7802cd8e3dca63ab531c7d4f2852f4acf118707ffd946 |
| independent-i181-ci-cases-v1.json | 9ac07d764a43017469c89598b39bc887d662c493f5a43a46a3bac5d6befccea7 |
| independent-i182-ci-cases-v1.json | df4f28e6a96957a111ea7c7ffaf38a6c19aefe3e0f732586de288f3dbe8dd57b |
| independent-i183-ci-cases-v1.json | 0f44738dcacbc301bd15b2fc1fde57b5051baa40397ebe52d12df6504227029a |
| independent-i184-ci-cases-v1.json | 5d8c64eedf12f7f7d66b5527d5d28541c0a84afa24e2746bc7451ce1e98607b3 |
| independent-i185-ci-cases-v1.json | 674211519cc4cf66b68e6a446b65d46d7d2e9e9f4a7f5d3e015de9f52e8caa6d |
| independent-i186-ci-cases-v1.json | e47f64c150639f798a813fc0d7ac53999973a9b1e13d533b00f9b743649dc2f5 |
| independent-i187-ci-cases-v1.json | 718f72c0bdf2f8c0161727b50996c965eb38468a85ce23d17740cbc24081896a |
| independent-i188-ci-cases-v1.json | 867ae1f9d283b52baacb3eb8b57e5dee547d299b586ade36f5891c64ca11dd5d |
| independent-i189-ci-audit-v3.json | 34f7d55b50609b484eb9213ca6168a618da7b33a2d394fab3cc3cd901b65d5ca |
| independent-i189-ci-cases-v1.json | 732d12c6339add80dddf4a1a0d21e57e234bec44a138952368ce13bebb9783cf |
| independent-producer-policy-ci-v1.json | 2ebe78b300eda1b611f863a594562c7a2e7e10d83258b6ca79dd6723c3192f01 |
| independent-restore-ci-v1.json | 0937cd90289ef6bbf900fb8de4919858ff1d941d9013a87054c1b4ed6f4d197c |
| independent-separation-ci-v1.json | a63e062f40bd52f3157bdced3e2edfe35b970b03d601602360d1ef00f032d21a |
| verifier-staged-count-failure-v3.json | f7fe4dd37d26270579ec05af83c45c492a8a1ac448a31dbe71e4d54797bb88cd |
