# E-I336 — complete native Core inventories and retained SMB failure

2026-10-11 CEST. Exact **7a596160220899114a7715639f8156a384dfe8c4 /1541 canonical raw Git blobs**, no overlays. Complete Core runs use **no class filter**, with FILECAT test switches cleared; separately targeted controls keep their own inventories. These runs do not qualify the hosted instrumented server/device/environment cases.

| Native platform | Pass | Explicit skip | Fail |
|---|---:|---:|---:|
| Windows guest, high RID 12288/session 0 | 3695 | 76 | 1 |
| Ubuntu UID 1000 | 3679 | 88 | 0 |
| Mac UID 501 | 3675 | 92 | 0 |
| Total | 11049 | 256 | 1 |

Every previously targeted outcome/message/skip is included. All 48 selected-source controls pass on each platform. The complete Windows run **fails** in `SmbToolPipeExchangeTests.User_cancellation_still_ends_an_owned_child`: no input-ready fence within 25 seconds. Its owned child records `started` and `before-prefix-write`; no later phase is recorded. Pending work and the exact child finish during cancellation cleanup, and the fixture is removed. The trace does not establish why progress stopped, a product defect or the cause of any older hosted failure.

A separate unchanged exact Windows payload runs all eleven SMB pipe controls **twice: 22 passes**. Complete known input/output hashes, adverse deadline, output cap, both cancellation cases and exact child/fixture exit verify. Both isolated repeats retain their actual identity; they do not revise the failed full run or establish a fix. This follow-up remains associated with the [I323 fixture/CI scope](E-I323-smb-cancellation-startup.md).

Independent postchecks verify907 native files after the complete runs and279 after the Windows repeats, with no owned process/temp root; payload/runtime hashes and stopped listeners verify. No package, persistent setting, workstation UI or physical source changes. The complete native batch remains **adverse**, independently of the passing [targeted I336 qualification](E-I336-native-qualification.md), original hosted CI and candidate scopes. Physical-source HOLD and human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact source, commands, original failures/skips, whole bytes and independent restoration.

| File | SHA256 |
|---|---|
| `i336-full-native-qualification-20261011-v1/independent-adverse-full-native-v1.json` | `44e62709f2015769d0d07fb2eeed480195ece9653bb679554e2362f3d3897b8b` |
| `i336-full-native-qualification-20261011-v1/retained-tool-sources-v1.json` | `063a0f0f0ff0a0c50465417666bd7822db78e77cd6ee9c92330e4c1608802809` |
| `i336-smb-repeat-20261011-v1/independent-repeat-final-v1.json` | `d43542aaaa0d7d1955e57e3d4c3cb29ce7c40b2ca7993c0f2cec0cd343bbcd02` |
