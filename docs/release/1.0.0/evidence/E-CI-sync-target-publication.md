# Original CI — synchronization target publication, with one native gap

Original [run 37957430524](https://github.com/benny-cz/FileCat/actions/runs/37957430524), **attempt 1 at cb52c13ffd062bc59fefbac17313bdbdce319dc7**, succeeds in all four required lanes. Independent inspection retains **27,972 actual records and 512 new I280 passes**, 21 server-digest ZIPs, 14 actual TRXs, 92 restored graphs and four SDK 10.0.401 toolchain/asset receipts from 1330 canonical source blobs.

**One native Windows x64 outcome changes from Passed to NotExecuted.** WindowsFileRecordsTests.As_administrator_the_live_log_shows_creation_and_a_time_change_while_history_is_retained sees creation/name but does not observe the before/after time change within its retry. Its full skip text, NTFS records, times, actual TRX and older Passed result remain. The time change is **not validated** in this run. All **27,459 other predecessor outcome/message multiplicities and every older exact skip remain**. This is a native qualification gap with cause unestablished, not evidence that the synchronization correction caused a regression.

The original collector exits 1 at its strict predecessor-outcome assertion after retaining all original archives. The versioned collector explicitly records the one transition and reuses successful cached inputs; no CI retry or redownload is needed. The first qualified reader then exits because a preparation replacement made its original-exit assertion reference the newer successful collector; reader v3 restores the correct original/new receipts. Both failures remain. No unqualified v1 independent final exists.

The same source-specific pool/pipe/connect/open/comparison/edit/publication observations remain checked. [The next original run](E-CI-sync-link-publication.md) observes this native case Passed; it does not retroactively validate this run's skipped time change. These are preliminary hosted checks, not final-candidate/native-desktop qualification or release GO.


## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute; nested receipts retain source, raw commands, payloads, failures and cleanup.

| File | SHA256 |
|---|---|
| `sync-target280-ci-v1/preparation-v1.json` | `489ba04419a8ef01674fafef2e03c4079a57035378187d99b89a3cfc2316c860` |
| `sync-target280-ci-v1/collector-v1-command.json` | `9d1c59df58983f1e581056a2ac0e481df005ab692d6889d6c13c9fe28a62951e` |
| `sync-target280-ci-v1/collector-refusal-preparation-v2.json` | `9ad25ca42b406aa254c332a402b1aa67ccb6fd2ac6f369af73dfbe6d79f461be` |
| `sync-target280-ci-v1/qualified-native-transition-v1.json` | `dde51fafc3e354ef1f7beba2a63972b303e9700431d1d5c530f972ce0b42a3aa` |
| `sync-target280-ci-v1/collector-v2-command.json` | `00eb848372dedc5f69109aebb258f78f0b15e382ebff569dca8eab8a308c4ff6` |
| `sync-target280-ci-v1/reader-refusal-preparation-v3.json` | `96db26cf7e7c40b3010f71a453561e974a78371ae862370697169237280d0bae` |
| `sync-target280-ci-v1/independent-sync-target-ci-final-v3.json` | `7784b14a0b398e5ae133ef0dd0e8022f709a884ca8fc871859f94ffac4908677` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-ci-v1/assets-attempt1-v1/independent-assets-ci.json` | `3541261b3d09a8c61bf334750e9009d26a02bc8cc68041556004d122a7fd9b86` |
| `E:/FileCat/artifacts/release-evidence/sync-target280-ci-v1/assets-attempt1-v1/independent-restore-ci-v1.json` | `763d40db647cc916c506d8b33bf3720ea00cb557b131b9bfb01524a267f0073d` |
