# E-I335 — recheck recovery destination at publication

2026-10-11 CEST. Baseline **10a8ced781f6cc263947922a880d648fe090c060 /1535 canonical raw Git blobs**. Identical test assertions and separately pinned baseline/product overlays are retained. This High/Critical destination-admission gap belongs to the open I106 deleted-data safety boundary. No device or physical source is opened.

The executor checks the destination disk before creating staging, but content reading, disposal, read-back verification and publication error decisions can subsequently change its admission answer. The final rename only rechecks backing image/container identities. Recorded RecoveryProvider topology answers changing to same-disk or unknown therefore still produce a completed output: **24 adverse controls**, covering six transition points and both new/existing targets. Twelve separate-disk controls remain healthy. These are real owned filesystem staging/rename operations with controlled topology answers, not actual kernel mount replacement or a physical-source-write observation.

The publication retry action now asks the source provider to admit the actual destination folder again immediately before its backing-target check and Move. A same-disk or unknown answer refuses every attempt, including automatic sharing-error and explicit user retries. Failed publication retires copied progress and its staged file through the existing cleanup path. Complete previous target bytes remain intact, or the new target remains absent. Separate-disk publication and retries stay healthy.

All **36 final local controls and 108 fixed native controls** pass: Windows guest high token RID 12288/session 0, Ubuntu UID 1000 and Mac UID 501, each on pinned private .NET 10.0.12. All **3528 affected predecessor outcomes/messages and 107 exact skips** are preserved: 694 local passes/17 skips; 921 Windows passes/18 skips; Ubuntu and Mac each 903 passes/36 skips. Native class selection is wider than local method selection; each compares its own full baseline/fixed inventory. Independent readers inspect every complete incoming/previous/final byte vector, state/progress, publication attempt, content open/close and refusal. Fresh native postchecks verify **1161 files** and absence of owned test processes/temp roots. Controllers rehash and retire only seven known duplicate input images per run. Windows payload listeners stop; no firewall, package, persistent setting or workstation UI changes.

The first local predecessor selector accidentally includes the new failing controls and excludes seven older retry controls. Its original 24-failure inventories remain adverse; a fresh v2 selector separates the 36 new controls and includes all older retry/backing-source controls. No test assertion or product safety condition changes between baseline and fixed. All three native baseline producers reproduce 24 failures/twelve positives with the same final fixture.

Exact committed/hosted and installed-candidate repeats remain separate. A check followed by a rename is not atomic; directory/mount replacement between checks, wider multi-source/provider races, cleanup writes after topology changes, kernel identity transitions and the I106/I110 physical-source HOLD remain unqualified. No owner risk acceptance, freeze, candidate or stable publication is implied.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact source, commands, original failures/skips, complete output bytes and independent restoration.

| File | SHA256 |
|---|---|
| `i335-recovery-publication-safety-20261011-v1/independent-remediation-v1.json` | `f12d02c946bbb4d62349a06844564a66e9887934c23852d4b3a010d5219b9319` |
| `i335-recovery-publication-safety-20261011-v1/retained-tool-sources-v1.json` | `30c3c2c30b1eb0773cccf37b5a21e3c46477de9b94efcd5775ab8041c93d0792` |
