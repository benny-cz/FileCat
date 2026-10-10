# E-I313 — stop attached Unix decoder children

2026-10-10 CEST. **High worker-lifetime defect, remediated preliminarily.** The actual Unix PictureDecoder worker stopped only its root process. A controlled helper created a bounded sleeping child; both Kill and Dispose ended the parent while the child stayed running, reparented to PID 1. Dispose returned before that child stopped. Native `/proc` UID, stable start ticks, executable and before/after process states independently establish both failures. These original product observations use pinned 1da71e7 assemblies; raw Git confirms the worker's executable lines are unchanged through 4f9d500. This is a synthetic lifetime control, without a parser-exploit claim.

The one-line correction requests `Process.Kill(entireProcessTree: true)` while the Unix worker root is alive. Windows retains its job route. Two permanent tests keep a shell parent and bounded child alive, then check Kill and Dispose separately. The child has separate streams so pipe EOF cannot masquerade as cleanup. A corrected test caches its disposal task; the first test version's double-disposal failure remains in raw evidence.

Validation uses a declared **4f9d500 one-line worker/test overlay**, all 1442 canonical raw Git blobs independently rechecked. **Eleven native controls pass:** Ubuntu has two persistent-child observations and both permanent regressions; macOS has five worker/echo/permission/child/start-stop controls and both permanent regressions. All **86 affected Windows tests pass**, and the two new Unix-only tests are explicitly skipped on Windows. The old Win32 job and low-token behavior is not relabelled from this Unix batch. [Committed native follow-up](E-I313-native-child-lifetime.md) and [original hosted CI](E-CI-unix-child-lifetime.md) now qualify their own exact 42e5185 identities; the original overlay evidence remains distinct.

Independent postchecks verify **490 Linux and 198 Mac staged file hashes**, all **193 Linux and 267 Mac private runtime files**, absence of seven known Linux native process identities, all five owned Linux temporary trees and all seven fixture roots. Owned payload/controller/caffeinate processes are absent. Payload/results remain campaign evidence; no persistent guest/Mac setting, host UI, physical-source or runtime installation changes occur.

Original identity guards, a colliding temporary-path refusal, archive CRLF/raw-blob refusal, native-asset path refusal, the first Mac manifest/runtime-key refusal and the failed double-disposal test remain at their immutable producers. Fresh readers and paths preserve them; no failure is overwritten or promoted. Product source changes are limited to the one process-tree call; tests and [ADR-06](../../../adr/ADR-06-worker-isolation-shell-host.md) document the finite scope.

**I08 remains open.** This corrects an attached live-child stop path; it does not create a Unix filesystem/network/memory sandbox, lower UID, prevent child creation, or establish cleanup for a root already exited or a detached/reparented descendant. Broader authority/parser/race/lifetime policy, installed-candidate and human/platform gates remain. No owner risk acceptance, freeze, candidate or stable publication is authorized. I106/I110 physical-source HOLD and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records retain exact source, artifact, command, failure/skip, native and restoration evidence.

| File | SHA256 |
|---|---|
| `i313-unix-worker-lifetime-20261010-v3/independent-remediation-v2.json` | `13b24e0a445d74a084a96d3bd50d8d6c88659d91e9658feb15328213e22393f0` |
| `i08-unix-descendant-20261010-v3/independent-baseline-v2.json` | `455e0ec709245dbb8e6464377b139e50d16b6c62aa4092ab82b3f6154ce25d6b` |
| `V:/FileCat/artifacts/release-evidence/i313-unix-worker-lifetime-20261010-v3/overlay-v1/inputs.json` | `dc19a15e815618858b2ff0374d2f48e9ca45bf41e8a0f89d2b8c175d3c2cde46` |
| `V:/FileCat/artifacts/release-evidence/i313-unix-worker-lifetime-20261010-v3/overlay-v1/controls/command.json` | `7a63aa93d7b1246650d7899cafcdafe5c3dd99a7c65ebdaa5be996ae9e61da29` |
| `V:/FileCat/artifacts/release-evidence/i313-unix-worker-lifetime-20261010-v3/overlay-v1/affected/command.json` | `5d8e8705f1a7b505005ac319efab15b8fccdd9965314ef9b794b78e52cf9e4e9` |
| `i313-unix-worker-native-fixed-20261010-v3/linux/transport-final-v1.json` | `87ef43e5bb0a4da2a256ba176b1aa7d0652a0563f5ada7a5f8745c3ea6270407` |
| `i313-unix-worker-macos-fixed-20261010-v2/transport-final-v1.json` | `cbe09ff0bace20fcb2acc8d3200fe7b8ed12470c62061190185b656323990be3` |
| `i313-unix-worker-lifetime-postchecks-20261010-v1/linux/transport-final-v1.json` | `9173c86e1d184a2161972868817602baac353e27559e94fe89f0a9084978bc63` |
| `i313-unix-worker-lifetime-postchecks-20261010-v1/macos/transport-final-v1.json` | `ecbd4066a2290e56e3b8d3f12f07e7aa473bd7c3c2e716a03569416d11eec41c` |
| `V:/FileCat/artifacts/release-evidence/i313-unix-worker-native-fixed-20261010-v2/linux-v2/retrieved/permanent-tests.trx` | `5a6a75b93419aec0280089f37cebf818003487dd672724c1ba392311ed3adb1f` |
| `i313-unix-worker-macos-fixed-20261010-v1/native-tests-command.json` | `1fad8da150ec60bb2c339e860a1b483b55878dafee053b3088717b99ec23e032` |
| `i313-unix-worker-lifetime-20261010-v3/archive-canonical-control-v2.json` | `7c3f2c85acfe521d8b14bcd753e0c4ed2b2def9aaee29564ce81a16ac9102cbd` |
| `V:/FileCat/artifacts/release-evidence/i313-unix-worker-lifetime-20261010-v3/overlay-v1/source/tests/FileCat.App.Tests/PictureWorkerProcessLifetimeTests.cs` | `de78d43eec10f30339568bd10d270e64ae78b353b5bc22c358d96ca48e79e108` |
