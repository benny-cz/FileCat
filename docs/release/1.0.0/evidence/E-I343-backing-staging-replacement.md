# E-I343 — protect source containers reached through changed staging paths

2026-10-11 CEST. Baseline **1b1369702c37e5837cc6a37aca97acc21f6c4e30 /1564 canonical raw Git blobs**, declared product/test overlays. After FileCat creates a staging file, source disposal or a failed publication can rename a selected backing source image into that pathname. Creation ownership alone then authorizes cleanup or publication of the replacement. Actual owned-file controls reproduce **four complete source-image deletions and two publications of source-image bytes as extracted output** per producer. Each baseline retains six adverse failures/six healthy passes.

The fix checks the staging pathname against all retained/current backing paths and identities before verification, after source closure and inside every publication attempt. Failed cleanup also checks backing identity before deleting, without requiring a cancellation checkpoint to succeed. A matching or refused identity query preserves the path. Complete source bytes remain at the controlled renamed path; no target is published and failed-copy byte progress rolls back. Healthy extraction and cleanup of the copy's own partial file remain covered.

Final identical safety assertions pass **24 local and 48 native controls**. The Core Windows portable route explicitly records identity answers; Ubuntu/Mac use actual native device/inode identities, and a separate Windows adapter route uses actual volume/file IDs. Every native-identity control requires a non-empty original identity. The six changed routes cover normal source close, I/O/unexpected close failure, verification failure, publication skip and publication retry; six unchanged routes preserve whole original source and extracted target bytes. Complete bytes, source closure, final state, publication counts, exact staging presence and progress independently verify.

Every **4060 final predecessor outcome/message and 109 exact skips** remains: 837 Core/fourteen Windows records locally, 1065 Core records on each native platform and fourteen native Windows-adapter records. Fresh postchecks verify **1279 native files**, absent owned processes/temporary roots and stopped listeners. Earlier recorded-only v1 producers retain their twelve local/36 native controls, 4032 predecessors/107 skips and 1161 native checks separately. Both host campaigns retain complete SDK logs and verify reconstructable fixture images before retiring their owned roots.

One final Windows postcheck initially refuses the second adapter root because its allowlist includes only the first prefix; 279 checked files and its exit-one result remain retained. A fresh postcheck accepts the two exact owned producer prefixes and independently checks all 483 Windows files without rerunning product tests. The first reader refuses that adverse postcheck; fresh v2 repeats the complete audit against the new result. Original failures are unchanged.

Native tokens are Windows high RID 12288/session 0, Ubuntu UID 1000 and Mac UID 501 with pinned .NET 10.0.12. No host UI, physical device, package or persistent setting changes. The control intentionally renames owned source files and checks their new location; it does not claim that an external rename preserves the old pathname. Checks are separate from filesystem mutation and do not close atomic races, unavailable identities, all unrelated staging replacements or physical-source safety. Exact committed/hosted/installed-candidate follow-up, physical-source HOLD, owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, whole bytes and restoration.

| File | SHA256 |
|---|---|
| `backing-staging-safety-20261011-v1/independent-remediation-v2.json` | `0a9815047c37efd5645d0bcde8aa426ea31cb5c22d010fe96e3a47b93a286f25` |
| `backing-staging-safety-20261011-v1/retained-tool-sources-v1.json` | `a9e9e2abd36066d2d9b171834474b8ec399ae436dd8f49634ab65adfa2b7de45` |

[Exact committed/native qualification](E-I343-native-qualification.md) now passes at fd851d1/no overlays. The original hosted Mac alias-observation failure and its separately reproduced fixture correction remain in [I345](E-I345-native-identity-observation.md).
