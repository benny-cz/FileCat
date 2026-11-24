# E-REL-NATIVE-20261124 — Native Mac, Ubuntu and Windows runs of the `v1.0.0-rc.1` source

2026-11-24 (UTC). **Source validation of the first candidate tag; not final qualification, which needs the candidate's installed bytes.** The full regression lanes ran natively on the three test machines against the exact source of `v1.0.0-rc.1`, while its tagged pipeline ran ([rc.1](E-RC1-TAG-20261124.md)). They are the lanes of the [23 November rounds](E-REL-NATIVE-20261123-release-tree.md). The owner's standing instruction is to keep every machine busy.

## Source and tools

- **Packet:** `git archive` of `8f1223654063be3a6ed21a364c3e6209f8467d7a`, the commit the tag peels to. SHA-256 `f5e23fa92dedd42db3d44c0fde3b1c27936f6e64e6d48dee03a3a28cb9b19932`, 89.5 MB.
- **Product change since the last full round** (the final tree `2d07b72b`): only the [I633](E-I633-interrupted-list-focus-choice.md) fix. The rest is test waits and I633's tests.
- **Runners:** the repository's `run_unix_test_packet.sh` (SHA-256 prefix `98d43e33`) and `run_windows_test_packet.ps1` (`13446ed5`), unchanged at this source. On Windows, `invoke-win-rel.ps1` started the runner with SDK 10.0.101.

## Lanes

- **Mac** (M1, macOS 27.2.1, 8 processors) and **Ubuntu** (24.04.5, kernel 7.0, 12 processors), SDK 10.0.101:
  - Core in two shards, App, Rendering and Remote, all at once (`parallel:on`).
  - The published app and worker probe, the crash harness, `FILECAT_EDIT_NATIVE_REQUIRED=1` and a 60-minute hang guard.
- **Windows 11 VM:**
  - Lanes one after another: App; Core in two shards (`ChangeMonitorCadenceTests` apart); the Windows platform tests in two shards (`ArchiveAppContainerTests` apart); Remote; Rendering.
  - Native containment and TxF, the published app and worker probe (`FILECAT_ARCHIVE_APP_ASSETS`, `FILECAT_ARCHIVE_PROBE_ASSETS`) and `FILECAT_EDIT_NATIVE_REQUIRED=1`.

## Results

| Machine | Run | Tests | Passed | Skipped | Failed |
|---|---|---|---|---|---|
| Mac | 07:52Z to 08:21Z | 20,202 | 19,145 | 1,057 | 0 |
| Ubuntu | 07:52Z to 08:32Z | 20,202 | 19,210 | 991 | 1 |
| Windows | from 07:53Z | still running when this record was first written | | | |

**Ubuntu, 1:** `SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret`: "The desktop keyring could not save the secret: Cannot create an item in a locked collection".
- The VM's login keyring reads `Locked=true`. The VM has been up since 2026-11-22T11:50, and a locked keyring after a boot is the state the 23 November round recorded.
- The keyring was not unlocked: that would mean entering the account's password.

## Evidence

Sealed run archives, on the development host under `E:\FileCat\.tmp\release-native-rc1\evidence` and on each machine. SHA-256 prefixes:

| Run | Archive | SHA-256 |
|---|---|---|
| `full-rc1-8f122365-mac` | `.tar.gz` | `dbe7c3ba802ba51d` |
| `full-rc1-8f122365-ubuntu` | `.tar.gz` | `50a3ca41f1d0342c` |

## Not shown

- The installed candidate's bytes. These runs build the source; final qualification runs on the candidate's packages.
