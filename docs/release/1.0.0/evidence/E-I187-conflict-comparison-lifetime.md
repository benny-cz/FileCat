# E-I187 — conflict comparison completion belongs to its live dialog

2026-10-07. Medium worker/completion defect under I06/V04/V12/V13; remediated preliminarily at d04a5d7f4cecea52832156133d790f9f24df919d. Final candidate and broader lifetime/revision qualification remain open.

At original b0bb08c, pressing Compare content then Cancel, Skip or Replace within the same UI turn closes the actual conflict dialog. Its asynchronous worker later writes the identical-content result to the detached controls and reenables the button. All three adverse controls fail; four ordinary identical-content, same-size-different-content, different-size and missing-item controls pass. The owned 4 MiB materialized files and actual PortableFileOperations SHA-256 are used. A synchronization-context delegate records completion of the actual async-void click handler, leaving its worker, dialog and hashing intact. This demonstrates obsolete completion; it does not measure read cancellation latency or a held native syscall.

Each comparison now owns cancellation linked to the job. Ending the conflict dialog cancels outstanding comparison work; queued work and hashing receive the token, and cancellation is checked before hashing, between files and before return. Completion and button updates require a live dialog. The click worker clears and disposes its cancellation source after completion; closing the dialog does not dispose it underneath a synchronous read. Concurrent duplicate clicks are refused while comparison is active. Decision actions and existing content/error strings remain.

Working and fresh locked committed Windows builds pass all 28 affected cases without skips: the exact 21 prior checksum/overlay/escape/Find-comparison names and outcomes plus seven new controls. Each adverse case leaves closed controls at Comparing…/disabled after the actual click completes, with the correct CancelJob/Skip/Replace action. Ordinary results retain identical/different/size/error behavior and a usable compare button. Independent Python SHA-256 values agree, all existing owned bytes are unchanged, the missing item stays absent, and owned fixtures clean up. No test, product or controller preflight failure occurs.

Independent seal SHA-256 35ff3c8c4b77b0554f815ed0db255887420c5eeb04cdfced2e25916498e1ca68 verifies all 20 retained files, 423 actual payload references, the 1,095 original raw Git blobs/modes/archive members, and all 1,098 clean committed blobs/modes/archive members. Clean FileCat.dll SHA-256 856892c962a111ca28e88fabd87e9fa0a0d086a600582c57a6bc54f337962224. Only the dialog lifetime fix and its seven controls change in correction d04a5d7 relative to parent 01690b4b608abd0885d5da2ea7ddfced8ede8b55. The earlier baseline keeps its own source identity.

Original push CI [37586182003](https://github.com/benny-cz/FileCat/actions/runs/37586182003), attempt 1 at d04a5d7 is running. A first bounded read-only status request times out; its raw files/pins remain and fresh status v2 succeeds without a CI rerun. These finite headless dialog/owned-file controls do not qualify native GUI, held synchronous I/O throughput/latency, shutdown/job cancellation races, source revisions, human UX, reference performance, physical sources or installed candidate behavior.

Private `FileCatReleaseEvidence/cc187-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | bd445c5ab8be97010bf0bba86bfa18c3344c807fada74057e4a169c0c8154fa5 |
| baseline-v1/results/baseline.trx | 73e871347d9e2df6860b4b6fb86ace677183999ec4e9f40ca1db1bb07b1750ac |
| working-v2/command.json | 1577d546cfe8ee9218527ca3b2e3c7fc3b32832ec8215f8ffa1767d9eb8c81d0 |
| working-v2/results/baseline.trx | 3f53543edec79c297197c862e0f7297086edf17895be830741dc8e49f339e791 |
| independent-working-v3.json | 642f08cb45b9c02c4b31d89882009391120baa382fc462cd6345f8ee8438b209 |
| clean-v4/command.json | 5d059cae699d08beb1b82e386577ffbaf02a597bebe85f40b576ca4cd5c99146 |
| clean-v4/results/clean.trx | 2f3b07622f509a3e1e6e4d7de25889669f856c036d1747b6973348b015c8f305 |
| independent-conflict-v5.json | 35ff3c8c4b77b0554f815ed0db255887420c5eeb04cdfced2e25916498e1ca68 |
