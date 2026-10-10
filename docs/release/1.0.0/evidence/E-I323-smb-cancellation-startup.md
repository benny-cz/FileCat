# E-I323 — cancellation fixture spends its readiness fence on interpreter startup

2026-10-10 CEST. **Medium validation reliability; remediated preliminarily.** The original 1b2229c ARM64 CI cancellation case fails before the owned child reaches its input fence. That test allowed four seconds for the fence and five seconds for the entire cancellation exchange, including interpreter startup. The exact historical cause is unproven.

Unchanged **156b723 product**, plus test-only controlled startup/diagnostic instrumentation, reproduces **one input-fence failure and ten healthy controls** on Windows, Ubuntu UID1000 and Mac UID501. A six-second owned child startup cannot reach the four-second fence. The pending exchange is cancelled/awaited before fixture removal; native checks find no owned Python/test process or temporary fixture. This establishes the fixture's startup sensitivity, not the historical runner timing or a product cancellation defect.

The cancellation fixture now gives the input fence 25 seconds within a 30-second exchange budget, then independently requires cancellation completion in **under five seconds after the cancellation request**. A permanent delayed-start control keeps this distinction executable. The original two-second adverse deadline, output-cap/known-byte assertions, no-watchdog-exit assertion and exact owned-child exit remain. Final cleanup awaits the pending operation and asserts completion before removing its fixture. Production SmbTools cancellation, timeouts and pipe handling are unchanged.

All **eleven corrected controls pass per platform**, including ordinary/delayed cancellation, the adverse deadline, output cap, 1 MiB input and concurrent stdout/stderr exchange. All **93 affected tests pass**, with no skips; every original ten-control and affected outcome/message remains. Independent readers verify all **1481 canonical raw blobs**, the declared single test overlay, known input/output hashes, **172 staged/920 reused runtime checks**, all Mac fixture roots and owned Python/test/temp restoration. The intermediate fixed-v1 C# name-shadowing compile failure is retained; fixed-v2 is the qualified producer.

The combined two-test-overlay producer also passes **twelve Mac controls**, including the unchanged 64 KiB real mount replacement. Its independent staged/runtime/image/process/temp cleanup verifies. Failure diagnostics now query actual containing mount points and retain unsuccessful replies without throwing before the safety assertion. The preceding diagnostic job's rejected non-mount query is retained separately; neither it nor these healthy Mac controls establishes the original hosted classification cause.

No production boundary, assertion or skip is weakened; no UI, account, privilege, persistent system setting or source device changes. [Exact committed/native follow-up](E-I323-native-qualification.md) passes at c0677d9; its actual Mac fixture failure is retained in [I325](E-I325-mac-backing-fixture.md). Original hosted follow-up, broader CI/platform/candidate qualification and all Critical source-identity/topology gates remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i323-smb-cancellation-startup-20261010-v1/independent-remediation-v1.json` | `2ed7384cd83a5a10496ad1ab5b19dda543d02a1161e711c8b7620864a54c6499` |
| `V:/FileCat/artifacts/release-evidence/i323-smb-cancellation-startup-20261010-v1/baseline-v1/inputs.json` | `55e2e7898e6fe62aa379fd6228d537a886d9a33bdb5f4c76d511927352856e06` |
| `V:/FileCat/artifacts/release-evidence/i323-smb-cancellation-startup-20261010-v1/fixed-v2/inputs.json` | `fe7d8dca4cb3230325eec46c78d33f4131df58ab92aa6dde54d2c415b4c0b3f7` |
| `i323-smb-cancellation-startup-20261010-v1/baseline-macos-v1/transport-final-v1.json` | `2cbad75b20fde2e655929f8880c7b9dd053aa96443e4f24b120d895bdb790d49` |
| `i323-smb-cancellation-startup-20261010-v1/fixed-macos-v1/transport-final-v1.json` | `95d9b2e8087842056598b0d2a11b014af71c55ff2aa38753af027d89fdb65b8e` |
| `i323-smb-cancel-native-baseline-20261010-v1/linux/transport-final-v1.json` | `3b908d3ebe04fd08fe2971f309a4fead957499772405c9e0d96301b4f6cab6a7` |
| `i323-smb-cancel-native-fixed-20261010-v1/linux/transport-final-v1.json` | `072c0252fd4f05704423310375dddc3071fbc3396b96f995ee35479cee372d8e` |
| `i323-public-20261010-v1/retained-tool-sources-v1.json` | `27cd14c9f50ece45b90953ba7a2e2fe7a39e35b9233567a073bbc1e777f177d8` |
| `i323-smb-cancellation-startup-20261010-v1/independent-diagnostic-final-v1.json` | `25727fd30a1bfe6d31a320e4763fe175ca67c02ec87593efcc3b9d692145486a` |
