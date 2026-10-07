# E-I186 — lifetime read-count controls establish their cache precondition

2026-10-07. Medium CI-fixture defect under I06/I12/V10/V11/V12; remediated preliminarily at b0bb08cbdadbf9aa0f1cc3f7c5c293ea2996ebd6. The change touches three test files and leaves product code unchanged. Final candidate qualification remains open.

Original CI 37580524539 at 9230f10 fails one macOS ViewerChecksumLifetimeTests ordinary whole-file case: expected three source calls, actual four, while full SHA-256/CRC-32, unchanged owned bytes, zero active reads, no disposal during a read and disposed/cleared cancellation ownership are correct. The test assumes a cached first page but only waits for PendingLoads to be zero; no request is guaranteed by that wait. Four calls are consistent with a cold four-page reader. Every published artifact and the raw failure remain in [I184](E-I184-hex-editor-copy-ownership.md).

Viewer checksum, viewer copy and hex-editor copy controls now read and verify the first 32 owned bytes explicitly, wait for pending loads, assert that page zero is cached, then arm the held source call. The existing three/one ordinary-call counts and exact no-later-read cancellation assertions remain intact. Each raw observation records FirstPageCachedBeforeArm. Clipboard content, exact hashes, current status, disposal and cleanup assertions remain.

All eighteen working Windows controls and the same eighteen on the ordinary-user ARM64 Mac pass without skips. Mac 27.0.1/.NET 10.0.12/UID 501 runs the identical 141-file portable payload via its standalone xUnit runner; no SDK, native GUI or authorization dialog is needed. The payload archive and all before/after bytes verify. The first controller preparer fails locally because a broad ROOT placeholder also changed DOTNET_ROOT; no controller or Mac test ran. A fresh controller succeeds. The first independent reader expects VSTest StdOut; the actual standalone TRX records JSON in TextMessages/Message and escapes display-name quotes. The failed reader/source/receipt stay intact; fresh v4 reads actual fields and preserves raw attributes. No test rerun conceals those preflight failures.

Fresh locked committed Windows build at b0bb08c passes all eighteen without skips. Independent seal SHA-256 4bac7957d2963a2b9569ccafb20131568efc75ce6d0c28b8a62d426b62646e81 verifies 32 retained files, all 1,095 original and clean raw blobs/modes/archive members, 564 Windows/Mac/clean payload references and every complete-case observation. Clean FileCat.dll SHA-256 d0fb63dc5b58975c37a14bdfbc6e4a4ffa35f2ae23ca4ee7eaa8cab042701ce8. The comparison verifies that only the three test files changed.

The owned ten-minute Mac awake helper stops and its process is confirmed absent. All nine result files are hash-compared before the owned stage is removed; restoration SHA-256 e8beb380f8e7d0c549a786e00f0435bd54f5d0de4063de3a4eaf84af59321588. No persistent Mac setting changes, VM changes or physical-source access occur.

Original push CI [37582929698](https://github.com/benny-cz/FileCat/actions/runs/37582929698), attempt 1 at b0bb08c remains pending at this local seal. These finite file/component/headless controls do not qualify native desktop/clipboard/shutdown, human UX, reference performance, physical sources or installed candidate behavior.

Private `FileCatReleaseEvidence/wc186-v1`:

| Retained path | SHA-256 |
|---|---|
| working-v2/command.json | 4f9580528c1825acf08e13e360396639bac3d92eb06e272e08274aadb29852ad |
| working-v2/results/baseline.trx | 46d05f1c852dd9add12536d207d9618da77af9f405533a7d4d3f23e46ca56b70 |
| independent-working-v4.json | a6a1fd2d466e76754a8f2c01e70076dcb309ee958abaa82fc761b8130ee87c4b |
| mac-v3/mac-command-v3.json | 7184b78de2b6ac3ea35458ba632896a0d34c01c803ffbeb9a555534456680094 |
| mac-v3/mac-results.trx | bf96744e7d99dac34f81ea8c18552fd22c3964244515ac87ee90b1e1d43b3f15 |
| mac-payload-v1.json | 7d1cc9f8a120c01936181cd4a90a29bbc3f98e72e9b90b68ff72578c8cf56d43 |
| clean-v5/command.json | 2fda035b5aa5183fdf577a8af4c0dbce01563d9ea38ae9e76793a66410600098 |
| clean-v5/results/clean.trx | 8e8d7900b116d4b4ea0014846225093e67085f2888deac5fde2970303700f67b |
| independent-warm-v6.json | 4bac7957d2963a2b9569ccafb20131568efc75ce6d0c28b8a62d426b62646e81 |
| mac-restoration-v7.json | e8beb380f8e7d0c549a786e00f0435bd54f5d0de4063de3a4eaf84af59321588 |
