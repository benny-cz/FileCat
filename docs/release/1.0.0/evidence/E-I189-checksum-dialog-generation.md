# E-I189 — checksum generation completion belongs to its current dialog

2026-10-07. Medium obsolete-error/worker-lifetime defect under I06/V12/V15; remediated preliminarily at 3b84bc1081ca2de6423715ef055e432ef6b5557b. Remaining queued-frame/revision/native/race and final-candidate scope stays open.

At original 4c86041, changing algorithms while an owned file has FileShare.None creates an actual asynchronous read error. After another algorithm finishes or the actual dialog closes, the old continuation appends “Error: The item is in use…” to the newer checksum output or detached controls. Four SHA-512/MD5 replacement/closure controls fail; two ordinary selected-algorithm controls pass. The actual complete MainWindow/OverlayDialog and real Task.Run, file open and hashing are used on an owned materialized 65,536-byte file. A delegated synchronization context holds only the actual UI continuation, leaving worker/file operations intact; the real async selection handler is fully drained before owned fixtures are removed. No held native syscall or desktop input is asserted.

Each computation now owns cancellation and a current-dialog/demand guard. Replacement cancels the prior demand; late errors, hash completion and queued progress require the current live demand. Cancellation is checked before the worker remembers a completed hash. The worker clears its own slot and disposes its source after completion; closing cancels outstanding work in finally without disposing its source beneath a read. A separate started flag prevents a completed clipboard-triggered computation from causing duplicate initial work after its slot clears. These implementation guards are reviewed; the controls specifically reproduce and verify late read-error completion, preserving existing progress/result behavior.

Working and fresh locked committed Windows builds pass all 34 affected cases without skips, retaining the exact 28 prior checksum/overlay/escape/Find/conflict names and outcomes plus six additions. Replacement keeps only the current SHA-256 line with usable Save and hidden progress; closed output remains unchanged. Ordinary SHA-512/MD5 results, current input errors and prior lifetime controls pass. Independent Python SHA-256/SHA-512/MD5 and all actual owned bytes agree; all six actual async handlers complete and fixtures clean up. No product/test/controller preflight failure occurs.

Independent seal fcd10393bdec5096a60ec3335ededea2f4e7596cab6b9cde3afeeccc87f21203 verifies 23 retained files, 423 actual payload references, all 1,100 original raw Git blobs/modes/archive members and all 1,102 clean committed members. Clean FileCat.dll SHA-256 8b132044e2451a53063be5ff4ed5bc1defed829bec09f03364d63316226d34e3. Only the dialog demand-lifetime implementation and six controls change relative to parent 1cf0273; baseline remains at its own exact producer. No Mac/VM/USB or persistent settings change. Original push CI 37593773630 is pending. The actual owned-file/headless controls do not qualify native GUI, queued native frames, synchronous I/O latency/throughput, source revisions, cache identity races, human/reference/physical-source or installed candidate behavior. No contract freeze, candidate or stable GO is claimed.

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
