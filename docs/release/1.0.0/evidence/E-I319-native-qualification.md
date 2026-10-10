# E-I319-EXACT — committed Unix source-case admission and cached scans

2026-10-10 CEST. Exact **7b73e2b970a2e8bc8b36cdcec0b6d31cdd56e3fb**, **1469 canonical raw Git blobs**, no product/test overlays. All **98 affected recovery tests pass** with the same **29 exact affected skips**. Windows additionally passes **two same-source controls** and explicitly skips **four Unix-only controls**; those skips supply no Unix acceptance. Every **133 predecessor outcome/message/skip** across these comparisons remains.

The committed artifact passes all **six Ubuntu UID1000 controls**: case-distinct unchosen references cannot inherit admission or cached scans, separately chosen references open their own readers with the correct labels, and repeat/rescan preserves the same-source reader. The recording reader opens only one owned regular 32 KiB FAT12 fixture; complete bytes remain unchanged. No actual mapper/device or distinct-image content mixup is demonstrated.

All **43 staged and 193 reused private runtime files** verify before/after, and a separate later postcheck rehashes them and independently finds no owned payload process, fixture or temporary root. No account, privilege, package, system-setting, workstation UI or VM-console changes. [Original push **38068462762 attempt1**](E-CI-recovery-source-case.md) is now independently sealed: four required lanes, 30,519 passes/1007 explicit skips and all sixteen applicable new controls passing at 7b73e2b. Wider case-volume/source identity/races/lifetime, physical-source attribution and installed-candidate scopes remain; I106/I110 HOLD and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i319-qualification-20261010-v1/independent-exact-final-v1.json` | `93456afdabfd22fb0e6e352a043027e6b8929cfca67237f220683420184d6447` |
| `V:/FileCat/artifacts/release-evidence/i319-qualification-20261010-v1/exact-v1/inputs.json` | `141c15ade90e1a953834f07f8bb276dcfeb8c90791c94cf518f3da8827ef09d5` |
| `i319-qualification-20261010-v1/exact-build-tests-v1.json` | `4242e6fac8cf48b3aa5d081dd5af3def831ecb036ad2254a5f48c257957b4ad7` |
| `i319-recovery-case-native-exact-20261010-v1/linux/transport-final-v1.json` | `2d0ce386bcbabe156ea5584555c99545f43840676dc45c53c5267aa9486794af` |
| `i319-recovery-case-native-exact-20261010-v1/postcheck-linux-v2/independent-final-v1.json` | `58b4cf579c70263c541694a7bf105a20e2ade8e27ee8c9b1f3754323a8a7215d` |
| `i319-public-20261010-v1/i319-main-push-v1.json` | `16edc779d6f7c53cec74ec0506bcb5c7f10b167485498792014c9fd837f2e226` |
