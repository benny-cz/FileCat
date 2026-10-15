# E-CI-I491-I492 — hosted qualification of Windows source and worker boundaries

Two consecutive original-push runs completed successfully on the exact commits named below. The producer-policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 required jobs passed; four tag/manual-only jobs skipped by design. Neither run created a release candidate.

| Scope | Exact `main` commit and GitHub run | Official artifacts and test results | Predecessor comparison |
|---|---|---|---|
| I491 whole-disk selected disk number | `6855fd094cbf0255a340b0179c9e5bd5bca9bae9`, [38590333013](https://github.com/benny-cz/FileCat/actions/runs/38590333013) | 27 server-digest ZIPs, 2,762 members, 14 TRX; 33,505 passed, 1,237 explicitly not executed, zero failed | Against I490, 34,734 previous rows and six changed inventories. Four Windows passes and four explicit skips are the intended additions; an unrelated live NTFS `$LogFile` history case on x64 changed Passed→NotExecuted because its log record had rolled off. Its raw skip is retained. |
| I492 elevated worker-token rejection | `df32338cc1086f21cb993d1beb2bd6f3e66846f2`, [38592716501](https://github.com/benny-cz/FileCat/actions/runs/38592716501) | 27 server-digest ZIPs, 2,762 members, 14 TRX; 33,508 passed, 1,236 explicitly not executed, zero failed | Against I491, 34,742 previous rows and two changed Platform inventories. Three parameterized test names were replaced on each Windows architecture, one elevated-token case was added on each, and the live NTFS x64 case returned to Passed. No other outcome changed. |

Every official ZIP was checked against its GitHub server SHA-256 digest before its members and TRX were enumerated. The four official/comparison archives were independently rehashed after sealing:

| Archive under `V:\FileCat\artifacts\release-evidence` | `file-manifest.json` SHA-256 | Audit/comparison JSON SHA-256 |
|---|---|---|
| `ci-i491-20261015-v1` | `7f562b9b87bcf125ffbe9443cd08271c062d35bd5babaa2a5613453dfb0170cf` | `dd126fb4a71a581268a626ec7ddc7f2f6b75fbdcfa3bace806752e8bb0208f4e` |
| `ci-i491-vs-i490-20261015-v1` | `2e1495efb3063f1681cddfd0833a802a3fc9af51994e6075e7a84da4fb473d22` | `0c864226c4392edb08acf5a85803a3732faf9a5ee578a25f842b70eb7d7671e6` |
| `ci-i492-20261015-v1` | `9ed406218f8f6befcf2c9968ccb1bf73cad95ef0951fa4092f4001089071631f` | `ebd323edf2a327684f3d89abebc87c3f210ba3a24a51a3cf7d7ebfa783160898` |
| `ci-i492-vs-i491-20261015-v1` | `f6e152ee031b620619a3254bc37919c415edcca720e4c70611bfeedb509593fc` | `0d9136addb9dfef72356aa046e824c28dc3b954df8c10091d8140c4df2db78a2` |

The [same-compiled Windows VM and VHD I491 controls](E-I491-windows-whole-disk-number-identity.md) and the [elevated/medium VM I492 controls](E-I492-windows-elevated-worker-fallback.md) give the local before/after evidence. Hosted success is preliminary qualification of these exact commits. Actual source swaps, installed/default-UAC interaction, broader worker containment, final candidate and human GO remain separate.
